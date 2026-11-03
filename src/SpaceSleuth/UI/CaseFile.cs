using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SpaceSleuth.Core;

namespace SpaceSleuth.UI
{
    /// <summary>"The case file" for the selected suspect: what it is, how much it added, when, why, and its biggest files.</summary>
    internal sealed class CaseFile : Painted
    {
        public readonly FileList Files = new FileList();
        public readonly SecondaryButton OpenButton = new SecondaryButton("open folder ↗");
        public readonly SecondaryButton CopyButton = new SecondaryButton("copy path");

        object current;
        int number;
        string title, path, addedCaption, added, whenCaption, when1, when2, counts, note, filesLeft, filesRight;
        Category? category;
        bool hasFiles, placeholder;

        RectangleF pathRect, statsRect, countsRect, noteRect, filesHeadRect;

        public CaseFile()
        {
            Controls.Add(Files);
            Controls.Add(OpenButton);
            Controls.Add(CopyButton);
            SetItem(null, null, 0);
        }

        protected override Color Ground { get { return Theme.Surface; } }

        public object Current { get { return current; } }

        public void SetItem(object tag, SystemArea areaOfCulprit, int no)
        {
            current = tag;
            number = no;
            title = path = addedCaption = added = whenCaption = when1 = when2 = counts = note = filesLeft = filesRight = null;
            category = null;
            placeholder = false;
            List<ChangedFile> fileList = null;
            string openText = null;
            bool canCopy = false;

            var c = tag as Culprit;
            var a = tag as SystemArea;
            if (c != null)
            {
                var lines = new List<string>();
                title = Names.Of(c);
                category = Labeler.CategoryOf(c);
                if (c.AddedBytes > 0) { addedCaption = "space added"; added = "+" + Format.Bytes(c.AddedBytes); }
                else if (c.EstimateBytes > 0) { addedCaption = "estimate"; added = "~" + Format.Bytes(c.EstimateBytes); }
                else if (c.PossibleBytes > 0) { addedCaption = "up to"; added = Format.Bytes(c.PossibleBytes); }
                else { addedCaption = "space added"; added = "±0"; }

                var parts = new List<string>();
                if (c.Kind == CulpritKind.Folder)
                {
                    path = c.Path;
                    openText = "open folder ↗";
                    canCopy = true;
                    if (c.Label != "Folder") lines.Add(c.Label + ".");
                    if (c.HiddenReason != null) lines.Add(c.HiddenReason + " — easy to miss.");
                    if (!string.IsNullOrEmpty(c.Contents)) lines.Add(c.Contents + ".");
                    if (!string.IsNullOrEmpty(c.Note) && !c.IsNewFolder) lines.Add(c.Note + ".");
                    if (c.GrewFiles > 0 && c.PossibleBytes > 0)
                        lines.Add("Existing files also grew (now " + Format.Bytes(c.PossibleBytes) + " in total). Their earlier size isn't known yet, so that part is a guess; your next scan measures it exactly.");
                    parts.Add(c.NewFiles.ToString("N0") + " new");
                    if (c.GrewFiles > 0) parts.Add(c.GrewFiles.ToString("N0") + " grown");
                }
                else if (c.Kind == CulpritKind.SystemArea)
                {
                    path = c.Note;
                    if (areaOfCulprit != null)
                    {
                        parts.Add("uses " + Format.Bytes(areaOfCulprit.CurrentBytes) + " now");
                        if (!string.IsNullOrEmpty(areaOfCulprit.Explanation)) lines.Add(areaOfCulprit.Explanation);
                    }
                    else if (!string.IsNullOrEmpty(c.Contents)) lines.Add(c.Contents);
                    if (c.NewFiles > 0) parts.Add(c.NewFiles == 1 ? "1 new restore point" : c.NewFiles + " new restore points");
                    if (c.EstimateBytes > 0 && c.AddedBytes == 0)
                        lines.Add("Windows doesn't report the size of a single restore point, so this is an estimate from the space restore points use in total.");
                    openText = OpenWords(c.OpenTarget);
                }
                else
                {
                    path = c.Label;
                    lines.Add("App updates, caches and logs each added a small amount. None of them is a big suspect on its own.");
                    parts.Add((c.NewFiles + c.GrewFiles).ToString("N0") + " files");
                }
                parts.Add(Format.Percent(c.Likelihood) + " likely");
                if (c.Confidence != "High") parts.Add("partly a guess");
                counts = string.Join(" · ", parts);
                note = string.Join(" ", lines);

                if (c.Kind == CulpritKind.SystemArea)
                {
                    whenCaption = "compared with";
                    when1 = areaOfCulprit != null && areaOfCulprit.PreviousUtc.HasValue ? Theme.Day(areaOfCulprit.PreviousUtc.Value) : "last scan";
                    when2 = c.NewFiles > 0 && c.LastUtc != default(DateTime) ? "latest point " + Theme.Ago(c.LastUtc) : "local time";
                }
                else
                {
                    whenCaption = "last changed";
                    when1 = Theme.Day(c.LastUtc);
                    string first = Theme.Day(c.FirstUtc);
                    when2 = first != when1 ? "first " + first : c.LastUtc.ToLocalTime().Year + " · local time";
                }

                if (c.Files.Count > 0)
                {
                    fileList = c.Files;
                    int total = c.NewFiles + c.GrewFiles;
                    filesLeft = "biggest additions";
                    filesRight = (total > c.Files.Count ? c.Files.Count.ToString("N0") + " of " + total.ToString("N0") + " · " : "") + "size ↓";
                }
            }
            else if (a != null)
            {
                title = a.Drive + " " + Names.Area(a.Name);
                path = "uses " + Format.Bytes(a.CurrentBytes) + " right now";
                category = Category.System;
                addedCaption = "change";
                added = a.Delta.HasValue ? (a.Delta.Value == 0 ? "±0" : (a.Delta.Value > 0 ? "+" : "−") + Format.Bytes(Math.Abs(a.Delta.Value))) : "first look";
                whenCaption = "compared with";
                when1 = a.PreviousUtc.HasValue ? Theme.Day(a.PreviousUtc.Value) : "next scan";
                when2 = a.PreviousUtc.HasValue ? Theme.Ago(a.PreviousUtc.Value) : "nothing to compare yet";
                var parts = new List<string> { "system space" };
                if (a.ActiveInWindow) parts.Add("busy in this window");
                counts = string.Join(" · ", parts);
                var lines = new List<string>();
                if (!string.IsNullOrEmpty(a.Activity)) lines.Add(a.Activity + ".");
                if (!string.IsNullOrEmpty(a.Explanation)) lines.Add(a.Explanation);
                if (!a.Delta.HasValue) lines.Add("First time this was measured. The next scan shows how much it changed.");
                note = string.Join(" ", lines);
                openText = OpenWords(a.OpenTarget);
            }
            else
            {
                placeholder = true;
                title = "No suspect selected.";
                note = "Pick a row in the suspects list to open its file: how much it added, when, and the biggest files inside.";
            }

            Files.SetFiles(fileList);
            hasFiles = fileList != null;
            OpenButton.Text = openText ?? "open folder ↗";
            OpenButton.Enabled = openText != null;
            CopyButton.Enabled = canCopy;
            PerformLayout();
            Invalidate();
        }

