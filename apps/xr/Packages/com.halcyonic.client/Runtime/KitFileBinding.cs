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
    }

    /// <summary>
    /// One kit control bound to what a task's file drew: the frame it came from, the action and key a press raises,
    /// and the words, availability and reason the frame gives it. A press is that action on that frame, so the
    /// column's own rules decide, and a press of a frame no longer standing is passed over as any other.
    /// </summary>
    public sealed class KitControl
    {
        public KitControl(KitPlace place, MenuFrame frame, string action, string? key, string words, GlazeIcon? icon, bool available, string? reason, bool holds,
            bool chosen = false)
        {
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

        /// <summary>A section that is the one showing.</summary>
        public bool Chosen { get; }
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
        private KitFileBinding(MenuFrame frame, KitWorkModel model, IReadOnlyList<KitControl> controls)
        {
            Frame = frame;
            Model = model;
            Controls = controls;
        }

        public MenuFrame Frame { get; }

        public KitWorkModel Model { get; }

        /// <summary>Every press the frame offers, each in its kit place; sections in the frame's order.</summary>
        public IReadOnlyList<KitControl> Controls { get; }

        /// <summary>The control in <paramref name="place"/>, or null where the frame offers none; for sections, the first.</summary>
        public KitControl? this[KitPlace place] => Controls.FirstOrDefault(control => control.Place == place);

        public IEnumerable<KitControl> Sections => Controls.Where(control => control.Place == KitPlace.Section);

        /// <summary>The prefix of a spine row's key that stands for one of the file's sections.</summary>
        public const string SectionRowKey = "section:";

        /// <summary>The file's sections as the spine's rows under the task, keyed <see cref="SectionRowKey"/> and the section's key, the one showing lit.</summary>
        public IReadOnlyList<KitRowModel> SectionRows => Sections
            .Select(section => new KitRowModel(SectionRowKey + section.Key, LineWords.Ours(section.Words), opensMore: false, selected: section.Chosen)).ToList();

        /// <summary>
        /// Binds the kit to <paramref name="frame"/>, the frame <paramref name="workspace"/>'s file drew with
        /// <paramref name="armed"/> armed (<see cref="WorkspaceSteering.Armed"/>), and the person's
        /// <paramref name="draft"/>; null where the kit can't draw the frame whole.
        /// </summary>
        public static KitFileBinding? Of(MenuFrame frame, WorkspaceAction? armed, WorkspacePresentation workspace, string draft, ConnectionStatus connection,
            string projectName, IReadOnlyList<KitSpineSection> spine, KitRowModel? spineFoot = null, KitAvatar? avatar = null)
        {
            if (frame.Side != null) return null;
            // The kit shows an approval's or a denial's confirmation; any other waits for the file's own drawing.
            if (armed != null && armed != WorkspaceAction.Approve && armed != WorkspaceAction.Deny) return null;
            var controls = new List<KitControl>();
            foreach (var (slot, prompt) in frame.Footer.All)
            {
                var place = PlaceOf(slot, prompt);
                if (place == null || controls.Any(control => control.Place == place)) return null;
                controls.Add(new KitControl(place.Value, frame, prompt.Id, null, prompt.Words, prompt.Icon, prompt.Available, prompt.Reason, prompt.Holds));
            }
            foreach (var line in frame.Lines.Where(line => line.Action != null))
            {
                var place = line.Action == FileScreens.EditInstruction ? KitPlace.Field : line.Action == Footer.NextPage ? KitPlace.Pager : (KitPlace?)null;
                if (place == null || controls.Any(control => control.Place == place)) return null;
                controls.Add(new KitControl(place.Value, frame, line.Action!, line.Key, line.Words, line.Icon, line.Available, null, false));
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
            KitNeed? need = null;
            if ((deny != null || approve != null || cancel != null) && KitPresenter.NeedOf(workspace) is KitNeed asked)
            {
                KitConfirmation? confirming = null;
                if (cancel != null && armed is WorkspaceAction answer)
                {
                    // The question under the request is the frame's own last line: what to read first, then whether to answer.
                    var question = frame.Lines.Count > 0 ? frame.Lines[frame.Lines.Count - 1].Words : "";
                    var yes = At(KitPlace.Yes);
                    confirming = new KitConfirmation(answer == WorkspaceAction.Approve, question, yes?.Words ?? WorkspaceText.ConfirmLabel(answer),
                        yes != null && yes.Available, question);
                }
                need = new KitNeed(asked.Asks, asked.Request, asked.ScopeNote, asked.Notes, deny?.Words ?? asked.DenyWords, approve?.Words ?? asked.ApproveWords,
                    deny != null && deny.Available, approve != null && approve.Available, confirming);
            }
            else if (cancel != null || At(KitPlace.Yes) != null) return null;
            // Send says what the frame says of it; with no Send on the frame, only why the connection keeps it.
            var said = Said(frame, workspace);
            var model = new KitWorkModel(work.ProjectName, work.TaskTitle, work.Badge, work.Marks, work.Report, work.Trail, draft, work.VoiceOffered,
                send != null && send.Available, send != null ? send.Reason : KitPresenter.WhyNotSendOf(connection), work.Spine, work.SpineFoot,
                need, null, work.Notice, work.Avatar, said);
            // Every line the frame shows is words the kit shows too; a line it would leave out, as an attention note,
            // a pointer to Waiting, why nothing can be sent or a notice, keeps the frame on the file's own drawing.
            var shown = ShownWords(model, workspace);
            foreach (var line in said) shown.Add(line.Words);
            for (var index = 0; index < frame.Lines.Count; index++)
            {
                // A line that acts is a control, with its own words.
                if (frame.Lines[index].Action != null) continue;
                var words = frame.Lines[index].Words;
                var question = cancel != null && index == frame.Lines.Count - 1;
                var request = need?.Request is string whole && (words == whole || words.EndsWith(": " + whole, System.StringComparison.Ordinal));
                if (!question && !request && !shown.Contains(words)) return null;
            }
            // A page of Waiting the kit draws is an approval's: anything else waiting there stays the file's.
            if (need == null && frame.Sections.Any(section => section.Chosen && section.Key == FileScreens.Key(FileSection.Waiting))) return null;
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
        /// on the page; the why and the feedback lines are compared only with what writes them. Feedback that an outcome
        /// can't be told is the kit's notice already, so it is said once.
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
            // A notice leads the page whenever the file has one; nothing else in the secondary tone stands before what Activity leads with.
            if (Quiet(index) && lines[index].Words != why && !Feedback(index) && lines[index].Words != FileScreens.NoActivityYet) said.Add(lines[index++]);
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
                _ => null,
            };
        }
    }
}
