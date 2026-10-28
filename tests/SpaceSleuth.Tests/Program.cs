using System;
using System.Collections.Generic;
using System.Linq;
using SpaceSleuth.Core;

// Small self-contained test runner for the ranking logic (no test framework needed).
// Exit code 0 = all passed.
static class Tests
{
    static int failures;
    static readonly DateTime Now = DateTime.UtcNow;
    const long MB = 1024L * 1024, GB = 1024L * MB;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  PASS  " : "  FAIL  ") + what);
        if (!ok) failures++;
    }

    static ChangedFile New(string path, long bytes, double hoursAgo = 2)
    {
        return new ChangedFile { Path = path, SizeOnDisk = bytes, AddedBytes = bytes, Kind = ChangeKind.New, TimeUtc = Now.AddHours(-hoursAgo) };
    }

    static ChangedFile Grew(string path, long size, double hoursAgo = 1)
    {
        return new ChangedFile { Path = path, SizeOnDisk = size, PossibleBytes = size, Kind = ChangeKind.GrewUnknown, TimeUtc = Now.AddHours(-hoursAgo) };
    }

    static void Print(List<Culprit> list)
    {
        foreach (var c in list)
            Console.WriteLine("        #{0} {1,5} {2,10}  {3}  [{4}] {5}", c.Rank, Format.Percent(c.Likelihood), Format.Bytes(c.AddedBytes), c.Path, c.Label, c.Contents);
    }

    static int Main()
    {
        SampleLibraryScenario();
        DownloadsScenario();
        AppDataScenario();
        UnknownGrowthScenario();
        SystemAreaScenario();
        NoiseScenario();
        BaselineChecks();
        LabelerChecks();
        FormatChecks();
        Console.WriteLine(failures == 0 ? "All tests passed." : failures + " test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    static void SampleLibraryScenario()
    {
        Console.WriteLine("Scenario: 30 GB sample library dumped into a folder you didn't know about");
        var files = new List<ChangedFile>();
        // 30 GB library spread over 100 subfolders, 12 files each
        for (int d = 0; d < 100; d++)
            for (int i = 0; i < 12; i++)
                files.Add(New(@"C:\Users\Public\Documents\Producer Libs\Cinematic Strings\Samples\Mic" + d + @"\s" + i + ".ncw", 30 * GB / 1200));
        files.Add(New(@"C:\Users\Public\Documents\Producer Libs\Cinematic Strings\Cinematic Strings.nki", 2 * MB));
        // A 100 MB program install
        for (int i = 0; i < 10; i++) files.Add(New(@"C:\Program Files\CoolSynth\lib" + i + ".dll", 10 * MB, 20));
        // Scattered noise
        for (int i = 0; i < 50; i++) files.Add(New(@"C:\Users\me\AppData\Local\App" + i + @"\cache\f.bin", 1 * MB, 5));

        var newDirs = new[] { @"C:\Users\Public\Documents\Producer Libs\Cinematic Strings", @"C:\Program Files\CoolSynth" };
        var r = Analyzer.Analyze(files, newDirs, null, new AnalyzerOptions());
        Print(r);
        Check(r.Count >= 2, "found at least two places");
        Check(r[0].Path == @"C:\Users\Public\Documents\Producer Libs\Cinematic Strings", "#1 is the library's top folder");
        Check(r[0].IsNewFolder, "#1 is flagged as a new folder");
        Check(r[0].Likelihood > 0.95, "#1 explains >95% of the growth");
        Check(r[0].Contents.StartsWith("Mostly audio"), "#1 is recognised as audio samples");
        Check(r[0].HiddenReason != null, "#1 is flagged as an out-of-the-way location");
        Check(r[1].Path == @"C:\Program Files\CoolSynth", "#2 is the 100 MB program install");
        Check(r[1].Label == "Installed program: CoolSynth", "#2 is labelled as an installed program");
        Check(r.Last().Kind == CulpritKind.Scattered, "small scattered changes are grouped at the bottom");
        Check(Math.Abs(r.Sum(c => c.Likelihood) - 1.0) < 0.001, "likelihoods add up to 100%");
    }

    static void DownloadsScenario()
    {
        Console.WriteLine("Scenario: installers in Downloads plus an unzipped folder");
        var files = new List<ChangedFile>
        {
            New(@"C:\Users\me\Downloads\BigInstaller.exe", 4 * GB),
            New(@"C:\Users\me\Downloads\Other.zip", 1 * GB),
        };
        for (int i = 0; i < 30; i++) files.Add(New(@"C:\Users\me\Downloads\Unzipped Pack\Kit\f" + i + ".wav", 100 * MB));
        var r = Analyzer.Analyze(files, new[] { @"C:\Users\me\Downloads\Unzipped Pack" }, null, new AnalyzerOptions());
        Print(r);
        Check(r.Count == 2, "two entries");
        Check(r.Any(c => c.Path == @"C:\Users\me\Downloads" && c.IsRemainder && c.AddedBytes == 5 * GB), "Downloads itself (5 GB of direct files)");
        Check(r.Any(c => c.Path == @"C:\Users\me\Downloads\Unzipped Pack" && c.IsNewFolder), "the unzipped folder separately");
        Check(r[0].Label == "Downloads folder", "Downloads ranks first and is labelled");
    }

    static void AppDataScenario()
    {
        Console.WriteLine("Scenario: several apps growing inside AppData\\Local");
        var files = new List<ChangedFile>();
        for (int i = 0; i < 30; i++) files.Add(New(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache\c" + i, 100 * MB));
        for (int i = 0; i < 20; i++) files.Add(New(@"C:\Users\me\AppData\Local\Spotify\Storage\s" + i, 100 * MB));
        var r = Analyzer.Analyze(files, null, null, new AnalyzerOptions());
        Print(r);
        Check(r.Count == 2, "Chrome and Spotify listed separately, not lumped into AppData");
        Check(r[0].Label.StartsWith("Chrome"), "Chrome first");
        Check(r[1].Label.StartsWith("Spotify"), "Spotify second");
    }

    static void UnknownGrowthScenario()
    {
        Console.WriteLine("Scenario: a huge existing virtual disk was modified vs a definite 5 GB new folder");
        var files = new List<ChangedFile> { Grew(@"C:\Users\me\AppData\Local\Docker\wsl\disk\docker_data.vhdx", 60 * GB) };
        for (int i = 0; i < 5; i++) files.Add(New(@"D:\Games\NewGame\data" + i + ".pak", 1 * GB));
        var r = Analyzer.Analyze(files, new[] { @"D:\Games\NewGame" }, null, new AnalyzerOptions());
        Print(r);
        Check(r[0].Path == @"D:\Games\NewGame", "definite new data ranks above an uncertain modified file");
        var docker = r.FirstOrDefault(c => c.PossibleBytes > 0);
        Check(docker != null && docker.Confidence == "Low", "modified file still listed, with low confidence");
        Check(docker != null && docker.Label.StartsWith("Docker"), "virtual disk recognised as Docker");
    }

    static void SystemAreaScenario()
    {
        Console.WriteLine("Scenario: restore points grew 8 GB since last scan, plus 3 GB of downloads");
        var files = new List<ChangedFile> { New(@"C:\Users\me\Downloads\x.iso", 3 * GB) };
        var areas = new[]
        {
            new SystemArea { Drive = "C:", Key = "shadow", Name = "System Restore points / shadow copies", CurrentBytes = 18 * GB, PreviousBytes = 10 * GB, PreviousUtc = Now.AddHours(-30) },
            new SystemArea { Drive = "C:", Key = "hiberfil", Name = "Hibernation file", CurrentBytes = 12 * GB, PreviousBytes = 12 * GB, PreviousUtc = Now.AddHours(-30) },
        };
        var r = Analyzer.Analyze(files, null, areas, new AnalyzerOptions());
        Print(r);
        Check(r[0].Kind == CulpritKind.SystemArea && r[0].AddedBytes == 8 * GB, "restore points rank #1");
        Check(r.Count == 2, "unchanged hibernation file is not listed as a culprit");

        Console.WriteLine("Scenario: first scan ever, Windows made a restore point overnight (no earlier size to compare)");
        var firstScan = new[]
        {
            new SystemArea { Drive = "C:", Key = "shadow", Name = "System Restore points / shadow copies", CurrentBytes = 20 * GB,
                             NewInWindow = 1, TotalCount = 4, ActiveInWindow = true, LatestNewUtc = Now.AddHours(-9) },
        };
        var r2 = Analyzer.Analyze(new List<ChangedFile> { New(@"C:\Users\me\Downloads\song.wav", 1 * GB) }, null, firstScan, new AnalyzerOptions());
        Print(r2);
        var rp = r2.FirstOrDefault(c => c.Kind == CulpritKind.SystemArea);
        Check(rp != null, "a brand-new restore point is in the ranked list even on a first scan");
        Check(rp != null && rp.EstimateBytes == 5 * GB, "its size is estimated as the average restore point (20 GB / 4)");
        Check(rp != null && rp.Rank == 1, "and it ranks above the 1 GB download");

        Console.WriteLine("Scenario: a restore point was made but Windows deleted an older one, so the total didn't change");
        var replaced = new[]
        {
            new SystemArea { Drive = "C:", Key = "shadow", Name = "System Restore points / shadow copies", CurrentBytes = 20 * GB, PreviousBytes = 20 * GB,
                             PreviousUtc = Now.AddHours(-30), NewInWindow = 1, TotalCount = 4, ActiveInWindow = true },
        };
        var r3 = Analyzer.Analyze(new List<ChangedFile> { New(@"C:\Users\me\Downloads\song.wav", 1 * GB) }, null, replaced, new AnalyzerOptions());
        Print(r3);
        Check(r3.Any(c => c.Kind == CulpritKind.SystemArea), "it is still listed so you know it happened");
        Check(r3[0].Kind == CulpritKind.Folder, "but it doesn't outrank space that was really added");
    }

    static void NoiseScenario()
    {
        Console.WriteLine("Scenario: 50 GB total, a 100 MB install, and 80 apps each logging 3 MB in AppData\\Local");
        var files = new List<ChangedFile>();
        for (int i = 0; i < 50; i++) files.Add(New(@"E:\Libraries\Big Brass\Samples\b" + i + ".wav", 1 * GB));
        for (int i = 0; i < 10; i++) files.Add(New(@"C:\Program Files\CoolSynth\lib" + i + ".dll", 10 * MB, 20));
        for (int i = 0; i < 80; i++) files.Add(New(@"C:\Users\me\AppData\Local\App" + i + @"\log.txt", 3 * MB, 9));
        files.Add(New(@"C:\Users\me\Downloads\song.mp3", 30 * MB));
        var r = Analyzer.Analyze(files, new[] { @"E:\Libraries\Big Brass", @"C:\Program Files\CoolSynth" }, null, new AnalyzerOptions());
        Print(r);
        Check(r[0].Path == @"E:\Libraries\Big Brass", "the 50 GB library is #1");
        Check(r.Any(c => c.Path == @"C:\Program Files\CoolSynth"), "the 100 MB install is still listed further down");
        Check(!r.Any(c => c.Path == @"C:\Users\me\AppData\Local"), "AppData\\Local is not reported as one big culprit");
        Check(r.Last().Kind == CulpritKind.Scattered && r.Last().AddedBytes >= 240 * MB, "the 80 small app logs end up in 'small changes'");
    }

    static void BaselineChecks()
    {
        Console.WriteLine("Look-back windows: which free-space reading counts as \"was\"");
        // readings Sleuth saved when it ran: 6, 5, 3 and 1 day(s) ago, and 1 minute ago (today's open)
        var readings = new[] { -6.0, -5.0, -3.0, -1.0, -0.0007 }.Select(d => Now.AddDays(d)).ToList();
        Func<TimeSpan, DateTime> pick = w =>
        {
            object found = SpaceSleuth.Windows.History.PickBaseline(readings.Select(x => (object)x), r => (DateTime)r, Now - w, w, Now);
            return found == null ? DateTime.MinValue : (DateTime)found;
        };
        Func<DateTime, double> daysAgo = t => Math.Round((Now - t).TotalDays, 2);
        Check(daysAgo(pick(TimeSpan.FromDays(3))) == 3, "3d window starts at the reading from 3 days ago");
        Check(daysAgo(pick(TimeSpan.FromDays(1))) == 1, "24h window starts at yesterday's reading");
        Check(daysAgo(pick(TimeSpan.FromDays(7))) == 6, "7d window starts at the oldest reading (6 days ago) when nothing is older");
        var last = readings[readings.Count - 1];
        var sinceLast = Now - last + TimeSpan.FromMinutes(5);
        Check(pick(sinceLast) == last, "\"since last scan\" starts at the last scan");
        var sparse = new List<DateTime> { Now.AddDays(-1), Now.AddMinutes(-1) };
        var six = TimeSpan.FromHours(6);
        var p6 = SpaceSleuth.Windows.History.PickBaseline(sparse.Select(x => (object)x), o => (DateTime)o, Now - six, six, Now);
        Check(p6 != null && daysAgo((DateTime)p6) == 1, "6h window with no reading near its start falls back to the closest earlier one (not the one from a minute ago)");
    }

    static void LabelerChecks()
    {
        Console.WriteLine("Labels");
        Check(Labeler.Describe(@"D:\SteamLibrary\steamapps\common\Elden Ring\Game") == "Steam game: Elden Ring", "Steam game name");
        Check(Labeler.Describe(@"C:\Users\me\AppData\Local\Temp\{ABC}") == "Temp files (often leftovers from installers)", "installer temp");
        Check(Labeler.Describe(@"C:\Windows\SoftwareDistribution\Download") == "Windows Update downloads", "Windows Update");
        Check(Labeler.Describe(@"E:\Kontakt Libraries\Session Horns") == "Kontakt library / content", "Kontakt");
        Check(Labeler.Describe(@"C:\$Recycle.Bin\S-1-5-21") .StartsWith("Recycle Bin"), "Recycle Bin");
        Check(Labeler.Describe(@"C:\Users\me\AppData\Local\Microsoft\Windows\INetCache\IE") == "Internet cache", "AppData\\...\\Windows isn't mistaken for C:\\Windows");
        Check(Labeler.IsGenericContainer(@"C:\Users\me\AppData\Local"), "AppData\\Local is a container");
        Check(Labeler.IsGenericContainer(@"C:\"), "drive root is a container");
        Check(!Labeler.IsGenericContainer(@"C:\Users\me\AppData\Local\Google"), "an app folder is not a container");
        Check(Labeler.HiddenReasonFromPath(@"C:\ProgramData\Native Instruments") != null, "ProgramData flagged as hidden");
        Check(Labeler.HiddenReasonFromPath(@"D:\Music\Samples") == null, "normal folder not flagged");

        Console.WriteLine("Categories");
        Func<string, string, Category> cat = (path, contents) =>
            Labeler.CategoryOf(new Culprit { Kind = CulpritKind.Folder, Path = path, Label = Labeler.Describe(path), Contents = contents });
        Check(cat(@"C:\Users\Public\Documents\Libs\Strings", "Mostly audio samples / instrument data (.ncw 100%)") == Category.Libraries, "sample library -> Libraries");
        Check(cat(@"C:\Users\me\Downloads", "Mostly installers (.exe 80%)") == Category.YourFiles, "Downloads -> Your files");
        Check(cat(@"C:\Program Files\CoolSynth", "Mostly program files (.dll 100%)") == Category.Programs, "program -> Apps & programs");
        Check(cat(@"D:\SteamLibrary\steamapps\common\Elden Ring", "Mostly game data (.pak 90%)") == Category.Games, "Steam -> Games");
        Check(cat(@"C:\Users\me\AppData\Local\Google\Chrome\User Data\Default\Cache", "") == Category.Caches, "Chrome cache -> Caches");
        Check(cat(@"C:\Windows\SoftwareDistribution\Download", "") == Category.System, "Windows Update -> System");
        Check(Labeler.FriendlyName(new Culprit { Kind = CulpritKind.Folder, Path = @"C:\Users\Public\Documents\Libs\Strings" }) == "Strings", "friendly name is the folder's own name");
    }

    static void FormatChecks()
    {
        Console.WriteLine("Formatting");
        Check(Format.Bytes(30 * GB) == (30.0).ToString("0.0") + " GB", "30 GB");
        Check(Format.Bytes(100 * MB) == "100 MB", "100 MB");
        Check(Format.Percent(0.004) == "<1%", "tiny share");
    }
}
