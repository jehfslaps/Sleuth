using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>"Sound as data": solid, mirrored waveform blobs with ragged toner edges.</summary>
    internal static class Waves
    {
        /// <summary>
        /// Draws a blob. Each hit is (position 0..1, height 0..1): a sharp attack and a decay, like a drum hit.
        /// With no hits, a few are made up from the seed. Returns the peak of the tallest hit.
        /// </summary>
        public static PointF Draw(Graphics g, RectangleF r, Color c, int seed, IList<PointF> hits)
        {
            var rnd = new Random(seed);
            if (hits == null || hits.Count == 0)
            {
                hits = new List<PointF>();
                int k = 3 + rnd.Next(3);
                for (int i = 0; i < k; i++) hits.Add(new PointF((i + (float)rnd.NextDouble() * 0.4f) / k, 0.45f + (float)rnd.NextDouble() * 0.55f));
            }
            int n = Math.Max(24, (int)(r.Width / Theme.S(1.1f)));
            float cy = r.Y + r.Height * 0.5f, half = r.Height * 0.48f;
            float slot = 1f / Math.Max(1, hits.Count);
            var env = new float[n];
            for (int i = 0; i < n; i++)
            {
                float x = i / (float)(n - 1), e = 0.015f;
                foreach (var h in hits)
                {
                    float d = x - h.X;
                    float v = d >= 0 ? h.Y * (float)Math.Exp(-d * 4.2f / slot) : d > -0.012f ? h.Y * (1 + d / 0.012f) : 0;
                    if (v > e) e = v;
                }
                env[i] = e * (0.86f + 0.14f * (float)rnd.NextDouble());
            }
            var pts = new List<PointF>(n * 2);
            float jitter = Theme.S(0.45f);
            for (int i = 0; i < n; i++)
                pts.Add(new PointF(r.X + r.Width * i / (n - 1), cy - env[i] * half + (float)(rnd.NextDouble() * 2 - 1) * jitter));
            for (int i = n - 1; i >= 0; i--)
                pts.Add(new PointF(r.X + r.Width * i / (n - 1), cy + env[i] * half * 0.92f + (float)(rnd.NextDouble() * 2 - 1) * jitter));
            using (var b = new SolidBrush(c)) g.FillPolygon(b, pts.ToArray());

            var top = hits.OrderByDescending(h => h.Y).First();
            return new PointF(r.X + r.Width * top.X, cy - top.Y * half);
        }
    }

    /// <summary>
    /// fig. 01, the right-hand data column for C:: free space at your last scan vs now, a mint specimen panel
    /// with the drop drawn as data, meters, a spec list and the legend of where the space went.
    /// </summary>
    internal sealed class DataColumn : Painted, IAnimated
    {
        public sealed class Slice
        {
            public string Name;
            public long Bytes;
            public Color Color;
            public bool Estimate;
        }

        public enum Mode { NoDrive, FirstLook, Scanning, Result }

        public Mode State = Mode.NoDrive;
        public string Drive = "C:";
        public long NowFree, TotalBytes;
        public long? WasFree;
        public DateTime? WasUtc;
        public string WindowText = "since last scan";
        public string Method;
        public readonly List<Slice> Slices = new List<Slice>();
        public long NotFound;
        public string Footnote;
        public string Signature = "by jehf slaps";

        long Drop { get { return WasFree.HasValue ? WasFree.Value - NowFree : 0; } }
        long Found { get { return Slices.Sum(s => s.Bytes); } }

        protected override Color Ground { get { return Theme.Paper2; } }

        public void Tick(float now)
        {
            if (State == Mode.Scanning && !Theme.ReducedMotion) Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            float W = Width, H = Height, pad = Theme.S(24);
            Theme.Line(g, Theme.Hair, Theme.S(1), 0.5f, 0, 0.5f, H);
            string d = Theme.Lc(Drive);

            Theme.Label(g, "fig. 01", Theme.M(9.5f), Theme.Ink2, pad, Theme.S(22));
            Theme.Label(g, d + " drive / free space / " + Theme.Lc(WindowText), Theme.M(10), Theme.Ink, pad, Theme.S(36), W - pad * 2);

            // ---- specimen panel ----
            float side = Math.Min(W - pad * 2 - Theme.S(48), Theme.S(236));
            var sp = new RectangleF(pad + Theme.S(24), Theme.S(70), side, side);
            // dotted guides run across the column
            var peak = new PointF(sp.X + sp.Width * 0.5f, sp.Y + sp.Height * 0.5f);

            Theme.Rect(g, Theme.Mint, sp.X, sp.Y, sp.Width, sp.Height);
            var mini = new RectangleF(sp.Right - Theme.S(40), sp.Y - Theme.S(26), Theme.S(66), Theme.S(66));
            Theme.Rect(g, Color.FromArgb(235, Theme.Mint), mini.X, mini.Y, mini.Width, mini.Height);
            Theme.Toner(g, Theme.Blue, new RectangleF(sp.X - Theme.S(16), sp.Bottom - Theme.S(20), Theme.S(32), Theme.S(32)), 9);
            Theme.Pixels(g, Theme.Diag, Theme.Violet, sp.X + Theme.S(16), sp.Bottom + Theme.S(12), Theme.S(8));
            Theme.Pixels(g, Theme.Cluster, Theme.Red, sp.Right - Theme.S(22), sp.Bottom - Theme.S(14), Theme.S(5));

            var inner = new RectangleF(sp.X + Theme.S(12), sp.Y + Theme.S(28), sp.Width - Theme.S(24), sp.Height - Theme.S(56));
            bool lost = WasFree.HasValue && Drop > 0;
            if (State == Mode.Result && lost)
            {
                peak = DrawHits(g, inner);
                // crosshair ring on the peak
                using (var p = new Pen(Theme.OnMint, Theme.S(1.3f))) g.DrawEllipse(p, peak.X - Theme.S(8), peak.Y - Theme.S(8), Theme.S(16), Theme.S(16));
                Theme.Line(g, Theme.OnMint, Theme.S(1.1f), peak.X - Theme.S(13), peak.Y, peak.X + Theme.S(13), peak.Y);
                Theme.Line(g, Theme.OnMint, Theme.S(1.1f), peak.X, peak.Y - Theme.S(13), peak.X, peak.Y + Theme.S(13));
                var top = Slices.OrderByDescending(s => s.Bytes).FirstOrDefault();
                if (top != null)
                {
                    Theme.Line(g, Theme.Ink, Theme.S(0.8f), peak.X + Theme.S(9), peak.Y - Theme.S(9), peak.X + Theme.S(30), sp.Y - Theme.S(12));
                    Theme.Label(g, "peak " + Theme.Lc((top.Estimate ? "~" : "") + Format.Bytes(top.Bytes)), Theme.M(8.5f), Theme.Ink2, peak.X + Theme.S(32), sp.Y - Theme.S(16));
                }
                Theme.Label(g, Theme.Coord(peak.X, peak.Y), Theme.M(7.5f), Theme.OnMint, peak.X + Theme.S(16), peak.Y - Theme.S(12));
            }
            else if (State == Mode.Scanning)
            {
                int step = Theme.ReducedMotion ? 5 : (int)(Theme.Now * 8) % 24;
                Waves.Draw(g, inner, Theme.OnMint2, 17, null);
                float hx = inner.X + inner.Width * step / 23f;
                Theme.Rect(g, Color.FromArgb(150, Theme.Mint), hx, inner.Y - Theme.S(4), inner.Right - hx, inner.Height + Theme.S(8));
                Theme.Dotted(g, Theme.Red, Theme.S(1), hx, sp.Y + Theme.S(6), hx, sp.Bottom - Theme.S(6), 2, 3);
                peak = new PointF(hx, sp.Y + sp.Height / 2);
                Theme.Label(g, "reading…", Theme.M(9), Theme.OnMint, sp.X + Theme.S(10), sp.Y + Theme.S(14));
            }
            else
            {
                string big = State == Mode.NoDrive ? "no " + d + " here." : State == Mode.FirstLook || !WasFree.HasValue ? "starting point." : Drop < 0 ? "more room." : "no drop.";
                Theme.Text(g, big, Theme.G(22, 550), Theme.OnMint, new RectangleF(sp.X + Theme.S(14), sp.Y, sp.Width - Theme.S(28), sp.Height), StringAlignment.Near, StringAlignment.Center);
                string small = State == Mode.NoDrive ? "pick all drives or " + d + " to see before → now."
                             : State == Mode.FirstLook || !WasFree.HasValue ? "saved. next time you'll see exactly what changed."
                             : "nothing went missing since your last scan.";
                Theme.Text(g, small, Theme.M(9), Theme.OnMint2, new RectangleF(sp.X + Theme.S(14), sp.Y + sp.Height / 2 + Theme.S(18), sp.Width - Theme.S(28), Theme.S(40)),
                    StringAlignment.Near, StringAlignment.Near, false, true);
            }

            // mini panel: was vs now as two bars
            if (WasFree.HasValue && TotalBytes > 0 && State != Mode.NoDrive)
            {
                float bh = mini.Height - Theme.S(18), bw = Theme.S(12), by = mini.Bottom - Theme.S(9);
                double top = Math.Max(WasFree.Value, NowFree) * 1.08;
                float h1 = (float)(bh * Math.Min(1, WasFree.Value / Math.Max(1, top)));
                float h2 = (float)(bh * Math.Min(1, NowFree / Math.Max(1, top)));
                Theme.Toner(g, Theme.OnMint2, new RectangleF(mini.X + Theme.S(16), by - h1, bw, h1), 21);
                Theme.Toner(g, Theme.OnMint, new RectangleF(mini.X + Theme.S(38), by - h2, bw, h2), 22);
                Theme.Label(g, "free", Theme.M(8), Theme.Ink, mini.Right - Theme.S(24), mini.Bottom + Theme.S(10));
            }

            Theme.Dotted(g, Theme.Ink, Theme.S(0.7f), Theme.S(12), peak.Y, W - Theme.S(10), peak.Y);
            Theme.Dotted(g, Theme.Ink, Theme.S(0.7f), peak.X, sp.Y - Theme.S(20), peak.X, sp.Bottom + Theme.S(24));
            Theme.Label(g, "fig. 01a — where it went", Theme.M(8.5f), Theme.Ink2, sp.X + Theme.S(40), sp.Bottom + Theme.S(14));

            // ---- meters ----
            float y = sp.Bottom + Theme.S(46), right = W - pad;
            float labelW = Theme.S(46), valueW = Theme.S(72), mx = pad + labelW, mw = right - valueW - mx - Theme.S(8);
            Func<bool> room = () => y < H - Theme.S(56);
            if (State != Mode.NoDrive && TotalBytes > 0 && room())
            {
                if (WasFree.HasValue)
                {
                    Theme.Label(g, "was", Theme.G(15, 500), Theme.Ink, pad, y);
                    Theme.Meter(g, mx, y, mw, (double)WasFree.Value / TotalBytes, false, Theme.Ink);
                    Theme.LabelRight(g, Theme.Lc(Format.Bytes(WasFree.Value)), Theme.M(11), Theme.Ink2, right, y);
                    y += Theme.S(28);
                }
                Theme.Label(g, "now", Theme.G(15, 500), Theme.Ink, pad, y);
                Theme.Meter(g, mx, y, mw, (double)NowFree / TotalBytes, false, Theme.Ink);
                Theme.LabelRight(g, Theme.Lc(Format.Bytes(NowFree)), Theme.M(11), Theme.Ink2, right, y);
                y += Theme.S(28);
                if (WasFree.HasValue && Drop > 0)
                {
                    Theme.Label(g, "drop", Theme.G(15, 500), Theme.Ink, pad, y);
                    Theme.Meter(g, mx, y, mw, State == Mode.Result ? Math.Min(1, (double)Found / Drop) : 0, true, Theme.Ink);
                    Theme.LabelRight(g, Theme.Lc(Format.SignedBytes(-Drop)), Theme.M(11), Theme.Red, right, y);
                    y += Theme.S(14);
                    Theme.Label(g, "found", Theme.M(7.5f), Theme.Ink2, mx, y);
                    Theme.LabelRight(g, "drop", Theme.M(7.5f), Theme.Ink2, right - valueW - Theme.S(8), y);
                    y += Theme.S(12);
                }
                y += Theme.S(14);
            }

            // ---- spec. ----
            var rows = new List<KeyValuePair<string, string>>();
            if (WasFree.HasValue && State != Mode.NoDrive)
            {
                rows.Add(Kv("change", Drop == 0 ? "none" : Format.SignedBytes(-Drop)));
                if (State == Mode.Result && Drop > 0)
                {
                    rows.Add(Kv("found", Format.Bytes(Found)));
                    rows.Add(Kv("not found yet", Format.Bytes(NotFound)));
                }
                rows.Add(Kv("last scan", WasUtc.HasValue ? Format.When(WasUtc.Value) : "—"));
            }
            if (!string.IsNullOrEmpty(Method)) rows.Add(Kv("read by", Method));
            if (rows.Count > 0 && room())
            {
                Theme.Label(g, "spec.", Theme.M(9.5f), Theme.Ink2, pad, y);
                y += Theme.S(18);
                foreach (var kv in rows)
                {
                    if (!room()) break;
                    Theme.Label(g, kv.Key, Theme.M(10), Theme.Ink, pad, y);
                    Theme.LabelRight(g, Theme.Lc(kv.Value), Theme.M(10), Theme.Ink2, right, y);
                    Theme.Line(g, Theme.Hair2, Theme.S(1), pad, y + Theme.S(11), right, y + Theme.S(11));
                    y += Theme.S(22);
                }
                y += Theme.S(10);
            }

            // ---- legend: where it went ----
            if (State == Mode.Result && Drop > 0 && (Slices.Count > 0 || NotFound > 0) && room())
            {
                foreach (var s in Slices)
                {
                    if (!room()) break;
                    Theme.Toner(g, s.Color, new RectangleF(pad, y - Theme.S(4), Theme.S(8), Theme.S(8)), s.Name.Length);
                    Theme.Text(g, Theme.Lc(s.Name), Theme.G(13.5f, 500), Theme.Ink, new RectangleF(pad + Theme.S(16), y - Theme.S(10), right - pad - Theme.S(16) - valueW, Theme.S(20)));
                    Theme.LabelRight(g, Theme.Lc((s.Estimate ? "~" : "") + Format.Bytes(s.Bytes)), Theme.M(10), Theme.Ink2, right, y);
                    y += Theme.S(22);
                }
                if (NotFound > 0 && room())
                {
                    using (var hatch = new HatchBrush(HatchStyle.WideUpwardDiagonal, Theme.Ink, Theme.Scan))
                        g.FillRectangle(hatch, pad, y - Theme.S(4), Theme.S(8), Theme.S(8));
                    Theme.Label(g, "not found yet", Theme.G(13.5f, 500), Theme.Grey, pad + Theme.S(16), y);
                    Theme.LabelRight(g, Theme.Lc(Format.Bytes(NotFound)), Theme.M(10), Theme.Grey, right, y);
                    y += Theme.S(22);
                }
                y += Theme.S(6);
            }

            if (!string.IsNullOrEmpty(Footnote) && y < H - Theme.S(80))
            {
                string t = Theme.Lc(Footnote);
                float th = Math.Min(Theme.WrappedHeight(g, t, Theme.M(9), right - pad), H - Theme.S(44) - y);
                Theme.Text(g, t, Theme.M(9), Theme.Ink2, new RectangleF(pad, y, right - pad, th), StringAlignment.Near, StringAlignment.Near, false, true);
            }

            Theme.LabelRight(g, Signature, Theme.M(10), Theme.Ink, W - Theme.S(18), H - Theme.S(18));
            Theme.Pixels(g, Theme.Step, Theme.Grey, W - Theme.S(14), H - Theme.S(10), Theme.S(3));
        }

        PointF DrawHits(Graphics g, RectangleF r)
        {
            var hits = new List<PointF>();
            var ordered = Slices.Where(s => s.Bytes > 0).ToList();
            long max = Math.Max(1, ordered.Select(s => s.Bytes).DefaultIfEmpty(0).Max());
            int count = ordered.Count + (NotFound > 0 ? 1 : 0);
            for (int i = 0; i < ordered.Count; i++)
                hits.Add(new PointF((i + 0.1f) / Math.Max(1, count), 0.18f + 0.82f * (float)Math.Sqrt((double)ordered[i].Bytes / max)));
            PointF peak = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
            if (hits.Count > 0) peak = Waves.Draw(g, r, Theme.OnMint, 42, hits);
            if (NotFound > 0)
            {
                // what couldn't be traced: a scanned grey ghost at the end
                float x0 = r.X + r.Width * ordered.Count / (float)count;
                var ghost = new RectangleF(x0, r.Y, r.Right - x0, r.Height);
                Waves.Draw(g, ghost, Theme.OnMint2, 7, new List<PointF> { new PointF(0.05f, 0.2f + 0.8f * (float)Math.Sqrt((double)NotFound / max)) });
            }
            return peak;
        }

        static KeyValuePair<string, string> Kv(string k, string v) { return new KeyValuePair<string, string>(k, v); }
    }
}
