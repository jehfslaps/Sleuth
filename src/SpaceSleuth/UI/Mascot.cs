using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace SpaceSleuth.UI
{
    internal enum Mood { Happy, Searching, Grin, Worried, Calm }

    /// <summary>
    /// "Pebble", Space Sleuth's original mascot: a soft cream pebble with a little sprout, glossy eyes,
    /// rosy cheeks and a magnifying glass. Thick wobbly outline, sticker style.
    /// </summary>
    internal static class Mascot
    {
        /// <summary>
        /// Draws the body (and magnifier). In peek mode, call <see cref="DrawHands"/> after drawing whatever the
        /// mascot is peeking over, so the hands overlap that edge.
        /// </summary>
        public static void DrawBody(Graphics g, float cx, float cy, float size, Mood mood, float t, bool peek, bool animate)
        {
            float W = size, H = size * 0.9f;
            float stroke = Math.Max(2f, size * 0.03f);
            if (animate) cy += (float)Math.Sin(t * Math.PI * 2 / 3.0) * Theme.S(3);

            float look = mood == Mood.Searching && animate ? (float)Math.Sin(t * 1.6) : 0f;
            bool blink = animate && (t % 4.2f) > 4.05f;

            using (var outline = new Pen(Theme.Outline, stroke) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var body = new SolidBrush(Theme.OffWhite))
            {
                // Feet (only when standing)
                if (!peek)
                {
                    foreach (var side in new[] { -1, 1 })
                    {
                        var foot = new RectangleF(cx + side * W * 0.2f - W * 0.11f, cy + H * 0.4f, W * 0.22f, W * 0.13f);
                        g.FillEllipse(body, foot);
                        g.DrawEllipse(outline, foot);
                    }
                }

                // Sprout on top
                float topY = cy - H / 2;
                var stemTop = new PointF(cx + W * 0.08f, topY - H * 0.2f);
                g.DrawCurve(outline, new[] { new PointF(cx + W * 0.02f, topY + stroke), new PointF(cx + W * 0.07f, topY - H * 0.1f), stemTop }, 0.5f);
                DrawLeaf(g, stemTop, W * 0.2f, W * 0.1f, -30, outline);
                DrawLeaf(g, stemTop, W * 0.15f, W * 0.08f, 200, outline);

                // Pebble body with a slightly wobbly, hand-drawn outline
                var pts = new PointF[28];
                for (int i = 0; i < pts.Length; i++)
                {
                    double a = i * Math.PI * 2 / pts.Length;
                    float wob = 1 + 0.022f * (float)Math.Sin(3 * a + 1.3) + 0.012f * (float)Math.Sin(5 * a);
                    float sy = (float)Math.Sin(a);
                    float ry = H / 2 * (sy > 0 ? 0.94f : 1f);
                    pts[i] = new PointF(cx + (float)Math.Cos(a) * W / 2 * wob, cy + sy * ry * wob);
                }
                g.FillClosedCurve(body, pts, FillMode.Alternate, 0.5f);
                g.DrawClosedCurve(outline, pts, 0.5f, FillMode.Alternate);

                // Cheeks
                using (var blush = new SolidBrush(Color.FromArgb(170, Theme.Pink)))
                {
                    g.FillEllipse(blush, cx - W * 0.36f, cy + H * 0.05f, W * 0.14f, W * 0.08f);
                    g.FillEllipse(blush, cx + W * 0.22f, cy + H * 0.05f, W * 0.14f, W * 0.08f);
                }

                // Big glossy eyes
                float ew = W * 0.13f, eh = blink ? W * 0.025f : W * 0.17f;
                float shift = look * W * 0.035f;
                using (var eye = new SolidBrush(Theme.Outline))
                using (var shine = new SolidBrush(Color.White))
                {
                    foreach (var side in new[] { -1, 1 })
                    {
                        float ex = cx + side * W * 0.16f + shift, ey = cy - H * 0.07f;
                        g.FillEllipse(eye, ex - ew / 2, ey - eh / 2, ew, eh);
                        if (!blink)
                        {
                            float d = W * 0.05f;
                            g.FillEllipse(shine, ex - ew * 0.3f, ey - eh * 0.36f, d, d);
                            g.FillEllipse(shine, ex + ew * 0.1f, ey + eh * 0.12f, d * 0.45f, d * 0.45f);
                        }
                    }
                }

                // Mouth
                float my = cy + H * 0.12f;
                switch (mood)
                {
                    case Mood.Grin:
                        using (var p = new GraphicsPath())
                        {
                            var mr = new RectangleF(cx - W * 0.09f, my - W * 0.06f, W * 0.18f, W * 0.13f);
                            p.AddArc(mr, 0, 180);
                            p.CloseFigure();
                            using (var b = new SolidBrush(Theme.HCoral.Dark)) g.FillPath(b, p);
                            g.DrawPath(outline, p);
                        }
                        break;
                    case Mood.Searching:
                        g.DrawEllipse(outline, cx - W * 0.03f + shift, my - W * 0.02f, W * 0.06f, W * 0.07f);
                        break;
                    case Mood.Worried:
                        g.DrawCurve(outline, new[] { new PointF(cx - W * 0.07f, my + W * 0.02f), new PointF(cx - W * 0.02f, my - W * 0.01f), new PointF(cx + W * 0.03f, my + W * 0.02f), new PointF(cx + W * 0.07f, my) }, 0.5f);
                        // sweat drop
                        using (var p = new GraphicsPath())
                        {
                            float dx = cx + W * 0.42f, dy = cy - H * 0.3f, dr = W * 0.06f;
                            p.AddBezier(dx, dy - dr * 1.6f, dx + dr, dy - dr * 0.2f, dx + dr, dy + dr, dx, dy + dr);
                            p.AddBezier(dx, dy + dr, dx - dr, dy + dr, dx - dr, dy - dr * 0.2f, dx, dy - dr * 1.6f);
                            using (var b = new SolidBrush(Theme.Sky)) g.FillPath(b, p);
                            using (var thin = new Pen(Theme.Outline, stroke * 0.7f)) g.DrawPath(thin, p);
                        }
                        break;
                    case Mood.Calm:
                        g.DrawArc(outline, cx - W * 0.05f, my - W * 0.035f, W * 0.1f, W * 0.06f, 15, 150);
                        break;
                    default:
                        g.DrawArc(outline, cx - W * 0.07f, my - W * 0.05f, W * 0.14f, W * 0.09f, 15, 150);
                        break;
                }

                // Magnifying glass
                float sway = mood == Mood.Searching && animate ? (float)Math.Sin(t * 2.2) * 7f : 0f;
                var lens = new PointF(cx + W * 0.62f, cy - H * (peek ? 0.18f : 0.05f));
                DrawMagnifier(g, lens, W * 0.19f, 128 + sway, stroke);

                if (!peek)
                {
                    // Thin white-outlined stick arm reaching to the handle
                    double a = (128 + sway) * Math.PI / 180;
                    var handle = new PointF(lens.X + (float)Math.Cos(a) * W * 0.36f, lens.Y + (float)Math.Sin(a) * W * 0.36f);
                    var shoulder = new PointF(cx + W * 0.42f, cy + H * 0.08f);
                    using (var arm = new Pen(Theme.Outline, stroke * 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    using (var armIn = new Pen(Color.White, stroke * 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        g.DrawLine(arm, shoulder, handle);
                        g.DrawLine(armIn, shoulder, handle);
                    }
                    float hr = W * 0.06f;
                    g.FillEllipse(body, handle.X - hr, handle.Y - hr, hr * 2, hr * 2);
                    g.DrawEllipse(outline, handle.X - hr, handle.Y - hr, hr * 2, hr * 2);
                }
            }
        }

        /// <summary>Two cartoon hands gripping an edge at <paramref name="edgeY"/>.</summary>
        public static void DrawHands(Graphics g, float cx, float edgeY, float size, float t, bool animate)
        {
            float W = size;
            float stroke = Math.Max(2f, size * 0.03f);
            float bob = animate ? (float)Math.Sin(t * Math.PI * 2 / 3.0) * Theme.S(1) : 0;
            using (var outline = new Pen(Theme.Outline, stroke) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var fill = new SolidBrush(Theme.OffWhite))
            {
                foreach (var side in new[] { -1, 1 })
                {
                    float hx = cx + side * W * 0.3f, hw = W * 0.24f, hh = W * 0.16f;
                    var r = new RectangleF(hx - hw / 2, edgeY - hh * 0.55f + bob, hw, hh);
                    using (var p = Theme.Round(r, hh / 2))
                    {
                        g.FillPath(fill, p);
                        g.DrawPath(outline, p);
                    }
                    using (var finger = new Pen(Theme.Outline, stroke * 0.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    {
                        for (int i = 1; i <= 2; i++)
                        {
                            float fx = r.X + r.Width * i / 3f;
                            g.DrawLine(finger, fx, r.Y + r.Height * 0.45f, fx, r.Bottom - stroke);
                        }
                    }
                }
            }
        }

        static void DrawLeaf(Graphics g, PointF at, float len, float wid, float angle, Pen outline)
        {
            var state = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(angle);
            using (var p = new GraphicsPath())
            {
                p.AddBezier(0, 0, len * 0.3f, -wid, len * 0.75f, -wid, len, 0);
                p.AddBezier(len, 0, len * 0.75f, wid, len * 0.3f, wid, 0, 0);
                using (var b = new SolidBrush(Theme.Mint)) g.FillPath(b, p);
                g.DrawPath(outline, p);
            }
            g.Restore(state);
        }

        static void DrawMagnifier(Graphics g, PointF c, float r, float handleAngle, float stroke)
        {
            double a = handleAngle * Math.PI / 180;
            var start = new PointF(c.X + (float)Math.Cos(a) * r, c.Y + (float)Math.Sin(a) * r);
            var end = new PointF(c.X + (float)Math.Cos(a) * r * 2.1f, c.Y + (float)Math.Sin(a) * r * 2.1f);
            using (var hOut = new Pen(Theme.Outline, r * 0.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var hIn = new Pen(Theme.Sun, r * 0.55f - stroke * 2) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawLine(hOut, start, end);
                g.DrawLine(hIn, start, end);
            }
            using (var glass = new SolidBrush(Color.FromArgb(215, Theme.HSky.Tint)))
                g.FillEllipse(glass, c.X - r, c.Y - r, r * 2, r * 2);
            using (var ring = new Pen(Theme.Outline, stroke * 1.5f))
                g.DrawEllipse(ring, c.X - r, c.Y - r, r * 2, r * 2);
            using (var glint = new Pen(Color.White, stroke * 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawArc(glint, c.X - r * 0.6f, c.Y - r * 0.6f, r * 1.2f, r * 1.2f, 200, 60);
        }
    }
}
