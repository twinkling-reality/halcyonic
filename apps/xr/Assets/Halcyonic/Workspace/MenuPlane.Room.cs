#nullable enable
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    public sealed partial class MenuPlane
    {
        private Transform? room;

        /// <summary>A picked project's screen in the room (ADR 0040, rule 2), while one is shown.</summary>
        public KitScreen? RoomScreen { get; private set; }

        /// <summary>The screen's bar beneath it, while a screen is shown.</summary>
        public KitScreenBar? RoomBar { get; private set; }

        /// <summary>Where <see cref="RoomLayout"/> stood the screen and its bar, in the room's metres.</summary>
        public RoomPose? ScreenPose { get; private set; }

        public RoomPose? BarPose { get; private set; }

        /// <summary>
        /// Shows a picked project's screen on <paramref name="bearing"/> from <paramref name="eyes"/>, as wide as the kit's
        /// <see cref="SpatialKit.ScreenDegrees"/> at <see cref="RoomLayout.ScreenDistance"/>, with its bar beneath at the
        /// bar's own height, as wide as the screen and tilted to face the eyes. The kit draws both; this only stands them.
        /// </summary>
        public void ShowScreen(KitScreenModel model, Vector3 eyes, float bearing)
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
            var screen = RoomLayout.Screen(at, bearing, SpatialKit.ScreenDegrees, SpatialKit.ScreenHeightDegrees);
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
            (ScreenPose, BarPose) = (screen, bar);
        }

        /// <summary>Takes the screen and its bar away.</summary>
        public void HideScreen()
        {
            if (room != null)
            {
                room.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(room.gameObject);
                else DestroyImmediate(room.gameObject);
            }
            (room, RoomScreen, RoomBar, ScreenPose, BarPose) = (null, null, null, null, null);
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
