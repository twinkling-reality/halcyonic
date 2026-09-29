#nullable enable
using System.Collections.Generic;
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
    [RequireComponent(typeof(ControlPlaneConnection))]
    public sealed class CharacterStage : MonoBehaviour
    {
        [Tooltip("How far the characters stand from the person's head, in meters. System windows, such as a virtual display opened over the app, render within about 2 m, so the characters stand beyond them. Characters and their labels scale with this distance, so they keep the same apparent size.")]
        [SerializeField] private float distance = 2.4f;

        [Tooltip("The height of the characters' centers relative to the person's eyes when the stage was placed, in meters; negative is below. A character that needs the person rises from here toward their eye level.")]
        [SerializeField] private float heightFromEyes = -0.45f;

        [Tooltip("The width of the arc of character slots, in degrees, centered on where the person faced when the stage was placed.")]
        [SerializeField] private float arcDegrees = 100f;

        [Tooltip("How many characters the stage shows at most.")]
        [SerializeField] private int maxCharacters = 6;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int RectId = Shader.PropertyToID("_Rect");

        private readonly Dictionary<string, CharacterView> views = new Dictionary<string, CharacterView>();
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
        private string? shownConnection;
        private bool shownLive;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            lineup = new CharacterLineup(Mathf.Max(1, maxCharacters));
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
            var reason = placement.Poll(head, Time.unscaledTime, Time.unscaledDeltaTime);
            if (reason != null) Place(reason);
        }

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
                }
                PlaceInSlot(view.transform, slot);
                view.Show(CharacterPresenter.Present(session.State.Workstreams[id], session.State, live));
            }
        }

        /// <summary>
        /// Stands the arc where the person's head is, facing where they face on the level, so the
        /// characters are in front of them wherever the tracking origin is.
        /// </summary>
        private void Place(string reason)
        {
            if (head != null)
            {
                var forward = head.forward;
                forward.y = 0f;
                var facing = forward.sqrMagnitude > 1e-4f
                    ? Quaternion.LookRotation(forward, Vector3.up)
                    : Quaternion.Euler(0f, head.eulerAngles.y, 0f);
                arc.SetPositionAndRotation(head.position, facing);
            }
            CharacterMaterials.SetKeyLight(arc.rotation);
            arc.gameObject.SetActive(true);
            foreach (var view in views.Values)
            {
                var slot = lineup.SlotOf(view.WorkstreamId);
                if (slot >= 0) PlaceInSlot(view.transform, slot);
            }
            connectionRoot.localPosition = new Vector3(0f, 0.05f * distance, distance);
            connectionRoot.localScale = Vector3.one * distance;
            Debug.Log("Halcyonic: placed the stage in front of the person because " + reason + ".");
        }

        /// <summary>
        /// Slots are fixed points on the arc, numbered from the person's left, so a character never
        /// moves while it stays on the stage. A character faces the person and is scaled with the
        /// distance, which keeps its apparent size.
        /// </summary>
        private void PlaceInSlot(Transform character, int slot)
        {
            var count = lineup.Capacity;
            var angle = (count > 1 ? -arcDegrees / 2f + arcDegrees * slot / (count - 1) : 0f) * Mathf.Deg2Rad;
            var level = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
            character.localPosition = level * distance + Vector3.up * heightFromEyes;
            character.localRotation = Quaternion.LookRotation(-level, Vector3.up);
            character.localScale = Vector3.one * distance;
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
            var textWidth = 0f;
            foreach (var line in connectionLabel.text.Split('\n')) textWidth = Mathf.Max(textWidth, Labels.Width(connectionLabel, line));
            var plateWidth = textWidth + 2f * padding;
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
    }
}
