#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// What an open workspace is in the middle of, besides the work itself: the tab chosen, a notice,
    /// the instructions offered where there is no keyboard, and the agent's question or the whole
    /// request as the panel split them into parts at its width. The director keeps one for each open
    /// workspace; <see cref="WorkspaceScreens"/> reads it.
    /// </summary>
    public sealed class WorkspaceScreen
    {
        private WorkspaceQuestion question = WorkspaceQuestion.Doing;
        private UnderstandPrompt prompt = UnderstandPrompt.WhatChanged;

        /// <summary>The tab chosen: the person's question it answers. Another tab's answer starts at its first page.</summary>
        public WorkspaceQuestion Question
        {
            get => question;
            set
            {
                if (value != question) AnswerPage = 0;
                question = value;
            }
        }

        /// <summary>What Help me understand answers. Another starts at its first page.</summary>
        public UnderstandPrompt Prompt
        {
            get => prompt;
            set
            {
                if (value != prompt) AnswerPage = 0;
                prompt = value;
            }
        }

        /// <summary>The pages of the section's answer, as <see cref="ReadAnswer"/> split it last.</summary>
        public IReadOnlyList<SectionPresentation> AnswerPages { get; private set; } = Array.Empty<SectionPresentation>();

        /// <summary>The page of the answer showing, from 0.</summary>
        public int AnswerPage { get; private set; }

        /// <summary>
        /// The page of the answer to draw, or null before one is read: of several, its provenance line
        /// starts with which it is, "Step 2 of 7", as the pager beside the heading has no words.
        /// </summary>
        public SectionPresentation? Answer
        {
            get
            {
                if (AnswerPages.Count == 0) return null;
                var page = AnswerPages[AnswerPage];
                if (AnswerPages.Count == 1) return page;
                return new SectionPresentation(page.Kind, Client.AnswerPages.Caption(page, AnswerPage, AnswerPages.Count) + " · " + page.Provenance,
                    page.ProvenanceTone, page.Lines, page.Simulated, page.Steps);
            }
        }

        /// <summary>
        /// Splits the section's answer into the pages <paramref name="room"/> holds under the heading,
        /// keeping the page the person is on where it still exists.
        /// </summary>
        public void ReadAnswer(SectionPresentation section, AnswerRoom room)
        {
            AnswerPages = Client.AnswerPages.Split(section, room);
            AnswerPage = Math.Min(AnswerPage, AnswerPages.Count - 1);
        }

        /// <summary>Turns the answer's pages by <paramref name="by"/>, never past its first or last.</summary>
        public void TurnAnswer(int by) => AnswerPage = Math.Max(0, Math.Min(AnswerPage + by, AnswerPages.Count - 1));

        /// <summary>What something the person just did came to, or what hold to talk is doing, for a few seconds; null when nothing.</summary>
        public string? Notice { get; set; }

        /// <summary>Hold to talk is offered: in a development build, never in the demonstration (ADR 0021).</summary>
        public bool Speak { get; set; }

        /// <summary>The instructions offered where there is no keyboard, while they show in place of the tab; null otherwise.</summary>
        public IReadOnlyList<PresetInstruction>? Presets { get; set; }

        /// <summary>The run's details show on What is it doing? in place of its log.</summary>
        public bool Details { get; set; }

        /// <summary>The zone the activity's times are in.</summary>
        public TimeZoneInfo Zone { get; set; } = TimeZoneInfo.Utc;

        /// <summary>Said after "Recent activity": that the times are in UTC, or how reading the history goes.</summary>
        public string ActivityNote { get; set; } = "";

        /// <summary>Where the person is in the agent's question.</summary>
        public QuestionPlace Place { get; } = new QuestionPlace();

        /// <summary>Each prompt's text in parts of <see cref="WorkspaceScreens.QuestionLines"/> lines, as the panel split it, in the prompts' order.</summary>
        public IReadOnlyList<IReadOnlyList<string>> QuestionParts { get; private set; } = Array.Empty<IReadOnlyList<string>>();

        /// <summary>The whole request an armed approval or denial answers, in parts of <see cref="RequestLines"/> lines, as the panel split it.</summary>
        public IReadOnlyList<string> RequestParts { get; set; } = Array.Empty<string>();

        /// <summary>The lines each part of the request holds.</summary>
        public int RequestLines { get; set; } = 1;

        /// <summary>The part of the request showing, from 0.</summary>
        public int RequestPart { get; set; }

        /// <summary>
        /// Reads <paramref name="draft"/>'s question, each prompt's text in <paramref name="parts"/> as
        /// the panel split it: from its first step when it is another question, else where the person
        /// was.
        /// </summary>
        public void ReadQuestion(QuestionDraft draft, IReadOnlyList<IReadOnlyList<string>> parts)
        {
            Place.Show(draft);
            QuestionParts = parts;
            Place.Measured(parts.Select(prompt => prompt.Count).ToList());
        }
    }

    /// <summary>
    /// Every screen of the open workspace as a <see cref="PanelModel"/> (ADR 0023), so the XR layer only
    /// draws it and acts on what is pressed. The header names the work, its goal and its state; the
    /// tabs ask the person's questions in short names, Waiting for you first and only while something
    /// waits; each tab's whole question heads its answer; the bar holds Stop at its left and the one
    /// action the work leads to at its right. Nothing here sends anything: every press goes through
    /// <see cref="WorkspaceSteering"/>, and an action that needs confirming shows the confirm step,
    /// whose Yes the frame stands where no control stood.
    /// </summary>
    public static class WorkspaceScreens
    {
        // What each action raises, for the workspace to act on; a tab raises PanelModel.Tab with its question's key.
        public const string Refresh = "refresh";
        public const string Approve = "approve";
        public const string Deny = "deny";
        public const string Stop = "stop";
        public const string TellIt = "tell-it";
        public const string SendAnswer = "send-answer";
        public const string Sent = "sent";
        public const string HoldToTalk = "hold-to-talk";
        public const string Yes = "yes";
        public const string Cancel = "cancel";
        public const string Preset = "preset";
        public const string Choose = "choose";
        public const string TypeAnswer = "type-answer";
        public const string SpeakAnswer = "speak-answer";
        public const string ShowDetails = "show-details";
        public const string ShowLog = "show-log";

        /// <summary>The lines in each part of the agent's question, the answers on their page under them.</summary>
        public const int QuestionLines = 2;

        /// <summary>How much of what this headset sent shows, newest first.</summary>
        public const int RecentFeedback = 2;

        /// <summary>How much of what the work did shows at most, oldest first; older lines give way where there is no room.</summary>
        public const int RecentActivity = 4;

        public static PanelModel Screen(WorkspacePresentation workspace, WorkspaceSteering steering, WorkspaceScreen screen)
        {
            var character = workspace.Character;
            var model = new PanelModel(character.Title)
            {
                TitleIsData = true,
                Context = WorkspaceText.Goal(workspace),
                ContextIsData = true,
                Badge = StateLanguage.BadgeOf(character),
                Marks = StateLanguage.MarksOf(character),
                Notice = screen.Notice,
                Movable = false,
            };
            // While an approval or denial waits for its confirmation, the whole request it answers
            // shows instead of the tabs and their answers; Cancel brings them back.
            var request = steering.Request(workspace);
            var answering = false;
            if (request != null) Request(model, screen, request);
            else
            {
                var shown = Showing(workspace, screen);
                Tabs(model, workspace, shown);
                if (screen.Presets != null) Presets(model, screen.Presets);
                else if (steering.Armed == WorkspaceAction.Answer && screen.Place.Draft != null) Answers(model, screen.Place.Draft);
                else Body(model, workspace, screen, shown, confirming: steering.Armed != null);
                // The agent's question with its answers: hold to talk there speaks the answer, beside it.
                answering = shown == WorkspaceQuestion.NeedFromYou && !(WorkspaceText.NeedFromYou(workspace) is NeedAnswer)
                    && Asked(workspace, screen)?.Question.Answerable == true;
            }
            Bar(model, workspace, steering, screen, answering);
            return model;
        }

        /// <summary>The tab that shows: the one chosen, unless it is what waits for the person and nothing does, or its question is not read yet.</summary>
        public static WorkspaceQuestion Showing(WorkspacePresentation workspace, WorkspaceScreen screen)
        {
            if (screen.Question != WorkspaceQuestion.NeedFromYou) return screen.Question;
            if (workspace.ApprovalToAnswer != null) return WorkspaceQuestion.NeedFromYou;
            return Asked(workspace, screen) != null ? WorkspaceQuestion.NeedFromYou : WorkspaceQuestion.Doing;
        }

        /// <summary>The key a tab raises its question with.</summary>
        public static string TabKey(WorkspaceQuestion question) => question switch
        {
            WorkspaceQuestion.Doing => "doing",
            WorkspaceQuestion.Understand => "understand",
            WorkspaceQuestion.Checked => "checked",
            WorkspaceQuestion.NeedFromYou => "waiting",
            _ => throw new ArgumentOutOfRangeException(nameof(question), question, "Unhandled question."),
        };

        /// <summary>The key a prompt under Help me understand raises.</summary>
        public static string PromptKey(UnderstandPrompt prompt) => prompt switch
        {
            UnderstandPrompt.WhatChanged => "what-changed",
            UnderstandPrompt.WhyChanged => "why-changed",
            UnderstandPrompt.HowBuilt => "how-built",
            _ => throw new ArgumentOutOfRangeException(nameof(prompt), prompt, "Unhandled prompt."),
        };

        /// <summary>The prompt a key names, or null.</summary>
        public static UnderstandPrompt? PromptOf(string? key)
        {
            foreach (UnderstandPrompt prompt in Enum.GetValues(typeof(UnderstandPrompt)))
            {
                if (PromptKey(prompt) == key) return prompt;
            }
            return null;
        }

        /// <summary>The question a tab's key names, or null.</summary>
        public static WorkspaceQuestion? QuestionOf(string? key)
        {
            foreach (WorkspaceQuestion question in Enum.GetValues(typeof(WorkspaceQuestion)))
            {
                if (TabKey(question) == key) return question;
            }
            return null;
        }

        /// <summary>The workspace's action a bar press raises, or null for any other press.</summary>
        public static WorkspaceAction? ActionOf(string id) => id switch
        {
            Approve => WorkspaceAction.Approve,
            Deny => WorkspaceAction.Deny,
            Stop => WorkspaceAction.Interrupt,
            TellIt => WorkspaceAction.Instruct,
            SendAnswer => WorkspaceAction.Answer,
            _ => (WorkspaceAction?)null,
        };

        /// <summary>The question the agent asks, while it is the one the person reads.</summary>
        private static QuestionDraft? Asked(WorkspacePresentation workspace, WorkspaceScreen screen)
        {
            var draft = screen.Place.Draft;
            var execution = workspace.Execution?.ExecutionId;
            return draft != null && execution != null && draft.Answers(execution, workspace.QuestionToAnswer) ? draft : null;
        }

        /// <summary>Waiting for you first, in the attention colour, and only while something waits; then the work's other questions.</summary>
        private static void Tabs(PanelModel model, WorkspacePresentation workspace, WorkspaceQuestion shown)
        {
            foreach (var question in WorkspaceText.Questions(workspace))
            {
                model.Tabs.Add(new PanelTab(TabKey(question), WorkspaceText.TabLabel(question), question == shown, question == WorkspaceQuestion.NeedFromYou));
            }
        }

        private static void Body(PanelModel model, WorkspacePresentation workspace, WorkspaceScreen screen, WorkspaceQuestion shown, bool confirming)
        {
            switch (shown)
            {
                case WorkspaceQuestion.NeedFromYou:
                    // An approval first, as the runtime blocks on it; else the agent's question.
                    if (WorkspaceText.NeedFromYou(workspace) is NeedAnswer need) Approval(model, need);
                    else Question(model, workspace, screen, Asked(workspace, screen)!);
                    break;
                case WorkspaceQuestion.Understand:
                case WorkspaceQuestion.Checked:
                    Section(model, screen, shown);
                    break;
                default:
                    Doing(model, workspace, screen, confirming);
                    break;
            }
        }

        /// <summary>
        /// Help me understand and What was checked?: the section's own lines, which the panel draws
        /// under the heading, a page at a time. Help me understand asks one of its questions at a time,
        /// each a pill in the heading's row; a flow pages a step at a time.
        /// </summary>
        private static void Section(PanelModel model, WorkspaceScreen screen, WorkspaceQuestion shown)
        {
            model.Heading = WorkspaceText.Question(shown);
            var pages = screen.AnswerPages;
            // Help me understand reads again by itself as the work changes, so while its flow pages,
            // the pager takes Refresh's place beside its questions, where all three don't fit.
            if (shown != WorkspaceQuestion.Understand || pages.Count <= 1)
            {
                model.HeadingAction = new PanelAction(Refresh, WorkspaceText.Refresh, PanelActionRole.Secondary, icon: GlazeIcon.Refresh);
            }
            if (shown == WorkspaceQuestion.Understand)
            {
                foreach (UnderstandPrompt prompt in Enum.GetValues(typeof(UnderstandPrompt)))
                {
                    model.Prompts.Add(new PanelTab(PromptKey(prompt), WorkspaceText.PromptLabel(prompt), prompt == screen.Prompt));
                }
            }
            model.CustomBody = true;
            model.CustomBodyIsText = true;
            // The pager stands in the heading's row, without words: which page shows leads the
            // provenance line (WorkspaceScreen.Answer).
            if (pages.Count > 1) model.Parts = (screen.AnswerPage, pages.Count);
        }

        /// <summary>
        /// What is it doing?: one plain answer, what this headset sent and how it went, newest first,
        /// then what the work did, oldest first so the newest is last, as in a log. The agent's words
        /// in it are its claims: quoted, and leaning. While a confirmation asks about something, what
        /// was sent gives way too where its Yes takes room.
        /// </summary>
        private static void Doing(PanelModel model, WorkspacePresentation workspace, WorkspaceScreen screen, bool confirming)
        {
            if (screen.Details)
            {
                RunDetails(model, workspace, screen);
                return;
            }
            model.Heading = WorkspaceText.Question(WorkspaceQuestion.Doing);
            model.HeadingAction = new PanelAction(ShowDetails, WorkspaceText.ShowDetails, PanelActionRole.Secondary);
            var tone = AnswerTone(workspace.Character);
            var answer = WorkspaceText.Answer(workspace);
            for (var index = 0; index < answer.Count; index++)
            {
                model.Rows.Add(new PanelRow { Line = true, Title = answer[index], TitleIsData = true, Tone = tone, Continues = index > 0 });
            }
            var feedback = workspace.Commands.Take(RecentFeedback).Select(command => command.Text).ToList();
            if (feedback.Count == 0) feedback.Add(WorkspaceText.NothingSentYet);
            for (var index = 0; index < feedback.Count; index++)
            {
                model.Rows.Add(new PanelRow
                {
                    Line = true, Size = PanelTextSize.Caption, Title = feedback[index], TitleIsData = true, Continues = index > 0, Droppable = confirming,
                });
            }
            var activity = workspace.Activity;
            // A caption over nothing would read as nothing done; it stays while it says how the reading goes.
            if (activity.Count == 0 && screen.ActivityNote.Length == 0) return;
            model.Rows.Add(new PanelRow
            {
                Line = true,
                Size = PanelTextSize.Caption,
                Title = WorkspaceText.RecentActivity + screen.ActivityNote,
                TitleIsData = screen.ActivityNote.Length > 0,
                Droppable = true,
            });
            for (var index = Math.Max(0, activity.Count - RecentActivity); index < activity.Count; index++)
            {
                var entry = activity[index];
                model.Rows.Add(new PanelRow
                {
                    Line = true,
                    Size = PanelTextSize.Caption,
                    Title = WorkspaceText.Activity(entry, screen.Zone),
                    TitleIsData = true,
                    Claim = entry.Reported,
                    Continues = true,
                    Droppable = true,
                });
            }
        }

        /// <summary>
        /// How is it running?: the run's details in place of the log, a line each, with Show the log
        /// beside the question to go back.
        /// </summary>
        private static void RunDetails(PanelModel model, WorkspacePresentation workspace, WorkspaceScreen screen)
        {
            model.Heading = WorkspaceText.HowIsItRunning;
            model.HeadingAction = new PanelAction(ShowLog, WorkspaceText.ShowLog, PanelActionRole.Secondary);
            var lines = WorkspaceText.RunDetails(workspace, screen.Zone);
            for (var index = 0; index < lines.Count; index++)
            {
                model.Rows.Add(new PanelRow { Line = true, Title = lines[index].Line, TitleIsData = lines[index].IsData, Continues = index > 0 });
            }
        }

        /// <summary>The answer's tone: quiet while nothing waits, else the state's own, waiting for you or what went wrong.</summary>
        private static GlazeTone? AnswerTone(CharacterPresentation character)
        {
            if (character.AttentionNotes.Count == 0) return GlazeTone.Neutral;
            var tone = StateLanguage.BadgeOf(character).Tone;
            return tone == GlazeTone.Attention || tone == GlazeTone.Failure || tone == GlazeTone.Unknown ? tone : (GlazeTone?)null;
        }

        /// <summary>What do you need from me?, for an approval: what it wants, the request as reported, and what each answer does.</summary>
        private static void Approval(PanelModel model, NeedAnswer need)
        {
            model.Heading = WorkspaceText.Question(WorkspaceQuestion.NeedFromYou);
            model.Rows.Add(new PanelRow { Line = true, Title = need.Asks, TitleIsData = true, Tone = GlazeTone.Attention });
            if (need.Request != null) model.Rows.Add(new PanelRow { Line = true, Title = need.Request, TitleIsData = true, TitleLines = 3, Continues = true });
            for (var index = 0; index < need.Notes.Count; index++)
            {
                model.Rows.Add(new PanelRow { Line = true, Size = PanelTextSize.Caption, Title = need.Notes[index], Continues = index > 0 });
            }
        }

        /// <summary>
        /// The agent's question (ADR 0022), a step at a time: a part of the prompt's text, and under it
        /// a page of its answers; which prompt shows and how it is answered stand at the pager's left.
        /// A question Halcyonic can't answer shows why, and the way on, instead of answers.
        /// </summary>
        private static void Question(PanelModel model, WorkspacePresentation workspace, WorkspaceScreen screen, QuestionDraft draft)
        {
            var place = screen.Place;
            var prompt = place.Prompt;
            var asked = draft.Prompts[prompt];
            var parts = prompt < screen.QuestionParts.Count && screen.QuestionParts[prompt].Count > 0
                ? screen.QuestionParts[prompt]
                : new[] { WorkspaceText.OneLine(asked.Text) };
            model.Columns = 2;
            model.Rows.Add(new PanelRow { Line = true, Title = parts[Math.Min(place.TextPart, parts.Count - 1)], TitleIsData = true, TitleLines = QuestionLines });
            model.PartsHeading = WorkspaceText.PromptHeading(draft.Question, prompt);
            if (!draft.Question.Answerable)
            {
                model.Rows.Add(new PanelRow { Line = true, Title = WorkspaceText.CannotAnswer(draft.Question), TitleLines = 2, Tone = GlazeTone.Attention });
                model.PartsNote = WorkspaceText.AgentWaits;
            }
            else
            {
                foreach (var index in place.Answers) model.Rows.Add(Answer(draft, prompt, index, screen.Speak));
                var lead = WorkspaceText.QuestionLead(workspace);
                model.PartsNote = WorkspaceText.PromptHow(asked) + (lead.Length == 0 ? "" : " " + lead);
            }
            if (place.Steps > 1)
            {
                model.Parts = (place.Step, place.Steps);
                model.PartsCaption = EntryText.Page(place.Step, place.Steps);
            }
        }

        /// <summary>
        /// One answer offered: its label whole where two lines hold it, its description after it, cut
        /// short only where it must be, and Chosen in words as well as by its edge; or Type an answer,
        /// with hold to talk beside it where it is offered. Either only drafts the answer.
        /// </summary>
        private static PanelRow Answer(QuestionDraft draft, int prompt, int index, bool speak)
        {
            var asked = draft.Prompts[prompt];
            if (index < asked.Options.Count)
            {
                var option = asked.Options[index];
                var chosen = draft.IsChosen(prompt, option.Label);
                var description = string.IsNullOrWhiteSpace(option.Description) ? null : WorkspaceText.OneLine(option.Description!);
                return new PanelRow
                {
                    Title = (chosen ? "Chosen: " : "") + WorkspaceText.OneLine(option.Label) + (description == null ? "" : " · " + description),
                    TitleIsData = true,
                    TitleLines = 2,
                    Chosen = chosen,
                    Action = Choose,
                    Key = index.ToString(CultureInfo.InvariantCulture),
                };
            }
            var typed = draft.Typed(prompt);
            return new PanelRow
            {
                Title = WorkspaceText.TypedLabel(typed),
                TitleIsData = typed != null,
                TitleLines = 2,
                Chosen = typed != null,
                Action = TypeAnswer,
                Side = speak ? new PanelAction(SpeakAnswer, VoiceText.HoldToTalk, PanelActionRole.Secondary, holds: true, icon: GlazeIcon.HoldToTalk) : null,
            };
        }

        /// <summary>The answers about to be sent, one line for each prompt, while their confirmation is asked.</summary>
        private static void Answers(PanelModel model, QuestionDraft draft)
        {
            model.Heading = WorkspaceText.Question(WorkspaceQuestion.NeedFromYou);
            for (var prompt = 0; prompt < draft.Prompts.Count; prompt++)
            {
                var asked = draft.Prompts[prompt];
                var given = asked.Options.Where(option => draft.IsChosen(prompt, option.Label)).Select(option => WorkspaceText.OneLine(option.Label)).ToList();
                if (draft.Typed(prompt) is string typed) given.Add("“" + WorkspaceText.OneLine(typed) + "”");
                model.Rows.Add(new PanelRow
                {
                    Line = true,
                    Title = WorkspaceText.PromptHeading(draft.Question, prompt) + ": " + string.Join(", ", given),
                    TitleIsData = true,
                    TitleLines = 2,
                    Continues = prompt > 0,
                });
            }
        }

        /// <summary>The instructions offered where there is no keyboard, the recorded demonstration's own while it plays.</summary>
        private static void Presets(PanelModel model, IReadOnlyList<PresetInstruction> presets)
        {
            model.Columns = 2;
            var recorded = !ReferenceEquals(presets, WorkspaceText.PresetInstructions);
            for (var index = 0; index < presets.Count; index++)
            {
                model.Rows.Add(new PanelRow { Title = presets[index].Label, TitleIsData = recorded, Action = Preset, Key = index.ToString(CultureInfo.InvariantCulture) });
            }
        }

        /// <summary>The whole request, a part at a time, never cut: the pager says which part shows.</summary>
        private static void Request(PanelModel model, WorkspaceScreen screen, string request)
        {
            var parts = screen.RequestParts.Count > 0 ? screen.RequestParts : new[] { request };
            var part = Math.Max(0, Math.Min(screen.RequestPart, parts.Count - 1));
            model.Rows.Add(new PanelRow { Line = true, Title = parts[part], TitleIsData = true, TitleLines = Math.Max(1, screen.RequestLines) });
            model.Parts = (part, parts.Count);
            model.PartsCaption = WorkspaceText.RequestCaption(part + 1, parts.Count);
        }

        /// <summary>
        /// The bar: Stop at the left; Deny, hold to talk and Tell it, then at the right end the action
        /// the work leads to: Approve, Send answer, Tell it while nothing waits, or, while an answer
        /// sent may still take effect, Sent…, which takes no press. No Tell it or hold to talk while
        /// the agent asks for a secret, and no hold to talk while <paramref name="answering"/> its
        /// question, where the one beside the answers speaks the answer: one Hold to talk a screen.
        /// Or the confirm step; or Cancel, while the keyboard or the instructions offered in its place
        /// show; or why nothing can be sent.
        /// </summary>
        private static void Bar(PanelModel model, WorkspacePresentation workspace, WorkspaceSteering steering, WorkspaceScreen screen, bool answering)
        {
            var cancel = new PanelAction(Cancel, EntryText.Cancel, PanelActionRole.Secondary, icon: GlazeIcon.Close);
            if (steering.Armed is WorkspaceAction armed)
            {
                var yes = steering.CanConfirm
                    ? new PanelAction(Yes, WorkspaceText.ConfirmLabel(armed), armed == WorkspaceAction.Interrupt ? PanelActionRole.Destructive : PanelActionRole.Primary,
                        icon: WorkspaceText.IconOf(armed))
                    : new PanelAction(Yes, EntryText.ReadToPart(Math.Max(1, screen.RequestParts.Count)), PanelActionRole.Primary, available: false,
                        icon: GlazeIcon.Locked);
                model.Confirm = new ConfirmStep(steering.Prompt(workspace), yes, cancel);
                return;
            }
            if (screen.Presets != null || steering.Typing)
            {
                if (screen.Presets == null) model.BarNote = WorkspaceText.TypingPrompt;
                model.Actions = new ActionSet(cancel);
                return;
            }
            var actions = workspace.Actions;
            var waits = WorkspaceText.SomethingWaits(workspace);
            PanelAction? Offered(WorkspaceAction action, string id, PanelActionRole role) =>
                actions.Contains(action) ? new PanelAction(id, WorkspaceText.Label(action), role, icon: WorkspaceText.IconOf(action)) : null;
            var stop = Offered(WorkspaceAction.Interrupt, Stop, PanelActionRole.Destructive);
            var deny = Offered(WorkspaceAction.Deny, Deny, PanelActionRole.Secondary);
            // While the agent asks for something secret, nothing here invites typing or saying it
            // (ADR 0022): it would be journaled. Stop is the way on.
            var secret = workspace.QuestionToAnswer?.Prompts.Any(prompt => prompt.Secret) == true;
            var tell = secret ? null : Offered(WorkspaceAction.Instruct, TellIt, waits ? PanelActionRole.Secondary : PanelActionRole.Primary);
            var primary = Offered(WorkspaceAction.Approve, Approve, PanelActionRole.Primary)
                ?? Offered(WorkspaceAction.Answer, SendAnswer, PanelActionRole.Primary)
                ?? (workspace.AnswerInFlight ? new PanelAction(Sent, WorkspaceText.Sent, PanelActionRole.Primary, available: false) : null);
            // Hold to talk beside Tell it, only where the bar has room for it.
            var secondaries = (deny != null ? 1 : 0) + (tell?.Role == PanelActionRole.Secondary ? 1 : 0);
            var hold = screen.Speak && !answering && tell != null && secondaries < ActionSet.MaxSecondary
                ? new PanelAction(HoldToTalk, VoiceText.HoldToTalk, PanelActionRole.Secondary, holds: true, icon: GlazeIcon.HoldToTalk)
                : null;
            model.Actions = new ActionSet(stop, deny, hold, tell, primary);
            if (!model.Actions.All.Any()) model.BarNote = WorkspaceText.WhyNoActions(workspace);
        }
    }
}
