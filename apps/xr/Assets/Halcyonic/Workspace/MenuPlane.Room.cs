#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    public sealed partial class MenuPlane
    {
        private Transform? room;
        private readonly Dictionary<KitButton, Action> roomBarHandlers = new Dictionary<KitButton, Action>();
        private KitPress? wiredHandlePress;

        /// <summary>A picked project's screen in the room (ADR 0040, rule 2), while one is shown.</summary>
        public KitScreen? RoomScreen { get; private set; }

        /// <summary>The screen's bar beneath it, while a screen is shown.</summary>
        public KitScreenBar? RoomBar { get; private set; }

        /// <summary>The project id whose screen is currently shown, or null.</summary>
        public string? ScreenProjectId { get; private set; }

        /// <summary>Where <see cref="RoomLayout"/> stood the screen and its bar, in the room's metres.</summary>
        public RoomPose? ScreenPose { get; private set; }

        public RoomPose? BarPose { get; private set; }

        /// <summary>Raised when a button on <see cref="RoomBar"/> is pressed, with the action and current <see cref="ScreenProjectId"/>.</summary>
        public event Action<KitScreenAction, string?>? ScreenActed;

        /// <summary>Raised when the folded <see cref="RoomBar"/> handle is pressed to open the command bar.</summary>
        public event Action? ScreenBarToggled;

        /// <summary>
        /// Shows a picked project's screen on <paramref name="bearing"/> from <paramref name="eyes"/>, as wide as the kit's
        /// <see cref="SpatialKit.ScreenDegrees"/> at <see cref="RoomLayout.ScreenDistance"/>, with its bar beneath at the
        /// bar's own height, as wide as the screen and tilted to face the eyes. The kit draws both; this only stands them.
        /// </summary>
        public void ShowScreen(KitScreenModel model, Vector3 eyes, float bearing, string? projectId = null)
        {
            if (room == null)
            {
                room = new GameObject("Room of projects").transform;
                room.SetParent(transform, false);
            }
            if (RoomScreen == null) RoomScreen = KitScreen.Create(room);
            if (RoomBar == null) RoomBar = KitScreenBar.Create(room);
            RoomScreen.Show(model);
            var at = new System.Numerics.Vector3(eyes.x, eyes.y, eyes.z);
            var screen = RoomLayout.Screen(at, bearing, SpatialKit.ScreenDegreesAt(GlazeText.Scale), SpatialKit.ScreenHeightDegreesAt(GlazeText.Scale));
            // The kit lays out in units of the viewing distance, so each view is scaled by its own distance from the eyes.
            // The bar stands lower and so farther: laid out that much narrower, it keeps the screen's width in metres and
            // its targets and words their angles. Its height can change its row count, so it is laid out until it settles.
            var bar = RoomLayout.Bar(at, screen, SpatialKit.BarDegrees);
            for (var pass = 0; pass < 3; pass++)
            {
                RoomBar.Show(model, screen.Width / Reach(bar, eyes));
                var settled = RoomLayout.Bar(at, screen, 2f * Mathf.Atan(RoomBar.Size.y / 2f) * Mathf.Rad2Deg);
                if (settled.Equals(bar)) break;
                bar = settled;
            }
            Stand(RoomScreen.transform, screen, Reach(screen, eyes));
            Stand(RoomBar.transform, bar, Reach(bar, eyes));
            (ScreenProjectId, ScreenPose, BarPose) = (projectId, screen, bar);
            WireRoomBar();
        }

        private void UnwireRoomBar()
        {
            foreach (var pair in roomBarHandlers)
            {
                if (pair.Key != null) pair.Key.Pressed -= pair.Value;
            }
            roomBarHandlers.Clear();
            if (wiredHandlePress != null)
            {
                wiredHandlePress.Pressed -= OnRoomBarHandlePressed;
                wiredHandlePress = null;
            }
        }

        private void WireRoomBar()
        {
            UnwireRoomBar();
            if (RoomBar == null) return;
            foreach (var (action, button) in RoomBar.Shown)
            {
                if (button == null) continue;
                var captured = action;
                Action handler = () => ScreenActed?.Invoke(captured, ScreenProjectId);
                roomBarHandlers[button] = handler;
                button.Pressed += handler;
            }
            var handle = RoomBar.transform.Find("Handle")?.GetComponent<KitPress>();
            if (handle != null)
            {
                wiredHandlePress = handle;
                wiredHandlePress.Pressed += OnRoomBarHandlePressed;
            }
        }

        private void OnRoomBarHandlePressed() => ScreenBarToggled?.Invoke();

        /// <summary>Takes the screen and its bar away.</summary>
        public void HideScreen()
        {
            UnwireRoomBar();
            if (room != null)
            {
                room.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(room.gameObject);
                else DestroyImmediate(room.gameObject);
            }
            (room, RoomScreen, RoomBar, ScreenProjectId, ScreenPose, BarPose) = (null, null, null, null, null, null);
        }

        /// <summary>
        /// The room's screen and its bar, four corners each, as <see cref="Stand"/> stood them; null while no screen shows. A
        /// shelf keeps clear of them as of the console (LARGER-SHELF, 2026-10-31: the room's screen was left out before).
        /// </summary>
        private Vector3[]? RoomCorners()
        {
            if (room == null || !room.gameObject.activeInHierarchy || !(ScreenPose is RoomPose screen) || !(BarPose is RoomPose bar)) return null;
            var corners = new Vector3[8];
            Corners(screen, corners, 0);
            Corners(bar, corners, 4);
            return corners;
        }

        private static void Corners(RoomPose pose, Vector3[] corners, int from)
        {
            var centre = new Vector3(pose.Centre.X, pose.Centre.Y, pose.Centre.Z);
            var facing = PlaneLayout.Facing(new PanelDirection(pose.Bearing, pose.Pitch, true, false));
            for (var corner = 0; corner < 4; corner++)
                corners[from + corner] = centre + facing * new Vector3((corner % 2 == 0 ? -0.5f : 0.5f) * pose.Width, (corner < 2 ? -0.5f : 0.5f) * pose.Height, 0f);
        }

        private static float Reach(RoomPose pose, Vector3 eyes) => Vector3.Distance(new Vector3(pose.Centre.X, pose.Centre.Y, pose.Centre.Z), eyes);

        private void Stand(Transform view, RoomPose pose, float scale)
        {
            view.SetPositionAndRotation(new Vector3(pose.Centre.X, pose.Centre.Y, pose.Centre.Z),
                PlaneLayout.Facing(new PanelDirection(pose.Bearing, pose.Pitch, true, false)));
            var parent = view.parent != null ? view.parent.lossyScale.x : 1f;
            view.localScale = Vector3.one * (scale / parent);
        }
    }
}
