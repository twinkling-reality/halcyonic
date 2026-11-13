#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>What the redesign shows of the open task's instruction now (DESIGN's FINISH-3, 2026-11-05).</summary>
    public enum RoomBarState
    {
        /// <summary>Nothing: no action can act and there is nothing to say.</summary>
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

        /// <summary>
        /// A question (DESIGN 2.9): its prompt on the band of the agent's words, each answer a floating action with its mark
        /// in the information panel's place, then the file's way on; with several prompts the panel holds which question it
        /// is and the choices stand in the column; the answers' confirmation and Sending as an approval's.
        /// </summary>
        Question,

        /// <summary>
        /// The instructions offered to choose from where there is no keyboard, or a recorded run plays its own (DESIGN 2.1):
        /// each a floating action in the panel's place, the one chosen marked, then the file's Review Instruction once one is
        /// chosen, and Cancel.
        /// </summary>
        Choose,

        /// <summary>
        /// A frame with no moment of its own, by the general rule (<see cref="RoomBar.OfFrame"/>): Halcyonic's lines in the
        /// panel, the frame's actions in the column, a line of outside words on the band.
        /// </summary>
        Page,

        /// <summary>"Sending", then "Sent · Awaiting Start", words alone, sweeping: the instruction went and the run has not started.</summary>
        Sent,

        /// <summary>Why nothing can be written now, words alone, where no action can act.</summary>
        Words,
    }

    /// <summary>One floating action of the redesign: the file's own control at its place, with its words and glyph, main or not.</summary>
    public sealed class RoomAction
    {
        public RoomAction(KitControl control, bool main, string? words = null, bool locks = false, GlazeIcon? icon = null, AnswerMark mark = AnswerMark.None,
            bool chosen = false, string? chip = null, string? detail = null)
        {
            // The control's own second part where the reader gives none (an answer's description, a pager's count; JUDGE's dot list).
            detail ??= control.Under?.Text;
            Detail = string.IsNullOrWhiteSpace(detail) ? null : detail!.Trim();
            Chip = string.IsNullOrWhiteSpace(chip) ? null : chip;
            Control = control;
            Main = main;
            Words = words ?? control.Words;
            Locks = locks;
            Icon = icon ?? control.Icon;
            Mark = mark;
            Chosen = chosen;
        }

        /// <summary>
        /// An answer's mark, standing where the chip would (DESIGN 1.4): a circle for one of several, a squircle box where any
        /// number may be chosen; none for an action that is no answer.
        /// </summary>
        public AnswerMark Mark { get; }

        /// <summary>
        /// A quiet chip after the words, a fact of the action's own (the addendum's A17: the agent's level after its name),
        /// never joined to the words on a dot. Null for none.
        /// </summary>
        public string? Chip { get; }

        /// <summary>The answer is chosen: its mark is the light disc with a check.</summary>
        public bool Chosen { get; }

        /// <summary>
        /// The action's second part, given apart from its words and drawn on its own line under them: an answer's description,
        /// a pager's count ("2 of 7"). Never joined to the words on a dot (the owner, 2026-11-11). Null for none.
        /// </summary>
        public string? Detail { get; }

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
    public sealed partial class RoomBar
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

        /// <summary>The choices a page of a question holds as actions of their own (DESIGN 2.9: more than four page by the file's own row).</summary>
        public const int ChoicesAPage = 4;

        private static readonly RoomAction[] None = new RoomAction[0];

        private RoomBar(RoomBarState state, IReadOnlyList<RoomAction>? row = null, LineWords? words = null, LineWords? body = null, PageLine? part = null,
            bool caret = false, string? sentence = null, IReadOnlyList<RoomAction>? column = null, KitControl? field = null, bool mono = false,
            bool waits = false, int? rows = null, string? band = null, bool wide = false, IReadOnlyList<LineWords>? lines = null)
        {
            Lines = lines;
            Mono = mono;
            Waits = waits;
            Rows = rows;
            Band = band;
            Wide = wide;
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
        public IReadOnlyList<RoomAction> Row { get; private set; }

        /// <summary>Words alone: Sent · Awaiting Start, or why nothing can be written.</summary>
        public LineWords Words { get; }

        /// <summary>The information panel's words, the person's: the draft so far, or the instruction under confirmation.</summary>
        public LineWords Body { get; }

        /// <summary>
        /// Under a confirmation, or a draft the file cut in parts, the file's line of what is read: the rows its part shows and
        /// the row it starts from, counted in the panel's own lines of <see cref="Body"/>; null otherwise.
        /// </summary>
        public PageLine? Part { get; }

        /// <summary>The panel is the field, with its caret after the words.</summary>
        public bool Caret { get; }

        /// <summary>The panel's one sentence, in its sentence bar; null for none.</summary>
        public string? Sentence { get; }

        /// <summary>The body is exact text, a request as the runtime reported it: it stands in mono, literal, at most <see cref="RequestLines"/> lines of it before its confirmation.</summary>
        public bool Mono { get; }

        /// <summary>A state under way (Sending): the sentence sweeps, or the panel's one line where it has no sentence.</summary>
        public bool Waits { get; }

        /// <summary>
        /// The agent's own words for the band low in the view (DESIGN 1.6): the prompt of the question the page is about, as
        /// the agent wrote it; null where nothing of the agent's stands. Never in the panel, which holds the computer's own.
        /// </summary>
        public string? Band { get; }

        /// <summary>
        /// The panel's words as the file's own lines, where it holds more than one (the answers about to go, why a question
        /// can't be answered): each stands on a line of its own, so none ends in a separator; null where <see cref="Body"/> is one line.
        /// </summary>
        public IReadOnlyList<LineWords>? Lines { get; }

        /// <summary>No panel: with nothing from the computer to say, the choices take its place, as wide as it (DESIGN 0, rule 3).</summary>
        public bool Wide { get; }

        /// <summary>
        /// The rows the file counted for what is read under a confirmation (<see cref="Body"/>, whole), where it has measured
        /// it: the panel draws the confirmation only where it wraps the same words to as many, so its parts are the file's.
        /// </summary>
        public int? Rows { get; }

        /// <summary>The column's actions beside the panel, the main one first; at work, Stop alone beside the panel's empty place.</summary>
        public IReadOnlyList<RoomAction> Column { get; private set; }

        /// <summary>
        /// At work (DESIGN 2.3): the agent or its checks are working. No panel stands: the working field holds its place,
        /// Stop stands alone in the column, a rest action, and the row stands only where the file still offers an instruction.
        /// </summary>
        public bool Working { get; private set; }

        /// <summary>At work, the step the runtime reports under way, for the agent's cursor; null where it reports none.</summary>
        public RoomWorkStep? Step { get; private set; }

        /// <summary>
        /// The result at rest (DESIGN 2.5 to 2.8): the pair stands over the row, holding what the computer knows of the task,
        /// its actions the result's own (<see cref="RoomResult.Actions"/>) beside the file's. The agent's newest message
        /// stands on the band, and is not something to read before the row takes a press.
        /// </summary>
        public bool Result { get; private set; }

        /// <summary>Whether the frame counts as read only once the band shows its last part: so for a question; never for a result's message.</summary>
        public bool BandIsRead => Band != null && !Result;

        /// <summary>The control a press on the panel's field raises (the keyboard again), or null.</summary>
        public KitControl? Field { get; }

        /// <summary>Whether the pair (the panel and its column) shows.</summary>
        public bool Pair => Result || State is RoomBarState.Draft or RoomBarState.Confirm or RoomBarState.Request or RoomBarState.Question or RoomBarState.Choose or RoomBarState.Page;

        /// <summary>The words of the action that opens the offered instructions where no keyboard is offered: the product's own.</summary>
        public const string ChooseInstruction = FileScreens.ChooseAnInstruction;

        /// <summary>
        /// A question (DESIGN 2.9), every action the file's own control, in the file's order: each answer with its mark, the
        /// chosen one lit; the rows that take the person on within the page (Write Answer, More Choices, an answer given
        /// back to its question); then the way on, the main action, where the file offers it (Next Question, Review Answers,
        /// Send Answer), and the way back. The prompt stands on the band. With one prompt and nothing else to say there is
        /// no panel and the choices take its place; with several the panel holds which question it is. The answers'
        /// confirmation holds what goes in the panel, the file's question in its sentence bar, Confirm behind its lock and
        /// Cancel; sent, the panel reads Sending and no action stands until the runtime takes the question away.
        /// </summary>
        private static RoomBar Asking(KitFileBinding binding, KitAsk ask, VoiceStage voice)
        {
            KitControl? Available(KitPlace place) => binding[place] is KitControl control && control.Available ? control : null;
            if (ask.Writing is string written)
            {
                // Writing an answer is the typed draft's moment (DESIGN 2.1): the panel is the field, the person's words and the
                // caret; Use Answer arrives where the file offers it, the main action; Hold to Talk where it offers its held one;
                // the file's parts of a long answer; and Cancel. A lasting reason Use Answer waits stands in the sentence.
                var writing = new List<RoomAction>();
                var use = Available(KitPlace.Forward);
                if (use != null) writing.Add(new RoomAction(use, main: true));
                if (Available(KitPlace.Talk) is KitControl speak)
                    writing.Add(voice switch
                    {
                        VoiceStage.Listening => new RoomAction(speak, main: true, VoiceText.ListeningWords),
                        VoiceStage.WritingDown => new RoomAction(speak, main: true, VoiceText.WritingDownWords),
                        _ => new RoomAction(speak, main: false),
                    });
                foreach (var step in binding.Controls.Where(control => control.Place == KitPlace.Step && control.Available))
                    writing.Add(new RoomAction(step, main: false, icon: step.Icon ?? GlazeIcon.Next));
                if (Available(KitPlace.Back) is KitControl before) writing.Add(new RoomAction(before, main: false));
                if (Available(KitPlace.Close) is KitControl leave) writing.Add(new RoomAction(leave, main: false));
                var waits = written.Length > 0 && binding[KitPlace.Forward] is { Available: false, Reason: string reason } ? reason : null;
                return new RoomBar(RoomBarState.Question, body: written.Length > 0 ? LineWords.Theirs(written) : LineWords.None, caret: true, sentence: waits,
                    column: writing, field: Available(KitPlace.Field), band: ask.Prompt);
            }
            var column = new List<RoomAction>();
            foreach (var control in binding.Controls.Where(control => control.Available && (control.Place == KitPlace.Choice || control.Place == KitPlace.Step)))
            {
                if (control.Place == KitPlace.Choice)
                    column.Add(new RoomAction(control, main: false, mark: control.Mark == AnswerMark.None ? AnswerMark.Single : control.Mark, chosen: control.Chosen));
                // An answer given, on the page of the person's answers: it reads as chosen, and its press goes back to its question.
                else if (control.Action == FileScreens.GoToQuestion) column.Add(new RoomAction(control, main: false, mark: AnswerMark.Single, chosen: true));
                else column.Add(new RoomAction(control, main: false, icon: control.Icon ?? GlazeIcon.Next));
            }
            if (Available(KitPlace.Yes) is KitControl yes) column.Add(new RoomAction(yes, main: true, locks: true));
            if (Available(KitPlace.Forward) is KitControl forward) column.Add(new RoomAction(forward, main: true));
            if (Available(KitPlace.Back) is KitControl back) column.Add(new RoomAction(back, main: false));
            if (Available(KitPlace.Cancel) is KitControl cancel) column.Add(new RoomAction(cancel, main: false));
            if (Available(KitPlace.Stop) is KitControl stop) column.Add(new RoomAction(stop, main: false));
            // The panel: Sending; what goes, under its confirmation; why it can't be answered; or, with several prompts, which this is.
            var sending = binding[KitPlace.Sending] != null;
            var body = LineWords.None;
            string? sentence = null;
            IReadOnlyList<LineWords>? lines = null;
            if (sending) body = LineWords.Ours(WorkspaceText.Sent);
            else if (ask.Body.Count > 0)
            {
                body = ask.Body[0].Runs;
                for (var index = 1; index < ask.Body.Count; index++) body = body + LineWords.Ours(KitMomentWords.Dot) + ask.Body[index].Runs;
                if (ask.Body.Count > 1) lines = ask.Body.Select(line => line.Runs).ToList();
                // Under what the panel holds: what the confirmation asks, or the page's own caption (answers that read alike).
                sentence = ask.Asks;
            }
            else if (ask.Confirming && ask.Asks != null) body = LineWords.Ours(ask.Asks);
            else if (ask.Several && ask.Caption != null) body = LineWords.Ours(ask.Caption);
            // Where the way on waits and a panel stands, its sentence bar says why, in the file's words.
            if (!sending && !ask.Confirming && !body.IsEmpty && binding[KitPlace.Forward] is { Available: false, Reason: string why }) sentence = why;
            return new RoomBar(RoomBarState.Question, body: body, sentence: sentence, column: sending ? None : column, waits: sending, band: ask.Prompt, wide: body.IsEmpty,
                lines: lines);
        }

        /// <summary>
        /// What the panel reads of a request or an instruction under its confirmation, whole, and whether it stands in mono:
        /// an instruction's words as the file's line holds them; a command's request alone, in mono, without the tool's name
        /// and colon the file's line leads it with (Run Command? named it before the answer was pressed); for a tool that is
        /// <paramref name="commands"/> false, the file's own line in mono, the tool's name leading it, so the person never
        /// confirms a request with no tool on screen (the lead, 2026-11-10); where no tool was named, the file's own line.
        /// The panel wraps these words at its own width and shows the rows the file's part names (<see cref="Part"/>), and
        /// the file counts the same words at the same width (<see cref="Rows"/>), so every part together is the whole, no
        /// word skipped or read twice.
        /// </summary>
        public static (LineWords Words, bool Mono) Reading(PageLine line, string? request, bool instruction, bool commands = true)
        {
            if (instruction || request == null) return (line.Runs, false);
            var alone = commands && (line.Words == request || line.Words.EndsWith(": " + request, System.StringComparison.Ordinal));
            return (LineWords.Theirs(alone ? request : line.Words), true);
        }

        /// <summary>What a confirmation's <paramref name="line"/> reads in the panel for <paramref name="workspace"/>'s approval or its armed instruction (<see cref="Reading(PageLine, string?, bool, bool)"/>).</summary>
        public static (LineWords Words, bool Mono) Reading(PageLine line, WorkspacePresentation workspace, bool instruction)
        {
            var need = instruction ? null : KitPresenter.NeedOf(workspace);
            return Reading(line, need?.Request, instruction, need?.Commands ?? true);
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
            // A command stands alone; another tool's request keeps the file's own line, its name leading it, asked and confirming.
            var body = need.Request == null ? need.Asks : LineWords.Theirs(need.Commands || need.Line == null ? need.Request : need.Line);
            var mono = need.Request != null;
            var column = new List<RoomAction>();
            if (need.Confirming is KitConfirmation confirming)
            {
                if (need.Part != null) (body, mono) = Reading(need.Part, need.Request, instruction: false, need.Commands);
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

        /// <summary>
        /// The redesign's view of <paramref name="binding"/>, the file's frame, with voice as it stands and whether the run
        /// waits to start: what the task's own loop makes of it (<see cref="Tasks"/>), unless a moment outside that loop
        /// answers for the frame (<see cref="Moments"/>, asked in their order, the first that answers).
        /// </summary>
        /// <param name="shown">
        /// What the connection says the work it shows is (Recorded or Demo while a recording plays; null while live): the fact a plain frame,
        /// which carries no task's marks, takes its notice from.
        /// </param>
        public static RoomBar Of(KitFileBinding binding, VoiceStage voice, bool sentAwaitingStart, WorkMarkKind? shown = null)
        {
            var bar = OfBinding(binding, voice, sentAwaitingStart);
            // The lead's honesty rule (2026-11-12), one rule for every frame the bar shows: wherever the work shown is recorded or the
            // demonstration, the bar carries the product's own line whole over all it shows: by the task's own mark where the frame is a task's,
            // else by what the connection says it shows; live work, a practice run included, carries none.
            var task = binding.Plain ? null : binding.Model.Marks.FirstOrDefault(mark => mark.Kind is WorkMarkKind.Recorded or WorkMarkKind.Demo)?.Kind;
            if ((task ?? (shown is WorkMarkKind.Recorded or WorkMarkKind.Demo ? shown : null)) is WorkMarkKind recorded)
                bar.Notice = LineWords.Ours(DemonstrationFallback.Notice(recordedRun: recorded == WorkMarkKind.Recorded));
            return bar;
        }

        private static RoomBar OfBinding(KitFileBinding binding, VoiceStage voice, bool sentAwaitingStart)
        {
            // A frame the task's binding does not bind is read off the frame itself (RoomBar.Frames).
            if (binding.Plain) return OfFrame(binding.Frame, voice) ?? new RoomBar(RoomBarState.Hidden);
            var tasks = Tasks(binding, voice, sentAwaitingStart);
            // What waits on the person, and what was sent, always stands: no moment is asked over an approval, a question,
            // a confirmation or Sent (AUDIT, 2026-11-10).
            if (tasks.State is RoomBarState.Request or RoomBarState.Question or RoomBarState.Confirm or RoomBarState.Sent) return WithClose(binding, tasks);
            foreach (var moment in Moments)
                if (moment(binding, voice, tasks) is RoomBar own)
                {
                    own.FromMoment = true;
                    return WithClose(binding, own);
                }
            return WithClose(binding, tasks);
        }

        /// <summary>The most actions a column holds; past it the older view draws the frame (the view lays as many).</summary>
        public const int ColumnMost = 6;

        /// <summary>The row's way back to the Projects place at rest (FIELD's parts note): "Close" there would read as closing the task.</summary>
        public const string ProjectsWords = "Projects";

        /// <summary>
        /// A task's file always offers its way back to the Projects place (FIELD's parts note; JUDGE's census, 2026-11-11):
        /// the file's own Close, last in the result's column with its cross (the result and its pages, where the census found
        /// no way out), where the column has room and no Cancel already leads out; at rest with no pair, "Projects" last in
        /// the row, with the Back glyph, never main. An approval, a question or a confirmation keeps its own ways out.
        /// </summary>
        private static RoomBar WithClose(KitFileBinding binding, RoomBar bar)
        {
            if (binding[KitPlace.Close] is not { Available: true } close || bar.Column.Concat(bar.Row).Any(action => action.Control.Place is KitPlace.Close or KitPlace.Cancel))
                return bar;
            if (bar.Result && bar.Column.Count < ColumnMost) bar.Column = bar.Column.Append(new RoomAction(close, main: false, icon: GlazeIcon.Close)).ToList();
            // At rest with nothing to press (a recorded run's result), the bar was hidden: it stands as the row with its way back.
            else if (bar.State == RoomBarState.Hidden && !bar.Working && !bar.Result)
                return new RoomBar(RoomBarState.Rest, row: new[] { new RoomAction(close, main: false, ProjectsWords, icon: GlazeIcon.Back) });
            else if (!bar.Pair && !bar.Working && bar.State == RoomBarState.Rest)
                bar.Row = bar.Row.Append(new RoomAction(close, main: false, ProjectsWords, icon: GlazeIcon.Back)).ToList();
            return bar;
        }

        /// <summary>
        /// The result at rest this bar shows, its facts apart for FIELD's cards (A17): the count, the check, each change and
        /// each step (<see cref="RoomResult"/>); null where the bar is no result.
        /// </summary>
        public RoomResult? Known { get; private set; }

        /// <summary>Where a chain of tries at a failed check ended, its line (<see cref="FixedWords"/>), apart from the result's own; null otherwise.</summary>
        public string? Ended { get; private set; }

        /// <summary>Whether a moment outside the task's own loop made this (<see cref="Moments"/>).</summary>
        public bool FromMoment { get; private set; }

        /// <summary>
        /// The task's own loop: the row at rest, talking, a draft, a confirmation, an approval, a question, what was sent;
        /// and, while the agent works and none of those moments stands, at work (<see cref="Working"/>).
        /// </summary>
        private static RoomBar Tasks(KitFileBinding binding, VoiceStage voice, bool sentAwaitingStart)
        {
            var loop = Loop(binding, voice, sentAwaitingStart);
            var model = binding.Model;
            // A task at rest with no result read says how its run stands (B), on the same result moment.
            if (!model.Working && (model.Result ?? (model.Standing != null ? RoomResult.Nothing : null)) is RoomResult result) return Resulting(binding, result, loop);
            if (!model.Working || loop.Pair || loop.State == RoomBarState.Sent) return loop;
            // That a request is under way is not said in words while the agent works: the field and the cursor say it.
            if (loop.State == RoomBarState.Words && loop.Words.Text == FileScreens.RequestUnderWay) loop = new RoomBar(RoomBarState.Hidden);
            loop.Working = true;
            loop.Step = model.Step;
            if (binding[KitPlace.Stop] is { Available: true } stop) loop.Column = new[] { new RoomAction(stop, main: false) };
            // SCREENS's instructions waiting for this turn to end (RoomBar.Waiting.cs).
            if (model.Result?.Waiting is WaitingInstructions waiting) loop = Waited(loop, waiting, binding.Frame);
            return loop;
        }

        private static RoomBar Loop(KitFileBinding binding, VoiceStage voice, bool sentAwaitingStart)
        {
            var model = binding.Model;
            if (model.Stops is string stops)
            {
                // Stop's confirmation, in the file's own words: what stopping does in the panel, its question in the sentence
                // bar (as an approval's stands), Confirm behind its lock, and Cancel. Where the file's line is the question
                // alone, it is the panel's one line.
                var answers = new List<RoomAction>();
                if (binding[KitPlace.Yes] is { Available: true } sure) answers.Add(new RoomAction(sure, main: true, locks: true));
                if (binding[KitPlace.Cancel] is { Available: true } back) answers.Add(new RoomAction(back, main: false));
                var cut = stops.IndexOf(KitMomentWords.Dot, System.StringComparison.Ordinal);
                return cut > 0
                    ? new RoomBar(RoomBarState.Confirm, body: LineWords.Ours(stops.Substring(cut + KitMomentWords.Dot.Length)), sentence: stops.Substring(0, cut), column: answers)
                    : new RoomBar(RoomBarState.Confirm, body: LineWords.Ours(stops), column: answers);
            }
            if (model.Need is KitNeed need) return Asked(binding, need);
            // A question's page as the redesign reads it (its prompt, caption and lines apart); one bound without them stays the file's.
            if (model.Ask is KitAsk ask) return ask.Lines.Count > 0 && (ask.Prompt != null || ask.Caption != null || ask.Body.Count > 0 || ask.Asks != null)
                ? Asking(binding, ask, voice) : new RoomBar(RoomBarState.Hidden);
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
                // A draft longer than the panel's lines stands in the file's parts (its field's line names the rows of the part
                // showing): the next part is the main action until Send stands, the previous one after it, and the caret only
                // on the last part, where the words end.
                var turns = binding.Controls.Where(control => control.Place == KitPlace.Step && control.Available
                    && (control.Action == FileScreens.NextInstructionPart || control.Action == FileScreens.PreviousInstructionPart)).ToList();
                var cut = turns.Count > 0 ? binding.Frame.Lines.FirstOrDefault(line => line.Action == FileScreens.EditInstruction) : null;
                var further = turns.Any(control => control.Action == FileScreens.NextInstructionPart);
                foreach (var turn in turns.OrderBy(control => control.Action == FileScreens.NextInstructionPart ? 0 : 1))
                    column.Add(turn.Action == FileScreens.NextInstructionPart
                        ? new RoomAction(turn, main: column.Count == 0, icon: GlazeIcon.Next)
                        : new RoomAction(turn, main: false, icon: GlazeIcon.Back));
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
                return new RoomBar(RoomBarState.Draft, body: worded ? LineWords.Theirs(model.Draft) : LineWords.None, part: worded ? cut : null,
                    caret: !spoken && !further, sentence: sentence, column: column, field: field!.Available ? field : null);
            }
            // The command's own state while it goes, then Sent · Awaiting Start until the run starts.
            if (model.Said.FirstOrDefault(line => line.Words == WorkspaceText.Sent) is PageLine sending)
                return new RoomBar(RoomBarState.Sent, words: LineWords.Ours(sending.Words));
            if (sentAwaitingStart) return new RoomBar(RoomBarState.Sent, words: LineWords.Ours(SentWords));
            var row = new List<RoomAction>();
            if (talk != null && talk.Available) row.Add(new RoomAction(talk, main: true));
            // Where the file offers instructions to choose from rather than the keyboard, the action says so.
            if (field != null && field.Available) row.Add(new RoomAction(field, main: row.Count == 0, model.Chooses ? ChooseInstruction : Type));
            if (row.Count > 0) return new RoomBar(RoomBarState.Rest, row);
            var why = field?.Reason ?? talk?.Reason ?? model.WhyNotSend;
            return why != null ? new RoomBar(RoomBarState.Words, words: LineWords.Ours(why)) : new RoomBar(RoomBarState.Hidden);
        }
    }
}
