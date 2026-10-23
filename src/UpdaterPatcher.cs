using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace BifrostUpdater
{
    /// <summary>
    /// BepInEx preloader patcher. Patchers are initialised before BepInEx loads any plugin, so a mod updated here
    /// is already the new version in the same start. The class patches no assembly; it only uses the hook.
    /// </summary>
    public static class UpdaterPatcher
    {
        public static IEnumerable<string> TargetDLLs { get; } = new string[0];

        public static void Patch(AssemblyDefinition assembly)
        {
        }

        /// <summary>The owner named in this DLL's own UpdateRepo metadata is trusted by default.</summary>
        private static string[] OwnOwner()
        {
            foreach (object attribute in typeof(UpdaterPatcher).Assembly.GetCustomAttributes(typeof(AssemblyMetadataAttribute), false))
            {
                var metadata = (AssemblyMetadataAttribute)attribute;
                if (metadata.Key == "UpdateRepo" && metadata.Value != null && metadata.Value.Contains("/"))
                {
                    return new[] { metadata.Value.Split('/')[0] };
                }
            }

            return new string[0];
        }

        /// <summary>
        /// Dedicated servers run as a separate executable (valheim_server, for example) or headless.
        /// UnityEngine is not loaded yet in the preloader, so the process name and command line have to tell.
        /// </summary>
        private static bool IsDedicatedServer()
        {
            try
            {
                if ((Paths.ProcessName ?? "").IndexOf("server", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                string[] args = Environment.GetCommandLineArgs();
                return Array.Exists(args, a => string.Equals(a, "-batchmode", StringComparison.OrdinalIgnoreCase))
                    && Array.Exists(args, a => string.Equals(a, "-nographics", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Initialize()
        {
            ManualLogSource log = Logger.CreateLogSource("BifrostUpdater");
            try
            {
                var core = new UpdateCore(
                    Paths.BepInExRootPath,
                    OwnOwner(),
                    IsDedicatedServer(),
                    message => log.LogInfo(message),
                    message => log.LogWarning(message),
                    message => log.LogDebug(message));
                core.Run();
            }
            catch (Exception e)
            {
                // An update problem must never keep the game from starting.
                log.LogWarning("Update check failed: " + e.Message);
            }
        }
    }
}
