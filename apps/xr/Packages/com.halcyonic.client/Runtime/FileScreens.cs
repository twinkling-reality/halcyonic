#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// A task's file as a <see cref="MenuFrame"/> (ADR 0026): its title under its state pill, its four
    /// sections with an amber dot on Waiting while something waits, the chosen section's page in a few
    /// short lines, the page's one source line last, a side panel a line opened, and a footer of
    /// prompts. Changes and Checks show the brief answers and open the full ones beside them, each
    /// claim keeping its class. Nothing here sends anything: every press goes through
    /// <see cref="WorkspaceSteering"/>, and a confirmation's Yes stands in the free middle.
    /// </summary>
    public static partial class FileScreens
    {
        // What a press raises, besides MenuFrame.ChooseSection, Footer.Close, Footer.NextPage and
        // SidePanel.Close; the workspace's own actions keep WorkspaceScreens' ids.
        public const string Open = "open";
        public const string Refresh = WorkspaceScreens.Refresh;
        public const string Approve = WorkspaceScreens.Approve;
        public const string Deny = WorkspaceScreens.Deny;
        public const string Stop = WorkspaceScreens.Stop;
        public const string TellIt = WorkspaceScreens.TellIt;
        public const string SendAnswer = WorkspaceScreens.SendAnswer;
        /// <summary>Hold to talk on Activity, which speaks an instruction.</summary>
        public const string HoldToTalk = WorkspaceScreens.HoldToTalk;

        /// <summary>Hold to talk under the agent's question, which speaks an answer: its own id, so an answer is never taken for an instruction.</summary>
        public const string SpeakAnswer = WorkspaceScreens.SpeakAnswer;
        public const string Yes = WorkspaceScreens.Yes;
        public const string Cancel = WorkspaceScreens.Cancel;
        public const string Preset = WorkspaceScreens.Preset;
        public const string Choose = WorkspaceScreens.Choose;
        public const string TypeAnswer = WorkspaceScreens.TypeAnswer;

        /// <summary>The rows the request shows on Waiting before Approve or Deny shows it whole.</summary>
        public const int RequestRows = 3;

        /// <summary>The keys of the lines that open a side panel.</summary>
        public const string WhatChangedKey = "what-changed";
        public const string WhyChangedKey = "why-changed";
        public const string HowBuiltKey = "how-built";
        public const string ChecksKey = "checks";

        /// <summary>The section a file opens on: Waiting while something waits for the person, else Activity.</summary>
        public static FileSection Opening(WorkspacePresentation workspace) =>
            WorkspaceText.SomethingWaits(workspace) ? FileSection.Waiting : FileSection.Activity;

        /// <summary>A section's words on the file's row of sections.</summary>
        public static string Word(FileSection section) => section switch
        {
            FileSection.Waiting => "Waiting",
            FileSection.Activity => "Activity",
            FileSection.Changes => "Changes",
            FileSection.Checks => "Checks",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unhandled section."),
        };

        /// <summary>The key a section raises when chosen.</summary>
        public static string Key(FileSection section) => section switch
        {
            FileSection.Waiting => "waiting",
            FileSection.Activity => "activity",
            FileSection.Changes => "changes",
            FileSection.Checks => "checks",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Unhandled section."),
        };

        /// <summary>The section a key names, or null.</summary>
        public static FileSection? SectionOf(string? key)
        {
            foreach (FileSection section in Enum.GetValues(typeof(FileSection)))
            {
                if (Key(section) == key) return section;
            }
            return null;
        }

        /// <summary>
        /// The file as it stands: the section <paramref name="screen"/> shows, its lines fitted to
        /// <paramref name="room"/>'s rows a page, and the side panel its chosen line opened. Waiting
        /// reads only the work's own state, never an answer still being read.
        /// </summary>
        public static MenuFrame Screen(WorkspacePresentation workspace, WorkspaceSteering steering, FileScreen screen, AnswerRoom room)
        {
            // A confirmation that no longer holds is dropped before anything is drawn: too late, no
            // longer offered, already answered, or a request that now reads differently.
            if (steering.Refresh(workspace) is string lapse) screen.Notice = lapse;
            if (steering.ToRead(workspace) == null) screen.ForgetRequest();
            // Whatever is armed asks on the section showing, whichever it is, so a press never arms
            // something the page does not show, and changing section never hides it.
            var page = steering.Armed is WorkspaceAction armed ? Confirmation(workspace, steering, screen, armed) : screen.Section switch
            {
                FileSection.Waiting => Waiting(workspace, steering, screen, room),
                FileSection.Activity => Activity(workspace, steering, screen, room),
                FileSection.Changes => Changes(screen, room),
                _ => Checks(screen, room),
            };
            // Only the question's own page counts toward reading it when drawn, never another page on
            // Waiting meanwhile whose words happen to read the same, as an approval's request.
            screen.QuestionPage = page.OfQuestion;
            var lines = page.Lines.ToList();
            if (screen.Notice != null) lines.Add(new PageLine(screen.Notice, tone: LineTone.Secondary, rows: 2));
            return new MenuFrame(
                workspace.Character.Title,
                page.Footer,
                subjectIsData: true,
                pill: StateLanguage.BadgeOf(workspace.Character),
                sections: Sections(workspace, screen.Section),
                lines: lines,
                source: page.Source,
                sourceIsData: page.SourceIsData,
                side: page.Side);
        }

        /// <summary>The four sections, the one showing chosen, Waiting's amber dot while something waits.</summary>
        private static IReadOnlyList<FrameSection> Sections(WorkspacePresentation workspace, FileSection showing)
        {
            var waits = WorkspaceText.SomethingWaits(workspace);
            return ((FileSection[])Enum.GetValues(typeof(FileSection)))
                .Select(section => new FrameSection(Key(section), Word(section), section == showing, waits && section == FileSection.Waiting))
                .ToList();
        }

        /// <summary>A section's page: its lines, its source line, the side panel a line opened and its footer.</summary>
        private sealed class Page
        {
            public Page(IReadOnlyList<PageLine> lines, string? source, Footer footer, SidePanel? side = null, bool sourceIsData = false, bool ofQuestion = false)
            {
                OfQuestion = ofQuestion;
                SourceIsData = sourceIsData;
                Lines = lines;
                Source = source;
                Footer = footer;
                Side = side;
            }

            public IReadOnlyList<PageLine> Lines { get; }

            public string? Source { get; }

            /// <summary>The source line is an answer's provenance, holding a source's version or why it could not be read: text from outside.</summary>
            public bool SourceIsData { get; }

            public Footer Footer { get; }

            public SidePanel? Side { get; }

            /// <summary>The page is the agent's question's own, built from the person's place in it (<see cref="FileQuestion"/>).</summary>
            public bool OfQuestion { get; }
        }

        // The footer's prompts.
        private static Prompt CloseFile => new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);

        private static Prompt Action(WorkspaceAction action, string id, bool main = false) =>
            new Prompt(id, WorkspaceText.Label(action), WorkspaceText.IconOf(action), main: main);

        /// <summary>Hold to talk; where the voice stands, listening or writing down, the plane draws on it from the menu's one voice (<see cref="Prompt.Voiced"/>).</summary>
        private static Prompt Talk => new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true);

        private static Prompt CancelChoice => new Prompt(Cancel, EntryText.Cancel, GlazeIcon.Close);

        private static Prompt Next(int page, int pages) => new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages), GlazeIcon.Next, PromptKind.NextPage);

        /// <summary>
        /// The source line of the work's own state. An app's name arrives only as data, never in
        /// Halcyonic's sentence (WORDS.md), so it names the agent.
        /// </summary>
        public const string AgentSource = "As the agent reported it";

        /// <summary>
        /// Activity: what it is doing, what this headset sent last, and what the work did, the newest
        /// last as in a log, as much as fits; Tell it as the main action and Stop beside Close. While
        /// Tell it waits for words, its page says how to give them, or offers the instructions in rows
        /// where there is no keyboard; while Stop or an instruction waits for its confirmation, it asks.
        /// </summary>
        private static Page Activity(WorkspacePresentation workspace, WorkspaceSteering steering, FileScreen screen, AnswerRoom room)
        {
            var source = AgentSource;
            var normal = ActivityFooter(workspace, screen);
            if (steering.Typing)
            {
                return new Page(new[] { new PageLine(WorkspaceText.TypingPrompt) }, source, new Footer(CloseFile, rare: CancelChoice));
            }
            if (screen.Presets is { } presets)
            {
                // Rows only choose, showing the very words Tell it sends; Tell it, the main action, sends
                // them, and nothing is sent by the press that chooses.
                var recorded = !ReferenceEquals(presets, WorkspaceText.PresetInstructions);
                var rows = presets.Select((preset, index) => new PageLine(WorkspaceText.OneLine(preset.Text), wordsAreData: recorded, action: Preset,
                    key: index.ToString(CultureInfo.InvariantCulture), choice: true, chosen: screen.ChosenPreset == index, rows: 2)).ToList();
                var chosen = screen.ChosenPreset != null;
                var tell = new Prompt(TellIt, WorkspaceText.Label(WorkspaceAction.Instruct), WorkspaceText.IconOf(WorkspaceAction.Instruct), main: true,
                    available: chosen, reason: chosen ? null : ChooseAnInstruction);
                return new Page(rows, source, new Footer(CloseFile, rare: CancelChoice, farRight: tell));
            }

            var lines = new List<PageLine>();
            var waiting = WorkspaceText.SomethingWaits(workspace);
            var tone = waiting ? LineTone.Waiting : workspace.Character.AttentionNotes.Count > 0 ? LineTone.Problem : LineTone.Primary;
            foreach (var line in Doing(workspace)) lines.Add(new PageLine(line, wordsAreData: !waiting, tone: tone, rows: line == NothingWaits ? 1 : 2));
            if (normal.Reason == null && !normal.All.Any(each => each.Prompt.Kind == PromptKind.Action) && WorkspaceText.WhyNoActions(workspace) is string why)
            {
                lines.Add(new PageLine(why, tone: LineTone.Secondary, rows: 2));
            }
            if (workspace.Commands.FirstOrDefault() is { } sent) lines.Add(new PageLine(sent.Text, wordsAreData: true, tone: LineTone.Secondary, waits: sent.Waits));
            var used = lines.Sum(line => line.Rows);
            var activity = workspace.Activity;
            var fits = Math.Max(0, WithSource(room).Rows - used);
            if (activity.Count == 0 && screen.ActivityNote.Length > 0 && fits > 0)
            {
                lines.Add(new PageLine(screen.ActivityNote.TrimStart(' ', '·'), wordsAreData: true, tone: LineTone.Secondary));
            }
            for (var index = Math.Max(0, activity.Count - fits); index < activity.Count; index++)
            {
                var entry = activity[index];
                lines.Add(new PageLine(
                    entry.Reported ? "“" + WorkspaceText.OneLine(entry.Text) + "”" : WorkspaceText.OneLine(entry.Text),
                    wordsAreData: true,
                    fact: Clock(entry.OccurredAt, screen.Zone),
                    tone: LineTone.Secondary,
                    chip: entry.Reported ? "Agent says" : null,
                    claim: entry.Reported));
            }
            return new Page(lines, source, normal);
        }

        /// <summary>
        /// What it is doing, in a line or two: what waits for the person names the section it is in, and
        /// while nothing does, the latest of the work is the log's last line, so it is said only without one.
        /// </summary>
        private static IReadOnlyList<string> Doing(WorkspacePresentation workspace)
        {
            if (workspace.ApprovalToAnswer != null) return new[] { "It wants your approval. See it under " + Word(FileSection.Waiting) + "." };
            if (workspace.QuestionToAnswer != null) return new[] { "It asks you a question. See it under " + Word(FileSection.Waiting) + "." };
            var answer = WorkspaceText.Answer(workspace);
            return answer.Count == 2 && answer[0] == NothingWaits && workspace.Activity.Count > 0 && !workspace.Character.Stale ? new[] { NothingWaits } : answer;
        }

        /// <summary>Activity's footer: Close, Stop beside it, Hold to talk beside Tell it, and Tell it as the main action.</summary>
        private static Footer ActivityFooter(WorkspacePresentation workspace, FileScreen screen)
        {
            var actions = workspace.Actions;
            // While the agent asks for something secret, nothing here invites typing or saying it
            // (ADR 0022): it would be journaled. Stop is the way on.
            var secret = workspace.QuestionToAnswer?.Prompts.Any(prompt => prompt.Secret) == true;
            var tell = !secret && actions.Contains(WorkspaceAction.Instruct) ? Action(WorkspaceAction.Instruct, TellIt, main: true) : null;
            return new Footer(
                CloseFile,
                rare: actions.Contains(WorkspaceAction.Interrupt) ? Action(WorkspaceAction.Interrupt, Stop) : null,
                secondary: tell != null && screen.Speak ? Talk : null,
                farRight: tell);
        }

        /// <summary>A time as the activity shows it, in the person's zone; null when it does not read.</summary>
        private static string? Clock(string occurredAt, TimeZoneInfo zone) =>
            DateTimeOffset.TryParse(occurredAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? TimeZoneInfo.ConvertTime(at, zone).ToString("HH:mm", CultureInfo.InvariantCulture)
                : null;

        /// <summary>
        /// Changes: what changed, then why, then how it was built, each its brief lines, the first of
        /// each opening its full answer beside the page. The source line names where they come from.
        /// </summary>
        private static Page Changes(FileScreen screen, AnswerRoom room)
        {
            var groups = new List<(string Key, string Subject, FileAnswer Answer)>();
            if (screen.WhatChanged != null) groups.Add((WhatChangedKey, WorkspaceText.PromptLabel(UnderstandPrompt.WhatChanged), screen.WhatChanged));
            if (screen.WhyChanged != null) groups.Add((WhyChangedKey, WorkspaceText.PromptLabel(UnderstandPrompt.WhyChanged), screen.WhyChanged));
            if (screen.HowBuilt != null) groups.Add((HowBuiltKey, WorkspaceText.PromptLabel(UnderstandPrompt.HowBuilt), screen.HowBuilt));
            return Answered(screen, room, groups, StillReading(FileSection.Changes), null);
        }

        /// <summary>Checks: what was checked, its brief lines, the first opening the full answer beside the page; Refresh beside Close.</summary>
        private static Page Checks(FileScreen screen, AnswerRoom room)
        {
            var groups = new List<(string Key, string Subject, FileAnswer Answer)>();
            if (screen.Checked != null) groups.Add((ChecksKey, WorkspaceText.WhatWasChecked, screen.Checked));
            return Answered(screen, room, groups, StillReading(FileSection.Checks), new Prompt(Refresh, WorkspaceText.Refresh, GlazeIcon.Refresh));
        }

        /// <summary>Why Tell it waits while the instructions offered show and none is chosen.</summary>
        public const string ChooseAnInstruction = "Choose what to tell it first.";

        /// <summary>What a section says while its answer is still being read: plainly so, never an empty page.</summary>
        public static string StillReading(FileSection section) => section switch
        {
            FileSection.Changes => "Still reading what changed. While the work runs, this can take a few seconds.",
            FileSection.Checks => "Still reading what was checked. While the work runs, this can take a few seconds.",
            _ => throw new ArgumentOutOfRangeException(nameof(section), section, "Only Changes and Checks read an answer."),
        };

        /// <summary>
        /// A page of answers: each group's brief lines, its first line opening its full answer, paged
        /// by <paramref name="room"/>'s rows with Next page; the side panel of the chosen line; the
        /// first answer's provenance as the page's one source line. An answer with no lines says why
        /// in its provenance, which becomes the page's line, as a source that could not be read names no
        /// source; none read yet says it is still being read.
        /// </summary>
        private static Page Answered(FileScreen screen, AnswerRoom room, IReadOnlyList<(string Key, string Subject, FileAnswer Answer)> groups,
            string reading, Prompt? rare)
        {
            var footer = new Footer(CloseFile, rare: rare);
            if (groups.Count == 0)
            {
                screen.Pages = 1;
                screen.Page = 0;
                screen.SideParts = 0;
                return new Page(new[] { new PageLine(reading, tone: LineTone.Secondary, rows: 2) }, null, footer);
            }
            var first = groups[0].Answer.Brief;
            var lines = new List<PageLine>();
            string? source = first.Provenance;
            foreach (var (key, _, answer) in groups)
            {
                var content = answer.Brief.Lines.Where(line => !line.Source).ToList();
                if (content.Count == 0)
                {
                    if (ReferenceEquals(answer.Brief, first)) source = null;
                    // Why there is no brief answer; where the full one still holds something, as the evaluation
                    // source's measurement beside an understanding that couldn't be read, it opens it.
                    var opens = answer.Full.Lines.Count > 0;
                    lines.Add(new PageLine(IntelligenceText.Plain(answer.Brief.Provenance), wordsAreData: true, tone: Tone(answer.Brief.ProvenanceTone), rows: 3,
                        action: opens ? Open : null, key: opens ? key : null, opens: opens, chosen: opens && key == screen.Chosen));
                    continue;
                }
                for (var index = 0; index < content.Count; index++)
                {
                    lines.Add(index == 0 ? Line(content[index], room, key, key == screen.Chosen) : Line(content[index], room));
                }
            }

            var pages = Paged(lines, source == null ? room.Rows : WithSource(room).Rows);
            screen.Pages = pages.Count;
            screen.Page = Math.Min(screen.Page, pages.Count - 1);
            var shown = pages[screen.Page];
            // A side panel only from a line on the page showing.
            var opened = shown.FirstOrDefault(line => line.Opens && line.Chosen);
            SidePanel? side = null;
            if (opened != null)
            {
                var (_, subject, answer) = groups.First(group => group.Key == opened.Key);
                side = Side(subject, answer.Full, screen, room);
            }
            else screen.SideParts = 0;
            if (side?.Parts is (int part, int parts) && parts > 1) footer = footer.WithNext(Next(part, parts));
            else if (pages.Count > 1) footer = footer.WithNext(Next(screen.Page, pages.Count));
            return new Page(shown, source, footer, side, sourceIsData: true);
        }

        /// <summary>
        /// The room a page's lines have beside its source line, which takes one of the page's rows (ADR
        /// 0026): with a side panel open, a page of the room's rows and its source reached past the
        /// field of view.
        /// </summary>
        internal static AnswerRoom WithSource(AnswerRoom room) => new AnswerRoom(Math.Max(1, room.Rows - 1), room.RowsOf);

        /// <summary>Lines split into pages of at most <paramref name="rows"/> rows, a line never split; a line taller than a page has one of its own.</summary>
        private static List<List<PageLine>> Paged(IReadOnlyList<PageLine> lines, int rows)
        {
            var pages = new List<List<PageLine>> { new List<PageLine>() };
            var used = 0;
            foreach (var line in lines)
            {
                if (used > 0 && used + line.Rows > rows)
                {
                    pages.Add(new List<PageLine>());
                    used = 0;
                }
                pages[pages.Count - 1].Add(line);
                used += line.Rows;
            }
            return pages;
        }

        /// <summary>
        /// The full answer beside the page, a part at a time where it does not fit: its lines say
        /// something and take no press, its source line last.
        /// </summary>
        private static SidePanel Side(string subject, SectionPresentation full, FileScreen screen, AnswerRoom room)
        {
            var parts = AnswerPages.Split(full, WithSource(room));
            screen.SideParts = parts.Count;
            screen.SidePart = Math.Min(screen.SidePart, parts.Count - 1);
            var part = parts[screen.SidePart];
            var lines = part.Lines.Select(line => Line(line, room)).ToList();
            return new SidePanel(subject, lines: lines, source: part.Provenance, parts: parts.Count > 1 ? (screen.SidePart, parts.Count) : ((int, int)?)null,
                sourceIsData: true);
        }

        /// <summary>
        /// An answer's line as a page line: its words without its chip, which is drawn beside them, the
        /// chip unless observed, measured or Halcyonic's own, quoted words leaning, and a source line
        /// inside an answer, as Seorak's part's, in the secondary colour. With <paramref name="opens"/>,
        /// pressing it opens the full answer beside the page.
        /// </summary>
        internal static PageLine Line(SectionLine line, AnswerRoom room, string? opens = null, bool chosen = false) => new PageLine(
            line.Words,
            // A source line inside an answer carries the source's own words, its version or why it could
            // not be read, so it is data, as every class but Halcyonic's own is.
            wordsAreData: line.Source || line.Evidence != Evidence.Halcyonic,
            icon: line.File is FileKind kind ? Icon(kind) : (GlazeIcon?)null,
            tone: line.Source || line.Detail ? LineTone.Secondary : Tone(line.Tone),
            chip: line.Chip,
            claim: line.Tone == SectionTone.Claim,
            action: opens == null ? null : Open,
            key: opens,
            opens: opens != null,
            chosen: chosen && opens != null,
            rows: room.RowsOf(line));

        /// <summary>A changed file's generic glyph by its kind (ADR 0026), never a language's or a product's mark.</summary>
        public static GlazeIcon Icon(FileKind kind) => kind switch
        {
            FileKind.Code => GlazeIcon.CodeFile,
            FileKind.Database => GlazeIcon.DatabaseFile,
            FileKind.Data => GlazeIcon.DataFile,
            FileKind.Image => GlazeIcon.ImageFile,
            FileKind.Script => GlazeIcon.ScriptFile,
            FileKind.Package => GlazeIcon.PackageFile,
            FileKind.Folder => GlazeIcon.Folder,
            _ => GlazeIcon.TextFile,
        };

        /// <summary>An answer's tone on the page: amber only for what waits for the person.</summary>
        internal static LineTone Tone(SectionTone tone) => tone switch
        {
            SectionTone.Secondary => LineTone.Secondary,
            SectionTone.Attention => LineTone.Waiting,
            SectionTone.Problem => LineTone.Problem,
            SectionTone.Good => LineTone.Good,
            _ => LineTone.Primary,
        };

        /// <summary>A confirmation's Yes, named by what it sends.</summary>
        private static Prompt YesFor(WorkspaceAction armed) => new Prompt(Yes, WorkspaceText.ConfirmLabel(armed), WorkspaceText.IconOf(armed), PromptKind.Yes);

        private static Prompt CancelConfirm => new Prompt(Cancel, EntryText.Cancel, GlazeIcon.Close, PromptKind.Cancel);
    }
}
