using System;
using System.Collections.Generic;

namespace SpaceSleuth.Core
{
    public enum ChangeKind
    {
        /// <summary>File was created inside the time window. All of its size is new.</summary>
        New,
        /// <summary>Existing file grew, and we know its earlier size from a previous scan.</summary>
        GrewKnown,
        /// <summary>Existing file was written to / extended, but we have no earlier size for it.</summary>
        GrewUnknown
    }

    public sealed class ChangedFile
    {
        public string Path;
        /// <summary>Current size on disk (allocated bytes).</summary>
        public long SizeOnDisk;
        /// <summary>Bytes we are confident were added during the window.</summary>
        public long AddedBytes;
        /// <summary>For existing files that grew with unknown earlier size: "up to" this many bytes.</summary>
        public long PossibleBytes;
        public DateTime TimeUtc;
        public ChangeKind Kind;

        public string Name
        {
            get
            {
                int i = Path.LastIndexOf('\\');
                return i >= 0 ? Path.Substring(i + 1) : Path;
            }
        }

        public string Folder
        {
            get
            {
                int i = Path.LastIndexOf('\\');
                if (i < 0) return "";
                string f = Path.Substring(0, i);
                return f.Length == 2 && f[1] == ':' ? f + "\\" : f;
            }
        }
    }

    /// <summary>Broad kind of culprit. The UI gives each one its own color everywhere.</summary>
    public enum Category
    {
        YourFiles,
        Libraries,
        Programs,
        Games,
        Caches,
        System
    }

    public enum CulpritKind
    {
        Folder,
        SystemArea,
        Scattered
    }

    public sealed class Culprit
    {
        public int Rank;
        public CulpritKind Kind;
        /// <summary>Folder path, or a description for system areas.</summary>
        public string Path;
        /// <summary>Friendly guess at what this is, e.g. "Steam game: Elden Ring".</summary>
        public string Label;
        /// <summary>What the files look like, e.g. "Mostly audio samples (.wav 84%)".</summary>
        public string Contents;
        public long AddedBytes;
        public long PossibleBytes;
        /// <summary>A rough estimate when the exact amount can't be known yet (e.g. a brand-new restore point on a first scan).</summary>
        public long EstimateBytes;
        public double Score;
        /// <summary>Share (0..1) of all the growth found. This is the "likelihood" shown to the user.</summary>
        public double Likelihood;
        public string Confidence;
        public DateTime FirstUtc;
        public DateTime LastUtc;
        public int NewFiles;
        public int GrewFiles;
        public bool IsNewFolder;
        /// <summary>Only the files directly in this folder (bigger subfolders are listed separately).</summary>
        public bool IsRemainder;
        /// <summary>Why the user may not have noticed this place (hidden folder etc.), or null.</summary>
        public string HiddenReason;
        public string Note;
        /// <summary>For system areas: what to open when the user double-clicks.</summary>
        public string OpenTarget;
        public List<ChangedFile> Files = new List<ChangedFile>();
    }

    public sealed class SystemArea
    {
        public string Drive;
        public string Key;
        public string Name;
        public long CurrentBytes;
        public long? PreviousBytes;
        public DateTime? PreviousUtc;
        /// <summary>Something that happened in the window, e.g. "Restore point created 3:12 PM".</summary>
        public string Activity;
        public bool ActiveInWindow;
        /// <summary>Restore points / shadow copies: how many were made in the window, how many exist, and when the latest was made.</summary>
        public int NewInWindow;
        public int TotalCount;
        public DateTime? LatestNewUtc;
        public string Explanation;
        public string OpenTarget;

        public long? Delta
        {
            get { return PreviousBytes.HasValue ? CurrentBytes - PreviousBytes.Value : (long?)null; }
        }
    }
}
