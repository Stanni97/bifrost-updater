using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using Mono.Cecil;

namespace StanniUpdater
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

        public static void Initialize()
        {
            ManualLogSource log = Logger.CreateLogSource("StanniUpdater");
            try
            {
                var core = new UpdateCore(
                    Paths.BepInExRootPath,
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
