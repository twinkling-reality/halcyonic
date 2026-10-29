#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The bar beneath a picked project's screen (ADR 0040, rule 2; R03 YouTube VR, R04 visionOS ornaments, R17 hidden until
    /// wanted): one opaque bar as wide as the screen, its primary action and the other actions at the left, the quiet doors at
    /// the right, a second row only where the person's text size needs it. Folded, it is a handle alone. Each button only asks
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
                buttons[action] = button = KitButton.Create(carrier.transform, KitScreenModel.Words(action), KitButtonRole.Quiet);
            return button;
        }

        private static bool IsDoor(KitScreenAction action) =>
            action == KitScreenAction.Steps || action == KitScreenAction.Why || action == KitScreenAction.HowItWent;

        /// <summary>Shows the bar <paramref name="width"/> wide, the screen's own width, for <paramref name="model"/>.</summary>
        public void Show(KitScreenModel model, float width)
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
            float Measure(KitScreenAction action) => ButtonFor(action).Measure(KitScreenModel.Words(action), null);
            float Span(IEnumerable<KitScreenAction> actions) => actions.Sum(Measure) + gap * System.Math.Max(0, actions.Count() - 1);
            var inner = width - 2f * padX;
            var leadSpan = Span(lead);
            var doorSpan = Span(doors);
            Rows = leadSpan + doorSpan + 2f * gap <= inner ? 1 : 2;
            var padY = (KitType.Units(SpatialKit.BarDegrees) - target) / 2f;
            var height = Rows * target + (Rows - 1) * gap + 2f * padY;
            Size = new Vector2(width, height);
            carrier.Draw(Size, KitType.Units(SpatialKit.ControlRadiusDegrees), SpatialKit.Carrier);

            var top = height / 2f - padY - target / 2f;
            var x = -width / 2f + padX;
            foreach (var action in lead)
            {
                var button = ButtonFor(action);
                button.Role = action == model.Primary ? KitButtonRole.Primary : KitButtonRole.Secondary;
                var w = Measure(action);
                button.Show(KitScreenModel.Words(action), null, new Vector2(x + w / 2f, top), w);
                shown.Add((action, button));
                x += w + gap;
            }
            var doorsY = Rows == 1 ? top : top - target - gap;
            var right = width / 2f - padX;
            foreach (var action in Enumerable.Reverse(doors))
            {
                var button = ButtonFor(action);
                button.Role = KitButtonRole.Quiet;
                var w = Measure(action);
                button.Show(KitScreenModel.Words(action), null, new Vector2(right - w / 2f, doorsY), w);
                shown.Insert(lead.Count, (action, button));
                right -= w + gap;
            }
            gameObject.SetActive(true);
        }
    }
}
