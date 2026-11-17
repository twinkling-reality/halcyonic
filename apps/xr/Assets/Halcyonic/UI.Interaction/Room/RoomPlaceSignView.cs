#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A project's place says what it is (the lead's 2026-11-17T18:56Z, AUDIT2's 18:57Z): its project's name once, in the person's own
    /// case and never in the label role, a step quieter than a tag's task name, broken at a word onto as many lines as it needs and
    /// never cut; and under it, where the place gathers a crowd, its one press, "{n} More" or "Show Fewer" (<see cref="RoomPlaceSign"/>),
    /// quiet, since the one accent stays the page's main action. CharacterStage stands it at its place; a press raises
    /// <see cref="Pressed"/> with the place's project, which the stage turns into <see cref="RoomPlaceSign.Act"/>. Its origin is its top
    /// middle, so it hangs from where the stage puts it.
    /// </summary>
    public sealed class RoomPlaceSignView : MonoBehaviour
    {
        /// <summary>The name's size and its line, a step under a tag's task name (RoomGeometry.LineDp), and its share of white.</summary>
        public const float NameDp = 16f;
        public const float NameLineDp = 22f;
        public const float NameAlpha = 0.78f;

        /// <summary>The widest the name stands before it breaks at a word, as a tag's name does (RoomTag.NameWordsMostDp).</summary>
        public const float NameMostDp = RoomTag.NameWordsMostDp;

        /// <summary>The gap between the name and its press.</summary>
        public const float PressGapDp = 8f;

        private TextMeshPro nameLabel = null!;
        private RoomButton press = null!;
        private string? projectId;
        private string? action;
        private bool pressApart;

        /// <summary>
        /// Stands the press at <paramref name="point"/>, apart from the name (FIELD's places sheet v4: "{n} More" at the place's inner end
        /// over the front row, the name under the floor arc).
        /// </summary>
        public void StandPressAt(Transform point)
        {
            press.transform.SetParent(point, false);
            pressApart = true;
        }

        /// <summary>Raised with the place's project and the press's action (<see cref="RoomPlaceSign.MoreAction"/> or <see cref="RoomPlaceSign.FewerAction"/>).</summary>
        public event Action<string, string>? Pressed;

        /// <summary>The place's name as drawn; null where it has none.</summary>
        public TextMeshPro? Name => nameLabel.gameObject.activeSelf ? nameLabel : null;

        /// <summary>The place's press as drawn; null where nothing is gathered.</summary>
        public RoomButton? Press => press.gameObject.activeSelf ? press : null;

        /// <summary>How wide and tall it stands.</summary>
        public Vector2 Size { get; private set; }

        public static RoomPlaceSignView Create(Transform parent)
        {
            var go = new GameObject("Place sign");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomPlaceSignView>();
            view.nameLabel = KitType.Create(go.transform, "Place name", KitRole.Body, new Color(1f, 1f, 1f, NameAlpha), KitType.Order.Words, TextAlignmentOptions.Top, room: true);
            view.press = RoomButton.Create(go.transform, "Place press");
            view.press.Pressed += () =>
            {
                if (view.projectId != null && view.action != null) view.Pressed?.Invoke(view.projectId, view.action);
            };
            go.SetActive(false);
            return view;
        }

        /// <summary>Shows <paramref name="place"/>'s name and its press as <paramref name="places"/> stands it now; returns its size.</summary>
        public Vector2 Show(RoomPlace place, RoomPlaces places)
        {
            gameObject.SetActive(true);
            projectId = place.ProjectId;
            var down = 0f;
            var wide = 0f;
            if (RoomPlaceSign.NameOf(place) is LineWords said)
            {
                nameLabel.gameObject.SetActive(true);
                nameLabel.fontSize = GlazeTokens.FontSize(RoomType.Scaled(NameDp));
                nameLabel.lineSpacing = KitType.LineSpacingFor(nameLabel, NameLineDp / NameDp);
                // The person's own case: never the label role's capitals (AUDIT2's 18:57Z).
                nameLabel.fontStyle &= ~FontStyles.UpperCase;
                GlazeRuns.Of(nameLabel).Set(said);
                var (lines, laidWide, tall) = KitType.Lay(nameLabel, RoomType.Scaled(NameMostDp), 12);
                // Its box hangs from its top, so the name stands under the point it is given, never over what stands above it (the floor arc, the
                // team's tags: AUDIT's 22:0xZ found it centred on the point, over a tag).
                nameLabel.rectTransform.pivot = new Vector2(0.5f, 1f);
                nameLabel.transform.localPosition = new Vector3(0f, 0f, 0f);
                wide = laidWide;
                down = Mathf.Max(lines, 1) * RoomType.Scaled(NameLineDp);
            }
            else nameLabel.gameObject.SetActive(false);
            if (RoomPlaceSign.PressOf(place, places) is { } pressed)
            {
                var (words, pressAction) = pressed;
                action = pressAction;
                var icon = pressAction == RoomPlaceSign.MoreAction ? GlazeIcon.Next : GlazeIcon.Back;
                var measured = press.Measure(words, 0f);
                if (pressApart) press.Show(words, icon, isMain: false, available: true, Vector2.zero, measured.x);
                else
                {
                    if (down > 0f) down += RoomType.Scaled(PressGapDp);
                    press.Show(words, icon, isMain: false, available: true, new Vector2(0f, -down - measured.y / 2f), measured.x);
                    down += measured.y;
                    wide = Mathf.Max(wide, measured.x);
                }
            }
            else
            {
                action = null;
                press.Hide();
            }
            Size = new Vector2(wide, down);
            return Size;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
