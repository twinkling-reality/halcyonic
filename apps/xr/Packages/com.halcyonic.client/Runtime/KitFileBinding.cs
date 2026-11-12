#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>Where a kit control stands for a press a task's file offers.</summary>
    public enum KitPlace
    {
        /// <summary>The frame's Close, or the composer's Cancel or Hide composer: leaving what shows.</summary>
        Close,

        /// <summary>Stop, beside Close.</summary>
        Stop,

        /// <summary>The composer's Discard draft, beside its Hide composer.</summary>
        Discard,

        /// <summary>The composer's field: Write instruction where the composer is closed, the instruction's own line where it is open.</summary>
        Field,

        /// <summary>Hold to talk, held rather than pressed.</summary>
        Talk,

        Send,

        Deny,

        Approve,

        /// <summary>A confirmation's Cancel, where the answer pressed stood.</summary>
        Cancel,

        /// <summary>A confirmation's Yes, once the whole request has been read.</summary>
        Yes,

        /// <summary>One of the file's sections: Waiting, Activity, Changes or Checks.</summary>
        Section,

        /// <summary>Activity's pager: Next page, and on its last page First page.</summary>
        Pager,

        /// <summary>
        /// One of the task's own routes Activity offers under its log: Show this task here, Ask workspace, Read results or
        /// Character colour, each the line that opens it; several stand, in the frame's order.
        /// </summary>
        Route,

        /// <summary>One of a question's answers to choose, the one chosen lit; several stand, in the frame's order.</summary>
        Choice,

        /// <summary>
        /// A row on a question's page that takes the person on within it: Type my answer, More choices, Read question, Read
        /// why, an answer back to its question, a part's next part; several stand, in the frame's order.
        /// </summary>
        Step,

        /// <summary>A question's main press on: Next question, the next part, Review answers, the answers' next part, Send answer.</summary>
        Forward,

        /// <summary>Back to the question before, or the answers' part before.</summary>
        Back,

        /// <summary>An answer sent that may still take effect, standing where the answer was and taking no press; bound only for the redesign.</summary>
        Sending,
    }

    /// <summary>
    /// One kit control bound to what a task's file drew: the frame it came from, the action and key a press raises,
    /// and the words, availability and reason the frame gives it. A press is that action on that frame, so the
    /// column's own rules decide, and a press of a frame no longer standing is passed over as any other.
    /// </summary>
    public sealed class KitControl
    {
        public KitControl(KitPlace place, MenuFrame frame, string action, string? key, string words, GlazeIcon? icon, bool available, string? reason, bool holds,
            bool chosen = false, AnswerMark mark = AnswerMark.None)
        {
            Mark = mark;
            Place = place;
            Frame = frame;
            Action = action;
            Key = key;
            Words = words;
            Icon = icon;
            Available = available;
            Reason = reason;
            Holds = holds;
            Chosen = chosen;
        }

        public KitPlace Place { get; }

        /// <summary>The frame the press belongs to, as the plane reports a press with the frame it drew.</summary>
        public MenuFrame Frame { get; }

        public string Action { get; }

        public string? Key { get; }

        public string Words { get; }

        public GlazeIcon? Icon { get; }

        public bool Available { get; }

        /// <summary>Why it can't be taken now, in the frame's words; null when it can.</summary>
        public string? Reason { get; }

        /// <summary>Held rather than pressed, as Hold to talk is.</summary>
        public bool Holds { get; }

        /// <summary>A section that is the one showing, or an answer that is chosen.</summary>
        public bool Chosen { get; }

        /// <summary>An answer's mark as the file's row carries it: one of several to choose, or any number.</summary>
        public AnswerMark Mark { get; }
    }

    /// <summary>
    /// The spatial kit bound to a task's file (D3.4): what the kit's focused work shows of the frame the file drew,
    /// and every press that frame offers as a kit control. The kit draws the frame production draws, and nothing
    /// else: each footer prompt and each line that acts takes its place in the kit, with the frame's own words,
    /// availability and reason, and a press raises the frame's own action, so guarded routes, drawn authority,
    /// confirmations and the whole-request and whole-instruction rules stay the file's. Where a frame holds a press
    /// the kit has no place for, or what it cannot draw whole (a side panel, an instruction in parts, its notice,
    /// a question's answers, results, Changes and Checks pages, paging, a confirmation other than an approval's or a
    /// denial's), there is no binding and the file draws it as before.
    /// </summary>
    public sealed class KitFileBinding
    {
        private KitFileBinding(MenuFrame frame, KitWorkModel? model, IReadOnlyList<KitControl> controls)
        {
            Frame = frame;
            this.model = model;
            Controls = controls;
        }

        private readonly KitWorkModel? model;

        /// <summary>
        /// A frame this binding has no place for, carried for the redesign alone, which reads it off the frame itself
        /// (<see cref="RoomBar.OfFrame"/>): it holds no control and no task's model.
        /// </summary>
        public static KitFileBinding PlainOf(MenuFrame frame) => new KitFileBinding(frame, null, new KitControl[0]);

        /// <summary>Whether this carries a frame with no binding of its own (<see cref="PlainOf"/>).</summary>
        public bool Plain => model == null;

        public MenuFrame Frame { get; }

        /// <summary>The task as the kit shows it. A plain binding has none: asking is a mistake in the caller.</summary>
        public KitWorkModel Model => model ?? throw new System.InvalidOperationException("A plain binding carries a frame and no task's model.");

        /// <summary>Every press the frame offers, each in its kit place; sections in the frame's order.</summary>
        public IReadOnlyList<KitControl> Controls { get; }

        /// <summary>The control in <paramref name="place"/>, or null where the frame offers none; for sections, the first.</summary>
        public KitControl? this[KitPlace place] => Controls.FirstOrDefault(control => control.Place == place);

        public IEnumerable<KitControl> Sections => Controls.Where(control => control.Place == KitPlace.Section);

        /// <summary>The task's routes the frame offers, in its order.</summary>
        public IEnumerable<KitControl> Routes => Controls.Where(control => control.Place == KitPlace.Route);

        /// <summary>The prefix of a spine row's key that stands for one of the file's sections.</summary>
        public const string SectionRowKey = "section:";

        /// <summary>The file's sections as the spine's rows under the task, keyed <see cref="SectionRowKey"/> and the section's key, the one showing lit.</summary>
        public IReadOnlyList<KitRowModel> SectionRows => Sections
            .Select(section => new KitRowModel(SectionRowKey + section.Key, LineWords.Ours(section.Words), opensMore: false, selected: section.Chosen)).ToList();

        /// <summary>
        /// The page the kit lays a file's open instruction on, its composer's well <paramref name="wellRows"/> rows. The kit draws
        /// the whole draft in the well and holds any draft the well holds, so the page leaves the instruction's field the well's
        /// rows after the head row and the reason <see cref="FileInstruction.Measure"/> takes from it: a draft the well holds is
        /// then one part, with no part pager, which has no place in the kit (a frame the kit can't bind goes back to the views,
        /// which lay it for the kit again on the next update).
        /// </summary>
        public static PageBudget ComposerBudget(int wellRows)
        {
            var rows = new RowBudget(1);
            return new RowBudget(wellRows + (int)System.Math.Ceiling(rows.Words(1) + rows.LineGap + rows.Reason));
        }

        /// <summary>
        /// Binds the kit to <paramref name="frame"/>, the frame <paramref name="workspace"/>'s file drew with
        /// <paramref name="armed"/> armed (<see cref="WorkspaceSteering.Armed"/>), and the person's
        /// <paramref name="draft"/>; null where the kit can't draw the frame whole. The composer's chips are
        /// <paramref name="selectors"/> as given (<see cref="KitPresenter.ChipsOf"/>); without them, the workspace's own
        /// runtime's, locked. With <paramref name="roomDesign"/> (<see cref="RoomDesign.Enabled"/>) an instruction's confirmation
        /// binds too (<see cref="KitWorkModel.Instruction"/>), which the old kit has no place for, and an approval's page binds
        /// with a long request in parts, an answer in flight and the file's notice (<see cref="KitNeed.Part"/>,
        /// <see cref="KitNeed.Sending"/>, <see cref="KitNeed.Says"/>), with <paramref name="requestRows"/> the rows the file
        /// counted for what its confirmation reads, where it has measured it. <paramref name="draftSpoken"/>:
        /// the draft was heard, not typed (<see cref="FileInstruction.Spoken"/>).
        /// </summary>
        public static KitFileBinding? Of(MenuFrame frame, WorkspaceAction? armed, WorkspacePresentation workspace, string draft, ConnectionStatus connection,
            string projectName, IReadOnlyList<KitSpineSection> spine, KitRowModel? spineFoot = null, KitAvatar? avatar = null,
            KitSelectors? selectors = null, bool roomDesign = false, bool draftSpoken = false, int? requestRows = null, bool chooses = false,
            int? asking = null, string? writing = null, RoomResult? result = null)
        {
            if (frame.Side != null) return null;
            // The kit shows an approval's, a denial's or the answers' confirmation, and the redesign an instruction's too; any
            // other waits for the file's own drawing.
            var instructing = roomDesign && armed == WorkspaceAction.Instruct;
            // Stop's own confirmation, for the redesign: the file's question, its Yes and its Cancel, whatever else waits.
            var stopping = roomDesign && armed == WorkspaceAction.Interrupt;
            if (armed != null && armed != WorkspaceAction.Approve && armed != WorkspaceAction.Deny && armed != WorkspaceAction.Answer && !instructing && !stopping) return null;
            var controls = new List<KitControl>();
            // The instructions offered to choose from (no keyboard, or a recorded run's own): each a choice, bound for the redesign.
            var offering = roomDesign && frame.Lines.Any(line => line.Action == FileScreens.Preset);
            foreach (var (slot, prompt) in frame.Footer.All)
            {
                // For the redesign: an answer in flight, and the way back from the offered instructions (the page's own Cancel).
                var place = roomDesign && prompt.Id == WorkspaceScreens.Sent && !prompt.Available ? KitPlace.Sending
                    : roomDesign && prompt.Id == FileScreens.Cancel && prompt.Kind != PromptKind.Cancel && offering ? KitPlace.Back
                    : roomDesign ? RoomPlaceOf(prompt) ?? PlaceOf(slot, prompt) : PlaceOf(slot, prompt);
                if (place == null || controls.Any(control => control.Place == place)) return null;
                controls.Add(new KitControl(place.Value, frame, prompt.Id, null, prompt.Words, prompt.Icon, prompt.Available, prompt.Reason, prompt.Holds));
            }
            // The task's routes stand under Activity's log; Read results beside an open instruction is the composer's, which has no place for it.
            var composing = frame.Lines.Any(line => line.Action == FileScreens.EditInstruction);
            foreach (var line in frame.Lines.Where(line => line.Action != null))
            {
                // For the redesign, on the page that writes an answer: its line is the field, and Edit Answer a row on within the page.
                // For the redesign, in the composer: a draft cut in parts turns them in the column, as a confirmation's do.
                var place = roomDesign && line.Action == FileScreens.EditInput ? (line.Control == LineControl.Input ? KitPlace.Field : KitPlace.Step)
                    : roomDesign && composing && (line.Action == FileScreens.NextInstructionPart || line.Action == FileScreens.PreviousInstructionPart) ? KitPlace.Step
                    : line.Action == FileScreens.EditInstruction ? KitPlace.Field : line.Action == Footer.NextPage ? KitPlace.Pager
                    : !composing && RouteActions.Contains(line.Action!) ? KitPlace.Route
                    : line.Action == FileScreens.Choose || (offering && line.Action == FileScreens.Preset) ? KitPlace.Choice
                    : StepActions.Contains(line.Action!) || (offering && line.Action == FileScreens.NextPresetPage) ? KitPlace.Step : (KitPlace?)null;
                var several = place is KitPlace.Route or KitPlace.Choice or KitPlace.Step;
                if (place == null || (!several && controls.Any(control => control.Place == place))) return null;
                controls.Add(new KitControl(place.Value, frame, line.Action!, line.Key, line.Words, line.Icon, line.Available, null, false, line.Chosen, line.AnswerMark));
            }
            foreach (var section in frame.Sections)
                controls.Add(new KitControl(KitPlace.Section, frame, MenuFrame.ChooseSection, section.Key, section.Words, null, true, null, false, section.Chosen));

            KitControl? At(KitPlace place) => controls.FirstOrDefault(control => control.Place == place);
            var send = At(KitPlace.Send);
            var talk = At(KitPlace.Talk);
            var work = KitPresenter.WorkOf(workspace, projectName, draft, talk != null, connection, spine, spineFoot, avatar: avatar);
            // What it waits on shows where the frame answers it: its approval page, or the confirmation of an answer.
            var deny = At(KitPlace.Deny);
            var approve = At(KitPlace.Approve);
            var cancel = At(KitPlace.Cancel);
            // An instruction's confirmation: its words first, as the person's, and the frame's question last.
            KitInstruction? instruction = null;
            if (instructing)
            {
                var lines = frame.Lines;
                if (lines.Count < 2 || lines[0].Action != null || lines[lines.Count - 1].Action != null || cancel == null) return null;
                instruction = new KitInstruction(lines[0], lines[lines.Count - 1].Words, requestRows);
            }
            KitNeed? need = null;
            var sending = At(KitPlace.Sending);
            string? stops = null;
            if (stopping)
            {
                if (cancel == null || frame.Lines.LastOrDefault(line => line.Action == null) is not PageLine asks) return null;
                stops = asks.Words;
            }
            if (instruction == null && !stopping && (deny != null || approve != null || cancel != null || sending != null) && KitPresenter.NeedOf(workspace) is KitNeed asked)
            {
                KitConfirmation? confirming = null;
                PageLine? part = null;
                string? says = null;
                var texts = frame.Lines.Where(line => line.Action == null).ToList();
                if (cancel != null && armed is WorkspaceAction answer)
                {
                    // The question under the request is the frame's own last line: what to read first, then whether to answer.
                    var question = frame.Lines.Count > 0 ? frame.Lines[frame.Lines.Count - 1].Words : "";
                    if (roomDesign)
                    {
                        // The redesign reads the request as the file cut it, and a notice the file stands after its question.
                        var at = texts.FindLastIndex(line => line.Words == WorkspaceText.ReadRequestFirst || line.Words == WorkspaceText.ConfirmationPrompt(answer, null));
                        if (at < 1 || at + 2 < texts.Count) return null;
                        question = texts[at].Words;
                        part = texts[0];
                        if (at + 1 < texts.Count) says = texts[at + 1].Words;
                    }
                    var yes = At(KitPlace.Yes);
                    confirming = new KitConfirmation(answer == WorkspaceAction.Approve, question, yes?.Words ?? WorkspaceText.ConfirmLabel(answer),
                        yes != null && yes.Available, question);
                }
                else if (roomDesign && texts.Count > 0)
                {
                    // Asked: a last line that is none of the page's own is what the file says now, a lapse's notice or why no answer can be given.
                    var last = texts[texts.Count - 1].Words;
                    var own = last == asked.Asks.Text || last == asked.Request || last == asked.ScopeNote || asked.Notes.Contains(last);
                    if (!own && texts.Count > 1) says = last;
                    else says = texts.Skip(1).Select(line => line.Words).FirstOrDefault(words => words == WorkspaceText.WhyNoActions(workspace));
                }
                need = new KitNeed(asked.Asks, asked.Request, asked.ScopeNote, asked.Notes, deny?.Words ?? asked.DenyWords, approve?.Words ?? asked.ApproveWords,
                    deny != null && deny.Available, approve != null && approve.Available, confirming, part, says, sending != null, asked.Commands, part != null ? requestRows : null, asked.Line);
            }
            else if (instruction == null && !stopping && (cancel != null || At(KitPlace.Yes) != null) && armed != WorkspaceAction.Answer) return null;
            // A question's page, wherever the file draws one (never Activity, which says only where it waits): its own lines, as drawn.
            KitAsk? ask = null;
            if (need == null && instruction == null && !stopping && workspace.QuestionToAnswer != null
                && !frame.Sections.Any(section => section.Chosen && section.Key == FileScreens.Key(FileSection.Activity)))
            {
                var words = frame.Lines.Where(line => line.Action == null).ToList();
                ask = new KitAsk(words, armed == WorkspaceAction.Answer);
                if (roomDesign)
                {
                    // What the redesign reads of the page: the prompt it shows, for the band (a line of the page, acting or not, that is
                    // a prompt's words; with one prompt, that prompt on every page of it); the page's caption; what the confirmation
                    // asks; and the lines left, which the panel holds.
                    var question = workspace.QuestionToAnswer!;
                    var prompts = question.Prompts.Select(each => FileScreens.QuestionWords(each).Text).ToList();
                    var at = frame.Lines.Select(line => prompts.IndexOf(line.Words)).Where(index => index >= 0).DefaultIfEmpty(-1).First();
                    if (at < 0 && prompts.Count == 1) at = 0;
                    // A page that shows no prompt's words is still about one: the one the person is on (the page that writes its answer).
                    if (at < 0 && asking is int on && on >= 0 && on < prompts.Count) at = on;
                    var band = at >= 0 ? question.Prompts[at].Text : null;
                    var rest = words.Where(line => !prompts.Contains(line.Words)).ToList();
                    string? asks = null;
                    // The page of answers that read alike: the answers, the agent's own words, go on the band, and the panel holds
                    // only Halcyonic's (the lead, 2026-11-10); its caption is the panel's sentence.
                    var alike = rest.FindIndex(line => line.Words.StartsWith(FileScreens.AlikeLead, System.StringComparison.Ordinal));
                    if (alike >= 0)
                    {
                        band = rest[alike].Words.Substring(FileScreens.AlikeLead.Length);
                        rest[alike] = new PageLine(FileScreens.AlikeLead.Substring(0, FileScreens.AlikeLead.Length - KitMomentWords.Dot.Length));
                        var captioned = rest.FindIndex(line => line.Words == FileScreens.AlikeCaption);
                        if (captioned >= 0)
                        {
                            asks = FileScreens.AlikeCaption;
                            rest.RemoveAt(captioned);
                        }
                    }
                    if (ask.Confirming && rest.FindLastIndex(line => line.Words == WorkspaceText.ConfirmationPrompt(WorkspaceAction.Answer, null)) is int last && last >= 0)
                    {
                        asks = rest[last].Words;
                        rest.RemoveAt(last);
                    }
                    PageLine? caption = null;
                    if (!ask.Confirming && rest.Count > 0 && rest[0].Tone == LineTone.Secondary && ReferenceEquals(rest[0], words[0]))
                    {
                        caption = rest[0];
                        rest.RemoveAt(0);
                    }
                    ask = new KitAsk(words, ask.Confirming, band, prompts.Count > 1, caption?.Words, rest, asks, writing);
                }
            }
            else if (armed == WorkspaceAction.Answer) return null;
            // Send says what the frame says of it; with no Send on the frame, only why the connection keeps it.
            var said = Said(frame, workspace);
            // Under the offered instructions the file's notice is its page's last line: what it says now.
            if (offering && frame.Lines.Count > 0 && frame.Lines[frame.Lines.Count - 1].Action == null) said = said.Append(frame.Lines[frame.Lines.Count - 1]).ToList();
            var model = new KitWorkModel(work.ProjectName, work.TaskTitle, work.Badge, work.Marks, work.Report, work.Trail, draft, work.VoiceOffered,
                send != null && send.Available, send != null ? send.Reason : KitPresenter.WhyNotSendOf(connection), work.Spine, work.SpineFoot,
                need, ask, work.Notice, work.Avatar, said, selectors != null ? selectors.Environment : work.Environment,
                selectors != null ? selectors.Agent : work.Agent, selectors?.EnvironmentChangeable ?? false, selectors?.AgentChangeable ?? false, instruction,
                draftSpoken && draft.Length > 0, chooses, stops,
                working: roomDesign && workspace.Execution?.Status is Halcyonic.Contracts.ExecutionStatus.Running or Halcyonic.Contracts.ExecutionStatus.Verifying,
                step: roomDesign ? RoomWork.Of(workspace.Execution) : null,
                offersNeverAsk: selectors?.OffersNeverAsk ?? false, neverAsk: selectors?.NeverAsk ?? false,
                runNeverAsk: workspace.Execution?.ApprovalLevel is Halcyonic.Contracts.ApprovalLevel level ? level == Halcyonic.Contracts.ApprovalLevel.NeverAsk : (bool?)null,
                result: roomDesign && KitPresenter.AtRest(workspace) ? result : null,
                autoFix: roomDesign ? workspace.Execution?.AutoFix : null,
                standing: roomDesign && KitPresenter.AtRest(workspace)
                    ? RoomStanding.Card(workspace.Execution, LineWords.TheirName(work.TaskTitle), KitPresenter.TurnReportOf(work.Report, work.Trail)?.Words) : null);
            // Every line the frame shows is words the kit shows too; a line it would leave out, as an attention note,
            // a pointer to Waiting, why nothing can be sent or a notice, keeps the frame on the file's own drawing.
            var shown = ShownWords(model, workspace);
            foreach (var line in said) shown.Add(line.Words);
            if (ask != null) foreach (var line in ask.Lines) shown.Add(line.Words);
            for (var index = 0; index < frame.Lines.Count; index++)
            {
                // A line that acts is a control, with its own words.
                if (frame.Lines[index].Action != null) continue;
                var words = frame.Lines[index].Words;
                // The redesign's approval shows the request and one sentence; the page's other lines (its notes, where a command runs) it leaves out by design.
                if (roomDesign && need != null) continue;
                var question = cancel != null && index == frame.Lines.Count - 1;
                var request = need?.Request is string whole && (words == whole || words.EndsWith(": " + whole, System.StringComparison.Ordinal))
                    || (instruction != null && index == 0);
                if (!question && !request && !shown.Contains(words)) return null;
            }
            // A page of Waiting the kit draws is an approval's: anything else waiting there stays the file's.
            if (need == null && ask == null && !stopping && frame.Sections.Any(section => section.Chosen && section.Key == FileScreens.Key(FileSection.Waiting))) return null;
            return new KitFileBinding(frame, model, controls);
        }

        /// <summary>The words the kit shows of <paramref name="model"/>: its recorded activity, the agent's report, what it asks, its notice, and the person's instruction under its caption.</summary>
        private static HashSet<string> ShownWords(KitWorkModel model, WorkspacePresentation workspace)
        {
            // The rail says production's words where nothing has been recorded.
            var shown = new HashSet<string>(System.StringComparer.Ordinal) { FileScreens.YourInstruction, FileScreens.NoActivityYet };
            foreach (var entry in workspace.Activity) shown.Add(entry.Text);
            if (model.Draft.Length > 0) shown.Add(model.Draft);
            if (model.Notice != null) shown.Add(model.Notice.Words.Text);
            if (model.Need is KitNeed need)
            {
                shown.Add(need.Asks.Text);
                foreach (var note in need.Notes) shown.Add(note);
                if (need.ScopeNote != null) shown.Add(need.ScopeNote);
            }
            return shown;
        }

        /// <summary>
        /// What Activity says of the work beside its state, as the file laid it, in its order: a notice first; the lines
        /// it leads with in a waiting or a problem's tone (an approval or a question waiting on Waiting, attention notes);
        /// why nothing can be sent, where it says so; then the newest command's feedback. Taken by their place and tone
        /// on the page; the why and the feedback lines are compared only with what writes them, and a notice is never a
        /// recorded entry's words nor outside text. Feedback that an outcome can't be told is the kit's notice already, so
        /// it is said once.
        /// </summary>
        private static IReadOnlyList<PageLine> Said(MenuFrame frame, WorkspacePresentation workspace)
        {
            var said = new List<PageLine>();
            if (!frame.Sections.Any(section => section.Chosen && section.Key == FileScreens.Key(FileSection.Activity))) return said;
            var lines = frame.Lines;
            var why = WorkspaceText.WhyNoActions(workspace);
            var sent = workspace.Commands.FirstOrDefault();
            bool Quiet(int at) => at < lines.Count && lines[at].Action == null && lines[at].Tone == LineTone.Secondary;
            bool Feedback(int at) => Quiet(at) && sent != null && lines[at].Words == sent.Text;
            var index = 0;
            // A notice leads the page whenever the file has one: Halcyonic's own line, never a recorded entry nor the history
            // note (outside text), which stand in the same tone and place when there is no notice.
            var recorded = new HashSet<string>(workspace.Activity.Select(entry => entry.Text), System.StringComparer.Ordinal);
            if (Quiet(index) && !lines[index].WordsAreData && !recorded.Contains(lines[index].Words) && lines[index].Words != why && !Feedback(index)
                && lines[index].Words != FileScreens.NoActivityYet) said.Add(lines[index++]);
            while (index < lines.Count && lines[index].Action == null && (lines[index].Tone == LineTone.Waiting || lines[index].Tone == LineTone.Problem))
                said.Add(lines[index++]);
            if (Quiet(index) && why != null && lines[index].Words == why) said.Add(lines[index++]);
            if (Feedback(index))
            {
                if (sent!.Text != WorkspaceText.NotSure(sent.CommandType)) said.Add(lines[index]);
                index++;
            }
            return said;
        }

        /// <summary>The lines that open the task's own routes from Activity, each a <see cref="KitPlace.Route"/>.</summary>
        private static readonly HashSet<string> RouteActions = new HashSet<string>(System.StringComparer.Ordinal)
        {
            FileScreens.ShowTaskHere, FileScreens.OpenWorkspaceRequest, FileScreens.OpenResults, FileScreens.OpenCharacterColour, FileScreens.ShowScreen,
        };

        /// <summary>The rows that take the person on within a question's page, or an approval's request, each a <see cref="KitPlace.Step"/>.</summary>
        private static readonly HashSet<string> StepActions = new HashSet<string>(System.StringComparer.Ordinal)
        {
            FileScreens.TypeAnswer, FileScreens.MoreAnswers, FileScreens.GoToQuestion, FileScreens.ReadQuestion, FileScreens.ReadWhy, FileScreens.NextPart,
        };

        /// <summary>
        /// The place of a footer prompt only the redesign draws: on the page that writes an answer, Use Answer and the next
        /// part the way on, the part before the way back, and the held Hold to Talk; on the page of answers that read alike,
        /// its next part and its way back. Null for any other prompt.
        /// </summary>
        private static KitPlace? RoomPlaceOf(Prompt prompt) => prompt.Id switch
        {
            FileScreens.UseInput or FileScreens.NextInputPart or FileScreens.NextAmbiguityPart => KitPlace.Forward,
            FileScreens.PreviousInputPart or FileScreens.BackAmbiguity => KitPlace.Back,
            FileScreens.SpeakAnswer when prompt.Holds => KitPlace.Talk,
            _ => null,
        };

        /// <summary>The kit place of a footer prompt, or null where the kit has none for it.</summary>
        private static KitPlace? PlaceOf(PromptSlot slot, Prompt prompt)
        {
            if (prompt.Kind == PromptKind.NextPage) return KitPlace.Pager;
            if (prompt.Kind == PromptKind.Close) return KitPlace.Close;
            if (prompt.Kind == PromptKind.Cancel) return KitPlace.Cancel;
            if (prompt.Kind == PromptKind.Yes) return KitPlace.Yes;
            if (prompt.Holds && prompt.Id == FileScreens.HoldToTalk) return KitPlace.Talk;
            return prompt.Id switch
            {
                FileScreens.Stop => KitPlace.Stop,
                FileScreens.CancelInstruction when slot == PromptSlot.Rare => KitPlace.Discard,
                FileScreens.TellIt => KitPlace.Field,
                FileScreens.SendInstruction => KitPlace.Send,
                FileScreens.Deny => KitPlace.Deny,
                FileScreens.Approve => KitPlace.Approve,
                FileScreens.NextQuestion or FileScreens.NextPart or FileScreens.MoreAnswers or FileScreens.SendAnswer => KitPlace.Forward,
                FileScreens.BackQuestion or FileScreens.PreviousReviewPart => KitPlace.Back,
                _ => null,
            };
        }
    }
}
