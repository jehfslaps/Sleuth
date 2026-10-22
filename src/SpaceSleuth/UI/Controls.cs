using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SpaceSleuth.UI
{
    /// <summary>Anything that wants a frame of animation from the window's shared timer.</summary>
    internal interface IAnimated
    {
        void Tick(float now);
    }

    /// <summary>Base for every custom-painted control: double buffered, cream background, smooth drawing.</summary>
    internal abstract class Painted : Control
    {
        protected Painted()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Cream;
            ForeColor = Theme.Ink;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Theme.Quality(e.Graphics);
            Draw(e.Graphics);
        }

        protected abstract void Draw(Graphics g);
    }

    internal enum PillKind { Primary, Secondary }

    /// <summary>Fully rounded button. Primary = sun yellow with a soft inner shadow; Secondary = white with a faint ink border.</summary>
    internal sealed class PillButton : Painted
    {
        public PillKind Kind = PillKind.Secondary;
        public bool ShowCaret;
        bool hover, down;

        public PillButton(string text, PillKind kind)
        {
            Text = text;
            Kind = kind;
            TabStop = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
        }

        Font TextFont { get { return Theme.Ui(Kind == PillKind.Primary ? 15 : 14, Weight.Semibold); } }

        public int PreferredWidth()
        {
            using (var g = CreateGraphics())
                return (int)Math.Ceiling(Theme.Width(g, Text, TextFont) + Theme.S(Kind == PillKind.Primary ? 52 : 40) + (ShowCaret ? Theme.S(20) : 0));
        }

        protected override void Draw(Graphics g)
        {
            var r = new RectangleF(Theme.S(2), Theme.S(1), Width - Theme.S(4), Height - Theme.S(4));
            if (down) r.Inflate(-r.Width * 0.02f, -r.Height * 0.04f);
            float rad = r.Height / 2;

            if (Kind == PillKind.Primary)
            {
                Color fill = !Enabled ? Color.FromArgb(150, Theme.Sun) : hover ? Theme.Hex("#FFBE33") : Theme.Sun;
                Theme.Fill(g, fill, r, rad);
                using (var p = Theme.Round(r, rad))
                {
                    var state = g.Save();
                    g.SetClip(p);
                    using (var b = new SolidBrush(Color.FromArgb(31, 0, 0, 0)))
                        g.FillRectangle(b, r.X, r.Bottom - Theme.S(3), r.Width, Theme.S(3));
                    g.Restore(state);
                }
            }
            else
            {
                Theme.Fill(g, hover && Enabled ? Theme.Hex("#FFF5DD") : Theme.White, r, rad);
                Theme.Stroke(g, Theme.InkA(26), Theme.S(1.5f), r, rad);
            }

            if (Focused && ShowFocusCues)
            {
                var f = r; f.Inflate(Theme.S(1), Theme.S(1));
                Theme.Stroke(g, Theme.Purple, Theme.S(2), f, f.Height / 2);
            }

            var textRect = r;
            if (ShowCaret) textRect.Width -= Theme.S(16);
            Theme.Text(g, Text, TextFont, Enabled ? Theme.Ink : Theme.InkA(110), textRect, StringAlignment.Center);
            if (ShowCaret)
                Theme.Caret(g, r.Right - Theme.S(22), r.Y + r.Height / 2, Theme.S(4), Theme.Ink);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
    }

    /// <summary>Segmented pill control. The selected option is a purple pill that springs across when you pick another.</summary>
    internal sealed class Segmented : Painted, IAnimated
    {
        readonly string[] items;
        int selected, hover = -1;
        RectangleF pill, pillFrom;
        float animStart = -1;

        public event EventHandler SelectedIndexChanged;

        public Segmented(string[] items, int selected)
        {
            this.items = items;
            this.selected = selected;
            TabStop = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value == selected || value < 0 || value >= items.Length) return;
                pillFrom = pill;
                selected = value;
                animStart = Theme.ReducedMotion ? -1 : Theme.Now;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        Font ItemFont { get { return Theme.Ui(13.5f, Weight.Semibold); } }

        RectangleF[] Rects(Graphics g)
        {
            var rects = new RectangleF[items.Length];
            float x = Theme.S(4), h = Height - Theme.S(10);
            for (int i = 0; i < items.Length; i++)
            {
                float w = Theme.Width(g, items[i], ItemFont) + Theme.S(24);
                rects[i] = new RectangleF(x, Theme.S(3), w, h);
                x += w + Theme.S(2);
            }
            return rects;
        }

        public int PreferredWidth()
        {
            using (var g = CreateGraphics())
            {
                var r = Rects(g);
                return (int)Math.Ceiling(r[r.Length - 1].Right + Theme.S(4));
            }
        }

        public void Tick(float now)
        {
            if (animStart >= 0) Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            var rects = Rects(g);
            var outer = new RectangleF(0, 0, Width - 1, Height - Theme.S(4));
            Theme.Card(g, Theme.White, outer, outer.Height / 2);

            var target = rects[selected];
            if (animStart >= 0 && pillFrom.Width > 0)
            {
                float p = Math.Min(1, (Theme.Now - animStart) / 0.32f);
                float e = Theme.Spring(p);
                pill = new RectangleF(pillFrom.X + (target.X - pillFrom.X) * e, target.Y,
                                      pillFrom.Width + (target.Width - pillFrom.Width) * e, target.Height);
                if (p >= 1) animStart = -1;
            }
            else
            {
                pill = target;
                animStart = -1;
            }
            Theme.Fill(g, Theme.Purple, pill, pill.Height / 2);

            for (int i = 0; i < items.Length; i++)
            {
                bool inPill = Math.Abs(rects[i].X + rects[i].Width / 2 - (pill.X + pill.Width / 2)) < rects[i].Width / 2;
                Color c = inPill ? Color.White : i == hover ? Theme.InkA(220) : Theme.InkA(153);
                Theme.Text(g, items[i], ItemFont, c, rects[i], StringAlignment.Center);
            }
            if (Focused && ShowFocusCues) Theme.Stroke(g, Theme.Purple, Theme.S(2), outer, outer.Height / 2);
        }

        int HitTest(Point p)
        {
            using (var g = CreateGraphics())
            {
                var rects = Rects(g);
                for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(p)) return i;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int h = HitTest(e.Location);
            if (h >= 0 && Enabled) SelectedIndex = h;
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left && selected > 0) SelectedIndex = selected - 1;
            if (e.KeyCode == Keys.Right && selected < items.Length - 1) SelectedIndex = selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>On/off dot chip. Off = white with a sand dot; on = mint with a mint dot and a check.</summary>
    internal sealed class ToggleChip : Painted
    {
        bool isChecked, hover;
        public event EventHandler CheckedChanged;

        public ToggleChip(string text)
        {
            Text = text;
            TabStop = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
        }

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (value == isChecked) return;
                isChecked = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        Font TextFont { get { return Theme.Ui(13.5f, Weight.Medium); } }

        public int PreferredWidth()
        {
            using (var g = CreateGraphics())
                return (int)Math.Ceiling(Theme.Width(g, Text + "  ✓", TextFont) + Theme.S(46));
        }

        protected override void Draw(Graphics g)
        {
            var r = new RectangleF(Theme.S(1), Theme.S(1), Width - Theme.S(2), Height - Theme.S(4));
            float rad = r.Height / 2;
            var hue = Theme.HMint;
            if (isChecked) Theme.Fill(g, hue.Tint, r, rad);
            else
            {
                Theme.Fill(g, hover && Enabled ? Theme.Hex("#FFF5DD") : Theme.White, r, rad);
                Theme.Stroke(g, Theme.InkA(26), Theme.S(1.5f), r, rad);
            }
            float dot = Theme.S(12);
            using (var b = new SolidBrush(isChecked ? hue.Base : Theme.Sand))
                g.FillEllipse(b, r.X + Theme.S(12), r.Y + (r.Height - dot) / 2, dot, dot);
            var tr = new RectangleF(r.X + Theme.S(32), r.Y, r.Width - Theme.S(36), r.Height);
            Theme.Text(g, Text + (isChecked ? "  ✓" : ""), TextFont, isChecked ? hue.Dark : Theme.InkA(180), tr);
            if (Focused && ShowFocusCues) Theme.Stroke(g, Theme.Purple, Theme.S(2), r, rad);
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Rounded white input with a search glyph and a purple focus ring.</summary>
    internal sealed class SearchBox : Painted
    {
        public readonly TextBox Box = new TextBox();

        public SearchBox(string placeholder)
        {
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.White;
            Box.ForeColor = Theme.Ink;
            Box.Font = Theme.Ui(14, Weight.Regular);
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Box.HandleCreated += (s, e) =>
            {
                try { SendMessage(Box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, placeholder); }
                catch { }
            };
            Cursor = Cursors.IBeam;
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int left = Theme.Si(38);
            Box.SetBounds(left, (Height - Theme.Si(4) - Box.PreferredHeight) / 2, Math.Max(10, Width - left - Theme.Si(14)), Box.PreferredHeight);
            base.OnLayout(e);
        }

        protected override void Draw(Graphics g)
        {
            var r = new RectangleF(Theme.S(1), Theme.S(1), Width - Theme.S(2), Height - Theme.S(4));
            Theme.Fill(g, Theme.White, r, Theme.S(16));
            if (Box.Focused) Theme.Stroke(g, Theme.Purple, Theme.S(2), r, Theme.S(16));
            else Theme.Stroke(g, Theme.InkA(26), Theme.S(1.5f), r, Theme.S(16));
            Theme.SearchGlyph(g, r.X + Theme.S(14), r.Y + r.Height / 2 - Theme.S(7), Theme.S(14), Theme.InkA(115));
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }

    /// <summary>The blue brand panel: two-tone chunky wordmark, ribbon loops, tilted word stickers and confetti.</summary>
    internal sealed class HeroPanel : Painted
    {
        protected override void Draw(Graphics g)
        {
            float m = Theme.S(16);
            var panel = new RectangleF(m, m, Width - m * 2, Height - m);
            using (var path = Theme.Round(panel, Theme.S(32)))
            {
                using (var b = new SolidBrush(Theme.Blue)) g.FillPath(b, path);
                var state = g.Save();
                g.SetClip(path);

                // Ribbon loops (texture), a shade darker than the panel
                float w = panel.Width, h = panel.Height, x0 = panel.X, y0 = panel.Y;
                Theme.Ribbon(g, Theme.BlueDeep, Theme.S(30),
                    new PointF(x0 + w * 0.30f, y0 + h + Theme.S(30)), new PointF(x0 + w * 0.40f, y0 + h * 0.45f),
                    new PointF(x0 + w * 0.50f, y0 + h * 0.15f), new PointF(x0 + w * 0.56f, y0 + h * 0.55f),
                    new PointF(x0 + w * 0.47f, y0 + h * 0.75f), new PointF(x0 + w * 0.44f, y0 + h * 0.35f),
                    new PointF(x0 + w * 0.62f, y0 - Theme.S(30)));
                Theme.Ribbon(g, Theme.BlueDeep, Theme.S(26),
                    new PointF(x0 + w * 0.80f, y0 + h + Theme.S(30)), new PointF(x0 + w * 0.86f, y0 + h * 0.4f),
                    new PointF(x0 + w * 0.97f, y0 + h * 0.3f), new PointF(x0 + w * 0.93f, y0 + h * 0.75f),
                    new PointF(x0 + w + Theme.S(40), y0 + h * 0.6f));

                // Two-tone wordmark
                // Compact version (no tagline) when the C: banner needs the room
                bool compact = h < Theme.S(100);
                var big = Theme.Display(compact ? 34 : Math.Min(44, 28 + w / Theme.S(1) / 70));
                float tx = panel.X + Theme.S(32), ty = compact ? panel.Y + (h - big.Height * 1.15f) / 2 : panel.Y + Theme.S(16);
                float spaceW = Theme.Width(g, "SPACE", big);
                Theme.Text(g, "SPACE", big, Theme.OffWhite, new RectangleF(tx, ty, spaceW + Theme.S(10), big.Height * 1.25f), StringAlignment.Near, StringAlignment.Near);
                float sleuthW = Theme.Width(g, "SLEUTH", big);
                Theme.Text(g, "SLEUTH", big, Theme.Butter, new RectangleF(tx + spaceW + Theme.S(12), ty, sleuthW + Theme.S(10), big.Height * 1.25f), StringAlignment.Near, StringAlignment.Near);
                float wordRight = tx + spaceW + Theme.S(12) + sleuthW;

                var tag = Theme.Ui(15, Weight.Medium);
                if (!compact)
                Theme.Text(g, "Lost a few gigs? Let’s find out where they went — no stress.", tag, Color.FromArgb(235, Color.White),
                    new RectangleF(tx + Theme.S(2), ty + big.Height * 1.2f, Math.Max(Theme.S(200), wordRight - tx + Theme.S(120)), tag.Height * 1.5f),
                    StringAlignment.Near, StringAlignment.Near);

                // Sticker pile on the right
                float sx = Math.Max(wordRight + Theme.S(140), panel.Right - Theme.S(330));
                float cy = panel.Y + h / 2;
                if (sx < panel.Right - Theme.S(120))
                {
                    Theme.Flower(g, sx - Theme.S(30), panel.Y + Theme.S(26), Theme.S(16), Theme.Sun, Theme.Orange);
                    Theme.Sparkle(g, panel.Right - Theme.S(26), panel.Y + Theme.S(28), Theme.S(13), Theme.Butter);
                    Theme.Starburst(g, sx + Theme.S(210), panel.Bottom - Theme.S(10), Theme.S(22), Theme.Pink);
                    Theme.HalfSun(g, sx + Theme.S(40), panel.Bottom + Theme.S(2), Theme.S(20), Theme.Orange, 180);
                    Theme.Clover(g, panel.Right - Theme.S(40), panel.Bottom - Theme.S(22), Theme.S(18), Theme.Mint);
                    Theme.Sparkle(g, sx + Theme.S(95), panel.Y + Theme.S(18), Theme.S(9), Theme.White);

                    float v = compact ? 0.5f : 1f; // squash the pile vertically in the compact hero
                    Theme.Sticker(g, new PointF(sx + Theme.S(30), cy + Theme.S(14) * v), "found it!", -22, Theme.Pink, Theme.HCoral.Dark, false);
                    Theme.Sticker(g, new PointF(sx + Theme.S(130), cy - Theme.S(18) * v), "tidy", 18, Theme.Indigo, Theme.Lavender, true);
                    Theme.Sticker(g, new PointF(sx + Theme.S(210), cy + Theme.S(22) * v), "breathe", -14, Theme.Forest, Theme.Lime, false);
                    Theme.Sticker(g, new PointF(sx + Theme.S(255), cy - Theme.S(26) * v), "space!", 28, Theme.Orange, Theme.Butter, false);
                    if (!compact)
                        Theme.Sticker(g, new PointF(sx + Theme.S(110), cy + Theme.S(38) * v), "no stress", 9, Theme.Purple, Color.White, true, 15);
                }
                g.Restore(state);
            }
        }
    }

    /// <summary>The big "Most likely, 30 GB went to …" line, the dot chips under it, and any gentle notes.</summary>
    internal sealed class HeadlinePanel : Painted
    {
        public string Plain;          // used when there's no result to highlight
        public string Amount;         // goes in the green highlighter pill
        public string Lead = "Most likely, ";
        public string Middle = " went to ";
        public string Target;
        public readonly List<KeyValuePair<string, Hue>> Chips = new List<KeyValuePair<string, Hue>>();
        public readonly List<string> Notes = new List<string>();
        /// <summary>True when the C: banner above already shows the main sentence.</summary>
        public bool HideLine;

        Font Big { get { return Theme.Ui(26, Weight.Bold); } }
        Font NoteFont { get { return Theme.Ui(13, Weight.Regular); } }

        float Pad { get { return Theme.S(32); } }

        public int Measure(int width)
        {
            using (var g = CreateGraphics())
            {
                float h = HideLine ? Theme.S(2) : Theme.S(10) + Theme.S(42);
                if (Chips.Count > 0) h += Theme.S(34);
                if (HideLine && Chips.Count == 0 && Notes.Count == 0) return 0;
                if (Notes.Count > 0) h += NotesHeight(g, width) + Theme.S(10);
                return (int)Math.Ceiling(h + Theme.S(6));
            }
        }

        float NotesHeight(Graphics g, float width)
        {
            float inner = width - Pad * 2 - Theme.S(52);
            float h = Theme.S(20);
            foreach (var n in Notes) h += Theme.WrappedHeight(g, n, NoteFont, inner) + Theme.S(4);
            return h;
        }

        protected override void Draw(Graphics g)
        {
            float x = Pad, y = Theme.S(10), lineH = Theme.S(42);
            var big = Big;
            float maxRight = Width - Pad;

            if (HideLine)
            {
                y = Theme.S(2) - lineH - Theme.S(4);
            }
            else if (Amount != null && Target != null)
            {
                float lw = Theme.Width(g, Lead, big);
                Theme.Text(g, Lead, big, Theme.Ink, new RectangleF(x, y, lw + Theme.S(4), lineH));
                x += lw + Theme.S(4);
                x += Theme.Highlight(g, Amount, big, x, y, lineH) + Theme.S(4);
                float mw = Theme.Width(g, Middle, big);
                Theme.Text(g, Middle, big, Theme.Ink, new RectangleF(x, y, mw + Theme.S(4), lineH));
                x += mw + Theme.S(14);
                float tw = Math.Min(Theme.Width(g, Target, big) + Theme.S(4), maxRight - x - Theme.S(8));
                if (tw > Theme.S(40))
                {
                    Theme.Text(g, Target, big, Theme.Ink, new RectangleF(x, y, tw, lineH));
                    Theme.HandCircle(g, new RectangleF(x - Theme.S(9), y - Theme.S(1), tw + Theme.S(16), lineH + Theme.S(4)), Theme.Sun);
                }
            }
            else
            {
                Theme.Text(g, Plain ?? "", big, Theme.Ink, new RectangleF(x, y, maxRight - x, lineH));
            }
            y += lineH + Theme.S(4);

            if (Chips.Count > 0)
            {
                float cx = Pad;
                foreach (var c in Chips)
                {
                    float used = Theme.Chip(g, cx, y + Theme.S(2), c.Key, c.Value, maxRight - cx, 26);
                    if (used <= 0) break;
                    cx += used + Theme.S(8);
                }
                y += Theme.S(34);
            }

            if (Notes.Count > 0)
            {
                float nh = NotesHeight(g, Width);
                var card = new RectangleF(Pad, y + Theme.S(4), Width - Pad * 2, nh);
                Theme.Fill(g, Theme.HCoral.Tint, card, Theme.S(18));
                float ny = card.Y + Theme.S(10);
                float inner = card.Width - Theme.S(52);
                foreach (var n in Notes)
                {
                    float th = Theme.WrappedHeight(g, n, NoteFont, inner);
                    using (var b = new SolidBrush(Theme.Coral))
                        g.FillEllipse(b, card.X + Theme.S(18), ny + Theme.S(5), Theme.S(9), Theme.S(9));
                    Theme.Text(g, n, NoteFont, Theme.HCoral.Dark, new RectangleF(card.X + Theme.S(36), ny, inner, th + Theme.S(2)),
                        StringAlignment.Near, StringAlignment.Near, false, true);
                    ny += th + Theme.S(4);
                }
            }
        }
    }

    /// <summary>Bottom strip: status text, a bouncy progress pill while scanning, and the version.</summary>
    internal sealed class StatusBar : Painted, IAnimated
    {
        string status = "";
        public bool Busy;
        public string Version = "";

        public string Status
        {
            get { return status; }
            set { status = value ?? ""; Invalidate(); }
        }

        public void Tick(float now)
        {
            if (Busy) Invalidate();
        }

        protected override void Draw(Graphics g)
        {
            float pad = Theme.S(32);
            var small = Theme.Ui(12.5f, Weight.Regular);
            float right = Width - pad;
            float vw = Theme.Width(g, Version, small);
            Theme.Text(g, Version, small, Theme.InkA(110), new RectangleF(right - vw, 0, vw + Theme.S(2), Height));
            right -= vw + Theme.S(16);

            if (Busy)
            {
                float bw = Theme.S(150), bh = Theme.S(8);
                var track = new RectangleF(right - bw, (Height - bh) / 2, bw, bh);
                Theme.Fill(g, Theme.Sand, track, bh / 2);
                float seg = bw * 0.35f;
                float p = Theme.ReducedMotion ? 0.5f : (float)(0.5 + 0.5 * Math.Sin(Theme.Now * 2.6));
                Theme.Fill(g, Theme.Purple, new RectangleF(track.X + (bw - seg) * p, track.Y, seg, bh), bh / 2);
                right = track.X - Theme.S(16);
            }
            Theme.Text(g, status, small, Theme.InkA(150), new RectangleF(pad, 0, Math.Max(0, right - pad), Height));
        }
    }
}
