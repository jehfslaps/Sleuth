using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SpaceSleuth.Windows
{
    /// <summary>Everything the change journal told us about one file or folder during the time window.</summary>
    internal sealed class JournalEntry
    {
        public ulong Frn;
        public ulong ParentFrn;
        public string Name;
        public uint Reasons;
        public bool Created;
        public bool Deleted;
        public bool IsDir;
        public long FirstTime;
        public long LastTime;
    }

    internal sealed class JournalResult
    {
        public bool Available;
        public string Problem;
        /// <summary>Timestamp of the oldest record still in the journal.</summary>
        public DateTime OldestUtc;
        public bool CoversWindow;
        public long RecordsRead;
        public Dictionary<ulong, JournalEntry> Entries = new Dictionary<ulong, JournalEntry>();
    }

    /// <summary>
    /// Reads the NTFS USN change journal: Windows' own log of every file created, changed, renamed or deleted.
    /// This is the same trick the "Everything" search tool uses, and it's why scans are fast.
    /// </summary>
    internal static class UsnJournal
    {
        public static JournalResult Read(string driveLetter, DateTime cutoffUtc, Action<string> progress, CancellationToken ct)
        {
            var result = new JournalResult();
            using (var volume = Native.CreateFileW(@"\\.\" + driveLetter, Native.GENERIC_READ, Native.FILE_SHARE_ALL,
                                                   IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero))
            {
                if (volume.IsInvalid)
                {
                    result.Problem = "Couldn't open the drive (error " + Marshal.GetLastWin32Error() + "). Is the app running as administrator?";
                    return result;
                }

                ulong journalId;
                long firstUsn, nextUsn;
                int err = Query(volume, out journalId, out firstUsn, out nextUsn);
                if (err != 0)
                {
                    result.Problem = err == Native.ERROR_JOURNAL_NOT_ACTIVE
                        ? "The change journal is turned off on this drive"
                        : "Couldn't read the change journal (error " + err + ")";
                    return result;
                }

                const int BufSize = 1 << 20;
                IntPtr inBuf = Marshal.AllocHGlobal(40);
                IntPtr outBuf = Marshal.AllocHGlobal(BufSize);
                try
                {
                    long cutoff = cutoffUtc.ToFileTimeUtc();
                    long start = firstUsn;
                    bool sawFirst = false;
                    int retries = 0;
                    var lastReport = DateTime.UtcNow;

                    while (start < nextUsn)
                    {
                        ct.ThrowIfCancellationRequested();

                        // READ_USN_JOURNAL_DATA_V0 (40 bytes) -> returns USN_RECORD_V2 records
                        Marshal.WriteInt64(inBuf, 0, start);                // StartUsn
                        Marshal.WriteInt32(inBuf, 8, unchecked((int)0xFFFFFFFF)); // ReasonMask: everything
                        Marshal.WriteInt32(inBuf, 12, 0);                   // ReturnOnlyOnClose
                        Marshal.WriteInt64(inBuf, 16, 0);                   // Timeout
                        Marshal.WriteInt64(inBuf, 24, 0);                   // BytesToWaitFor: return immediately
                        Marshal.WriteInt64(inBuf, 32, unchecked((long)journalId));

                        int got;
                        if (!Native.DeviceIoControl(volume, Native.FSCTL_READ_USN_JOURNAL, inBuf, 40, outBuf, BufSize, out got, IntPtr.Zero))
                        {
                            int e = Marshal.GetLastWin32Error();
                            if (e == Native.ERROR_JOURNAL_ENTRY_DELETED && retries++ < 5)
                            {
                                // Old records were recycled while we were reading; jump forward to the new oldest record.
                                ulong id2; long first2, next2;
                                if (Query(volume, out id2, out first2, out next2) == 0) start = Math.Max(start, first2);
                                continue;
                            }
                            if (e == Native.ERROR_HANDLE_EOF) break;
                            if (result.RecordsRead == 0)
                            {
                                result.Problem = "Couldn't read the change journal (error " + e + ")";
                                return result;
                            }
                            break;
                        }
                        if (got <= 8) break;

                        long next = Marshal.ReadInt64(outBuf, 0);
                        int off = 8;
                        while (off + 60 <= got)
                        {
                            int len = Marshal.ReadInt32(outBuf, off);
                            if (len <= 0 || off + len > got) break;
                            int major = (ushort)Marshal.ReadInt16(outBuf, off + 4);
                            if (major == 2)
                            {
                                long ts = Marshal.ReadInt64(outBuf, off + 32);
                                if (!sawFirst)
                                {
                                    sawFirst = true;
                                    result.OldestUtc = SafeFromFileTime(ts);
                                }
                                result.RecordsRead++;
                                if (ts >= cutoff) Add(result.Entries, outBuf, off, ts);
                            }
                            off += len;
                        }

                        if (next <= start) break;
                        start = next;

                        if (progress != null && (DateTime.UtcNow - lastReport).TotalMilliseconds > 250)
                        {
                            lastReport = DateTime.UtcNow;
                            progress(driveLetter + " reading change journal... " + result.RecordsRead.ToString("N0") + " records");
                        }
                    }

                    result.Available = true;
                    // Small tolerance: a journal whose oldest record is within 10 minutes of the cutoff is fine.
                    result.CoversWindow = sawFirst && result.OldestUtc <= cutoffUtc.AddMinutes(10);
                    if (!sawFirst) result.CoversWindow = true; // empty journal = nothing happened
                }
                finally
                {
                    Marshal.FreeHGlobal(inBuf);
                    Marshal.FreeHGlobal(outBuf);
                }
            }
            return result;
        }

        static int Query(SafeFileHandle volume, out ulong journalId, out long firstUsn, out long nextUsn)
        {
            journalId = 0; firstUsn = 0; nextUsn = 0;
            IntPtr q = Marshal.AllocHGlobal(128);
            try
            {
                int got;
                if (!Native.DeviceIoControl(volume, Native.FSCTL_QUERY_USN_JOURNAL, IntPtr.Zero, 0, q, 128, out got, IntPtr.Zero))
                    return Marshal.GetLastWin32Error();
                // USN_JOURNAL_DATA: UsnJournalID(0) FirstUsn(8) NextUsn(16) ...
                journalId = unchecked((ulong)Marshal.ReadInt64(q, 0));
                firstUsn = Marshal.ReadInt64(q, 8);
                nextUsn = Marshal.ReadInt64(q, 16);
                return 0;
            }
            finally
            {
                Marshal.FreeHGlobal(q);
            }
        }

        // USN_RECORD_V2 layout:
        //  0 RecordLength(4) 4 Major(2) 6 Minor(2) 8 FileReferenceNumber(8) 16 ParentFileReferenceNumber(8)
        // 24 Usn(8) 32 TimeStamp(8) 40 Reason(4) 44 SourceInfo(4) 48 SecurityId(4) 52 FileAttributes(4)
        // 56 FileNameLength(2) 58 FileNameOffset(2) 60 FileName
        static void Add(Dictionary<ulong, JournalEntry> entries, IntPtr buf, int off, long ts)
        {
            ulong frn = unchecked((ulong)Marshal.ReadInt64(buf, off + 8));
            ulong parent = unchecked((ulong)Marshal.ReadInt64(buf, off + 16));
            uint reason = unchecked((uint)Marshal.ReadInt32(buf, off + 40));
            uint attrs = unchecked((uint)Marshal.ReadInt32(buf, off + 52));

            JournalEntry e;
            if (!entries.TryGetValue(frn, out e))
            {
                // Only start tracking a file when something happened that can change disk usage.
                // (Attribute, permission and close-only records are by far the most common and don't matter here.)
                const uint interesting = Native.USN_REASON_FILE_CREATE | Native.USN_REASON_FILE_DELETE |
                                         Native.USN_REASON_DATA_EXTEND | Native.USN_REASON_NAMED_DATA_EXTEND;
                if ((reason & interesting) == 0) return;
                e = new JournalEntry { Frn = frn, FirstTime = ts };
                entries[frn] = e;
            }
            e.ParentFrn = parent;
            e.Reasons |= reason;
            e.LastTime = ts;
            e.IsDir = (attrs & Native.FILE_ATTRIBUTE_DIRECTORY) != 0;
            if ((reason & Native.USN_REASON_FILE_CREATE) != 0) e.Created = true;
            if ((reason & Native.USN_REASON_FILE_DELETE) != 0) e.Deleted = true;

            // Only decode the name when it may have changed, to keep memory churn down.
            if (e.Name == null || (reason & (Native.USN_REASON_FILE_CREATE | Native.USN_REASON_RENAME_NEW_NAME)) != 0)
            {
                int nameLen = (ushort)Marshal.ReadInt16(buf, off + 56);
                int nameOff = (ushort)Marshal.ReadInt16(buf, off + 58);
                e.Name = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + off + nameOff), nameLen / 2);
            }
        }

        static DateTime SafeFromFileTime(long ft)
        {
            try { return DateTime.FromFileTimeUtc(ft); }
            catch { return DateTime.MinValue; }
        }
    }

    /// <summary>Opens files by their NTFS file ID to get their current size and full path.</summary>
    internal sealed class FileIdResolver : IDisposable
    {
        readonly SafeFileHandle hint;
        readonly Dictionary<ulong, string> dirCache = new Dictionary<ulong, string>();
        readonly StringBuilder sb = new StringBuilder(1024);

        public FileIdResolver(string root)
        {
            hint = Native.CreateFileW(root, Native.FILE_READ_ATTRIBUTES, Native.FILE_SHARE_ALL, IntPtr.Zero,
                                      Native.OPEN_EXISTING, Native.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        }

        public bool Ok { get { return !hint.IsInvalid; } }

        public SafeFileHandle Open(ulong frn)
        {
            var id = new Native.FILE_ID_DESCRIPTOR
            {
                dwSize = (uint)Marshal.SizeOf(typeof(Native.FILE_ID_DESCRIPTOR)),
                Type = 0, // FileIdType
                FileId = unchecked((long)frn)
            };
            return Native.OpenFileById(hint, ref id, Native.FILE_READ_ATTRIBUTES, Native.FILE_SHARE_ALL, IntPtr.Zero,
                                       Native.FILE_FLAG_BACKUP_SEMANTICS);
        }

        /// <summary>Current allocated size (size on disk). Returns false if the file can't be opened or is a folder.</summary>
        public bool TryGetSize(ulong frn, out long allocated, out DateTime createdUtc)
        {
            allocated = 0;
            createdUtc = DateTime.MinValue;
            using (var h = Open(frn))
            {
                if (h.IsInvalid) return false;
                Native.FILE_STANDARD_INFO si;
                if (!Native.GetFileInformationByHandleEx(h, Native.FileStandardInfo, out si, Marshal.SizeOf(typeof(Native.FILE_STANDARD_INFO))))
                    return false;
                if (si.Directory != 0) return false;
                allocated = si.AllocationSize;
                Native.FILE_BASIC_INFO bi;
                if (Native.GetFileInformationByHandleEx(h, Native.FileBasicInfo, out bi, Marshal.SizeOf(typeof(Native.FILE_BASIC_INFO))))
                {
                    try { createdUtc = DateTime.FromFileTimeUtc(bi.CreationTime); } catch { }
                }
                return true;
            }
        }

        /// <summary>Full path of a folder from its file ID (cached), or null.</summary>
        public string FolderPath(ulong frn)
        {
            string path;
            if (dirCache.TryGetValue(frn, out path)) return path;
            path = null;
            using (var h = Open(frn))
            {
                if (!h.IsInvalid) path = FinalPath(h);
            }
            dirCache[frn] = path;
            return path;
        }

        string FinalPath(SafeFileHandle h)
        {
            sb.Clear();
            uint n = Native.GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
            if (n == 0) return null;
            if (n >= sb.Capacity)
            {
                sb.Capacity = (int)n + 1;
                sb.Clear();
                n = Native.GetFinalPathNameByHandleW(h, sb, (uint)sb.Capacity, 0);
                if (n == 0 || n >= sb.Capacity) return null;
            }
            return Native.StripLongPrefix(sb.ToString());
        }

        public void Dispose()
        {
            hint.Dispose();
        }
    }
}
