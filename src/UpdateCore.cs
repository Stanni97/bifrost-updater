using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BifrostUpdater
{
    /// <summary>
    /// Looks up the latest release of every installed mod and replaces its files if a newer version exists.
    /// Plain .NET plus Mono.Cecil, so it can run in the BepInEx preloader and in tests.
    /// </summary>
    internal sealed class UpdateCore
    {
        private const int MaxApiBytes = 512 * 1024;
        private const int MaxGitHubZipBytes = 25 * 1024 * 1024;
        private const int MaxStoreZipBytes = 100 * 1024 * 1024;
        private const int MaxFileBytes = 60 * 1024 * 1024;
        private const int OfflineBackoffMinutes = 10;

        // Parts of a Thunderstore package that are not part of the mod folder itself.
        private static readonly string[] OtherTargets = { "patchers/", "monomod/", "core/", "config/" };
        private static readonly string[] SkippedFiles = { "readme.md", "changelog.md", "icon.png", "license", "license.md", "license.txt" };

        // lloesche/valheim-server copies /config/bepinex/{plugins,patchers} into the game at every start
        // and leaves this file behind as the record of what it copied.
        private const string ServerImageSyncMarker = ".synced_from_config";
        private const string ServerImageConfigRoot = "/config/bepinex";

        private readonly string _bepInExRoot;
        private readonly string[] _defaultTrusted;
        private readonly bool _isServer;
        private readonly Action<string> _info;
        private readonly Action<string> _warn;
        private readonly Action<string> _debug;
        private string _mirrorRoot;

        /// <param name="defaultTrusted">Owners whose mods are updated by default, normally the owner of the updater itself.</param>
        /// <param name="isServer">Running on a dedicated server: uses ServerCheckIntervalHours.</param>
        public UpdateCore(string bepInExRoot, IEnumerable<string> defaultTrusted, bool isServer, Action<string> info, Action<string> warn, Action<string> debug)
        {
            _bepInExRoot = bepInExRoot;
            _defaultTrusted = defaultTrusted.ToArray();
            _isServer = isServer;
            _info = info;
            _warn = warn;
            _debug = debug;
        }

        private string ConfigPath => Path.Combine(_bepInExRoot, "config", "bifrost.updater.cfg");
        private string StatePath => Path.Combine(_bepInExRoot, "config", "bifrost.updater.state");

        /// <summary>The updater was called StanniUpdater before; keep its settings.</summary>
        private void MigrateOldFiles()
        {
            foreach (string suffix in new[] { "cfg", "state" })
            {
                try
                {
                    string old = Path.Combine(_bepInExRoot, "config", "stanni.updater." + suffix);
                    string fresh = Path.Combine(_bepInExRoot, "config", "bifrost.updater." + suffix);
                    if (File.Exists(old) && !File.Exists(fresh))
                    {
                        File.Move(old, fresh);
                    }
                }
                catch (Exception e)
                {
                    _debug("Could not move the old settings: " + e.Message);
                }
            }
        }

        /// <summary>Returns how many mods were updated.</summary>
        public int Run(bool force = false)
        {
            MigrateOldFiles();
            Settings settings = Settings.Load(ConfigPath, _defaultTrusted, _warn);
            if (!settings.Enabled)
            {
                _debug("Disabled in the config.");
                return 0;
            }

            _mirrorRoot = ResolveMirror(settings.MirrorTo);
            if (_isServer)
            {
                _info("Dedicated server: checking " + (settings.ServerCheckIntervalHours == 0 ? "at every start" : "every " + settings.ServerCheckIntervalHours + " h")
                      + (_mirrorRoot == null ? "." : ", updates also go to " + _mirrorRoot + "."));
            }

            if (!force && !CheckDue(settings))
            {
                _debug("Checked recently, next check later.");
                return 0;
            }

            DiscoveryResult found = Discovery.Find(_bepInExRoot, settings.Sources, _debug);
            var http = new Http(settings.TimeoutSeconds, _debug);
            try
            {
                ResolveByOwner(http, settings, found);
            }
            catch (Exception e) when (!(e is HttpStatusException))
            {
                _warn("Could not reach GitHub: " + e.Message);
                WriteState("lastfail", DateTime.UtcNow);
                return 0;
            }

            List<Candidate> mods = found.Mods.Values
                .Where(m => !settings.IsDisabled(m.Key, m.Label, Path.GetFileName(m.Folder)))
                .ToList();
            if (mods.Count == 0)
            {
                _debug("No updatable mods found.");
                return 0;
            }

            int updated = 0;
            bool anyAnswer = false;
            foreach (Candidate mod in mods)
            {
                try
                {
                    if (Check(http, settings, mod))
                    {
                        updated++;
                    }

                    anyAnswer = true;
                }
                catch (Exception e)
                {
                    bool answered = e is HttpStatusException;
                    anyAnswer |= answered;
                    // A 404 on Thunderstore only means: this mod is not published there.
                    Action<string> report = answered && mod.Kind == SourceKind.Thunderstore ? _debug : _warn;
                    report("Update check for " + mod.Key + " failed: " + e.Message);
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

        /// <summary>
        /// BepInEx plugins that name no source are matched with the repositories of the trusted GitHub users:
        /// the repository name has to equal the DLL name or contain it (ignoring case and separators), and has to be the only one.
        /// A wrong match is harmless: the release zip has to contain the very same DLL and its checksum has to fit.
        /// </summary>
        private void ResolveByOwner(Http http, Settings settings, DiscoveryResult found)
        {
            if (found.Unresolved.Count == 0)
            {
                return;
            }

            foreach (string owner in settings.Trusted.Where(t => t.Length > 0 && !t.Contains("/")))
            {
                List<object> repos;
                try
                {
                    byte[] answer = http.Get("https://api.github.com/users/" + Uri.EscapeDataString(owner) + "/repos?type=owner&per_page=100", "application/vnd.github+json", 2 * MaxApiBytes);
                    repos = Json.Parse(Encoding.UTF8.GetString(answer)) as List<object>;
                }
                catch (HttpStatusException)
                {
                    continue;   // not a GitHub user, e.g. a Thunderstore team
                }

                var names = new List<string>();
                foreach (object repo in repos ?? new List<object>())
                {
                    string name = Json.Str(repo, "name");
                    bool skip = (Json.Get(repo, "archived") as bool?) == true || (Json.Get(repo, "fork") as bool?) == true;
                    if (name != null && !skip)
                    {
                        names.Add(name);
                    }
                }

                foreach (string dll in found.Unresolved.ToList())
                {
                    string match = MatchRepo(Path.GetFileNameWithoutExtension(dll), names);
                    if (match == null)
                    {
                        continue;
                    }

                    _debug(Path.GetFileName(dll) + " matches " + owner + "/" + match + ".");
                    Discovery.AddGitHub(found.Mods, owner + "/" + match, dll);
                    found.Unresolved.Remove(dll);
                }
            }

            foreach (string dll in found.Unresolved)
            {
                _debug(Path.GetFileName(dll) + ": no update source found.");
            }
        }

        internal static string MatchRepo(string dllName, List<string> repoNames)
        {
            string target = Normalize(dllName);
            if (target.Length == 0)
            {
                return null;
            }

            string exact = repoNames.FirstOrDefault(r => Normalize(r) == target);
            if (exact != null)
            {
                return exact;
            }

            List<string> near = repoNames.Where(r =>
            {
                string n = Normalize(r);
                return (target.Length >= 5 && n.Contains(target)) || (n.Length >= 5 && target.Contains(n));
            }).ToList();
            return near.Count == 1 ? near[0] : null;
        }

        private static string Normalize(string name)
        {
            return new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        /// <summary>Returns true if the mod was updated.</summary>
        private bool Check(Http http, Settings settings, Candidate mod)
        {
            ThirdPartyMode mode = settings.IsTrusted(mod) ? ThirdPartyMode.Update : settings.ThirdParty;
            if (mode == ThirdPartyMode.Off)
            {
                _debug(mod.Key + ": not from a trusted owner and ThirdParty = Off.");
                return false;
            }

            if (mod.Installed == null)
            {
                _warn(mod.Label + ": could not read the installed version, skipping.");
                return false;
            }

            return mod.Kind == SourceKind.GitHub ? CheckGitHub(http, settings, mod, mode) : CheckThunderstore(http, mod, mode);
        }

        private bool CheckGitHub(Http http, Settings settings, Candidate mod, ThirdPartyMode mode)
        {
            byte[] answer = http.Get("https://api.github.com/repos/" + mod.Repo + "/releases/latest", "application/vnd.github+json", MaxApiBytes);
            object release = Json.Parse(Encoding.UTF8.GetString(answer));
            string tag = Json.Str(release, "tag_name");
            Version latest = ParseTag(tag);
            if (latest == null)
            {
                _warn(mod.Repo + ": release tag '" + tag + "' is not a version, skipping.");
                return false;
            }

            if (latest <= mod.Installed)
            {
                _debug(mod.Label + " " + mod.Installed + " is up to date (latest " + latest + ").");
                return false;
            }

            if (mode == ThirdPartyMode.Notify)
            {
                _info("Update available: " + mod.Label + " " + mod.Installed + " -> " + latest + " (" + mod.Repo + "). Set ThirdParty = Update to install it automatically.");
                return false;
            }

            Dictionary<string, object> asset = Json.List(release, "assets")?.OfType<Dictionary<string, object>>()
                .FirstOrDefault(a => (Json.Str(a, "name") ?? "").EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            string url = asset == null ? null : Json.Str(asset, "browser_download_url");
            if (url == null || !Http.IsAllowedUrl(url) || !url.StartsWith("https://github.com/" + mod.Repo + "/", StringComparison.OrdinalIgnoreCase))
            {
                _warn(mod.Repo + " " + tag + ": no zip inside the repository to download, skipping.");
                return false;
            }

            string digest = Json.Str(asset, "digest");
            string expected = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7).ToLowerInvariant() : null;
            if (expected == null && settings.RequireChecksum)
            {
                _warn(mod.Repo + " " + tag + ": GitHub lists no SHA-256 for the download, skipping (RequireChecksum).");
                return false;
            }

            _info("Updating " + mod.Label + " " + mod.Installed + " -> " + latest + " ...");
            byte[] zip = http.Get(url, "application/octet-stream", MaxGitHubZipBytes);
            if (expected != null && !string.Equals(Hex(Sha256(zip)), expected, StringComparison.Ordinal))
            {
                _warn(mod.Repo + " " + tag + ": checksum of the download does not match, not installing.");
                return false;
            }

            // Only the DLLs the mod already has are taken from the zip, by file name.
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                var fresh = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (string dll in mod.Dlls)
                {
                    string name = Path.GetFileName(dll);
                    // Zips made on Windows may use backslashes; Path.GetFileName only knows them on Windows.
                    ZipArchiveEntry entry = archive.Entries.FirstOrDefault(e => string.Equals(e.FullName.Replace('\\', '/').Split('/').Last(), name, StringComparison.OrdinalIgnoreCase));
                    byte[] bytes = entry == null ? null : ReadEntry(entry);
                    if (bytes == null || bytes.Length < 2 || bytes[0] != 'M' || bytes[1] != 'Z')
                    {
                        _warn(mod.Repo + " " + tag + ": " + name + " is not in the zip, not installing.");
                        return false;
                    }

                    fresh[name] = bytes;
                }

                foreach (string dll in mod.Dlls)
                {
                    ReplaceFile(dll, fresh[Path.GetFileName(dll)]);
                }
            }

            _info("Updated " + mod.Label + " to " + latest + (IsPatcher(mod.Folder)
                ? " (patchers are already loaded, the new version runs from the next start)."
                : ", already in use in this start."));
            return true;
        }

        private bool CheckThunderstore(Http http, Candidate mod, ThirdPartyMode mode)
        {
            byte[] answer = http.Get("https://thunderstore.io/api/experimental/package/" + Uri.EscapeDataString(mod.Namespace) + "/" + Uri.EscapeDataString(mod.Package) + "/",
                "application/json", MaxApiBytes);
            object package = Json.Parse(Encoding.UTF8.GetString(answer));
            object latest = Json.Obj(package, "latest");
            string fullName = Json.Str(latest, "full_name");
            string versionText = Json.Str(latest, "version_number");
            string url = Json.Str(latest, "download_url");
            if (fullName == null || !fullName.StartsWith(mod.Key + "-", StringComparison.OrdinalIgnoreCase) || !Version.TryParse(versionText, out Version version))
            {
                _debug(mod.Key + ": unexpected answer from Thunderstore, skipping.");
                return false;
            }

            if (version <= mod.Installed)
            {
                _debug(mod.Label + " " + mod.Installed + " is up to date (latest " + version + ").");
                return false;
            }

            if (mode == ThirdPartyMode.Notify)
            {
                _info("Update available: " + mod.Label + " " + mod.Installed + " -> " + version + " (Thunderstore " + mod.Key + "). Set ThirdParty = Update to install it automatically.");
                return false;
            }

            if (url == null || !Http.IsAllowedUrl(url) || !url.StartsWith("https://thunderstore.io/package/download/", StringComparison.OrdinalIgnoreCase))
            {
                _warn(mod.Key + ": no usable download address, skipping.");
                return false;
            }

            _info("Updating " + mod.Label + " " + mod.Installed + " -> " + version + " (Thunderstore) ...");
            byte[] zip = http.Get(url, "application/zip", MaxStoreZipBytes);
            InstallPackage(mod.Folder, zip);
            _info("Updated " + mod.Label + " to " + version + ". Check that the other mods and the server still match.");
            return true;
        }

        /// <summary>
        /// Writes the files of a Thunderstore package into the mod folder. Everything is checked first,
        /// so a broken zip changes nothing. Files that belong elsewhere (config, patchers, ...) are never touched.
        /// </summary>
        private void InstallPackage(string folder, byte[] zip)
        {
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var files = new List<KeyValuePair<string, byte[]>>();
            long total = 0;
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string path = entry.FullName.Replace('\\', '/');
                    if (path.EndsWith("/") || string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    if (path.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase))
                    {
                        path = path.Substring("plugins/".Length);
                    }
                    else if (OtherTargets.Any(t => path.StartsWith(t, StringComparison.OrdinalIgnoreCase)))
                    {
                        _debug("Skipping " + path + " (not part of the mod folder).");
                        continue;
                    }

                    if (SkippedFiles.Contains(path.ToLowerInvariant()))
                    {
                        continue;
                    }

                    string target = Path.GetFullPath(Path.Combine(folder, path));
                    if (path.Split('/').Contains("..") || !target.StartsWith(root, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("The package contains an unsafe path: " + entry.FullName);
                    }

                    byte[] bytes = ReadEntry(entry);
                    if (bytes == null)
                    {
                        throw new InvalidDataException("A file in the package is empty or too large: " + entry.FullName);
                    }

                    total += bytes.Length;
                    if (total > MaxStoreZipBytes * 2L)
                    {
                        throw new InvalidDataException("The package is unreasonably large.");
                    }

                    files.Add(new KeyValuePair<string, byte[]>(target, bytes));
                }
            }

            if (!files.Any(f => f.Key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("The package contains no DLL.");
            }

            // manifest.json goes last: it is what tells the next start which version is installed.
            foreach (KeyValuePair<string, byte[]> file in files.OrderBy(f => Path.GetFileName(f.Key) == "manifest.json" ? 1 : 0))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file.Key));
                ReplaceFile(file.Key, file.Value);
            }
        }

        private static byte[] ReadEntry(ZipArchiveEntry entry)
        {
            if (entry.Length == 0 || entry.Length > MaxFileBytes)
            {
                return null;
            }

            using (Stream stream = entry.Open())
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        /// <summary>
        /// The BepInEx folder that is copied over this one at every start, or null. Without writing updates there too,
        /// the copy would put the old version back at the next start.
        /// </summary>
        private string ResolveMirror(string setting)
        {
            string value = (setting ?? "").Trim();
            bool auto = value.Equals("auto", StringComparison.OrdinalIgnoreCase);
            if (value.Length == 0 || value.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (auto && !File.Exists(Path.Combine(_bepInExRoot, ServerImageSyncMarker)))
            {
                return null;
            }

            try
            {
                string full = Path.GetFullPath(auto ? ServerImageConfigRoot : value).TrimEnd(Path.DirectorySeparatorChar);
                string own = Path.GetFullPath(_bepInExRoot).TrimEnd(Path.DirectorySeparatorChar);
                if (Directory.Exists(full) && !string.Equals(full, own, StringComparison.Ordinal))
                {
                    return full;
                }

                if (!auto)
                {
                    _warn("MirrorTo = " + value + " is not a usable folder, updates are only written into the game.");
                }
            }
            catch (Exception e)
            {
                _warn("MirrorTo = " + value + ": " + e.Message);
            }

            return null;
        }

        private bool IsPatcher(string folder)
        {
            string root = Path.GetFullPath(Path.Combine(_bepInExRoot, "patchers")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return (Path.GetFullPath(folder) + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.Ordinal);
        }

        /// <summary>Replaces the file in the game and, if the mod came from the mirror folder, there as well.</summary>
        private void ReplaceFile(string path, byte[] content)
        {
            ReplaceInPlace(path, content);
            if (_mirrorRoot == null)
            {
                return;
            }

            string root = Path.GetFullPath(_bepInExRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(root, StringComparison.Ordinal))
            {
                return;
            }

            string relative = full.Substring(root.Length);
            string top = relative.Split(Path.DirectorySeparatorChar)[0];
            if (top != "plugins" && top != "patchers")
            {
                return;
            }

            string target = Path.Combine(_mirrorRoot, relative);
            if (!Directory.Exists(Path.GetDirectoryName(target)))
            {
                // The mod was not installed through the mirror folder, so nothing there would undo the update.
                _debug(relative + ": not in " + _mirrorRoot + ", nothing to mirror.");
                return;
            }

            try
            {
                string fresh = target + ".new";
                File.WriteAllBytes(fresh, content);
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(fresh, target);
                _debug("Mirrored " + relative + " to " + _mirrorRoot + ".");
            }
            catch (Exception e)
            {
                _warn("Could not write the update to " + target + ", the next start may bring back the old version: " + e.Message);
            }
        }

        /// <summary>
        /// Swaps the file by renaming: the old one becomes .old, which also works for a DLL that is loaded right now
        /// (Windows allows renaming but not overwriting a loaded file). Puts the old one back if anything fails.
        /// </summary>
        private static void ReplaceInPlace(string path, byte[] content)
        {
            string fresh = path + ".new";
            string old = path + ".old";
            bool keepOld = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);   // one step back for DLLs only
            File.WriteAllBytes(fresh, content);
            try
            {
                if (File.Exists(path) && !keepOld)
                {
                    File.Delete(path);
                }
                else if (File.Exists(path))
                {
                    if (File.Exists(old))
                    {
                        File.Delete(old);
                    }

                    File.Move(path, old);
                }

                try
                {
                    File.Move(fresh, path);
                }
                catch (Exception)
                {
                    if (File.Exists(old))
                    {
                        File.Move(old, path);
                    }

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

        /// <summary>Accepts tags like v1.2.0, 1.2 or V1.2.3.4. Anything else (nightly, test) is ignored.</summary>
        internal static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out Version version))
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

            int interval = _isServer ? settings.ServerCheckIntervalHours : settings.CheckIntervalHours;
            if (interval > 0 && state.TryGetValue("lastcheck", out string check) && long.TryParse(check, out long checkTicks)
                && now - new DateTime(checkTicks, DateTimeKind.Utc) < TimeSpan.FromHours(interval))
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
