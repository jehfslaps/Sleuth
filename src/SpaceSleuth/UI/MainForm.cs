using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpaceSleuth.Core;
using SpaceSleuth.Windows;

namespace SpaceSleuth.UI
{
    internal sealed class BufferedListView : ListView
    {
        public BufferedListView()
        {
            DoubleBuffered = true;
        }
    }

    public sealed class MainForm : Form
    {
        static readonly Color Accent = Color.FromArgb(0, 95, 184);
        static readonly Color Muted = Color.FromArgb(96, 96, 96);
        static readonly Color Warn = Color.FromArgb(157, 93, 0);

        static readonly KeyValuePair<string, TimeSpan>[] TimeWindows =
        {
            new KeyValuePair<string, TimeSpan>("last 6 hours", TimeSpan.FromHours(6)),
            new KeyValuePair<string, TimeSpan>("last 12 hours", TimeSpan.FromHours(12)),
            new KeyValuePair<string, TimeSpan>("last 24 hours", TimeSpan.FromHours(24)),
            new KeyValuePair<string, TimeSpan>("last 48 hours", TimeSpan.FromHours(48)),
            new KeyValuePair<string, TimeSpan>("last 3 days", TimeSpan.FromDays(3)),
            new KeyValuePair<string, TimeSpan>("last 7 days", TimeSpan.FromDays(7)),
            new KeyValuePair<string, TimeSpan>("last 14 days", TimeSpan.FromDays(14)),
            new KeyValuePair<string, TimeSpan>("last 30 days", TimeSpan.FromDays(30)),
        };

        readonly float scale;
        readonly ComboBox windowBox = new ComboBox();
        readonly ComboBox driveBox = new ComboBox();
        readonly CheckBox thoroughBox = new CheckBox();
        readonly Button scanButton = new Button();
        readonly TextBox filterBox = new TextBox();
        readonly Label headline = new Label();
        readonly Label subline = new Label();
        readonly Label notes = new Label();
        readonly BufferedListView culpritList = new BufferedListView();
        readonly BufferedListView fileList = new BufferedListView();
        readonly TextBox detailBox = new TextBox();
        readonly SplitContainer split = new SplitContainer();
        readonly ToolStripStatusLabel statusLabel = new ToolStripStatusLabel();
        readonly ToolStripProgressBar progressBar = new ToolStripProgressBar();
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly ToolTip tips = new ToolTip();
        readonly ListViewGroup rankedGroup = new ListViewGroup("ranked", "Most likely → least likely places your space went");
        readonly ListViewGroup areaGroup = new ListViewGroup("areas", "System space (restore points, hibernation, page file, Recycle Bin) — current sizes");

        ScanResult result;
        CancellationTokenSource cts;
        DateTime lastStatus = DateTime.MinValue;

        public MainForm()
        {
            using (var g = CreateGraphics()) scale = g.DpiX / 96f;

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = SystemFonts.MessageBoxFont;
            Text = "Space Sleuth — where did my disk space go?";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(S(1280), S(820));
            MinimumSize = new Size(S(820), S(520));
            BackColor = SystemColors.Window;

            BuildToolbar();
            BuildHeader();
            BuildLists();
            BuildStatus();

            Controls.Add(split);
            Controls.Add(headerPanel);
            Controls.Add(toolbar);
            Controls.Add(statusStrip);
            ResumeLayout(true);

            Shown += (s, e) =>
            {
                split.SplitterDistance = Math.Max(S(200), (int)(split.Height * 0.58));
#if PREVIEW
                result = PreviewData.Build();
                ShowResult();
#else
                StartScan();
#endif
            };
            FormClosing += (s, e) => { if (cts != null) cts.Cancel(); };
        }

        int S(int v)
        {
            return (int)Math.Round(v * scale);
        }

        // ---------------- Layout ----------------

        FlowLayoutPanel toolbar;
        TableLayoutPanel headerPanel;
        StatusStrip statusStrip;

        void BuildToolbar()
        {
            toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(S(10), S(10), S(10), S(4)),
                WrapContents = true,
                BackColor = SystemColors.Window
            };

