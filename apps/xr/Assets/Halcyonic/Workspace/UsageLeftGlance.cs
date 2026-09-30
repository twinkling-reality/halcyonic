#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The optional Usage left glance: a chip in the room the project rail leaves at its lower row's
    /// right end (<see cref="ProjectRail.UsageLeftRoom"/>), and, when pressed, a small panel above the
    /// rail with the provider limits the Mac last saw. It reads the control plane only when opened or
    /// when Read again is pressed, never on its own, and shows nothing that belongs to a Workstream.
    /// </summary>
    /// <remarks>
    /// It lives on the rail, so it steps away with it while the entry panel or a workspace is open, and
    /// it closes then too: one foreground surface at a time. While the app lacks focus, as when a 2D
    /// window has it, the chip and the panel hide and the panel closes, so returning is deliberate.
    /// Without a rail it shows nothing.
    /// </remarks>
    public sealed class UsageLeftGlance : MonoBehaviour
    {
        private const string Label = "Usage left";
        private const float PanelWidth = ProjectRail.RailWidth;
        private const float Padding = 0.03f;
        private const float ButtonHeight = 0.06f;

        private ControlPlaneConnection? connection;
        private ProjectRail? rail;
        private PanelButton chip = null!;
        private GameObject panel = null!;
        private SpriteRenderer plate = null!;
        private TextMeshPro body = null!;
        private TextMeshPro note = null!;
        private PanelButton again = null!;
        private CancellationTokenSource? cancellation;
        private Task<UsageLimitsResponse>? read;
        private UsageLeftPresentation? shown;
        private UsageLimitsResponse? answer;
        private bool open;
        private bool built;
        private float nextLayout;

        /// <summary>The chip, for the editor's renders.</summary>
        public PanelButton Chip => chip;

        /// <summary>Whether the panel is open.</summary>
        public bool Open => open;

        /// <summary>
        /// Builds a glance on <paramref name="projectRail"/> that shows <paramref name="presentation"/>
        /// as it is, open, for the editor's renders: nothing reads the control plane.
        /// </summary>
        public static UsageLeftGlance ForRender(ProjectRail projectRail, UsageLeftPresentation presentation)
        {
            var glance = projectRail.gameObject.AddComponent<UsageLeftGlance>();
            glance.rail = projectRail;
            glance.Build();
            glance.open = true;
            glance.shown = presentation;
            glance.Layout();
            return glance;
        }

        private void Awake() => connection = GetComponent<ControlPlaneConnection>();

        private void OnDestroy() => Cancel();

        private void Update()
        {
            if (!built)
            {
                // The workspace director adds the rail in its Start; without one there is no glance.
                if (connection == null || (rail = GetComponent<ProjectRail>()) == null) return;
                Build();
            }
            var railShown = rail!.Root.gameObject.activeInHierarchy;
            if (FocusGuard.InputSuspended || !railShown)
            {
                if (open) Close();
                if (chip.gameObject.activeSelf) chip.Hide();
                return;
            }
            if (!chip.gameObject.activeSelf) Layout();
            if (read != null && read.IsCompleted) Finish();
            // A window can pass its reset while the panel is open; it then goes, rather than stay wrong.
            if (open && Time.unscaledTime >= nextLayout) Layout();
        }

        private void Build()
        {
            var root = rail!.Root;
            chip = PanelButton.Create(root, Label, ProjectRail.ChipHeight, ProjectRail.ChipText);
            chip.Accepting = () => !FocusGuard.InputSuspended;
            chip.Pressed += Toggle;
            panel = new GameObject("Usage left panel");
            panel.transform.SetParent(root, false);
            plate = WorkspaceVisuals.Plate(panel.transform, "Background", new Vector2(PanelWidth, 0.2f),
                WorkspaceVisuals.PanelColor, WorkspaceVisuals.PanelPlateOrder);
            body = WorkspaceVisuals.Text(panel.transform, "Readings", WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor,
                new Vector2(PanelWidth - 2 * Padding, 1f), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            note = WorkspaceVisuals.Text(panel.transform, "Note", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor,
                new Vector2(PanelWidth - 2 * Padding, 1f), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            again = PanelButton.Create(panel.transform, "Read again", ButtonHeight, WorkspaceVisuals.CaptionSize);
            again.Accepting = () => !FocusGuard.InputSuspended && read == null;
            again.Pressed += Read;
            panel.SetActive(false);
            built = true;
            Layout();
        }

        private void Toggle()
        {
            if (FocusGuard.InputSuspended) return;
            if (open)
            {
                Close();
                return;
            }
            open = true;
            Read();
        }

        private void Close()
        {
            open = false;
            Cancel();
            answer = null;
            shown = null;
            Layout();
        }

        private void Cancel()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            cancellation = null;
            read = null;
        }

        private void Read()
        {
            if (!open || FocusGuard.InputSuspended || read != null || connection == null) return;
            answer = null;
            var api = connection.DemonstrationLine != null ? null : ControlPlaneSettings.Api();
            if (api == null)
            {
                shown = UsageLeftPresenter.Message(connection.DemonstrationLine != null
                    ? "Usage left isn't part of the demonstration."
                    : UsageLeftPresenter.NotSetUp);
                Layout();
                return;
            }
            cancellation = new CancellationTokenSource();
            shown = UsageLeftPresenter.Message(UsageLeftPresenter.Reading);
            read = api.GetUsageLimitsAsync(cancellation.Token);
            Layout();
        }

        private void Finish()
        {
            var finished = read!;
            read = null;
            cancellation?.Dispose();
            cancellation = null;
            if (finished.IsCanceled) return;
            if (finished.IsFaulted)
            {
                shown = UsageLeftPresenter.Unreachable();
            }
            else
            {
                answer = finished.Result;
            }
            Layout();
        }

        /// <summary>The chip in the rail's free room; the panel, when open, above the rail at its right edge.</summary>
        private void Layout()
        {
            nextLayout = Time.unscaledTime + 15f;
            var room = ProjectRail.UsageLeftRoom - ProjectRail.Gap;
            var lower = -(ProjectRail.ChipHeight / 2f + ProjectRail.Gap / 2f);
            chip.Show(Label, new Vector2(ProjectRail.RailWidth / 2f - room / 2f, lower), room);
            panel.SetActive(open);
            if (!open) return;

            if (answer != null) shown = UsageLeftPresenter.Present(answer, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            var presentation = shown ?? UsageLeftPresenter.Message(UsageLeftPresenter.Reading);
            var lines = new List<string>();
            foreach (var row in presentation.Rows)
            {
                lines.Add(row.Title);
                lines.Add(row.Text);
            }
            var width = PanelWidth - 2 * Padding;
            body.gameObject.SetActive(lines.Count > 0);
            var bodyHeight = 0f;
            if (lines.Count > 0)
            {
                WorkspaceVisuals.SetLiteralLines(body, lines);
                bodyHeight = body.GetPreferredValues(body.text, width, 0f).y;
            }
            note.color = presentation.Problem ? WorkspaceVisuals.ProblemColor : WorkspaceVisuals.SecondaryColor;
            WorkspaceVisuals.SetLiteral(note, presentation.Note);
            var noteHeight = note.GetPreferredValues(note.text, width, 0f).y;
            var gap = lines.Count > 0 ? Padding / 2f : 0f;
            var height = Padding + bodyHeight + gap + noteHeight + Padding / 2f + ButtonHeight + Padding;

            // Above the rail's upper row, its right edge on the rail's.
            var bottom = ProjectRail.ChipHeight + ProjectRail.Gap * 2f;
            panel.transform.localPosition = new Vector3(0f, bottom + height / 2f, 0f);
            plate.size = new Vector2(PanelWidth, height);
            var target = panel.GetComponent<PointerTarget>();
            if (target == null) PointerTarget.Rectangle(panel, new Vector2(PanelWidth, height), ray: true, poke: false);
            else target.Resize(new Vector2(PanelWidth, height));
            var top = height / 2f - Padding;
            var left = -PanelWidth / 2f + Padding;
            body.rectTransform.sizeDelta = new Vector2(width, bodyHeight);
            body.rectTransform.localPosition = new Vector3(left, top, -0.003f);
            note.rectTransform.sizeDelta = new Vector2(width, noteHeight);
            note.rectTransform.localPosition = new Vector3(left, top - bodyHeight - gap, -0.003f);
            var buttonWidth = again.Measure("Read again", 0.16f);
            again.Show("Read again", new Vector2(PanelWidth / 2f - Padding - buttonWidth / 2f, -height / 2f + Padding + ButtonHeight / 2f), buttonWidth);
        }
    }
}
