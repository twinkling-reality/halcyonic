#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>The words of the first question (ADR 0026, WORDS.md), as the owner saw them on lane V's page.</summary>
    public static class FirstQuestionText
    {
        /// <summary>The question, Projects' own subject, which Projects keeps when it opens from here.</summary>
        public const string Subject = ProjectsText.Subject;

        public const string SomethingNew = "Something new";
        public const string OnYourComputer = "A project on " + HostText.Your;
        public const string StartAProject = "Start a project";
        public const string ShowMyProjects = "Show my projects";
    }

    /// <summary>The first question's two rows, one always chosen.</summary>
    public enum FirstAnswer
    {
        SomethingNew,
        OnYourComputer,
    }

    /// <summary>
    /// The first question's one plate (ADR 0026): "What would you like to work on?" with two rows,
    /// Something new, chosen as it opens, and A project on your computer. The chosen row sets the main
    /// action, Start a project or Show my projects, and Hold to talk stands beside Start a project only,
    /// so the person can say the idea straight away; with the other row chosen it is gone, so nothing
    /// is drawn quiet. Both rows choose; neither opens a side panel, so neither shows a chevron.
    /// </summary>
    public static class FirstQuestionScreens
    {
        /// <summary>What the rows and prompts raise.</summary>
        public const string Choose = "first-choose";
        public const string StartProject = "first-start-project";
        public const string ShowProjects = "first-show-projects";
        public const string HoldToTalk = "first-hold-to-talk";

        /// <param name="voice">Hold to talk may show (<see cref="IMenuHost.VoiceOffered"/>).</param>
        /// <param name="said">Hold to talk's own line, as why nothing came of it; null for none.</param>
        public static MenuFrame Question(FirstAnswer chosen, bool voice, string? said)
        {
            var lines = new List<PageLine>
            {
                new PageLine(FirstQuestionText.SomethingNew, icon: GlazeIcon.CreateProject, action: Choose, key: nameof(FirstAnswer.SomethingNew),
                    chosen: chosen == FirstAnswer.SomethingNew),
                new PageLine(FirstQuestionText.OnYourComputer, icon: GlazeIcon.Folder, action: Choose, key: nameof(FirstAnswer.OnYourComputer),
                    chosen: chosen == FirstAnswer.OnYourComputer),
            };
            if (said != null && chosen == FirstAnswer.SomethingNew) lines.Add(new PageLine(said, tone: LineTone.Secondary, rows: 2));
            var close = new Prompt(Footer.Close, ProjectsText.Close, GlazeIcon.Close, PromptKind.Close);
            var footer = chosen == FirstAnswer.SomethingNew
                ? new Footer(close, secondary: voice ? new Prompt(HoldToTalk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true) : null,
                    farRight: new Prompt(StartProject, FirstQuestionText.StartAProject, GlazeIcon.CreateProject, main: true))
                : new Footer(close, farRight: new Prompt(ShowProjects, FirstQuestionText.ShowMyProjects, GlazeIcon.Folder, main: true));
            return new MenuFrame(FirstQuestionText.Subject, footer, lines: lines);
        }

        /// <summary>
        /// Whether a press of <paramref name="id"/> with <paramref name="key"/> acts on <paramref name="frame"/>:
        /// only a row or a prompt it shows, available, so a press from a frame no longer standing does nothing.
        /// </summary>
        public static bool Allows(MenuFrame frame, string id, string? key) =>
            id == Choose
                ? frame.Lines.Any(line => line.Action == Choose && line.Key == key && line.Pressable)
                : frame.Footer.All.Any(each => each.Prompt.Id == id && each.Prompt.Available);
    }

    /// <summary>
    /// The first question as the menu's column (ADR 0026, <see cref="IMenuColumn"/>), shown in the
    /// menu's place with no places until the computer's first task. It sends nothing: Start a project
    /// opens New project in its place, with any words Hold to talk heard as the idea, and Show my
    /// projects opens Projects in its place. Close closes the menu to its bar.
    /// </summary>
    public sealed class FirstQuestionColumn : IMenuColumn
    {
        private readonly IMenuHost host;
        private readonly Action<string?> startProject;
        private readonly Action showProjects;
        private FirstAnswer chosen = FirstAnswer.SomethingNew;
        private string? said;
        private bool voice;
        private bool closed;
        private MenuFrame? frame;

        /// <param name="startProject">Opens New project in the question's place, with the words Hold to talk heard as the idea, or none.</param>
        /// <param name="showProjects">Opens Projects in the question's place.</param>
        public FirstQuestionColumn(IMenuHost host, Action<string?> startProject, Action showProjects)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.startProject = startProject ?? throw new ArgumentNullException(nameof(startProject));
            this.showProjects = showProjects ?? throw new ArgumentNullException(nameof(showProjects));
            voice = host.VoiceOffered;
        }

        public event Action? Changed;

        public event Action? Closed;

        public MenuFrame? Frame => frame ??= FirstQuestionScreens.Question(chosen, voice, said);

        /// <summary>The row chosen now.</summary>
        public FirstAnswer Chosen => chosen;

        public void Act(string id, string? key)
        {
            if (closed || !(Frame is MenuFrame shown) || !FirstQuestionScreens.Allows(shown, id, key)) return;
            switch (id)
            {
                case Footer.Close:
                    closed = true;
                    Closed?.Invoke();
                    return;
                case FirstQuestionScreens.Choose when Enum.TryParse<FirstAnswer>(key, out var answer) && answer != chosen:
                    chosen = answer;
                    said = null;
                    Redraw();
                    return;
                case FirstQuestionScreens.StartProject:
                    startProject(null);
                    return;
                case FirstQuestionScreens.ShowProjects:
                    showProjects();
                    return;
            }
        }

        public void Drawn(MenuFrame drawn, Footer? sidePanel)
        {
            // Nothing here counts as read only once it has shown.
        }

        public void HoldStarted(string id)
        {
            // The director's one voice records; where it stands shows on Hold to talk itself.
        }

        public void HoldEnded(string id, bool letGo)
        {
        }

        /// <summary>Hold to talk: the idea, said straight away, opens New project with it as the person's own words, to check before going on.</summary>
        public void Heard(string text)
        {
            if (closed || chosen != FirstAnswer.SomethingNew || text.Trim().Length == 0) return;
            startProject(text);
        }

        public void Said(string words)
        {
            // Listening and writing down show on Hold to talk itself (ADR 0027); a line for them would grow the page under the hand.
            if (closed || words == VoiceText.Listening || words == VoiceText.Hearing) return;
            said = words;
            Redraw();
        }

        public void Tick()
        {
            if (closed || host.VoiceOffered == voice) return;
            voice = host.VoiceOffered;
            Redraw();
        }

        public void FocusLeft()
        {
            // The question arms nothing.
        }

        private void Redraw()
        {
            frame = null;
            Changed?.Invoke();
        }
    }
}
