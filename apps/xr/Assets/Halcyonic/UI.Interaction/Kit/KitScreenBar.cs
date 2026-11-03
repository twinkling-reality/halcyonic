#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The bar beneath a picked project's screen (ADR 0040, rule 2; R03 YouTube VR, R04 visionOS ornaments, R17 hidden until
    /// wanted): one opaque bar as wide as the screen, its actions gathered toward its middle, the primary first and the quiet
    /// doors after a wider gap, a second row only where the person's text size needs it. Folded, it is a handle alone. Each button only asks
    /// its owner (<see cref="KitPress"/>). Centred on its transform; where it stands is the placement's.
    /// </summary>
    public sealed class KitScreenBar : MonoBehaviour
    {
        private KitCarrier carrier = null!;
        private Surface handle = null!;
        private readonly Dictionary<KitScreenAction, KitButton> buttons = new Dictionary<KitScreenAction, KitButton>();
        private readonly List<(KitScreenAction Action, KitButton Button)> shown = new List<(KitScreenAction, KitButton)>();

        public Vector2 Size { get; private set; }

        public KitCarrier Carrier => carrier;

        public Surface Handle => handle;

        public int Rows { get; private set; }

        /// <summary>The actions standing now, each with its button, in their order.</summary>
        public IReadOnlyList<(KitScreenAction Action, KitButton Button)> Shown => shown;

        public IEnumerable<KitPress> Targets => shown.Select(each => each.Button.Press);

        public IEnumerable<KitCarrier> Carriers => carrier.gameObject.activeSelf ? new[] { carrier } : new KitCarrier[0];

        public static KitScreenBar Create(Transform parent)
        {
            var go = new GameObject("Screen bar");
            go.transform.SetParent(parent, false);
            var bar = go.AddComponent<KitScreenBar>();
            bar.carrier = KitCarrier.Create(go.transform, "Bar");
            bar.handle = Surface.Create(go.transform, "Handle", KitType.Order.Plate);
            return bar;
        }

        private KitButton ButtonFor(KitScreenAction action)
        {
            if (!buttons.TryGetValue(action, out var button))
                buttons[action] = button = KitButton.Create(carrier.transform, KitScreenModel.Words(action), KitButtonRole.Quiet, room: true);
            return button;
        }

        private static bool IsDoor(KitScreenAction action) =>
            action == KitScreenAction.Steps || action == KitScreenAction.Why || action == KitScreenAction.HowItWent;

        /// <summary>
        /// Shows the bar <paramref name="width"/> wide, the screen's own width, for <paramref name="model"/>. Its actions gather
        /// toward its middle, as YouTube VR's controls do, and each grows by how much farther and more slanted its outer
        /// corner stands than the point where the bar faces the eyes, <paramref name="facing"/> from the bar's centre in units
        /// of the distance (zero where the bar is turned to face the eyes itself): every target holds the floor at its true
        /// distance, not only along the bar's normal.
        /// </summary>
        public void Show(KitScreenModel model, float width, Vector2 facing = default)
        {
            foreach (var button in buttons.Values) button.Hide();
            shown.Clear();
            var target = KitButton.Height;
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var padX = KitType.Units(SpatialKit.PaddingDegrees);
            if (model.Folded)
            {
                carrier.gameObject.SetActive(false);
                var length = KitType.Units(SpatialKit.HandleDegrees);
                var thick = KitType.Units(SpatialKit.HandleThicknessDegrees);
                handle.gameObject.SetActive(true);
                handle.Draw(new Vector2(length, thick), thick / 2f, KitType.Colour(SpatialKit.TextSecondary));
                handle.transform.localPosition = Vector3.zero;
                Size = new Vector2(width, thick);
                Rows = 0;
                return;
            }
            handle.gameObject.SetActive(false);
            carrier.gameObject.SetActive(true);

            var lead = model.Shown.Where(action => !IsDoor(action)).ToList();
            var doors = model.Shown.Where(IsDoor).ToList();
            // Each button takes its role before it is measured: the primary's words stand further in than a quiet door's.
            foreach (var action in model.Shown)
                ButtonFor(action).Role = action == model.Primary ? KitButtonRole.Primary : lead.Contains(action) ? KitButtonRole.Secondary : KitButtonRole.Quiet;
            var natural = model.Shown.ToDictionary(action => action, action => ButtonFor(action).Measure(KitScreenModel.Words(action), null));
            var groupGap = 3f * gap;
            var inner = width - 2f * padX;
            var padY = (KitType.Units(SpatialKit.BarDegrees) - target) / 2f;
            // One scale for the whole bar, the most any of its buttons needs to hold the floor where it stands, so every word
            // stands at one size (the owner's one type scale): one row where it fits the bar's inside at that scale, else
            // the lead actions above the doors.
            var scale = 1f;
            float Span(IList<KitScreenAction> row, bool split) =>
                row.Sum(action => natural[action] * scale) + gap * Mathf.Max(0, row.Count - 1) + (split ? groupGap - gap : 0f);
            var centres = new Dictionary<KitScreenAction, Vector2>();
            var height = 0f;
            var rows = new List<List<KitScreenAction>>();
            void LayOut()
            {
                var rowHeight = target * scale;
                height = rows.Count * rowHeight + gap * (rows.Count - 1) + 2f * padY;
                var y = height / 2f - padY;
                foreach (var row in rows)
                {
                    var split = rows.Count == 1 && lead.Count > 0 && doors.Count > 0;
                    var x = -Span(row, split) / 2f;
                    for (var k = 0; k < row.Count; k++)
                    {
                        var w = natural[row[k]] * scale;
                        centres[row[k]] = new Vector2(x + w / 2f, y - rowHeight / 2f);
                        x += w + (split && k == lead.Count - 1 ? groupGap : gap);
                    }
                    y -= rowHeight + gap;
                }
            }
            Rows = 1;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                rows = Rows == 1 ? new List<List<KitScreenAction>> { model.Shown.ToList() } : new List<List<KitScreenAction>> { lead, doors };
                scale = 1f;
                // Lay out, grow the bar's scale for where its buttons stand, and lay out again: three rounds settle it, and the
                // buttons stand where the settled scale lays them.
                for (var round = 0; round < 3; round++)
                {
                    LayOut();
                    var needed = 1f;
                    foreach (var action in model.Shown)
                    {
                        var c = centres[action];
                        var outer = Mathf.Abs(c.x - facing.x) + natural[action] * scale / 2f;
                        var edge = Mathf.Abs(c.y - facing.y) + target * scale / 2f;
                        needed = Mathf.Max(needed, 1f / PlaneComposition.ShrinkAt(outer, edge));
                    }
                    scale = needed;
                }
                LayOut();
                if (Rows == 1 && lead.Count > 0 && doors.Count > 0 && Span(rows[0], true) > inner + 1e-5f)
                {
                    Rows = 2;
                    continue;
                }
                break;
            }
            Size = new Vector2(width, height);
            carrier.Draw(Size, KitType.Units(SpatialKit.ControlRadiusDegrees), SpatialKit.Carrier);
            foreach (var action in lead.Concat(doors))
            {
                var button = ButtonFor(action);
                // The bar's growth is the button's own base, so pointing at it grows it from there and never loses it.
                button.BaseScale = scale;
                button.Show(KitScreenModel.Words(action), null, centres[action], natural[action]);
                shown.Add((action, button));
            }
            gameObject.SetActive(true);
        }
    }
}
