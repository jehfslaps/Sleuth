using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using SpaceSleuth.Core;

namespace SpaceSleuth.Windows
{
    /// <summary>What the Volume Shadow Copy service and System Restore report.</summary>
    internal sealed class ShadowInfo
    {
        public readonly Dictionary<string, long> UsedByVolume = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, long> MaxByVolume = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<DateTime>> CreatedByVolume = new Dictionary<string, List<DateTime>>(StringComparer.OrdinalIgnoreCase);
        public readonly List<KeyValuePair<DateTime, string>> RestorePoints = new List<KeyValuePair<DateTime, string>>();
        public string Problem;
    }

    /// <summary>
    /// Space users that aren't ordinary files: restore points / shadow copies, the hibernation file,
    /// the page file, the swap file, crash dumps and the Recycle Bin.
    /// </summary>
    internal static class SystemAreas
    {
        static readonly Regex GuidPattern = new Regex(@"\{[0-9a-fA-F\-]{36}\}", RegexOptions.Compiled);

        static string GuidOf(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = GuidPattern.Match(s);
            return m.Success ? m.Value.ToUpperInvariant() : null;
        }

        public static ShadowInfo LoadShadowInfo()
        {
            var info = new ShadowInfo();
            try
            {
                using (var q = new ManagementObjectSearcher(@"root\cimv2", "SELECT DiffVolume, UsedSpace, MaxSpace FROM Win32_ShadowStorage"))
                using (var results = q.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            // Space is consumed on the "diff area" volume, which is usually the same drive.
                            string g = GuidOf(mo["DiffVolume"] as string);
                            if (g == null) continue;
                            long used = Convert.ToInt64(mo["UsedSpace"] ?? 0L);
                            long max = Convert.ToInt64(mo["MaxSpace"] ?? 0L);
                            long cur;
                            info.UsedByVolume.TryGetValue(g, out cur);
                            info.UsedByVolume[g] = cur + used;
                            info.MaxByVolume.TryGetValue(g, out cur);
                            info.MaxByVolume[g] = cur + max;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                info.Problem = "Couldn't read restore point storage: " + ex.Message;
            }

            try
            {
                using (var q = new ManagementObjectSearcher(@"root\cimv2", "SELECT InstallDate, VolumeName FROM Win32_ShadowCopy"))
                using (var results = q.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            string g = GuidOf(mo["VolumeName"] as string);
                            string when = mo["InstallDate"] as string;
                            if (g == null || string.IsNullOrEmpty(when)) continue;
                            List<DateTime> list;
                            if (!info.CreatedByVolume.TryGetValue(g, out list)) info.CreatedByVolume[g] = list = new List<DateTime>();
                            list.Add(ManagementDateTimeConverter.ToDateTime(when).ToUniversalTime());
                        }
                    }
                }
            }
            catch
            {
                // Shadow copy list is optional detail.
            }

            try
            {
                var scope = new ManagementScope(@"\\.\root\default");
                using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT Description, CreationTime FROM SystemRestore")))
                using (var results = q.Get())
                {
                    foreach (ManagementObject mo in results)
                    {
                        using (mo)
                        {
                            string when = mo["CreationTime"] as string;
                            if (string.IsNullOrEmpty(when)) continue;
                            info.RestorePoints.Add(new KeyValuePair<DateTime, string>(
                                ManagementDateTimeConverter.ToDateTime(when).ToUniversalTime(),
                                (mo["Description"] as string) ?? "Restore point"));
                        }
                    }
                }
            }
            catch
            {
                // System Restore may be off; that's fine.
            }
            return info;
        }