        static string OpenWords(string target)
        {
            if (string.IsNullOrEmpty(target)) return null;
            return target.IndexOf("RecycleBin", StringComparison.OrdinalIgnoreCase) >= 0 ? "open recycle bin ↗"
                 : target.StartsWith("cleanmgr", StringComparison.OrdinalIgnoreCase) ? "open disk cleanup ↗"
                 : "open settings ↗";
        }

        float Pad { get { return Theme.S(20); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width <= 0 || Height <= 0) return;
            using (var g = CreateGraphics())
            {
                float x = Pad, w = Width - Pad * 2;
                float bh = Theme.S(40);
                float bottom = Height - Pad - bh;
                float bw = (w - Theme.S(8)) / 2;
                OpenButton.SetBounds((int)x, (int)bottom, (int)bw, (int)bh);
                CopyButton.SetBounds((int)(x + bw + Theme.S(8)), (int)bottom, (int)(w - bw - Theme.S(8)), (int)bh);
                bottom -= Theme.S(16);

                float y = Pad + Theme.S(20) + Theme.S(18);   // head + gap
                y += Theme.S(34) + Theme.S(8);               // title
                bool tight = Height < Theme.S(420);
                float ph = placeholder || string.IsNullOrEmpty(path) ? 0 : Math.Min(Theme.S(tight ? 18 : 36), Theme.WrappedHeight(g, path, Theme.Mono(12), w));
                pathRect = new RectangleF(x, y, w, ph);
                y += ph;
                statsRect = countsRect = RectangleF.Empty;
                float filesNeed = hasFiles ? Theme.S(24 + 31) : 0;
                // the numbers come first; on a short window the counts line and then the stats give way
                if (!placeholder && y + Theme.S(16 + 76 + 12) + filesNeed <= bottom)
                {
                    y += Theme.S(16);
                    statsRect = new RectangleF(x, y, w, Theme.S(12 + 16 + 36 + 12));
                    y = statsRect.Bottom + Theme.S(12);
                    if (y + Theme.S(20) + filesNeed <= bottom)
                    {
                        countsRect = new RectangleF(x, y, w, Theme.S(18));
                        y += Theme.S(20);
                    }
                }

                var nf = Theme.Sans(14);
                float lineH = Theme.S(21);
                float full = string.IsNullOrEmpty(note) ? 0 : Theme.WrappedHeight(g, note, nf, w);
                float filesMin = hasFiles ? Theme.S(26 + 6 + 31 * 3) : 0;
                float noteRoom = bottom - y - filesMin - Theme.S(8);
                float nh = Math.Min(full, Math.Max(0, noteRoom));
                nh = (float)Math.Floor(nh / lineH + 0.05f) * lineH;
                if (full > 0 && nh < lineH && noteRoom >= lineH) nh = lineH;
                noteRect = nh > 0 ? new RectangleF(x, y, w, Math.Min(nh, full + Theme.S(2))) : RectangleF.Empty;
                if (nh > 0) y += noteRect.Height + Theme.S(12);

                filesHeadRect = RectangleF.Empty;
                bool fit = false;
                if (hasFiles)
                {
                    filesHeadRect = new RectangleF(x, y, w, Theme.S(18));
                    y += Theme.S(24);
                    float fh = bottom - y;
                    if (fh >= Theme.S(31))
                    {
                        fit = true;
                        Files.SetBounds((int)x, (int)y, (int)w, (int)fh);
                    }
                    else filesHeadRect = RectangleF.Empty;
                }
                Files.Visible = fit;
            }
        }

