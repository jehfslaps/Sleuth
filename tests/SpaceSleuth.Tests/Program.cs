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
    }

    static void FormatChecks()
    {
        Console.WriteLine("Formatting");
        Check(Format.Bytes(30 * GB) == (30.0).ToString("0.0") + " GB", "30 GB");
        Check(Format.Bytes(100 * MB) == "100 MB", "100 MB");
        Check(Format.Percent(0.004) == "<1%", "tiny share");
    }
}
