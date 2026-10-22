# Sleuth

**A tiny Windows 11 app that answers one question: _where did my disk space just go?_**

You had 250 GB free this morning and 238 GB now. Sleuth looks at what changed on your drives in the last few hours or days and gives you a ranked list, from the most likely place your space went to the least likely:

| # | Likelihood | Space added | Where | What it probably is |
|---|---|---|---|---|
| 1 | 71% | 30.2 GB | `C:\Users\Public\Documents\Cinematic Strings` | Shared Public folder · Mostly audio samples (.ncw 98%) · **New folder** · *in a folder most people never open* |
| 2 | 19% | 8.0 GB | C: System Restore points / shadow copies | Grew since your last scan |
| 3 | 6% | 2.4 GB | `C:\Users\you\Downloads` | Downloads folder · Mostly installers |
| 4 | <1% | 100 MB | `C:\Program Files\CoolSynth` | Installed program: CoolSynth |
| | 3% | 1.1 GB | Many small changes in different folders | App updates, caches, logs |

Click any card to see the individual files. Double-click to open it in File Explorer.

![Sleuth preview (sample data)](docs/preview.png)

## Look & feel

Sleuth uses **Glitch Swiss: sound as data**, the design language from the MERCY drum processor by jehf slaps: a clean Swiss layout that has been through a broken photocopier. Paper ground, hairlines, everything lowercase, tiny mono lab labels (`fig. 01`, `x: 182 y: 242`), flat accent blocks, red pixel stairs, an RGB-split `sleuth.` wordmark and fine grain over everything. Disk space is shown the way MERCY shows sound: the drop on C: is drawn as a waveform specimen on a mint panel.

Each kind of place keeps one small square color everywhere: **blue** = libraries & media, **mint** = your files, **violet** = apps & programs, **yellow** = games, **grey** = caches & temp, **orange** = system space. Red only ever means "selected" or "look at this". There's a light and a dark mode (top right); it starts in whichever mode Windows uses.

## Download

Go to **Releases → latest** on this repo and download **`Sleuth-Setup.exe`**. Run it to install Sleuth like any other program: it adds a Start menu shortcut (and a desktop icon if you want one), and you can uninstall it any time from **Settings → Apps**.

Prefer no install? `Sleuth.exe` on the same page is the whole app in one file (well under 1 MB). Just run it.

The first time, Windows SmartScreen may say *"Windows protected your PC"* because the app isn't code-signed. Click **More info → Run anyway**. Sleuth asks for administrator rights, which it needs to read Windows' change log and restore-point info.

## How it works

1. **Windows' change journal.** NTFS drives keep a log (the USN journal) of every file created, grown, renamed or deleted. Sleuth reads it the same way the *Everything* search tool does, so it usually doesn't have to crawl your whole drive.
2. **File dates as a backup.** If the change log doesn't reach back far enough, if the drive isn't NTFS (e.g. an exFAT external sample drive), or if you tick **Thorough**, it also checks the created/modified date of every file.
3. **Finding the culprit folder.** All the new data is added up into a folder tree. A brand-new folder (an install, an unzipped pack, a sample library) is reported as one item. Busy folders like `AppData` or `Downloads` are broken down into the specific things inside them.
4. **Likelihood** = that item's share of all the new data found. Things we're certain about (new files) count fully. Existing files that were merely modified count only a little, because we don't know their size before.
5. **System space.** Restore points/shadow copies, the hibernation file, the page file, the swap file, crash dumps and the Recycle Bin are measured separately and listed in their own section.
6. **Before → now for C:.** The data column on the right (fig. 01) shows how much free space C: had the last time you opened Sleuth, how much it has now, and a waveform specimen plus meters splitting the drop into where it most likely went, plus anything that couldn't be traced ("not found yet"). The time window defaults to **since last scan**, so a week later it covers exactly that week.
7. **Memory between scans.** Each scan saves a small history file (`%LOCALAPPDATA%\SpaceSleuth\history.txt`). Next time, the app can say *"free space dropped 11.7 GB since your last scan"* and measure exactly how much restore points and big existing files (game files, virtual disks) grew.

It also recognises common locations: Steam/Epic/Xbox games, Windows Update caches, browser caches, Docker/WSL disks, and music-production content (Kontakt/Native Instruments, Spitfire, Splice, Ableton, FL Studio, Arturia, Spectrasonics, Toontrack, audio plugin folders, and more). Folders hidden from normal view (AppData, ProgramData, the Public folder) are flagged.

Sleuth only reads. It never deletes or changes anything.

## Building

GitHub Actions builds `Sleuth.exe` and the installer `Sleuth-Setup.exe` (made with [Inno Setup](https://jrsoftware.org/isinfo.php) from `installer/Sleuth.iss`) on every push to `main`, and publishes both as the `latest` release. To build locally on Windows with the .NET SDK installed:

```
dotnet build src/SpaceSleuth/SpaceSleuth.csproj -c Release -o out
```

The app targets .NET Framework 4.8, which is built into Windows 11, so the .exe has no dependencies.

## Credits

Design: the Glitch Swiss design kit by jehf slaps (from MERCY v1.7). Fonts embedded in the app: [Archivo](https://github.com/Omnibus-Type/Archivo), [JetBrains Mono](https://github.com/JetBrains/JetBrainsMono) and [Unbounded](https://github.com/googlefonts/unbounded), all under the SIL Open Font License (see `licenses/`); the copies inside the app are trimmed to Latin characters and renamed so they can't clash with fonts you have installed.
