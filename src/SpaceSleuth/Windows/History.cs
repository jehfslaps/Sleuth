using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpaceSleuth.Windows
{
    internal static class SystemFiles
    {
        static readonly Regex AreaFile = new Regex(
            @"^[a-z]:\\(pagefile\.sys|hiberfil\.sys|swapfile\.sys|dumpstack\.log\.tmp|windows\\memory\.dmp)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        static readonly Regex ExcludedFolder = new Regex(
            @"^[a-z]:\\system volume information(\\|$)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>Files that are reported as "system areas" instead of as normal files.</summary>
        public static bool IsSystemAreaFile(string path)
        {
            return AreaFile.IsMatch(path);
        }

        /// <summary>Restore point storage lives here; it's measured separately via the shadow copy service.</summary>
        public static bool IsExcludedFolder(string path)
        {
            return ExcludedFolder.IsMatch(path);
        }
    }

    /// <summary>
    /// A small text file in %LOCALAPPDATA%\SpaceSleuth that remembers earlier scans, so the app can say
    /// "free space dropped 11.7 GB since your last scan" and measure exactly how much big files and system areas grew.
    /// </summary>
    internal sealed class History
    {
        public sealed class ScanRecord
        {
            public string Drive;
            public DateTime Utc;
            public long FreeBytes;
        }

        sealed class SizeRecord
        {
            public long Bytes;
            public DateTime Utc;
        }

        readonly string path;
        readonly List<ScanRecord> scans = new List<ScanRecord>();
        // every reading of every system area over time, so "3 days ago" can be looked up
        readonly Dictionary<string, List<SizeRecord>> areas = new Dictionary<string, List<SizeRecord>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, SizeRecord> big = new Dictionary<string, SizeRecord>(StringComparer.OrdinalIgnoreCase);

        const int MaxScans = 2000;
        const int MaxAreaReadings = 400;
        const int MaxBigFiles = 50000;

        History(string path)
        {
            this.path = path;
        }

        public static string DefaultPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSleuth");
                return Path.Combine(dir, "history.txt");
            }
        }

        public static History Load()
        {
            var h = new History(DefaultPath);
            try
            {
                if (!File.Exists(h.path)) return h;
                foreach (var line in File.ReadAllLines(h.path))
                {
                    var p = line.Split('\t');
                    try
                    {
                        if (p[0] == "scan" && p.Length >= 4)
                            h.scans.Add(new ScanRecord { Drive = p[1], Utc = Ticks(p[2]), FreeBytes = Long(p[3]) });
                        else if (p[0] == "area" && p.Length >= 4)
                            h.AreaList(p[1]).Add(new SizeRecord { Bytes = Long(p[2]), Utc = Ticks(p[3]) });
                        else if (p[0] == "big" && p.Length >= 4)
                            h.big[p[1]] = new SizeRecord { Bytes = Long(p[2]), Utc = Ticks(p[3]) };
                    }
                    catch
                    {
                        // ignore a damaged line
                    }
                }
            }
            catch
            {
                // unreadable history: start fresh
            }
            return h;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var lines = new List<string> { "# Sleuth scan history. Safe to delete." };
                foreach (var s in scans.OrderBy(s => s.Utc).Skip(Math.Max(0, scans.Count - MaxScans)))
                    lines.Add("scan\t" + s.Drive + "\t" + s.Utc.Ticks.ToString(CultureInfo.InvariantCulture) + "\t" + s.FreeBytes.ToString(CultureInfo.InvariantCulture));
                foreach (var kv in areas)
                    foreach (var r in kv.Value.OrderBy(r => r.Utc).Skip(Math.Max(0, kv.Value.Count - MaxAreaReadings)))
                        lines.Add("area\t" + kv.Key + "\t" + r.Bytes.ToString(CultureInfo.InvariantCulture) + "\t" + r.Utc.Ticks.ToString(CultureInfo.InvariantCulture));
                foreach (var kv in big.OrderByDescending(kv => kv.Value.Utc).Take(MaxBigFiles))
                    lines.Add("big\t" + kv.Key + "\t" + kv.Value.Bytes.ToString(CultureInfo.InvariantCulture) + "\t" + kv.Value.Utc.Ticks.ToString(CultureInfo.InvariantCulture));
                string tmp = path + ".tmp";
                File.WriteAllLines(tmp, lines);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch
            {
                // History is a nice-to-have; never fail a scan because of it.
            }
        }

        public ScanRecord LastScan(string drive)
        {
            return scans.Where(s => string.Equals(s.Drive, drive, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(s => s.Utc).FirstOrDefault();
        }

        public DateTime? LatestScanUtc()
        {
            return scans.Count == 0 ? (DateTime?)null : scans.Max(s => s.Utc);
        }

        public void AddScan(string drive, DateTime utc, long free)
        {
            scans.Add(new ScanRecord { Drive = drive, Utc = utc, FreeBytes = free });
        }

        List<SizeRecord> AreaList(string key)
        {
            List<SizeRecord> list;
            if (!areas.TryGetValue(key, out list)) areas[key] = list = new List<SizeRecord>();
            return list;
        }

        /// <summary>
        /// Picks the reading that best marks the START of a time window: the latest one at (or just after) the
        /// window's start, if it isn't far older than the window; otherwise the earliest one inside the first
        /// half of the window. Null when nothing that old was ever recorded.
        /// </summary>
        public static T PickBaseline<T>(IEnumerable<T> readings, Func<T, DateTime> when, DateTime cutoffUtc, TimeSpan window, DateTime nowUtc) where T : class
        {
            var sorted = readings.OrderBy(when).ToList();
            var edge = cutoffUtc.AddMinutes(10);
            T before = sorted.LastOrDefault(r => when(r) <= edge);
            T after = sorted.FirstOrDefault(r => when(r) > edge);
            var slack = TimeSpan.FromTicks(Math.Max(TimeSpan.FromHours(2).Ticks, window.Ticks / 4));
            if (before != null && cutoffUtc - when(before) <= slack) return before;
            if (after != null && when(after) <= cutoffUtc + TimeSpan.FromTicks(window.Ticks / 2)) return after;
            return before; // older than ideal, but the only thing that reaches back that far
        }

        /// <summary>Free space on a drive at the start of a time window (see <see cref="PickBaseline{T}"/>).</summary>
        public ScanRecord BaselineScan(string drive, DateTime cutoffUtc, TimeSpan window, DateTime nowUtc)
        {
            return PickBaseline(scans.Where(s => string.Equals(s.Drive, drive, StringComparison.OrdinalIgnoreCase)),
                                s => s.Utc, cutoffUtc, window, nowUtc);
        }

        public DateTime? EarliestScanUtc(string drive)
        {
            var mine = scans.Where(s => string.Equals(s.Drive, drive, StringComparison.OrdinalIgnoreCase)).ToList();
            return mine.Count == 0 ? (DateTime?)null : mine.Min(s => s.Utc);
        }

        /// <summary>A system area's size at the start of a time window.</summary>
        public bool TryGetAreaAt(string drive, string key, DateTime cutoffUtc, TimeSpan window, DateTime nowUtc, out long bytes, out DateTime utc)
        {
            List<SizeRecord> list;
            SizeRecord r = areas.TryGetValue(drive + "|" + key, out list) ? PickBaseline(list, x => x.Utc, cutoffUtc, window, nowUtc) : null;
            if (r != null)
            {
                bytes = r.Bytes; utc = r.Utc;
                return true;
            }
            bytes = 0; utc = DateTime.MinValue;
            return false;
        }

        public void SetArea(string drive, string key, long bytes, DateTime utc)
        {
            AreaList(drive + "|" + key).Add(new SizeRecord { Bytes = bytes, Utc = utc });
        }

        public bool TryGetBig(string filePath, out long bytes, out DateTime utc)
        {
            SizeRecord r;
            if (big.TryGetValue(filePath, out r))
            {
                bytes = r.Bytes; utc = r.Utc;
                return true;
            }
            bytes = 0; utc = DateTime.MinValue;
            return false;
        }

        public void SetBig(string filePath, long bytes, DateTime utc)
        {
            big[filePath] = new SizeRecord { Bytes = bytes, Utc = utc };
        }

        static DateTime Ticks(string s)
        {
            return new DateTime(long.Parse(s, CultureInfo.InvariantCulture), DateTimeKind.Utc);
        }

        static long Long(string s)
        {
            return long.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
