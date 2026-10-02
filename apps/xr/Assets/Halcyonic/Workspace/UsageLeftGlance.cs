#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The optional Usage left glance: a chip the project rail places at its lower row's right end
    /// (<see cref="ProjectRail.UsageLeft"/>) that opens a panel with the provider limits the Mac last
    /// saw. It reads the control plane only when opened or when Refresh is pressed, never on its own,
    /// and shows nothing that belongs to a Workstream. Each screen is a <see cref="PanelModel"/> from
    /// <see cref="UsageLeftScreens"/>, drawn on a <see cref="PanelFrame"/>.
    /// </summary>
    /// <remarks>
    /// The panel opens where the entry panel would (<see cref="WorkspaceLayout.PlaceForeground"/>),
    /// at touch distance, clear of every character, and the rail steps aside while it shows: one
    /// foreground surface at a time. It closes by its own Close or the chip, and when the entry panel
    /// or a workspace opens. While another window has focus its controls take no input; once focus
    /// stays away it folds, with what it read, and comes back as it was (<see cref="FocusGuard.Folded"/>).
    /// Without a rail it shows nothing.
    /// </remarks>
    public sealed class UsageLeftGlance : MonoBehaviour
    {
        /// <summary>How often an open panel is drawn again, so a window that passes its reset goes rather than stay wrong.</summary>
        private const float RedrawSeconds = 15f;

        private readonly List<BodyInView> scratch = new List<BodyInView>();
        private ControlPlaneConnection? connection;
        private ProjectRail? rail;
        private EntryPanel? entry;
        private WorkspaceDirector? director;
        private CharacterStage? stage;
        private GlazeButton chip = null!;
        private Transform root = null!;
        private PanelFrame frame = null!;
        private CancellationTokenSource? cancellation;
        private Task<UsageLimitsResponse>? read;
        private UsageLeftPresentation? shown;
        private UsageLimitsResponse? answer;
        private bool open;
        private bool built;
        private bool rendering;
        private bool renderReading;
        private bool renderDemonstration;
        private float nextLayout;

        /// <summary>The chip on the rail, which the rail places and this answers, for the editor's renders.</summary>
        public GlazeButton Chip => chip;

        /// <summary>The panel is open; the rail steps aside meanwhile.</summary>
        public bool Open => open;

        /// <summary>The panel's root, placed at touch distance and scaled by it, for the editor's renders.</summary>
        public Transform Panel => root;

        /// <summary>The frame drawing the panel, for the editor's checks.</summary>
        public PanelFrame Frame => frame;

        /// <summary>Everything the glance shows now, for the editor's checks.</summary>
        public IEnumerable<Component> Shown
        {
            get
            {
                if (chip.gameObject.activeInHierarchy) yield return chip;
                if (!root.gameObject.activeInHierarchy) yield break;
                foreach (var button in frame.Buttons) yield return button;
                foreach (var label in frame.Labels) yield return label;
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
            glance.rendering = true;
            glance.Build();
            return glance;
        }

        /// <summary>
        /// Opens the panel showing <paramref name="presentation"/> as it is, where it would open among
        /// <paramref name="characters"/>, or closes it for null: as while a read is in flight when
        /// <paramref name="reading"/>, and as while the recorded demonstration plays when
        /// <paramref name="demonstration"/>.
        /// </summary>
        public void ShowForRender(UsageLeftPresentation? presentation, IEnumerable<CharacterTarget> characters, float? surfaceHeight,
            bool reading = false, bool demonstration = false)
        {
            open = presentation != null;
            answer = null;
            shown = presentation;
            renderReading = reading;
            renderDemonstration = demonstration;
            frame.Page = 0;
            if (open) Place(characters, surfaceHeight);
            Layout();
        }

        /// <summary>Folded while another window keeps focus, with what it read; back as it was on return. Public for the editor's renders.</summary>
        public void ApplyFold()
        {
            if (root.gameObject.activeSelf != (open && !FocusGuard.Folded)) root.gameObject.SetActive(open && !FocusGuard.Folded);
        }

        private bool Reading => rendering ? renderReading : read != null;

        /// <summary>There is somewhere to read from: not while the recorded demonstration plays.</summary>
        private bool CanRead => rendering ? !renderDemonstration : connection != null && connection.DemonstrationLine == null;

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
            if (root != null) Destroy(root.gameObject);
        }

        private void Update()
        {
            if (!built)
            {
                // The workspace director adds the rail in its Start; without one there is no glance.
                if (connection == null || !TryGetComponent(out rail)) return;
                if (entry == null) TryGetComponent(out entry);
                Build();
            }
            var foreground = (entry != null && entry.Visible) || (director != null && director.OpenWorkstream != null);
            // The recorded demonstration has nothing to read, so it offers no Usage left at all.
            var demonstration = connection?.DemonstrationLine != null;
            if (open && (foreground || demonstration)) Close();
            ApplyFold();
            // The chip hides while the app lacks focus, the return's grace included, and comes back after.
            rail!.OfferUsageLeft(FocusGuard.InputSuspended || demonstration ? null : UsageLeftPresenter.Title);
            if (FocusGuard.InputSuspended) return;
            if (read != null && read.IsCompleted) Finish();
            if (open && Time.unscaledTime >= nextLayout) Layout();
        }

        private void Build()
        {
            // The rail makes and places the chip; the glance answers it and says when it shows.
            chip = rail!.UsageLeft;
            chip.Pressed += Toggle;
            rail.OfferUsageLeft(connection?.DemonstrationLine != null ? null : UsageLeftPresenter.Title);
            root = new GameObject("Usage left panel").transform;
            root.SetParent(transform, false);
            // The stage's banner steps aside while the panel shows where it goes, and names it while it is folded.
            AmbientCover.Add(root.gameObject, panel: true, () => open ? UsageLeftPresenter.Title : null);
            frame = PanelFrame.Create(root, "Frame");
            frame.Accepting = () => !FocusGuard.InputSuspended;
            frame.Acted += OnActed;
            root.gameObject.SetActive(false);
            built = true;
            Layout();
        }

        private void OnActed(string id, string? key)
        {
            if (id == PanelModel.Close) Close();
            else if (id == UsageLeftScreens.Refresh) Read();
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
            frame.Page = 0;
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
            var api = connection.DemonstrationLine != null ? null : ControlPlaneSettings.Api();
            if (api == null)
            {
                answer = null;
                shown = UsageLeftPresenter.Message(connection.DemonstrationLine != null ? UsageLeftPresenter.NotInDemo : UsageLeftPresenter.NotSetUp);
                Layout();
                return;
            }
            // The rows read before stay while the limits are read again, their meters waiting; with none, it says it is reading.
            if (Presented().Rows.Count == 0)
            {
                answer = null;
                shown = UsageLeftPresenter.Message(UsageLeftPresenter.Reading);
            }
            cancellation = new CancellationTokenSource();
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
                answer = null;
                shown = UsageLeftPresenter.Unreachable();
            }
            else answer = finished.Result;
            Layout();
        }

        /// <summary>What was read, presented now, so a window past its reset goes; or what the glance was given to say.</summary>
        private UsageLeftPresentation Presented() =>
            answer != null ? UsageLeftPresenter.Present(answer, DateTimeOffset.UtcNow, TimeZoneInfo.Local)
                : shown ?? UsageLeftPresenter.Message(UsageLeftPresenter.Reading);

        /// <summary>Where the entry panel would open, at touch distance, clear of every character and its label, facing the eyes.</summary>
        private void Place(IEnumerable<CharacterTarget> characters, float? surfaceHeight)
        {
            var eyes = WorkspaceVisuals.HeadPosition;
            var looking = WorkspaceVisuals.Head != null ? WorkspaceVisuals.Head.forward : Vector3.forward;
            var (pose, _) = WorkspaceLayout.PlaceForeground(characters, eyes, looking, surfaceHeight, scratch, WorkspaceLayout.FrameSize);
            root.SetPositionAndRotation(pose.position, pose.rotation);
            root.localScale = Vector3.one * PanelFrame.Scale;
        }

        /// <summary>The panel, when open, drawn from what was read.</summary>
        private void Layout()
        {
            nextLayout = Time.unscaledTime + RedrawSeconds;
            root.gameObject.SetActive(open && !FocusGuard.Folded);
            if (!open) return;
            frame.Show(UsageLeftScreens.Screen(Presented(), Reading, CanRead));
        }
    }
}
