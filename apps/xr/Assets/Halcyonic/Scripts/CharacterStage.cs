#nullable enable
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// The characters, on an arc in front of the person, kept current from the session. The lineup
    /// in the client core decides which workstreams have a character and which slot each stands in;
    /// this component places the arc relative to the person and says above it whether the state is
    /// live, so a disconnected stage never looks live. It keeps animating and updating while the
    /// app lacks input focus: losing focus is not a pause.
    /// </summary>
    /// <remarks>
    /// The arc keeps every character and its labels within about 36 degrees of where the person
    /// faced, a comfortable field of view on narrower headsets too, and the lineup puts characters
    /// that need attention in its middle. Everything is looked at and pointed at from the seat;
    /// nothing needs the person to stand or reach.
    /// <para>
    /// An <see cref="IStagePlacementSource"/> on the same object, such as a room placement that found
    /// the person's desk, can give the stage a surface instead. Its pose's position is where the
    /// middle of the lineup stands; the arc curves around the person's side of it, at the distance
    /// they were from it when the pose arrived, and every character's label plate rests on the
    /// surface. While the pose is set, only the source moves the stage: recenters leave it where it
    /// is. Without a source, or when it clears its pose, the stage stands in front of the person.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(ControlPlaneConnection))]
    public sealed class CharacterStage : MonoBehaviour
    {
        [Tooltip("How far the characters stand from the person's head, in meters. System windows, such as a virtual display opened over the app, render within about 2 m, so the characters stand beyond them. Characters and their labels scale with this distance, so they keep the same apparent size.")]
        [SerializeField] private float distance = 2.4f;

        [Tooltip("The height of the characters' centers relative to the person's eyes when the stage was placed, in meters; negative is below. A character that needs the person rises from here toward their eye level.")]
        [SerializeField] private float heightFromEyes = -0.45f;

        [Tooltip("The angle between the outermost characters, in degrees, centered on where the person faced when the stage was placed. At 60 the outermost stand 30 degrees to each side and their labels end within about 36, a comfortable field of view on narrower headsets too.")]
        [SerializeField] private float spanDegrees = 60f;

        [Tooltip("How many characters the stage shows at most.")]
        [SerializeField] private int maxCharacters = 6;

        /// <summary>The nearest and the default reach to a surface, in meters.</summary>
        private const float NearestSurface = 0.4f;
        private const float DefaultSurface = 0.8f;

        /// <summary>The gap between a label plate and the surface it rests on, in a character's units.</summary>
        private const float SurfaceClearance = 0.005f;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int RectId = Shader.PropertyToID("_Rect");

        private readonly Dictionary<string, CharacterView> views = new Dictionary<string, CharacterView>();
        private readonly Dictionary<string, Standing> standings = new Dictionary<string, Standing>();
        private readonly List<string> departed = new List<string>();
        private readonly PersonPlacement placement = new PersonPlacement();
        private ControlPlaneConnection connection = null!;
        private CharacterLineup lineup = null!;
        private Transform arc = null!;
        private Transform connectionRoot = null!;
        private TextMesh connectionLabel = null!;
        private MeshRenderer connectionPlate = null!;
        private MaterialPropertyBlock connectionPlateBlock = null!;
        private Transform? head;
        private IStagePlacementSource? source;
        private float nextSourceSearch;
        private bool preferredChanged;
        private bool placedOnce;
        private bool onSurface;

        /// <summary>The arc's radius now: the distance setting in front of the person, or the reach to a surface.</summary>
        private float radius;
        private string? shownConnection;
        private bool shownLive;

        /// <summary>
        /// Raised with the workstream id when the stage creates a character, so other components can
        /// add to it at runtime; the workspace attaches its ray and poke targets this way.
        /// </summary>
        public event System.Action<string, CharacterView>? CharacterCreated;

        /// <summary>The character showing a workstream, if it is on the stage.</summary>
        public bool TryGetCharacter(string workstreamId, [MaybeNullWhen(false)] out CharacterView view) =>
            views.TryGetValue(workstreamId, out view);

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            lineup = new CharacterLineup(Mathf.Max(1, maxCharacters));
            radius = distance;
            CharacterMaterials.Prepare();

            // Hidden until it is placed in front of the person.
            arc = new GameObject("Arc").transform;
            arc.SetParent(transform, false);
            arc.gameObject.SetActive(false);

            connectionRoot = new GameObject("Connection").transform;
            connectionRoot.SetParent(arc, false);
            connectionPlate = Labels.CreatePlate(connectionRoot, "Plate");
            connectionLabel = Labels.CreateSized(connectionRoot, "Status", Vector3.zero, 0.02f);
            connectionPlateBlock = new MaterialPropertyBlock();
        }

        private void OnEnable() => connection.Changed += OnChanged;

        private void OnDisable()
        {
            connection.Changed -= OnChanged;
            placement.Stop();
            if (source != null) source.Changed -= OnPreferredChanged;
            source = null;
            nextSourceSearch = 0f;
        }

        private void Start() => Refresh();

        private void OnApplicationPause(bool paused)
        {
            // The person may have moved while the headset was off.
            if (!paused) placement.Request("the app resumed", Time.unscaledTime);
        }

        private void LateUpdate()
        {
            var camera = Camera.main;
            var current = camera != null ? camera.transform : null;
            if (current != head)
            {
                head = current;
                foreach (var view in views.Values) view.Person = head;
            }
            FindPlacementSource();
            var preferred = source?.Preferred;
            var reason = placement.Poll(head, Time.unscaledTime, Time.unscaledDeltaTime);
            var placed = false;
            if (preferredChanged && placedOnce)
            {
                preferredChanged = false;
                placed = preferred.HasValue || onSurface;
                if (preferred.HasValue) Place(preferred, "the room placement gave it a surface");
                else if (onSurface) Place(null, "the room placement has no surface any more");
            }
            if (reason != null && !placed)
            {
                // Once the stage stands on a surface, only the room placement moves it.
                if (!placedOnce || !preferred.HasValue) Place(preferred, reason);
                else Debug.Log("Halcyonic: kept the stage on its surface although " + reason + ".");
            }
            Glide();
        }

        /// <summary>
        /// Looks for a placement source on this object, once a second until one appears, because the
        /// component that provides it can be added after the stage starts.
        /// </summary>
        private void FindPlacementSource()
        {
            if (source is Object component && component == null)
            {
                source = null;
                preferredChanged = true;
            }
            if (source != null || Time.unscaledTime < nextSourceSearch) return;
            nextSourceSearch = Time.unscaledTime + 1f;
            source = GetComponent<IStagePlacementSource>();
            if (source == null) return;
            source.Changed += OnPreferredChanged;
            preferredChanged = true;
        }

        private void OnPreferredChanged() => preferredChanged = true;

        private void OnChanged(StateChanges changes) => Refresh();

        private void Refresh()
        {
            var session = connection.Session;
            // A demonstration says so in its own words, so a recording is never read as live work.
            var demonstration = connection.DemonstrationLine;
            ShowConnection(
                demonstration ?? (session == null ? connection.SetupProblem ?? "Not connected" : Describe(session)),
                demonstration == null && session != null && session.Status.IsLive);
            if (session == null) return;

            var live = session.Status.IsLive;
            lineup.Update(session.State.Workstreams.Values);
            departed.Clear();
            foreach (var id in views.Keys)
            {
                if (lineup.SlotOf(id) < 0) departed.Add(id);
            }
            foreach (var id in departed)
            {
                Destroy(views[id].gameObject);
                views.Remove(id);
                standings.Remove(id);
            }
            var slots = lineup.Slots;
            for (var slot = 0; slot < slots.Count; slot++)
            {
                var id = slots[slot];
                if (id == null) continue;
                if (!views.TryGetValue(id, out var view))
                {
                    view = CharacterView.Create(arc, id);
                    view.Person = head;
                    views[id] = view;
                    CharacterCreated?.Invoke(id, view);
                }
                view.Show(CharacterPresenter.Present(session.State.Workstreams[id], session.State, live));
                MoveToSlot(id, view, slot);
            }
        }

        /// <summary>Places the stage on the preferred surface, or in front of the person without one.</summary>
        private void Place(Pose? preferred, string reason)
        {
            preferredChanged = false;
            placedOnce = true;
            if (preferred.HasValue) StandOnSurface(preferred.Value);
            else StandBeforePerson();
            CharacterMaterials.SetKeyLight(arc.rotation);
            arc.gameObject.SetActive(true);
            // Every character to its slot, with the current settings; a glide in progress ends.
            foreach (var pair in views)
            {
                var standing = standings[pair.Key];
                var slot = lineup.SlotOf(pair.Key);
                if (slot >= 0) standing.Angle = standing.From = standing.To = SlotAngle(slot);
                Stand(pair.Value, standing.Angle, 1f);
            }
            // Above the characters: at eye level in front of the person, higher over a surface.
            connectionRoot.localPosition = new Vector3(0f, (onSurface ? 0.4f : 0.05f) * radius, radius);
            connectionRoot.localScale = Vector3.one * radius;
            Debug.Log("Halcyonic: placed the stage " + (onSurface ? "on its surface" : "in front of the person") + " because " + reason + ".");
        }

        /// <summary>
        /// Stands the arc where the person's head is, facing where they face on the level, so the
        /// characters are in front of them wherever the tracking origin is.
        /// </summary>
        private void StandBeforePerson()
        {
            onSurface = false;
            radius = distance;
            if (head == null) return;
            var forward = head.forward;
            forward.y = 0f;
            var facing = forward.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.Euler(0f, head.eulerAngles.y, 0f);
            arc.SetPositionAndRotation(head.position, facing);
        }

        /// <summary>
        /// Stands the arc on a surface: the middle of the lineup at the pose's position, the arc's
        /// center on the person's side of it at the surface's height, and its radius the person's
        /// distance from the pose, so the characters face them.
        /// </summary>
        private void StandOnSurface(Pose pose)
        {
            onSurface = true;
            var toward = head != null ? pose.position - head.position : pose.forward;
            toward.y = 0f;
            if (toward.sqrMagnitude < 1e-4f)
            {
                toward = pose.forward;
                toward.y = 0f;
            }
            if (toward.sqrMagnitude < 1e-6f) toward = Vector3.forward;
            radius = head != null ? Mathf.Clamp(toward.magnitude, NearestSurface, distance) : DefaultSurface;
            var facing = Quaternion.LookRotation(toward.normalized, Vector3.up);
            arc.SetPositionAndRotation(pose.position - facing * Vector3.forward * radius, facing);
        }

        /// <summary>
        /// Slots are fixed points on the arc, numbered from the person's left. A new character appears
        /// in its slot; one the lineup moves, only ever to bring attention to the middle, glides there
        /// along the arc, swinging out behind the others rather than passing through them.
        /// </summary>
        private void MoveToSlot(string id, CharacterView view, int slot)
        {
            var angle = SlotAngle(slot);
            if (!standings.TryGetValue(id, out var standing))
            {
                standing = new Standing { Angle = angle, From = angle, To = angle };
                standings[id] = standing;
            }
            else if (!Mathf.Approximately(standing.To, angle))
            {
                standing.From = standing.Angle;
                standing.To = angle;
                standing.Started = Time.time;
                standing.Duration = 0.5f + Mathf.Abs(angle - standing.Angle) / 60f;
                return;
            }
            // Standing again also follows a label plate that grew or shrank, on a surface.
            if (standing.Angle == standing.To) Stand(view, standing.Angle, 1f);
        }

        private void Glide()
        {
            foreach (var pair in views)
            {
                var standing = standings[pair.Key];
                if (standing.Angle == standing.To) continue;
                var progress = Mathf.Clamp01((Time.time - standing.Started) / standing.Duration);
                standing.Angle = progress >= 1f ? standing.To : Mathf.Lerp(standing.From, standing.To, Mathf.SmoothStep(0f, 1f, progress));
                Stand(pair.Value, standing.Angle, 1f + 0.15f * Mathf.Sin(Mathf.PI * progress));
            }
        }

        private float SlotAngle(int slot)
        {
            var count = lineup.Capacity;
            return count > 1 ? -spanDegrees / 2f + spanDegrees * slot / (count - 1) : 0f;
        }

        /// <summary>
        /// Stands a character on the arc at an angle from where the person faced, facing them, scaled
        /// with the arc's radius so it keeps its apparent size. In front of the person it stands at
        /// the height setting; on a surface, its label plate rests on the surface.
        /// </summary>
        private void Stand(CharacterView view, float angle, float reach)
        {
            var radians = angle * Mathf.Deg2Rad;
            var level = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            var height = onSurface ? (SurfaceClearance - view.Footing) * radius : heightFromEyes;
            var character = view.transform;
            character.localPosition = level * (radius * reach) + Vector3.up * height;
            character.localRotation = Quaternion.LookRotation(-level, Vector3.up);
            character.localScale = Vector3.one * radius;
        }

        private void ShowConnection(string text, bool live)
        {
            if (text == shownConnection && live == shownLive) return;
            shownConnection = text;
            shownLive = live;
            const float width = 0.6f;
            const float padding = 0.01f;
            connectionLabel.text = Labels.Wrap(connectionLabel, text, width - 2f * padding, 3, out var lines);
            connectionLabel.color = live ? new Color(0.72f, 0.76f, 0.84f) : new Color(1f, 0.86f, 0.62f);
            var plateWidth = Labels.WidestLine(connectionLabel) + 2f * padding;
            var plateHeight = lines * Labels.LineHeight(connectionLabel) + 2f * padding;
            var plate = connectionPlate.transform;
            plate.localPosition = new Vector3(0f, 0f, 0.002f);
            plate.localScale = new Vector3(plateWidth, plateHeight, 1f);
            connectionPlateBlock.SetColor(ColorId, new Color(0.06f, 0.07f, 0.09f, 0.62f));
            connectionPlateBlock.SetVector(RectId, new Vector4(plateWidth, plateHeight, 0.015f, 0f));
            connectionPlate.SetPropertyBlock(connectionPlateBlock);
        }

        private static string Describe(RealtimeSession session)
        {
            var status = session.Status;
            var origin = session.State.Journal?.Origin == JournalOrigin.Fixture ? " (recorded data)" : "";
            switch (status.Phase)
            {
                case ConnectionPhase.Live:
                    return "Live" + origin;
                case ConnectionPhase.WaitingToRetry:
                    return "Disconnected, showing the last known state. " + status.Detail;
                case ConnectionPhase.Refused:
                    return "The control plane refused this client. " + status.Detail;
                default:
                    return status.Phase + origin;
            }
        }

        /// <summary>Where a character stands on the arc, and where it is gliding to.</summary>
        private sealed class Standing
        {
            public float Angle;
            public float From;
            public float To;
            public float Started;
            public float Duration;
        }
    }
}
