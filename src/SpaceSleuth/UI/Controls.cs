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

    /// <summary>Base for every custom-painted control: double buffered, paper ground, grain on top.</summary>
    internal abstract class Painted : Control
    {
        protected Painted()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);
            Theme.Changed += (s, e) => { BackColor = Ground; Invalidate(); };
            BackColor = Theme.Paper;
        }

        protected virtual Color Ground { get { return Theme.Paper; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Ground);
            Theme.Quality(g);
            Draw(g);
            Theme.Grain(g, this, ClientRectangle);
        }

        protected abstract void Draw(Graphics g);
    }

    /// <summary>
    /// The 92px side bar: red pixel stair, tiny coordinates, the vertical "sleuth." wordmark with its RGB split,
    /// a hairline with a crosshair, and a rotated caption.
    /// </summary>
    internal sealed class SideBar : Painted
    {
        public string Coords = "x: 041\ny: 366";

        protected override Color Ground { get { return Theme.Side; } }

        protected override void Draw(Graphics g)
        {
            float w = Width, h = Height;
            Theme.Line(g, Theme.Hair, Theme.S(1), w - 0.5f, 0, w - 0.5f, h);
            Theme.Pixels(g, Theme.StairA, Theme.Red, Theme.S(24), Theme.S(30), Theme.S(6));
            Theme.Text(g, Coords, Theme.M(8.5f), Theme.Grey, new RectangleF(Theme.S(24), Theme.S(60), w - Theme.S(28), Theme.S(30)),
                StringAlignment.Near, StringAlignment.Near, false, true);

            // vertical wordmark, reading upward
            var font = Theme.W(50);
            float baseY = Math.Max(Theme.S(330), h * 0.62f);
            var state = g.Save();
            g.TranslateTransform(Theme.S(16), baseY);
            g.RotateTransform(-90);
            Theme.Split(g, "sleuth.", font, Theme.Blue, 0, 0);
            g.Restore(state);

            // hairline with a crosshair under it
            float lx = Theme.S(46);
            float top = baseY + Theme.S(22), bottom = Math.Min(h - Theme.S(150), top + Theme.S(80));
            if (bottom > top)
            {
                Theme.Line(g, Theme.Hair, Theme.S(1), lx, top, lx, bottom);
                Theme.Cross(g, Theme.Ink, lx, top, Theme.S(4));
            }

            // rotated caption at the bottom
            state = g.Save();
            g.TranslateTransform(Theme.S(38), h - Theme.S(12));
            g.RotateTransform(-90);
            using (var b = new SolidBrush(Theme.Grey))
            using (var f = Theme.Format(StringAlignment.Near, StringAlignment.Near))
                g.DrawString("disk space finder", Theme.M(10.5f), b, 0, 0, f);
            g.Restore(state);
        }
    }

    /// <summary>Breadcrumb, live coordinates and the light / dark switch, over a hairline.</summary>
    internal sealed class HeaderBar : Painted
    {
        public string Breadcrumb = "sleuth / disk space finder";
        public string Coords = "x: 001 y: 006";
        public event EventHandler<bool> ThemePicked;
        RectangleF lightRect, darkRect;
        int hover = -1;

        public HeaderBar()
        {
            Cursor = Cursors.Default;
        }

        protected override void Draw(Graphics g)
        {
            float pad = Theme.S(20), cy = Theme.S(16);
            var mono = Theme.M(10);
            Theme.Label(g, Breadcrumb, mono, Theme.Ink, pad, cy);

            float right = Width - pad;
            float dw = Theme.Width(g, "dark", mono), sw = Theme.Width(g, " / ", mono), lw = Theme.Width(g, "light", mono);
            darkRect = new RectangleF(right - dw, cy - Theme.S(9), dw, Theme.S(18));
            lightRect = new RectangleF(darkRect.X - sw - lw, cy - Theme.S(9), lw, Theme.S(18));
            Theme.Label(g, "light", mono, !Theme.Dark ? Theme.Ink : hover == 0 ? Theme.Ink2 : Theme.Grey, lightRect.X, cy);
            Theme.Label(g, " / ", mono, Theme.Grey, lightRect.Right, cy);
            Theme.Label(g, "dark", mono, Theme.Dark ? Theme.Ink : hover == 1 ? Theme.Ink2 : Theme.Grey, darkRect.X, cy);
            var on = Theme.Dark ? darkRect : lightRect;
            Theme.Rect(g, Theme.Red, on.X, cy + Theme.S(9), on.Width, Theme.S(1.5f));

            Theme.LabelRight(g, Coords, mono, Theme.Ink, lightRect.X - Theme.S(28), cy);
            Theme.Line(g, Theme.Hair2, Theme.S(1), pad, Height - 0.5f, Width - pad, Height - 0.5f);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = lightRect.Contains(e.Location) ? 0 : darkRect.Contains(e.Location) ? 1 : -1;
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (ThemePicked != null)
            {
                if (lightRect.Contains(e.Location)) ThemePicked(this, false);
                else if (darkRect.Contains(e.Location)) ThemePicked(this, true);
            }
            base.OnMouseClick(e);
        }
    }

    /// <summary>Numbered tabs ("01 all drives", "02 c:") with the accent underline and a red pixel step.</summary>
    internal sealed class DriveTabs : Painted
    {
        public readonly List<string> Items = new List<string>();
        int selected, hover = -1;
        RectangleF[] rects = new RectangleF[0];
        public event EventHandler SelectedIndexChanged;

        public DriveTabs()
        {
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= Items.Count || value == selected) return;
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        Font Word { get { return Theme.G(26, 550); } }

        protected override void Draw(Graphics g)
        {
            rects = new RectangleF[Items.Count];
            float x = 0;
            var word = Word;
            var num = Theme.M(9.5f);
            for (int i = 0; i < Items.Count; i++)
            {
                float w = Theme.Width(g, Items[i], word);
                rects[i] = new RectangleF(x, 0, w + Theme.S(8), Height);
                Theme.Label(g, (i + 1).ToString("00"), num, Enabled ? Theme.Ink : Theme.Grey, x + Theme.S(1), Theme.S(14));
                Color c = i == selected ? Theme.Ink : i == hover && Enabled ? Theme.Ink2 : Theme.Grey;
                Theme.Label(g, Items[i], word, c, x - Theme.S(1), Theme.S(42));
                if (i == selected)
                {
                    float uy = Theme.S(62);
                    Theme.Rect(g, Theme.Blue, x, uy, w, Theme.S(3));
                    Theme.Pixels(g, Theme.Step, Theme.Red, x + w + Theme.S(3), uy - Theme.S(4), Theme.S(4));
                }
                x += w + Theme.S(42);
            }
            if (Focused && ShowFocusCues && selected < rects.Length)
                Theme.Outline(g, Theme.Red, Theme.S(1), RectangleF.Inflate(rects[selected], Theme.S(4), -Theme.S(4)));
        }

        public int PreferredWidth()
        {
            using (var g = CreateGraphics())
            {
                float x = 0;
                foreach (var s in Items) x += Theme.Width(g, s, Word) + Theme.S(42);
                return (int)Math.Ceiling(x);
            }
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            Cursor = h >= 0 && Enabled ? Cursors.Hand : Cursors.Default;
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

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled) return;
            if (e.KeyCode == Keys.Left) SelectedIndex = selected - 1;
            if (e.KeyCode == Keys.Right) SelectedIndex = selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>The one primary action: a flat yellow block, dark text, a chevron, and a yellow pixel step off its corner.</summary>
    internal sealed class ActionBlock : Painted
    {
        bool hover, down;

        public ActionBlock(string text)
        {
            Text = text;
            TabStop = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
        }

        Font TextFont { get { return Theme.G(17, 600); } }

        public int PreferredWidth()
        {
            using (var g = CreateGraphics())
                return (int)Math.Ceiling(Theme.Width(g, Text, TextFont) + Theme.S(72));
        }

        protected override void Draw(Graphics g)
        {
            float c = Theme.S(5);
            var r = new RectangleF(0, c * 2, Width - c * 2, Height - c * 2);
            if (down) r.Offset(Theme.S(1), Theme.S(1));
            Color fill = Enabled ? Theme.Yellow : Color.FromArgb(140, Theme.Yellow);
            Theme.Rect(g, fill, r.X, r.Y, r.Width, r.Height);
            Theme.Pixels(g, Theme.Corner, fill, r.Right - c, r.Y - c * 2, c);
            if (hover && Enabled) Theme.Outline(g, Theme.OnMint, Theme.S(1.5f), r);
            if (Focused && ShowFocusCues) Theme.Outline(g, Theme.Red, Theme.S(1.5f), RectangleF.Inflate(r, Theme.S(3), Theme.S(3)));

            var f = TextFont;
            float tw = Theme.Width(g, Text, f);
            float x = r.X + (r.Width - tw - Theme.S(22)) / 2, cy = r.Y + r.Height / 2;
            Theme.Label(g, Text, f, Theme.OnMint, x, cy);
            // chevron
            float ax = x + tw + Theme.S(10), s = Theme.S(4.5f);
            using (var p = new Pen(Theme.OnMint, Theme.S(1.8f)) { StartCap = LineCap.Square, EndCap = LineCap.Square })
                g.DrawLines(p, new[] { new PointF(ax, cy - s), new PointF(ax + s, cy), new PointF(ax, cy + s) });
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    /// <summary>Option words in a row: the selected one red with a red underline, the rest grey.</summary>
    internal sealed class OptionWords : Painted
    {
        readonly string caption;
        readonly string[] items;
        int selected, hover = -1;
        RectangleF[] rects = new RectangleF[0];
        public event EventHandler SelectedIndexChanged;

        public OptionWords(string caption, string[] items, int selected)
        {
            this.caption = caption;
            this.items = items;
            this.selected = selected;
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= items.Length || value == selected) return;
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        Font WordFont { get { return Theme.G(15, 500); } }

        public int PreferredWidth()
        {
            using (var g = CreateGraphics())
            {
                float x = Theme.Width(g, caption, Theme.M(10)) + Theme.S(14);
                foreach (var s in items) x += Theme.Width(g, s, WordFont) + Theme.S(13);
                return (int)Math.Ceiling(x);
            }
        }

        protected override void Draw(Graphics g)
        {
            float cy = Height / 2f;
            float x = Theme.Label(g, caption, Theme.M(10), Theme.Grey, 0, cy) + Theme.S(12);
            rects = new RectangleF[items.Length];
            var f = WordFont;
            for (int i = 0; i < items.Length; i++)
            {
                float w = Theme.Width(g, items[i], f);
                rects[i] = new RectangleF(x - Theme.S(4), 0, w + Theme.S(8), Height);
                Color c = i == selected ? Theme.Red : i == hover && Enabled ? Theme.Ink : Theme.Grey;
                Theme.Label(g, items[i], f, c, x, cy);
                if (i == selected) Theme.Rect(g, Theme.Red, x, cy + Theme.S(11), w, Theme.S(1.5f));
                x += w + Theme.S(13);
            }
            if (Focused && ShowFocusCues && selected < rects.Length)
                Theme.Outline(g, Theme.Red, Theme.S(1), RectangleF.Inflate(rects[selected], 0, -Theme.S(6)));
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < rects.Length; i++) if (rects[i].Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            Cursor = h >= 0 && Enabled ? Cursors.Hand : Cursors.Default;
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

        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!Enabled) return;
            if (e.KeyCode == Keys.Left) SelectedIndex = selected - 1;
            if (e.KeyCode == Keys.Right) SelectedIndex = selected + 1;
            base.OnKeyDown(e);
        }

        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Toggle box: a 12px square between short vertical bars, red when on, outlined when off.</summary>
    internal sealed class ToggleBox : Painted
    {
        bool isChecked, hover;
        public event EventHandler CheckedChanged;

        public ToggleBox(string text)
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

        public int PreferredWidth()
        {
            using (var g = CreateGraphics())
                return (int)Math.Ceiling(Theme.Width(g, Text, Theme.G(15, 500)) + Theme.S(52));
        }

        protected override void Draw(Graphics g)
        {
            float cy = Height / 2f;
            Theme.Line(g, Theme.Ink, Theme.S(1.5f), Theme.S(1), cy - Theme.S(10), Theme.S(1), cy + Theme.S(10));
            var box = new RectangleF(Theme.S(9), cy - Theme.S(6), Theme.S(12), Theme.S(12));
            if (isChecked) Theme.Toner(g, Theme.Red, box, 3);
            else Theme.Outline(g, Enabled ? Theme.Ink : Theme.Grey, Theme.S(1.3f), box);
            Theme.Line(g, Theme.Ink, Theme.S(1.5f), Theme.S(29), cy - Theme.S(10), Theme.S(29), cy + Theme.S(10));
            Theme.Label(g, Text, Theme.G(15, 500), !Enabled ? Theme.Grey : isChecked ? Theme.Red : hover ? Theme.Ink : Theme.Ink2, Theme.S(38), cy);
            if (Focused && ShowFocusCues) Theme.Outline(g, Theme.Red, Theme.S(1), new RectangleF(0, Theme.S(4), Width - 1, Height - Theme.S(8)));
        }

        protected override void OnClick(EventArgs e)
        {
            if (Enabled) Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && Enabled) { Checked = !Checked; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    /// <summary>Text field: hairline outline, red 1.2px outline while typing, mono "filter" label.</summary>
    internal sealed class FilterField : Painted
    {
        public readonly TextBox Box = new TextBox();

        public FilterField()
        {
            Box.BorderStyle = BorderStyle.None;
            Box.Font = Theme.G(14, 500);
            ApplyColors();
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Theme.Changed += (s, e) => ApplyColors();
            Box.HandleCreated += (s, e) =>
            {
                try { SendMessage(Box.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "type to filter"); }
                catch { }
            };
            Cursor = Cursors.IBeam;
        }

        void ApplyColors()
        {
            Box.BackColor = Theme.Paper;
            Box.ForeColor = Theme.Ink;
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int left = Theme.Si(58);
            Box.SetBounds(left, (Height - Box.PreferredHeight) / 2 + Theme.Si(1), Math.Max(10, Width - left - Theme.Si(10)), Box.PreferredHeight);
            base.OnLayout(e);
        }

        protected override void Draw(Graphics g)
        {
            var r = new RectangleF(Theme.S(1), Theme.S(6), Width - Theme.S(2), Height - Theme.S(12));
            if (Box.Focused) Theme.Outline(g, Theme.Red, Theme.S(1.2f), r);
            else Theme.Outline(g, Theme.Hair, Theme.S(1), r);
            Theme.Label(g, "filter", Theme.M(10), Theme.Grey, r.X + Theme.S(10), r.Y + r.Height / 2);
            Theme.Line(g, Theme.Hair2, Theme.S(1), r.X + Theme.S(48), r.Y + Theme.S(6), r.X + Theme.S(48), r.Bottom - Theme.S(6));
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }

    /// <summary>Mono status line above the bottom hairline, with a stepping red progress bar while scanning.</summary>
    internal sealed class StatusLine : Painted, IAnimated
    {
        string status = "";
        public bool Busy;
        public string RightText = "";

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
            var mono = Theme.M(10);
            float cy = Height / 2f;
            float right = Width;
            right -= Theme.LabelRight(g, RightText, mono, Theme.Ink, right, cy) + Theme.S(16);
            if (Busy)
            {
                float bw = Theme.S(120);
                // hard, stepped motion: snaps between positions
                int steps = 8;
                int i = Theme.ReducedMotion ? steps / 2 : (int)(Theme.Now * 7) % (steps * 2);
                if (i >= steps) i = steps * 2 - i - 1;
                float seg = bw / 4;
                Theme.Rect(g, Theme.Pink, right - bw, cy - Theme.S(3), bw, Theme.S(6));
                Theme.Rect(g, Theme.Red, right - bw + (bw - seg) * i / (steps - 1), cy - Theme.S(3), seg, Theme.S(6));
                right -= bw + Theme.S(16);
            }
            Theme.Text(g, Theme.Lc(status), mono, Theme.Ink, new RectangleF(0, 0, Math.Max(0, right), Height));
        }
    }

    /// <summary>fig. 02: the most-likely line (a red number box + the place), a summary line and any notes.</summary>
    internal sealed class Headline : Painted
    {
        public string Caption = "fig. 02 / most likely";
        public string Plain;
        public string Amount;
        public string Target;
        public string Summary;
        public readonly List<string> Notes = new List<string>();

        Font Big { get { return Theme.G(27, 550); } }
        Font NoteFont { get { return Theme.M(10); } }

        public int Measure(int width)
        {
            using (var g = CreateGraphics())
            {
                float h = Theme.S(22) + Theme.S(44);
                if (!string.IsNullOrEmpty(Summary)) h += Theme.S(20);
                foreach (var n in Notes) h += Theme.WrappedHeight(g, Theme.Lc(n), NoteFont, width - Theme.S(20)) + Theme.S(5);
                return (int)Math.Ceiling(h + Theme.S(10));
            }
        }

        protected override void Draw(Graphics g)
        {
            var mono = Theme.M(9.5f);
            Theme.Label(g, Theme.Lc(Caption), mono, Theme.Ink2, 0, Theme.S(9));
            float y = Theme.S(22), lineH = Theme.S(44), cy = y + lineH / 2;
            var big = Big;
            if (Amount != null && Target != null)
            {
                // number box, filled red because the value moved away from neutral
                var nf = Theme.M(17, true);
                string amt = Theme.Lc(Amount);
                float bw = Theme.Width(g, amt, nf) + Theme.S(22);
                var box = new RectangleF(0, cy - Theme.S(17), bw, Theme.S(34));
                Theme.Rect(g, Theme.Red, box.X, box.Y, box.Width, box.Height);
                Theme.Text(g, amt, nf, Color.White, box, StringAlignment.Center);
                float x = box.Right + Theme.S(14);
                x += Theme.Label(g, "went to", big, Theme.Grey, x, cy) + Theme.S(8);
                Theme.Text(g, Theme.Lc(Target) + ".", big, Theme.Ink, new RectangleF(x, cy - big.Height, Width - x, big.Height * 2));
            }
            else
            {
                Theme.Text(g, Theme.Lc(Plain ?? ""), big, Theme.Ink, new RectangleF(0, cy - big.Height, Width, big.Height * 2));
            }
            y += lineH;
            if (!string.IsNullOrEmpty(Summary))
            {
                Theme.Text(g, Theme.Lc(Summary), Theme.M(10), Theme.Ink2, new RectangleF(0, y, Width, Theme.S(18)));
                y += Theme.S(20);
            }
            foreach (var n in Notes)
            {
                string t = Theme.Lc(n);
                float th = Theme.WrappedHeight(g, t, NoteFont, Width - Theme.S(20));
                Theme.Rect(g, Theme.Red, 0, y + Theme.S(4), Theme.S(6), Theme.S(6));
                Theme.Text(g, t, NoteFont, Theme.Ink2, new RectangleF(Theme.S(16), y, Width - Theme.S(20), th + Theme.S(2)),
                    StringAlignment.Near, StringAlignment.Near, false, true);
                y += th + Theme.S(5);
            }
        }
    }
}
