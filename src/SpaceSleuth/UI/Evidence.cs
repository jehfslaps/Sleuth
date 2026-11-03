using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>
    /// "The evidence": C:'s free space at the start of the look-back window against now, how it was measured,
    /// and what was added, by category.
    /// </summary>
    internal sealed class Evidence : Painted
    {
        public enum Mode { NoDrive, FirstLook, NoRecord, Scanning, Result }

        public Mode State = Mode.NoDrive;
        public string Drive = "C:";
        public long NowFree, TotalBytes;
        public long? WasFree;
        public DateTime? WasUtc;
        /// <summary>Oldest reading on record, for when nothing reaches back to the start of the window.</summary>
        public DateTime? EarliestUtc;
        public string Method;
        public long Traced, NotTraced;
        public string Footnote;
        public readonly Dictionary<Category, long> Added = new Dictionary<Category, long>();

        long Drop { get { return WasFree.HasValue ? WasFree.Value - NowFree : 0; } }

        protected override void Draw(Graphics g)
        {
            float W = Width, H = Height;
            float y = 0;
            Func<float, bool> room = need => y + need <= H;

            // head
            Theme.Caps(g, "the evidence", Theme.Fg, 0, Theme.S(10), 14, 1.2f);
            Theme.LabelRight(g, "01", Theme.Mono(12), Theme.Muted, W, Theme.S(10));
            y = Theme.S(40);

            if (State == Mode.NoDrive)
            {
                string t = "Before → now is kept for " + Drive + " only. Pick all drives or " + Drive + " to see how its free space moved.";
                float th = Theme.WrappedHeight(g, t, Theme.Sans(14), W);
                Theme.Text(g, t, Theme.Sans(14), Theme.Muted, new RectangleF(0, y, W, th + Theme.S(4)), StringAlignment.Near, StringAlignment.Near, false, true);
                y += th + Theme.S(20);
                Categories(g, ref y, W, H);
                return;
            }

            // capacity
            float cy = y + Theme.S(10);
            Theme.Label(g, "drive capacity", Theme.Sans(16), Theme.Muted, 0, cy);
            Theme.LabelRight(g, TotalBytes > 0 ? Format.Bytes(TotalBytes) : "—", Theme.Mono(17, true), Theme.Fg, W, cy);
            y += Theme.S(32);

            // before
            bool known = WasFree.HasValue;
            FreeBlock(g, ref y, W, "before / free", known ? Format.Bytes(WasFree.Value) : "—", known && TotalBytes > 0 ? (double)WasFree.Value / TotalBytes : 0, true);

            // the change
            string change;
            if (!known)
                change = State == Mode.NoRecord && EarliestUtc.HasValue ? "no reading this far back · earliest " + Theme.Day(EarliestUtc.Value)
                       : "first reading · next time shows the change";
            else if (State == Mode.Scanning && Drop == 0) change = "↓  measuring…";
            else if (Drop > 0) change = "↓  " + Format.Bytes(Drop) + " less free";
            else if (Drop < 0) change = "↑  " + Format.Bytes(-Drop) + " more free";
            else change = "=  no change in free space";
            Theme.Text(g, change, Theme.Mono(12), Theme.Muted, new RectangleF(0, y - Theme.S(4), W, Theme.S(16)));
            y += Theme.S(22);

            // now
            FreeBlock(g, ref y, W, "now / free", Format.Bytes(NowFree), TotalBytes > 0 ? (double)NowFree / TotalBytes : 0, false);

            // how it was measured
            var lines = new List<string>();
            if (State == Mode.Scanning)
            {
                lines.Add(known ? "measured " + Theme.Ago(WasUtc.Value) : "no earlier reading");
                if (known) lines.Add(Theme.Day(WasUtc.Value));
                lines.Add("investigating…");
            }
            else if (known)
            {
                lines.Add("measured " + Theme.Ago(WasUtc.Value));
                lines.Add(Theme.Day(WasUtc.Value));
                if (!string.IsNullOrEmpty(Method)) lines.Add("snapshot + " + Theme.Lc(Method));
                if (State == Mode.Result && Drop > 0) lines.Add("traced " + Format.Bytes(Traced) + " · not traced " + Format.Bytes(NotTraced));
            }
            else
            {
                lines.Add(State == Mode.NoRecord ? "sleuth can only compare against" : "first look at " + Drive);
                lines.Add(State == Mode.NoRecord ? "times it ran" : "reading saved for next time");
                if (!string.IsNullOrEmpty(Method)) lines.Add(Theme.Lc(Method));
            }
            float mh = Theme.S(12) + lines.Count * Theme.S(18);
            if (room(mh))
            {
                Theme.HLine(g, Theme.Line, 0, W, y);
                y += Theme.S(12);
                foreach (var l in lines)
                {
                    Theme.Text(g, l, Theme.Mono(12), Theme.Muted, new RectangleF(0, y, W, Theme.S(18)));
                    y += Theme.S(18);
                }
            }

            if (!string.IsNullOrEmpty(Footnote))
            {
                float fh = Theme.WrappedHeight(g, Theme.Lc(Footnote), Theme.Mono(11), W);
                float catNeed = Theme.S(16 + 16 + 6 * 23);
                if (room(Theme.S(8) + fh + catNeed))
                {
                    y += Theme.S(8);
                    Theme.Text(g, Theme.Lc(Footnote), Theme.Mono(11), Theme.Muted, new RectangleF(0, y, W, fh + Theme.S(2)), StringAlignment.Near, StringAlignment.Near, false, true);
                    y += fh;
                }
            }

            Categories(g, ref y, W, H);
        }

        void FreeBlock(Graphics g, ref float y, float W, string caption, string value, double fraction, bool before)
        {
            string num, unit;
            Theme.SplitBytes(value, out num, out unit);
            var big = Theme.Display(36, 500);
            var small = Theme.Display(20, 500);
            float baseline = y + Theme.S(32);
            float uw = unit.Length > 0 ? Theme.Width(g, unit, small) : 0;
            float x = W - uw;
            if (unit.Length > 0) Theme.OnBaseline(g, unit, small, Theme.Fg, x, baseline);
            float nw = Theme.Width(g, num, big);
            Theme.OnBaseline(g, num, big, Theme.Fg, x - (unit.Length > 0 ? Theme.S(6) : 0) - nw, baseline);
            var capsFont = Theme.Mono(12, true);
            Theme.Caps(g, caption, before ? Theme.Muted : Theme.Fg, 0, baseline - Theme.Ascent(capsFont) / 2 + Theme.S(1), 12, 1.5f);
            y += Theme.S(40) + Theme.S(7);
            Theme.Meter(g, 0, y, W, Theme.S(6), fraction, Theme.Subtle, before ? Theme.Muted : Theme.Fg);
            y += Theme.S(6) + Theme.S(12);
        }

        void Categories(Graphics g, ref float y, float W, float H)
        {
            if (y + Theme.S(16 + 22 + 23) > H) return;
            y += Theme.S(16);
            float cy = y + Theme.S(8);
            Theme.Label(g, State == Mode.Scanning ? "added / by category (last case)" : "added / by category", Theme.Mono(12), Theme.Muted, 0, cy);
            Theme.LabelRight(g, "size", Theme.Mono(12), Theme.Muted, W, cy);
            y += Theme.S(16 + 6);
            foreach (Category cat in Enum.GetValues(typeof(Category)))
            {
                if (y + Theme.S(23) > H) break;
                float rcy = y + Theme.S(11.5f);
                Theme.Pattern(g, cat, new RectangleF(0, rcy - Theme.S(6), Theme.S(12), Theme.S(12)));
                Theme.Label(g, Theme.CategoryWord(cat), Theme.Sans(14), Theme.Fg, Theme.S(22), rcy);
                long v;
                Added.TryGetValue(cat, out v);
                Theme.LabelRight(g, v > 0 ? Format.Bytes(v) : "—", Theme.Mono(12), v > 0 ? Theme.Fg : Theme.Muted, W, rcy);
                y += Theme.S(23);
            }
        }
    }
}
