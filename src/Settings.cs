using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StanniUpdater
{
    /// <summary>One mod that is kept up to date: a GitHub repository and the DLL its releases contain.</summary>
    internal sealed class ModEntry
    {
        public string Repo;    // Owner/Name
        public string Dll;     // file name, e.g. KongStrong.dll

        public string Owner => Repo.Split('/')[0];
    }

    /// <summary>
    /// Settings read from BepInEx/config/stanni.updater.cfg. The mods by Stanni97 are built in,
    /// the file only turns the updater off, changes timing or adds further mods.
    /// </summary>
    internal sealed class Settings
    {
        public static readonly ModEntry[] BuiltIn =
        {
            new ModEntry { Repo = "Stanni97/valheim-kong-strong", Dll = "KongStrong.dll" },
            new ModEntry { Repo = "Stanni97/verler-leben", Dll = "VerlerLeben.dll" },
            new ModEntry { Repo = "Stanni97/stanni-updater", Dll = "StanniUpdater.dll" },
        };

        public bool Enabled = true;
        public int CheckIntervalHours = 6;
        public int TimeoutSeconds = 6;
        public bool RequireChecksum = true;
        public List<string> AllowedOwners = new List<string> { "Stanni97" };
        public List<string> Disabled = new List<string>();
        public List<ModEntry> Extra = new List<ModEntry>();

        public IEnumerable<ModEntry> Mods => BuiltIn.Concat(Extra)
            .Where(m => !Disabled.Contains(m.Repo, StringComparer.OrdinalIgnoreCase));

        private const string Header =
            "# StanniUpdater: keeps the mods by Stanni97 up to date when Valheim starts.\n" +
            "#\n" +
            "# Enabled            = false turns the updater off completely.\n" +
            "# CheckIntervalHours = how often GitHub is asked at most (0 = at every start).\n" +
            "# TimeoutSeconds     = how long one request may take before it is given up.\n" +
            "# RequireChecksum    = only install a download whose SHA-256 matches the one GitHub lists for it.\n" +
            "# AllowedOwners      = only repositories of these GitHub users are ever downloaded from (comma separated).\n" +
            "# Disable            = repositories not to update, comma separated, e.g. Stanni97/verler-leben\n" +
            "# Mod                = one more mod as Owner/Repo:File.dll, can be repeated. It has to be installed already.\n" +
            "#\n" +
            "# Built in: Stanni97/valheim-kong-strong (KongStrong.dll), Stanni97/verler-leben (VerlerLeben.dll),\n" +
            "#           Stanni97/stanni-updater (StanniUpdater.dll, takes effect on the next start)\n\n";

        public static Settings Load(string path, Action<string> warn)
        {
            var settings = new Settings();
            if (!File.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, Header
                        + "Enabled = true\nCheckIntervalHours = 6\nTimeoutSeconds = 6\nRequireChecksum = true\n"
                        + "AllowedOwners = Stanni97\nDisable =\n");
                }
                catch (Exception e)
                {
                    warn("Could not write " + path + ": " + e.Message);
                }

                return settings;
            }

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (line.Length == 0 || line[0] == '#' || eq < 0)
                {
                    continue;
                }

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                switch (key.ToLowerInvariant())
                {
                    case "enabled": settings.Enabled = ParseBool(value, true); break;
                    case "checkintervalhours": settings.CheckIntervalHours = ParseInt(value, 6); break;
                    case "timeoutseconds": settings.TimeoutSeconds = ParseInt(value, 6); break;
                    case "requirechecksum": settings.RequireChecksum = ParseBool(value, true); break;
                    case "allowedowners": settings.AllowedOwners = Split(value); break;
                    case "disable": settings.Disabled = Split(value); break;
                    case "mod":
                        string[] parts = value.Split(':');
                        if (parts.Length == 2 && parts[0].Trim().Split('/').Length == 2 && parts[1].Trim().EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.Extra.Add(new ModEntry { Repo = parts[0].Trim(), Dll = parts[1].Trim() });
                        }
                        else
                        {
                            warn("Ignoring invalid Mod line '" + value + "', expected Owner/Repo:File.dll");
                        }

                        break;
                }
            }

            return settings;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            return bool.TryParse(value, out bool result) ? result : fallback;
        }

        private static int ParseInt(string value, int fallback)
        {
            return int.TryParse(value, out int result) && result >= 0 ? result : fallback;
        }

        private static List<string> Split(string value)
        {
            return value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        }
    }
}
