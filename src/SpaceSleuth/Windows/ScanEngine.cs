using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SpaceSleuth.Core;

namespace SpaceSleuth.Windows
{
    public sealed class ScanOptions
    {
        public TimeSpan Window = TimeSpan.FromHours(48);
        public List<string> Drives = new List<string>();
        /// <summary>Also check every file's dates, even when the change journal covers the window.</summary>
        public bool Thorough;
    }

    public sealed class DriveChoice
    {
        public string Drive;
        public string Label;
        public long FreeBytes;
        public long TotalBytes;
        public string FileSystem;

        public override string ToString()
        {
            string name = string.IsNullOrEmpty(Label) ? Drive : Drive + " " + Label;
            return name + "  (" + Format.Bytes(FreeBytes) + " free of " + Format.Bytes(TotalBytes) + ")";
        }
    }

    public sealed class DriveReport
    {
        public string Drive;
        public string FileSystem;
        public string Method;
        public string Note;
        public long FreeBytes;
        public long TotalBytes;
        public long? PrevFreeBytes;
        public DateTime? PrevScanUtc;
        public int ChangedFiles;
        public long FoundBytes;
    }

    public sealed class ScanResult
    {
        public DateTime CutoffUtc;
        public DateTime FinishedUtc;
        public TimeSpan Window;
        public TimeSpan Elapsed;
        public bool Elevated;
        public List<Culprit> Culprits = new List<Culprit>();
        public List<SystemArea> Areas = new List<SystemArea>();
        public List<DriveReport> Drives = new List<DriveReport>();
        public List<string> Notes = new List<string>();
        public int DeletedFiles;
        public int Unreadable;

        public long TotalAdded { get { return Culprits.Sum(c => c.AddedBytes); } }
    }

    public static class ScanEngine
    {
        /// <summary>Files at least this big are remembered between scans so their exact growth can be measured.</summary>
        public const long BigFileBytes = 256L * 1024 * 1024;

        /// <summary>
        /// When Sleuth last scanned. With drives given, the earliest last scan among those drives
        /// (so nothing is missed on any of them); with null, the most recent scan of any drive.
        /// </summary>
        public static DateTime? LastScanUtc(IEnumerable<string> drives)
        {
            var history = History.Load();
            if (drives == null) return history.LatestScanUtc();
            DateTime? earliest = null;
            foreach (var d in drives)
            {
                var s = history.LastScan(d);
                if (s != null && (!earliest.HasValue || s.Utc < earliest.Value)) earliest = s.Utc;
            }
            return earliest;
        }

        /// <summary>Free space on a drive at the last finished scan, if there was one.</summary>
        public static bool TryLastScan(string drive, out long freeBytes, out DateTime utc)
        {
            var last = History.Load().LastScan(drive);
            freeBytes = last != null ? last.FreeBytes : 0;
            utc = last != null ? last.Utc : DateTime.MinValue;
            return last != null;
        }

        /// <summary>Free space on a drive right now.</summary>
        public static long FreeNow(string drive)
        {
            long free, total;
            return Native.FreeSpace(drive + "\\", out free, out total) ? free : 0;
        }