        public static List<SystemArea> Collect(string drive, bool isSystemDrive, DateTime cutoffUtc, History history,
                                               ShadowInfo shadows, ICollection<string> touchedAreaFiles)
        {
            var list = new List<SystemArea>();
            string root = drive + "\\";

            // Restore points and shadow copies
            string guid = GuidOf(Native.VolumeGuidPath(root));
            if (guid != null && shadows != null)
            {
                long used, max;
                bool hasStorage = shadows.UsedByVolume.TryGetValue(guid, out used);
                shadows.MaxByVolume.TryGetValue(guid, out max);
                List<DateTime> created;
                shadows.CreatedByVolume.TryGetValue(guid, out created);
                var recent = (created ?? new List<DateTime>()).Where(t => t >= cutoffUtc).OrderBy(t => t).ToList();

                // Named restore points (System Restore keeps these on the Windows drive). Whether you made one
                // yourself or Windows made it automatically before an update, it shows up the same way.
                var rps = isSystemDrive
                    ? shadows.RestorePoints.Where(r => r.Key >= cutoffUtc).OrderByDescending(r => r.Key).ToList()
                    : new List<KeyValuePair<DateTime, string>>();
                int newCount = Math.Max(recent.Count, rps.Count);

                if (hasStorage || newCount > 0)
                {
                    var activity = new List<string>();
                    if (recent.Count > 0)
                        activity.Add(recent.Count + (recent.Count == 1 ? " new restore point / shadow copy" : " new restore points / shadow copies")
                                     + " in this window (latest " + Format.When(recent.Last()) + ")");
                    if (rps.Count > 0)
                        activity.Add("Restore points: " + string.Join("; ", rps.Take(5).Select(r => "\"" + r.Value + "\" " + Format.When(r.Key))));
                    var area = Make(drive, "shadow", "System Restore points / shadow copies", used, history,
                        "Windows keeps restore points and shadow copies in a hidden area of the drive" +
                        (max > 0 ? ", and is allowed to use up to " + Format.Bytes(max) + " for them" : "") +
                        ". Manage or shrink this in System Protection > Configure.",
                        "SystemPropertiesProtection.exe",
                        string.Join(". ", activity), newCount > 0);
                    area.NewInWindow = newCount;
                    area.TotalCount = Math.Max(created == null ? 0 : created.Count, isSystemDrive ? shadows.RestorePoints.Count : 0);
                    var latest = recent.Concat(rps.Select(r => r.Key)).DefaultIfEmpty().Max();
                    if (newCount > 0 && latest != default(DateTime)) area.LatestNewUtc = latest;
                    list.Add(area);
                }
            }

            AddFile(list, drive, root + "hiberfil.sys", "hiberfil", "Hibernation file (hiberfil.sys)", history, touchedAreaFiles,
                "Reserved by Windows for Hibernate and Fast Startup; its size follows your RAM. Running \"powercfg /h off\" in an admin terminal removes it (and turns off Hibernate).",
                null);
            AddFile(list, drive, root + "pagefile.sys", "pagefile", "Page file (pagefile.sys)", history, touchedAreaFiles,
                "Virtual memory. Windows grows it automatically when memory runs low and may shrink it after a restart.",
                "SystemPropertiesPerformance.exe");
            AddFile(list, drive, root + "swapfile.sys", "swapfile", "Swap file (swapfile.sys)", history, touchedAreaFiles,
                "Used by Microsoft Store apps. Usually small and managed by Windows.",
                null);
            if (isSystemDrive)
            {
                AddFile(list, drive, root + @"Windows\MEMORY.DMP", "memorydmp", "Crash dump (MEMORY.DMP)", history, touchedAreaFiles,
                    "Saved after a blue-screen crash. Disk Cleanup can remove it (\"System error memory dump files\").",
                    "cleanmgr.exe");
            }

            // Recycle Bin
            try
            {
                var rb = new Native.SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(Native.SHQUERYRBINFO)) };
                if (Native.SHQueryRecycleBinW(root, ref rb) == 0 && rb.i64Size > 0)
                {
                    list.Add(Make(drive, "recyclebin", "Recycle Bin", rb.i64Size, history,
                        rb.i64NumItems.ToString("N0") + " deleted items still using space. Empty the Recycle Bin to get it back.",
                        "shell:RecycleBinFolder", null, false));
                }
            }
            catch
            {
            }

            return list;
        }

        static void AddFile(List<SystemArea> list, string drive, string path, string key, string name, History history,
                            ICollection<string> touched, string explanation, string open)
        {
            long size;
            if (!FileSize(path, out size)) return;
            bool wasTouched = touched != null && touched.Contains(path);
            list.Add(Make(drive, key, name, size, history, explanation, open,
                          wasTouched ? "Windows wrote to it during this window" : null, wasTouched));
        }

        static SystemArea Make(string drive, string key, string name, long current, History history, string explanation,
                               string open, string activity, bool active)
        {
            var a = new SystemArea
            {
                Drive = drive, Key = key, Name = name, CurrentBytes = current, Explanation = explanation,
                OpenTarget = open, Activity = activity, ActiveInWindow = active
            };
            long prev; DateTime prevUtc;
            if (history != null && history.TryGetArea(drive, key, out prev, out prevUtc))
            {
                a.PreviousBytes = prev;
                a.PreviousUtc = prevUtc;
            }
            return a;
        }

        /// <summary>Size of a file even when it's locked by Windows (pagefile, hiberfil).</summary>
        static bool FileSize(string path, out long size)
        {
            size = 0;
            Native.WIN32_FIND_DATAW fd;
            IntPtr h = Native.FindFirstFileExW(@"\\?\" + path, Native.FindExInfoBasic, out fd, Native.FindExSearchNameMatch, IntPtr.Zero, 0);
            if (h == Native.INVALID_HANDLE_VALUE) return false;
            Native.FindClose(h);
            size = ((long)fd.nFileSizeHigh << 32) | fd.nFileSizeLow;
            return true;
        }
    }
}
