using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BifrostUpdater
{
    /// <summary>What happens with an update of a mod from someone else.</summary>
    internal enum ThirdPartyMode
    {
        Off,
        Notify,
        Update,
    }

    /// <summary>
    /// Settings read from BepInEx/config/bifrost.updater.cfg. The mods are found automatically,
    /// the file only turns the updater off or changes how careful it is.
    /// </summary>
    internal sealed class Settings
    {
        public bool Enabled = true;
        public int CheckIntervalHours = 6;
        public int TimeoutSeconds = 6;
        public bool RequireChecksum = true;
        public ThirdPartyMode ThirdParty = ThirdPartyMode.Notify;
        public List<string> Trusted = new List<string>();
        public List<string> Disabled = new List<string>();

        /// <summary>Optional: DLL file name -> GitHub repository, for a mod that tells nothing about its source.</summary>
        public Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private const string Header =
            "# Bifrost Updater: keeps the BepInEx mods up to date when the game starts.\n" +
            "# Nothing here has to be changed. Mods are found automatically: by the update source built into the DLL,\n" +
            "# by a repository of a trusted GitHub user that matches the DLL, and by the manifest.json of Thunderstore installs.\n" +
            "#\n" +
            "# Enabled            = false turns the updater off completely.\n" +
            "# CheckIntervalHours = how often the servers are asked at most (0 = at every start).\n" +
            "# TimeoutSeconds     = how long one request may take before it is given up.\n" +
            "# Trusted            = GitHub users / Thunderstore teams (or single Owner/Repo, Owner-Name) whose mods are updated\n" +
            "#                      without asking, comma separated. Default: the owner of Bifrost Updater itself.\n" +
            "# ThirdParty         = what happens with updates of every other mod:\n" +
            "#                      Off = ignore them, Notify = only write them to the log, Update = install them.\n" +
            "#                      Careful with Update in multiplayer: most mods need the same version on client and server.\n" +
            "# RequireChecksum    = only install a GitHub download whose SHA-256 matches the one GitHub lists for it.\n" +
            "# Disable            = mods never to touch, comma separated: folder name, Owner/Repo or Owner-Name.\n" +
            "# Mod                = optional, can be repeated: Owner/Repo:File.dll tells where a mod's updates come from.\n\n";

        public static Settings Load(string path, IEnumerable<string> defaultTrusted, Action<string> warn)
        {
            var settings = new Settings();
            settings.Trusted.AddRange(defaultTrusted);
            if (!File.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, (Header
                        + "Enabled = true\nCheckIntervalHours = 6\nTimeoutSeconds = 6\nTrusted = " + string.Join(", ", settings.Trusted.ToArray()) + "\n"
                        + "ThirdParty = Notify\nRequireChecksum = true\nDisable =\n").Replace("\n", Environment.NewLine));
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
                    case "trusted": case "trustedowners": case "allowedowners": settings.Trusted = Split(value); break;
                    case "mod":
                        string[] parts = value.Split(':');
                        if (parts.Length == 2 && parts[0].Trim().Split('/').Length == 2 && parts[1].Trim().EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        {
                            settings.Sources[parts[1].Trim()] = parts[0].Trim();
                        }
                        else
                        {
                            warn("Ignoring invalid Mod line '" + value + "', expected Owner/Repo:File.dll");
                        }

                        break;
                    case "disable": settings.Disabled = Split(value); break;
                    case "thirdparty":
                        if (!Enum.TryParse(value, true, out settings.ThirdParty))
                        {
                            warn("ThirdParty = '" + value + "' is not Off, Notify or Update, using Notify.");
                            settings.ThirdParty = ThirdPartyMode.Notify;
                        }

                        break;
                }
            }

            return settings;
        }

        public bool IsTrusted(Candidate mod)
        {
            return Trusted.Contains(mod.Owner, StringComparer.OrdinalIgnoreCase) || Trusted.Contains(mod.Key, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsDisabled(params string[] names)
        {
            return names.Any(n => !string.IsNullOrEmpty(n) && Disabled.Contains(n, StringComparer.OrdinalIgnoreCase));
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
