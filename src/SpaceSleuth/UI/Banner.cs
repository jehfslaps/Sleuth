using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>
    /// The big "C: was 245 GB free → now 237 GB (−8 GB)" card, with the most-likely sentence and a bar that
    /// splits the drop into where it went, plus whatever couldn't be traced ("Not found yet").
    /// </summary>
    internal sealed class DriveBanner : Painted, IAnimated
    {
        public sealed class Slice
        {
            public string Name;
            public long Bytes;
            public Hue Hue;
            public bool Estimate;
        }

        public enum Mode { Hidden, FirstLook, Scanning, Result }

        public Mode State = Mode.Hidden;
        public string Drive = "C:";
        public long NowFree;
        public long? WasFree;
        public DateTime? WasUtc;

        // The "Most likely, [6.1 GB] went to Cinematic Strings" sentence (Result mode)
        public string Amount, Target, Sentence;

        public readonly List<Slice> Slices = new List<Slice>();
        public long NotFound;
        public string Footnote;

        long Drop { get { return WasFree.HasValue ? WasFree.Value - NowFree : 0; } }
        bool Lost { get { return Drop > 0; } }

        float Pad { get { return Theme.S(32); } }

        public int Measure(int width)
        {
            switch (State)
            {
                case Mode.Hidden: return 0;
                case Mode.FirstLook: return Theme.Si(124);
                case Mode.Scanning: return Theme.Si(WasFree.HasValue && Lost ? 194 : 140);
                default:
                    if (!WasFree.HasValue) return Theme.Si(124);
                    if (!Lost) return Theme.Si(string.IsNullOrEmpty(Footnote) ? 150 : 182);
                    return Theme.Si(string.IsNullOrEmpty(Footnote) ? 244 : 268);
            }
        }

        public void Tick(float now)
        {
            if (State == Mode.Scanning && !Theme.ReducedMotion) Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            if (State == Mode.Hidden) return;
            var card = new RectangleF(Pad, Theme.S(4), Width - Pad * 2, Height - Theme.S(14));
            Theme.Card(g, Theme.White, card, Theme.S(28));
            float x = card.X + Theme.S(26), right = card.Right - Theme.S(26);
            float y = card.Y + Theme.S(18);

            Theme.Text(g, Drive + " DRIVE · FREE SPACE", Theme.Ui(12, Weight.Semibold), Theme.InkA(140), new RectangleF(x, y, right - x, Theme.S(18)));
            y += Theme.S(24);

            if (State == Mode.FirstLook || (State == Mode.Result && !WasFree.HasValue) || (State == Mode.Scanning && !WasFree.HasValue))
            {
                float nw = DrawNumber(g, x, y, NowFree, "right now");
                Theme.Text(g, State == Mode.Scanning
                        ? "This is your starting point. Next time you open Space Sleuth, you'll see exactly what changed and where it went."
                        : "Saved as your starting point. Next time you open Space Sleuth, you'll see exactly what changed and where it went.",
                    Theme.Ui(14, Weight.Regular), Theme.InkA(190),
                    new RectangleF(x + nw + Theme.S(28), y + Theme.S(2), right - x - nw - Theme.S(28), Theme.S(50)),
                    StringAlignment.Near, StringAlignment.Center, false, true);
                return;
            }

            // Was → Now, then the change pill
            float cx = x;
            cx += DrawNumber(g, cx, y, WasFree.Value, "was · " + Format.When(WasUtc ?? DateTime.UtcNow));
            cx += Theme.S(14);
            DrawArrow(g, cx, y + Theme.S(20), Theme.S(28));
            cx += Theme.S(28) + Theme.S(16);
            cx += DrawNumber(g, cx, y, NowFree, "now");
            cx += Theme.S(20);
            long change = NowFree - WasFree.Value;
            var pillFont = Theme.Display(18);
            string pillText = change == 0 ? "no change" : Format.SignedBytes(change);
            float pw = Theme.Width(g, pillText, pillFont) + Theme.S(32);
            var pill = new RectangleF(cx, y + Theme.S(4), pw, Theme.S(36));
            if (change < 0)
            {
                Theme.Fill(g, Theme.Coral, pill, pill.Height / 2);
                Theme.Text(g, pillText, pillFont, Color.White, pill, StringAlignment.Center);
            }
            else
            {
                Theme.Fill(g, Theme.HMint.Tint, pill, pill.Height / 2);
                Theme.Text(g, pillText, pillFont, Theme.HMint.Dark, pill, StringAlignment.Center);
            }
            if (cx + pw + Theme.S(20) < right)
            {
                string mood = change < 0 ? "less free space" : change > 0 ? "more free space — nice" : "steady";
                Theme.Text(g, mood, Theme.Ui(13, Weight.Medium), change < 0 ? Theme.HCoral.Dark : Theme.HMint.Dark,
                    new RectangleF(cx + pw + Theme.S(12), y + Theme.S(4), right - cx - pw - Theme.S(12), Theme.S(36)));
            }
            y += Theme.S(70);

            if (State == Mode.Scanning)
            {
                if (Lost)
                {
                    Theme.Text(g, "Finding where the " + Format.Bytes(Drop) + " went…", Theme.Ui(16, Weight.Semibold), Theme.Ink,
                        new RectangleF(x, y, right - x, Theme.S(24)));
                    y += Theme.S(32);
                    var track = new RectangleF(x, y, right - x, Theme.S(16));
                    Theme.Fill(g, Theme.Sand, track, track.Height / 2);
                    float seg = track.Width * 0.22f;
                    float p = Theme.ReducedMotion ? 0.5f : (float)(0.5 + 0.5 * Math.Sin(Theme.Now * 2.2));
                    Theme.Fill(g, Theme.Purple, new RectangleF(track.X + (track.Width - seg) * p, track.Y, seg, track.Height), track.Height / 2);
                }
                return;
            }

            if (!Lost)
            {
                Theme.Text(g, change > 0
                        ? "You've got more room than last time — nothing went missing."
                        : "Same as last time — nothing went missing.",
                    Theme.Ui(15, Weight.Medium), Theme.Ink, new RectangleF(x, y, right - x, Theme.S(24)));
                if (!string.IsNullOrEmpty(Footnote))
                    Theme.Text(g, Footnote, Theme.Ui(12.5f, Weight.Regular), Theme.InkA(140),
                        new RectangleF(x, y + Theme.S(26), right - x, Theme.S(20)));
                return;
            }

            // "Most likely, [6.1 GB] went to Cinematic Strings"
            var big = Theme.Ui(22, Weight.Bold);
            float lineH = Theme.S(36);
            if (Amount != null && Target != null)
            {
                float sx = x;
                string lead = "Most likely, ";
                float lw = Theme.Width(g, lead, big);
                Theme.Text(g, lead, big, Theme.Ink, new RectangleF(sx, y, lw + Theme.S(4), lineH));
                sx += lw + Theme.S(4);
                sx += Theme.Highlight(g, Amount, big, sx, y, lineH) + Theme.S(4);
                string mid = " went to ";
                float mw = Theme.Width(g, mid, big);
                Theme.Text(g, mid, big, Theme.Ink, new RectangleF(sx, y, mw + Theme.S(4), lineH));
                sx += mw + Theme.S(12);
                float tw = Math.Min(Theme.Width(g, Target, big) + Theme.S(4), right - sx - Theme.S(8));
                if (tw > Theme.S(40))
                {
                    Theme.Text(g, Target, big, Theme.Ink, new RectangleF(sx, y, tw, lineH));
                    Theme.HandCircle(g, new RectangleF(sx - Theme.S(8), y - Theme.S(1), tw + Theme.S(14), lineH + Theme.S(3)), Theme.Sun);
                }
            }
            else
            {
                Theme.Text(g, Sentence ?? "", big, Theme.Ink, new RectangleF(x, y, right - x, lineH));
            }
            y += lineH + Theme.S(14);

            // The bar: each slice's share of the drop (or of what was found, if that's bigger)
            long found = 0;
            foreach (var s in Slices) found += s.Bytes;
            double whole = Math.Max(1, Math.Max(Drop, found));
            var bar = new RectangleF(x, y, right - x, Theme.S(18));
            using (var path = Theme.Round(bar, bar.Height / 2))
            {
                var state = g.Save();
                g.SetClip(path);
                using (var b = new SolidBrush(Theme.Sand)) g.FillRectangle(b, bar);
                float bx = bar.X;
                foreach (var s in Slices)
                {
                    float w = (float)(bar.Width * s.Bytes / whole);
                    if (w <= 0) continue;
                    using (var b = new SolidBrush(s.Hue.Base)) g.FillRectangle(b, bx, bar.Y, w + 0.5f, bar.Height);
                    bx += w;
                    using (var sep = new Pen(Color.White, Theme.S(2))) g.DrawLine(sep, bx, bar.Y, bx, bar.Bottom);
                }
                if (NotFound > 0)
                {
                    using (var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Theme.InkA(28), Theme.Sand))
                        g.FillRectangle(hatch, bx, bar.Y, bar.Right - bx, bar.Height);
                }
                g.Restore(state);
            }
            y += bar.Height + Theme.S(10);

            // Legend
            float lx = x;
            foreach (var s in Slices)
            {
                string text = s.Name + "  " + (s.Estimate ? "~" : "") + Format.Bytes(s.Bytes);
                float used = Theme.Chip(g, lx, y, text, s.Hue, right - lx, 26);
                if (used <= 0) break;
                lx += used + Theme.S(8);
            }
            if (NotFound > 0)
                Theme.Chip(g, lx, y, "Not found yet  " + Format.Bytes(NotFound), Theme.HNeutral, right - lx, 26);
            y += Theme.S(34);

            if (!string.IsNullOrEmpty(Footnote))
                Theme.Text(g, Footnote, Theme.Ui(12.5f, Weight.Regular), Theme.InkA(140), new RectangleF(x, y, right - x, Theme.S(20)));
        }

        /// <summary>Big number with a small caption under it. Returns the width used.</summary>
        float DrawNumber(Graphics g, float x, float y, long bytes, string caption)
        {
            var f = Theme.Display(30);
            string text = Format.Bytes(bytes);
            float w = Theme.Width(g, text, f);
            Theme.Text(g, text, f, Theme.Ink, new RectangleF(x, y - Theme.S(2), w + Theme.S(6), Theme.S(40)));
            var cf = Theme.Ui(12, Weight.Medium);
            float cw = Theme.Width(g, caption, cf);
            Theme.Text(g, caption, cf, Theme.InkA(140), new RectangleF(x + Theme.S(2), y + Theme.S(40), cw + Theme.S(6), Theme.S(18)));
            return Math.Max(w, cw);
        }

        static void DrawArrow(Graphics g, float x, float cy, float len)
        {
            using (var pen = new Pen(Theme.InkA(160), Theme.S(2.5f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLine(pen, x, cy, x + len, cy);
                g.DrawLines(pen, new[] { new PointF(x + len - Theme.S(8), cy - Theme.S(7)), new PointF(x + len, cy), new PointF(x + len - Theme.S(8), cy + Theme.S(7)) });
            }
        }
    }
}
