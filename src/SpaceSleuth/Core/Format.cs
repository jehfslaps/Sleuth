using System;
using System.Globalization;

namespace SpaceSleuth.Core
{
    public static class Format
    {
        /// <summary>Windows-style sizes: 1 GB = 1024^3 bytes, matching what Explorer shows.</summary>
        public static string Bytes(long bytes)
        {
            bool neg = bytes < 0;
            double b = Math.Abs((double)bytes);
            string s;
            if (b < 1024) s = b.ToString("0", CultureInfo.CurrentCulture) + " B";
            else if (b < 1024.0 * 1024) s = (b / 1024).ToString("0", CultureInfo.CurrentCulture) + " KB";
            else if (b < 1024.0 * 1024 * 1024)
            {
                double mb = b / (1024.0 * 1024);
                s = mb.ToString(mb < 10 ? "0.0" : "0", CultureInfo.CurrentCulture) + " MB";
            }
            else if (b < 1024.0 * 1024 * 1024 * 1024) s = (b / (1024.0 * 1024 * 1024)).ToString("0.0", CultureInfo.CurrentCulture) + " GB";
            else s = (b / (1024.0 * 1024 * 1024 * 1024)).ToString("0.00", CultureInfo.CurrentCulture) + " TB";
            return neg ? "-" + s : s;
        }

        public static string SignedBytes(long bytes)
        {
            return bytes > 0 ? "+" + Bytes(bytes) : bytes < 0 ? "−" + Bytes(-bytes) : "no change";
        }

        public static string Percent(double share)
        {
            if (share <= 0) return "0%";
            if (share < 0.01) return "<1%";
            return Math.Round(share * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
        }

        /// <summary>"just now", "25 min ago", "3 hours ago (2:14 PM)", "Tue 3:12 PM", "Oct 12, 3:12 PM".</summary>
        public static string When(DateTime utc)
        {
            return When(utc, DateTime.UtcNow);
        }

        public static string When(DateTime utc, DateTime nowUtc)
        {
            if (utc == default(DateTime)) return "";
            var local = utc.ToLocalTime();
            var ago = nowUtc - utc;
            string clock = local.ToString("h:mm tt", CultureInfo.CurrentCulture);
            if (ago.TotalMinutes < 1) return "just now";
            if (ago.TotalMinutes < 60) return (int)ago.TotalMinutes + " min ago";
            if (ago.TotalHours < 24)
            {
                int h = (int)ago.TotalHours;
                return h + (h == 1 ? " hour ago (" : " hours ago (") + clock + ")";
            }
            if (ago.TotalDays < 7) return local.ToString("ddd ", CultureInfo.CurrentCulture) + clock;
            return local.ToString("MMM d, ", CultureInfo.CurrentCulture) + clock;
        }

        public static string Window(TimeSpan span)
        {
            if (span.TotalHours < 24) return "last " + (int)Math.Round(span.TotalHours) + " hours";
            if (Math.Abs(span.TotalHours - 24) < 0.5) return "last 24 hours";
            if (span.TotalHours < 72) return "last " + (int)Math.Round(span.TotalHours) + " hours";
            return "last " + (int)Math.Round(span.TotalDays) + " days";
        }
    }
}
