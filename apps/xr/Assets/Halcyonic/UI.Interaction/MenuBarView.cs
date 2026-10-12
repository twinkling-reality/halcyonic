#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The menu closed (ADR 0026): one rounded glass shape, the menu's width, on the plane's top line.
    /// Its line at 18 dp identifies the retained composition. Move and Expand are separate named targets;
    /// Expand raises <see cref="MenuBar.Open"/>. Built in units of the plane's distance at the designed
    /// size, centred on its transform.
    /// </summary>
    public sealed class MenuBarView : MonoBehaviour
    {
        private const int Order = 20;

        private Surface shape = null!;
        private TextMeshPro line = null!;
        private OrganizationControlsView controls = null!;
        private TextMeshPro state = null!;
        private readonly List<Surface> identityGrounds = new List<Surface>();
        private Vector2 size;
        private float shown = 1f;
        private readonly List<Surface> shapes = new List<Surface>();
        private readonly List<TMP_Text> words = new List<TMP_Text>();

        /// <summary>The bar was pressed: <see cref="MenuBar.Open"/>.</summary>
        public event Action<string>? Acted;

        /// <summary>The bar's size, in units of the plane's distance at the designed size.</summary>
        public Vector2 Size => size;

        /// <summary>The bar's one place to press, for the renders' checks.</summary>
        public GlazeButton Target => controls.Toggle;
        public OrganizationControlsView Controls => controls;

        /// <summary>
        /// How far into view the bar is, from 0 to 1, as it appears where a file or the menu closed: by opacity
        /// alone, its shapes and its words alike. It takes no press until it shows whole.
        /// </summary>
        public float Shown
        {
            get => shown;
            set
            {
                shown = Mathf.Clamp01(value);
                GetComponentsInChildren(true, shapes);
                for (var index = 0; index < shapes.Count; index++) shapes[index].Shown = shown;
                GetComponentsInChildren(true, words);
                for (var index = 0; index < words.Count; index++) GlazeText.Show(words[index], shown);
            }
        }

        /// <summary>A closed bar's height: a target's, and room above and below it.</summary>
        public static float Height => GlazeButton.HeightOf(false) + 2f * GlazeTokens.Units(Glaze.Menu.PromptMarginDegrees);

        public static MenuBarView Create(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<MenuBarView>();
            view.shape = Surface.Create(go.transform, "Shape", Order);
            view.controls = OrganizationControlsView.Create(go.transform);
            view.controls.Accepting = () => view.shown >= 1f && !FocusGuard.InputSuspended;
            view.controls.Toggled += () => view.Acted?.Invoke(MenuBar.Open);
            view.line = GlazeText.Create(go.transform, "Retained identity", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, Order + 3);
            view.line.rectTransform.pivot = new Vector2(0f, 0.5f);
            view.state = GlazeText.Create(go.transform, "Retained state", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.Left, Order + 3);
            view.state.rectTransform.pivot = new Vector2(0f, 0.5f);
            return view;
        }

        /// <summary>Shows <paramref name="bar"/> closed, <paramref name="columnDegrees"/> wide.</summary>
        public void Show(MenuBar bar, float columnDegrees, LineWords? identity = null, string? status = null)
        {
            controls.Bind(identity ?? (object)bar);
            gameObject.SetActive(true);
            size = new Vector2(PlaneComposition.Units(columnDegrees), Height);
            shape.DrawGlass(size);
            var padding = GlazeTokens.Units(Glaze.Menu.PaddingDegrees);
            var left = -size.x / 2f + padding;
            var right = size.x / 2f - padding;
            controls.Show(right, 0f, compact: true);
            var room = right - left - OrganizationControlsView.Width(compact: true) - MenuPage.TargetGap;
            var words = identity ?? LineWords.Ours(bar.ClosedLine);
            var theirs = GlazeRuns.Of(line).Set(words, _ => true);
            GlazeText.Lay(line, room, 1);
            line.transform.localPosition = new Vector3(left, status == null ? 0f : -GlazeText.LineHeight(line) / 2f, -GlazeTokens.Units(0.05f));
            GlazeRuns.TokenGrounds(line, theirs, identityGrounds, transform, Order + 1);
            state.gameObject.SetActive(status != null);
            if (status != null)
            {
                GlazeText.SetLiteral(state, status);
                GlazeText.Lay(state, room, 1);
                state.transform.localPosition = new Vector3(left, GlazeText.LineHeight(line) / 2f, -GlazeTokens.Units(0.05f));
            }
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