        public static List<DriveChoice> AvailableDrives()
        {
            var list = new List<DriveChoice>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;
                    list.Add(new DriveChoice
                    {
                        Drive = d.Name.Substring(0, 2).ToUpperInvariant(),
                        Label = d.VolumeLabel,
                        FreeBytes = d.TotalFreeSpace,
                        TotalBytes = d.TotalSize,
                        FileSystem = d.DriveFormat
                    });
                }
                catch
                {
                    // drive went away or is locked (BitLocker); skip it
                }
            }
            return list;
        }

        internal static ChangedFile GrownFile(string path, long onDisk, DateTime timeUtc, History history)
        {
            long prev; DateTime prevUtc;
            if (history != null && history.TryGetBig(path, out prev, out prevUtc))
            {
                return new ChangedFile
                {
                    Path = path, SizeOnDisk = onDisk, AddedBytes = Math.Max(0, onDisk - prev),
                    Kind = ChangeKind.GrewKnown, TimeUtc = timeUtc
                };
            }
            return new ChangedFile
            {
                Path = path, SizeOnDisk = onDisk, PossibleBytes = onDisk, Kind = ChangeKind.GrewUnknown, TimeUtc = timeUtc
            };
        }

        public static ScanResult Run(ScanOptions opt, IProgress<string> progress, CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            var now = DateTime.UtcNow;
            var res = new ScanResult
            {
                Window = opt.Window,
                CutoffUtc = now - opt.Window,
                Elevated = Native.IsElevated()
            };
            Action<string> report = s => { if (progress != null) progress.Report(s); };

            if (res.Elevated) Native.EnablePrivilege("SeBackupPrivilege");
            else res.Notes.Add("Not running as administrator, so the fast change journal couldn't be used and some folders couldn't be read. Right-click SpaceSleuth.exe > Run as administrator for full results.");

            var history = History.Load();
            report("Checking restore points and shadow copies...");
            var shadows = res.Elevated ? SystemAreas.LoadShadowInfo() : null;
            if (shadows != null && shadows.Problem != null) res.Notes.Add(shadows.Problem);

            string systemDrive = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").Substring(0, 2).ToUpperInvariant();
            var allFiles = new List<ChangedFile>();
            var newDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var drive in opt.Drives)
            {
                ct.ThrowIfCancellationRequested();
                string root = drive + "\\";
                var rep = new DriveReport { Drive = drive, FileSystem = Native.FileSystemName(root) };
                long free, total;
                Native.FreeSpace(root, out free, out total);
                rep.FreeBytes = free;
                rep.TotalBytes = total;
                var last = history.LastScan(drive);
                if (last != null)
                {
                    rep.PrevFreeBytes = last.FreeBytes;
                    rep.PrevScanUtc = last.Utc;
                }

                var files = new Dictionary<string, ChangedFile>(StringComparer.OrdinalIgnoreCase);
                var touchedAreaFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var bigSeen = new List<KeyValuePair<string, long>>();
                bool journalCovers = false, journalUsed = false;

                if (res.Elevated && string.Equals(rep.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    report(drive + " reading change journal...");
                    var jr = UsnJournal.Read(drive, res.CutoffUtc, report, ct);
                    if (jr.Available)
                    {
                        journalUsed = true;
                        journalCovers = jr.CoversWindow;
                        ResolveJournal(drive, root, jr, history, files, newDirs, touchedAreaFiles, res, report, ct);
                        if (!jr.CoversWindow)
                            rep.Note = "Windows' change log for " + drive + " only reaches back to " + Format.When(jr.OldestUtc) +
                                       ", so every file's dates were checked as well.";
                    }
                    else
                    {
                        rep.Note = jr.Problem + "; checked file dates instead.";
                    }
                }
                else if (!string.Equals(rep.FileSystem, "NTFS", StringComparison.OrdinalIgnoreCase))
                {
                    rep.Note = (string.IsNullOrEmpty(rep.FileSystem) ? "This" : rep.FileSystem) + " drive has no change journal; checked file dates instead.";
                }

                if (!journalCovers || opt.Thorough)
                {
                    var ts = new TimestampScanner(res.CutoffUtc, Native.ClusterSize(root), history, BigFileBytes);
                    ts.Scan(root, report, ct);
                    foreach (var f in ts.Files)
                        if (!files.ContainsKey(f.Path)) files[f.Path] = f;
                    foreach (var d in ts.NewFolders) newDirs.Add(d);
                    bigSeen.AddRange(ts.BigFiles);
                    rep.Method = journalUsed ? "Change journal + file dates" : "File dates";
                }
                else
                {
                    rep.Method = "Change journal";
                }

                rep.ChangedFiles = files.Count;
                rep.FoundBytes = files.Values.Sum(f => f.AddedBytes);
                allFiles.AddRange(files.Values);

                report(drive + " measuring system areas...");
                var areas = SystemAreas.Collect(drive, drive == systemDrive, res.CutoffUtc, history, shadows, touchedAreaFiles);
                res.Areas.AddRange(areas);
                res.Drives.Add(rep);

                // Remember this scan for next time.
                history.AddScan(drive, now, free);
                foreach (var a in areas) history.SetArea(drive, a.Key, a.CurrentBytes, now);
                foreach (var f in files.Values) if (f.SizeOnDisk >= BigFileBytes) history.SetBig(f.Path, f.SizeOnDisk, now);
                foreach (var kv in bigSeen) history.SetBig(kv.Key, kv.Value, now);
            }

            report("Ranking the most likely places...");
            res.Culprits = Analyzer.Analyze(allFiles, newDirs, res.Areas, new AnalyzerOptions());
            foreach (var c in res.Culprits)
                if (c.Kind == CulpritKind.Folder && c.HiddenReason == null)
                    c.HiddenReason = HiddenFolderReason(c.Path);

            foreach (var rep in res.Drives)
            {
                if (!rep.PrevFreeBytes.HasValue) continue;
                long drop = rep.PrevFreeBytes.Value - rep.FreeBytes;
                long areaGrowth = res.Areas.Where(a => a.Drive == rep.Drive && a.Delta.HasValue && a.Delta.Value > 0).Sum(a => a.Delta.Value);
                long explained = rep.FoundBytes + areaGrowth;
                long gap = drop - explained;
                if (drop > 0 && gap > Math.Max(1024L * 1024 * 1024, drop / 5))
                {
                    res.Notes.Add("Free space on " + rep.Drive + " dropped " + Format.Bytes(drop) + " since your last scan (" + Format.When(rep.PrevScanUtc.Value) +
                                  "), but this scan only accounts for " + Format.Bytes(explained) + ". " +
                                  (rep.PrevScanUtc.Value < res.CutoffUtc ? "Your last scan was before this time window, so try a longer window. " : "") +
                                  "Space can also go to files that were created and deleted again, or to restore points that Windows later cleaned up.");
                }
            }

            history.Save();
            res.Elapsed = sw.Elapsed;
            res.FinishedUtc = DateTime.UtcNow;
            return res;
        }

        static void ResolveJournal(string drive, string root, JournalResult jr, History history, Dictionary<string, ChangedFile> files,
                                   HashSet<string> newDirs, HashSet<string> touchedAreaFiles, ScanResult res,
                                   Action<string> report, CancellationToken ct)
        {
            using (var r = new FileIdResolver(root))
            {
                if (!r.Ok) return;
                int i = 0, total = jr.Entries.Count;
                foreach (var e in jr.Entries.Values)
                {
                    if (++i % 2000 == 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        report(drive + " measuring changed files... " + i.ToString("N0") + " of " + total.ToString("N0"));
                    }

                    if (e.IsDir)
                    {
                        if (e.Created && !e.Deleted)
                        {
                            string p = r.FolderPath(e.Frn);
                            if (p != null) newDirs.Add(p.TrimEnd('\\'));
                        }
                        continue;
                    }
                    if (e.Deleted)
                    {
                        if (!e.Created) res.DeletedFiles++;
                        continue;
                    }

                    bool extended = (e.Reasons & (Native.USN_REASON_DATA_EXTEND | Native.USN_REASON_NAMED_DATA_EXTEND)) != 0;
                    if (!e.Created && !extended) continue; // renamed, overwritten in place or shrunk: no new space

                    long alloc; DateTime createdUtc;
                    if (!r.TryGetSize(e.Frn, out alloc, out createdUtc)) { res.Unreadable++; continue; }
                    string dir = r.FolderPath(e.ParentFrn);
                    if (dir == null || e.Name == null) { res.Unreadable++; continue; }
                    string path = dir.EndsWith("\\") ? dir + e.Name : dir + "\\" + e.Name;

                    if (SystemFiles.IsSystemAreaFile(path)) { touchedAreaFiles.Add(path); continue; }
                    if (SystemFiles.IsExcludedFolder(path)) continue;

                    DateTime time;
                    try { time = DateTime.FromFileTimeUtc(e.LastTime); } catch { time = DateTime.UtcNow; }

                    ChangedFile f;
                    if (e.Created || createdUtc >= res.CutoffUtc)
                        f = new ChangedFile { Path = path, SizeOnDisk = alloc, AddedBytes = alloc, Kind = ChangeKind.New, TimeUtc = time };
                    else
                        f = GrownFile(path, alloc, time, history);

                    if (f.AddedBytes > 0 || f.PossibleBytes > 0) files[path] = f;
                }
            }
        }

        static string HiddenFolderReason(string path)
        {
            var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                cur += "\\" + parts[i];
                uint a = Native.GetFileAttributesW(@"\\?\" + cur);
                if (a != 0xFFFFFFFF && (a & Native.FILE_ATTRIBUTE_HIDDEN) != 0)
                    return "Inside a hidden folder (" + parts[i] + ")";
            }
            return null;
        }
    }
}
