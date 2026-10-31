#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Where a character looks this frame, split between its head and its eyes (<see cref="CharacterLife.Aim"/>) and
    /// followed critically damped (head 6 a second, eyes 14): the head's turn and pitch in degrees, the eyes' shift inside
    /// the face as shares of their reach, -1 to 1.
    /// </summary>
    public readonly struct CharacterAim
    {
        public CharacterAim(float headYawDegrees, float headPitchDegrees, float eyesX, float eyesY)
        {
            HeadYawDegrees = headYawDegrees;
            HeadPitchDegrees = headPitchDegrees;
            EyesX = eyesX;
            EyesY = eyesY;
        }

        /// <summary>The head's turn about its up, positive to its own right, and its pitch, positive up.</summary>
        public float HeadYawDegrees { get; }

        public float HeadPitchDegrees { get; }

        /// <summary>The eyes' shift, a share of <see cref="CharacterLife.EyeReachH"/> sideways and <see cref="CharacterLife.EyeLiftH"/> up.</summary>
        public float EyesX { get; }

        public float EyesY { get; }
    }

    /// <summary>
    /// What a character's life needs of a figure, whatever its shape (the art direction's character handoff, 2026-10-30;
    /// the lead, 2026-10-31): its height, which the life's lengths are scaled to, the head the gaze turns, and one call
    /// that shows a pose of <see cref="CharacterLife"/> with the state's <see cref="KitFigureLook"/> and the aim. The view
    /// owns the life, the gaze's targets, the tag and the sounds; the figure only draws. Today's body is the one figure; a
    /// redrawn figure brings its own.
    /// </summary>
    public interface ICharacterFigure
    {
        /// <summary>The figure's full height in its view's units: the life's lengths, for a 0.22 m figure, scale by this over 0.22.</summary>
        float Height { get; }

        /// <summary>The part the gaze turns, and the point it looks from.</summary>
        Transform Head { get; }

        /// <summary>
        /// Shows <paramref name="pose"/> in <paramref name="look"/> with the eyes and head at <paramref name="aim"/>, eased as
        /// the frame's <paramref name="seconds"/> allow, or at once where <paramref name="snap"/>.
        /// </summary>
        void Show(CharacterPose pose, KitFigureLook look, CharacterAim aim, float seconds, bool snap);
    }
}
