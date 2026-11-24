// OriginalSetup.cs
// Feature: docs/notes/player-assets.md "First start": a build without the original's content (no GoF2Data tables in
// Resources) asks for the player's copy of Galaxy on Fire 2 HD before the menu (OriginalSetupView, on the startup splash)
// and converts it here: the picked packages (an Android .xapk / .obb / .apk, an iOS .ipa, a folder) on a worker thread
// (OriginalConverter) into "<cache>.new", checked (the game's data tables and English text came out), then swapped in for
// the old cache in one move, its GPU caches with it. GameContent.Refresh then lets every content cache start again.
// Packages a phone's picker copied (PickedFolder) are deleted after the conversion. The HD resources (OriginalPacks
// .InstallHd) are installed, merged into the copy or removed by jobs here too; the first start asks for them once
// (HdPromptDue, OriginalSetupView), docs/notes/player-assets.md "HD resources".

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace GoF2Remake.Data
{
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
    public static class OriginalSetup
    {
        static bool? builtIn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => builtIn = null;

        /// <summary>The build carries the original's content itself (the project, a build made with the assets).</summary>
        public static bool BuiltIn
        {
            get
            {
                if (builtIn == null)
                {
                    var t = Resources.Load<TextAsset>("GoF2Data/ships");
                    builtIn = t != null;
                    if (t != null) Resources.UnloadAsset(t);
                }
                return builtIn.Value;
            }
        }

        /// <summary>The player has to show the game their copy first: nothing built in, no converted copy of this version.</summary>
        public static bool Needed => !BuiltIn && !GameContent.Installed;

        /// <summary>The file kinds a copy comes as (the pickers' filter).</summary>
        public static readonly string[] Extensions = { "xapk", "obb", "apk", "ipa", "zip" };

        /// <summary>A conversion running on its worker thread; read its fields from the main thread.</summary>
        public sealed class Job
        {
            internal volatile int done, total;
            internal volatile string entry, phase = "start";
            internal volatile bool finished;
            /// <summary>Entries done and in all of the archive being read (both 0 between archives).</summary>
            public int Done => done;
            public int Total => total;
            /// <summary>The entry being converted.</summary>
            public string Entry => entry;
            /// <summary>"start", "read &lt;file&gt;", "tables" (the text, data tables and interface images), "swap", "done".</summary>
            public string Phase => phase;
            public bool Finished => finished;
            /// <summary>Why nothing was installed (null when it was).</summary>
            public string Error { get; internal set; }
            /// <summary>What the converter couldn't read (the copy is installed regardless).</summary>
            public List<string> Problems { get; } = new List<string>();
            /// <summary>Files per kind (Models, Textures, Audio, Resources, Localization).</summary>
            public Dictionary<string, int> Counts { get; } = new Dictionary<string, int>();
            public int Files { get; internal set; }
            public float Seconds { get; internal set; }
            /// <summary>The HD resources had been written over the copy this conversion replaced (asked for again).</summary>
            public bool HdLost { get; internal set; }
        }

        /// <summary>PlayerPrefs: the player was asked for the HD resources (1), so the first start doesn't ask again.</summary>
        public const string HdAskedPref = "hdAsked";

        /// <summary>Ask for the HD resources: a converted copy without them that the player wasn't asked about yet.</summary>
        public static bool HdPromptDue => !BuiltIn && GameContent.Installed && !GameContent.HdInstalled
                                          && !OriginalPacks.HdInCopy(GameContent.Root) && PlayerPrefs.GetInt(HdAskedPref, 0) == 0;

        /// <summary>How the HD pack is installed: over the copy (Options' "HD resources" choice), else in its own folder.</summary>
        public static bool HdOver => PlayerPrefs.GetInt(OriginalPacks.HdModePref, 0) == 1;

        /// <summary>Starts converting 'packages' (later ones' files win) into the cache; Finish once the job has finished.</summary>
        public static Job Convert(IList<string> packages)
        {
            var job = new Job();
            string final = GameContent.Root.TrimEnd('/', '\\');
            string work = final + ".new";
            var list = packages.ToList();
            var copies = list.Where(IsImportCopy).ToList();   // (temporaryCachePath: main thread only)
            string packs = OriginalPacks.Folder, hd = GameContent.HdRoot;
            bool over = HdOver;
            var thread = new Thread(() => Run(job, list, copies, final, work, packs, hd, over)) { IsBackground = true, Name = "GoF2 conversion", Priority = System.Threading.ThreadPriority.BelowNormal };
            thread.Start();
            return job;
        }

        static void Run(Job job, List<string> packages, List<string> copies, string final, string work, string packs, string hd, bool over)
        {
            var t0 = DateTime.UtcNow;
            try
            {
                if (Directory.Exists(work)) Directory.Delete(work, true);
                var c = new OriginalConverter(work)
                {
                    Progress = (d, t, e) => { job.total = t; job.done = d; job.entry = e; },
                    Phase = p => { job.phase = p; job.done = job.total = 0; job.entry = null; },
                };
                c.Run(packages);
                job.Problems.AddRange(c.Problems);
                foreach (var g in c.Items.GroupBy(i => i.Path.Split('/')[0])) job.Counts[g.Key] = g.Count();
                job.Files = c.Items.Count;
                // what the game can't start without: its data tables and English text
                string missing = !File.Exists(Path.Combine(work, "Resources", "GoF2Data", "ships.json")) ? "the game's data tables"
                               : !File.Exists(Path.Combine(work, "Localization", "text_en.json")) ? "the English text" : null;
                if (c.Items.Count == 0 || missing != null)
                    throw new InvalidDataException(c.Items.Count == 0 ? "nothing of Galaxy on Fire 2 HD was found in it"
                                                                      : "it has no " + missing + " (an incomplete copy, or another game)");
                // the kept packs over it (what the copy doesn't provide), before it replaces the old copy (an HD folder
                // stays as it is: it outlives the copy)
                foreach (var p in OriginalPacks.Kept(packs))
                {
                    job.phase = "pack " + (p.title ?? p.name); job.done = job.total = 0;
                    if (OriginalPacks.IsHd(p)) InstallKeptHd(job, p, over, work, hd);
                    else OriginalPacks.Apply(p, work, (d, t, e) => { job.total = t; job.done = d; job.entry = e; });
                }
                job.HdLost = Directory.Exists(final) && OriginalPacks.HdInCopy(final) && !OriginalPacks.HdInCopy(work);
                job.phase = "swap";
                if (Directory.Exists(final)) Directory.Delete(final, true);
                Directory.Move(work, final);
                job.phase = "done";
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException
                                      || e is NotSupportedException || e is FormatException || e is ArgumentException)
            {
                job.Error = e.Message;
                try { if (Directory.Exists(work)) Directory.Delete(work, true); } catch (Exception) { }
            }
            finally
            {
                // the pickers' copies (a phone keeps no second 1.4 GB)
                foreach (var p in copies)
                    try { if (File.Exists(p)) File.Delete(p); } catch (Exception) { }
                job.Seconds = (float)(DateTime.UtcNow - t0).TotalSeconds;
                job.finished = true;
            }
        }

        /// <summary>Packs kept but not yet written into the installed copy (a pack added since: the next start writes it).</summary>
        public static bool PacksPending => GameContent.Installed && OriginalPacks.Pending(OriginalPacks.Folder, GameContent.Root, GameContent.HdRoot).Count > 0;

        /// <summary>Writes the pending packs into the installed copy on a worker thread; Finish once the job has finished.</summary>
        public static Job ApplyPacks()
        {
            var job = new Job();
            var pending = OriginalPacks.Pending(OriginalPacks.Folder, GameContent.Root, GameContent.HdRoot);
            string root = GameContent.Root, hd = GameContent.HdRoot;
            bool over = HdOver;
            var thread = new Thread(() =>
            {
                var t0 = DateTime.UtcNow;
                try
                {
                    foreach (var p in pending)
                    {
                        job.phase = "pack " + (p.title ?? p.name); job.done = job.total = 0;
                        if (OriginalPacks.IsHd(p)) InstallKeptHd(job, p, over, root, hd);
                        else OriginalPacks.Apply(p, root, (d, t, e) => { job.total = t; job.done = d; job.entry = e; });
                        job.Files += p.files;
                    }
                    job.phase = "done";
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException || e is NotSupportedException)
                {
                    job.Error = e.Message;
                }
                finally { job.Seconds = (float)(DateTime.UtcNow - t0).TotalSeconds; job.finished = true; }
            }) { IsBackground = true, Name = "GoF2 packs", Priority = System.Threading.ThreadPriority.BelowNormal };
            thread.Start();
            return job;
        }

        /// <summary>A kept HD pack (the server's -pack, an old version's Packs folder) installed by the chosen way, then its
        /// file deleted: the installed files are what is kept.</summary>
        static void InstallKeptHd(Job job, OriginalPacks.Info p, bool over, string root, string hd)
        {
            OriginalPacks.InstallHd(p, over, root, hd, (d, t, e) => { job.total = t; job.done = d; job.entry = e; });
            try { File.Delete(p.file); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        /// <summary>Installs the HD pack picked in the HD window: in its own folder, or 'over' the copy; a picker's copy of
        /// it is deleted afterwards (the player's own file stays). Finish once the job has finished.</summary>
        public static Job InstallHd(OriginalPacks.Info pack, bool over)
        {
            string root = GameContent.Root, hd = GameContent.HdRoot;
            bool copy = IsImportCopy(pack.file) || pack.file.StartsWith(Application.temporaryCachePath, StringComparison.Ordinal);
            return Work("GoF2 HD", job =>
            {
                job.phase = "pack " + (pack.title ?? pack.name);
                try { OriginalPacks.InstallHd(pack, over, root, hd, (d, t, e) => { job.total = t; job.done = d; job.entry = e; }); }
                finally { if (copy) try { File.Delete(pack.file); } catch (Exception) { } }
                job.Files = pack.files;
            });
        }

        /// <summary>Moves the HD folder's files over the copy (freeing the originals' space). Finish once finished.</summary>
        public static Job MergeHd()
        {
            string root = GameContent.Root, hd = GameContent.HdRoot;
            return Work("GoF2 HD merge", job =>
            {
                job.phase = "merge";
                OriginalPacks.MergeHd(root, hd, (d, t, e) => { job.total = t; job.done = d; job.entry = e; });
            });
        }

        /// <summary>Deletes the HD folder. Finish once finished.</summary>
        public static Job RemoveHd()
        {
            string hd = GameContent.HdRoot;
            return Work("GoF2 HD removal", job => { job.phase = "remove"; if (Directory.Exists(hd)) Directory.Delete(hd, true); });
        }

        static Job Work(string name, Action<Job> body)
        {
            var job = new Job();
            var thread = new Thread(() =>
            {
                var t0 = DateTime.UtcNow;
                try { body(job); job.phase = "done"; }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException || e is NotSupportedException)
                {
                    job.Error = e.Message;
                }
                finally { job.Seconds = (float)(DateTime.UtcNow - t0).TotalSeconds; job.finished = true; }
            }) { IsBackground = true, Name = name, Priority = System.Threading.ThreadPriority.BelowNormal };
            thread.Start();
            return job;
        }

        /// <summary>Where a picked package's copy is kept until the conversion (a phone's picker copies it; main thread).</summary>
        public static string PickedFolder => Path.Combine(Application.temporaryCachePath, "OriginalImport");

        /// <summary>A file a picker copied in for the conversion (removed after it), not one of the player's own.</summary>
        public static bool IsImportCopy(string path) => path.StartsWith(PickedFolder, StringComparison.Ordinal);

        /// <summary>On the main thread once the job has finished: the content caches start again from the new copy.</summary>
        public static void Finish(Job job)
        {
            if (job == null || !job.Finished) return;
            if (job.HdLost) PlayerPrefs.DeleteKey(HdAskedPref);   // (they went with the old copy: asked for again)
            GameContent.Refresh();
        }
    }
}
