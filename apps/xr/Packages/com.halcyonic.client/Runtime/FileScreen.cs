#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>A task's file's four sections, left to right (ADR 0026).</summary>
    public enum FileSection
    {
        Waiting,
        Activity,
        Changes,
        Checks,
    }

    /// <summary>
    /// One answer a file shows: its brief lines for the section's page, and its full lines for the side
    /// panel a line opens (<see cref="AnswerDepth"/>), from one read.
    /// </summary>
    public sealed class FileAnswer
    {
        public FileAnswer(SectionPresentation brief, SectionPresentation full)
        {
            Brief = brief ?? throw new ArgumentNullException(nameof(brief));
            Full = full ?? throw new ArgumentNullException(nameof(full));
        }

        public SectionPresentation Brief { get; }

        public SectionPresentation Full { get; }
    }

    /// <summary>
    /// What an open file is in the middle of, besides the work itself: the section chosen, the line
    /// whose side panel shows, which page of each, the instructions offered where there is no keyboard,
    /// where the person is in the agent's question or in the request an approval answers, and the
    /// answers Changes and Checks read, null while they are still being read. The director keeps one
    /// for each open file; <see cref="FileScreens"/> reads it.
    /// </summary>
    public sealed class FileScreen
    {
        private FileSection section;
        private string? chosen;

        /// <summary>The section showing. Another section starts at its first page, with no side panel.</summary>
        public FileSection Section
        {
            get => section;
            set
            {
                if (value == section) return;
                section = value;
                chosen = null;
                Page = 0;
                SidePart = 0;
            }
        }

        /// <summary>The key of the line whose side panel shows, or null. Another starts at its first part.</summary>
        public string? Chosen
        {
            get => chosen;
            set
            {
                if (value != chosen) SidePart = 0;
                chosen = value;
            }
        }

        /// <summary>The page of the section showing, from 0, kept within its pages when drawn.</summary>
        public int Page { get; internal set; }

        /// <summary>How many pages the section took when last drawn.</summary>
        public int Pages { get; internal set; } = 1;

        /// <summary>The part of the side panel showing, from 0.</summary>
        public int SidePart { get; internal set; }

        /// <summary>How many parts the side panel took when last drawn; 0 while none shows.</summary>
        public int SideParts { get; internal set; }

        /// <summary>
        /// Turns to the next page, or from the last back to the first: of the side panel while one shows
        /// in parts, else of the section.
        /// </summary>
        public void NextPage()
        {
            if (Chosen != null && SideParts > 1) SidePart = (SidePart + 1) % SideParts;
            else if (Pages > 1) Page = (Page + 1) % Pages;
        }

        /// <summary>What something the person just did came to, for a few seconds; null when nothing.</summary>
        public string? Notice { get; set; }

        /// <summary>Hold to talk is offered: in a development build, never in the demonstration (ADR 0021).</summary>
        public bool Speak { get; set; }

        /// <summary>The instructions offered where there is no keyboard, while they show in Activity's page; null otherwise.</summary>
        public IReadOnlyList<PresetInstruction>? Presets { get; set; }

        /// <summary>The zone the activity's times are in.</summary>
        public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

        /// <summary>How reading the history goes, said while there is no activity to show; empty when nothing needs saying.</summary>
        public string ActivityNote { get; set; } = "";

        /// <summary>What changed, why and how it was built, from the understanding source; null while still being read.</summary>
        public FileAnswer? WhatChanged { get; set; }

        public FileAnswer? WhyChanged { get; set; }

        public FileAnswer? HowBuilt { get; set; }

        /// <summary>What was checked, from both sources; null while still being read.</summary>
        public FileAnswer? Checked { get; set; }

        internal static int PartsOf(int rows, int perPart) => Math.Max(1, (Math.Max(0, rows) + Math.Max(1, perPart) - 1) / Math.Max(1, perPart));
    }
}
