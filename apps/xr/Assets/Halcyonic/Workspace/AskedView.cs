#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// An agent's question, as What do you need from me? answers it (ADR 0022): which prompt shows and
    /// its header, the agent's question wrapped over two lines, and, when it takes more, in parts;
    /// how it is answered; then the answers offered, two to a page, with Type an answer where typing
    /// is allowed. Previous and Next step through the question's parts and pages, then on to its next
    /// prompt. Pressing an answer chooses it or not (<see cref="QuestionDraft"/>); nothing here sends:
    /// Send answer is an action of the workspace. A question Halcyonic cannot answer shows why, that
    /// the agent waits, and nothing to press but the workspace's Stop the turn. Every word from the
    /// agent shows by the one rule for text Halcyonic did not write.
    /// </summary>
    public sealed class AskedView : MonoBehaviour
    {
        private const float Pitch = 0.024f;
        private const float HeadingHeight = 0.032f;
        private const float RowHeight = 0.04f;
        private const float RowGap = 0.005f;
        private const float ButtonGap = 0.012f;
        private const int TextLines = 2;
        private const int RowsPerPage = 2;

        private const float SpeakWidth = 0.2f;

        private readonly List<PanelButton> rows = new List<PanelButton>();
        private PanelButton speak = null!;
        private bool speakOffered;
        private TextMeshPro heading = null!;
        private TextMeshPro text = null!;
        private TextMeshPro how = null!;
        private TextMeshPro cannot = null!;
        private PanelButton previous = null!;
        private PanelButton next = null!;
        private Func<bool> accepting = () => true;
        private QuestionDraft? draft;
        private string lead = "";
        private int prompt;
        private int step;
        private int textParts = 1;

        /// <summary>The person pressed Type an answer for a prompt.</summary>
        public event Action<int>? TypeRequested;

        /// <summary>Hold to talk was held long enough for a prompt's typed answer: start listening (ADR 0021).</summary>
        public event Action<int>? SpeakStarted;

        /// <summary>The hold ended: let go (true) or dropped (false).</summary>
        public event Action<bool>? SpeakEnded;

        /// <summary>Hold to talk was only tapped.</summary>
        public event Action? SpeakTapped;

        /// <summary>The person chose or unchose an answer, or turned to another part.</summary>
        public event Action? Changed;

        /// <summary>The prompt showing, from 0.</summary>
        public int Prompt => prompt;

        /// <summary>Every label it draws, for the editor's checks.</summary>
        public IEnumerable<Component> Shown
        {
            get
            {
                yield return heading;
                if (text.gameObject.activeSelf) yield return text;
                if (how.gameObject.activeSelf) yield return how;
                if (cannot.gameObject.activeSelf) yield return cannot;
                foreach (var row in rows)
                {
                    if (row.gameObject.activeSelf) yield return row;
                }
                if (speak.gameObject.activeSelf) yield return speak;
                if (previous.gameObject.activeSelf) yield return previous;
                if (next.gameObject.activeSelf) yield return next;
            }
        }

        /// <summary>The label that shows the agent's question, for the editor's checks.</summary>
        public TextMeshPro Text => text;

        public static AskedView Create(Transform panel, Func<bool> accepting)
        {
            var go = new GameObject("Question");
            go.transform.SetParent(panel, false);
            var view = go.AddComponent<AskedView>();
            view.accepting = accepting;
            view.Build();
            go.SetActive(false);
            return view;
        }

        /// <summary>
        /// Shows <paramref name="answering"/>'s question under <paramref name="questionLead"/>, from its
        /// first prompt when it is another draft, else where the person was.
        /// </summary>
        /// <param name="voice">Hold to talk is offered beside Type an answer, in development builds only.</param>
        public void Show(QuestionDraft answering, string questionLead, bool voice = false)
        {
            speakOffered = voice;
            // Another draft is another question, or the same asked again: it starts from its first prompt.
            if (!ReferenceEquals(draft, answering))
            {
                prompt = 0;
                step = 0;
            }
            draft = answering;
            prompt = Mathf.Clamp(prompt, 0, Mathf.Max(0, answering.Prompts.Count - 1));
            lead = questionLead;
            Layout();
        }

        private void Build()
        {
            heading = WorkspaceVisuals.Text(transform, "Heading", WorkspaceVisuals.DetailSize, WorkspaceVisuals.AttentionColor,
                new Vector2(WorkspacePanel.DetailsWidth, HeadingHeight), TextAlignmentOptions.MidlineLeft, order: WorkspaceVisuals.PanelTextOrder);
            text = WorkspaceVisuals.Text(transform, "Question text", WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor,
                new Vector2(WorkspacePanel.DetailsWidth, TextLines * Pitch), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            // Parts, never an ellipsis: what does not fit shows on the next part.
            text.overflowMode = TextOverflowModes.Page;
            how = WorkspaceVisuals.Text(transform, "How to answer", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor,
                new Vector2(WorkspacePanel.DetailsWidth, Pitch), TextAlignmentOptions.TopLeft, order: WorkspaceVisuals.PanelTextOrder);
            cannot = WorkspaceVisuals.Text(transform, "Cannot answer", WorkspaceVisuals.DetailSize, WorkspaceVisuals.AttentionColor,
                new Vector2(WorkspacePanel.DetailsWidth, 3 * Pitch), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            speak = PanelButton.Create(transform, "Hold to talk", RowHeight, WorkspaceVisuals.DetailSize);
            speak.Accepting = accepting;
            speak.Holds = true;
            speak.HoldStarted += () => SpeakStarted?.Invoke(prompt);
            speak.HoldEnded += sent => SpeakEnded?.Invoke(sent);
            speak.Pressed += () => SpeakTapped?.Invoke();
            previous = Button("Previous", () => Turn(-1));
            next = Button("Next", () => Turn(1));
            for (var index = 0; index < RowsPerPage; index++)
            {
                var row = index;
                var button = PanelButton.Create(transform, "Answer " + index, RowHeight, WorkspaceVisuals.DetailSize);
                button.Accepting = accepting;
                button.Pressed += () => Press(row);
                rows.Add(button);
            }
        }

        private PanelButton Button(string name, Action pressed)
        {
            var button = PanelButton.Create(transform, name, HeadingHeight, WorkspaceVisuals.CaptionSize);
            button.Accepting = accepting;
            button.Pressed += pressed;
            return button;
        }

        /// <summary>What a prompt offers, in rows: its labels, then Type an answer where typing is allowed.</summary>
        private int RowCount(int index)
        {
            var asked = draft!.Prompts[index];
            return asked.Options.Count + (asked.FreeText ? 1 : 0);
        }

        /// <summary>Pages of answers; a question Halcyonic cannot answer shows none.</summary>
        private int OptionPages(int index) => !draft!.Question.Answerable ? 1 : Mathf.Max(1, (RowCount(index) + RowsPerPage - 1) / RowsPerPage);

        /// <summary>The steps of the prompt showing: its text's parts, then its remaining pages of answers.</summary>
        private int Steps => textParts + OptionPages(prompt) - 1;

        /// <summary>Steps on or back as Next and Previous do, for the editor's renders.</summary>
        public void TurnForRender(int by) => Turn(by);

        /// <summary>How many steps the prompt showing takes, for the editor's checks.</summary>
        public int StepCount => Steps;

        private void Turn(int by)
        {
            if (draft == null) return;
            step += by;
            if (step >= Steps && prompt < draft.Prompts.Count - 1)
            {
                prompt++;
                step = 0;
            }
            else if (step < 0 && prompt > 0)
            {
                prompt--;
                step = int.MaxValue;
            }
            Layout();
            Changed?.Invoke();
        }

        private void Press(int row)
        {
            if (draft == null || !draft.Question.Answerable) return;
            var index = OptionPage() * RowsPerPage + row;
            var asked = draft.Prompts[prompt];
            if (index < asked.Options.Count) draft.Choose(prompt, asked.Options[index].Label);
            else TypeRequested?.Invoke(prompt);
            Layout();
            Changed?.Invoke();
        }

        private int OptionPage() => Mathf.Max(0, step - (textParts - 1));

        private void Layout()
        {
            if (draft == null) return;
            var asked = draft.Prompts[prompt];
            var top = WorkspacePanel.DetailsTop;
            var right = WorkspacePanel.DetailsLeft + WorkspacePanel.DetailsWidth;

            // The agent's question, measured into parts of two lines.
            WorkspaceVisuals.SetLiteral(text, asked.Text);
            text.pageToDisplay = 1;
            text.ForceMeshUpdate(true);
            textParts = Mathf.Max(1, text.textInfo.pageCount);
            step = Mathf.Clamp(step, 0, Steps - 1);
            var part = Mathf.Min(step, textParts - 1);
            text.pageToDisplay = part + 1;
            // Seen to its end once its last part has shown.
            if (part == textParts - 1) draft.ShownWhole(prompt);

            var headingText = WorkspaceText.PromptHeading(draft.Question, prompt)
                + (Steps > 1 ? " · page " + (step + 1) + " of " + Steps : "");
            var moreAfter = step < Steps - 1 || prompt < draft.Prompts.Count - 1;
            var moreBefore = step > 0 || prompt > 0;
            var nextWidth = next.Measure("Next", 0.1f);
            var previousWidth = previous.Measure("Previous", 0.1f);
            var center = top - HeadingHeight / 2f;
            if (moreAfter) next.Show("Next", new Vector2(right - nextWidth / 2f, center), nextWidth);
            else next.Hide();
            var previousRight = right - nextWidth - ButtonGap;
            if (moreBefore) previous.Show("Previous", new Vector2(previousRight - previousWidth / 2f, center), previousWidth);
            else previous.Hide();
            heading.rectTransform.sizeDelta = new Vector2(previousRight - previousWidth - ButtonGap - WorkspacePanel.DetailsLeft, HeadingHeight);
            heading.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, top, -0.001f);
            WorkspaceVisuals.SetLiteral(heading, headingText);
            top -= HeadingHeight + 0.003f;

            text.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, top, -0.001f);
            top -= TextLines * Pitch + 0.004f;

            if (!draft.Question.Answerable)
            {
                how.gameObject.SetActive(false);
                foreach (var row in rows) row.Hide();
                speak.Hide();
                cannot.gameObject.SetActive(true);
                cannot.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, top - 0.008f, -0.001f);
                WorkspaceVisuals.SetLiteralLines(cannot, new[] { WorkspaceText.CannotAnswer(draft.Question), WorkspaceText.AgentWaits });
                return;
            }
            cannot.gameObject.SetActive(false);
            how.gameObject.SetActive(true);
            how.rectTransform.localPosition = new Vector3(WorkspacePanel.DetailsLeft, top, -0.001f);
            // How to answer, then how many questions show, where the full width has room for both.
            WorkspaceVisuals.SetLiteral(how, WorkspaceText.PromptHow(asked) + (lead.Length == 0 ? "" : " " + lead));
            top -= Pitch - 0.002f;

            var first = OptionPage() * RowsPerPage;
            speak.Hide();
            for (var row = 0; row < RowsPerPage; row++)
            {
                var index = first + row;
                var button = rows[row];
                if (index >= RowCount(prompt))
                {
                    button.Hide();
                    continue;
                }
                var y = top - RowHeight / 2f - row * (RowHeight + RowGap);
                var position = new Vector2(WorkspacePanel.DetailsLeft + WorkspacePanel.DetailsWidth / 2f, y);
                if (index < asked.Options.Count)
                {
                    var option = asked.Options[index];
                    var chosen = draft.IsChosen(prompt, option.Label);
                    var description = string.IsNullOrWhiteSpace(option.Description) ? null : WorkspaceText.OneLine(option.Description!);
                    // The label whole; its description after it, cut short only where it must be.
                    // Chosen in words as well as in color.
                    var label = (chosen ? "Chosen: " : "") + WorkspaceText.OneLine(option.Label) + (description == null ? "" : " · " + description);
                    button.Show(label, position, WorkspacePanel.DetailsWidth, confirm: chosen);
                }
                else
                {
                    // Typing, and beside it hold to talk where this build has it: either only drafts the answer.
                    var typed = draft.Typed(prompt);
                    var width = speakOffered ? WorkspacePanel.DetailsWidth - SpeakWidth - ButtonGap : WorkspacePanel.DetailsWidth;
                    button.Show(WorkspaceText.TypedLabel(typed), new Vector2(WorkspacePanel.DetailsLeft + width / 2f, y), width, confirm: typed != null);
                    if (speakOffered) speak.Show(VoiceText.HoldToTalk, new Vector2(right - SpeakWidth / 2f, y), SpeakWidth);
                }
            }
        }
    }
}
