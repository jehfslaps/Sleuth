using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpaceSleuth.Core
{
    /// <summary>
    /// Turns raw folder paths into human hints: "Steam game: X", "Kontakt library", "hidden AppData folder"...
    /// Everything here is plain string logic so it can be unit tested on any OS.
    /// </summary>
    public static class Labeler
    {
        sealed class Rule
        {
            public Regex Pattern;
            public string Label;
            public Rule(string pattern, string label)
            {
                Pattern = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
                Label = label;
            }
        }

        // Order matters: the first rule that matches wins, so specific rules come before generic ones.
        static readonly Rule[] Rules =
        {
            // Windows' own space users
            new Rule(@"\\\$recycle\.bin\\", "Recycle Bin (deleted files still use space until emptied)"),
            new Rule(@"^[a-z]:\\system volume information\\", "System Restore / shadow copy storage"),
            new Rule(@"^[a-z]:\\windows\\softwaredistribution\\", "Windows Update downloads"),
            new Rule(@"^[a-z]:\\windows\\winsxs\\", "Windows component store (installed updates)"),
            new Rule(@"^[a-z]:\\windows\\installer\\", "Windows Installer cache (MSI / patch files)"),
            new Rule(@"\\deliveryoptimization\\", "Delivery Optimization cache (Windows Update sharing)"),
            new Rule(@"^[a-z]:\\windows\\(livekernelreports|minidump)\\", "Crash dumps"),
            new Rule(@"\\crashdumps\\", "Crash dumps"),
            new Rule(@"\\microsoft\\windows\\wer\\", "Windows Error Reporting files"),
            new Rule(@"^[a-z]:\\windows\.old\\", "Previous Windows installation (Windows.old)"),
            new Rule(@"^[a-z]:\\\$(windows\.~bt|windows\.~ws|winreagent|getcurrent|sysreset)\\", "Windows upgrade / reset files"),
            new Rule(@"^[a-z]:\\programdata\\microsoft\\search\\", "Windows Search index"),
            new Rule(@"^[a-z]:\\programdata\\package cache\\", "Installer package cache"),
            new Rule(@"^[a-z]:\\windows\\temp\\", "Temp files"),
            new Rule(@"\\appdata\\local\\temp\\", "Temp files (often leftovers from installers)"),
            new Rule(@"\\inetcache\\", "Internet cache"),
            new Rule(@"^[a-z]:\\windows\\", "Windows system files"),

            // Music production
            new Rule(@"\\splice\\", "Splice samples / plugins"),
            new Rule(@"\\native instruments\\native access\\", "Native Access downloads"),
            new Rule(@"\\native instruments\\([^\\]+)\\", "Native Instruments: {1}"),
            new Rule(@"\\native instruments\\", "Native Instruments content"),
            new Rule(@"\\kontakt[^\\]*\\", "Kontakt library / content"),
            new Rule(@"\\spitfire( audio)?\\", "Spitfire Audio library"),
            new Rule(@"\\ableton\\", "Ableton Live (packs / library)"),
            new Rule(@"\\image-line\\", "FL Studio (Image-Line)"),
            new Rule(@"\\arturia\\", "Arturia instruments / presets"),
            new Rule(@"\\spectrasonics\\", "Spectrasonics library (Omnisphere / Keyscape)"),
            new Rule(@"\\toontrack\\", "Toontrack library (EZdrummer / Superior Drummer)"),
            new Rule(@"\\eastwest\\", "EastWest library"),
            new Rule(@"\\uvi\\", "UVI library"),
            new Rule(@"\\heavyocity\\", "Heavyocity library"),
            new Rule(@"\\orchestral tools\\", "Orchestral Tools library"),
            new Rule(@"\\steinberg\\", "Steinberg (Cubase / content)"),
            new Rule(@"\\izotope\\", "iZotope"),
            new Rule(@"\\waves( audio)?\\", "Waves plugins / content"),
            new Rule(@"\\plugin alliance\\", "Plugin Alliance"),
            new Rule(@"\\slate digital\\", "Slate Digital"),
            new Rule(@"\\universal audio\\", "Universal Audio"),
            new Rule(@"\\xfer\\", "Xfer (Serum presets / content)"),
            new Rule(@"\\loopcloud\\", "Loopcloud samples"),
            new Rule(@"\\presonus\\", "PreSonus / Studio One"),
            new Rule(@"\\bitwig studio\\", "Bitwig Studio"),
            new Rule(@"\\reason studios\\", "Reason Studios"),
            new Rule(@"\\common files\\avid\\audio\\plug-ins\\", "AAX audio plugins"),
            new Rule(@"\\avid\\", "Avid / Pro Tools"),
            new Rule(@"\\(vst3|vstplugins|vst plugins|vst|vst2|clap)\\", "Audio plugins"),

            // Games
            new Rule(@"\\steamapps\\common\\([^\\]+)\\", "Steam game: {1}"),
            new Rule(@"\\steamapps\\downloading\\", "Steam download / update in progress"),
            new Rule(@"\\steamapps\\shadercache\\", "Steam shader cache"),
            new Rule(@"\\steamapps\\workshop\\", "Steam Workshop mods"),
            new Rule(@"\\steamapps\\", "Steam library"),
            new Rule(@"\\epic games\\([^\\]+)\\", "Epic Games: {1}"),
            new Rule(@"\\xboxgames\\([^\\]+)\\", "Xbox / Game Pass game: {1}"),
            new Rule(@"\\windowsapps\\", "Microsoft Store apps"),
            new Rule(@"\\(battle\.net|blizzard app)\\", "Battle.net"),
            new Rule(@"\\ea games\\([^\\]+)\\", "EA game: {1}"),
            new Rule(@"\\ubisoft game launcher\\", "Ubisoft game"),
            new Rule(@"\\riot games\\", "Riot Games"),
            new Rule(@"\\gog galaxy\\games\\([^\\]+)\\", "GOG game: {1}"),

            // Dev tools, AI models, virtual machines
            new Rule(@"docker", "Docker data (virtual disk / images)"),
            new Rule(@"\\packages\\canonicalgroup", "WSL Linux virtual disk"),
            new Rule(@"\\wsl\\", "WSL Linux virtual disk"),
            new Rule(@"\\(virtualbox vms|hyper-v|virtual hard disks)\\", "Virtual machine disks"),
            new Rule(@"\\(npm-cache|\.npm)\\", "npm cache"),
            new Rule(@"\\\.nuget\\packages\\", "NuGet package cache"),
            new Rule(@"\\pip\\cache\\", "Python pip cache"),
            new Rule(@"\\(anaconda3|miniconda3|\.conda)\\", "Python / Conda environments"),
            new Rule(@"\\\.gradle\\", "Gradle cache"),
            new Rule(@"\\\.cargo\\", "Rust / Cargo cache"),
            new Rule(@"\\\.cache\\huggingface\\", "AI model downloads (Hugging Face)"),
            new Rule(@"\\\.ollama\\", "Ollama AI models"),
            new Rule(@"\\(\.lmstudio|lm studio)\\", "LM Studio AI models"),

            // Browsers and common apps
            new Rule(@"\\google\\chrome\\user data\\", "Chrome browser data / cache"),
            new Rule(@"\\microsoft\\edge\\user data\\", "Edge browser data / cache"),
            new Rule(@"\\mozilla\\firefox\\", "Firefox data / cache"),
            new Rule(@"\\bravesoftware\\", "Brave browser data / cache"),
            new Rule(@"\\opera software\\", "Opera browser data / cache"),
            new Rule(@"\\adobe\\common\\media cache", "Adobe media cache"),
            new Rule(@"\\adobe\\", "Adobe app data"),
            new Rule(@"\\blackmagic design\\", "DaVinci Resolve (cache / project files)"),
            new Rule(@"\\nvidia( corporation)?\\", "NVIDIA driver / app files"),
            new Rule(@"\\spotify\\", "Spotify cache"),
            new Rule(@"\\discord\\", "Discord"),
            new Rule(@"\\onedrive[^\\]*\\", "OneDrive synced files"),
            new Rule(@"\\dropbox\\", "Dropbox synced files"),
            new Rule(@"\\google drive\\", "Google Drive files"),
            new Rule(@"\\icloud ?drive\\", "iCloud Drive files"),
            new Rule(@"\\appdata\\local\\packages\\([^\\]+)\\", "Store app data: {1}"),

            // User folders and general locations
            new Rule(@"^[a-z]:\\users\\public\\", "Shared Public folder (plugin installers often put content here)"),
            new Rule(@"^[a-z]:\\users\\[^\\]+\\downloads\\", "Downloads folder"),
            new Rule(@"^[a-z]:\\users\\[^\\]+\\desktop\\", "Desktop"),
            new Rule(@"^[a-z]:\\users\\[^\\]+\\documents\\", "Documents folder"),
            new Rule(@"^[a-z]:\\users\\[^\\]+\\music\\", "Music folder"),
            new Rule(@"^[a-z]:\\users\\[^\\]+\\videos\\", "Videos folder"),
            new Rule(@"^[a-z]:\\users\\[^\\]+\\pictures\\", "Pictures folder"),
            new Rule(@"^[a-z]:\\program files( \(x86\))?\\common files\\([^\\]+)\\", "Shared program files: {2}"),
            new Rule(@"^[a-z]:\\program files( \(x86\))?\\([^\\]+)\\", "Installed program: {2}"),
            new Rule(@"^[a-z]:\\programdata\\([^\\]+)\\", "Program data: {1}"),
            new Rule(@"\\appdata\\(local|roaming|locallow)\\programs\\([^\\]+)\\", "Installed app: {2}"),
            new Rule(@"\\appdata\\(local|roaming|locallow)\\([^\\]+)\\", "App data: {2}"),
            new Rule(@"^[a-z]:\\users\\([^\\]+)\\", "User folder ({1})"),
        };

        // Folders that hold many unrelated things. We never report one of these as a single culprit
        // when it can be broken down into the specific things inside it.
        static readonly Regex[] Containers = new[]
        {
            @"^[a-z]:\\$",
            @"^[a-z]:\\users\\$",
            @"^[a-z]:\\users\\[^\\]+\\$",
            @"^[a-z]:\\users\\[^\\]+\\appdata\\$",
            @"^[a-z]:\\users\\[^\\]+\\appdata\\(local|roaming|locallow)\\$",
            @"^[a-z]:\\users\\[^\\]+\\appdata\\local\\(packages|programs|temp|microsoft)\\$",
            @"^[a-z]:\\users\\[^\\]+\\appdata\\roaming\\microsoft\\$",
            @"^[a-z]:\\users\\[^\\]+\\(downloads|desktop|documents|music|videos|pictures)\\$",
            @"^[a-z]:\\users\\[^\\]+\\onedrive[^\\]*\\$",
            @"^[a-z]:\\users\\[^\\]+\\onedrive[^\\]*\\(documents|desktop|pictures)\\$",
            @"^[a-z]:\\users\\public\\$",
            @"^[a-z]:\\users\\public\\(documents|downloads|music|videos|pictures)\\$",
            @"^[a-z]:\\programdata\\$",
            @"^[a-z]:\\program files( \(x86\))?\\$",
            @"^[a-z]:\\program files( \(x86\))?\\common files\\$",
            @"^[a-z]:\\program files\\windowsapps\\$",
            @"^[a-z]:\\windows\\$",
            @"^[a-z]:\\windows\\(system32|syswow64|temp)\\$",
            @"\\steamapps\\$",
            @"\\steamapps\\common\\$",
            @"\\epic games\\$",
            @"\\xboxgames\\$",
            @"\\(vst3|vstplugins|vst plugins|vst|vst2|clap)\\$",
            @"\\common files\\avid\\audio\\plug-ins\\$",
        }.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled)).ToArray();

        static string WithSlash(string path)
        {
            return path.EndsWith("\\", StringComparison.Ordinal) ? path : path + "\\";
        }

        /// <summary>A friendly guess at what a folder is.</summary>
        public static string Describe(string folderPath)
        {
            string p = WithSlash(folderPath);
            foreach (var rule in Rules)
            {
                var m = rule.Pattern.Match(p);
                if (!m.Success) continue;
                string label = rule.Label;
                for (int g = 1; g < m.Groups.Count; g++)
                    label = label.Replace("{" + g + "}", m.Groups[g].Value);
                return label;
            }
            return "Folder";
        }

        public static bool IsGenericContainer(string folderPath)
        {
            string p = WithSlash(folderPath);
            foreach (var r in Containers)
                if (r.IsMatch(p)) return true;
            return false;
        }

        /// <summary>Why someone might never have seen this folder, judged from the path alone. Null if it's an ordinary spot.</summary>
        public static string HiddenReasonFromPath(string folderPath)
        {
            string p = WithSlash(folderPath).ToLowerInvariant();
            if (p.Length < 3) return null;
            string rest = p.Substring(3); // after "c:\"
            if (rest.StartsWith("$") || rest.StartsWith("system volume information\\"))
                return "Hidden system folder";
            if (p.Contains("\\appdata\\"))
                return "Inside AppData, a hidden folder in your user profile";
            if (rest.StartsWith("programdata\\"))
                return "Inside ProgramData, a hidden folder";
            if (rest.StartsWith("windows\\"))
                return "Inside the Windows folder";
            if (rest.StartsWith("users\\public\\"))
                return "In the shared Public folder, which most people never open";
            if (p.Contains("\\common files\\"))
                return "Inside Program Files\\Common Files, an out-of-the-way spot";
            return null;
        }

        // ---- Categories (each gets one consistent color in the UI) ----

        static readonly Regex GameLabel = new Regex(@"steam|epic games|xbox|game|gog|battle\.net|riot|ubisoft|microsoft store apps", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex CacheLabel = new Regex(@"temp files|cache|crash dump|error reporting|internet cache|logs", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex SystemLabel = new Regex(@"^(windows|recycle bin|system restore|previous windows|installer package|delivery optimization)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex StudioLabel = new Regex(@"splice|native|kontakt|spitfire|ableton|fl studio|arturia|spectrasonics|toontrack|eastwest|uvi|heavyocity|orchestral|steinberg|izotope|waves|plugin|slate|universal audio|xfer|loopcloud|presonus|bitwig|reason|avid|aax|audio|shared public folder", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex PersonalLabel = new Regex(@"^(downloads|desktop|documents|music|videos|pictures|user folder|onedrive|dropbox|google drive|icloud)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static Category CategoryOf(Culprit c)
        {
            if (c == null) return Category.Programs;
            if (c.Kind == CulpritKind.SystemArea) return Category.System;
            if (c.Kind == CulpritKind.Scattered) return Category.Caches;
            string label = c.Label ?? "";
            string contents = c.Contents ?? "";
            if (SystemLabel.IsMatch(label)) return Category.System;
            if (CacheLabel.IsMatch(label)) return Category.Caches;
            if (GameLabel.IsMatch(label)) return Category.Games;
            if (StudioLabel.IsMatch(label) || contents.StartsWith("Mostly audio") || contents.StartsWith("Mostly video") || contents.StartsWith("Mostly images"))
                return Category.Libraries;
            if (PersonalLabel.IsMatch(label)) return Category.YourFiles;
            if (contents.StartsWith("Mostly game data")) return Category.Games;
            return Category.Programs;
        }

        public static string CategoryName(Category c)
        {
            switch (c)
            {
                case Category.YourFiles: return "Your files";
                case Category.Libraries: return "Libraries & media";
                case Category.Programs: return "Apps & programs";
                case Category.Games: return "Games";
                case Category.Caches: return "Caches & temp";
                default: return "System";
            }
        }

        /// <summary>Short, human name for a culprit card: the folder's own name rather than the whole path.</summary>
        public static string FriendlyName(Culprit c)
        {
            if (c == null) return "";
            if (c.Kind == CulpritKind.Scattered) return "Little bits everywhere";
            if (c.Kind == CulpritKind.SystemArea) return c.Label;
            string p = (c.Path ?? "").TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            if (i < 0) return p.Length == 2 ? p + "\\ (top of the drive)" : p;
            return p.Substring(i + 1);
        }

        // ---- What kind of files are these? ----

        static readonly Dictionary<string, string> Categories = BuildCategories();

        static Dictionary<string, string> BuildCategories()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Action<string, string> add = (cat, exts) =>
            {
                foreach (var e in exts.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) d["." + e] = cat;
            };
            add("audio samples / instrument data",
                "wav aif aiff flac mp3 ogg m4a wma aac ncw nkx nkc nkr nki nkm nicnt ksd nmsv nabs nksf nksn sfz sf2 exs rex rx2 als alp adg adv amxd flp fst ptx ptf cpr vstpreset fxp fxb h2p spf mid midi");
            add("video", "mp4 mov mkv avi webm m4v wmv braw r3d mxf");
            add("images", "jpg jpeg png gif bmp tif tiff heic raw cr2 cr3 nef arw dng psd webp");
            add("archives / disc images", "zip rar 7z tar gz bz2 xz iso img dmg");
            add("installers", "exe msi msix msixbundle appx appxbundle cab msp msu");
            add("virtual disks", "vhdx vhd vmdk vdi qcow2 avhdx");
            add("game data", "pak ucas utoc vpk bundle forge big arc rpf bnk pck assets resource wem ba2 bsa esm upk");
            add("program files", "dll sys so lib pdb obj node jar");
            add("unfinished downloads", "crdownload part partial download opdownload !ut");
            add("temp files", "tmp temp");
            add("caches / databases", "db sqlite ldb edb idx cache");
            add("documents", "pdf docx doc xlsx xls pptx ppt txt");
            add("logs / crash dumps", "dmp hdmp etl log evtx");
            add("AI models", "gguf safetensors ckpt onnx");
            return d;
        }

        public static string CategoryOf(string fileName)
        {
            string ext = ExtensionOf(fileName);
            string cat;
            return ext.Length > 0 && Categories.TryGetValue(ext, out cat) ? cat : "other files";
        }

        static string ExtensionOf(string fileName)
        {
            int dot = fileName.LastIndexOf('.');
            if (dot <= 0 || dot == fileName.Length - 1) return "";
            return fileName.Substring(dot).ToLowerInvariant();
        }

        /// <summary>e.g. "Mostly audio samples / instrument data (.wav 84%)" or "Mixed: video 52%, images 30%".</summary>
        public static string Summarize(IEnumerable<ChangedFile> files)
        {
            var byCat = new Dictionary<string, double>();
            var byExt = new Dictionary<string, double>();
            double total = 0;
            foreach (var f in files)
            {
                double b = f.AddedBytes + f.PossibleBytes;
                if (b <= 0) continue;
                total += b;
                string name = f.Name;
                string cat = CategoryOf(name);
                string ext = ExtensionOf(name);
                if (ext.Length == 0) ext = "(no extension)";
                double v;
                byCat.TryGetValue(cat, out v); byCat[cat] = v + b;
                byExt.TryGetValue(ext, out v); byExt[ext] = v + b;
            }
            if (total <= 0) return "";
            var cats = byCat.OrderByDescending(kv => kv.Value).ToList();
            var topExt = byExt.OrderByDescending(kv => kv.Value).First();
            int extPct = (int)Math.Round(100 * topExt.Value / total);
            if (cats[0].Value >= 0.6 * total)
                return "Mostly " + cats[0].Key + " (" + topExt.Key + " " + extPct + "%)";
            var parts = cats.Take(2).Select(kv => kv.Key + " " + (int)Math.Round(100 * kv.Value / total) + "%");
            return "Mixed: " + string.Join(", ", parts);
        }
    }
}
