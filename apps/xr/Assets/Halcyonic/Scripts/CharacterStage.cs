#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// One character per workstream, in an arc in front of the user, kept current from the session.
    /// Above them, a line says whether the state is live, so a disconnected stage never looks live.
    /// </summary>
    [RequireComponent(typeof(ControlPlaneConnection))]
    public sealed class CharacterStage : MonoBehaviour
    {
        [SerializeField] private float radius = 1.6f;
        [SerializeField] private float height = 1.1f;
        [SerializeField] private float arcDegrees = 100f;
        [SerializeField] private int maxCharacters = 6;

        private readonly Dictionary<string, CharacterView> views = new Dictionary<string, CharacterView>();
        private ControlPlaneConnection connection = null!;
        private TextMesh connectionLabel = null!;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            connectionLabel = Labels.Create(transform, "Connection", new Vector3(0f, height + 0.75f, radius), 0.004f);
        }

        private void OnEnable() => connection.Changed += OnChanged;

        private void OnDisable() => connection.Changed -= OnChanged;

        private void Start() => Refresh();

        private void OnChanged(StateChanges changes) => Refresh();

        private void Refresh()
        {
            var session = connection.Session;
            connectionLabel.text = session == null ? connection.SetupProblem ?? "Not connected" : Describe(session);
            if (session == null) return;

            var live = session.Status.IsLive;
            var shown = session.State.Workstreams.Values
                .OrderBy(workstream => workstream.CreatedAt, StringComparer.Ordinal)
                .Take(maxCharacters)
                .ToList();
            foreach (var gone in views.Keys.Except(shown.Select(workstream => workstream.WorkstreamId)).ToList())
            {
                Destroy(views[gone].gameObject);
                views.Remove(gone);
            }
            for (var index = 0; index < shown.Count; index++)
            {
                var workstream = shown[index];
                if (!views.TryGetValue(workstream.WorkstreamId, out var view))
                {
                    view = CharacterView.Create(transform, workstream.WorkstreamId);
                    views[workstream.WorkstreamId] = view;
                }
                Place(view.transform, index, shown.Count);
                view.Show(CharacterPresenter.Present(workstream, session.State, live));
            }
        }

        private void Place(Transform character, int index, int count)
        {
            var step = count > 1 ? arcDegrees / (count - 1) : 0f;
            var angle = (count > 1 ? -arcDegrees / 2f + step * index : 0f) * Mathf.Deg2Rad;
            var offset = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            character.localPosition = offset + Vector3.up * height;
            // Facing away from the user keeps text readable from the center of the arc.
            character.localRotation = Quaternion.LookRotation(offset);
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
