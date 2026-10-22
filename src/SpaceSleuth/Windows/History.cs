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
        readonly Dictionary<string, SizeRecord> areas = new Dictionary<string, SizeRecord>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, SizeRecord> big = new Dictionary<string, SizeRecord>(StringComparer.OrdinalIgnoreCase);

        const int MaxScans = 500;
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
                            h.areas[p[1]] = new SizeRecord { Bytes = Long(p[2]), Utc = Ticks(p[3]) };
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
                    lines.Add("area\t" + kv.Key + "\t" + kv.Value.Bytes.ToString(CultureInfo.InvariantCulture) + "\t" + kv.Value.Utc.Ticks.ToString(CultureInfo.InvariantCulture));
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

        public bool TryGetArea(string drive, string key, out long bytes, out DateTime utc)
        {
            SizeRecord r;
            if (areas.TryGetValue(drive + "|" + key, out r))
            {
                bytes = r.Bytes; utc = r.Utc;
                return true;
            }
            bytes = 0; utc = DateTime.MinValue;
            return false;
        }

        public void SetArea(string drive, string key, long bytes, DateTime utc)
        {
            areas[drive + "|" + key] = new SizeRecord { Bytes = bytes, Utc = utc };
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
