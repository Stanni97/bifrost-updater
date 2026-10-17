using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace BifrostUpdater
{
    internal enum SourceKind
    {
        GitHub,
        Thunderstore,
    }

    /// <summary>An installed mod together with the place its updates come from.</summary>
    internal sealed class Candidate
    {
        public SourceKind Kind;
        public string Label;                                  // for the log
        public string Folder;                                 // mod folder, used for Thunderstore packages
        public List<string> Dlls = new List<string>();        // GitHub: the files that get replaced
        public Version Installed;
        public string Repo;                                   // GitHub: Owner/Repo
        public string Namespace, Package;                     // Thunderstore: Namespace-Package

        public string Owner => Kind == SourceKind.GitHub ? Repo.Split('/')[0] : Namespace;
        public string Key => Kind == SourceKind.GitHub ? Repo : Namespace + "-" + Package;
    }

    internal sealed class DiscoveryResult
    {
        public Dictionary<string, Candidate> Mods = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);

        /// <summary>BepInEx plugins that say nothing about where they come from.</summary>
        public List<string> Unresolved = new List<string>();
    }

    /// <summary>
    /// Finds the installed mods without any list to maintain. The source of a mod is, in this order:
    /// 1. a Mod line in the config,
    /// 2. the repository the DLL names itself: [assembly: AssemblyMetadata("UpdateRepo", "Owner/Repo")],
    /// 3. the manifest.json of a Thunderstore install (folder Namespace-Name),
    /// 4. for plugins without any of these, a repository of a trusted GitHub user that matches the DLL (see UpdateCore).
    /// </summary>
    internal static class Discovery
    {
        private const long MaxScanBytes = 30L * 1024 * 1024;

        private struct DllInfo
        {
            public string UpdateRepo;
            public bool IsPlugin;
        }

        public static DiscoveryResult Find(string bepInExRoot, IDictionary<string, string> sources, Action<string> debug)
        {
            var result = new DiscoveryResult();
            foreach (string rootName in new[] { "plugins", "patchers" })
            {
                string root = Path.Combine(bepInExRoot, rootName);
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string folder in Directory.GetDirectories(root))
                {
                    var unresolved = new List<string>();
                    bool resolved = false;
                    foreach (string dll in Directory.GetFiles(folder, "*.dll", SearchOption.AllDirectories))
                    {
                        resolved |= Add(dll, result.Mods, sources, unresolved);
                    }

                    if (resolved)
                    {
                        continue;
                    }

                    Candidate store = FromManifest(folder, debug);
                    if (store != null)
                    {
                        result.Mods[store.Key] = store;
                    }
                    else
                    {
                        result.Unresolved.AddRange(unresolved);
                    }
                }

                foreach (string dll in Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var unresolved = new List<string>();
                    if (!Add(dll, result.Mods, sources, unresolved))
                    {
                        result.Unresolved.AddRange(unresolved);
                    }
                }
            }

            return result;
        }

        /// <summary>Registers the DLL if its source is known; otherwise remembers it if it is a plugin.</summary>
        private static bool Add(string dll, Dictionary<string, Candidate> found, IDictionary<string, string> sources, List<string> unresolved)
        {
            DllInfo info = Inspect(dll);
            if (!sources.TryGetValue(Path.GetFileName(dll), out string repo))
            {
                repo = info.UpdateRepo;
            }

            if (repo == null || repo.Split('/').Length != 2 || repo.Contains(" "))
            {
                if (info.IsPlugin)
                {
                    unresolved.Add(dll);
                }

                return false;
            }

            AddGitHub(found, repo, dll);
            return true;
        }

        public static void AddGitHub(Dictionary<string, Candidate> found, string repo, string dll)
        {
            if (!found.TryGetValue(repo, out Candidate candidate))
            {
                candidate = new Candidate { Kind = SourceKind.GitHub, Repo = repo, Label = Path.GetFileNameWithoutExtension(dll), Folder = Path.GetDirectoryName(dll) };
                found[repo] = candidate;
            }

            candidate.Dlls.Add(dll);
            Version version = ReadVersion(dll);
            if (version != null && (candidate.Installed == null || version < candidate.Installed))
            {
                candidate.Installed = version;
            }
        }

        private static Candidate FromManifest(string folder, Action<string> debug)
        {
            string path = Path.Combine(folder, "manifest.json");
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                object manifest = Json.Parse(File.ReadAllText(path));
                string name = Json.Str(manifest, "name");
                string versionText = Json.Str(manifest, "version_number");
                string dirName = Path.GetFileName(folder);
                if (string.IsNullOrEmpty(name) || !Version.TryParse(versionText, out Version version))
                {
                    return null;
                }

                // r2modman and Gale name the folder Namespace-Name.
                string suffix = "-" + name;
                if (dirName.Length <= suffix.Length || !dirName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    debug(dirName + ": folder is not called Namespace-" + name + ", cannot tell where it comes from.");
                    return null;
                }

                return new Candidate
                {
                    Kind = SourceKind.Thunderstore,
                    Label = name,
                    Folder = folder,
                    Namespace = dirName.Substring(0, dirName.Length - suffix.Length),
                    Package = name,
                    Installed = version,
                };
            }
            catch (Exception e)
            {
                debug(folder + ": manifest.json unreadable: " + e.Message);
                return null;
            }
        }

        /// <summary>Reads the DLL's attributes without loading it into the game.</summary>
        private static DllInfo Inspect(string dll)
        {
            var info = new DllInfo();
            try
            {
                if (new FileInfo(dll).Length > MaxScanBytes)
                {
                    return info;
                }

                using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(dll))
                {
                    foreach (CustomAttribute attribute in assembly.CustomAttributes)
                    {
                        if (attribute.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute"
                            && attribute.ConstructorArguments.Count == 2
                            && attribute.ConstructorArguments[0].Value as string == "UpdateRepo")
                        {
                            info.UpdateRepo = attribute.ConstructorArguments[1].Value as string;
                        }
                    }

                    // The BepInPlugin attribute sits on the plugin class, not on the assembly.
                    info.IsPlugin = assembly.MainModule.Types.Any(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "BepInEx.BepInPlugin"));
                }
            }
            catch (Exception)
            {
                // not a managed DLL, or unreadable
            }

            return info;
        }

        internal static Version ReadVersion(string path)
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
    }
}
