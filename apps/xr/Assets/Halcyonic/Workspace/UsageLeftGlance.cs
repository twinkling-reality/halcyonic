#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The optional Usage left glance: a chip the project rail places at its lower row's right end
    /// (<see cref="ProjectRail.UsageLeft"/>) that opens a small panel with the provider
    /// limits the Mac last saw. It reads the control plane only when opened or when Read again is
    /// pressed, never on its own, and shows nothing that belongs to a Workstream.
    /// </summary>
    /// <remarks>
    /// The panel opens where the entry panel would (<see cref="WorkspaceLayout.PlaceForeground"/>),
    /// clear of every character, and the rail steps aside while it shows: one foreground surface at a
    /// time. It closes by its own Close or the chip, and when the entry panel or a workspace opens.
    /// While another window has focus its controls take no input; once focus stays away it folds,
    /// with what it read, and comes back as it was (<see cref="FocusGuard.Folded"/>).
    /// Without a rail it shows nothing.
    /// </remarks>
    public sealed class UsageLeftGlance : MonoBehaviour
    {
        private const string Label = "Usage left";
        /// <summary>Narrower than the workspace, whose space it opens in, and wide enough that a reading takes one line.</summary>
        private const float Width = 0.7f;
        private const float Padding = 0.03f;
        private const float ButtonHeight = 0.06f;

        /// <summary>Space between two readings.</summary>
        private const float ReadingGap = 0.015f;

        private readonly List<BodyInView> scratch = new List<BodyInView>();
        private ControlPlaneConnection? connection;
        private ProjectRail? rail;
        private EntryPanel? entry;
        private WorkspaceDirector? director;
        private CharacterStage? stage;
        private GlazeButton chip = null!;
        private Transform panel = null!;
        private SpriteRenderer plate = null!;
        private PointerTarget target = null!;
        private TextMeshPro title = null!;
        private readonly List<TextMeshPro> readings = new List<TextMeshPro>();
        private TextMeshPro note = null!;
        private PanelButton close = null!;
        private PanelButton again = null!;
        private CancellationTokenSource? cancellation;
        private Task<UsageLimitsResponse>? read;
        private UsageLeftPresentation? shown;
        private UsageLimitsResponse? answer;
        private Pose placed;
        private bool placedAbove;
        private bool open;
        private bool built;
        private float nextLayout;

        /// <summary>The chip on the rail, which the rail places and this answers, for the editor's renders.</summary>
        public GlazeButton Chip => chip;

        /// <summary>The panel is open; the rail steps aside meanwhile.</summary>
        public bool Open => open;

        /// <summary>The panel, for the editor's renders.</summary>
        public Transform Panel => panel;

        /// <summary>Everything the glance shows now, for the editor's checks.</summary>
        public IEnumerable<Component> Shown
        {
            get
            {
                if (chip.gameObject.activeInHierarchy) yield return chip;
                if (!panel.gameObject.activeInHierarchy) yield break;
                yield return title;
                yield return close;
                foreach (var reading in readings)
                {
                    if (reading.gameObject.activeSelf) yield return reading;
                }
                yield return note;
                yield return again;
            }
        }

        /// <summary>
        /// Builds a closed glance on <paramref name="projectRail"/> for the editor's renders: nothing
        /// reads the control plane, and <see cref="ShowForRender"/> sets what it shows.
        /// </summary>
        public static UsageLeftGlance ForRender(ProjectRail projectRail)
        {
            var glance = projectRail.gameObject.AddComponent<UsageLeftGlance>();
            glance.rail = projectRail;
            glance.Build();
            return glance;
        }

        /// <summary>
        /// Opens the panel showing <paramref name="presentation"/> as it is, where it would open among
        /// <paramref name="characters"/>, or closes it for null.
        /// </summary>
        public void ShowForRender(UsageLeftPresentation? presentation, IEnumerable<CharacterTarget> characters, float? surfaceHeight)
        {
            open = presentation != null;
            answer = null;
            shown = presentation;
            if (open) Place(characters, surfaceHeight);
            Layout();
        }

        /// <summary>Folded while another window keeps focus, with what it read; back as it was on return. Public for the editor's renders.</summary>
        public void ApplyFold()
        {
            if (panel.gameObject.activeSelf != (open && !FocusGuard.Folded)) panel.gameObject.SetActive(open && !FocusGuard.Folded);
        }

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            entry = GetComponent<EntryPanel>();
            director = GetComponent<WorkspaceDirector>();
            stage = GetComponent<CharacterStage>();
        }

        private void OnDestroy()
        {
            Cancel();
            if (panel != null) Destroy(panel.gameObject);
        }

        private void Update()
        {
            if (!built)
            {
                // The workspace director adds the rail in its Start; without one there is no glance.
                if (connection == null || (rail = GetComponent<ProjectRail>()) == null) return;
                if (entry == null) entry = GetComponent<EntryPanel>();
                Build();
            }
            var foreground = (entry != null && entry.Visible) || (director != null && director.OpenWorkstream != null);
            if (open && foreground) Close();
            ApplyFold();
            // The chip hides while the app lacks focus, the return's grace included, and comes back after.
            rail!.OfferUsageLeft(FocusGuard.InputSuspended ? null : Label);
            if (FocusGuard.InputSuspended) return;
            if (read != null && read.IsCompleted) Finish();
            // A window can pass its reset while the panel is open; it then goes, rather than stay wrong.
            if (open && Time.unscaledTime >= nextLayout) Layout();
        }

        private void Build()
        {
            // The rail makes and places the chip; the glance answers it and says when it shows.
            chip = rail!.UsageLeft;
            chip.Pressed += Toggle;
            rail.OfferUsageLeft(Label);
            panel = new GameObject("Usage left panel").transform;
            panel.SetParent(transform, false);
            // The stage's banner steps aside while the panel shows where it goes.
            AmbientCover.Add(panel.gameObject, panel: true);
            plate = WorkspaceVisuals.Plate(panel, "Background", new Vector2(Width, 0.2f), WorkspaceVisuals.PanelColor, WorkspaceVisuals.PanelPlateOrder);
            target = PointerTarget.Rectangle(panel.gameObject, new Vector2(Width, 0.2f), ray: true, poke: false);
            title = WorkspaceVisuals.Text(panel, "Title", WorkspaceVisuals.BodySize, WorkspaceVisuals.TextColor,
                new Vector2(Width / 2f, ButtonHeight), TextAlignmentOptions.MidlineLeft, order: WorkspaceVisuals.PanelTextOrder);
            WorkspaceVisuals.SetLiteral(title, Label);
            note = WorkspaceVisuals.Text(panel, "Note", WorkspaceVisuals.CaptionSize, WorkspaceVisuals.SecondaryColor,
                new Vector2(Width - 2 * Padding, 1f), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            close = PanelButton.Create(panel, "Close", ButtonHeight, WorkspaceVisuals.CaptionSize);
            close.Accepting = () => !FocusGuard.InputSuspended;
            close.Pressed += Close;
            again = PanelButton.Create(panel, "Read again", ButtonHeight, WorkspaceVisuals.CaptionSize);
            again.Accepting = () => !FocusGuard.InputSuspended && read == null;
            again.Pressed += Read;
            panel.gameObject.SetActive(false);
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
            Place(director != null ? director.Targets : Array.Empty<CharacterTarget>(), stage != null ? stage.SurfaceHeight : null);
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
            if (finished.IsFaulted) shown = UsageLeftPresenter.Unreachable();
            else answer = finished.Result;
            Layout();
        }

        /// <summary>
        /// Where the entry panel would open, clear of every character, facing the eyes, at the
        /// workspace's scale. The panel is shorter than the space kept for it, so <see cref="Layout"/>
        /// puts it at the edge of that space away from the characters, clear of their label plates too.
        /// </summary>
        private void Place(IEnumerable<CharacterTarget> characters, float? surfaceHeight)
        {
            var eyes = WorkspaceVisuals.HeadPosition;
            var looking = WorkspaceVisuals.Head != null ? WorkspaceVisuals.Head.forward : Vector3.forward;
            var (pose, direction) = WorkspaceLayout.PlaceForeground(characters, eyes, looking, surfaceHeight, scratch);
            placed = pose;
            placedAbove = direction.Above;
            panel.localScale = Vector3.one * WorkspaceLayout.Scale;
        }

        /// <summary>The panel, when open: its title and Close, the readings, the note, and Read again.</summary>
        private void Layout()
        {
            nextLayout = Time.unscaledTime + 15f;
            panel.gameObject.SetActive(open && !FocusGuard.Folded);
            if (!open) return;

            if (answer != null) shown = UsageLeftPresenter.Present(answer, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            var presentation = shown ?? UsageLeftPresenter.Message(UsageLeftPresenter.Reading);
            // One label per reading, with a gap between readings so each title leads its own line.
            var width = Width - 2 * Padding;
            while (readings.Count < presentation.Rows.Count)
            {
                readings.Add(WorkspaceVisuals.Text(panel, "Reading " + readings.Count, WorkspaceVisuals.DetailSize, WorkspaceVisuals.TextColor,
                    new Vector2(width, 1f), TextAlignmentOptions.TopLeft, wrap: true, order: WorkspaceVisuals.PanelTextOrder));
            }
            var heights = new List<float>();
            for (var index = 0; index < readings.Count; index++)
            {
                var shows = index < presentation.Rows.Count;
                readings[index].gameObject.SetActive(shows);
                if (!shows) continue;
                var row = presentation.Rows[index];
                WorkspaceVisuals.SetLiteralLines(readings[index], new[] { row.Title, row.Text });
                heights.Add(readings[index].GetPreferredValues(readings[index].text, width, 0f).y);
            }
            var bodyHeight = 0f;
            foreach (var each in heights) bodyHeight += each;
            bodyHeight += Mathf.Max(0, heights.Count - 1) * ReadingGap;
            note.color = presentation.Problem ? WorkspaceVisuals.ProblemColor : WorkspaceVisuals.SecondaryColor;
            WorkspaceVisuals.SetLiteral(note, presentation.Note);
            var noteHeight = note.GetPreferredValues(note.text, width, 0f).y;
            var gap = heights.Count > 0 ? Padding / 2f : 0f;
            var height = Padding + ButtonHeight + Padding / 2f + bodyHeight + gap + noteHeight + Padding / 2f + ButtonHeight + Padding;

            var away = (placedAbove ? 1f : -1f) * Mathf.Max(0f, WorkspacePanel.Height - height) / 2f * WorkspaceLayout.Scale;
            panel.SetPositionAndRotation(placed.position + placed.rotation * Vector3.up * away, placed.rotation);
            plate.size = new Vector2(Width, height);
            target.Resize(new Vector2(Width, height));
            var top = height / 2f - Padding;
            var left = -Width / 2f + Padding;
            title.rectTransform.localPosition = new Vector3(left, top, -0.003f);
            var closeWidth = close.Measure("Close", 0.14f);
            close.Show("Close", new Vector2(Width / 2f - Padding - closeWidth / 2f, top - ButtonHeight / 2f), closeWidth);
            top -= ButtonHeight + Padding / 2f;
            var y = top;
            for (var index = 0; index < heights.Count; index++)
            {
                readings[index].rectTransform.sizeDelta = new Vector2(width, heights[index]);
                readings[index].rectTransform.localPosition = new Vector3(left, y, -0.003f);
                y -= heights[index] + ReadingGap;
            }
            note.rectTransform.sizeDelta = new Vector2(width, noteHeight);
            note.rectTransform.localPosition = new Vector3(left, top - bodyHeight - gap, -0.003f);
            var againWidth = again.Measure("Read again", 0.16f);
            again.Show("Read again", new Vector2(Width / 2f - Padding - againWidth / 2f, -height / 2f + Padding + ButtonHeight / 2f), againWidth);
        }
    }
}
