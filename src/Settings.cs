using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace StanniUpdater
{
    /// <summary>What happens with an update of a mod from someone else.</summary>
    internal enum ThirdPartyMode
    {
        Off,
        Notify,
        Update,
    }

    /// <summary>
    /// Settings read from BepInEx/config/stanni.updater.cfg. The mods are found automatically,
    /// the file only turns the updater off or changes how careful it is.
    /// </summary>
    internal sealed class Settings
    {
        public bool Enabled = true;
        public int CheckIntervalHours = 6;
        public int TimeoutSeconds = 6;
        public bool RequireChecksum = true;
        public ThirdPartyMode ThirdParty = ThirdPartyMode.Notify;
        public List<string> TrustedOwners = new List<string> { "Stanni97" };
        public List<string> Disabled = new List<string>();

        private const string Header =
            "# StanniUpdater: keeps the BepInEx mods up to date when Valheim starts.\n" +
            "# Mods are found automatically: by the update source built into the DLL (the mods by Stanni97)\n" +
            "# and by the manifest.json of mods installed from Thunderstore.\n" +
            "#\n" +
            "# Enabled            = false turns the updater off completely.\n" +
            "# CheckIntervalHours = how often the servers are asked at most (0 = at every start).\n" +
            "# TimeoutSeconds     = how long one request may take before it is given up.\n" +
            "# TrustedOwners      = GitHub users / Thunderstore teams whose mods are updated without asking, comma separated.\n" +
            "# ThirdParty         = what happens with updates of every other mod:\n" +
            "#                      Off = ignore them, Notify = only write them to the log, Update = install them.\n" +
            "#                      Careful with Update in multiplayer: most mods need the same version on client and server.\n" +
            "# RequireChecksum    = only install a GitHub download whose SHA-256 matches the one GitHub lists for it.\n" +
            "# Disable            = mods never to touch, comma separated: folder name, Owner/Repo or Owner-Name.\n\n";

        public static Settings Load(string path, Action<string> warn)
        {
            var settings = new Settings();
            if (!File.Exists(path))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, Header
                        + "Enabled = true\nCheckIntervalHours = 6\nTimeoutSeconds = 6\nTrustedOwners = Stanni97\n"
                        + "ThirdParty = Notify\nRequireChecksum = true\nDisable =\n");
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
                    case "trustedowners": case "allowedowners": settings.TrustedOwners = Split(value); break;
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

        public bool IsTrusted(string owner)
        {
            return TrustedOwners.Contains(owner, StringComparer.OrdinalIgnoreCase);
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
