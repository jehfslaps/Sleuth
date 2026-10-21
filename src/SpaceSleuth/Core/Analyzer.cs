using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceSleuth.Core
{
    public sealed class AnalyzerOptions
    {
        /// <summary>
        /// How much an existing file that grew (earlier size unknown) counts toward ranking, per byte of its current size.
        /// Kept low so a big file that was merely modified doesn't outrank folders we know are brand new.
        /// </summary>
        public double UnknownGrowthWeight = 0.05;
        /// <summary>Smallest amount worth its own line.</summary>
        public long MinBytes = 20L * 1024 * 1024;
        /// <summary>Also require at least this share of the total before something gets its own line.</summary>
        public double MinShare = 0.001;
        /// <summary>A child folder is "significant" when it holds at least this share of its parent's growth.</summary>
        public double SignificantChildShare = 0.15;
        public int MaxFilesPerCulprit = 500;
        public int MaxCulprits = 200;
    }

    /// <summary>
    /// Builds a folder tree from every file that grew, then finds the folders that best explain the growth
    /// and ranks them from most likely to least likely.
    /// </summary>
    public static class Analyzer
    {
        sealed class Node
        {
            public string Name;
            public string Path;
            public Dictionary<string, Node> Kids = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
            public List<ChangedFile> Files = new List<ChangedFile>();
            public long Added;
            public long Possible;
            public bool IsNewDir;
        }

        public static List<Culprit> Analyze(IEnumerable<ChangedFile> files, IEnumerable<string> newFolders,
                                            IEnumerable<SystemArea> areas, AnalyzerOptions opt)
        {
            if (opt == null) opt = new AnalyzerOptions();
            var root = new Node { Name = "", Path = "" };

            foreach (var f in files)
            {
                if (f == null || string.IsNullOrEmpty(f.Path)) continue;
                if (f.AddedBytes <= 0 && f.PossibleBytes <= 0) continue;
                var parts = f.Path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var node = root;
                node.Added += f.AddedBytes; node.Possible += f.PossibleBytes;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    Node kid;
                    if (!node.Kids.TryGetValue(parts[i], out kid))
                    {
                        string path = i == 0 ? parts[0] + "\\" : (node.Path.EndsWith("\\") ? node.Path : node.Path + "\\") + parts[i];
                        kid = new Node { Name = parts[i], Path = path };
                        node.Kids[parts[i]] = kid;
                    }
                    node = kid;
                    node.Added += f.AddedBytes; node.Possible += f.PossibleBytes;
                }
                node.Files.Add(f);
            }

            if (newFolders != null)
            {
                foreach (var dir in newFolders)
                {
                    var n = Find(root, dir);
                    if (n != null) n.IsNewDir = true;
                }
            }

            // System areas that belong in the ranking: anything that grew since the last scan, plus any
            // restore point made during the window (even on a first scan, or when the total barely moved).
            var areaCulprits = new List<Culprit>();
            foreach (var a in areas ?? Enumerable.Empty<SystemArea>())
            {
                var c = AreaCulprit(a);
                if (c != null) areaCulprits.Add(c);
            }

            Func<Node, double> score = n => n.Added + opt.UnknownGrowthWeight * n.Possible;
            double total = score(root) + areaCulprits.Sum(c => c.Score);
            var result = new List<Culprit>();
            if (total <= 0 && areaCulprits.Count == 0) return result;

            double threshold = Math.Max(opt.MinBytes, total * opt.MinShare);
            var scattered = new List<Node>();

            Action<Node> process = null;
            process = node =>
            {
                double s = score(node);
                if (s < threshold) { scattered.Add(node); return; }

                bool container = node == root || Labeler.IsGenericContainer(node.Path);

                // A folder created inside the window is almost always "the thing" (an install, an unzipped download,
                // a new sample library). Report it as one item instead of breaking it apart.
                if (node.IsNewDir && !container)
                {
                    result.Add(Emit(node, null, opt, null));
                    return;
                }

                var kids = node.Kids.Values.OrderByDescending(score).ToList();
                double minKid = container ? threshold : Math.Max(threshold, opt.SignificantChildShare * s);
                var big = kids.Where(k => score(k) >= minKid).ToList();
                double bigSum = big.Sum(score);

                if (big.Count > 0 && (container || bigSum >= 0.5 * s))
                {
                    foreach (var b in big) process(b);
                    var exclude = new HashSet<Node>(big);
                    if (container)
                    {
                        // Small unrelated things inside a busy folder (e.g. 80 apps in AppData\Local each logging
                        // a few MB) are not one culprit; they belong with the scattered changes.
                        foreach (var k in kids)
                            if (exclude.Add(k)) scattered.Add(k);
                    }
                    double rest = s - exclude.Sum(score);
                    if (node != root && rest >= threshold)
                    {
                        result.Add(Emit(node, exclude, opt, "New files in this folder itself (its bigger subfolders are listed separately)"));
                    }
                    else
                    {
                        foreach (var k in kids) if (!exclude.Contains(k)) scattered.Add(k);
                        if (node.Files.Count > 0) scattered.Add(DirectFilesOf(node));
                    }
                    return;
                }

                if (container)
                {
                    // Nothing inside stands out on its own.
                    foreach (var k in kids) scattered.Add(k);
                    double direct = s - kids.Sum(score);
                    if (node != root && direct >= threshold)
                        result.Add(Emit(node, new HashSet<Node>(kids), opt, "New files directly in this folder"));
                    else if (node.Files.Count > 0)
                        scattered.Add(DirectFilesOf(node));
                    return;
                }

                // Growth is spread across this folder's contents with no single dominant subfolder: this is the spot.
                result.Add(Emit(node, null, opt, null));
            };
            process(root);

            result.AddRange(areaCulprits);

            result = result.OrderByDescending(c => c.Score).ThenByDescending(c => c.LastUtc).ToList();

            // Anything past the cap joins the "scattered" bucket.
            var extra = new List<ChangedFile>();
            if (result.Count > opt.MaxCulprits)
            {
                foreach (var c in result.Skip(opt.MaxCulprits)) extra.AddRange(c.Files);
                result = result.Take(opt.MaxCulprits).ToList();
            }

            var scatteredFiles = new List<ChangedFile>();
            foreach (var n in scattered) Collect(n, null, scatteredFiles);
            scatteredFiles.AddRange(extra);
            if (scatteredFiles.Count > 0)
            {
                var c = Build(scatteredFiles, opt, opt.UnknownGrowthWeight);
                c.Kind = CulpritKind.Scattered;
                c.Path = "Lots of small changes in different folders";
                c.Label = "Little bits spread around \u2014 app updates, caches, logs";
                c.Note = "Each folder here added less than " + Format.Bytes((long)threshold);
                if (c.Score > 0) result.Add(c);
            }

            for (int i = 0; i < result.Count; i++)
            {
                result[i].Rank = i + 1;
                result[i].Likelihood = total > 0 ? result[i].Score / total : 0;
            }
            return result;
        }

        static bool IsRestorePoints(SystemArea a)
        {
            return a.Key == "shadow";
        }

        /// <summary>Turns a system area into a ranked item, or null if it doesn't belong in the ranking.</summary>
        static Culprit AreaCulprit(SystemArea a)
        {
            const long MB = 1024L * 1024;
            bool grew = a.Delta.HasValue && a.Delta.Value >= MB;
            bool newRestorePoint = IsRestorePoints(a) && a.NewInWindow > 0;
            if (!grew && !newRestorePoint) return null;

            var c = new Culprit
            {
                Kind = CulpritKind.SystemArea,
                Path = a.Drive + " " + a.Name,
                Label = a.Name,
                Contents = a.Explanation,
                LastUtc = a.LatestNewUtc ?? DateTime.UtcNow,
                FirstUtc = a.PreviousUtc ?? a.LatestNewUtc ?? DateTime.UtcNow,
                HiddenReason = "Not visible as normal files",
                OpenTarget = a.OpenTarget,
                NewFiles = a.NewInWindow
            };
            string activity = string.IsNullOrEmpty(a.Activity) ? "" : ". " + a.Activity;

            if (grew)
            {
                c.AddedBytes = a.Delta.Value;
                c.Score = a.Delta.Value;
                c.Confidence = "High";
                c.Note = "Grew since your last scan" + activity;
            }
            else if (a.Delta.HasValue)
            {
                // A restore point was made, but the total hardly changed: Windows usually deletes the oldest
                // restore point to make room when the space set aside for them is full.
                c.Score = 0;
                c.Confidence = "Medium";
                c.Note = "New restore point, but the total barely changed \u2014 Windows probably removed an older one to make room" + activity;
            }
            else
            {
                // First scan: Windows doesn't report the size of a single restore point, so estimate it as
                // the average size of the restore points / shadow copies on this drive.
                long estimate = a.TotalCount > 0 ? a.CurrentBytes / a.TotalCount : a.CurrentBytes;
                c.EstimateBytes = estimate * a.NewInWindow;
                if (c.EstimateBytes > a.CurrentBytes) c.EstimateBytes = a.CurrentBytes;
                c.Score = c.EstimateBytes;
                c.Confidence = "Low";
                c.Note = "New restore point \u2014 exact size shows up after your next scan (this is an estimate)" + activity;
            }
            return c;
        }

        static Node Find(Node root, string path)
        {
            var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var node = root;
            foreach (var p in parts)
            {
                if (!node.Kids.TryGetValue(p, out node)) return null;
            }
            return node == root ? null : node;
        }

        static void Collect(Node n, HashSet<Node> exclude, List<ChangedFile> into)
        {
            into.AddRange(n.Files);
            foreach (var k in n.Kids.Values)
            {
                if (exclude != null && exclude.Contains(k)) continue;
                Collect(k, null, into);
            }
        }

        static Node DirectFilesOf(Node node)
        {
            return new Node
            {
                Path = node.Path,
                Files = node.Files,
                Added = node.Files.Sum(f => f.AddedBytes),
                Possible = node.Files.Sum(f => f.PossibleBytes)
            };
        }

        static Culprit Emit(Node node, HashSet<Node> exclude, AnalyzerOptions opt, string note)
        {
            var files = new List<ChangedFile>();
            Collect(node, exclude, files);
            var c = Build(files, opt, opt.UnknownGrowthWeight);
            c.Kind = CulpritKind.Folder;
            c.Path = node.Path;
            c.IsNewFolder = node.IsNewDir;
            c.IsRemainder = exclude != null && exclude.Count > 0;
            c.Label = Labeler.Describe(node.Path);
            c.HiddenReason = Labeler.HiddenReasonFromPath(node.Path);
            if (c.IsNewFolder)
            {
                c.Note = "New folder created " + Format.When(c.FirstUtc);
                if (c.AddedBytes > 0) c.Confidence = "High";
            }
            else
            {
                c.Note = note;
            }
            return c;
        }

        static Culprit Build(List<ChangedFile> files, AnalyzerOptions opt, double unknownWeight)
        {
            var c = new Culprit();
            c.AddedBytes = files.Sum(f => f.AddedBytes);
            c.PossibleBytes = files.Sum(f => f.PossibleBytes);
            c.Score = c.AddedBytes + unknownWeight * c.PossibleBytes;
            c.NewFiles = files.Count(f => f.Kind == ChangeKind.New);
            c.GrewFiles = files.Count - c.NewFiles;
            if (files.Count > 0)
            {
                c.FirstUtc = files.Min(f => f.TimeUtc);
                c.LastUtc = files.Max(f => f.TimeUtc);
            }
            c.Contents = Labeler.Summarize(files);
            c.Files = files.OrderByDescending(f => f.AddedBytes + f.PossibleBytes).Take(opt.MaxFilesPerCulprit).ToList();
            double exact = c.AddedBytes;
            c.Confidence = c.Score <= 0 ? "Low" : exact >= 0.8 * c.Score ? "High" : exact >= 0.4 * c.Score ? "Medium" : "Low";
            return c;
        }
    }
}
