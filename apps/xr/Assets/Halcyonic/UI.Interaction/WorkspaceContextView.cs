#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>Actual project choices and selected work identity, independently placed beside reading.</summary>
    public sealed class WorkspaceContextView : MonoBehaviour
    {
        public const float WidthDegrees = 18f;
        private Surface face = null!;
        private TextMeshPro heading = null!, connection = null!;
        private readonly List<GlazeButton> projects = new List<GlazeButton>();
        private readonly List<string> projectIds = new List<string>();
        public IReadOnlyList<(string ProjectId, GlazeButton Button)> ProjectChoices => projects.Select((button, index) => (ProjectId: projectIds[index], Button: button)).Where(choice => choice.Button.gameObject.activeSelf).ToArray();
        public Surface ReadingSurface => face;
        public GlazeButton WorkspaceTarget => workspace;
        public GlazeButton TasksTarget => tasks;
        public GlazeButton MoreProjectsTarget => more;
        public TextMeshPro ConnectionLabel => connection;
        private GlazeButton more = null!, tasks = null!, workspace = null!;
        private object? binding;
        public object? Binding => binding;
        public Vector2 Size { get; private set; }
        public IReadOnlyList<GlazeButton> Targets => projects.Concat(new[] { more, tasks, workspace }).Where(button => button.gameObject.activeSelf).ToArray();
        public Func<bool> Accepting { get; set; } = () => false;
        public event Action<string, object>? ProjectPressed;
        public event Action<MenuPlace, object>? PlacePressed;
        public event Action<object>? WorkspacePressed;
        private static float U(float degrees) => GlazeTokens.Units(degrees);

        public static WorkspaceContextView Create(Transform parent)
        {
            var root = new GameObject("Project and work context"); root.transform.SetParent(parent, false);
            var view = root.AddComponent<WorkspaceContextView>();
            view.face = Surface.Create(root.transform, "Context reading surface", 16);
            view.heading = GlazeText.Create(root.transform, "Projects heading", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.TopLeft, 23);
            view.connection = GlazeText.Create(root.transform, "Current connection state", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.TopLeft, 23);
            view.more = view.Button("More projects"); view.tasks = view.Button("Tasks"); view.workspace = view.Button("Workspace");
            view.more.Pressed += () => { if (view.binding is object owner) view.PlacePressed?.Invoke(MenuPlace.Projects, owner); };
            view.tasks.Pressed += () => { if (view.binding is object owner) view.PlacePressed?.Invoke(MenuPlace.Tasks, owner); };
            view.workspace.Pressed += () => { if (view.binding is object owner) view.WorkspacePressed?.Invoke(owner); };
            root.SetActive(false); return view;
        }

        private GlazeButton Button(string name)
        {
            var button = GlazeButton.Create(transform, name, ButtonRole.Navigation, order: 23);
            button.Accepting = () => Accepting();
            button.BoundedPrompt = true;
            return button;
        }

        public float HeightFor(int projectCount)
        {
            var gap = MenuPage.TargetGap;
            var row = Mathf.Max(U(Glaze.TargetDegrees), 2f * GlazeText.LineHeight(heading) + U(0.75f));
            return 2f * GlazeText.LineHeight(heading) + MenuPage.Grid + gap + Math.Min(3, projectCount) * (row + gap)
                + (projectCount > 3 ? 3f : 2f) * (GlazeButton.HeightOf(false) + gap) + U(0.75f);
        }

        public void Show(IReadOnlyList<ProjectView> choices, string? selectedProject, object owner, string connectionState)
        {
            if (!ReferenceEquals(binding, owner))
                foreach (var target in Targets) { target.EndPressSince(float.MinValue); target.Unsettle(); }
            binding = owner;
            var width = U(WidthDegrees); var gap = MenuPage.TargetGap; var padding = U(0.75f);
            var inner = width - 2f * padding; var y = 0f;
            GlazeText.SetLiteral(heading, "Projects"); GlazeText.Lay(heading, inner, 1);
            heading.rectTransform.pivot = new Vector2(0f, 1f); heading.transform.localPosition = new Vector3(-inner / 2f, y, -0.001f);
            y -= GlazeText.LineHeight(heading) + MenuPage.Grid;
            GlazeText.SetLiteral(connection, connectionState); GlazeText.Lay(connection, inner, 1);
            connection.rectTransform.pivot = new Vector2(0f, 1f); connection.transform.localPosition = new Vector3(-inner / 2f, y, -0.001f);
            y -= GlazeText.LineHeight(connection) + gap;
            var shown = choices.Take(3).ToList();
            while (projects.Count < shown.Count)
            {
                var index = projects.Count; var button = Button("Project choice " + index); projects.Add(button); projectIds.Add("");
                button.Pressed += () =>
                {
                    // Each rendered button carries its actual offered ID and current binding.
                    if (binding is object current && button.gameObject.activeSelf)
                        ProjectPressed?.Invoke(projectIds[index], current);
                };
            }
            for (var index = 0; index < projects.Count; index++)
            {
                var button = projects[index];
                if (index >= shown.Count) { button.gameObject.SetActive(false); continue; }
                var project = shown[index]; projectIds[index] = project.ProjectId; button.On = project.ProjectId == selectedProject;
                var height = Mathf.Max(U(Glaze.TargetDegrees), 2f * GlazeText.LineHeight(button.Label) + U(0.75f));
                button.ShowArea(new Vector2(0f, y - height / 2f), new Vector2(inner, height));
                button.Label.gameObject.SetActive(true); button.Label.rectTransform.pivot = new Vector2(0f, 1f);
                GlazeRuns.Of(button.Label).SetIdentity(project.Name, text => GlazeText.Lay(button.Label, inner - U(1f), int.MaxValue).Lines <= 2);
                GlazeText.Lay(button.Label, inner - U(1f), 2);
                button.Label.transform.localPosition = new Vector3(-inner / 2f + U(0.5f), GlazeText.LineHeight(button.Label), -0.001f);
                y -= height + gap;
            }
            void Control(GlazeButton button, string text)
            {
                button.Show(text, new Vector2(0f, y - GlazeButton.HeightOf(false) / 2f), inner);
                y -= GlazeButton.HeightOf(false) + gap;
            }
            more.gameObject.SetActive(choices.Count > 3);
            if (choices.Count > 3) Control(more, "More projects");
            Control(tasks, "Tasks"); Control(workspace, "Workspace");
            Size = new Vector2(width, -y + padding);
            var offset = new Vector3(0f, Size.y / 2f - padding, 0f);
            foreach (Transform child in transform) if (child != face.transform) child.localPosition += offset;
            face.DrawCoatedInstrument(Size, U(Glaze.Menu.RadiusDegrees), GlazeTokens.ColorOf(Glaze.Palette.Raised));
            face.transform.localPosition = Vector3.zero;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            binding = null;
            foreach (var target in Targets) target.EndPressSince(float.MinValue);
            gameObject.SetActive(false);
        }
    }
}
