#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>An optional, account-wide glance beside the stage. It reads only while opened.</summary>
    [RequireComponent(typeof(ControlPlaneConnection))]
    public sealed class UsageLimitsPanel : MonoBehaviour
    {
        private ControlPlaneConnection connection = null!;
        private Transform root = null!;
        private PanelButton toggle = null!;
        private PanelButton refresh = null!;
        private GameObject plate = null!;
        private TextMeshPro body = null!;
        private CancellationTokenSource? cancellation;
        private Task<UsageLimitsResponse>? read;
        private UsageLimitsResponse? answer;
        private string? error;
        private bool open;
        private bool placed;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            root = new GameObject("Usage left controls").transform;
            root.SetParent(transform, false);
            toggle = PanelButton.Create(root, "Usage left", textSize: WorkspaceVisuals.DetailSize);
            toggle.Pressed += Toggle;
            plate = new GameObject("Usage left panel");
            plate.transform.SetParent(root, false);
            WorkspaceVisuals.Plate(plate.transform, "Background", new Vector2(0.62f, 0.48f),
                WorkspaceVisuals.PanelColor, WorkspaceVisuals.PanelPlateOrder);
            PointerTarget.Rectangle(plate, new Vector2(0.62f, 0.48f), ray: true, poke: false);
            body = WorkspaceVisuals.Text(plate.transform, "Readings", WorkspaceVisuals.DetailSize,
                WorkspaceVisuals.TextColor, new Vector2(0.56f, 0.35f), TextAlignmentOptions.TopLeft,
                wrap: true, order: WorkspaceVisuals.PanelTextOrder);
            body.rectTransform.localPosition = new Vector3(-0.28f, 0.20f, -0.003f);
            refresh = PanelButton.Create(plate.transform, "Read again", height: 0.06f,
                textSize: WorkspaceVisuals.CaptionSize);
            refresh.Pressed += Read;
            Layout();
        }

        private void OnDestroy()
        {
            cancellation?.Cancel();
            cancellation?.Dispose();
            if (root != null) Destroy(root.gameObject);
        }

        private void Update()
        {
            if (!placed) Place();
            if (read == null || !read.IsCompleted) return;
            var finished = read;
            read = null;
            if (finished.IsCanceled) return;
            if (finished.IsFaulted)
            {
                error = "Could not read usage. Try again.";
                answer = null;
            }
            else
            {
                answer = finished.Result;
                error = null;
            }
            Layout();
        }

        private void Toggle()
        {
            if (FocusGuard.InputSuspended) return;
            open = !open;
            Place();
            if (open) Read();
            else
            {
                cancellation?.Cancel();
                read = null;
                Layout();
            }
        }

        private void Read()
        {
            if (!open || FocusGuard.InputSuspended || read != null) return;
            if (connection.DemonstrationLine != null)
            {
                error = "Usage is unavailable in the demonstration.";
                Layout();
                return;
            }
            var api = ControlPlaneSettings.Api();
            if (api == null)
            {
                error = "Connect this headset to your Mac to see usage.";
                Layout();
                return;
            }
            cancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            answer = null;
            error = null;
            read = api.GetUsageLimitsAsync(cancellation.Token);
            Layout();
        }

        private void Layout()
        {
            if (root == null) return;
            toggle.Show(open ? "Close usage" : "Usage left", Vector2.zero, 0.22f);
            toggle.Accepting = () => !FocusGuard.InputSuspended;
            plate.SetActive(open);
            if (!open) return;
            var lines = read != null ? new[] { "Reading provider limits…" }
                : error != null ? new[] { error }
                : answer == null ? new[] { "Press Read again to see usage." }
                : UsageLimitsPresenter.Lines(answer, TimeZoneInfo.Local);
            WorkspaceVisuals.SetLiteralLines(body, lines);
            refresh.Show("Read again", new Vector2(0f, -0.18f), 0.28f);
            refresh.Accepting = () => !FocusGuard.InputSuspended && read == null;
        }

        private void Place()
        {
            var head = WorkspaceVisuals.Head;
            if (head == null) return;
            var forward = head.forward;
            var horizontal = new Vector3(forward.x, 0f, forward.z).normalized;
            if (horizontal.sqrMagnitude < 0.01f) horizontal = Vector3.forward;
            var right = new Vector3(head.right.x, 0f, head.right.z).normalized;
            if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            var position = head.position + horizontal * (open ? 0.78f : 0.43f)
                + right * (open ? 0.70f : 0.25f) + Vector3.down * (open ? 0.10f : 0.42f);
            root.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
            root.localScale = Vector3.one * (Vector3.Distance(head.position, position) / WorkspaceVisuals.PanelDistance);
            placed = true;
        }
    }
}
