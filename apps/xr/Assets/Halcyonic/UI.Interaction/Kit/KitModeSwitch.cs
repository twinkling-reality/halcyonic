#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The connected two-position switch: Workspace over Compact in one track, one raised thumb in the position the
    /// workspace's presentation is confirmed to be in. Each position is its own whole hand target, the two
    /// <see cref="SpatialKit.TargetGapDegrees"/> apart; the thumb crosses that gap, where no press lands. A press on
    /// the other position only asks: it raises <see cref="WorkspaceRequested"/> or <see cref="CompactRequested"/> for
    /// the switch's owner to forward to its existing Open or Compact route, and the thumb moves only when
    /// <see cref="ShowConfirmed"/> reports the presentation that route produced; a refused route leaves it where it
    /// was. A press on the current position does nothing. It offers exactly the two presentations the workspace has
    /// and no third. Moving, the thumb stretches toward its new place and settles there in
    /// <see cref="SpatialKit.SwitchSeconds"/>; under Keep things still it is there at once. Centred on its transform.
    /// </summary>
    public sealed class KitModeSwitch : MonoBehaviour
    {
        public const string WorkspaceWords = "Workspace";
        public const string CompactWords = "Compact";

        private Surface track = null!;
        private Surface shadow = null!;
        private Surface thumb = null!;
        private Surface workspaceMark = null!;
        private Surface workspaceMarkBar = null!;
        private Surface compactMark = null!;
        private Surface hoverFrame = null!;
        private TextMeshPro workspaceLabel = null!;
        private TextMeshPro compactLabel = null!;
        private KitPress workspaceTarget = null!;
        private KitPress compactTarget = null!;
        private Vector2 size;
        private bool compact;
        private float position;
        private float from;
        private float moving = 1f;

        /// <summary>The person asked for the whole workspace; the owner forwards it to its existing Open route.</summary>
        public event Action? WorkspaceRequested;

        /// <summary>The person asked for the compact presence; the owner forwards it to its existing Compact route.</summary>
        public event Action? CompactRequested;

        /// <summary>The presentation last confirmed: the thumb's place.</summary>
        public bool ConfirmedCompact => compact;

        /// <summary>Where the thumb is drawn now, from 0 at Workspace to 1 at Compact.</summary>
        public float ThumbPosition => position;

        public bool Moving => moving < 1f;

        public Vector2 Size => size;

        public KitPress WorkspaceTarget => workspaceTarget;

        public KitPress CompactTarget => compactTarget;

        public TextMeshPro WorkspaceLabel => workspaceLabel;

        public TextMeshPro CompactLabel => compactLabel;

        public Surface Thumb => thumb;

        public static KitModeSwitch Create(Transform parent, string name = "Workspace and Compact", int order = KitType.Order.Control)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<KitModeSwitch>();
            view.track = Surface.Create(go.transform, "Track", order);
            view.hoverFrame = Surface.Create(go.transform, "Pointed position", order + 1);
            view.shadow = Surface.Create(go.transform, "Thumb shadow", order + 1);
            view.thumb = Surface.Create(go.transform, "Thumb", order + 2);
            view.workspaceMark = Surface.Create(go.transform, "Workspace mark", order + 4);
            view.workspaceMarkBar = Surface.Create(go.transform, "Workspace mark bar", order + 4);
            view.compactMark = Surface.Create(go.transform, "Compact mark", order + 4);
            view.workspaceLabel = KitType.Create(go.transform, "Workspace", KitRole.Strong, KitType.Text, order + 5, TextAlignmentOptions.Left);
            view.compactLabel = KitType.Create(go.transform, "Compact", KitRole.Strong, KitType.Secondary, order + 5, TextAlignmentOptions.Left);
            foreach (var label in new[] { view.workspaceLabel, view.compactLabel })
            {
                label.rectTransform.pivot = new Vector2(0f, 0.5f);
                label.textWrappingMode = TextWrappingModes.NoWrap;
            }
            GlazeText.SetLiteral(view.workspaceLabel, WorkspaceWords);
            GlazeText.SetLiteral(view.compactLabel, CompactWords);
            view.workspaceTarget = MakeTarget(go.transform, "Workspace position");
            view.compactTarget = MakeTarget(go.transform, "Compact position");
            view.workspaceTarget.Pressed += () => { if (view.compact) view.WorkspaceRequested?.Invoke(); };
            view.compactTarget.Pressed += () => { if (!view.compact) view.CompactRequested?.Invoke(); };
            return view;
        }

        private static KitPress MakeTarget(Transform parent, string name)
        {
            var host = new GameObject(name);
            host.transform.SetParent(parent, false);
            return KitPress.Create(host, Vector2.one * KitType.Units(SpatialKit.SwitchCellDegrees));
        }

        /// <summary>The track's height: two whole targets, the gap between them and the inset round them.</summary>
        public static float Height => KitType.Units(SpatialKit.SwitchTrackDegrees);

        /// <summary>The narrowest the switch can be with both its positions' words whole.</summary>
        public float NarrowestWidth()
        {
            KitType.Size(workspaceLabel, KitRole.Strong);
            KitType.Size(compactLabel, KitRole.Strong);
            var words = Mathf.Max(workspaceLabel.GetPreferredValues(WorkspaceWords).x, compactLabel.GetPreferredValues(CompactWords).x);
            return 2f * KitType.Units(SpatialKit.SwitchInsetDegrees) + 2f * KitType.Scaled(0.8f) + MarkRoom + words;
        }

        private static float MarkRoom => KitType.Scaled(1.35f) + KitType.Scaled(0.55f);

        /// <summary>Lays the switch out <paramref name="width"/> wide and returns its size.</summary>
        public Vector2 Layout(float width)
        {
            size = new Vector2(Mathf.Max(width, NarrowestWidth()), Height);
            var inset = KitType.Units(SpatialKit.SwitchInsetDegrees);
            var cell = KitType.Units(SpatialKit.SwitchCellDegrees);
            var cellWidth = size.x - 2f * inset;
            track.Draw(size, cell / 2f + inset, KitType.Colour(SpatialKit.Well));
            workspaceTarget.transform.localPosition = new Vector3(0f, CellCentre(0f), -KitType.Units(0.05f));
            compactTarget.transform.localPosition = new Vector3(0f, CellCentre(1f), -KitType.Units(0.05f));
            workspaceTarget.Resize(new Vector2(cellWidth, cell));
            compactTarget.Resize(new Vector2(cellWidth, cell));
            var left = -size.x / 2f + inset + KitType.Scaled(0.8f);
            var markWidth = KitType.Scaled(1.35f);
            var wordsLeft = left + markWidth + KitType.Scaled(0.55f);
            KitType.Size(workspaceLabel, KitRole.Strong);
            KitType.Size(compactLabel, KitRole.Strong);
            KitType.Lay(workspaceLabel, size.x, 1);
            KitType.Lay(compactLabel, size.x, 1);
            workspaceLabel.transform.localPosition = new Vector3(wordsLeft, CellCentre(0f), -0.0006f);
            compactLabel.transform.localPosition = new Vector3(wordsLeft, CellCentre(1f), -0.0006f);
            // Two small marks of Halcyonic's beside the words, never in their place: the whole workspace as a
            // framed field with its top band, the compact presence as one short bar.
            var markCentre = left + markWidth / 2f;
            workspaceMark.transform.localPosition = new Vector3(markCentre, CellCentre(0f), -0.0005f);
            workspaceMarkBar.transform.localPosition = new Vector3(markCentre, CellCentre(0f) + KitType.Scaled(0.25f), -0.0005f);
            compactMark.transform.localPosition = new Vector3(markCentre, CellCentre(1f), -0.0005f);
            Draw();
            return size;
        }

        /// <summary>The centre of a position's cell, 0 for Workspace and 1 for Compact, in the switch's units.</summary>
        private float CellCentre(float at)
        {
            var offset = (KitType.Units(SpatialKit.SwitchCellDegrees) + KitType.Units(SpatialKit.TargetGapDegrees)) / 2f;
            return Mathf.Lerp(offset, -offset, at);
        }

        /// <summary>
        /// Shows the presentation the owner's route confirmed: <paramref name="isCompact"/> or the whole workspace.
        /// The thumb travels there, at once when <paramref name="immediately"/> or while things keep still.
        /// </summary>
        public void ShowConfirmed(bool isCompact, bool immediately = false)
        {
            var changed = compact != isCompact;
            compact = isCompact;
            if (immediately || GlazeMotion.Still || !changed)
            {
                position = compact ? 1f : 0f;
                moving = 1f;
            }
            else
            {
                from = position;
                moving = 0f;
            }
            // Each position is a new action once the other is current: a press under way is not one of it.
            workspaceTarget.Unsettle();
            compactTarget.Unsettle();
            Draw();
        }

        /// <summary>Moves the thumb on by <paramref name="seconds"/>.</summary>
        public void Advance(float seconds)
        {
            if (moving >= 1f) return;
            moving = GlazeMotion.Still ? 1f : Mathf.Min(1f, moving + seconds / SpatialKit.SwitchSeconds);
            position = Mathf.Lerp(from, compact ? 1f : 0f, Glaze.EaseInOut(moving));
            Draw();
        }

        private void Update() => Advance(Time.unscaledDeltaTime);

        /// <summary>For renders, which have no hands: draws a position as pointed at until the next frame draws the switch as it is.</summary>
        public void PointForRender(bool workspacePosition) => Draw(workspacePosition ? 0 : 1);

        private void Draw(int? pointedForRender = null)
        {
            if (size == Vector2.zero) return;
            var inset = KitType.Units(SpatialKit.SwitchInsetDegrees);
            var cell = KitType.Units(SpatialKit.SwitchCellDegrees);
            var cellWidth = size.x - 2f * inset;
            var travel = CellCentre(0f) - CellCentre(1f);
            // Mid-move the thumb reaches toward where it goes: one connected shape, never two.
            var stretch = Mathf.Sin(Mathf.PI * (moving >= 1f ? 0f : Glaze.EaseInOut(moving))) * 0.6f * travel;
            var thumbSize = new Vector2(cellWidth, cell + stretch);
            var centre = CellCentre(position);
            var accent = Glaze.Tone(GlazeTone.Accent);
            shadow.Draw(thumbSize, cell / 2f, new Color(0f, 0f, 0f, 0.42f));
            shadow.transform.localPosition = new Vector3(0f, centre - KitType.Units(0.14f), -0.0002f);
            // Raised by its fill and shadow; its accent edge says which position is confirmed. No light of its own:
            // the instrument carries one.
            thumb.Draw(thumbSize, cell / 2f, KitType.Colour(SpatialKit.Thumb), KitType.Mix(SpatialKit.Thumb, accent.Strong, 0.7f), KitType.Units(0.08f));
            thumb.Selection = SurfaceSelection.Lit;
            thumb.transform.localPosition = new Vector3(0f, centre, -0.0003f);
            // The pointed position: its frame inside the track. The current one needs none: its thumb is lit already.
            var hoveredWorkspace = pointedForRender == 0 || workspaceTarget.Hovered;
            var hoveredCompact = pointedForRender == 1 || compactTarget.Hovered;
            var pointedAt = compact && hoveredWorkspace ? 0f : !compact && hoveredCompact ? 1f : -1f;
            hoverFrame.gameObject.SetActive(pointedAt >= 0f);
            if (pointedAt >= 0f)
            {
                hoverFrame.Draw(new Vector2(cellWidth, cell), cell / 2f, Color.clear, KitType.Mix(SpatialKit.Well, GlazeColor.Hex(0xFFFFFF), 0.55f), KitType.Units(0.08f));
                hoverFrame.Selection = SurfaceSelection.Pointed;
                hoverFrame.transform.localPosition = new Vector3(0f, CellCentre(pointedAt), -0.0001f);
            }
            var onWorkspace = position < 0.5f;
            workspaceLabel.color = onWorkspace ? KitType.Text : KitType.Secondary;
            compactLabel.color = onWorkspace ? KitType.Secondary : KitType.Text;
            GlazeText.SetStrong(workspaceLabel, onWorkspace);
            GlazeText.SetStrong(compactLabel, !onWorkspace);
            var markWidth = KitType.Scaled(1.35f);
            var edge = KitType.Units(0.1f);
            workspaceMark.Draw(new Vector2(markWidth, markWidth * 0.72f), KitType.Units(0.2f), Color.clear, workspaceLabel.color, edge);
            workspaceMarkBar.Draw(new Vector2(markWidth - 2f * edge, edge * 1.4f), edge * 0.7f, workspaceLabel.color);
            compactMark.Draw(new Vector2(markWidth, markWidth * 0.34f), markWidth * 0.17f, compactLabel.color);
        }
    }
}
