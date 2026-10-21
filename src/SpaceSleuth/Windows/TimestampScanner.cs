using System;
using System.Collections.Generic;
using System.Threading;
using SpaceSleuth.Core;

namespace SpaceSleuth.Windows
{
    /// <summary>
    /// Slower fallback: walks every folder on a drive and looks at file creation / modified dates.
    /// Used when the change journal doesn't reach back far enough, on non-NTFS drives (e.g. exFAT
    /// external sample drives), or when "Thorough scan" is ticked.
    /// </summary>
    internal sealed class TimestampScanner
    {
        public readonly List<ChangedFile> Files = new List<ChangedFile>();
        public readonly List<string> NewFolders = new List<string>();
        /// <summary>Every file over the "big file" size, so later scans can measure exactly how much it grew.</summary>
        public readonly List<KeyValuePair<string, long>> BigFiles = new List<KeyValuePair<string, long>>();
        public long FilesSeen;
        public long FoldersSkipped;

        readonly DateTime cutoffUtc;
        readonly long cluster;
        readonly History history;
        readonly long bigFileBytes;

        public TimestampScanner(DateTime cutoffUtc, long clusterSize, History history, long bigFileBytes)
        {
            this.cutoffUtc = cutoffUtc;
            cluster = Math.Max(512, clusterSize);
            this.history = history;
            this.bigFileBytes = bigFileBytes;
        }

        public void Scan(string root, Action<string> progress, CancellationToken ct)
        {
            long cutoff = cutoffUtc.ToFileTimeUtc();
            var stack = new Stack<string>();
            stack.Push(root.TrimEnd('\\'));
            var lastReport = DateTime.UtcNow;

            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string dir = stack.Pop();
                string dirWithSlash = dir + "\\";

                Native.WIN32_FIND_DATAW fd;
                IntPtr h = Native.FindFirstFileExW(@"\\?\" + dirWithSlash + "*", Native.FindExInfoBasic, out fd,
                                                   Native.FindExSearchNameMatch, IntPtr.Zero, Native.FIND_FIRST_EX_LARGE_FETCH);
                if (h == Native.INVALID_HANDLE_VALUE)
                {
                    FoldersSkipped++;
                    continue;
                }
                try
                {
                    do
                    {
                        string name = fd.cFileName;
                        if (name == "." || name == "..") continue;
                        string full = dirWithSlash + name;
                        bool isDir = (fd.dwFileAttributes & Native.FILE_ATTRIBUTE_DIRECTORY) != 0;
                        long created = Native.FileTimeToLong(fd.ftCreationTimeLow, fd.ftCreationTimeHigh);
                        long written = Native.FileTimeToLong(fd.ftLastWriteTimeLow, fd.ftLastWriteTimeHigh);

                        if (isDir)
                        {
                            // Don't follow junctions/symlinks (avoids loops and double counting).
                            if ((fd.dwFileAttributes & Native.FILE_ATTRIBUTE_REPARSE_POINT) != 0) continue;
                            if (SystemFiles.IsExcludedFolder(full)) continue;
                            if (created >= cutoff) NewFolders.Add(full);
                            stack.Push(full);
                            continue;
                        }

                        FilesSeen++;
                        if (SystemFiles.IsSystemAreaFile(full)) continue;

                        long size = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
                        long onDisk = (size + cluster - 1) / cluster * cluster;
                        if (onDisk >= bigFileBytes) BigFiles.Add(new KeyValuePair<string, long>(full, onDisk));

                        if (created >= cutoff)
                        {
                            Files.Add(new ChangedFile
                            {
                                Path = full, SizeOnDisk = onDisk, AddedBytes = onDisk, Kind = ChangeKind.New,
                                TimeUtc = SafeTime(Math.Max(created, written))
                            });
                        }
                        else if (written >= cutoff && onDisk > 0)
                        {
                            Files.Add(ScanEngine.GrownFile(full, onDisk, SafeTime(written), history));
                        }
                    }
                    while (Native.FindNextFileW(h, out fd));
                }
                finally
                {
                    Native.FindClose(h);
                }

                if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds > 250)
                {
                    lastReport = DateTime.UtcNow;
                    progress(root.Substring(0, 2) + " checking file dates... " + FilesSeen.ToString("N0") + " files  (" + Shorten(dir) + ")");
                }
            }
        }

        static DateTime SafeTime(long ft)
        {
            try { return DateTime.FromFileTimeUtc(ft); }
            catch { return DateTime.UtcNow; }
        }

        static string Shorten(string path)
        {
            return path.Length <= 70 ? path : path.Substring(0, 30) + "..." + path.Substring(path.Length - 35);
        }
    }
}
