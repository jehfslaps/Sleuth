using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
    public sealed class MainForm : Form
    {
        static readonly KeyValuePair<string, TimeSpan>[] TimeWindows =
        {
            // Zero = "since your last scan", worked out from the saved history when you press the button.
            new KeyValuePair<string, TimeSpan>("since last scan", TimeSpan.Zero),
            new KeyValuePair<string, TimeSpan>("6h", TimeSpan.FromHours(6)),
            new KeyValuePair<string, TimeSpan>("12h", TimeSpan.FromHours(12)),
            new KeyValuePair<string, TimeSpan>("24h", TimeSpan.FromHours(24)),
            new KeyValuePair<string, TimeSpan>("48h", TimeSpan.FromHours(48)),
            new KeyValuePair<string, TimeSpan>("3d", TimeSpan.FromDays(3)),
            new KeyValuePair<string, TimeSpan>("7d", TimeSpan.FromDays(7)),
            new KeyValuePair<string, TimeSpan>("14d", TimeSpan.FromDays(14)),
            new KeyValuePair<string, TimeSpan>("30d", TimeSpan.FromDays(30)),
        };

        const int Default48h = 4;
        const string FindText = "find my space";
        const string StopText = "stop looking";
        const string MainDrive = "C:";

        readonly SideBar side = new SideBar();
        readonly HeaderBar header = new HeaderBar();
        readonly DriveTabs tabs = new DriveTabs();
        readonly ActionBlock findButton = new ActionBlock(FindText);
        readonly Headline headline = new Headline();
        readonly ResultList results = new ResultList();
        readonly DetailPanel detail = new DetailPanel();
        readonly DataColumn data = new DataColumn();
        readonly StatusLine status = new StatusLine();
        readonly BottomBar bottom;
        readonly OptionWords windowPicker;
        readonly ToggleBox thorough = new ToggleBox("thorough");
        readonly FilterField filter = new FilterField();
        readonly ContextMenuStrip itemMenu = Theme.Menu();
        readonly ToolTip tips = new ToolTip();
        readonly System.Windows.Forms.Timer frame = new System.Windows.Forms.Timer { Interval = 33 };
        readonly List<IAnimated> animated = new List<IAnimated>();

        List<DriveChoice> drives = new List<DriveChoice>();
        DriveChoice chosenDrive; // null = all drives
        ScanResult result;
        string windowPhrase = "in the last 48 hours";
        CancellationTokenSource cts;
        DateTime lastStatus = DateTime.MinValue;
        Point lastMouse;

        /// <summary>The bottom bar: hairline on top, the time window words, the thorough toggle and the filter.</summary>
        sealed class BottomBar : Painted
        {
            public readonly List<int> Separators = new List<int>();

            protected override void Draw(Graphics g)
            {
                Theme.Line(g, Theme.Hair, Theme.S(1), 0, 0.5f, Width, 0.5f);
                foreach (var x in Separators)
                    Theme.Line(g, Theme.Hair, Theme.S(1), x, Theme.S(18), x, Theme.S(38));
            }
        }

        public MainForm()
        {
            using (var g = CreateGraphics()) Theme.Scale = g.DpiX / 96f;
            Theme.Dark = LoadThemeSetting();

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.G(14, 500);
            Text = "Sleuth";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Theme.Paper;
            ForeColor = Theme.Ink;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            StartPosition = FormStartPosition.CenterScreen;
            var work = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Theme.Si(1380), work.Width - 40), Math.Min(Theme.Si(900), work.Height - 40));
            MinimumSize = new Size(Theme.Si(1060), Theme.Si(660));

            // If you've scanned before, pick up exactly where you left off.
            DateTime? previous = null;
            try { previous = ScanEngine.LastScanUtc(null); } catch { }
            windowPicker = new OptionWords("look back", TimeWindows.Select(w => w.Key).ToArray(), previous.HasValue ? 0 : Default48h);
            bottom = new BottomBar();
            bottom.Controls.AddRange(new Control[] { windowPicker, thorough, filter });

            tips.SetToolTip(windowPicker, "How far back to look. “since last scan” covers everything since you last ran Sleuth.");
            tips.SetToolTip(thorough, "Also checks the dates on every file, not just Windows' change log.\nSlower, but catches anything the change log has already forgotten.");
            tips.SetToolTip(tabs, "Which drive to look at");

            Controls.AddRange(new Control[] { side, header, tabs, findButton, headline, results, detail, data, status, bottom });

            BuildDrives();
            BuildItemMenu();
            Wire();

            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            header.Breadcrumb = "sleuth / disk space finder / v" + (version != null ? version.InformationalVersion : "1.0");
            status.Status = Native.IsElevated() ? "ready." : "not running as administrator — partial results only.";
            headline.Plain = "where did your space go?";

            animated.AddRange(new IAnimated[] { status, results, detail.Files, data });
            frame.Tick += (s, e) => OnFrame();
            Theme.Changed += (s, e) => { BackColor = Theme.Paper; ApplyWindowColors(); Invalidate(true); };

            ResumeLayout(false);

            Shown += (s, e) =>
            {
                frame.Start();
                ready = true;
#if PREVIEW
                if (Environment.GetEnvironmentVariable("PREVIEW_DARK") == "1") Theme.SetDark(true);
                if (Environment.GetEnvironmentVariable("PREVIEW_STATE") == "scan")
                {
                    status.Busy = true;
                    SetHeadline("looking around your drives…", null, null);
                    headline.Caption = "fig. 02 / scanning — since your last scan (mon 10:15 am)";
                    data.WasFree = 245L << 30; data.WasUtc = DateTime.UtcNow.AddDays(-3); data.NowFree = 237L << 30; data.TotalBytes = 953L << 30;
                    data.State = DataColumn.Mode.Scanning;
                    results.State = ListMood.Scanning;
                    results.MoodTitle = "looking.";
                    results.MoodText = "c: reading change journal... 412,300 records";
                    detail.PlaceholderTitle = "hang on.";
                    detail.SetItem(null, null);
                    status.Status = "c: reading change journal... 412,300 records";
                    findButton.Text = StopText;
                    PerformLayout();
                }
                else
                {
                    result = PreviewData.Build();
                    windowPhrase = "since your last scan (mon 10:15 am)";
                    ShowResult();
                }
#else
                StartScan();
#endif
            };
            FormClosing += (s, e) =>
            {
                frame.Stop();
                if (cts != null) cts.Cancel();
            };
        }

        // ---------------- Theme & window chrome ----------------

        static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSleuth", "settings.txt"); }
        }

        static bool LoadThemeSetting()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    foreach (var line in File.ReadAllLines(SettingsPath))
                        if (line.StartsWith("theme=")) return line.Substring(6).Trim() == "dark";
            }
            catch { }
            return Theme.WindowsPrefersDark();
        }

        static void SaveThemeSetting()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, "theme=" + (Theme.Dark ? "dark" : "light") + Environment.NewLine);
            }
            catch { }
        }

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        void ApplyWindowColors()
        {
            if (!IsHandleCreated) return;
            try
            {
                int dark = Theme.Dark ? 1 : 0;
                DwmSetWindowAttribute(Handle, 20, ref dark, 4);               // dark title bar buttons
                int caption = ColorRef(Theme.Side), text = ColorRef(Theme.Ink), border = ColorRef(Theme.Dark ? Theme.Hex("#2C2830") : Theme.Hex("#CFCBD0"));
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(Handle, 36, ref text, 4);
                DwmSetWindowAttribute(Handle, 34, ref border, 4);
                int squareCorners = 1;                                        // DWMWCP_DONOTROUND: no rounded corners
                DwmSetWindowAttribute(Handle, 33, ref squareCorners, 4);
            }
            catch { }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // the gaps between panels get the same paper and grain, so nothing looks like a box
            e.Graphics.Clear(Theme.Paper);
            Theme.Grain(e.Graphics, this, ClientRectangle);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyWindowColors();
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void OnFrame()
        {
            if (WindowState == FormWindowState.Minimized) return;
            if (Form.ActiveForm != this && cts == null) return;
            float now = Theme.Now;
            foreach (var a in animated) a.Tick(now);

            // live coordinates, like a lab sheet
            var p = PointToClient(Cursor.Position);
            if (p != lastMouse && ClientRectangle.Contains(p))
            {
                lastMouse = p;
                header.Coords = Theme.Coord(p.X, p.Y);
                header.Invalidate();
            }
        }

        // ---------------- Layout ----------------

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (bottom == null) return;
            int W = ClientSize.Width, H = ClientSize.Height;
            int sideW = Theme.Si(92), headerH = Theme.Si(36);
            int dataW = Math.Max(Theme.Si(330), Math.Min(Theme.Si(400), (int)(W * 0.27)));
            int x0 = sideW + Theme.Si(22), xr = W - dataW - Theme.Si(22);

            side.SetBounds(0, 0, sideW, H);
            header.SetBounds(sideW, 0, W - sideW, headerH);
            data.SetBounds(W - dataW, headerH, dataW, H - headerH);

            int aw = findButton.PreferredWidth();
            findButton.SetBounds(xr - aw, headerH + Theme.Si(20), aw, Theme.Si(52));
            tabs.SetBounds(x0, headerH + Theme.Si(6), Math.Max(Theme.Si(100), xr - aw - Theme.Si(20) - x0), Theme.Si(76));

            // bottom bar (wraps to a second row when narrow)
            int bx = Theme.Si(20), row = 0, rowH = Theme.Si(52);
            int barW = xr - sideW + Theme.Si(22) - Theme.Si(1);
            bottom.Separators.Clear();
            Action<Control, int> place = (c, w) =>
            {
                if (bx + w > barW - Theme.Si(10) && bx > Theme.Si(30)) { bx = Theme.Si(20); row++; }
                c.SetBounds(bx, Theme.Si(4) + row * rowH, w, rowH);
                bx += w + Theme.Si(18);
                bottom.Separators.Add(bx - Theme.Si(9));
            };
            place(windowPicker, windowPicker.PreferredWidth());
            place(thorough, thorough.PreferredWidth());
            int fw = Math.Min(Theme.Si(260), Math.Max(Theme.Si(180), barW - bx - Theme.Si(10)));
            place(filter, fw);
            bottom.Separators.RemoveAt(bottom.Separators.Count - 1);
            int barH = (row + 1) * rowH + Theme.Si(10);
            bottom.SetBounds(sideW + 1, H - barH, barW, barH);

            int statusH = Theme.Si(24);
            status.SetBounds(x0, bottom.Top - statusH - Theme.Si(2), xr - x0, statusH);
            status.RightText = Theme.Coord(xr, bottom.Top);

            int hy = tabs.Bottom + Theme.Si(10);
            int hh = headline.Measure(xr - x0);
            headline.SetBounds(x0, hy, xr - x0, hh);

            int ly = headline.Bottom + Theme.Si(8);
            int lh = Math.Max(Theme.Si(160), status.Top - Theme.Si(10) - ly);
            int listW = (int)((xr - x0) * 0.56);
            results.SetBounds(x0, ly, listW, lh);
            detail.SetBounds(x0 + listW + Theme.Si(16), ly, xr - (x0 + listW + Theme.Si(16)) + Theme.Si(10), lh);
        }

        // ---------------- Wiring ----------------

        void Wire()
        {
            findButton.Click += (s, e) => StartScan();
            filter.Box.TextChanged += (s, e) => FillResults(false);
            tabs.SelectedIndexChanged += (s, e) =>
            {
                ChooseDrive(tabs.SelectedIndex > 0 && tabs.SelectedIndex - 1 < drives.Count ? drives[tabs.SelectedIndex - 1] : null);
                RescanSoon();
            };
            // picking a look-back window (or a drive) looks again straight away
            windowPicker.SelectedIndexChanged += (s, e) => RescanSoon();
            header.ThemePicked += (s, dark) => { Theme.SetDark(dark); SaveThemeSetting(); };

            results.SelectionChanged += (s, e) => ShowDetails();
            results.ItemActivated += (s, e) => OpenTag(results.SelectedTag);
            detail.Files.ItemActivated += (s, e) => OpenTag(detail.Files.SelectedFile);
            detail.OpenButton.Click += (s, e) => OpenTag(detail.Current);
            detail.CopyButton.Click += (s, e) => CopyTag(detail.Current);

            results.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { CopyTag(results.SelectedTag); e.Handled = true; } };
            detail.Files.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { CopyTag(detail.Files.SelectedFile); e.Handled = true; } };
        }

        void BuildDrives()
        {
            try { drives = ScanEngine.AvailableDrives(); } catch { drives = new List<DriveChoice>(); }
            tabs.Items.Clear();
            tabs.Items.Add("all drives");
            foreach (var d in drives) tabs.Items.Add(d.Drive.ToLowerInvariant());
#if PREVIEW
            drives.Clear(); tabs.Items.Clear(); tabs.Items.Add("all drives"); tabs.Items.Add("c:"); tabs.Items.Add("d:");
#endif
        }

        bool ready;

        void RescanSoon()
        {
            if (!ready || cts != null) return;
            BeginInvoke((Action)StartScan);
        }

        void ChooseDrive(DriveChoice d)
        {
            chosenDrive = d;
        }

        void BuildItemMenu()
        {
            var open = Theme.MenuItem("open in file explorer", (s, e) => OpenTag(MenuTag()));
            var copy = Theme.MenuItem("copy path", (s, e) => CopyTag(MenuTag()));
            itemMenu.Items.Add(open);
            itemMenu.Items.Add(copy);
            itemMenu.Opening += (s, e) =>
            {
                object tag = MenuTag();
                string target = OpenTargetFor(tag);
                if (tag == null || (target == null && PathFor(tag) == null)) { e.Cancel = true; return; }
                open.Enabled = target != null;
                open.Text = target != null && !target.Contains("\\") ? "open settings" : "open in file explorer";
                copy.Enabled = PathFor(tag) != null;
            };
            results.ContextMenuStrip = itemMenu;
            detail.Files.ContextMenuStrip = itemMenu;
        }

        object MenuTag()
        {
            if (itemMenu.SourceControl == detail.Files) return detail.Files.SelectedFile;
            return results.SelectedTag;
        }

        // ---------------- Scanning ----------------

        async void StartScan()
        {
            if (cts != null)
            {
                cts.Cancel();
                findButton.Enabled = false;
                findButton.Text = "stopping…";
                SetStatus("wrapping up…", true);
                return;
            }

            var opt = new ScanOptions { Thorough = thorough.Checked };
            if (chosenDrive != null) opt.Drives.Add(chosenDrive.Drive);
            else opt.Drives.AddRange(drives.Select(d => d.Drive));

            var window = TimeWindows[Math.Max(0, windowPicker.SelectedIndex)].Value;
            if (window == TimeSpan.Zero)
            {
                DateTime? last = null;
                try { last = ScanEngine.LastScanUtc(opt.Drives); } catch { }
                if (last.HasValue)
                {
                    // A few minutes of overlap so nothing slips between scans; always look back at least an hour.
                    window = DateTime.UtcNow - last.Value + TimeSpan.FromMinutes(5);
                    if (window < TimeSpan.FromHours(1)) window = TimeSpan.FromHours(1);
                    windowPhrase = "since your last scan (" + Format.When(last.Value) + ")";
                }
                else
                {
                    window = TimeSpan.FromHours(48);
                    windowPhrase = "in the last 48 hours (no earlier scan yet)";
                }
            }
            else windowPhrase = "in the " + Format.Window(window);
            opt.Window = window;
            if (opt.Drives.Count == 0)
            {
                SetHeadline("no drives to look at.", null, null);
                return;
            }

            cts = new CancellationTokenSource();
            var token = cts.Token;
            findButton.Text = StopText;
            windowPicker.Enabled = tabs.Enabled = thorough.Enabled = false;
            status.Busy = true;

            SetHeadline("looking around your drives…", null, null);
            headline.Caption = "fig. 02 / scanning — " + windowPhrase;
            ShowScanningData(opt.Drives, DateTime.UtcNow - window, window);
            results.Clear();
            results.State = ListMood.Scanning;
            results.MoodTitle = "looking.";
            results.MoodText = "checking " + string.Join(", ", opt.Drives) + " for what changed " + windowPhrase + ". usually a few seconds.";
            detail.PlaceholderTitle = "hang on.";
            detail.SetItem(null, null);
            PerformLayout();

            var progress = new Progress<string>(s => SetStatus(s, false));
            try
            {
                var res = await Task.Run(() => ScanEngine.Run(opt, progress, token));
                result = res;
                ShowResult();
                SetStatus("done in " + res.Elapsed.TotalSeconds.ToString("0.0") + " s. double-click a row to open it.", true);
            }
            catch (OperationCanceledException)
            {
                SetHeadline("stopped.", null, null);
                data.State = DataColumn.Mode.NoDrive;
                results.State = ListMood.Stopped;
                results.MoodTitle = "stopped.";
                results.MoodText = "run it again whenever you like.";
                SetStatus("stopped.", true);
            }
            catch (Exception ex)
            {
                SetHeadline("something got in the way.", null, null);
                data.State = DataColumn.Mode.NoDrive;
                results.State = ListMood.Error;
                results.MoodTitle = "hm.";
                results.MoodText = ex.Message;
                SetStatus("problem: " + ex.Message, true);
            }
            finally
            {
                detail.PlaceholderTitle = "pick a row.";
                if (results.SelectedTag == null) detail.SetItem(null, null);
                cts.Dispose();
                cts = null;
                findButton.Text = FindText;
                findButton.Enabled = true;
                windowPicker.Enabled = tabs.Enabled = thorough.Enabled = true;
                status.Busy = false;
                results.Invalidate();
                data.Invalidate();
                PerformLayout();
            }
        }

        void SetStatus(string text, bool force)
        {
            if (!force && (DateTime.UtcNow - lastStatus).TotalMilliseconds < 100) return;
            lastStatus = DateTime.UtcNow;
            status.Status = text;
            if (cts != null && results.State == ListMood.Scanning && !force)
            {
                results.MoodText = text;
                results.Invalidate();
            }
        }

        void SetHeadline(string plain, string amount, string target)
        {
            headline.Plain = plain;
            headline.Amount = amount;
            headline.Target = target;
            headline.Summary = null;
            headline.Caption = "fig. 02 / most likely — " + windowPhrase;
            headline.Notes.Clear();
            headline.Invalidate();
            PerformLayout();
        }

        // ---------------- Showing results ----------------

        static string LastSegment(string path)
        {
            string p = path.TrimEnd('\\');
            int cut = p.LastIndexOf('\\');
            return cut >= 0 && cut < p.Length - 1 ? p.Substring(cut + 1) : path;
        }

        void ShowScanningData(IList<string> scanned, DateTime cutoffUtc, TimeSpan window)
        {
            data.Slices.Clear();
            data.NotFound = 0;
            data.Footnote = null;
            data.Method = null;
            data.WindowText = WindowLabel();
            data.EarliestUtc = null;
            if (!scanned.Any(d => string.Equals(d, MainDrive, StringComparison.OrdinalIgnoreCase)))
            {
                data.State = DataColumn.Mode.NoDrive;
                data.Invalidate();
                return;
            }
            long was; DateTime when;
            bool known = false;
            try { known = ScanEngine.TryBaseline(MainDrive, cutoffUtc, window, out was, out when); }
            catch { was = 0; when = DateTime.MinValue; }
            data.Drive = MainDrive;
            data.WasFree = known ? was : (long?)null;
            data.WasUtc = known ? when : (DateTime?)null;
            try { data.NowFree = ScanEngine.FreeNow(MainDrive); } catch { }
            var choice = drives.FirstOrDefault(d => string.Equals(d.Drive, MainDrive, StringComparison.OrdinalIgnoreCase));
            if (choice != null) data.TotalBytes = choice.TotalBytes;
            data.State = DataColumn.Mode.Scanning;
            data.Invalidate();
        }

        string WindowLabel()
        {
            return windowPhrase.StartsWith("since") ? "since last scan" : windowPhrase.Replace("in the ", "").Replace(" (no earlier scan yet)", "");
        }

        static bool OnDrive(Culprit c, string drive)
        {
            if (c.Kind == CulpritKind.SystemArea) return c.Path.StartsWith(drive + " ", StringComparison.OrdinalIgnoreCase);
            if (c.Kind == CulpritKind.Folder) return c.Path.StartsWith(drive + "\\", StringComparison.OrdinalIgnoreCase);
            return false;
        }

        static long BytesOf(Culprit c)
        {
            return c.AddedBytes > 0 ? c.AddedBytes : c.EstimateBytes;
        }

        static string ShortName(Culprit c)
        {
            if (c.Kind == CulpritKind.Folder) return LastSegment(c.Path);
            if (c.Kind == CulpritKind.SystemArea && c.Label.StartsWith("System Restore")) return "restore points";
            return c.Label;
        }

        /// <summary>fig. 01: C: was X free at your last scan, now Y, and where the drop most likely went.</summary>
        void FillData(ScanResult r)
        {
            var rep = r.Drives.FirstOrDefault(d => string.Equals(d.Drive, MainDrive, StringComparison.OrdinalIgnoreCase));
            data.Slices.Clear();
            data.NotFound = 0;
            data.Footnote = null;
            data.WindowText = WindowLabel();
            data.EarliestUtc = null;
            if (rep == null)
            {
                data.State = DataColumn.Mode.NoDrive;
                data.Method = null;
                data.Invalidate();
                return;
            }

            data.Drive = MainDrive;
            data.NowFree = rep.FreeBytes;
            data.TotalBytes = rep.TotalBytes;
            data.WasFree = rep.PrevFreeBytes;
            data.WasUtc = rep.PrevScanUtc;
            data.Method = rep.Method;
            data.EarliestUtc = rep.EarliestRecordUtc;
            data.State = rep.PrevFreeBytes.HasValue ? DataColumn.Mode.Result
                       : rep.EarliestRecordUtc.HasValue ? DataColumn.Mode.NoRecord
                       : DataColumn.Mode.FirstLook;
            data.Invalidate();
            if (!rep.PrevFreeBytes.HasValue) return;

            long drop = rep.PrevFreeBytes.Value - rep.FreeBytes;
            var onC = r.Culprits.Where(c => c.Kind != CulpritKind.Scattered && OnDrive(c, MainDrive) && BytesOf(c) > 0)
                                .OrderByDescending(BytesOf).ToList();
            long scatteredOnC = r.Culprits.Where(c => c.Kind == CulpritKind.Scattered)
                                .SelectMany(c => c.Files)
                                .Where(f => f.Path.StartsWith(MainDrive + "\\", StringComparison.OrdinalIgnoreCase))
                                .Sum(f => f.AddedBytes);
            long found = onC.Sum(BytesOf) + scatteredOnC;

            if (drop <= 0)
            {
                if (found >= 1024L * 1024 * 1024)
                    data.Footnote = "still, " + Format.Bytes(found) + " of new data showed up on " + MainDrive + " — it's in the list. something else freed up even more.";
                return;
            }

            foreach (var c in onC.Take(3))
                data.Slices.Add(new DataColumn.Slice { Name = ShortName(c), Bytes = BytesOf(c), Color = Theme.For(c), Estimate = c.AddedBytes == 0 });
            long rest = found - data.Slices.Sum(x => x.Bytes);
            if (rest > 0) data.Slices.Add(new DataColumn.Slice { Name = "everything else", Bytes = rest, Color = Theme.Grey });
            data.NotFound = Math.Max(0, drop - found);

            // "was" comes from the reading closest to the start of the window; say so when it isn't close
            var slack = TimeSpan.FromTicks(Math.Max(TimeSpan.FromHours(2).Ticks, r.Window.Ticks / 10));
            bool readingLate = rep.PrevScanUtc.HasValue && rep.PrevScanUtc.Value > r.CutoffUtc + slack;
            bool readingEarly = rep.PrevScanUtc.HasValue && rep.PrevScanUtc.Value < r.CutoffUtc - slack;
            if (readingLate)
                data.Footnote = "sleuth's first free-space reading in this window is from " + Format.When(rep.PrevScanUtc.Value) +
                                ", so \u201cwas\u201d starts there. it can only compare against times it ran.";
            else if (readingEarly)
                data.Footnote = "the closest earlier reading is from " + Format.When(rep.PrevScanUtc.Value) + ", a bit before this window.";
            else if (found > drop + drop / 10)
                data.Footnote = "found " + Format.Bytes(found) + " of new data — more than the drop, because some space was also freed in the same time (deleted files, cleanup).";
            else if (data.NotFound > Math.Max(512L * 1024 * 1024, drop / 10))
                data.Footnote = "“not found yet” is space that couldn't be traced — usually temporary files that came and went, or restore points windows already cleaned up.";
        }

        void ShowResult()
        {
            var r = result;
            var top = r.Culprits.FirstOrDefault(c => c.Kind != CulpritKind.Scattered);

            if (top == null)
                SetHeadline(r.Culprits.Count == 0 ? "nothing big showed up. calm." : "just little bits here and there. nothing big.", null, null);
            else if (top.AddedBytes > 0)
                SetHeadline(null, Format.Bytes(top.AddedBytes), top.Kind == CulpritKind.Folder ? LastSegment(top.Path) : top.Label);
            else if (top.EstimateBytes > 0)
                SetHeadline(null, "~" + Format.Bytes(top.EstimateBytes), "a new restore point");
            else if (top.Kind == CulpritKind.SystemArea)
                SetHeadline("a new restore point was made, but free space barely changed.", null, null);
            else
                SetHeadline("an existing file grew in " + LastSegment(top.Path) + ".", null, null);

            FillData(r);
            bool cShown = data.State == DataColumn.Mode.Result || data.State == DataColumn.Mode.FirstLook;
            var summary = new List<string> { Format.Bytes(r.TotalAdded) + " new " + windowPhrase };
            foreach (var d in r.Drives)
            {
                if (cShown && string.Equals(d.Drive, MainDrive, StringComparison.OrdinalIgnoreCase)) continue;
                string s = d.Drive + " " + Format.Bytes(d.FreeBytes) + " free";
                if (d.PrevFreeBytes.HasValue && d.PrevScanUtc.HasValue)
                {
                    long change = d.FreeBytes - d.PrevFreeBytes.Value;
                    s += " (" + (change == 0 ? "no change" : Format.SignedBytes(change)) + " since " + Format.When(d.PrevScanUtc.Value) + ")";
                }
                summary.Add(s);
            }
            headline.Summary = string.Join("  ·  ", summary);
            foreach (var d in r.Drives) if (!string.IsNullOrEmpty(d.Note)) headline.Notes.Add(d.Note);
            headline.Notes.AddRange(r.Notes.Where(n => !(cShown && n.StartsWith("Free space on " + MainDrive))));
            if (r.Drives.All(d => !d.PrevScanUtc.HasValue))
                headline.Notes.Add("first look done. next time you'll also see exactly how much restore points, the page file and big existing files grew.");

            FillResults(true);
            PerformLayout();
        }

        void FillResults(bool fromScan)
        {
            if (result == null) return;
            string f = filter.Box.Text.Trim();
            Func<string[], bool> matches = fields =>
                f.Length == 0 || fields.Any(x => x != null && x.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);

            var cs = result.Culprits.Where(c => matches(new[] { c.Path, c.Label, c.Contents, c.Note, c.HiddenReason })).ToList();
            var areas = result.Areas.OrderByDescending(a => a.CurrentBytes).Where(a => matches(new[] { a.Name, a.Explanation, a.Activity, a.Drive })).ToList();
            results.SetResults(cs, areas);
            if (cs.Count == 0 && areas.Count == 0)
            {
                results.State = ListMood.Empty;
                if (f.Length > 0)
                {
                    results.MoodTitle = "nothing.";
                    results.MoodText = "no place matches “" + f + "”. try a shorter word, or clear the filter.";
                }
                else
                {
                    results.MoodTitle = "calm.";
                    results.MoodText = "nothing big showed up " + windowPhrase + ". try a longer window if you're still missing space.";
                }
                results.Invalidate();
            }
            ShowDetails();
        }

        void ShowDetails()
        {
            object tag = results.SelectedTag;
            SystemArea area = null;
            var c = tag as Culprit;
            if (c != null && c.Kind == CulpritKind.SystemArea && result != null)
                area = result.Areas.FirstOrDefault(x => x.Drive + " " + x.Name == c.Path);
            detail.SetItem(tag, area);
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

        void OpenTag(object tag)
        {
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
                SetStatus("couldn't open that one: " + ex.Message, true);
            }
        }

        void CopyTag(object tag)
        {
            string p = PathFor(tag);
            if (p == null) return;
            try
            {
                Clipboard.SetText(p);
                SetStatus("copied: " + p, true);
            }
            catch
            {
            }
        }
    }
}
