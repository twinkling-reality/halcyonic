#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The redesign's actions (DESIGN's BUILD-SPEC-v4 1.4, unit 1c) in every look, stacked left-aligned at their 72 dp pitch as
    /// the choices column stands them, over the dark and the bright room: at rest, the main one, pointed, the main one
    /// pointed, held, and Confirm with its lock half filled (an action that cannot act is not drawn: no disabled look). Each is drawn in the room's halo, never a box; its
    /// words whole in the one size; its target Meta's hand target at least, 12 mm from the next.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ActionStates = { "32-actions" };

        private static Shot ActionsShot(string state, Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "The redesign's actions in every look: rest, main, pointed, main pointed, held, Confirm locking, synthetic data" };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var holder = new GameObject("Actions").transform;
            holder.SetParent(root, false);
            var looks = new (string Words, GlazeIcon Icon, bool Main, bool Available, RoomButtonLook? Look, float? Lock, RoomButtonLook Expected)[]
            {
                ("Approve", GlazeIcon.Approve, true, true, null, null, RoomButtonLook.Main),
                ("Deny", GlazeIcon.Deny, false, true, null, null, RoomButtonLook.Rest),
                ("Changes", GlazeIcon.Next, false, true, RoomButtonLook.Pointed, null, RoomButtonLook.Pointed),
                ("Make VR Version", GlazeIcon.Next, true, true, RoomButtonLook.MainPointed, null, RoomButtonLook.MainPointed),
                ("Checks", GlazeIcon.Next, false, true, RoomButtonLook.Pressed, null, RoomButtonLook.Pressed),
                ("Confirm", GlazeIcon.Approve, false, true, null, 0.5f, RoomButtonLook.Rest),
            };
            // DESIGN's 72 dp pitch keeps 12 mm between targets at 1.0 m; the plane stands at 0.46 m, so the pitch is the target
            // and the kit's own 12 mm there (SpatialKit.TargetGapDegrees).
            var pitch = RoomType.Units(RoomButton.HeightDp) + KitType.Units(SpatialKit.TargetGapDegrees);
            var buttons = new List<RoomButton>();
            for (var index = 0; index < looks.Length; index++)
            {
                var (words, icon, main, available, look, locking, expected) = looks[index];
                var button = RoomButton.Create(holder, words);
                button.LookForRender = look;
                button.LockProgress = locking;
                var width = button.Measure(words);
                // Left-aligned in the column, one under another, the main one first.
                button.Show(words, icon, main, available, new Vector2(width / 2f, -index * pitch), 0f);
                button.Press.SettleForRender();
                buttons.Add(button);
                Require(button.LookNow() == expected, words + " shows " + button.LookNow() + ", not " + expected);
                Require(button.Size.y >= RoomType.Units(RoomButton.HeightDp) - 1e-5f && button.Size.x >= RoomType.Units(RoomButton.HeightDp) - 1e-5f,
                    words + "'s target is under Meta's hand target");
                button.Words.ForceMeshUpdate(true);
                Require(!button.Words.isTextTruncated && button.Words.textInfo.lineCount == 1, words + "'s words are cut");
                var renderer = button.GetComponentInChildren<MeshRenderer>();
                Require(renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "Halcyonic/Room Halo", words + " is not drawn in the room's halo");
            }
            for (var index = 1; index < buttons.Count; index++)
            {
                var gap = (buttons[index - 1].transform.localPosition.y - buttons[index - 1].Size.y / 2f) - (buttons[index].transform.localPosition.y + buttons[index].Size.y / 2f);
                Require(gap >= KitType.Units(SpatialKit.TargetGapDegrees) - 1e-5f, buttons[index - 1].name + " and " + buttons[index].name + "'s targets stand under 12 mm apart");
            }
            var height = (looks.Length - 1) * pitch + RoomType.Units(RoomButton.HeightDp);
            var widest = buttons.Max(button => button.Size.x);
            holder.localPosition = new Vector3(-widest / 2f, height / 2f - RoomType.Units(RoomButton.HeightDp) / 2f, 0f);
            var view = new GameObject("Actions view").transform;
            view.SetParent(root, false);
            holder.SetParent(view, false);
            shot.Elevation = 0f;
            shot.Centre = Place(view, eyes, 0f, shot.Elevation);
            Stand(shot, view, eyes, new KitCarrier[0], new Vector2(widest, height));
            foreach (var button in buttons)
            {
                var half = button.Size / 2f;
                foreach (var corner in new[] { new Vector3(-half.x, -half.y, 0f), new Vector3(half.x, -half.y, 0f), new Vector3(-half.x, half.y, 0f), new Vector3(half.x, half.y, 0f) })
                    shot.Corners.Add(button.transform.TransformPoint(corner));
            }
            shot.Targets.Add(("the actions", view, buttons.Select(button => button.Press)));
            return shot;
        }
    }
}