        protected override void Draw(Graphics g)
        {
            Theme.Box(g, Theme.Line, new RectangleF(0, 0, Width, Height));
            float x = Pad, w = Width - Pad * 2;
            float cy = Pad + Theme.S(10);
            Theme.Caps(g, "the case file", Theme.Fg, x, cy, 14, 1.2f);
            if (number > 0) Theme.LabelRight(g, "NO. " + number.ToString("00"), Theme.Mono(12), Theme.Muted, x + w, cy);

            float ty = Pad + Theme.S(38);
            Theme.Text(g, title, Theme.Display(28, 500), placeholder ? Theme.Muted : Theme.Fg, new RectangleF(x, ty, w, Theme.S(34)), StringAlignment.Near, StringAlignment.Center);

            if (!pathRect.IsEmpty)
                Theme.Text(g, path, Theme.Mono(12), Theme.Muted, pathRect, StringAlignment.Near, StringAlignment.Near, false, true);

            if (!statsRect.IsEmpty)
            {
                var r = statsRect;
                Theme.HLine(g, Theme.Line, r.X, r.Right, r.Y);
                Theme.HLine(g, Theme.Line, r.X, r.Right, r.Bottom - Theme.S(1));
                float col2 = r.X + (r.Width + Theme.S(16)) / 2;
                float capY = r.Y + Theme.S(12 + 6);
                Theme.Caps(g, addedCaption, Theme.Muted, r.X, capY, 10, 1);
                Theme.Caps(g, whenCaption, Theme.Muted, col2, capY, 10, 1);
                var big = Theme.Display(32, 500);
                Theme.OnBaseline(g, added, added != null && added.Length > 9 ? Theme.Display(24, 500) : big, Theme.Fg, r.X, r.Y + Theme.S(12 + 16 + 35));
                var mono = Theme.Mono(13);
                Theme.Text(g, when1, mono, Theme.Fg, new RectangleF(col2, r.Y + Theme.S(12 + 16 + 4), r.Right - col2, Theme.S(20)));
                Theme.Text(g, when2, mono, Theme.Fg, new RectangleF(col2, r.Y + Theme.S(12 + 16 + 24), r.Right - col2, Theme.S(20)));
            }

            if (!countsRect.IsEmpty)
            {
                float ccy = countsRect.Y + countsRect.Height / 2;
                float right = countsRect.Right;
                if (category.HasValue)
                {
                    float cw = Theme.LabelRight(g, Theme.CategoryWord(category.Value), Theme.Mono(12), Theme.Fg, right, ccy);
                    Theme.Pattern(g, category.Value, new RectangleF(right - cw - Theme.S(20), ccy - Theme.S(6), Theme.S(12), Theme.S(12)));
                    right -= cw + Theme.S(32);
                }
                Theme.Text(g, counts, Theme.Mono(12), Theme.Fg, new RectangleF(countsRect.X, countsRect.Y, Math.Max(0, right - countsRect.X), countsRect.Height));
            }

            if (!noteRect.IsEmpty)
                Theme.Text(g, note, Theme.Sans(14), placeholder ? Theme.Muted : Theme.Fg, noteRect, StringAlignment.Near, StringAlignment.Near, false, true);

            if (!filesHeadRect.IsEmpty)
            {
                float fcy = filesHeadRect.Y + filesHeadRect.Height / 2;
                Theme.Label(g, filesLeft, Theme.Mono(11), Theme.Muted, filesHeadRect.X, fcy);
                Theme.LabelRight(g, filesRight, Theme.Mono(11), Theme.Muted, filesHeadRect.Right, fcy);
                Theme.HLine(g, Theme.Line, filesHeadRect.X, filesHeadRect.Right, filesHeadRect.Bottom + Theme.S(5));
            }
        }
    }
}
