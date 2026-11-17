#nullable enable
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The plane's handle (the lead's 2026-11-14T01:26Z ruling 3): a short pill a person takes, drags and lets go to move the plane,
    /// as a window on a desk moves in one gesture. It stands centred under what the plane draws, below the band at its tallest, so
    /// it never meets the row or the band and never moves while a person reads. At rest a quiet light pill; pointed, it brightens
    /// and widens a little; held, it stands whole and a touch smaller, so hover and press are felt. Its hold area is Meta's hand
    /// target tall. It drags by <see cref="PointerTarget.EnableDrag"/>, the same target the older views' Move used, so the plane
    /// wires it through MenuDrag as it stands (XR's half): <see cref="Target"/>'s Dragged, Selected and Released.
    /// </summary>
    /// <summary>How the plane's handle looks: at rest, pointed at, or held.</summary>
    public enum RoomHandleLook { Rest, Pointed, Held }

    public sealed class RoomHandle : MonoBehaviour
    {
        /// <summary>The pill's length and height at rest, in dp.</summary>
        public const float WidthDp = 120f;

        public const float BarDp = 8f;

        /// <summary>The hold area round it: the pill's length and 40 dp more, Meta's 60.8 dp hand target tall.</summary>
        public const float HoldWidthDp = WidthDp + 40f;

        public const float HoldHeightDp = 60.8f;

        /// <summary>Between the band's tallest reach and the handle's hold area, in dp.</summary>
        public const float GapDp = 8f;

        /// <summary>
        /// Between the panel's foot and the pill's top, in dp: just under its panel, as visionos-window-example.png's controls sit with
        /// their glass, never floating far below (AUDIT's kit pass at 9 (7)); the host stands it here once the band yields to the panel.
        /// </summary>
        public const float UnderPanelGapDp = 12f;

        /// <summary>The pill's light at rest, pointed and held (white over the room's glass).</summary>
        public const float RestAlpha = 0.45f;

        public const float PointedAlpha = 0.85f;

        public const float HeldAlpha = 1f;

        /// <summary>Pointed, the pill widens by this share; held, it narrows by the second.</summary>
        public const float PointedGrow = 0.12f;

        public const float HeldShrink = 0.04f;

        /// <summary>How fast the pill eases between its looks, a share a second (about 150 ms, as the room's actions).</summary>
        public const float EaseRate = 7f;

        private RoomShape pill = null!;
        private PointerTarget target = null!;
        private float alphaNow = RestAlpha;
        private float widthNow = 1f;

        /// <summary>What a person takes hold of: its Dragged, Selected and Released are the move's (XR wires them).</summary>
        public PointerTarget Target => target;

        /// <summary>Where a hand takes hold of it, in world space: the pill's middle (for the walks, which hold as a person does).</summary>
        public Vector3 HoldPoint => transform.position;

        /// <summary>Whether a hand holds it now.</summary>
        public bool Held => target.HeldPoint != null;

        /// <summary>For renders, which have no hands: the look to show in place of the one its state gives, or null.</summary>
        public RoomHandleLook? LookForRender { get; set; }

        public static RoomHandle Create(Transform parent, int order = KitType.Order.ControlWords)
        {
            var go = new GameObject("Plane handle");
            go.transform.SetParent(parent, false);
            var handle = go.AddComponent<RoomHandle>();
            handle.pill = RoomShape.Create(go.transform, "Handle pill", order);
            var hold = new GameObject("Handle hold");
            hold.transform.SetParent(go.transform, false);
            hold.transform.localPosition = new Vector3(0f, 0f, -RoomType.Units(0.7f));
            handle.target = PointerTarget.Rectangle(hold, new Vector2(RoomType.Units(HoldWidthDp), RoomType.Units(HoldHeightDp)), ray: true, poke: true);
            handle.target.EnableDrag();
            handle.Paint(immediately: true);
            return handle;
        }

        private void Update() => Paint(immediately: false);

        /// <summary>Draws it at its look at once, for a render.</summary>
        public void SettleForRender() => Paint(immediately: true);

        private void Paint(bool immediately)
        {
            var look = LookForRender ?? (Held ? RoomHandleLook.Held : target.Hovered ? RoomHandleLook.Pointed : RoomHandleLook.Rest);
            var (alpha, width) = look switch
            {
                RoomHandleLook.Held => (HeldAlpha, 1f - HeldShrink),
                RoomHandleLook.Pointed => (PointedAlpha, 1f + PointedGrow),
                RoomHandleLook.Rest => (RestAlpha, 1f),
                _ => throw new System.ArgumentOutOfRangeException(nameof(look), look, "a handle look the kit does not draw"),
            };
            var step = immediately ? 1f : Mathf.Clamp01(EaseRate * Time.unscaledDeltaTime);
            alphaNow = Mathf.Lerp(alphaNow, alpha, step);
            widthNow = Mathf.Lerp(widthNow, width, step);
            var size = new Vector2(RoomType.Units(WidthDp) * widthNow, RoomType.Units(BarDp));
            pill.Draw(size, size.y / 2f, new Color(1f, 1f, 1f, alphaNow));
        }
    }
}