            var l1 = MakeLabel("Show space added in the");
            windowBox.DropDownStyle = ComboBoxStyle.DropDownList;
            windowBox.Width = S(130);
            foreach (var w in TimeWindows) windowBox.Items.Add(w.Key);
            windowBox.SelectedIndex = 3; // 48 hours
            windowBox.Margin = new Padding(S(4), S(3), S(4), S(3));

            var l2 = MakeLabel("on");
            driveBox.DropDownStyle = ComboBoxStyle.DropDownList;
            driveBox.Width = S(260);
            driveBox.Margin = new Padding(S(4), S(3), S(10), S(3));
            driveBox.Items.Add("All drives");
            try
            {
                foreach (var d in ScanEngine.AvailableDrives()) driveBox.Items.Add(d);
            }
            catch
            {
            }
            driveBox.SelectedIndex = 0;

            thoroughBox.Text = "Thorough (slower)";
            thoroughBox.AutoSize = true;
            thoroughBox.Margin = new Padding(S(4), S(6), S(10), S(3));
            tips.SetToolTip(thoroughBox,
                "Also checks the dates on every file, not just Windows' change log.\n" +
                "Slower, but catches anything the change log has already forgotten.");

            scanButton.Text = "Find where my space went";
            scanButton.AutoSize = true;
            scanButton.Padding = new Padding(S(8), S(2), S(8), S(2));
            scanButton.Margin = new Padding(S(4), S(1), S(16), S(3));
            scanButton.BackColor = Accent;
            scanButton.ForeColor = Color.White;
            scanButton.FlatStyle = FlatStyle.Flat;
            scanButton.FlatAppearance.BorderSize = 0;
            scanButton.Font = new Font(Font, FontStyle.Bold);
            scanButton.Click += (s, e) => StartScan();

            filterBox.Width = S(220);
            filterBox.Margin = new Padding(S(4), S(3), S(4), S(3));
            filterBox.TextChanged += (s, e) => FillCulprits();
            filterBox.HandleCreated += (s, e) =>
            {
                try { SendMessage(filterBox.Handle, 0x1501 /* EM_SETCUEBANNER */, (IntPtr)1, "Filter results…"); }
                catch { /* placeholder text is cosmetic */ }
            };

            toolbar.Controls.AddRange(new Control[] { l1, windowBox, l2, driveBox, thoroughBox, scanButton, filterBox });
        }

