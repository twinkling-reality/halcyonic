#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The workspace's details, chosen with the person's questions as tabs under its actions: What is
    /// it doing?, the requests and recent activity the panel shows; Help me understand, what the
    /// understanding source concluded about the execution; What was checked?, what the evaluation
    /// source measured about it; and, only while a request waits, What do you need from me?, the
    /// request and what each answer does (<see cref="WorkspaceText.NeedFromYou"/>). Each question
    /// shows whole, in two lines on its tab. The source's name stays in the answer's provenance line,
    /// never on a tab. The tabs are the workspace's buttons, pointed at and pinched, or poked, and
    /// ignore input while the app lacks focus. A workspace opens on the request when one waits. A section reads when it is shown for an execution it holds nothing about, and again
    /// when the person presses Refresh. Understanding also follows the execution as it changes, at
    /// most every two seconds; Evaluation, whose reads spend the evaluation source's budget, never
    /// reads by itself again. Reads go to the control plane, or to the recorded demonstration while
    /// it plays, through <see cref="IIntelligenceReader"/>; the client core writes every word. Acting
    /// from the workspace returns the details to Activity, where the request's result shows. While an
    /// approval or denial waits for its confirmation, the tab row and the details show the whole
    /// request it answers instead (<see cref="RequestView"/>).
    /// </summary>
    public sealed class WorkspaceSections : MonoBehaviour
    {
        private const float TabGap = 0.016f;

        /// <summary>A tab's margin beside its words: four whole questions and Refresh share one row.</summary>
        private const float TabMargin = 0.03f;
        private const float FollowSeconds = 2f;

        /// <summary>How often a section is written again anyway, since "2 minutes ago" and staleness move with time.</summary>
        private const float RedrawSeconds = 1f;

        /// <summary>Lines under the provenance that fit the details area (<see cref="SectionView"/>).</summary>
        public const int UnderstandingLines = 7;

        private static readonly TimeZoneInfo Zone = LocalZone();

        private WorkspacePanel panel = null!;
        private Func<WorkspacePresentation?> presentation = () => null;
        private Func<IIntelligenceReader?> reader = () => null;
        private readonly PanelButton[] tabs = new PanelButton[4];
        private PanelButton refresh = null!;
        private SpriteRenderer chosenMark = null!;
        private SectionView view = null!;
        private NeedView need = null!;
        private AskedView asking = null!;
        private Func<QuestionDraft?> draft = () => null;
        private Func<bool> voice = () => false;
        private RequestView request = null!;
        private IntelligenceFeed<UnderstandingResponse> understanding = null!;
        private IntelligenceFeed<EvaluationResponse> evaluation = null!;
        private WorkspaceQuestion question;
        private bool offersNeed;
        private int drawn = -1;
        private float redrawAt;
        private bool fixedContent;

        /// <summary>The question whose answer shows under the tabs.</summary>
        public WorkspaceQuestion Question => question;

        /// <summary>
        /// Adds the tabs and sections to a workspace panel. <paramref name="presentation"/> is the open
        /// workstream as last presented; <paramref name="reader"/> is where to read, null when there is
        /// nowhere to, as without a control plane or demonstration; <paramref name="first"/> is the
        /// question it opens on.
        /// </summary>
        /// <param name="questionDraft">The person's answers to the agent's question shown, or null while none is asked.</param>
        public static WorkspaceSections Attach(WorkspacePanel panel, Func<WorkspacePresentation?> presentation, Func<IIntelligenceReader?> reader,
            WorkspaceQuestion first = WorkspaceQuestion.Doing, Func<QuestionDraft?>? questionDraft = null, Func<bool>? speakAnswers = null)
        {
            var sections = panel.gameObject.AddComponent<WorkspaceSections>();
            sections.panel = panel;
            sections.presentation = presentation;
            sections.reader = reader;
            if (questionDraft != null) sections.draft = questionDraft;
            if (speakAnswers != null) sections.voice = speakAnswers;
            sections.offersNeed = first == WorkspaceQuestion.NeedFromYou;
            sections.Build(first);
            return sections;
        }

        /// <summary>Shows the answer to a question, reading a section if it holds nothing about the execution yet.</summary>
        public void Show(WorkspaceQuestion shown) => Choose(shown);

        /// <summary>Shows a section with content given as it is, reading nothing: for the editor's renders.</summary>
        public void ShowFixed(SectionPresentation section)
        {
            fixedContent = true;
            Choose(section.Kind == SectionKind.Understanding ? WorkspaceQuestion.Understand : WorkspaceQuestion.Checked);
            view.Show(section);
        }

        /// <summary>Shows What do you need from me? with lines given as they are: for the editor's renders.</summary>
        public void ShowFixedNeed(NeedAnswer answer)
        {
            fixedContent = true;
            draft = () => null;
            offersNeed = true;
            Choose(WorkspaceQuestion.NeedFromYou);
            need.Show(answer);
        }

        /// <summary>Shows an agent's question with the answers in <paramref name="answering"/>, reading nothing: for the editor's renders.</summary>
        public void ShowFixedQuestion(QuestionDraft answering, string lead, bool speak = false)
        {
            fixedContent = true;
            offersNeed = true;
            draft = () => answering;
            Choose(WorkspaceQuestion.NeedFromYou);
            asking.Show(answering, lead, speak);
        }

        /// <summary>The agent's question, for the editor's checks.</summary>
        public AskedView Asking => asking;

        /// <summary>The person asked to type an answer to a prompt of the question shown.</summary>
        public event Action<int>? TypeAnswer;

        /// <summary>Every label the sections draw, for the editor's check that none interprets what it shows.</summary>
        public SectionView View => view;

        /// <summary>The answer to What do you need from me?, for the editor's checks.</summary>
        public NeedView Need => need;

        /// <summary>The tab of each question offered now, for the editor's check that every question shows whole.</summary>
        public IEnumerable<PanelButton> Tabs
        {
            get
            {
                foreach (var tab in tabs)
                {
                    if (tab.gameObject.activeSelf) yield return tab;
                }
            }
        }

        /// <summary>The whole request an armed approval or denial answers, while it shows.</summary>
        public RequestView Request => request;

        /// <summary>Raised when the person turns to another part of the request.</summary>
        public event Action? RequestTurned;

        /// <summary>
        /// Shows the whole request in place of the tabs and the details, from its first part unless it
        /// shows already.
        /// </summary>
        public void ShowRequest(string text)
        {
            request.Show(text);
            Arrange();
        }

        /// <summary>Hides the request, if it shows, and returns to the activity.</summary>
        public void EndRequest()
        {
            if (!request.gameObject.activeSelf) return;
            request.Hide();
            Choose(WorkspaceQuestion.Doing);
        }

        private void Build(WorkspaceQuestion first)
        {
            understanding = new IntelligenceFeed<UnderstandingResponse>((id, cancel) => Reader().ReadUnderstandingAsync(id, cancel));
            evaluation = new IntelligenceFeed<EvaluationResponse>((id, cancel) => Reader().ReadEvaluationAsync(id, cancel));
            foreach (WorkspaceQuestion each in Enum.GetValues(typeof(WorkspaceQuestion)))
            {
                var asked = each;
                tabs[(int)asked] = Tab(WorkspaceText.Question(asked), () => Choose(asked));
            }
            refresh = Tab("Refresh", Refresh);
            // A shape as well as a color marks the tab chosen.
            chosenMark = WorkspaceVisuals.Plate(transform, "Chosen tab", new Vector2(0.1f, 0.004f), WorkspaceVisuals.TextColor, WorkspaceVisuals.PanelControlOrder);
            view = SectionView.Create(transform);
            need = NeedView.Create(transform);
            asking = AskedView.Create(transform, () => panel.Accepting());
            asking.TypeRequested += index => TypeAnswer?.Invoke(index);
            request = RequestView.Create(transform, () => panel.Accepting());
            request.Turned += () => RequestTurned?.Invoke();
            // Send answer keeps the question in view: its result shows there, or why nothing was sent.
            panel.ActionPressed += action =>
            {
                if (action != WorkspaceAction.Answer) Choose(WorkspaceQuestion.Doing);
            };
            panel.ConfirmPressed += () => Choose(WorkspaceQuestion.Doing);
            panel.PresetPressed += _ => Choose(WorkspaceQuestion.Doing);
            Choose(first);
        }

        private IIntelligenceReader Reader() =>
            reader() ?? throw new ControlPlaneRequestException("There is no control plane or demonstration to ask.");

        private PanelButton Tab(string name, Action pressed)
        {
            var button = PanelButton.Create(transform, name, WorkspacePanel.TabsHeight, WorkspaceVisuals.DetailSize);
            button.Accepting = () => panel.Accepting();
            button.Pressed += pressed;
            return button;
        }

        private void Choose(WorkspaceQuestion asked)
        {
            if (asked == WorkspaceQuestion.NeedFromYou && !offersNeed) asked = WorkspaceQuestion.Doing;
            question = asked;
            drawn = -1;
            Arrange();
        }

        /// <summary>The section a question reads, or null for the ones the workspace answers itself.</summary>
        private static SectionKind? KindOf(WorkspaceQuestion question) => question switch
        {
            WorkspaceQuestion.Understand => SectionKind.Understanding,
            WorkspaceQuestion.Checked => SectionKind.Evaluation,
            _ => null,
        };

        /// <summary>The tabs and the details they choose, or, while it shows, the whole request in their place.</summary>
        private void Arrange()
        {
            var reading = request.gameObject.activeSelf;
            var kind = KindOf(question);
            panel.ShowActivity(!reading && question == WorkspaceQuestion.Doing);
            view.gameObject.SetActive(!reading && kind != null);
            // An approval first, as the runtime blocks on it; else the agent's question.
            var current = presentation();
            var askingNow = draft() != null && current?.ApprovalToAnswer == null;
            need.gameObject.SetActive(!reading && question == WorkspaceQuestion.NeedFromYou && !askingNow);
            asking.gameObject.SetActive(!reading && question == WorkspaceQuestion.NeedFromYou && askingNow);
            chosenMark.gameObject.SetActive(!reading);
            var x = WorkspacePanel.DetailsLeft;
            var center = WorkspacePanel.TabsTop - WorkspacePanel.TabsHeight / 2f;
            foreach (WorkspaceQuestion each in Enum.GetValues(typeof(WorkspaceQuestion)))
            {
                var button = tabs[(int)each];
                if (reading || (each == WorkspaceQuestion.NeedFromYou && !offersNeed))
                {
                    button.Hide();
                    continue;
                }
                var lines = WorkspaceText.TabLines(each);
                var width = button.MeasureLines(lines, 0.1f, TabMargin);
                button.ShowLines(lines, new Vector2(x + width / 2f, center), width);
                if (each == question)
                {
                    chosenMark.size = new Vector2(width - 0.02f, 0.004f);
                    chosenMark.transform.localPosition = new Vector3(x + width / 2f, WorkspacePanel.TabsTop - WorkspacePanel.TabsHeight - 0.005f, -0.004f);
                }
                x += width + TabGap;
            }
            if (reading || kind == null)
            {
                refresh.Hide();
                return;
            }
            var refreshWidth = refresh.Measure("Refresh", 0.1f, TabMargin);
            refresh.Show("Refresh", new Vector2(WorkspacePanel.DetailsLeft + WorkspacePanel.DetailsWidth - refreshWidth / 2f, center), refreshWidth);
        }

        private void Refresh()
        {
            var execution = presentation()?.Execution;
            var now = DateTimeOffset.UtcNow;
            if (question == WorkspaceQuestion.Understand) understanding.Refresh(execution?.UpdatedAt, now);
            else if (question == WorkspaceQuestion.Checked) evaluation.Refresh(execution?.UpdatedAt, now);
        }

        private void Update()
        {
            if (fixedContent) return;
            var now = DateTimeOffset.UtcNow;
            var current = presentation();
            var execution = current?.Execution;
            if (current != null) FollowRequest(current);
            if (question == WorkspaceQuestion.Understand)
            {
                understanding.Show(execution?.ExecutionId, execution?.UpdatedAt, now, TimeSpan.FromSeconds(FollowSeconds));
            }
            else if (question == WorkspaceQuestion.Checked)
            {
                evaluation.Show(execution?.ExecutionId, execution?.UpdatedAt, now);
            }
            understanding.Poll();
            evaluation.Poll();
            if (question == WorkspaceQuestion.NeedFromYou && current != null && !request.gameObject.activeSelf && (drawn < 0 || Time.unscaledTime >= redrawAt))
            {
                drawn = 0;
                redrawAt = Time.unscaledTime + RedrawSeconds;
                if (WorkspaceText.NeedFromYou(current) is NeedAnswer answer) need.Show(answer);
                else if (draft() is QuestionDraft answering) asking.Show(answering, WorkspaceText.QuestionLead(current), voice());
                Arrange();
            }
            if (KindOf(question) is not SectionKind kind || request.gameObject.activeSelf) return;
            var version = kind == SectionKind.Understanding ? understanding.Version : evaluation.Version;
            if (version == drawn && Time.unscaledTime < redrawAt) return;
            drawn = version;
            redrawAt = Time.unscaledTime + RedrawSeconds;
            view.Show(kind == SectionKind.Understanding
                ? UnderstandingPresenter.Present(understanding, now, Zone, UnderstandingLines)
                : EvaluationPresenter.Present(evaluation, now, Zone));
        }

        /// <summary>
        /// Offers What do you need from me? while a real request waits, and only then: when the request
        /// is answered, the tab goes, and an answer showing returns to What is it doing?.
        /// </summary>
        private void FollowRequest(WorkspacePresentation current)
        {
            var waiting = WorkspaceText.SomethingWaits(current);
            if (waiting == offersNeed) return;
            offersNeed = waiting;
            if (!waiting && question == WorkspaceQuestion.NeedFromYou) Choose(WorkspaceQuestion.Doing);
            else Arrange();
        }

        private void OnDestroy()
        {
            understanding?.Clear();
            evaluation?.Clear();
        }

        private static TimeZoneInfo LocalZone()
        {
            try
            {
                return TimeZoneInfo.Local;
            }
            catch (Exception)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}
