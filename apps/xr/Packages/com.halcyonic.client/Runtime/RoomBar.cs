#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>What the redesign shows of the open task's instruction now (DESIGN's FINISH-3, 2026-11-05).</summary>
    public enum RoomBarState
    {
        /// <summary>Nothing: an approval or a question waits, which the redesign's later units draw.</summary>
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

        /// <summary>"Sending", then "Sent · Awaiting Start", words alone, sweeping: the instruction went and the run has not started.</summary>
        Sent,

        /// <summary>Why nothing can be written now, words alone, where no action can act.</summary>
        Words,
    }

    /// <summary>One floating action of the redesign: the file's own control at its place, with its words and glyph, main or not.</summary>
    public sealed class RoomAction
    {
        public RoomAction(KitControl control, bool main, string? words = null)
        {
            Control = control;
            Main = main;
            Words = words ?? control.Words;
        }

        /// <summary>The file's control a press raises, with its frame.</summary>
        public KitControl Control { get; }

        public KitPlace Place => Control.Place;

        public string Words { get; }

        public GlazeIcon? Icon => Control.Icon;

        public bool Main { get; }
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

        private static readonly RoomAction[] None = new RoomAction[0];

        private RoomBar(RoomBarState state, IReadOnlyList<RoomAction>? row = null, LineWords? words = null, LineWords? body = null, PageLine? part = null,
            bool caret = false, string? sentence = null, IReadOnlyList<RoomAction>? column = null, KitControl? field = null)
        {
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

        /// <summary>The instruction's line as the file drew it while it is confirmed, its rows and the row its part starts from; null otherwise.</summary>
        public PageLine? Part { get; }

        /// <summary>The panel is the field, with its caret after the words.</summary>
        public bool Caret { get; }

        /// <summary>The panel's one sentence, in its sentence bar; null for none.</summary>
        public string? Sentence { get; }

        /// <summary>The column's actions beside the panel, the main one first.</summary>
        public IReadOnlyList<RoomAction> Column { get; }

        /// <summary>The control a press on the panel's field raises (the keyboard again), or null.</summary>
        public KitControl? Field { get; }

        /// <summary>Whether the pair (the panel and its column) shows.</summary>
        public bool Pair => State is RoomBarState.Draft or RoomBarState.Confirm;

        /// <summary>The redesign's view of <paramref name="binding"/>, the file's frame, with voice as it stands and whether the run waits to start.</summary>
        public static RoomBar Of(KitFileBinding binding, VoiceStage voice, bool sentAwaitingStart)
        {
            var model = binding.Model;
            if (model.Need != null || model.Ask != null) return new RoomBar(RoomBarState.Hidden);
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
                column.AddRange(steps.Select(step => new RoomAction(step, main: yes == null && column.Count == 0)));
                if (Available(KitPlace.Cancel) is KitControl cancel) column.Add(new RoomAction(cancel, main: false));
                return new RoomBar(RoomBarState.Confirm, part: instruction.Words, sentence: instruction.Asks, column: column);
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
