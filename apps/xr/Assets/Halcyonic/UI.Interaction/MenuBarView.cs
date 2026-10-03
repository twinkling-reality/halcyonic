#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The menu closed (ADR 0026): one rounded glass shape, the menu's width, on the plane's top line.
    /// Its line at 18 dp says what waits, in amber with the waiting icon, or that nothing is waiting,
    /// and Open stands at its right as a prompt's cap and words. The whole shape is one place to press,
    /// which raises <see cref="MenuBar.Open"/>. Built in units of the plane's distance at the designed
    /// size, centred on its transform.
    /// </summary>
    public sealed class MenuBarView : MonoBehaviour
    {
        private const int Order = 20;

        private Surface shape = null!;
        private GlazeButton target = null!;
        private TextMeshPro icon = null!;
        private TextMeshPro line = null!;
        private Surface cap = null!;
        private TextMeshPro capIcon = null!;
        private TextMeshPro open = null!;
        private Vector2 size;

        /// <summary>The bar was pressed: <see cref="MenuBar.Open"/>.</summary>
        public event Action<string>? Acted;

        /// <summary>The bar's size, in units of the plane's distance at the designed size.</summary>
        public Vector2 Size => size;

        /// <summary>The bar's one place to press, for the renders' checks.</summary>
        public GlazeButton Target => target;

        /// <summary>A closed bar's height: a target's, and room above and below it.</summary>
        public static float Height => GlazeButton.HeightOf(false) + 2f * GlazeTokens.Units(Glaze.Menu.PromptMarginDegrees);

        public static MenuBarView Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MenuBarView>();
            view.shape = Surface.Create(go.transform, "Shape", Order);
            view.target = GlazeButton.Create(go.transform, "Open the menu", ButtonRole.Row, compact: false, order: Order + 1);
            view.target.Pressed += () => view.Acted?.Invoke(MenuBar.Open);
            view.icon = GlazeIcons.Create(go.transform, "Waits", Glaze.Menu.BodyDegrees, GlazeTokens.Text, Order + 3);
            view.line = GlazeText.Create(go.transform, "Line", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, Order + 3);
            view.line.rectTransform.pivot = new Vector2(0f, 0.5f);
            view.cap = Surface.Create(go.transform, "Cap", Order + 2);
            view.capIcon = GlazeIcons.Create(go.transform, "Cap icon", Glaze.Menu.PromptIconDegrees, GlazeTokens.Text, Order + 3);
            view.open = GlazeText.Create(go.transform, "Open", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, Order + 3);
            view.open.rectTransform.pivot = new Vector2(0f, 0.5f);
            view.open.textWrappingMode = TextWrappingModes.NoWrap;
            return view;
        }

        /// <summary>Shows <paramref name="bar"/> closed, <paramref name="columnDegrees"/> wide.</summary>
        public void Show(MenuBar bar, float columnDegrees)
        {
            gameObject.SetActive(true);
            float U(float degrees) => GlazeTokens.Units(degrees);
            size = new Vector2(PlaneComposition.Units(columnDegrees), Height);
            shape.DrawGlass(size);
            target.ShowArea(Vector2.zero, size);
            var padding = U(Glaze.Menu.PaddingDegrees);
            var grid = U(Glaze.Menu.GridDegrees);
            var left = -size.x / 2f + padding;
            var right = size.x / 2f - padding;

            // Open at the right: its cap, a grid step, its words.
            GlazeText.SetLiteral(open, "Open");
            var words = open.GetPreferredValues(open.text).x;
            GlazeText.Lay(open, words + U(0.1f), 1);
            open.transform.localPosition = new Vector3(right - words, 0f, -U(0.05f));
            var capSize = U(Glaze.Menu.PromptCapDegrees);
            var capX = right - words - grid - capSize / 2f;
            cap.Draw(Vector2.one * capSize, capSize / 2f, Color.clear, new Color(1f, 1f, 1f, Glaze.Menu.CapOutlineOpacity), U(Glaze.Menu.CapOutlineDegrees));
            cap.transform.localPosition = new Vector3(capX, 0f, -U(0.04f));
            GlazeIcons.Show(capIcon, GlazeIcon.Next);
            capIcon.transform.localPosition = new Vector3(capX, 0f, -U(0.05f));

            // What waits, in amber with its icon, or that nothing does.
            var waits = false;
            foreach (var place in MenuBar.Places) waits |= bar.Waits(place);
            var colour = waits ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground) : GlazeTokens.Text;
            icon.gameObject.SetActive(waits);
            var start = left;
            if (waits)
            {
                GlazeIcons.Show(icon, GlazeIcon.WaitingForYou);
                icon.color = colour;
                icon.transform.localPosition = new Vector3(left + U(Glaze.Menu.IconColumnDegrees) / 2f, 0f, -U(0.05f));
                start = left + U(Glaze.Menu.IconColumnDegrees) + grid;
            }
            line.color = colour;
            GlazeText.SetLiteral(line, bar.ClosedLine);
            GlazeText.Lay(line, capX - capSize / 2f - grid - start, 1);
            line.transform.localPosition = new Vector3(start, 0f, -U(0.05f));
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
