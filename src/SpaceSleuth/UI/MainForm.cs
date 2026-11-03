using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
        const string StopText = "stop scan";
        const string MainDrive = "C:";

        readonly SideBar side = new SideBar();
        readonly TopBar top = new TopBar();
        readonly DriveTabs tabs = new DriveTabs();
        readonly PrimaryButton findButton = new PrimaryButton(FindText);
        readonly Hero hero = new Hero();
        readonly Evidence evidence = new Evidence();
        readonly SuspectsPanel suspects = new SuspectsPanel();
        readonly CaseFile caseFile = new CaseFile();
        readonly FirstStage firstStage = new FirstStage();
        readonly InvestigationStage investigation = new InvestigationStage();
        readonly FilterBar filters;
        readonly LookBack windowPicker;
        readonly CheckBox thorough = new CheckBox("thorough");
        readonly SearchField filter = new SearchField();
        readonly StatusBar status = new StatusBar();
        readonly ContextMenuStrip itemMenu = Theme.Menu();
        readonly ToolTip tips = new ToolTip();
        readonly System.Windows.Forms.Timer frame = new System.Windows.Forms.Timer { Interval = 33 };
        readonly List<IAnimated> animated = new List<IAnimated>();

        SuspectList results { get { return suspects.List; } }

        enum Screen { First, Investigating, Results }
        Screen screen = Screen.First;

        List<DriveChoice> drives = new List<DriveChoice>();
        DriveChoice chosenDrive; // null = all drives
        ScanResult result;
        string windowPhrase = "in the last 48 hours";
        string periodLabel = "past 48 hours";
        CancellationTokenSource cts;
        DateTime lastStatus = DateTime.MinValue;
        int caseNumber;

        /// <summary>The bottom bar: hairline on top, look back, thorough, and the filter on the right.</summary>
        sealed class FilterBar : Painted
        {
            protected override void Draw(Graphics g)
            {
                Theme.HLine(g, Theme.Line, 0, Width, 0);
            }
        }

        public MainForm()
        {
            using (var g = CreateGraphics()) Theme.Scale = g.DpiX / 96f;
            LoadSettings();

            SuspendLayout();
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Sans(14);
            Text = "Sleuth";
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            StartPosition = FormStartPosition.CenterScreen;
            var work = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(Theme.Si(1600), work.Width - 40), Math.Min(Theme.Si(1000), work.Height - 40));
            MinimumSize = new Size(Theme.Si(1100), Theme.Si(640));

            // If you've scanned before, pick up exactly where you left off.
            DateTime? previous = null;
            try { previous = ScanEngine.LastScanUtc(null); } catch { }
            windowPicker = new LookBack(TimeWindows.Select(w => w.Key).ToArray(), previous.HasValue ? 0 : Default48h);
            filters = new FilterBar();
            filters.Controls.AddRange(new Control[] { windowPicker, thorough, filter });

            tips.SetToolTip(windowPicker, "How far back to look. “since last scan” covers everything since you last ran Sleuth.");
            tips.SetToolTip(thorough, "Also checks the dates on every file, not just Windows' change log.\nSlower, but catches anything the change log has already forgotten.");
            tips.SetToolTip(tabs, "Which drive to investigate");

            Controls.AddRange(new Control[] { side, top, tabs, findButton, hero, evidence, suspects, caseFile, firstStage, investigation, filters, status });

            BuildDrives();
            BuildItemMenu();
            Wire();

            var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            side.Footer = "est. 2026 / v" + (version != null ? version.InformationalVersion : "1.0");
            status.LeftText = Native.IsElevated() ? "ready · " + (previous.HasValue ? "last case " + Theme.Ago(previous.Value) : "no previous scan on this computer")
                                              : "not running as administrator · partial results only";
            status.RightText = previous.HasValue ? "picks up where the last case left off" : "your first scan establishes a baseline";
            ShowScreen(Screen.First);
            UpdateCrumbs();

            animated.AddRange(new IAnimated[] { results, caseFile.Files, investigation });
            frame.Tick += (s, e) => OnFrame();
            Theme.Changed += (s, e) => { BackColor = Theme.Bg; ApplyWindowColors(); Invalidate(true); };

            ResumeLayout(false);

            Shown += (s, e) =>
            {
                frame.Start();
                ready = true;
#if PREVIEW
                if (Environment.GetEnvironmentVariable("PREVIEW_DARK") == "0") Theme.SetDark(false);
                string ps = Environment.GetEnvironmentVariable("PREVIEW_STATE");
                if (ps == "scan")
                {
                    var preview = new List<string> { "C:" };
                    BeginInvestigation(preview, DateTime.UtcNow.AddDays(-3), TimeSpan.FromDays(3), false);
                    OnProgress("C: checking file dates... 184,206 files  (C:\\Users\\Public\\Documents\\Kontakt Libraries)");
                }
                else if (ps == "first") { }
                else
                {
                    result = PreviewData.Build();
                    caseNumber = 28;
                    windowPhrase = "in the last 3 days";
                    periodLabel = "past 3 days";
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

        // ---------------- Settings & window chrome ----------------

        static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceSleuth", "settings.txt"); }
        }

        void LoadSettings()
        {
            Theme.Dark = true;
            try
            {
                if (File.Exists(SettingsPath))
                    foreach (var line in File.ReadAllLines(SettingsPath))
                    {
                        if (line.StartsWith("theme=")) Theme.Dark = line.Substring(6).Trim() != "light";
                        if (line.StartsWith("case=")) int.TryParse(line.Substring(5).Trim(), out caseNumber);
                    }
            }
            catch { }
        }

        void SaveSettings()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                File.WriteAllText(SettingsPath, "theme=" + (Theme.Dark ? "dark" : "light") + Environment.NewLine + "case=" + caseNumber + Environment.NewLine);
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
                int caption = ColorRef(Theme.Bg), text = ColorRef(Theme.Fg), border = ColorRef(Theme.Line);
                DwmSetWindowAttribute(Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(Handle, 36, ref text, 4);
                DwmSetWindowAttribute(Handle, 34, ref border, 4);
                int squareCorners = 1;                                        // DWMWCP_DONOTROUND: square corners
                DwmSetWindowAttribute(Handle, 33, ref squareCorners, 4);
            }
            catch { }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
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
            if (cts != null && scanClock != null)
            {
                string el = Clock(scanClock.Elapsed);
                if (el != investigation.Elapsed)
                {
                    investigation.Elapsed = el;
                    investigation.Invalidate();
                    UpdateScanStatus();
                }
            }
        }

        // ---------------- Layout ----------------

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (filters == null) return;
            int W = ClientSize.Width, H = ClientSize.Height;
            int sideW = Theme.Si(104), topH = Theme.Si(56);
            int left = sideW + Theme.Si(32), right = W - Theme.Si(32);
            int statusH = Theme.Si(32), filterH = Theme.Si(64);

            side.SetBounds(0, 0, sideW, H);
            top.SetBounds(sideW, 0, W - sideW, topH);

            bool shortScreen = H < Theme.Si(800);
            int cmdY = topH + Theme.Si(shortScreen ? 22 : 32), cmdH = Theme.Si(56);
            int bw = Theme.Si(232);
            findButton.SetBounds(right - bw, cmdY, bw, cmdH);
            tabs.SetBounds(left, cmdY - Theme.Si(8), Math.Max(Theme.Si(300), right - bw - Theme.Si(24) - left), cmdH + Theme.Si(8));

            // bottom: filters bar and status
            status.SetBounds(sideW, H - statusH, W - sideW, statusH);
            filters.SetBounds(sideW, H - statusH - filterH, W - sideW, filterH);
            int fx = Theme.Si(32), fpad = Theme.Si(24);
            int lw = windowPicker.PreferredWidth();
            windowPicker.SetBounds(fx, 1, lw, filterH - 1);
            fx += lw + fpad;
            int cw = Math.Max(thorough.PreferredWidth(), Theme.Si(120));
            thorough.SetBounds(fx, 1, cw, filterH - 1);
            fx += cw + fpad;
            int sw = Math.Max(Theme.Si(150), Math.Min(Theme.Si(266), filters.Width - Theme.Si(32) - fx));
            filter.SetBounds(filters.Width - Theme.Si(32) - sw, 1, sw, filterH - 1);

            int contentTop = cmdY + cmdH, contentBottom = filters.Top;
            firstStage.SetBounds(left, contentTop, right - left, contentBottom - contentTop);
            investigation.SetBounds(left, contentTop, right - left, contentBottom - contentTop);

            hero.Tier = H >= Theme.Si(940) ? 0 : !shortScreen ? 1 : 2;
            int hh = hero.Measure(right - left);
            hero.SetBounds(left, contentTop, right - left, hh);

            int colGap = Theme.Si(shortScreen ? 16 : 24);
            int colTop = hero.Bottom + colGap, colBottom = contentBottom - colGap;
            int colH = Math.Max(Theme.Si(200), colBottom - colTop);
            int evW = Theme.Si(280), gap = Theme.Si(24);
            int caseW = Math.Max(Theme.Si(340), Math.Min(Theme.Si(424), (int)((right - left) * 0.296)));
            evidence.SetBounds(left, colTop, evW, colH);
            caseFile.SetBounds(right - caseW, colTop, caseW, colH);
            suspects.SetBounds(left + evW + gap, colTop, right - caseW - gap - (left + evW + gap), colH);
        }

        void ShowScreen(Screen s)
        {
            screen = s;
            bool res = s == Screen.Results;
            hero.Visible = evidence.Visible = suspects.Visible = caseFile.Visible = res;
            firstStage.Visible = s == Screen.First;
            investigation.Visible = s == Screen.Investigating;
            status.Progress = s == Screen.Investigating ? investigation.Progress : (double?)null;
            UpdateCrumbs();
            Invalidate(true);
        }

        void UpdateCrumbs()
        {
            string where = chosenDrive == null ? "all drives" : (string.IsNullOrEmpty(chosenDrive.Label) ? "local disk" : chosenDrive.Label.ToLowerInvariant()) + " (" + chosenDrive.Drive + ")";
            string what = screen == Screen.Investigating ? "investigation in progress" : screen == Screen.Results ? "case " + caseNumber.ToString("000") : "new case";
            top.Crumbs = new[] { "this computer", where, what };
            top.Invalidate();
            tabs.Note = chosenDrive == null
                ? drives.Count + (drives.Count == 1 ? " drive · " : " drives · ") + Format.Bytes(drives.Sum(d => d.TotalBytes))
                : (string.IsNullOrEmpty(chosenDrive.Label) ? "local disk" : chosenDrive.Label) + (string.IsNullOrEmpty(chosenDrive.FileSystem) ? "" : " · " + chosenDrive.FileSystem);
            tabs.Invalidate();
            firstStage.Hint = "↑   start with " + (chosenDrive == null ? "all drives" : "local disk (" + chosenDrive.Drive + ")");
            firstStage.Invalidate();
        }

        // ---------------- Wiring ----------------

        void Wire()
        {
            findButton.Click += (s, e) => StartScan();
            filter.Box.TextChanged += (s, e) => FillResults(false);
            tabs.SelectedIndexChanged += (s, e) =>
            {
                chosenDrive = tabs.SelectedIndex > 0 && tabs.SelectedIndex - 1 < drives.Count ? drives[tabs.SelectedIndex - 1] : null;
                UpdateCrumbs();
                RescanSoon();
            };
            // picking a look-back window (or a drive) looks again straight away
            windowPicker.SelectedIndexChanged += (s, e) => RescanSoon();
            top.ThemePicked += (s, dark) => { Theme.SetDark(dark); SaveSettings(); };

            results.SelectionChanged += (s, e) => ShowDetails();
            results.ItemActivated += (s, e) => OpenTag(results.SelectedTag);
            caseFile.Files.ItemActivated += (s, e) => OpenTag(caseFile.Files.SelectedFile);
            caseFile.Files.SelectionChanged += (s, e) =>
            {
                var f = caseFile.Files.SelectedFile;
                if (f != null) SetStatus(f.Path, true);
            };
            caseFile.OpenButton.Click += (s, e) => OpenTag(caseFile.Current);
            caseFile.CopyButton.Click += (s, e) => CopyTag(caseFile.Current);

            results.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { CopyTag(results.SelectedTag); e.Handled = true; } };
            caseFile.Files.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.C) { CopyTag(caseFile.Files.SelectedFile); e.Handled = true; } };
        }

        void BuildDrives()
        {
            try { drives = ScanEngine.AvailableDrives(); } catch { drives = new List<DriveChoice>(); }
#if PREVIEW
            drives = new List<DriveChoice>
            {
                new DriveChoice { Drive = "C:", Label = "Local Disk", FileSystem = "NTFS", TotalBytes = 953L << 30, FreeBytes = 238L << 30 },
                new DriveChoice { Drive = "D:", Label = "Samples", FileSystem = "NTFS", TotalBytes = 1863L << 30, FreeBytes = 410L << 30 }
            };
#endif
            tabs.Items.Clear();
            tabs.Items.Add("ALL");
            foreach (var d in drives) tabs.Items.Add(d.Drive.ToUpperInvariant());
        }

        bool ready;

        void RescanSoon()
        {
            if (!ready || cts != null) return;
            BeginInvoke((Action)StartScan);
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
            caseFile.Files.ContextMenuStrip = itemMenu;
        }

        object MenuTag()
        {
            if (itemMenu.SourceControl == caseFile.Files) return caseFile.Files.SelectedFile;
            return results.SelectedTag;
        }

        // ---------------- Scanning ----------------

        Stopwatch scanClock;
        double shownProgress;
        int drivesInScan = 1, driveIndex;
        string countText = "", stepWord = "";

        static string Clock(TimeSpan t)
        {
            return ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
        }

        static string PeriodLabel(string key)
        {
            switch (key)
            {
                case "6h": return "past 6 hours";
                case "12h": return "past 12 hours";
                case "24h": return "past 24 hours";
                case "48h": return "past 48 hours";
                case "3d": return "past 3 days";
                case "7d": return "past 7 days";
                case "14d": return "past 14 days";
                case "30d": return "past 30 days";
                default: return key;
            }
        }

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

            var key = TimeWindows[Math.Max(0, windowPicker.SelectedIndex)];
            var window = key.Value;
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
                    periodLabel = "since last scan · " + Theme.Day(last.Value);
                }
                else
                {
                    window = TimeSpan.FromHours(48);
                    windowPhrase = "in the last 48 hours (no earlier scan yet)";
                    periodLabel = "past 48 hours";
                }
            }
            else
            {
                windowPhrase = "in the " + Format.Window(window);
                periodLabel = PeriodLabel(key.Key);
            }
            opt.Window = window;
            if (opt.Drives.Count == 0)
            {
                ShowStage("no drives", "Nothing to investigate.", "Sleuth couldn't find a drive it can read. ", null, "", "Plug one in, or run Sleuth as administrator.");
                return;
            }

            cts = new CancellationTokenSource();
            var token = cts.Token;
            findButton.Text = StopText;
            findButton.StopIcon = true;
            windowPicker.Enabled = tabs.Enabled = thorough.Enabled = false;
            filter.Enabled = false;
            BeginInvestigation(opt.Drives, DateTime.UtcNow - window, window, opt.Thorough);

            var progress = new Progress<string>(OnProgress);
            try
            {
                var res = await Task.Run(() => ScanEngine.Run(opt, progress, token));
                result = res;
                caseNumber++;
                SaveSettings();
                ShowResult();
                status.LeftText = "case complete · " + results_Count() + " · double-click a row to open it";
                status.RightText = Theme.Day(DateTime.UtcNow) + " · " + Clock(res.Elapsed) + " elapsed";
            }
            catch (OperationCanceledException)
            {
                ShowStage("case on hold", "The trail went cold.\nOn purpose, this time.", "You stopped the scan. Press ", FindText, " to pick it back up.", "Nothing was changed on your drives.");
                SetStatus("stopped", true);
                status.RightText = "nothing was changed on your drives";
            }
            catch (Exception ex)
            {
                ShowStage("something got in the way", "The investigation\nhit a wall.", "Try again with ", FindText, ", or tick thorough below.", ex.Message);
                SetStatus("problem: " + ex.Message, true);
            }
            finally
            {
                cts.Dispose();
                cts = null;
                scanClock = null;
                findButton.Text = FindText;
                findButton.StopIcon = false;
                findButton.Enabled = true;
                windowPicker.Enabled = tabs.Enabled = thorough.Enabled = true;
                filter.Enabled = true;
                status.Progress = null;
                status.Invalidate();
                PerformLayout();
            }
        }

        string results_Count()
        {
            int n = result == null ? 0 : result.Culprits.Count(c => c.Kind != CulpritKind.Scattered);
            return n == 0 ? "no suspects" : n == 1 ? "1 suspect" : n + " suspects";
        }

        void ShowStage(string kicker, string title, string body1, string bold, string body2, string body3)
        {
            firstStage.Kicker = kicker;
            firstStage.Title = title;
            firstStage.Body1 = body1;
            firstStage.BodyBold = bold;
            firstStage.Body2 = body2;
            firstStage.Body3 = body3;
            firstStage.Index = "";
            firstStage.Meta = new string[0, 2];
            ShowScreen(Screen.First);
        }

        void BeginInvestigation(IList<string> scanned, DateTime cutoffUtc, TimeSpan window, bool thoroughScan)
        {
            scanClock = Stopwatch.StartNew();
            shownProgress = 0.01;
            drivesInScan = Math.Max(1, scanned.Count);
            driveIndex = 0;
            countText = "";
            stepWord = "checking restore points";

            long was = 0; DateTime when = DateTime.MinValue;
            bool known = false;
            string baselineDrive = scanned.FirstOrDefault(d => string.Equals(d, MainDrive, StringComparison.OrdinalIgnoreCase)) ?? scanned.FirstOrDefault();
            try { known = baselineDrive != null && ScanEngine.TryBaseline(baselineDrive, cutoffUtc, window, out was, out when); }
            catch { was = 0; when = DateTime.MinValue; }

            investigation.CaseLabel = "case " + (caseNumber + 1).ToString("000") + " / in progress";
            investigation.DriveText = string.Join(", ", scanned);
            investigation.Progress = shownProgress;
            investigation.ProgressLeft = "opening the case";
            investigation.Elapsed = "00:00";
            investigation.Checking = "restore points and shadow copies";
            investigation.Facts.Clear();
            investigation.Facts.Add(new KeyValuePair<string, string>("0", "files checked"));
            investigation.Facts.Add(new KeyValuePair<string, string>(periodLabel.StartsWith("since") ? "last scan" : periodLabel.Replace("past ", ""), "comparison window"));
            investigation.Facts.Add(new KeyValuePair<string, string>(scanned.Count == 1 ? scanned[0] : scanned.Count.ToString(), scanned.Count == 1 ? "under investigation" : "drives under investigation"));
            investigation.Steps.Clear();
            investigation.Steps.Add(new InvestigationStage.Step { Title = "baseline located", Detail = known ? baselineDrive + " · " + Theme.Day(when) : "no earlier reading · first look", Done = true });
            investigation.Steps.Add(new InvestigationStage.Step { Title = "checking restore points", Detail = "volume shadow copies", Active = true });
            investigation.Steps.Add(new InvestigationStage.Step { Title = "examining changes", Detail = thoroughScan ? "change journal + file dates" : "NTFS change journal" });
            investigation.Steps.Add(new InvestigationStage.Step { Title = "measuring system space", Detail = "hibernation · page file · recycle bin" });
            investigation.Steps.Add(new InvestigationStage.Step { Title = "ranking the suspects", Detail = "most likely first" });

            results.Clear();
            caseFile.SetItem(null, null, 0);
            ShowScreen(Screen.Investigating);
            UpdateScanStatus();
        }

        static readonly Regex CountRx = new Regex(@"([\d][\d,\.\u00A0\u202F ]*)\s+(records|files)", RegexOptions.Compiled);
        static readonly Regex OfRx = new Regex(@"([\d][\d,\.\u00A0\u202F]*) of ([\d][\d,\.\u00A0\u202F]*)", RegexOptions.Compiled);

        static long ParseNumber(string s)
        {
            long v;
            return long.TryParse(new string(s.Where(char.IsDigit).ToArray()), out v) ? v : 0;
        }

        /// <summary>Turns the engine's progress messages into the procedure steps, a counter and a progress estimate.</summary>
        void OnProgress(string msg)
        {
            if (string.IsNullOrEmpty(msg)) return;
            var steps = investigation.Steps;
            if (steps.Count < 5) return;
            string m = msg.ToLowerInvariant();
            int step;
            double within = 0;
            if (m.StartsWith("checking restore points")) step = 1;
            else if (m.Contains("measuring system areas")) step = 3;
            else if (m.StartsWith("ranking")) step = 4;
            else step = 2;

            if (m.Length > 2 && m[1] == ':')
            {
                string d = msg.Substring(0, 2).ToUpperInvariant();
                int idx = drives.FindIndex(x => string.Equals(x.Drive, d, StringComparison.OrdinalIgnoreCase));
                var scanned = investigation.DriveText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries).ToList();
                int si = scanned.FindIndex(x => string.Equals(x, d, StringComparison.OrdinalIgnoreCase));
                if (si >= 0) driveIndex = si;
                if (idx < 0 && si < 0) driveIndex = 0;
            }

            if (step == 2)
            {
                var of = OfRx.Match(msg);
                var count = CountRx.Match(msg);
                if (m.Contains("measuring changed files") && of.Success)
                {
                    long i = ParseNumber(of.Groups[1].Value), n = Math.Max(1, ParseNumber(of.Groups[2].Value));
                    within = 0.55 + 0.45 * Math.Min(1, (double)i / n);
                    stepWord = "measuring changed files";
                    countText = i.ToString("N0") + " of " + n.ToString("N0") + " changed files measured";
                }
                else if (count.Success)
                {
                    long v = ParseNumber(count.Groups[1].Value);
                    bool records = count.Groups[2].Value == "records";
                    within = records ? 0.5 * (1 - Math.Exp(-v / 500000.0)) : 0.1 + 0.85 * (1 - Math.Exp(-v / 400000.0));
                    stepWord = records ? "reading the change journal" : "checking file dates";
                    countText = v.ToString("N0") + (records ? " journal records read" : " files checked");
                    investigation.Facts[0] = new KeyValuePair<string, string>(v.ToString("N0"), records ? "journal records read" : "files checked");
                    if (!records) steps[2].Detail = "file dates" + (steps[2].Detail.Contains("journal") ? " + change journal" : "");
                }
                else stepWord = m.Contains("journal") ? "reading the change journal" : "examining changes";

                int open = msg.LastIndexOf('('), close = msg.LastIndexOf(')');
                if (open > 0 && close > open && msg.IndexOf(":\\", open, StringComparison.Ordinal) > 0)
                    investigation.Checking = msg.Substring(open + 1, close - open - 1);
                else if (m.Contains("journal")) investigation.Checking = "the change journal on " + msg.Substring(0, 2).ToUpperInvariant();
                else if (m.Contains("measuring changed files")) investigation.Checking = "the size of every changed file on " + msg.Substring(0, 2).ToUpperInvariant();
            }
            else if (step == 1) { stepWord = "checking restore points"; investigation.Checking = "restore points and shadow copies"; }
            else if (step == 3) { stepWord = "measuring system space"; investigation.Checking = "hibernation file, page file, recycle bin and restore points on " + msg.Substring(0, 2).ToUpperInvariant(); }
            else { stepWord = "ranking the suspects"; investigation.Checking = "everything that changed, most likely first"; }

            for (int i = 1; i < steps.Count; i++)
            {
                // with several drives, examining and measuring repeat per drive: keep them active until ranking
                steps[i].Active = i == step;
                steps[i].Done = i < step;
            }

            // progress: restore points 0-5%, then each drive 5-95% (examining 75%, system space 25%), ranking 95%+
            double perDrive = 0.90 / drivesInScan;
            double p = step == 1 ? 0.03
                     : step == 4 ? 0.97
                     : 0.05 + perDrive * driveIndex + perDrive * (step == 2 ? 0.75 * within : 0.75 + 0.2);
            shownProgress = Math.Max(shownProgress, Math.Min(0.99, p));
            investigation.Progress = shownProgress;
            investigation.ProgressLeft = string.IsNullOrEmpty(countText) ? stepWord : countText;
            investigation.Invalidate();
            UpdateScanStatus();
        }

        void UpdateScanStatus()
        {
            if (screen != Screen.Investigating) return;
            status.Progress = shownProgress;
            int stepNo = Math.Max(1, investigation.Steps.FindIndex(s => s.Active) + 1);
            status.LeftText = "investigating · " + (string.IsNullOrEmpty(countText) ? stepWord : countText);
            status.RightText = "step " + stepNo + " of " + investigation.Steps.Count + " · " + investigation.Elapsed + " elapsed";
        }

        void SetStatus(string text, bool force)
        {
            if (!force && (DateTime.UtcNow - lastStatus).TotalMilliseconds < 100) return;
            lastStatus = DateTime.UtcNow;
            status.LeftText = text;
        }

        // ---------------- Showing results ----------------

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

        /// <summary>The evidence: C: was X free at the start of the window, now Y; how it was measured; what was added, by kind.</summary>
        void FillEvidence(ScanResult r)
        {
            var rep = r.Drives.FirstOrDefault(d => string.Equals(d.Drive, MainDrive, StringComparison.OrdinalIgnoreCase));
            evidence.Added.Clear();
            foreach (var c in r.Culprits)
            {
                long b = c.Kind == CulpritKind.Scattered ? c.AddedBytes : BytesOf(c);
                if (b <= 0) continue;
                var cat = Labeler.CategoryOf(c);
                long v;
                evidence.Added.TryGetValue(cat, out v);
                evidence.Added[cat] = v + b;
            }
            evidence.Footnote = null;
            evidence.Traced = evidence.NotTraced = 0;
            evidence.Drive = MainDrive;
            if (rep == null)
            {
                evidence.State = Evidence.Mode.NoDrive;
                evidence.Method = null;
                evidence.Invalidate();
                return;
            }

            evidence.NowFree = rep.FreeBytes;
            evidence.TotalBytes = rep.TotalBytes;
            evidence.WasFree = rep.PrevFreeBytes;
            evidence.WasUtc = rep.PrevScanUtc;
            evidence.Method = rep.Method;
            evidence.EarliestUtc = rep.EarliestRecordUtc;
            evidence.State = rep.PrevFreeBytes.HasValue ? Evidence.Mode.Result
                           : rep.EarliestRecordUtc.HasValue ? Evidence.Mode.NoRecord
                           : Evidence.Mode.FirstLook;
            evidence.Invalidate();
            if (!rep.PrevFreeBytes.HasValue) return;

            long drop = rep.PrevFreeBytes.Value - rep.FreeBytes;
            long found = TracedOnMain(r);
            evidence.Traced = found;
            evidence.NotTraced = Math.Max(0, drop - found);

            if (drop <= 0)
            {
                if (found >= 1024L * 1024 * 1024)
                    evidence.Footnote = "Still, " + Format.Bytes(found) + " of new data showed up on " + MainDrive + " (it's in the list). Something else freed up even more.";
                return;
            }

            // "was" comes from the reading closest to the start of the window; say so when it isn't close
            var slack = TimeSpan.FromTicks(Math.Max(TimeSpan.FromHours(2).Ticks, r.Window.Ticks / 10));
            bool readingLate = rep.PrevScanUtc.HasValue && rep.PrevScanUtc.Value > r.CutoffUtc + slack;
            bool readingEarly = rep.PrevScanUtc.HasValue && rep.PrevScanUtc.Value < r.CutoffUtc - slack;
            if (readingLate)
                evidence.Footnote = "Sleuth's first reading in this window is from " + Format.When(rep.PrevScanUtc.Value) + ", so \u201cbefore\u201d starts there. It can only compare against times it ran.";
            else if (readingEarly)
                evidence.Footnote = "The closest earlier reading is from " + Format.When(rep.PrevScanUtc.Value) + ", a little before this window.";
            else if (found > drop + drop / 10)
                evidence.Footnote = "Found " + Format.Bytes(found) + " of new data, more than the drop: some space was also freed in the same time.";
            else if (evidence.NotTraced > Math.Max(512L * 1024 * 1024, drop / 10))
                evidence.Footnote = "Not traced = space that came and went (temporary files) or restore points Windows already cleaned up.";
        }

        static long TracedOnMain(ScanResult r)
        {
            long onC = r.Culprits.Where(c => c.Kind != CulpritKind.Scattered && OnDrive(c, MainDrive)).Sum(c => Math.Max(0, BytesOf(c)));
            long scattered = r.Culprits.Where(c => c.Kind == CulpritKind.Scattered)
                                .SelectMany(c => c.Files)
                                .Where(f => f.Path.StartsWith(MainDrive + "\\", StringComparison.OrdinalIgnoreCase))
                                .Sum(f => f.AddedBytes);
            return onC + scattered;
        }

        static string Count(int n)
        {
            string[] words = { "No", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine" };
            return n < words.Length ? words[n] : n.ToString();
        }

        void ShowResult()
        {
            var r = result;
            FillEvidence(r);
            var top = r.Culprits.FirstOrDefault(c => c.Kind != CulpritKind.Scattered);
            int leads = r.Culprits.Count(c => c.Kind != CulpritKind.Scattered && c.Likelihood >= 0.15);
            var rep = r.Drives.FirstOrDefault(d => string.Equals(d.Drive, MainDrive, StringComparison.OrdinalIgnoreCase));
            bool firstLook = r.Drives.All(d => !d.PrevScanUtc.HasValue);

            hero.Eyebrow = "the missing space / " + periodLabel;
            hero.Notes.Clear();
            string lead = top == null ? "No leads." : leads <= 1 ? "One clear lead." : Count(leads) + " leads.";
            if (rep != null && rep.PrevFreeBytes.HasValue)
            {
                long drop = rep.PrevFreeBytes.Value - rep.FreeBytes;
                if (Math.Abs(drop) < 1024L * 1024)
                {
                    hero.Number = null;
                    hero.Sentence = MainDrive + " free space held steady. " + (top == null ? "" : lead);
                }
                else
                {
                    hero.Number = Format.Bytes(Math.Abs(drop));
                    hero.Sentence = (drop > 0 ? "less free. " : "more free. ") + lead;
                }
            }
            else if (top != null)
            {
                hero.Number = Format.Bytes(r.TotalAdded > 0 ? r.TotalAdded : BytesOf(top));
                hero.Sentence = "new " + (periodLabel.StartsWith("since") ? "since last scan" : "in the " + periodLabel) + ". " + lead;
            }
            else
            {
                hero.Number = null;
                hero.Sentence = "Nothing big showed up. Calm.";
            }

            if (top != null)
            {
                long b = BytesOf(top);
                hero.Strong = Names.Of(top) + (b > 0 ? (top.AddedBytes > 0 ? " added " : " added about ") + Format.Bytes(b) + "." : " changed.");
                var parts = new List<string>();
                if (rep != null && rep.PrevFreeBytes.HasValue)
                {
                    long drop = rep.PrevFreeBytes.Value - rep.FreeBytes;
                    if (drop > 0) parts.Add(Format.Bytes(Math.Min(drop, evidence.Traced)) + " of " + Format.Bytes(drop) + " traced on " + MainDrive + ".");
                }
                parts.Add(Format.Bytes(r.TotalAdded) + " new " + windowPhrase + ".");
                hero.Summary = string.Join("  ", parts);
            }
            else
            {
                hero.Strong = null;
                hero.Summary = "Try a longer look-back window if you're still missing space.";
            }

            foreach (var d in r.Drives)
            {
                if (string.Equals(d.Drive, MainDrive, StringComparison.OrdinalIgnoreCase)) continue;
                string s = d.Drive + " " + Format.Bytes(d.FreeBytes) + " free";
                if (d.PrevFreeBytes.HasValue && d.PrevScanUtc.HasValue)
                {
                    long change = d.FreeBytes - d.PrevFreeBytes.Value;
                    s += " (" + (change == 0 ? "no change" : Format.SignedBytes(change)) + " since " + Format.When(d.PrevScanUtc.Value) + ")";
                }
                hero.Notes.Add(s);
            }
            foreach (var d in r.Drives) if (!string.IsNullOrEmpty(d.Note)) hero.Notes.Add(d.Note);
            hero.Notes.AddRange(r.Notes.Where(n => !n.StartsWith("Free space on " + MainDrive)));
            if (firstLook)
                hero.Notes.Insert(0, "First look done. Next time you'll also see exactly how much restore points, the page file and big existing files grew.");
            hero.StampText = firstLook ? "baseline filed" : top == null ? "no leads" : "case reviewed";

            results.EmptyTitle = top == null ? "No suspects." : "Nothing matches.";
            FillResults(true);
            ShowScreen(Screen.Results);
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
                if (f.Length > 0)
                {
                    results.EmptyTitle = "Nothing matches.";
                    results.EmptyText = "No suspect or path matches “" + f + "”. Try a shorter word, or clear the filter.";
                }
                else
                {
                    results.EmptyTitle = "No suspects.";
                    results.EmptyText = "Nothing big showed up " + windowPhrase + ". Try a longer look-back window if you're still missing space.";
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
            int no = c != null && c.Kind != CulpritKind.Scattered ? c.Rank : 0;
            caseFile.SetItem(tag, area, no);
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
                SetStatus("path copied · " + p, true);
            }
            catch
            {
            }
        }
    }
}
