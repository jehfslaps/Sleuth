using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>
    /// A smooth-scrolling list of custom-painted rows/cards with a slim pill scrollbar,
    /// mouse + keyboard selection and double-click / Enter to activate.
    /// </summary>
    internal abstract class ScrollList : Painted, IAnimated
    {
        int[] tops = new int[0];
        int[] heights = new int[0];
        int contentHeight;
        float scrollY, targetY;
        int selected = -1, hover = -1;
        bool draggingThumb;
        float dragOffset;

        public event EventHandler SelectionChanged;
        public event EventHandler ItemActivated;

        protected ScrollList()
        {
            TabStop = true;
            SetStyle(ControlStyles.Selectable, true);
        }

        protected abstract int ItemCount { get; }
        protected abstract int MeasureItem(int index, int width);
        protected abstract void PaintItem(Graphics g, int index, RectangleF r, bool selected, bool hover);
        protected virtual bool IsSelectable(int index) { return true; }
        protected virtual void PaintEmpty(Graphics g) { }
        protected virtual int PadX { get { return Theme.Si(4); } }
        protected virtual int Gap { get { return Theme.Si(10); } }
        protected virtual int PadTop { get { return Theme.Si(4); } }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < -1 || value >= ItemCount) value = -1;
                if (value >= 0 && !IsSelectable(value)) return;
                if (value == selected) return;
                selected = value;
                EnsureVisible(value);
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        int ScrollbarSpace { get { return Theme.Si(14); } }

        public void Relayout()
        {
            int n = ItemCount;
            tops = new int[n];
            heights = new int[n];
            int y = PadTop, w = Math.Max(10, Width - PadX * 2 - ScrollbarSpace);
            for (int i = 0; i < n; i++)
            {
                tops[i] = y;
                heights[i] = MeasureItem(i, w);
                y += heights[i] + Gap;
            }
            contentHeight = y + Theme.Si(10);
            if (selected >= n) selected = -1;
            targetY = scrollY = Clamp(scrollY);
            Invalidate();
        }

        public void ResetScroll()
        {
            scrollY = targetY = 0;
            hover = -1;
        }

        float MaxScroll { get { return Math.Max(0, contentHeight - Height); } }
        float Clamp(float v) { return Math.Max(0, Math.Min(MaxScroll, v)); }

        void ScrollTo(float y, bool animate)
        {
            targetY = Clamp(y);
            if (!animate || Theme.ReducedMotion) scrollY = targetY;
            Invalidate();
        }

        public void EnsureVisible(int i)
        {
            if (i < 0 || i >= tops.Length) return;
            if (tops[i] < targetY) ScrollTo(tops[i] - PadTop, true);
            else if (tops[i] + heights[i] > targetY + Height) ScrollTo(tops[i] + heights[i] - Height + Theme.Si(12), true);
        }

        public virtual void Tick(float now)
        {
            if (Math.Abs(targetY - scrollY) > 0.5f)
            {
                scrollY += (targetY - scrollY) * 0.28f;
                if (Math.Abs(targetY - scrollY) <= 0.5f) scrollY = targetY;
                Invalidate();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        RectangleF ItemRect(int i)
        {
            return new RectangleF(PadX, tops[i] - scrollY, Width - PadX * 2 - ScrollbarSpace, heights[i]);
        }

        RectangleF ThumbRect()
        {
            if (contentHeight <= Height) return RectangleF.Empty;
            float trackH = Height - Theme.S(8);
            float h = Math.Max(Theme.S(36), trackH * Height / contentHeight);
            float y = Theme.S(4) + (trackH - h) * (scrollY / Math.Max(1, MaxScroll));
            return new RectangleF(Width - Theme.S(10), y, Theme.S(6), h);
        }

        protected override void Draw(Graphics g)
        {
            if (ItemCount == 0)
            {
                PaintEmpty(g);
                return;
            }
            for (int i = 0; i < tops.Length; i++)
            {
                var r = ItemRect(i);
                if (r.Bottom < -Theme.S(10)) continue;
                if (r.Top > Height) break;
                PaintItem(g, i, r, i == selected, i == hover);
            }
            var thumb = ThumbRect();
            if (!thumb.IsEmpty) Theme.Fill(g, Theme.InkA(draggingThumb ? 90 : 45), thumb, thumb.Width / 2);
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < tops.Length; i++)
                if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollTo(targetY - e.Delta / 120f * Theme.S(90), true);
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var thumb = ThumbRect();
            if (!thumb.IsEmpty && e.X >= Width - ScrollbarSpace)
            {
                if (thumb.Contains(e.Location))
                {
                    draggingThumb = true;
                    dragOffset = e.Y - thumb.Y;
                }
                else ScrollTo(targetY + (e.Y < thumb.Y ? -Height : Height) * 0.9f, true);
                base.OnMouseDown(e);
                return;
            }
            int i = HitTest(e.Location);
            if (i >= 0 && IsSelectable(i)) SelectedIndex = i;
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (draggingThumb)
            {
                float trackH = Height - Theme.S(8);
                var thumb = ThumbRect();
                float frac = (e.Y - dragOffset - Theme.S(4)) / Math.Max(1, trackH - thumb.Height);
                ScrollTo(frac * MaxScroll, false);
            }
            else
            {
                int h = HitTest(e.Location);
                if (h >= 0 && !IsSelectable(h)) h = -1;
                if (h != hover) { hover = h; Invalidate(); }
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (draggingThumb) { draggingThumb = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (hover != -1) { hover = -1; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = HitTest(e.Location);
            if (e.Button == MouseButtons.Left && i >= 0 && i == selected && ItemActivated != null) ItemActivated(this, EventArgs.Empty);
            base.OnMouseDoubleClick(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int n = ItemCount;
            switch (e.KeyCode)
            {
                case Keys.Down: MoveBy(1); e.Handled = true; break;
                case Keys.Up: MoveBy(-1); e.Handled = true; break;
                case Keys.PageDown: MoveBy(6); e.Handled = true; break;
                case Keys.PageUp: MoveBy(-6); e.Handled = true; break;
                case Keys.Home: SelectFirstFrom(0, 1); e.Handled = true; break;
                case Keys.End: SelectFirstFrom(n - 1, -1); e.Handled = true; break;
                case Keys.Enter:
                    if (selected >= 0 && ItemActivated != null) ItemActivated(this, EventArgs.Empty);
                    e.Handled = true;
                    break;
            }
            base.OnKeyDown(e);
        }

        void MoveBy(int delta)
        {
            int step = Math.Sign(delta), remaining = Math.Abs(delta);
            int i = selected < 0 ? (step > 0 ? -1 : ItemCount) : selected;
            int last = selected;
            while (remaining > 0)
            {
                i += step;
                if (i < 0 || i >= ItemCount) break;
                if (IsSelectable(i)) { last = i; remaining--; }
            }
            if (last >= 0) SelectedIndex = last;
        }

        void SelectFirstFrom(int start, int step)
        {
            for (int i = start; i >= 0 && i < ItemCount; i += step)
                if (IsSelectable(i)) { SelectedIndex = i; return; }
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    }

    internal enum ListMood { Idle, Scanning, Empty, Stopped, Error }

    /// <summary>The ranked result cards (most likely first), then the system-space cards.</summary>
    internal sealed class ResultList : ScrollList
    {
        sealed class Entry
        {
            public string Header, HeaderNote;
            public Culprit C;
            public SystemArea A;
        }

        readonly List<Entry> entries = new List<Entry>();
        public ListMood State = ListMood.Idle;
        public string MoodTitle = "Ready when you are.";
        public string MoodText = "Pick a time window and press “Find my space”.";

        protected override int ItemCount { get { return entries.Count; } }
        protected override int PadX { get { return Theme.Si(32); } }
        protected override int Gap { get { return Theme.Si(12); } }

        public object SelectedTag
        {
            get
            {
                int i = SelectedIndex;
                if (i < 0 || i >= entries.Count) return null;
                return (object)entries[i].C ?? entries[i].A;
            }
        }

        public void SetResults(IEnumerable<Culprit> culprits, IEnumerable<SystemArea> areas)
        {
            entries.Clear();
            var cs = culprits.ToList();
            var asx = areas.ToList();
            if (cs.Count > 0)
            {
                entries.Add(new Entry { Header = "Most likely → least likely", HeaderNote = cs.Count(c => c.Kind != CulpritKind.Scattered) + " places" });
                foreach (var c in cs) entries.Add(new Entry { C = c });
            }
            if (asx.Count > 0)
            {
                entries.Add(new Entry { Header = "System space", HeaderNote = "restore points, hibernation, page file, Recycle Bin" });
                foreach (var a in asx) entries.Add(new Entry { A = a });
            }
            ResetScroll();
            Relayout();
            SelectFirst();
        }

        public void SelectFirst()
        {
            for (int i = 0; i < entries.Count; i++)
                if (IsSelectable(i)) { SelectedIndex = i; return; }
            SelectedIndex = -1;
        }

        public void Clear()
        {
            entries.Clear();
            SelectedIndex = -1;
            ResetScroll();
            Relayout();
        }

        protected override bool IsSelectable(int index) { return entries[index].Header == null; }

        protected override int MeasureItem(int index, int width)
        {
            return entries[index].Header != null ? Theme.Si(index == 0 ? 30 : 44) : Theme.Si(108);
        }

        public override void Tick(float now)
        {
            base.Tick(now);
            if (ItemCount == 0 && !Theme.ReducedMotion) Invalidate();
        }

        protected override void PaintItem(Graphics g, int index, RectangleF r, bool selected, bool hover)
        {
            var e = entries[index];
            if (e.Header != null)
            {
                var hf = Theme.Ui(15, Weight.Bold);
                float hw = Theme.Width(g, e.Header, hf);
                Theme.Text(g, e.Header, hf, Theme.Ink, new RectangleF(r.X + Theme.S(4), r.Bottom - Theme.S(26), hw + Theme.S(4), Theme.S(24)));
                Theme.Text(g, e.HeaderNote, Theme.Ui(12.5f, Weight.Regular), Theme.InkA(130),
                    new RectangleF(r.X + hw + Theme.S(16), r.Bottom - Theme.S(26), r.Width - hw - Theme.S(20), Theme.S(24)));
                return;
            }
            if (e.C != null) PaintCulprit(g, e.C, r, selected, hover);
            else PaintArea(g, e.A, r, selected, hover);
        }

        void CardBack(Graphics g, Hue hue, RectangleF r, bool selected, bool hover)
        {
            float rad = Theme.S(26);
            if (selected)
            {
                Theme.Card(g, hue.Tint, r, rad, 20);
                Theme.Stroke(g, Color.FromArgb(Focused ? 200 : 120, hue.Base), Theme.S(2), r, rad);
            }
            else Theme.Card(g, hover ? Theme.Hex("#FFFEFA") : Theme.White, r, rad, hover ? 30 : 16);
        }

        void Badge(Graphics g, Hue hue, RectangleF r, string text, bool sparkle)
        {
            float d = Theme.S(46);
            var b = new RectangleF(r.X + Theme.S(18), r.Y + (r.Height - d) / 2, d, d);
            using (var br = new SolidBrush(hue.Base)) g.FillEllipse(br, b);
            if (sparkle) Theme.Sparkle(g, b.X + d / 2, b.Y + d / 2, d * 0.28f, Color.White);
            else Theme.Text(g, text, Theme.Ui(text.Length > 2 ? 14 : 18, Weight.Bold), Color.White, b, StringAlignment.Center);
        }

        void PaintCulprit(Graphics g, Culprit c, RectangleF r, bool selected, bool hover)
        {
            var hue = Theme.For(c);
            CardBack(g, hue, r, selected, hover);
            Badge(g, hue, r, c.Kind == CulpritKind.Scattered ? "…" : c.Rank.ToString(), c.Kind == CulpritKind.SystemArea);

            float rightW = Theme.S(170);
            float x = r.X + Theme.S(80), right = r.Right - rightW - Theme.S(16);

            // Right: amount + likelihood bar
            string amount = c.AddedBytes > 0 ? Format.Bytes(c.AddedBytes) : c.PossibleBytes > 0 ? "up to " + Format.Bytes(c.PossibleBytes) : "—";
            var amountFont = Theme.Ui(c.AddedBytes > 0 ? 24 : 17, c.AddedBytes > 0 ? Weight.Semibold : Weight.Medium);
            var rr = new RectangleF(r.Right - rightW - Theme.S(20), r.Y + Theme.S(16), rightW, Theme.S(34));
            Theme.Text(g, amount, amountFont, hue.Dark, rr, StringAlignment.Far);
            float bw = Theme.S(124), bh = Theme.S(8);
            var bar = new RectangleF(r.Right - Theme.S(20) - bw, r.Y + Theme.S(60), bw, bh);
            Theme.Fill(g, selected ? Theme.White : hue.Tint, bar, bh / 2);
            float fw = Math.Max(bh, (float)(bw * Math.Min(1, c.Likelihood)));
            Theme.Fill(g, hue.Base, new RectangleF(bar.X, bar.Y, fw, bh), bh / 2);
            Theme.Text(g, Format.Percent(c.Likelihood) + " likely", Theme.Ui(12, Weight.Medium), Theme.InkA(150),
                new RectangleF(r.Right - rightW - Theme.S(20), r.Y + Theme.S(74), rightW, Theme.S(18)), StringAlignment.Far);

            // Middle: name, path, chips
            string title, sub;
            if (c.Kind == CulpritKind.Folder)
            {
                string p = c.Path.TrimEnd('\\');
                int cut = p.LastIndexOf('\\');
                title = cut >= 0 && cut < p.Length - 1 ? p.Substring(cut + 1) : c.Path;
                sub = c.Path;
            }
            else
            {
                title = c.Kind == CulpritKind.SystemArea ? c.Path : "Little bits, here and there";
                sub = c.Kind == CulpritKind.SystemArea ? c.Note : c.Label;
            }
            Theme.Text(g, title, Theme.Ui(17, Weight.Bold), Theme.Ink, new RectangleF(x, r.Y + Theme.S(14), right - x, Theme.S(26)));
            Theme.Text(g, sub, Theme.Ui(12.5f, Weight.Regular), Theme.InkA(140), new RectangleF(x, r.Y + Theme.S(40), right - x, Theme.S(20)),
                StringAlignment.Near, StringAlignment.Center, c.Kind == CulpritKind.Folder);

            float cy = r.Y + Theme.S(68), cx = x;
            var chips = new List<KeyValuePair<string, Hue>>();
            if (c.Kind == CulpritKind.Folder)
            {
                chips.Add(Pair(Labeler.CategoryName(Labeler.CategoryOf(c)), hue));
                if (c.IsNewFolder) chips.Add(Pair("New folder", Theme.HNeutral));
                if (c.HiddenReason != null) chips.Add(Pair("Hidden spot", Theme.HCoral));
                chips.Add(Pair(c.Label, Theme.HNeutral));
                if (!string.IsNullOrEmpty(c.Contents)) chips.Add(Pair(ShortContents(c.Contents), Theme.HNeutral));
                if (c.Confidence != "High") chips.Add(Pair("Partly a guess", Theme.HNeutral));
            }
            else if (c.Kind == CulpritKind.Scattered)
            {
                chips.Add(Pair((c.NewFiles + c.GrewFiles).ToString("N0") + " files", Theme.HMint));
            }
            else
            {
                chips.Add(Pair("System", Theme.HPurple));
            }
            chips.Add(Pair(WhenText(c), Theme.HNeutral));
            foreach (var chip in chips)
            {
                float used = Theme.Chip(g, cx, cy, chip.Key, chip.Value, right - cx);
                if (used <= 0) break;
                cx += used + Theme.S(6);
            }
        }

        static string WhenText(Culprit c)
        {
            if (c.Kind == CulpritKind.SystemArea) return "since last scan";
            string a = Format.When(c.FirstUtc), b = Format.When(c.LastUtc);
            return a == b ? b : a + " → " + b;
        }

        static string ShortContents(string s)
        {
            // "Mostly audio samples / instrument data (.wav 84%)" -> "Mostly .wav audio samples"
            return s.Length > 46 ? s.Substring(0, 44) + "…" : s;
        }

        static KeyValuePair<string, Hue> Pair(string s, Hue h) { return new KeyValuePair<string, Hue>(s, h); }

        void PaintArea(Graphics g, SystemArea a, RectangleF r, bool selected, bool hover)
        {
            var hue = Theme.HPurple;
            CardBack(g, hue, r, selected, hover);
            Badge(g, hue, r, "", true);
            float rightW = Theme.S(170);
            float x = r.X + Theme.S(80), right = r.Right - rightW - Theme.S(16);

            string change = a.Delta.HasValue ? (a.Delta.Value == 0 ? "no change" : Format.SignedBytes(a.Delta.Value)) : "first look";
            bool grew = a.Delta.HasValue && a.Delta.Value > 0;
            Theme.Text(g, change, Theme.Ui(grew ? 22 : 16, grew ? Weight.Semibold : Weight.Medium), grew ? hue.Dark : Theme.InkA(150),
                new RectangleF(r.Right - rightW - Theme.S(20), r.Y + Theme.S(18), rightW, Theme.S(32)), StringAlignment.Far);
            Theme.Text(g, a.PreviousUtc.HasValue ? "vs. " + Format.When(a.PreviousUtc.Value) : "next scan shows the change",
                Theme.Ui(12, Weight.Medium), Theme.InkA(140), new RectangleF(r.Right - rightW - Theme.S(20), r.Y + Theme.S(56), rightW, Theme.S(18)), StringAlignment.Far);

            Theme.Text(g, a.Drive + "  " + a.Name, Theme.Ui(17, Weight.Bold), Theme.Ink, new RectangleF(x, r.Y + Theme.S(14), right - x, Theme.S(26)));
            Theme.Text(g, "Uses " + Format.Bytes(a.CurrentBytes) + " right now", Theme.Ui(12.5f, Weight.Regular), Theme.InkA(140),
                new RectangleF(x, r.Y + Theme.S(40), right - x, Theme.S(20)));
            float cx = x, cy = r.Y + Theme.S(68);
            if (a.ActiveInWindow) cx += Theme.Chip(g, cx, cy, "Busy in this window", Theme.HOrange, right - cx) + Theme.S(6);
            if (!string.IsNullOrEmpty(a.Activity)) Theme.Chip(g, cx, cy, a.Activity, Theme.HNeutral, right - cx);
        }

        protected override void PaintEmpty(Graphics g)
        {
            float size = Math.Min(Theme.S(150), Height * 0.42f);
            float cx = Width / 2f, cy = Height * 0.38f;
            float t = Theme.Now;
            bool anim = !Theme.ReducedMotion;

            // a little confetti behind the mascot
            Theme.Flower(g, cx - size * 1.1f, cy - size * 0.45f, Theme.S(14), Theme.Sun, Theme.Orange);
            Theme.Sparkle(g, cx + size * 1.15f, cy - size * 0.5f, Theme.S(12), Theme.Purple);
            Theme.Clover(g, cx + size * 1.0f, cy + size * 0.45f, Theme.S(14), Theme.Mint);
            Theme.Starburst(g, cx - size * 0.95f, cy + size * 0.5f, Theme.S(12), Theme.Pink);

            Mood m = State == ListMood.Scanning ? Mood.Searching : State == ListMood.Error ? Mood.Worried :
                     State == ListMood.Empty ? Mood.Grin : State == ListMood.Stopped ? Mood.Calm : Mood.Happy;
            Mascot.DrawBody(g, cx - size * 0.15f, cy, size, m, t, false, anim);

            float ty = cy + size * 0.75f;
            Theme.Text(g, MoodTitle, Theme.Ui(20, Weight.Bold), Theme.Ink, new RectangleF(Theme.S(24), ty, Width - Theme.S(48), Theme.S(30)), StringAlignment.Center);
            Theme.Text(g, MoodText, Theme.Ui(14, Weight.Regular), Theme.InkA(150),
                new RectangleF(Theme.S(40), ty + Theme.S(34), Width - Theme.S(80), Theme.S(44)), StringAlignment.Center, StringAlignment.Near, false, true);
        }
    }

    /// <summary>The files inside the selected result.</summary>
    internal sealed class FileList : ScrollList
    {
        List<ChangedFile> files = new List<ChangedFile>();

        public FileList()
        {
            BackColor = Theme.White;
        }

        protected override int ItemCount { get { return files.Count; } }
        protected override int Gap { get { return Theme.Si(2); } }
        protected override int PadTop { get { return 0; } }

        public ChangedFile SelectedFile
        {
            get { return SelectedIndex >= 0 && SelectedIndex < files.Count ? files[SelectedIndex] : null; }
        }

        public void SetFiles(List<ChangedFile> list)
        {
            files = list ?? new List<ChangedFile>();
            SelectedIndex = -1;
            ResetScroll();
            Relayout();
        }

        protected override int MeasureItem(int index, int width) { return Theme.Si(52); }

        protected override void PaintItem(Graphics g, int index, RectangleF r, bool selected, bool hover)
        {
            var f = files[index];
            if (selected) Theme.Fill(g, Theme.Hex("#FFF1D2"), r, Theme.S(14));
            else if (hover) Theme.Fill(g, Theme.Hex("#FFF9EC"), r, Theme.S(14));

            float rightW = Theme.S(150);
            float x = r.X + Theme.S(12), right = r.Right - rightW - Theme.S(8);
            Theme.Text(g, f.Name, Theme.Ui(14, Weight.Semibold), Theme.Ink, new RectangleF(x, r.Y + Theme.S(6), right - x, Theme.S(22)));
            Theme.Text(g, Format.When(f.TimeUtc) + "  ·  " + f.Folder, Theme.Ui(12, Weight.Regular), Theme.InkA(130),
                new RectangleF(x, r.Y + Theme.S(28), right - x, Theme.S(18)), StringAlignment.Near, StringAlignment.Center, true);

            Theme.Text(g, Format.Bytes(f.SizeOnDisk), Theme.Ui(14, Weight.Semibold), Theme.Ink,
                new RectangleF(r.Right - rightW - Theme.S(12), r.Y + Theme.S(6), rightW, Theme.S(22)), StringAlignment.Far);
            string change; Hue hue;
            switch (f.Kind)
            {
                case ChangeKind.New: change = "New file"; hue = Theme.HMint; break;
                case ChangeKind.GrewKnown: change = "Grew " + Format.SignedBytes(f.AddedBytes); hue = Theme.HOrange; break;
                default: change = "Modified"; hue = Theme.HCoral; break;
            }
            float cw = Theme.Width(g, change, Theme.Ui(12, Weight.Medium)) + Theme.S(34);
            Theme.Chip(g, r.Right - Theme.S(12) - cw, r.Y + Theme.S(28), change, hue, cw, 20);
        }

        protected override void PaintEmpty(Graphics g)
        {
            Theme.Text(g, "No individual files to show here.", Theme.Ui(13, Weight.Regular), Theme.InkA(130),
                new RectangleF(0, 0, Width, Theme.S(40)), StringAlignment.Center);
        }
    }
}
