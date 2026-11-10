#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>What the redesign shows of the open task's instruction now (DESIGN's FINISH-3, 2026-11-05).</summary>
    public enum RoomBarState
    {
        /// <summary>Nothing: a question waits, which the redesign's next unit draws.</summary>
        Hidden,

        /// <summary>The row at rest: Hold to Talk, the main action, and Type, floating with no field.</summary>
        Rest,

        /// <summary>Hold to Talk held: it reads Listening, with the microphone's real level.</summary>
        Listening,

        /// <summary>Let go: it reads Transcribing while the computer writes down what was said.</summary>
        Transcribing,

        /// <summary>A draft: the information panel holds its words (typed, the field with its caret), the column its actions.</summary>
        Draft,

        /// <summary>An instruction's confirmation: its words in the panel, what it asks in the sentence bar, the column its answers.</summary>
        Confirm,

        /// <summary>
        /// An approval (DESIGN 2.4): the request in mono in the panel, what it asks in the sentence bar, Approve (main) and Deny in
        /// the column; once either is pressed, its confirmation, Confirm behind its lock and Cancel; once sent, Sending with the
        /// column emptied until the runtime resolves it.
        /// </summary>
        Request,

        /// <summary>"Sending", then "Sent · Awaiting Start", words alone, sweeping: the instruction went and the run has not started.</summary>
        Sent,

        /// <summary>Why nothing can be written now, words alone, where no action can act.</summary>
        Words,
    }

    /// <summary>One floating action of the redesign: the file's own control at its place, with its words and glyph, main or not.</summary>
    public sealed class RoomAction
    {
        public RoomAction(KitControl control, bool main, string? words = null, bool locks = false, GlazeIcon? icon = null)
        {
            Control = control;
            Main = main;
            Words = words ?? control.Words;
            Locks = locks;
            Icon = icon ?? control.Icon;
        }

        /// <summary>The file's control a press raises, with its frame.</summary>
        public KitControl Control { get; }

        public KitPlace Place => Control.Place;

        public string Words { get; }

        /// <summary>Its glyph: the control's own, or the one its place takes where the file's row carries none.</summary>
        public GlazeIcon? Icon { get; }

        public bool Main { get; }

        /// <summary>
        /// Confirm's lock (DESIGN 1.4): it arrives a rest action with a ring round its chip that fills over
        /// <see cref="RoomBar.ConfirmLockSeconds"/>, taking no press, and only then is the main action, so the press that
        /// answered is never the one that confirms.
        /// </summary>
        public bool Locks { get; }
    }

    /// <summary>
    /// The redesign's instruction (DESIGN's FINISH-3, the owner's yes on 2026-11-05), a view of the open task's own file, never a
    /// second instruction: every action is a control the file's frame offers, with its own words, so sending, reading before
    /// Send and voice keep their rules. At rest two actions float where the bar stood, Hold to Talk (main) and Type; held, Hold
    /// to Talk reads Listening and Type goes. A typed draft and an instruction's confirmation are moments like any other: the
    /// information panel holds the words, its sentence bar the one thing to say, the column the actions. An action that cannot
    /// act is not drawn; where none can, the reason stands as words alone.
    /// </summary>
    public sealed class RoomBar
    {
        /// <summary>The words of the action that opens the instruction to type: the redesign's own.</summary>
        public const string Type = "Type";

        /// <summary>The words of Hold to Talk beside a heard draft: it records afresh and replaces the words. The redesign's own.</summary>
        public const string TalkAgain = "Talk Again";

        /// <summary>The words once the instruction went and the run has not started.</summary>
        public const string SentWords = KitMomentWords.SentTitle + KitMomentWords.Dot + KitMomentWords.WaitingToStart;

        /// <summary>How long Confirm stands locked once it could be pressed, its ring filling (DESIGN 1.4, the kit's own lock).</summary>
        public const float ConfirmLockSeconds = 0.7f;

        /// <summary>The lines of a request the panel shows at most (DESIGN 1.1): a longer one is read whole, part by part, under its confirmation.</summary>
        public const int RequestLines = 8;

        private static readonly RoomAction[] None = new RoomAction[0];

        private RoomBar(RoomBarState state, IReadOnlyList<RoomAction>? row = null, LineWords? words = null, LineWords? body = null, PageLine? part = null,
            bool caret = false, string? sentence = null, IReadOnlyList<RoomAction>? column = null, KitControl? field = null, bool mono = false,
            bool waits = false, int? rows = null)
        {
            Mono = mono;
            Waits = waits;
            Rows = rows;
            State = state;
            Row = row ?? None;
            Words = words ?? LineWords.None;
            Body = body ?? part?.Runs ?? LineWords.None;
            Part = part;
            Caret = caret;
            Sentence = sentence;
            Column = column ?? None;
            Field = field;
        }

        public RoomBarState State { get; }

        /// <summary>The row's actions at rest, listening or transcribing, the main one first.</summary>
        public IReadOnlyList<RoomAction> Row { get; }

        /// <summary>Words alone: Sent · Awaiting Start, or why nothing can be written.</summary>
        public LineWords Words { get; }

        /// <summary>The information panel's words, the person's: the draft so far, or the instruction under confirmation.</summary>
        public LineWords Body { get; }

        /// <summary>
        /// Under a confirmation, the file's line of what is read: the rows its part shows and the row it starts from, counted
        /// in the panel's own lines of <see cref="Body"/>; null otherwise.
        /// </summary>
        public PageLine? Part { get; }

        /// <summary>The panel is the field, with its caret after the words.</summary>
        public bool Caret { get; }

        /// <summary>The panel's one sentence, in its sentence bar; null for none.</summary>
        public string? Sentence { get; }

        /// <summary>The body is exact text, a request as the runtime reported it: it stands in mono, literal, at most <see cref="RequestLines"/> lines of it before its confirmation.</summary>
        public bool Mono { get; }

        /// <summary>The sentence is a state under way (Sending): it sweeps.</summary>
        public bool Waits { get; }

        /// <summary>
        /// The rows the file counted for what is read under a confirmation (<see cref="Body"/>, whole), where it has measured
        /// it: the panel draws the confirmation only where it wraps the same words to as many, so its parts are the file's.
        /// </summary>
        public int? Rows { get; }

        /// <summary>The column's actions beside the panel, the main one first.</summary>
        public IReadOnlyList<RoomAction> Column { get; }

        /// <summary>The control a press on the panel's field raises (the keyboard again), or null.</summary>
        public KitControl? Field { get; }

        /// <summary>Whether the pair (the panel and its column) shows.</summary>
        public bool Pair => State is RoomBarState.Draft or RoomBarState.Confirm or RoomBarState.Request;

        /// <summary>
        /// What the panel reads of a request or an instruction under its confirmation, whole, and whether it stands in mono:
        /// an instruction's words as the file's line holds them; an approval's request alone, in mono, without the tool's name
        /// and colon the file's line leads it with (the sentence named the tool before the answer was pressed), or, where no
        /// tool was named, the file's own line. The panel wraps these words at its own width and shows the rows the file's part
        /// names (<see cref="Part"/>), and the file counts the same words at the same width (<see cref="Rows"/>), so every part
        /// together is the whole, no word skipped or read twice.
        /// </summary>
        public static (LineWords Words, bool Mono) Reading(PageLine line, string? request, bool instruction)
        {
            if (instruction || request == null) return (line.Runs, false);
            var alone = line.Words == request || line.Words.EndsWith(": " + request, System.StringComparison.Ordinal);
            return (LineWords.Theirs(alone ? request : line.Words), true);
        }

        /// <summary>
        /// An approval (DESIGN 2.4), every action the file's own control. Asked: the request in the panel, what it asks in the
        /// sentence bar (or why only Deny stands, or what the file says of a lapse), Approve (main) and Deny. Confirming: the
        /// request as the file shows it part by part, the file's question in the sentence bar, Confirm behind its lock once the
        /// file offers it, the next part until then, and Cancel. Sent: Sending, sweeping, and no action until the runtime
        /// resolves the approval.
        /// </summary>
        private static RoomBar Asked(KitFileBinding binding, KitNeed need)
        {
            KitControl? Available(KitPlace place) => binding[place] is KitControl control && control.Available ? control : null;
            // With no tool named the panel is Halcyonic's one line, and a panel of one line has no sentence bar.
            var body = need.Request != null ? LineWords.Theirs(need.Request) : need.Asks;
            var mono = need.Request != null;
            var column = new List<RoomAction>();
            if (need.Confirming is KitConfirmation confirming)
            {
                if (need.Part != null) (body, mono) = Reading(need.Part, need.Request, instruction: false);
                var yes = Available(KitPlace.Yes);
                if (yes != null) column.Add(new RoomAction(yes, main: true, locks: true));
                foreach (var step in binding.Controls.Where(control => control.Place == KitPlace.Step && control.Available))
                    column.Add(new RoomAction(step, main: yes == null && column.Count == 0, icon: GlazeIcon.Next));
                if (Available(KitPlace.Cancel) is KitControl cancel) column.Add(new RoomAction(cancel, main: false));
                return new RoomBar(RoomBarState.Request, body: body, part: need.Part, sentence: confirming.Ask, column: column, mono: mono, rows: need.Rows);
            }
            if (need.Sending) return new RoomBar(RoomBarState.Request, body: body, sentence: WorkspaceText.Sent, mono: mono, waits: true);
            if (need.ApproveOffered && Available(KitPlace.Approve) is KitControl approve) column.Add(new RoomAction(approve, main: true));
            if (need.DenyOffered && Available(KitPlace.Deny) is KitControl deny) column.Add(new RoomAction(deny, main: false));
            // The sentence: what the file says now (a lapse, why nothing can be answered), else why only Deny stands, else what it asks.
            var onlyDeny = need.Notes.FirstOrDefault(note => note == WorkspaceText.TooLongToShow || note == WorkspaceText.CommandNotKnown || note == WorkspaceText.RequestNotKnown);
            var asks = need.Commands ? KitMomentWords.RunThisCommand : need.Asks.Text;
            var sentence = need.Says ?? onlyDeny ?? (need.Request != null ? asks : null);
            return new RoomBar(RoomBarState.Request, body: body, sentence: sentence, column: column, mono: mono);
        }

        /// <summary>The redesign's view of <paramref name="binding"/>, the file's frame, with voice as it stands and whether the run waits to start.</summary>
        public static RoomBar Of(KitFileBinding binding, VoiceStage voice, bool sentAwaitingStart)
        {
            var model = binding.Model;
            if (model.Need is KitNeed need) return Asked(binding, need);
            if (model.Ask != null) return new RoomBar(RoomBarState.Hidden);
            KitControl? Available(KitPlace place) => binding[place] is KitControl control && control.Available ? control : null;
            var talk = binding[KitPlace.Talk];
            var field = binding[KitPlace.Field];
            var composing = field != null && field.Action == FileScreens.EditInstruction;
            if (!composing && talk != null && voice == VoiceStage.Listening)
                return new RoomBar(RoomBarState.Listening, new[] { new RoomAction(talk, main: true, VoiceText.ListeningWords) });
            if (!composing && talk != null && voice == VoiceStage.WritingDown)
                return new RoomBar(RoomBarState.Transcribing, new[] { new RoomAction(talk, main: true, VoiceText.WritingDownWords) });
            if (model.Instruction is KitInstruction instruction)
            {
                // Confirm once the whole instruction was read, the next part until then, and Cancel always.
                var yes = Available(KitPlace.Yes);
                var steps = binding.Controls.Where(control => control.Place == KitPlace.Step && control.Available).ToList();
                var column = new List<RoomAction>();
                if (yes != null) column.Add(new RoomAction(yes, main: true));
                column.AddRange(steps.Select(step => new RoomAction(step, main: yes == null && column.Count == 0, icon: GlazeIcon.Next)));
                if (Available(KitPlace.Cancel) is KitControl cancel) column.Add(new RoomAction(cancel, main: false));
                return new RoomBar(RoomBarState.Confirm, part: instruction.Words, sentence: instruction.Asks, column: column, rows: instruction.Rows);
            }
            if (composing)
            {
                // Only the way out until a word exists, then Send arrives where it can send (DESIGN 2.1). Heard: the sentence bar asks
                // whether to send what was heard, or says what to read first, and Talk Again records afresh. Typed: no bar but a
                // lasting reason Send waits; the panel is the field, with its caret.
                var worded = model.Draft.Trim().Length > 0;
                var spoken = worded && model.DraftSpoken;
                var column = new List<RoomAction>();
                if (worded && voice == VoiceStage.Idle && Available(KitPlace.Send) is KitControl send) column.Add(new RoomAction(send, main: true));
                // Held, Talk Again stays in its place, lit, reading Listening then Transcribing, so the hand that holds it keeps it.
                if (spoken && Available(KitPlace.Talk) is KitControl again)
                    column.Add(voice switch
                    {
                        VoiceStage.Listening => new RoomAction(again, main: true, VoiceText.ListeningWords),
                        VoiceStage.WritingDown => new RoomAction(again, main: true, VoiceText.WritingDownWords),
                        _ => new RoomAction(again, main: false, TalkAgain),
                    });
                if (Available(KitPlace.Close) is KitControl close) column.Add(new RoomAction(close, main: false));
                if (worded && Available(KitPlace.Discard) is KitControl discard) column.Add(new RoomAction(discard, main: false));
                string? sentence = null;
                if (spoken) sentence = model.Sendable ? WorkspaceText.SendWordsAbove(heard: true) : model.WhyNotSend;
                else if (worded && !model.Sendable && model.WhyNotSend != FileScreens.InstructionNotRead) sentence = model.WhyNotSend;
                return new RoomBar(RoomBarState.Draft, body: worded ? LineWords.Theirs(model.Draft) : LineWords.None, caret: !spoken,
                    sentence: sentence, column: column, field: field!.Available ? field : null);
            }
            // The command's own state while it goes, then Sent · Awaiting Start until the run starts.
            if (model.Said.FirstOrDefault(line => line.Words == WorkspaceText.Sent) is PageLine sending)
                return new RoomBar(RoomBarState.Sent, words: LineWords.Ours(sending.Words));
            if (sentAwaitingStart) return new RoomBar(RoomBarState.Sent, words: LineWords.Ours(SentWords));
            var row = new List<RoomAction>();
            if (talk != null && talk.Available) row.Add(new RoomAction(talk, main: true));
            if (field != null && field.Available) row.Add(new RoomAction(field, main: row.Count == 0, Type));
            if (row.Count > 0) return new RoomBar(RoomBarState.Rest, row);
            var why = field?.Reason ?? talk?.Reason ?? model.WhyNotSend;
            return why != null ? new RoomBar(RoomBarState.Words, words: LineWords.Ours(why)) : new RoomBar(RoomBarState.Hidden);
        }
    }
}
