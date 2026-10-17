using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace StanniUpdater
{
    /// <summary>
    /// Checks the latest GitHub release of every installed mod and replaces its DLL if a newer version exists.
    /// Plain .NET only, so it can run in the BepInEx preloader and in tests.
    /// </summary>
    internal sealed class UpdateCore
    {
        private const int MaxApiBytes = 512 * 1024;
        private const int MaxZipBytes = 25 * 1024 * 1024;
        private const int MaxDllBytes = 10 * 1024 * 1024;
        private const int OfflineBackoffMinutes = 30;

        private readonly string _bepInExRoot;
        private readonly string _selfDll;
        private readonly Action<string> _info;
        private readonly Action<string> _warn;
        private readonly Action<string> _debug;

        public UpdateCore(string bepInExRoot, string selfDll, Action<string> info, Action<string> warn, Action<string> debug)
        {
            _bepInExRoot = bepInExRoot;
            _selfDll = selfDll;
            _info = info;
            _warn = warn;
            _debug = debug;
        }

        private string ConfigPath => Path.Combine(_bepInExRoot, "config", "stanni.updater.cfg");
        private string StatePath => Path.Combine(_bepInExRoot, "config", "stanni.updater.state");

        /// <summary>Returns how many DLLs were replaced.</summary>
        public int Run(bool force = false)
        {
            Settings settings = Settings.Load(ConfigPath, _warn);
            if (!settings.Enabled)
            {
                _debug("Disabled in the config.");
                return 0;
            }

            // Only mods that are installed get updated, so skip the network if there is nothing to do.
            var installed = new List<KeyValuePair<ModEntry, List<string>>>();
            foreach (ModEntry mod in settings.Mods)
            {
                if (!settings.AllowedOwners.Contains(mod.Owner, StringComparer.OrdinalIgnoreCase))
                {
                    _warn("Not checking " + mod.Repo + ": its owner is not in AllowedOwners.");
                    continue;
                }

                List<string> files = FindInstalled(mod);
                if (files.Count > 0)
                {
                    installed.Add(new KeyValuePair<ModEntry, List<string>>(mod, files));
                }
            }

            if (installed.Count == 0)
            {
                _debug("None of the known mods is installed.");
                return 0;
            }

            if (!force && !CheckDue(settings))
            {
                _debug("Checked recently, next check later.");
                return 0;
            }

            var http = new Http(settings.TimeoutSeconds, _debug);
            int updated = 0;
            bool anyAnswer = false;
            foreach (KeyValuePair<ModEntry, List<string>> pair in installed)
            {
                try
                {
                    CheckResult result = Check(http, settings, pair.Key, pair.Value);
                    anyAnswer = true;
                    if (result == CheckResult.Updated)
                    {
                        updated++;
                    }
                }
                catch (Exception e)
                {
                    _warn("Update check for " + pair.Key.Repo + " failed: " + e.Message);
                    anyAnswer |= e is HttpStatusException;
                    if (!anyAnswer)
                    {
                        // Nothing answered so far: most likely offline, do not wait for every other mod as well.
                        WriteState("lastfail", DateTime.UtcNow);
                        return updated;
                    }
                }
            }

            WriteState("lastcheck", DateTime.UtcNow);
            return updated;
        }

        private enum CheckResult
        {
            UpToDate,
            Skipped,
            Updated,
        }

        private CheckResult Check(Http http, Settings settings, ModEntry mod, List<string> files)
        {
            Version installedVersion = files.Select(ReadVersion).Where(v => v != null).OrderBy(v => v).FirstOrDefault();
            if (installedVersion == null)
            {
                _warn(mod.Dll + ": could not read the installed version, skipping.");
                return CheckResult.Skipped;
            }

            byte[] answer = http.Get("https://api.github.com/repos/" + mod.Repo + "/releases/latest", "application/vnd.github+json", MaxApiBytes);
            object release = Json.Parse(Encoding.UTF8.GetString(answer));
            string tag = Json.Str(release, "tag_name");
            Version latest = ParseTag(tag);
            if (latest == null)
            {
                _warn(mod.Repo + ": release tag '" + tag + "' is not a version, skipping.");
                return CheckResult.Skipped;
            }

            if (latest <= installedVersion)
            {
                _debug(mod.Dll + " " + installedVersion + " is up to date (latest " + latest + ").");
                return CheckResult.UpToDate;
            }

            Dictionary<string, object> asset = PickAsset(release);
            if (asset == null)
            {
                _warn(mod.Repo + " " + tag + " has no zip file, skipping.");
                return CheckResult.Skipped;
            }

            string url = Json.Str(asset, "browser_download_url");
            if (!Http.IsAllowedUrl(url) || !url.StartsWith("https://github.com/" + mod.Repo + "/", StringComparison.OrdinalIgnoreCase))
            {
                _warn(mod.Repo + ": the download address of " + tag + " does not point into the repository, skipping.");
                return CheckResult.Skipped;
            }

            string digest = Json.Str(asset, "digest");
            string expected = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7).ToLowerInvariant() : null;
            if (expected == null && settings.RequireChecksum)
            {
                _warn(mod.Repo + " " + tag + ": GitHub lists no SHA-256 for the download, skipping (RequireChecksum).");
                return CheckResult.Skipped;
            }

            _info("Updating " + mod.Dll + " " + installedVersion + " -> " + latest + " ...");
            byte[] zip = http.Get(url, "application/octet-stream", MaxZipBytes);
            if (expected != null && !string.Equals(Hex(Sha256(zip)), expected, StringComparison.Ordinal))
            {
                _warn(mod.Repo + " " + tag + ": checksum of the download does not match, not installing.");
                return CheckResult.Skipped;
            }

            byte[] dll = ExtractDll(zip, mod.Dll);
            if (dll == null)
            {
                _warn(mod.Repo + " " + tag + ": " + mod.Dll + " is not in the zip, skipping.");
                return CheckResult.Skipped;
            }

            foreach (string file in files)
            {
                Replace(file, dll);
            }

            _info("Updated " + mod.Dll + " to " + latest + (string.Equals(mod.Dll, Path.GetFileName(_selfDll), StringComparison.OrdinalIgnoreCase) ? " (active from the next start)." : "."));
            return CheckResult.Updated;
        }

        private List<string> FindInstalled(ModEntry mod)
        {
            var files = new List<string>();
            if (string.Equals(mod.Dll, Path.GetFileName(_selfDll), StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(_selfDll))
                {
                    files.Add(_selfDll);
                }

                return files;
            }

            string plugins = Path.Combine(_bepInExRoot, "plugins");
            if (Directory.Exists(plugins))
            {
                files.AddRange(Directory.GetFiles(plugins, mod.Dll, SearchOption.AllDirectories));
            }

            return files;
        }

        private static Dictionary<string, object> PickAsset(object release)
        {
            List<object> assets = Json.List(release, "assets");
            if (assets == null)
            {
                return null;
            }

            return assets.OfType<Dictionary<string, object>>()
                .FirstOrDefault(a => (Json.Str(a, "name") ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        private static byte[] ExtractDll(byte[] zip, string dllName)
        {
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                // Only the DLL itself is taken, by file name. Nothing else in the zip is ever written to disk.
                ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e =>
                    string.Equals(Path.GetFileName(e.FullName), dllName, StringComparison.OrdinalIgnoreCase));
                if (entry == null || entry.Length == 0 || entry.Length > MaxDllBytes)
                {
                    return null;
                }

                using (Stream stream = entry.Open())
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    byte[] bytes = buffer.ToArray();
                    // Windows executables start with "MZ".
                    return bytes.Length > 2 && bytes[0] == 'M' && bytes[1] == 'Z' ? bytes : null;
                }
            }
        }

        /// <summary>
        /// Swaps the file by renaming: the old one becomes .old, which also works for a DLL that is loaded right now
        /// (Windows allows renaming but not overwriting a loaded file). Puts the old one back if anything fails.
        /// </summary>
        private void Replace(string path, byte[] content)
        {
            string fresh = path + ".new";
            string old = path + ".old";
            File.WriteAllBytes(fresh, content);
            try
            {
                if (File.Exists(old))
                {
                    File.Delete(old);
                }

                File.Move(path, old);
                try
                {
                    File.Move(fresh, path);
                }
                catch (Exception)
                {
                    File.Move(old, path);
                    throw;
                }
            }
            finally
            {
                if (File.Exists(fresh))
                {
                    File.Delete(fresh);
                }
            }
        }

        private static Version ReadVersion(string path)
        {
            try
            {
                Version v = AssemblyName.GetAssemblyName(path).Version;
                return v == null ? null : new Version(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build), Math.Max(0, v.Revision));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Accepts tags like v1.2.0, 1.2 or V1.2.3.4. Anything else (nightly, test) is ignored.</summary>
        internal static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                return null;
            }

            string text = tag.TrimStart('v', 'V');
            if (!Version.TryParse(text, out Version version))
            {
                return null;
            }

            return new Version(version.Major, Math.Max(0, version.Minor), Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        private bool CheckDue(Settings settings)
        {
            DateTime now = DateTime.UtcNow;
            Dictionary<string, string> state = ReadState();
            if (state.TryGetValue("lastfail", out string fail) && long.TryParse(fail, out long failTicks)
                && now - new DateTime(failTicks, DateTimeKind.Utc) < TimeSpan.FromMinutes(OfflineBackoffMinutes))
            {
                return false;
            }

            if (settings.CheckIntervalHours > 0 && state.TryGetValue("lastcheck", out string check) && long.TryParse(check, out long checkTicks)
                && now - new DateTime(checkTicks, DateTimeKind.Utc) < TimeSpan.FromHours(settings.CheckIntervalHours))
            {
                return false;
            }

            return true;
        }

        private Dictionary<string, string> ReadState()
        {
            var state = new Dictionary<string, string>();
            try
            {
                if (File.Exists(StatePath))
                {
                    foreach (string line in File.ReadAllLines(StatePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                        {
                            state[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _debug("Could not read the state file: " + e.Message);
            }

            return state;
        }

        private void WriteState(string key, DateTime value)
        {
            try
            {
                Dictionary<string, string> state = ReadState();
                state[key] = value.Ticks.ToString();
                if (key == "lastcheck")
                {
                    state.Remove("lastfail");
                }

                File.WriteAllLines(StatePath, state.Select(p => p.Key + "=" + p.Value).ToArray());
            }
            catch (Exception e)
            {
                _debug("Could not write the state file: " + e.Message);
            }
        }

        private static byte[] Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(data);
            }
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