        Label MakeLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Margin = new Padding(S(4), S(7), 0, S(3)) };
        }

        void BuildHeader()
        {
            headerPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 1,
                Padding = new Padding(S(14), S(4), S(14), S(8)),
                BackColor = SystemColors.Window
            };
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            headline.AutoSize = true;
            headline.Font = new Font(Font.FontFamily, Font.SizeInPoints + 5, FontStyle.Bold);
            headline.ForeColor = Accent;
            headline.Text = "Ready";
            headline.Margin = new Padding(0, S(4), 0, S(2));

            subline.AutoSize = true;
            subline.ForeColor = Muted;
            subline.Margin = new Padding(0, 0, 0, S(2));

            notes.AutoSize = true;
            notes.ForeColor = Warn;
            notes.Margin = new Padding(0, S(2), 0, 0);
            notes.Visible = false;

            headerPanel.Controls.Add(headline, 0, 0);
            headerPanel.Controls.Add(subline, 0, 1);
            headerPanel.Controls.Add(notes, 0, 2);
            headerPanel.Resize += (s, e) =>
            {
                int w = Math.Max(S(200), headerPanel.ClientSize.Width - S(30));
                headline.MaximumSize = new Size(w, 0);
                subline.MaximumSize = new Size(w, 0);
                notes.MaximumSize = new Size(w, 0);
            };
        }

        void BuildLists()
        {
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SplitterWidth = S(6);
            split.BackColor = SystemColors.ControlLight;
            split.Panel1.BackColor = SystemColors.Window;
            split.Panel2.BackColor = SystemColors.Window;

            SetupList(culpritList);
            culpritList.ShowGroups = true;
            culpritList.Groups.Add(rankedGroup);
            culpritList.Groups.Add(areaGroup);
            culpritList.Columns.Add("#", S(36));
            culpritList.Columns.Add("Likelihood", S(90), HorizontalAlignment.Right);
            culpritList.Columns.Add("Space added", S(150), HorizontalAlignment.Right);
            culpritList.Columns.Add("Where", S(420));
            culpritList.Columns.Add("What it probably is", S(260));
            culpritList.Columns.Add("What's in it", S(240));
            culpritList.Columns.Add("When", S(170));
            culpritList.Columns.Add("Notes", S(320));
            culpritList.SelectedIndexChanged += (s, e) => ShowDetails();
            culpritList.ItemActivate += (s, e) => OpenSelected(culpritList);

            detailBox.Dock = DockStyle.Top;
            detailBox.Multiline = true;
            detailBox.ReadOnly = true;
            detailBox.BorderStyle = BorderStyle.None;
            detailBox.BackColor = SystemColors.Window;
            detailBox.Height = S(78);
            detailBox.ScrollBars = ScrollBars.Vertical;
            detailBox.Padding = new Padding(S(6));

            var detailHost = new Panel { Dock = DockStyle.Top, Height = S(86), Padding = new Padding(S(12), S(6), S(12), S(2)) };
            detailHost.Controls.Add(detailBox);
            detailBox.Dock = DockStyle.Fill;

            SetupList(fileList);
            fileList.Columns.Add("File", S(320));
            fileList.Columns.Add("Size on disk", S(110), HorizontalAlignment.Right);
            fileList.Columns.Add("Change", S(150));
            fileList.Columns.Add("When", S(170));
            fileList.Columns.Add("Folder", S(520));
            fileList.ItemActivate += (s, e) => OpenSelected(fileList);

            split.Panel1.Controls.Add(culpritList);
            split.Panel2.Controls.Add(fileList);
            split.Panel2.Controls.Add(detailHost);

            var open = new ToolStripMenuItem("Open in File Explorer", null, (s, e) => OpenSelected(menu.SourceControl as ListView));
            open.Font = new Font(open.Font, FontStyle.Bold);
            var copy = new ToolStripMenuItem("Copy path", null, (s, e) => CopySelected(menu.SourceControl as ListView));
            menu.Items.Add(open);
            menu.Items.Add(copy);
            menu.Opening += (s, e) =>
            {
                var lv = menu.SourceControl as ListView;
                if (lv == null || lv.SelectedItems.Count == 0) { e.Cancel = true; return; }
                var tag = lv.SelectedItems[0].Tag;
                string target = OpenTargetFor(tag);
                open.Enabled = target != null;
                open.Text = target != null && !target.Contains("\\") ? "Open settings" : "Open in File Explorer";
                copy.Enabled = PathFor(tag) != null;
            };
            culpritList.ContextMenuStrip = menu;
            fileList.ContextMenuStrip = menu;
        }

        void SetupList(ListView lv)
        {
            lv.Dock = DockStyle.Fill;
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.HideSelection = false;
            lv.MultiSelect = false;
            lv.BorderStyle = BorderStyle.None;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.C) { CopySelected(lv); e.Handled = true; }
            };
        }

        void BuildStatus()
        {
            statusStrip = new StatusStrip { SizingGrip = true };
            statusLabel.Spring = true;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Text = Native.IsElevated() ? "Ready." : "Not running as administrator — results will be limited.";
            progressBar.Style = ProgressBarStyle.Marquee;
            progressBar.MarqueeAnimationSpeed = 30;
            progressBar.Visible = false;
            progressBar.Size = new Size(S(160), S(16));
            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            var verLabel = new ToolStripStatusLabel("v" + (version != null ? version.InformationalVersion : "1.0")) { ForeColor = Muted };
            statusStrip.Items.Add(statusLabel);
            statusStrip.Items.Add(progressBar);
            statusStrip.Items.Add(verLabel);
        }

        // ---------------- Scanning ----------------

        async void StartScan()
        {
            if (cts != null)
            {
                cts.Cancel();
                scanButton.Enabled = false;
                SetStatus("Stopping…", true);
                return;
            }

            var opt = new ScanOptions
            {
                Window = TimeWindows[Math.Max(0, windowBox.SelectedIndex)].Value,
                Thorough = thoroughBox.Checked
            };
            var choice = driveBox.SelectedItem as DriveChoice;
            if (choice != null) opt.Drives.Add(choice.Drive);
            else
            {
                foreach (var item in driveBox.Items)
                {
                    var d = item as DriveChoice;
                    if (d != null) opt.Drives.Add(d.Drive);
                }
            }
            if (opt.Drives.Count == 0)
            {
                headline.Text = "No drives found to scan.";
                return;
            }

            cts = new CancellationTokenSource();
            var token = cts.Token;
            scanButton.Text = "Stop";
            windowBox.Enabled = driveBox.Enabled = thoroughBox.Enabled = false;
            progressBar.Visible = true;
            headline.Text = "Looking for where your space went in the " + Format.Window(opt.Window) + "…";
            subline.Text = "Scanning " + string.Join(", ", opt.Drives) + ". The first look at a drive can take a little longer.";
            notes.Visible = false;
            culpritList.Items.Clear();
            fileList.Items.Clear();
            detailBox.Text = "";

            var progress = new Progress<string>(s => SetStatus(s, false));
            try
            {
                var res = await Task.Run(() => ScanEngine.Run(opt, progress, token));
                result = res;
                ShowResult();
                SetStatus("Done in " + res.Elapsed.TotalSeconds.ToString("0.0") + " s. Double-click a row to open it in File Explorer.", true);
            }
            catch (OperationCanceledException)
            {
                headline.Text = "Scan stopped.";
                subline.Text = "";
                SetStatus("Scan stopped.", true);
            }
            catch (Exception ex)
            {
                headline.Text = "The scan ran into a problem.";
                subline.Text = ex.Message;
                SetStatus("Error: " + ex.Message, true);
            }
            finally
            {
                cts.Dispose();
                cts = null;
                scanButton.Text = "Find where my space went";
                scanButton.Enabled = true;
                windowBox.Enabled = driveBox.Enabled = thoroughBox.Enabled = true;
                progressBar.Visible = false;
            }
        }

        void SetStatus(string text, bool force)
        {
            if (!force && (DateTime.UtcNow - lastStatus).TotalMilliseconds < 100) return;
            lastStatus = DateTime.UtcNow;
            statusLabel.Text = text;
        }

        // ---------------- Showing results ----------------

        void ShowResult()
        {
            var r = result;
            var top = r.Culprits.FirstOrDefault(c => c.Kind != CulpritKind.Scattered);
            string window = Format.Window(r.Window);

            if (top == null)
            {
                headline.Text = r.Culprits.Count == 0
                    ? "No noticeable new data in the " + window + "."
                    : "Only small scattered changes in the " + window + ".";
            }
            else if (top.AddedBytes > 0)
            {
                headline.Text = "Most likely: " + Format.Bytes(top.AddedBytes) + " went to " +
                                (top.Kind == CulpritKind.Folder ? top.Path : top.Label);
            }
            else
            {
                headline.Text = "Most likely: an existing file grew in " + top.Path;
            }

            var parts = new List<string>();
            parts.Add("Found " + Format.Bytes(r.TotalAdded) + " of new data in the " + window + ".");
            foreach (var d in r.Drives)
            {
                string s = d.Drive + " has " + Format.Bytes(d.FreeBytes) + " free";
                if (d.PrevFreeBytes.HasValue && d.PrevScanUtc.HasValue)
                {
                    long change = d.FreeBytes - d.PrevFreeBytes.Value;
                    s += " (" + (change == 0 ? "no change" : Format.SignedBytes(change)) + " since your last scan, " + Format.When(d.PrevScanUtc.Value) + ")";
                }
                parts.Add(s + ".");
            }
            subline.Text = string.Join("  ", parts);

            var noteLines = new List<string>();
            foreach (var d in r.Drives) if (!string.IsNullOrEmpty(d.Note)) noteLines.Add(d.Note);
            noteLines.AddRange(r.Notes);
            if (r.Drives.All(d => !d.PrevScanUtc.HasValue))
                noteLines.Add("First scan: from now on Space Sleuth remembers sizes, so next time it can also show exactly how much restore points, the page file and big existing files grew.");
            notes.Text = string.Join("\n", noteLines.Select(n => "• " + n));
            notes.Visible = noteLines.Count > 0;

            FillCulprits();
            if (culpritList.Items.Count > 0)
            {
                culpritList.Items[0].Selected = true;
                culpritList.Items[0].Focused = true;
            }
        }

        void FillCulprits()
        {
            if (result == null) return;
            string filter = filterBox.Text.Trim();
            Func<string[], bool> matches = fields =>
                filter.Length == 0 || fields.Any(f => f != null && f.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

            culpritList.BeginUpdate();
            culpritList.Items.Clear();
            foreach (var c in result.Culprits)
            {
                if (!matches(new[] { c.Path, c.Label, c.Contents, c.Note, c.HiddenReason })) continue;
                string added = c.AddedBytes > 0 ? Format.Bytes(c.AddedBytes) : "";
                if (c.PossibleBytes > 0)
                    added += (added.Length > 0 ? " + " : "") + "up to " + Format.Bytes(c.PossibleBytes) + "?";
                string when = c.Kind == CulpritKind.SystemArea ? "since last scan" :
                              c.FirstUtc == c.LastUtc || Format.When(c.FirstUtc) == Format.When(c.LastUtc) ? Format.When(c.LastUtc)
                              : Format.When(c.FirstUtc) + " → " + Format.When(c.LastUtc);
                var noteParts = new List<string>();
                if (!string.IsNullOrEmpty(c.Note)) noteParts.Add(c.Note);
                if (!string.IsNullOrEmpty(c.HiddenReason) && c.Kind == CulpritKind.Folder) noteParts.Add(c.HiddenReason);
                if (c.Confidence != "High" && c.Kind != CulpritKind.Scattered) noteParts.Add(c.Confidence + " confidence: includes existing files that were modified");

                var item = new ListViewItem(new[]
                {
                    c.Kind == CulpritKind.Scattered ? "" : c.Rank.ToString(),
                    Format.Percent(c.Likelihood),
                    added,
                    c.Path,
                    c.Label,
                    c.Contents,
                    when,
                    string.Join(" · ", noteParts)
                }, rankedGroup) { Tag = c };
                if (c.Rank == 1)
                {
                    item.Font = new Font(culpritList.Font, FontStyle.Bold);
                    item.ForeColor = Accent;
                }
                if (c.Kind == CulpritKind.Scattered) item.ForeColor = Muted;
                culpritList.Items.Add(item);
            }

            foreach (var a in result.Areas.OrderByDescending(a => a.CurrentBytes))
            {
                if (!matches(new[] { a.Name, a.Explanation, a.Activity, a.Drive })) continue;
                string change = a.Delta.HasValue
                    ? (a.Delta.Value == 0 ? "no change" : Format.SignedBytes(a.Delta.Value))
                    : "—";
                var item = new ListViewItem(new[]
                {
                    "",
                    "",
                    change,
                    a.Drive + " " + a.Name,
                    "Uses " + Format.Bytes(a.CurrentBytes) + " right now",
                    "",
                    a.PreviousUtc.HasValue ? "vs. " + Format.When(a.PreviousUtc.Value) : "first scan",
                    a.Activity ?? ""
                }, areaGroup) { Tag = a };
                if (a.ActiveInWindow || (a.Delta.HasValue && a.Delta.Value > 0)) item.ForeColor = Warn;
                culpritList.Items.Add(item);
            }
            culpritList.EndUpdate();
        }

        void ShowDetails()
        {
            fileList.BeginUpdate();
            fileList.Items.Clear();
            if (culpritList.SelectedItems.Count == 0)
            {
                detailBox.Text = "";
                fileList.EndUpdate();
                return;
            }
            var tag = culpritList.SelectedItems[0].Tag;
            var c = tag as Culprit;
            var a = tag as SystemArea;
            var lines = new List<string>();

            if (c != null)
            {
                lines.Add(c.Kind == CulpritKind.Folder ? c.Path : c.Label);
                if (c.Kind == CulpritKind.Folder)
                    lines.Add(c.Label + (string.IsNullOrEmpty(c.Contents) ? "" : "  ·  " + c.Contents));
                else if (!string.IsNullOrEmpty(c.Contents))
                    lines.Add(c.Contents);

                var stats = new List<string>();
                if (c.AddedBytes > 0) stats.Add(Format.Bytes(c.AddedBytes) + " added");
                if (c.PossibleBytes > 0) stats.Add("plus existing files that grew (now " + Format.Bytes(c.PossibleBytes) + " in total; their earlier size isn't known yet)");
                if (c.NewFiles > 0) stats.Add(c.NewFiles.ToString("N0") + " new file" + (c.NewFiles == 1 ? "" : "s"));
                if (c.GrewFiles > 0) stats.Add(c.GrewFiles.ToString("N0") + " existing file" + (c.GrewFiles == 1 ? "" : "s") + " grew");
                stats.Add("Likelihood " + Format.Percent(c.Likelihood) + " (" + c.Confidence.ToLowerInvariant() + " confidence)");
                lines.Add(string.Join("  ·  ", stats));
                var extra = new[] { c.Note, c.Kind == CulpritKind.Folder ? c.HiddenReason : null }.Where(x => !string.IsNullOrEmpty(x));
                if (extra.Any()) lines.Add(string.Join("  ·  ", extra));

                foreach (var f in c.Files) fileList.Items.Add(FileItem(f));
                if (c.Kind == CulpritKind.SystemArea)
                {
                    var area = result.Areas.FirstOrDefault(x => x.Drive + " " + x.Name == c.Path);
                    if (area != null && !string.IsNullOrEmpty(area.Explanation)) lines.Add(area.Explanation);
                }
            }
            else if (a != null)
            {
                lines.Add(a.Drive + " " + a.Name + " — uses " + Format.Bytes(a.CurrentBytes));
                if (a.Delta.HasValue)
                    lines.Add((a.Delta.Value == 0 ? "No change" : Format.SignedBytes(a.Delta.Value)) + " since your last scan (" + Format.When(a.PreviousUtc.Value) + ")");
                else
                    lines.Add("First time this was measured. Next scan will show how much it changed.");
                if (!string.IsNullOrEmpty(a.Activity)) lines.Add(a.Activity);
                if (!string.IsNullOrEmpty(a.Explanation)) lines.Add(a.Explanation);
            }
            detailBox.Text = string.Join(Environment.NewLine, lines);
            fileList.EndUpdate();
        }

        static ListViewItem FileItem(ChangedFile f)
        {
            string change;
            switch (f.Kind)
            {
                case ChangeKind.New: change = "New file"; break;
                case ChangeKind.GrewKnown: change = "Grew " + Format.SignedBytes(f.AddedBytes); break;
                default: change = "Modified (grew)"; break;
            }
            return new ListViewItem(new[]
            {
                f.Name,
                Format.Bytes(f.SizeOnDisk),
                change,
                Format.When(f.TimeUtc),
                f.Folder
            }) { Tag = f };
        }

        // ---------------- Actions ----------------

        static string PathFor(object tag)
        {
            var c = tag as Culprit;
            if (c != null) return c.Kind == CulpritKind.Folder ? c.Path : null;
            var f = tag as ChangedFile;
            if (f != null) return f.Path;
            return null;
        }

        static string OpenTargetFor(object tag)
        {
            var c = tag as Culprit;
            if (c != null) return c.Kind == CulpritKind.Folder ? c.Path : c.OpenTarget;
            var a = tag as SystemArea;
            if (a != null) return a.OpenTarget;
            var f = tag as ChangedFile;
            if (f != null) return f.Path;
            return null;
        }

        void OpenSelected(ListView lv)
        {
            if (lv == null || lv.SelectedItems.Count == 0) return;
            var tag = lv.SelectedItems[0].Tag;
            string target = OpenTargetFor(tag);
            if (target == null) return;
            try
            {
                if (tag is ChangedFile)
                    Process.Start("explorer.exe", "/select,\"" + target + "\"");
                else if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || target.Contains("\\"))
                    Process.Start("explorer.exe", "\"" + target + "\"");
                else
                    Process.Start(target);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't open it: " + ex.Message, "Space Sleuth", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        void CopySelected(ListView lv)
        {
            if (lv == null || lv.SelectedItems.Count == 0) return;
            string p = PathFor(lv.SelectedItems[0].Tag);
            if (p == null) return;
            try
            {
                Clipboard.SetText(p);
                SetStatus("Copied: " + p, true);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
    }
}
