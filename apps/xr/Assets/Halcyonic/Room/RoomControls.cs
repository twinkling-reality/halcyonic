#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.Workspace;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Room
{
    /// <summary>Explicit room and optional shelf controls. No window position is guessed and no placement enables agents before Apply.</summary>
    internal sealed class RoomControls : MonoBehaviour, IRoomSettings
    {
        private RoomPlacement placement = null!;
        private WorkspaceDirector? director;
        private AgentShelfColumn? column;
        private AgentShelfDrag? drag;
        private Transform? handleRoot;
        private GlazeButton? handle;
        public static RoomControls Create(Transform parent, RoomPlacement placement)
        {
            var go = new GameObject("Room controls");
            go.transform.SetParent(parent, false);
            var controls = go.AddComponent<RoomControls>();
            controls.placement = placement;
            controls.director = placement.GetComponent<WorkspaceDirector>();
            if (controls.director != null) controls.director.Room = controls;
            placement.Draft.Changed += controls.PreviewChanged;
            return controls;
        }
        public RoomStatus Status => placement.Status;
        public RoomOffer Offer => placement.Offer;
        public bool AgentsEnabled => director != null && director.Presentation.AgentsEnabled;
        public bool ShelfPlaced => placement.Preferred.HasValue;
        public BoundaryNow? Boundary => placement.Boundary;
        public void ToggleBoundary() { if (!FocusGuard.InputSuspended) placement.ToggleBoundary(); }
        public void Present() { }
        public void SwitchSpace()
        {
            if (!FocusGuard.InputSuspended && placement.Status.CanSwitch)
                placement.SetSpace(placement.Status.Shown == RoomSpace.Room ? RoomSpace.Virtual : RoomSpace.Room);
        }
        public void TakeOffer() { if (FocusGuard.InputSuspended) return; if (placement.Draft.Candidate == null) { PlaceAgents(); return; } placement.TakeOffer(); }
        public void ToggleAgents()
        {
            if (director == null || FocusGuard.InputSuspended || director.Presentation.Minimized) return;
            if (!AgentsEnabled && !ShelfPlaced) { PlaceAgents(); return; }
            SetEnabled(!AgentsEnabled);
        }
        private void SetEnabled(bool value)
        {
            if (director == null || value && !ShelfPlaced) return;
            director.Presentation.SetAgentsEnabled(value);
        }
        public void PlaceAgents() => Place(null, null, null);
        public void PlaceTask(string workstreamId, string title, System.Func<bool> current) => Place(workstreamId, title, current);
        private void Place(string? workstreamId, string? title, System.Func<bool>? current)
        {
            if (director == null || FocusGuard.InputSuspended || director.Presentation.Minimized || director.MenuHost == null || current?.Invoke() == false) return;
            director.EnsureAgentStage();
            if (!placement.BeginPreview())
            {
                director.ReportLocal("No shelf position fits beside the workspace in view. Move the workspace, then choose Place agents again.");
                return;
            }
            drag?.End();
            column = new AgentShelfColumn(director.MenuHost, placement.Draft, direction => { drag?.End(); placement.MovePreview(direction); },
                () =>
                {
                    if (current?.Invoke() == false) { placement.CancelPreview(); return; }
                    if (!placement.ApplyPreview()) return;
                    if (workstreamId != null) director.EnsureAgentStage().Request(workstreamId);
                    SetEnabled(true);
                }, placement.CancelPreview, placement.FindRoomSurface, title, current);
            if (!director.OpenLocal(column)) column.FocusLeft();
            else { MakeHandle(); PreviewChanged(); }
        }
        private void MakeHandle()
        {
            if (handle != null) return;
            handleRoot = new GameObject("Shelf movement handle").transform;
            handleRoot.SetParent(transform, false);
            handle = GlazeButton.Create(handleRoot, "Move shelf", ButtonRole.Secondary);
            handle.Holds = true;
            handle.Show("Move shelf", Vector2.zero, 0.3f, withIcon: GlazeIcon.Move);
            handle.Target.EnableDrag();
            handle.Accepting = () => CanMove() && (drag?.Active == true || column?.CurrentDraw == true);
            handle.HoldStarted += () =>
            {
                if (!CanMove() || column?.CurrentDraw != true || handle.Target.HeldPoint is not Vector3 point) return;
                placement.StopRoomSuggestion();
                drag ??= new AgentShelfDrag(placement.Draft);
                drag.Begin(new System.Numerics.Vector3(point.x, point.y, point.z), currentDraw: true);
            };
            handle.Target.Dragged += point => drag?.Follow(new System.Numerics.Vector3(point.x, point.y, point.z), CanMove());
            handle.HoldEnded += _ => drag?.End();
        }
        private bool CanMove() => director != null && !FocusGuard.InputSuspended && !director.Presentation.Minimized && column?.Active == true;
        private void PreviewChanged()
        {
            if (director != null) director.EnsureAgentStage().PreviewShelf(placement.Preview);
            if (handleRoot == null) return;
            var shown = placement.Preview;
            handleRoot.gameObject.SetActive(shown.HasValue && CanMove());
            if (shown is not Pose pose) { drag?.End(); return; }
            handleRoot.SetPositionAndRotation(pose.position - pose.forward * (AgentShelfDraft.Depth / 2f + 0.03f) + Vector3.up * 0.03f, pose.rotation);
        }
        private void Update()
        {
            if (CanMove()) return;
            drag?.End();
            if (handleRoot != null) handleRoot.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            if (placement != null) { placement.Draft.Changed -= PreviewChanged; placement.CancelPreview(); }
            if (director != null && ReferenceEquals(director.Room, this)) director.Room = null;
        }
    }
}
