#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.Workspace;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>
    /// The room's small controls: a switch between the real room and a virtual space; when it would
    /// help, an offer to allow room access or to set up the room; and, while the stage stands in front
    /// of the person, Make room for a window, which turns the lineup to their right
    /// (<see cref="CharacterStage.SetAside"/>). They are the workspace's
    /// <see cref="PanelButton"/>s, the Interaction SDK's ray and poke targets, so they are pointed at
    /// and pinched, or poked, like every other button, and ignore input while
    /// <see cref="FocusGuard.InputSuspended"/>. Above them, one line says where the agents are and
    /// why, for a few seconds after it changes.
    /// </summary>
    /// <remarks>
    /// Nothing here is needed to get started: the room placement shows the real room where it works
    /// and the virtual space otherwise by itself. The controls rest low and to the right, within
    /// reach of a seated person's right hand and about 45 degrees below the eyes, under the lineup
    /// on a desk; when there is something to read or offered, they come up in front of the person for
    /// a few seconds, then go back. They return to their resting place when the person has looked
    /// elsewhere for a moment, so a turned chair or a recenter never leaves them out of view.
    /// </remarks>
    internal sealed class RoomControls : MonoBehaviour
    {
        /// <summary>Where they rest, relative to where the person faces: to the right, near, and low.</summary>
        private const float RestTurnDegrees = 26f;
        private const float RestReach = 0.40f;
        private const float RestBelowEyes = 0.40f;

        /// <summary>Where they come up when there is something to read: ahead, a little below the eyes.</summary>
        private const float PresentReach = 0.50f;
        private const float PresentBelowEyes = 0.10f;
        private const float PresentSeconds = 6f;

        /// <summary>How long the line shows after it changes, while resting.</summary>
        private const float LineSeconds = 8f;

        /// <summary>Out of the comfortable zone for this long, they return to their resting place.</summary>
        private const float AwaySeconds = 1.5f;
        private const float AwayDegrees = 50f;

        /// <summary>A resting panel stays this far above the surface the stage stands on.</summary>
        private const float SurfaceClearance = 0.02f;

        private const float Gap = 0.02f;
        private const float LineWidth = 0.56f;
        private const float LinePadding = 0.02f;
        private const float MinButtonWidth = 0.2f;

        /// <summary>Turns the lineup aside so a window in front covers fewer characters; it cannot see the window.</summary>
        private const string AsideMake = "Make room for a window";

        private const string AsideBack = "Characters in front";

        private RoomPlacement placement = null!;
        private PanelButton switchButton = null!;
        private PanelButton offerButton = null!;
        private PanelButton asideButton = null!;
        private CharacterStage? stage;
        private Transform lineRoot = null!;
        private SpriteRenderer linePlate = null!;
        private TextMeshPro line = null!;
        private string shownLine = "";
        private float lineUntil;
        private float presentUntil;
        private bool presenting;
        private bool placed;
        private float awayFor;

        public static RoomControls Create(Transform parent, RoomPlacement placement)
        {
            var go = new GameObject("Room controls");
            go.transform.SetParent(parent, false);
            var controls = go.AddComponent<RoomControls>();
            controls.placement = placement;
            controls.Build();
            return controls;
        }

        /// <summary>Brings the controls and their line up in front of the person for a few seconds.</summary>
        public void Present()
        {
            presentUntil = Time.unscaledTime + PresentSeconds;
            lineUntil = Mathf.Max(lineUntil, presentUntil);
            if (!presenting || !placed) Place(presentingNow: true);
            Layout();
        }

        private void Build()
        {
            lineRoot = new GameObject("Line").transform;
            lineRoot.SetParent(transform, false);
            linePlate = RoomVisuals.Plate(lineRoot, "Plate");
            line = RoomVisuals.Text(lineRoot, "Text", new Vector2(LineWidth, 0.2f));
            switchButton = PanelButton.Create(transform, "Space switch");
            switchButton.Pressed += OnSwitch;
            offerButton = PanelButton.Create(transform, "Room offer");
            offerButton.Pressed += OnOffer;
            stage = placement.GetComponent<CharacterStage>();
            asideButton = PanelButton.Create(transform, "Aside");
            asideButton.Accepting = () => !FocusGuard.InputSuspended;
            asideButton.Pressed += OnAside;
            placement.StatusChanged += OnStatusChanged;
            placement.Changed += OnPlacementChanged;
            OnStatusChanged(placement.Status);
        }

        private void OnDestroy()
        {
            if (placement == null) return;
            placement.StatusChanged -= OnStatusChanged;
            placement.Changed -= OnPlacementChanged;
        }

        /// <summary>A stage that moved onto a surface or off it changes where the controls can rest.</summary>
        private void OnPlacementChanged()
        {
            if (placed && !presenting) Place(presentingNow: false);
            Layout();
        }

        private void OnSwitch() =>
            placement.SetSpace(placement.Status.Shown == RoomSpace.Room ? RoomSpace.Virtual : RoomSpace.Room);

        private void OnOffer() => placement.TakeOffer();

        /// <summary>Turns the lineup to the right to make room for a window in front, or back.</summary>
        private void OnAside()
        {
            if (stage == null || FocusGuard.InputSuspended) return;
            stage.SetAside(!stage.Aside);
            Layout();
        }

        /// <summary>The aside choice is offered while the stage stands in front of the person, not on a surface.</summary>
        private bool AsideOffered => stage != null && placement.Preferred == null;

        private void OnStatusChanged(RoomStatus status)
        {
            if (status.Line != shownLine)
            {
                shownLine = status.Line;
                lineUntil = Time.unscaledTime + LineSeconds;
            }
            Layout();
        }

        private void Update()
        {
            var now = Time.unscaledTime;
            if (presenting && now >= presentUntil && placement.Status.Scan != RoomScan.AskingAccess) Place(presentingNow: false);
            if (!placed) Place(presentingNow: false);
            if (lineRoot.gameObject.activeSelf != LineShown(now)) Layout();
            FollowPerson(now, Time.unscaledDeltaTime);
        }

        private bool LineShown(float now) => now < lineUntil || placement.Status.Scan == RoomScan.AskingAccess;

        /// <summary>
        /// Lays the controls out in their own plane, at the workspace's design sizes, centered on
        /// their origin: the buttons side by side, and the line above them while it shows.
        /// </summary>
        private void Layout()
        {
            var status = placement.Status;
            var offer = placement.Offer;
            var switchLabel = status.SwitchLabel;
            var offerLabel = RoomStatus.OfferLabel(offer);
            var switchWidth = status.CanSwitch ? switchButton.Measure(switchLabel, MinButtonWidth) : 0f;
            var offerWidth = offer != RoomOffer.None ? offerButton.Measure(offerLabel, MinButtonWidth) : 0f;
            var asideLabel = stage != null && stage.Aside ? AsideBack : AsideMake;
            var asideWidth = AsideOffered ? asideButton.Measure(asideLabel, MinButtonWidth) : 0f;
            var widths = new[] { switchWidth, offerWidth, asideWidth };
            var total = 0f;
            foreach (var width in widths)
            {
                if (width > 0f) total += (total > 0f ? Gap : 0f) + width;
            }
            var left = -total / 2f;
            var buttons = new[] { switchButton, offerButton, asideButton };
            var labels = new[] { switchLabel, offerLabel, asideLabel };
            for (var index = 0; index < buttons.Length; index++)
            {
                if (widths[index] <= 0f)
                {
                    buttons[index].Hide();
                    continue;
                }
                buttons[index].Show(labels[index], new Vector2(left + widths[index] / 2f, 0f), widths[index]);
                left += widths[index] + Gap;
            }

            var shown = LineShown(Time.unscaledTime);
            lineRoot.gameObject.SetActive(shown);
            if (!shown) return;
            line.text = shownLine;
            var size = line.GetPreferredValues(shownLine, LineWidth, 0f);
            var textHeight = Mathf.Min(size.y, 0.2f);
            line.rectTransform.sizeDelta = new Vector2(LineWidth, textHeight);
            var plateSize = new Vector2(Mathf.Min(size.x, LineWidth) + 2f * LinePadding, textHeight + 2f * LinePadding);
            linePlate.size = plateSize;
            linePlate.transform.localPosition = new Vector3(0f, 0f, 0.002f);
            var buttonsTop = total > 0f ? PanelButton.Height / 2f + Gap : 0f;
            lineRoot.localPosition = new Vector3(0f, buttonsTop + plateSize.y / 2f, 0f);
        }

        /// <summary>
        /// Stands the controls where they rest, or up in front of the person, facing the eyes and
        /// scaled to keep the buttons' designed angular size.
        /// </summary>
        private void Place(bool presentingNow)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null) return;
            presenting = presentingNow;
            placed = true;
            awayFor = 0f;
            var eyes = head.position;
            var facing = Level(head);
            var direction = Quaternion.AngleAxis(presentingNow ? 0f : RestTurnDegrees, Vector3.up) * facing;
            var position = eyes + direction * (presentingNow ? PresentReach : RestReach)
                + Vector3.down * (presentingNow ? PresentBelowEyes : RestBelowEyes);
            var surface = placement.Preferred;
            if (!presentingNow && surface.HasValue)
            {
                // Resting above the desk, never in it.
                var scale = Vector3.Distance(eyes, position) / RoomVisuals.DesignDistance;
                var lowest = surface.Value.position.y + SurfaceClearance + scale * PanelButton.Height / 2f;
                if (position.y < lowest) position.y = lowest;
            }
            var toPanel = position - eyes;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(toPanel.normalized, Vector3.up));
            transform.localScale = Vector3.one * (toPanel.magnitude / RoomVisuals.DesignDistance);
        }

        /// <summary>Returns the controls to rest when the person has faced well away from them for a moment.</summary>
        private void FollowPerson(float now, float deltaTime)
        {
            var head = Camera.main != null ? Camera.main.transform : null;
            if (head == null || presenting) return;
            var toPanel = transform.position - head.position;
            var level = new Vector3(toPanel.x, 0f, toPanel.z);
            var away = level.sqrMagnitude < 1e-4f || Vector3.Angle(Level(head), level) > AwayDegrees || level.magnitude > 1.0f;
            awayFor = away ? awayFor + Mathf.Min(deltaTime, 0.1f) : 0f;
            if (awayFor > AwaySeconds) Place(presentingNow: false);
        }

        /// <summary>Where the head faces on the level, even looking straight down.</summary>
        private static Vector3 Level(Transform head)
        {
            var forward = head.forward;
            var level = new Vector3(forward.x, 0f, forward.z);
            if (level.sqrMagnitude < 0.01f)
            {
                var up = head.up;
                level = new Vector3(up.x, 0f, up.z) * (forward.y < 0f ? 1f : -1f);
            }
            return level.sqrMagnitude < 1e-6f ? Vector3.forward : level.normalized;
        }
    }
}
