using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>A clickable word with an arrow: grotesk, ink, red underline on hover.</summary>
    internal sealed class LinkWord : Painted
    {
        bool hover;
        public string Arrow = "↗";

        public LinkWord(string text)
        {
            Text = text;
            TabStop = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.Selectable, true);
        }

        public int PreferredWidth()
        {
            using (var g = CreateGraphics())
                return (int)Math.Ceiling(Theme.Width(g, Text + " " + Arrow, Theme.G(15, 600)) + Theme.S(8));
        }

        protected override void Draw(Graphics g)
        {
            float cy = Height / 2f;
            var f = Theme.G(15, 600);
            float w = Theme.Label(g, Text, f, hover ? Theme.Red : Theme.Ink, 0, cy);
            Theme.Label(g, " " + Arrow, Theme.G(15, 500), hover ? Theme.Red : Theme.Ink, w - Theme.S(2), cy);
            if (hover || (Focused && ShowFocusCues)) Theme.Rect(g, Theme.Red, 0, cy + Theme.S(11), w, Theme.S(1.5f));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }
    }

    /// <summary>fig. 03: what the selected place is, a spec list, actions, and the files inside.</summary>
    internal sealed class DetailPanel : Painted
    {
        public readonly FileList Files = new FileList();
        public readonly LinkWord OpenButton = new LinkWord("open in file explorer");
        public readonly LinkWord CopyButton = new LinkWord("copy path") { Arrow = "→" };
        public string PlaceholderTitle = "pick a row.";

        object current;
        string title, path, info, filesHeader;
        Color square = Color.Empty;
        readonly List<KeyValuePair<string, Color>> tags = new List<KeyValuePair<string, Color>>();
        readonly List<KeyValuePair<string, string>> spec = new List<KeyValuePair<string, string>>();
        bool hasFiles, showOpen, showCopy, placeholder;

        RectangleF titleRect, pathRect, tagRect, specRect, infoRect, filesHeaderRect;
        int specShown;

        public DetailPanel()
        {
            Controls.Add(Files);
            Controls.Add(OpenButton);
            Controls.Add(CopyButton);
            SetItem(null, null);
        }

        public object Current { get { return current; } }

        public void SetItem(object tag, SystemArea areaOfCulprit)
        {
            current = tag;
            title = path = info = filesHeader = null;
            tags.Clear();
            spec.Clear();
            square = Color.Empty;
            showOpen = showCopy = false;
            placeholder = false;
            List<ChangedFile> fileList = null;

            var c = tag as Culprit;
            var a = tag as SystemArea;
            if (c != null)
            {
                var lines = new List<string>();
                square = Theme.For(c);
                tags.Add(T(Labeler.CategoryName(Labeler.CategoryOf(c)), Theme.Grey));
                if (c.Kind == CulpritKind.Folder)
                {
                    string p = c.Path.TrimEnd('\\');
                    int cut = p.LastIndexOf('\\');
                    title = (cut >= 0 && cut < p.Length - 1 ? p.Substring(cut + 1) : c.Path) + ".";
                    path = c.Path;
                    tags.Add(T(c.Label, Theme.Grey));
                    if (c.IsNewFolder) tags.Add(T("new folder", Theme.Grey));
                    if (c.HiddenReason != null) tags.Add(T("hidden spot", Theme.Red));
                    showOpen = showCopy = true;
                    OpenButton.Text = "open in file explorer";
                    if (c.HiddenReason != null) lines.Add(c.HiddenReason + " — easy to miss.");
                }
                else if (c.Kind == CulpritKind.SystemArea)
                {
                    title = c.Label + ".";
                    path = c.Note;
                    if (c.NewFiles > 0) tags.Add(T(c.NewFiles == 1 ? "new restore point" : c.NewFiles + " new restore points", Theme.Red));
                    if (areaOfCulprit != null && !string.IsNullOrEmpty(areaOfCulprit.Explanation)) lines.Add(areaOfCulprit.Explanation);
                    SetOpenText(c.OpenTarget);
                }
                else
                {
                    title = "little bits, here and there.";
                    path = c.Note;
                    lines.Add("app updates, caches and logs each added a small amount. none of them is a big culprit on its own.");
                }

                if (c.AddedBytes > 0) S("added", Format.Bytes(c.AddedBytes));
                else if (c.EstimateBytes > 0) S("about (estimate)", "~" + Format.Bytes(c.EstimateBytes));
                else if (c.PossibleBytes > 0) S("maybe", "up to " + Format.Bytes(c.PossibleBytes));
                else S("added", c.Kind == CulpritKind.SystemArea ? "~0" : "—");
                if (c.Kind == CulpritKind.SystemArea && areaOfCulprit != null) S("uses now", Format.Bytes(areaOfCulprit.CurrentBytes));
                if (c.Kind != CulpritKind.SystemArea) S("new files", c.NewFiles.ToString("N0"));
                if (c.GrewFiles > 0) S("existing files that grew", c.GrewFiles.ToString("N0"));
                S("likelihood", Format.Percent(c.Likelihood) + " · " + c.Confidence.ToLowerInvariant() + " confidence");
                if (c.Kind == CulpritKind.Folder && !string.IsNullOrEmpty(c.Contents)) S("contents", c.Contents);
                if (c.Kind != CulpritKind.SystemArea)
                {
                    string a1 = Format.When(c.FirstUtc), b1 = Format.When(c.LastUtc);
                    S("when", a1 == b1 ? b1 : a1 + " → " + b1);
                }
                if (c.Kind == CulpritKind.Folder && !string.IsNullOrEmpty(c.Note) && !c.IsNewFolder) lines.Add(c.Note + ".");
                if (c.GrewFiles > 0 && c.PossibleBytes > 0)
                    lines.Add("existing files also grew (now " + Format.Bytes(c.PossibleBytes) + " in total). their earlier size isn't known yet, so that part is a guess. your next scan measures it exactly.");
                info = string.Join("\n", lines);

                if (c.Files.Count > 0)
                {
                    fileList = c.Files;
                    int total = c.NewFiles + c.GrewFiles;
                    filesHeader = total > c.Files.Count
                        ? c.Files.Count.ToString("N0") + " biggest of " + total.ToString("N0")
                        : total.ToString("00");
                }
            }
            else if (a != null)
            {
                square = Theme.For(Category.System);
                title = a.Drive + " " + a.Name + ".";
                path = "uses " + Format.Bytes(a.CurrentBytes) + " right now";
                tags.Add(T("system", Theme.Grey));
                if (a.ActiveInWindow) tags.Add(T("busy in this window", Theme.Red));
                S("uses now", Format.Bytes(a.CurrentBytes));
                S("change", a.Delta.HasValue ? (a.Delta.Value == 0 ? "none" : Format.SignedBytes(a.Delta.Value)) : "first look");
                S("compared with", a.PreviousUtc.HasValue ? Format.When(a.PreviousUtc.Value) : "next scan");
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(a.Activity)) lines.Add(a.Activity + ".");
                if (!string.IsNullOrEmpty(a.Explanation)) lines.Add(a.Explanation);
                if (!a.Delta.HasValue) lines.Add("first time this was measured. the next scan shows how much it changed.");
                info = string.Join("\n", lines);
                SetOpenText(a.OpenTarget);
            }
            else
            {
                placeholder = true;
                title = PlaceholderTitle;
                path = "the files inside each place show up here. the colored square says what kind of place it is:";
                foreach (Category cat in Enum.GetValues(typeof(Category)))
                    spec.Add(new KeyValuePair<string, string>("■ " + Labeler.CategoryName(cat), ""));
            }

            Files.SetFiles(fileList);
            hasFiles = fileList != null;
            Files.Visible = hasFiles;
            OpenButton.Visible = showOpen;
            CopyButton.Visible = showCopy;
            PerformLayout();
            Invalidate();
        }

        static KeyValuePair<string, Color> T(string s, Color c) { return new KeyValuePair<string, Color>(Theme.Lc(s), c); }
        void S(string k, string v) { spec.Add(new KeyValuePair<string, string>(k, v)); }

        void SetOpenText(string target)
        {
            if (string.IsNullOrEmpty(target)) return;
            OpenButton.Text = target.IndexOf("RecycleBin", StringComparison.OrdinalIgnoreCase) >= 0 ? "open recycle bin"
                            : target.StartsWith("cleanmgr", StringComparison.OrdinalIgnoreCase) ? "open disk cleanup"
                            : "open settings";
            showOpen = true;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width <= 0 || Height <= 0) return;
            using (var g = CreateGraphics())
            {
                float x = Theme.S(22), w = Width - x - Theme.S(8);
                float y = Theme.S(30);
                titleRect = new RectangleF(x, y, w, Theme.S(34));
                y += Theme.S(36);
                pathRect = new RectangleF(x, y, w, Theme.S(placeholder ? 34 : 16));
                y += Theme.S(placeholder ? 40 : 20);
                tagRect = new RectangleF(x, y, w, Theme.S(16));
                y += Theme.S(28);

                float rowH = Theme.S(22);
                int bh = Theme.Si(30);
                float actions = showOpen || showCopy ? bh + Theme.S(14) : 0;
                float filesMin = hasFiles ? Theme.S(36 + 150) : 0;
                var infoFont = Theme.G(13, 500);
                float lineH = infoFont.GetHeight(g) + Theme.S(2);
                float infoFull = string.IsNullOrEmpty(info) ? 0 : Theme.WrappedHeight(g, Theme.Lc(info), infoFont, w);

                // files and actions win; then the spec rows; then the explanation
                float room = Height - Theme.S(10) - y - actions - filesMin;
                specShown = Math.Max(0, Math.Min(spec.Count, (int)((room - (infoFull > 0 ? lineH * 2 : 0)) / rowH)));
                if (placeholder) specShown = spec.Count;
                specRect = new RectangleF(x, y, w, specShown * rowH);
                y += specShown * rowH + (specShown > 0 ? Theme.S(12) : 0);
                room -= specShown * rowH;

                if (infoFull > 0 && room >= lineH)
                {
                    float maxInfo = hasFiles ? Math.Min(room - Theme.S(6), lineH * 4) : room - Theme.S(6);
                    float ih = Math.Min(infoFull, Math.Max(lineH, maxInfo));
                    ih = Math.Max(lineH, (float)Math.Floor(ih / lineH + 0.05f) * lineH);
                    infoRect = new RectangleF(x, y, w, ih);
                    y += ih + Theme.S(12);
                }
                else infoRect = RectangleF.Empty;

                float bx = x;
                if (showOpen) { OpenButton.SetBounds((int)bx, (int)y, OpenButton.PreferredWidth(), bh); bx += OpenButton.Width + Theme.S(26); }
                if (showCopy) CopyButton.SetBounds((int)bx, (int)y, CopyButton.PreferredWidth(), bh);
                if (showOpen || showCopy) y += bh + Theme.S(14);

                filesHeaderRect = RectangleF.Empty;
                bool fit = false;
                if (hasFiles)
                {
                    filesHeaderRect = new RectangleF(x, y, w, Theme.S(26));
                    y += Theme.S(34);
                    float fh = Height - Theme.S(8) - y;
                    if (fh >= Theme.S(48))
                    {
                        fit = true;
                        Files.SetBounds((int)x, (int)y, (int)(Width - x), (int)fh);
                    }
                    else filesHeaderRect = RectangleF.Empty;
                }
                Files.Visible = fit;
            }
        }

        protected override void Draw(Graphics g)
        {
            Theme.Line(g, Theme.Hair, Theme.S(1), 0.5f, 0, 0.5f, Height);
            Theme.Label(g, "fig. 03 / inside", Theme.M(9.5f), Theme.Ink2, Theme.S(22), Theme.S(12));
            Theme.LabelRight(g, Theme.Coord(Left, Top), Theme.M(8), Theme.Ink, Width - Theme.S(10), Theme.S(12));

            Theme.Text(g, Theme.Lc(title), Theme.G(24, 550), Theme.Ink, titleRect);
            Theme.Text(g, Theme.Lc(path), Theme.M(10), Theme.Ink2, pathRect, StringAlignment.Near, StringAlignment.Center,
                current is Culprit && ((Culprit)current).Kind == CulpritKind.Folder, placeholder);

            if (!placeholder)
            {
                float tx = tagRect.X, cy = tagRect.Y + tagRect.Height / 2;
                if (square != Color.Empty)
                {
                    Theme.Toner(g, square, new RectangleF(tx, cy - Theme.S(4.5f), Theme.S(9), Theme.S(9)), 4);
                    tx += Theme.S(16);
                }
                var tf = Theme.M(9.5f);
                for (int i = 0; i < tags.Count && tx < tagRect.Right - Theme.S(20); i++)
                {
                    if (i > 0) tx += Theme.Label(g, " · ", tf, Theme.Grey, tx, cy);
                    tx += Theme.Label(g, tags[i].Key, tf, tags[i].Value, tx, cy, tagRect.Right - tx);
                }
            }

            float rowH = Theme.S(22);
            for (int i = 0; i < specShown && i < spec.Count; i++)
            {
                float cy = specRect.Y + i * rowH + rowH / 2;
                var kv = spec[i];
                if (placeholder)
                {
                    var cat = (Category)i;
                    Theme.Toner(g, Theme.For(cat), new RectangleF(specRect.X, cy - Theme.S(4), Theme.S(8), Theme.S(8)), i + 2);
                    Theme.Label(g, Labeler.CategoryName(cat).ToLowerInvariant(), Theme.G(14, 500), Theme.Ink, specRect.X + Theme.S(18), cy);
                }
                else
                {
                    float kw = Theme.Label(g, kv.Key, Theme.M(10), Theme.Ink, specRect.X, cy);
                    float vx = specRect.X + kw + Theme.S(14);
                    Theme.TextRightFit(g, Theme.Lc(kv.Value), Theme.M(10), Theme.Ink2, new RectangleF(vx, cy - Theme.S(9), specRect.Right - vx, Theme.S(18)));
                }
                Theme.Line(g, Theme.Hair2, Theme.S(1), specRect.X, cy + rowH / 2 - 0.5f, specRect.Right, cy + rowH / 2 - 0.5f);
            }

            if (!infoRect.IsEmpty)
                Theme.Text(g, Theme.Lc(info), Theme.G(13, 500), Theme.Ink2, infoRect, StringAlignment.Near, StringAlignment.Near, false, true);

            if (!filesHeaderRect.IsEmpty)
            {
                float cy = filesHeaderRect.Y + filesHeaderRect.Height / 2;
                float w = Theme.Label(g, "files.", Theme.G(16, 600), Theme.Ink, filesHeaderRect.X, cy);
                Theme.Label(g, Theme.Lc(filesHeader), Theme.M(9.5f), Theme.Grey, filesHeaderRect.X + w + Theme.S(10), cy);
                Theme.Line(g, Theme.Hair, Theme.S(1), filesHeaderRect.X, filesHeaderRect.Bottom + Theme.S(4), filesHeaderRect.Right, filesHeaderRect.Bottom + Theme.S(4));
            }
        }
    }
}
