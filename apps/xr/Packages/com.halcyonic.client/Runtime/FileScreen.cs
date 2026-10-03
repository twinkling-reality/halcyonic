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

        /// <summary>The system keyboard can open here; where it can't, no row whose only job is to open it shows.</summary>
        public bool KeyboardOffered { get; set; } = true;

        /// <summary>Hold to talk is offered: in a development build, never in the demonstration (ADR 0021).</summary>
        public bool Speak { get; set; }

        /// <summary>The instructions offered where there is no keyboard, while they show in Activity's page; null otherwise.</summary>
        public IReadOnlyList<PresetInstruction>? Presets
        {
            get => presets;
            set
            {
                // The same instructions offered again, as a director does on every refresh, keep
                // what was chosen; other ones, or none, clear it.
                if (Same(presets, value)) return;
                presets = value;
                ChosenPreset = null;
            }
        }

        private IReadOnlyList<PresetInstruction>? presets;

        private static bool Same(IReadOnlyList<PresetInstruction>? a, IReadOnlyList<PresetInstruction>? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Count != b.Count) return false;
            for (var index = 0; index < a.Count; index++)
            {
                if (a[index].Label != b[index].Label || a[index].Text != b[index].Text) return false;
            }
            return true;
        }

        /// <summary>The instruction chosen among <see cref="Presets"/>, by its index; choosing only lights it.</summary>
        public int? ChosenPreset { get; private set; }

        /// <summary>The person chose an instruction offered: it lights, and Tell it would send its words as shown.</summary>
        public void ChoosePreset(int index)
        {
            if (presets != null && index >= 0 && index < presets.Count) ChosenPreset = index;
        }

        /// <summary>The words Tell it sends: the chosen instruction's, exactly as its row shows them; null while none is chosen.</summary>
        public string? PresetToSend => presets != null && ChosenPreset is int index ? presets[index].Text : null;

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

        /// <summary>Where the person is in the agent's question.</summary>
        public FileQuestion Question { get; } = new FileQuestion();

        /// <summary>
        /// Reads <paramref name="draft"/>'s question, each prompt laid out as <paramref name="measured"/>
        /// says on pages as <paramref name="page"/> holds them and side panels as <paramref name="side"/>
        /// does, each source line already taken: from its first prompt when it is another question, else
        /// where the person was.
        /// </summary>
        public void ReadQuestion(QuestionDraft draft, IReadOnlyList<PromptMeasure> measured, PageBudget page, PageBudget side) =>
            Question.Show(draft, measured, page, side);

        private int armingRead = -1;
        private readonly HashSet<int> drawn = new HashSet<int>();
        private DateTimeOffset? partDrawnAt;

        /// <summary>A press on the part's row this soon after its part was first drawn turns nothing, so a double press can't skip a part almost unseen.</summary>
        public static readonly TimeSpan TurnGuard = TimeSpan.FromSeconds(0.4);

        /// <summary>The request as it was measured, so a measurement counts only for the text it was made of.</summary>
        public string? RequestText { get; private set; }

        /// <summary>The rows the whole request an armed approval or denial answers wraps to, as the layout measured it.</summary>
        public int RequestRows { get; private set; }

        /// <summary>The rows of the request each part shows.</summary>
        public int RequestPartRows { get; private set; } = 1;

        /// <summary>The part of the request showing, from 0.</summary>
        public int RequestPart { get; private set; }

        /// <summary>How many parts the request takes.</summary>
        public int RequestParts => PartsOf(RequestRows, RequestPartRows);

        /// <summary>Every part of the request has been drawn in the layout measured now.</summary>
        public bool RequestDrawnWhole { get; private set; }

        /// <summary>
        /// The layout measured <paramref name="request"/>, the request the armed approval or denial
        /// answers: it wraps to <paramref name="rows"/> rows, and a part holds
        /// <paramref name="partRows"/>. Measured again the same, nothing changes. Measured differently
        /// under the same confirmation, as at another text size: a request drawn whole stays read and
        /// shows its last part; one drawn only in part is read again from its first, as its parts no
        /// longer map to what was drawn (<see cref="WorkspaceSteering.ReadAgain"/>). Measured for
        /// another confirmation or another text, it starts afresh.
        /// </summary>
        public void ReadRequest(string request, int rows, int partRows, WorkspaceSteering steering)
        {
            rows = Math.Max(1, rows);
            partRows = Math.Max(1, partRows);
            var sameArming = steering.Armings == armingRead;
            if (sameArming && request == RequestText && rows == RequestRows && partRows == RequestPartRows) return;
            var keep = sameArming && request == RequestText && RequestDrawnWhole;
            if (sameArming && !keep) steering.ReadAgain();
            armingRead = steering.Armings;
            RequestText = request;
            RequestRows = rows;
            RequestPartRows = partRows;
            drawn.Clear();
            partDrawnAt = null;
            if (keep)
            {
                for (var part = 0; part < RequestParts; part++) drawn.Add(part);
                RequestPart = RequestParts - 1;
            }
            else
            {
                RequestDrawnWhole = false;
                RequestPart = 0;
            }
        }

        /// <summary>
        /// The measurement holds for what <paramref name="steering"/> has armed now: made in this arming,
        /// of this very text, which is the text the confirmation was armed with.
        /// </summary>
        public bool Measured(WorkspaceSteering steering, string request) =>
            armingRead == steering.Armings && RequestRows > 0 && request == RequestText && request == steering.ArmedRequest;

        /// <summary>
        /// The view reports it drew <paramref name="part"/> of the request, the part showing, in the
        /// layout measured now, at <paramref name="now"/>: only this counts a part as read, never
        /// building the page nor turning to it. Once every part has been drawn,
        /// <paramref name="steering"/> holds the whole request shown.
        /// </summary>
        public void RequestDrawn(int part, WorkspaceSteering steering, DateTimeOffset now)
        {
            if (armingRead != steering.Armings || RequestRows == 0 || part != RequestPart || steering.ArmedRequest != RequestText) return;
            partDrawnAt ??= now;
            drawn.Add(part);
            if (drawn.Count >= RequestParts) RequestDrawnWhole = true;
            steering.RequestShown(RequestDrawnWhole ? RequestParts : Math.Min(part + 1, RequestParts - 1), RequestParts);
        }

        /// <summary>
        /// The person pressed the part's last row at <paramref name="now"/>: the next part shows, or from
        /// the last the first again. It turns nothing until the part showing has been drawn and stood
        /// for <see cref="TurnGuard"/>, as lane C's review of Start building does, so a double press
        /// can't skip a part almost unseen; a request of one part, or a row from an earlier
        /// confirmation, turns nothing either.
        /// </summary>
        public void NextRequestPart(WorkspaceSteering steering, DateTimeOffset now)
        {
            if (steering.Armings != armingRead || RequestParts < 2) return;
            if (!(partDrawnAt is DateTimeOffset drawnAt) || now - drawnAt < TurnGuard) return;
            RequestPart = (RequestPart + 1) % RequestParts;
            partDrawnAt = null;
        }

        /// <summary>Forgets the request's parts, as when its confirmation is answered or dropped.</summary>
        public void ForgetRequest()
        {
            armingRead = -1;
            RequestText = null;
            RequestRows = 0;
            RequestPartRows = 1;
            RequestPart = 0;
            RequestDrawnWhole = false;
            drawn.Clear();
            partDrawnAt = null;
        }

        internal static int PartsOf(int rows, int perPart) => Math.Max(1, (Math.Max(0, rows) + Math.Max(1, perPart) - 1) / Math.Max(1, perPart));
    }
}
