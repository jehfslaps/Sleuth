using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SpaceSleuth.Windows
{
    /// <summary>Win32 API declarations. Struct layouts match the 64-bit Windows headers.</summary>
    internal static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint FILE_READ_ATTRIBUTES = 0x0080;
        public const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4; // read | write | delete
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        public const uint FSCTL_QUERY_USN_JOURNAL = 0x000900F4;
        public const uint FSCTL_READ_USN_JOURNAL = 0x000900BB;

        public const int ERROR_HANDLE_EOF = 38;
        public const int ERROR_JOURNAL_DELETE_IN_PROGRESS = 1178;
        public const int ERROR_JOURNAL_NOT_ACTIVE = 1179;
        public const int ERROR_JOURNAL_ENTRY_DELETED = 1181;

        public const uint USN_REASON_DATA_EXTEND = 0x00000002;
        public const uint USN_REASON_NAMED_DATA_EXTEND = 0x00000020;
        public const uint USN_REASON_FILE_CREATE = 0x00000100;
        public const uint USN_REASON_FILE_DELETE = 0x00000200;
        public const uint USN_REASON_RENAME_NEW_NAME = 0x00002000;

        public const uint FILE_ATTRIBUTE_HIDDEN = 0x2;
        public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x400;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle hDevice, uint dwIoControlCode, IntPtr lpInBuffer, int nInBufferSize,
            IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

        // FILE_ID_DESCRIPTOR: DWORD dwSize; FILE_ID_TYPE Type; union { LARGE_INTEGER FileId; GUID ObjectId; FILE_ID_128 ExtendedFileId; } -> 24 bytes
        [StructLayout(LayoutKind.Sequential)]
        public struct FILE_ID_DESCRIPTOR
        {
            public uint dwSize;
            public int Type;
            public long FileId;
            public long FileIdHigh;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeFileHandle OpenFileById(SafeFileHandle hVolumeHint, ref FILE_ID_DESCRIPTOR lpFileId,
            uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwFlagsAndAttributes);

        public const int FileBasicInfo = 0;
        public const int FileStandardInfo = 1;

        // FILE_STANDARD_INFO: 24 bytes on x64
        [StructLayout(LayoutKind.Sequential)]
        public struct FILE_STANDARD_INFO
        {
            public long AllocationSize;
            public long EndOfFile;
            public uint NumberOfLinks;
            public byte DeletePending;
            public byte Directory;
        }

        // FILE_BASIC_INFO: 40 bytes on x64
        [StructLayout(LayoutKind.Sequential)]
        public struct FILE_BASIC_INFO
        {
            public long CreationTime;
            public long LastAccessTime;
            public long LastWriteTime;
            public long ChangeTime;
            public uint FileAttributes;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetFileInformationByHandleEx(SafeFileHandle hFile, int FileInformationClass,
            out FILE_STANDARD_INFO lpFileInformation, int dwBufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetFileInformationByHandleEx(SafeFileHandle hFile, int FileInformationClass,
            out FILE_BASIC_INFO lpFileInformation, int dwBufferSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetVolumeNameForVolumeMountPointW(string lpszVolumeMountPoint, StringBuilder lpszVolumeName, uint cchBufferLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetVolumeInformationW(string lpRootPathName, StringBuilder lpVolumeNameBuffer, int nVolumeNameSize,
            out uint lpVolumeSerialNumber, out uint lpMaximumComponentLength, out uint lpFileSystemFlags,
            StringBuilder lpFileSystemNameBuffer, int nFileSystemNameSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetDiskFreeSpaceExW(string lpDirectoryName, out ulong lpFreeBytesAvailable,
            out ulong lpTotalNumberOfBytes, out ulong lpTotalNumberOfFreeBytes);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool GetDiskFreeSpaceW(string lpRootPathName, out uint lpSectorsPerCluster, out uint lpBytesPerSector,
            out uint lpNumberOfFreeClusters, out uint lpTotalNumberOfClusters);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFileAttributesW(string lpFileName);

        // WIN32_FIND_DATAW. FILETIMEs are split into two DWORDs to keep 4-byte alignment exactly like the C struct.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public uint ftCreationTimeLow;
            public uint ftCreationTimeHigh;
            public uint ftLastAccessTimeLow;
            public uint ftLastAccessTimeHigh;
            public uint ftLastWriteTimeLow;
            public uint ftLastWriteTimeHigh;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        public const int FindExInfoBasic = 1;
        public const int FindExSearchNameMatch = 0;
        public const int FIND_FIRST_EX_LARGE_FETCH = 2;
        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileExW(string lpFileName, int fInfoLevelId, out WIN32_FIND_DATAW lpFindFileData,
            int fSearchOp, IntPtr lpSearchFilter, int dwAdditionalFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool FindClose(IntPtr hFindFile);

        // SHQUERYRBINFO: 24 bytes on x64 (natural alignment)
        [StructLayout(LayoutKind.Sequential)]
        public struct SHQUERYRBINFO
        {
            public int cbSize;
            public long i64Size;
            public long i64NumItems;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

        // ---- Privileges ----

        const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
        const uint TOKEN_QUERY = 0x0008;
        const uint SE_PRIVILEGE_ENABLED = 0x00000002;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct TOKEN_PRIVILEGES_ONE
        {
            public uint PrivilegeCount;
            public long Luid;
            public uint Attributes;
        }

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool LookupPrivilegeValueW(string lpSystemName, string lpName, out long lpLuid);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES_ONE NewState,
            int BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        /// <summary>
        /// SeBackupPrivilege lets an administrator read file sizes and names in folders whose permissions
        /// would otherwise block them (other user profiles, some ProgramData folders).
        /// </summary>
        public static bool EnablePrivilege(string name)
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token)) return false;
            try
            {
                long luid;
                if (!LookupPrivilegeValueW(null, name, out luid)) return false;
                var tp = new TOKEN_PRIVILEGES_ONE { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
                if (!AdjustTokenPrivileges(token, false, ref tp, Marshal.SizeOf(typeof(TOKEN_PRIVILEGES_ONE)), IntPtr.Zero, IntPtr.Zero))
                    return false;
                return Marshal.GetLastWin32Error() == 0;
            }
            finally
            {
                CloseHandle(token);
            }
        }

        public static bool IsElevated()
        {
            try
            {
                using (var id = WindowsIdentity.GetCurrent())
                    return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        // ---- Small helpers ----

        public static string FileSystemName(string root)
        {
            var fs = new StringBuilder(64);
            var label = new StringBuilder(261);
            uint serial, maxLen, flags;
            return GetVolumeInformationW(root, label, label.Capacity, out serial, out maxLen, out flags, fs, fs.Capacity) ? fs.ToString() : "";
        }

        public static string VolumeLabel(string root)
        {
            var fs = new StringBuilder(64);
            var label = new StringBuilder(261);
            uint serial, maxLen, flags;
            return GetVolumeInformationW(root, label, label.Capacity, out serial, out maxLen, out flags, fs, fs.Capacity) ? label.ToString() : "";
        }

        public static long ClusterSize(string root)
        {
            uint spc, bps, free, total;
            return GetDiskFreeSpaceW(root, out spc, out bps, out free, out total) ? (long)spc * bps : 4096;
        }

        public static bool FreeSpace(string root, out long free, out long total)
        {
            ulong avail, tot, totFree;
            if (GetDiskFreeSpaceExW(root, out avail, out tot, out totFree))
            {
                free = (long)totFree;
                total = (long)tot;
                return true;
            }
            free = total = 0;
            return false;
        }

        /// <summary>"\\?\Volume{guid}\" for a drive root, or null.</summary>
        public static string VolumeGuidPath(string root)
        {
            var sb = new StringBuilder(64);
            return GetVolumeNameForVolumeMountPointW(root, sb, (uint)sb.Capacity) ? sb.ToString() : null;
        }

        public static string StripLongPrefix(string p)
        {
            if (p == null) return null;
            if (p.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) return @"\\" + p.Substring(8);
            if (p.StartsWith(@"\\?\", StringComparison.Ordinal)) return p.Substring(4);
            return p;
        }

        public static long FileTimeToLong(uint low, uint high)
        {
            return ((long)high << 32) | low;
        }
    }
}
