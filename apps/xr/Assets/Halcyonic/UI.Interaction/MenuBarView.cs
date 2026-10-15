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
        private Surface identityEdge = null!;
        private TaskIdentityAvatarView portrait = null!;
        public TaskIdentityAvatarView TaskIdentity => portrait;
        private TextMeshPro line = null!;
        private OrganizationControlsView controls = null!;
        private TextMeshPro state = null!;
        private StateBadgeView badge = null!;
        public StateBadgeView Badge => badge;
        public TextMeshPro WaitingCount => state;
        private readonly List<Surface> identityGrounds = new List<Surface>();
        private Vector2 size;
        private float shown = 1f;
        private readonly List<Surface> shapes = new List<Surface>();
        private readonly List<TMP_Text> words = new List<TMP_Text>();

        /// <summary>The bar was pressed: <see cref="MenuBar.Open"/>.</summary>
        public event Action<string>? Acted;

        /// <summary>The bar's size, in units of the plane's distance at the designed size.</summary>
        public Vector2 Size => size;
        public Surface Shape => shape;

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
                for (var index = 0; index < shapes.Count; index++) if (!shapes[index].transform.IsChildOf(badge.transform)) shapes[index].Shown = shown;
                GetComponentsInChildren(true, words);
                for (var index = 0; index < words.Count; index++) if (!words[index].transform.IsChildOf(badge.transform)) GlazeText.Show(words[index], shown);
                badge.Fade(shown);
                // CharacterBody has no UI opacity contract. Reveal only with the complete
                // compact object, and hide immediately when it begins leaving.
                if (portrait != null) portrait.gameObject.SetActive(shown >= 1f && portrait.WorkstreamId != null);
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
            view.identityEdge = Surface.Create(go.transform, "Retained work identity edge", Order + 1);
            view.portrait = TaskIdentityAvatarView.Create(go.transform);
            view.controls = OrganizationControlsView.Create(go.transform);
            view.controls.Accepting = () => view.shown >= 1f && !FocusGuard.InputSuspended;
            view.controls.Toggled += () => view.Acted?.Invoke(MenuBar.Open);
            view.line = GlazeText.Create(go.transform, "Retained identity", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, Order + 3);
            view.line.rectTransform.pivot = new Vector2(0f, 0.5f);
            view.badge = StateBadgeView.Create(go.transform, "Retained state", Order + 2, pill: true);
            view.state = GlazeText.Create(go.transform, "Retained state", GlazeType.Body, GlazeTokens.TextSecondary, TextAlignmentOptions.Left, Order + 3);
            view.state.rectTransform.pivot = new Vector2(0f, 0.5f);
            return view;
        }

        /// <summary>Shows <paramref name="bar"/> closed, <paramref name="columnDegrees"/> wide.</summary>
        public void Show(MenuBar bar, float columnDegrees, LineWords? identity = null, StateBadge? status = null, int waitingCount = 0, object? compactBinding = null, CharacterPresentation? taskPortrait = null, double? portraitHue = null)
        {
            controls.Bind(compactBinding ?? (object)bar);
            gameObject.SetActive(true);
            size = new Vector2(PlaneComposition.Units(columnDegrees), Height);
            shape.DrawCoatedInstrument(size, GlazeTokens.Units(0.6f), GlazeTokens.ColorOf(Glaze.Panel));
            var edgeWidth = GlazeTokens.Units(0.4f);
            identityEdge.DrawCoatedInstrument(new Vector2(edgeWidth, size.y - MenuPage.Grid), edgeWidth / 3f, GlazeTokens.ColorOf(Glaze.Palette.Raised));
            identityEdge.transform.localPosition = new Vector3(-size.x / 2f + edgeWidth / 2f, 0f, -0.0005f);
            var padding = GlazeTokens.Units(Glaze.Menu.PaddingDegrees);
            var portraitWidth = taskPortrait != null ? MenuFrameView.PortraitWidth : 0f;
            if (taskPortrait != null)
                portrait.Show(taskPortrait, portraitHue, new Vector2(-size.x / 2f + padding + portraitWidth / 2f, 0f),
                    new Vector2(portraitWidth, size.y - 2f * MenuPage.Grid));
            else portrait.Hide();
            var left = -size.x / 2f + padding + (taskPortrait != null ? portraitWidth + MenuPage.TargetGap : 0f);
            var right = size.x / 2f - padding;
            controls.Show(right, 0f, compact: true);
            var room = right - left - OrganizationControlsView.Width(compact: true) - MenuPage.TargetGap;
            var words = identity ?? LineWords.Ours(bar.ClosedLine);
            var theirs = GlazeRuns.Of(line).Set(words, _ => true);
            GlazeText.Lay(line, room, 1);
            line.transform.localPosition = new Vector3(left, status == null && waitingCount == 0 ? 0f : -GlazeText.LineHeight(line) / 2f, -GlazeTokens.Units(0.05f));
            GlazeRuns.TokenGrounds(line, theirs, identityGrounds, transform, Order + 1);
            badge.gameObject.SetActive(status != null);
            var statusY = GlazeText.LineHeight(line) / 2f;
            var countLeft = left;
            if (status != null)
            {
                badge.Show(status);
                badge.transform.localPosition = new Vector3(left + badge.Width / 2f, statusY, -GlazeTokens.Units(0.05f));
                countLeft += badge.Width + GlazeTokens.Units(Glaze.Menu.GridDegrees);
            }
            state.gameObject.SetActive(waitingCount > 0);
            if (waitingCount > 0)
            {
                state.color = GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Attention).Foreground);
                GlazeText.SetLiteral(state, waitingCount + " waiting");
                GlazeText.Lay(state, left + room - countLeft, 1);
                state.transform.localPosition = new Vector3(countLeft, statusY, -GlazeTokens.Units(0.05f));
            }
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
