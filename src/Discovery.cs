using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace StanniUpdater
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

    /// <summary>
    /// Finds the installed mods without any list to maintain:
    /// 1. A DLL can name its own repository with [assembly: AssemblyMetadata("UpdateRepo", "Owner/Repo")].
    /// 2. Mods installed from Thunderstore have a manifest.json and sit in a folder called Namespace-Name.
    /// </summary>
    internal static class Discovery
    {
        // Mods released before they carried the metadata.
        private static readonly Dictionary<string, string> Legacy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "KongStrong.dll", "Stanni97/valheim-kong-strong" },
            { "VerlerLeben.dll", "Stanni97/verler-leben" },
        };

        private const long MaxScanBytes = 30L * 1024 * 1024;

        public static List<Candidate> Find(string bepInExRoot, Action<string> debug)
        {
            var found = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
            foreach (string rootName in new[] { "plugins", "patchers" })
            {
                string root = Path.Combine(bepInExRoot, rootName);
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string folder in Directory.GetDirectories(root))
                {
                    Candidate bySource = null;
                    foreach (string dll in Directory.GetFiles(folder, "*.dll", SearchOption.AllDirectories))
                    {
                        Candidate github = FromDll(dll, found);
                        bySource = bySource ?? github;
                    }

                    if (bySource == null)
                    {
                        Candidate store = FromManifest(folder, debug);
                        if (store != null)
                        {
                            found[store.Key] = store;
                        }
                    }
                }

                foreach (string dll in Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    FromDll(dll, found);
                }
            }

            return found.Values.ToList();
        }

        private static Candidate FromDll(string dll, Dictionary<string, Candidate> found)
        {
            string repo = ReadUpdateRepo(dll);
            if (repo == null)
            {
                Legacy.TryGetValue(Path.GetFileName(dll), out repo);
            }

            if (repo == null || repo.Split('/').Length != 2 || repo.Contains(" "))
            {
                return null;
            }

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

            return candidate;
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

        /// <summary>Reads [assembly: AssemblyMetadata("UpdateRepo", ...)] without loading the DLL into the game.</summary>
        private static string ReadUpdateRepo(string dll)
        {
            try
            {
                if (new FileInfo(dll).Length > MaxScanBytes)
                {
                    return null;
                }

                using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(dll))
                {
                    foreach (CustomAttribute attribute in assembly.CustomAttributes)
                    {
                        if (attribute.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute"
                            && attribute.ConstructorArguments.Count == 2
                            && attribute.ConstructorArguments[0].Value as string == "UpdateRepo")
                        {
                            return attribute.ConstructorArguments[1].Value as string;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // not a managed DLL, or unreadable
            }

            return null;
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
