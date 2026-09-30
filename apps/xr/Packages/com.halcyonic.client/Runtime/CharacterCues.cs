#nullable enable
using System;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>What a character's eyes do.</summary>
    public enum CharacterEyes
    {
        /// <summary>Open, looking ahead, blinking now and then.</summary>
        Open,

        /// <summary>Open and looking down at the work.</summary>
        OnTask,

        /// <summary>Open and sweeping from side to side.</summary>
        Scanning,

        /// <summary>Wide open and looking at the person.</summary>
        OnPerson,

        /// <summary>Closed.</summary>
        Closed,

        /// <summary>Crossed out.</summary>
        Crossed,

        /// <summary>Half open, wandering and unfocused.</summary>
        Unfocused,

        /// <summary>Flat lines.</summary>
        Flat,
    }

    /// <summary>How a character's body moves.</summary>
    public enum CharacterMotion
    {
        /// <summary>Slow breathing.</summary>
        Breathe,

        /// <summary>A light, quick bob, getting ready.</summary>
        Warm,

        /// <summary>Hopping.</summary>
        Hop,

        /// <summary>Hovering gently.</summary>
        Hover,

        /// <summary>Rising toward the person's eye level.</summary>
        Rise,

        /// <summary>Settled and almost still.</summary>
        Settle,

        /// <summary>Sunk down and tilted.</summary>
        Slump,

        /// <summary>Drifting slowly.</summary>
        Drift,

        /// <summary>Stopped in the middle of a hop.</summary>
        Frozen,
    }

    /// <summary>The light around a character.</summary>
    public enum CharacterHalo
    {
        None,

        /// <summary>Warm amber, pulsing.</summary>
        NeedsYou,

        /// <summary>Red.</summary>
        Failed,

        /// <summary>Soft green.</summary>
        Finished,

        /// <summary>Pale ice, with the ring that sweeps around the character.</summary>
        Verifying,

        /// <summary>Grey.</summary>
        Unknown,
    }

    /// <summary>
    /// How a character shows its presentation: eyes, motion, surface and light. State lives in the
    /// eyes and motion first, so no two activities differ only in color, and the written status
    /// stays under the character. The XR layer chooses the shapes, timings and colors; it derives
    /// nothing else (docs/internal/decisions/0013-characters-are-bots-with-a-living-surface.md).
    /// </summary>
    public sealed class CharacterCues
    {
        private CharacterCues(
            CharacterEyes eyes,
            CharacterMotion motion,
            CharacterHalo halo,
            bool flowing,
            bool ring,
            bool cracked,
            bool fogged,
            bool facesPerson,
            bool paused,
            bool ghosted)
        {
            Eyes = eyes;
            Motion = motion;
            Halo = halo;
            Flowing = flowing;
            Ring = ring;
            Cracked = cracked;
            Fogged = fogged;
            FacesPerson = facesPerson;
            Paused = paused;
            Ghosted = ghosted;
        }

        public CharacterEyes Eyes { get; }

        public CharacterMotion Motion { get; }

        public CharacterHalo Halo { get; }

        /// <summary>The satin flow moves across the surface: work is running.</summary>
        public bool Flowing { get; }

        /// <summary>A ring sweeps around the character: tests are running.</summary>
        public bool Ring { get; }

        /// <summary>The surface is cracked: the work failed.</summary>
        public bool Cracked { get; }

        /// <summary>Fog covers the surface: Halcyonic cannot observe the work.</summary>
        public bool Fogged { get; }

        /// <summary>The character turns to face the person.</summary>
        public bool FacesPerson { get; }

        /// <summary>
        /// The character's motion is stopped where it is: the work was stopped, or the state is only
        /// the last known one.
        /// </summary>
        public bool Paused { get; }

        /// <summary>
        /// The state is the last known one while the session is not live: the character is ghosted
        /// into a halftone of dots.
        /// </summary>
        public bool Ghosted { get; }

        public static CharacterCues Of(CharacterPresentation presentation)
        {
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));
            var stale = presentation.Stale;
            var halo = HaloOf(presentation);
            switch (presentation.Activity)
            {
                case CharacterActivity.Idle:
                    return new CharacterCues(CharacterEyes.Open, CharacterMotion.Breathe, halo, false, false, false, false, false, stale, stale);
                case CharacterActivity.Starting:
                    return new CharacterCues(CharacterEyes.OnTask, CharacterMotion.Warm, halo, false, false, false, false, false, stale, stale);
                case CharacterActivity.Working:
                    return new CharacterCues(CharacterEyes.OnTask, CharacterMotion.Hop, halo, true, false, false, false, false, stale, stale);
                case CharacterActivity.Verifying:
                    return new CharacterCues(CharacterEyes.Scanning, CharacterMotion.Hover, halo, true, true, false, false, false, stale, stale);
                case CharacterActivity.WaitingForHuman:
                    return new CharacterCues(CharacterEyes.OnPerson, CharacterMotion.Rise, halo, false, false, false, false, true, stale, stale);
                case CharacterActivity.TurnFinished:
                    // No celebration: a finished turn says nothing about whether the work is correct.
                    return new CharacterCues(CharacterEyes.Closed, CharacterMotion.Settle, halo, false, false, false, false, false, stale, stale);
                case CharacterActivity.Failed:
                    return new CharacterCues(CharacterEyes.Crossed, CharacterMotion.Slump, halo, false, false, true, false, false, stale, stale);
                case CharacterActivity.Interrupted:
                    return new CharacterCues(CharacterEyes.Flat, CharacterMotion.Frozen, halo, false, false, false, false, false, true, stale);
                case CharacterActivity.Unknown:
                    return new CharacterCues(CharacterEyes.Unfocused, CharacterMotion.Drift, halo, false, false, false, true, false, stale, stale);
                default:
                    throw new ArgumentOutOfRangeException(nameof(presentation), presentation.Activity, "Unhandled activity.");
            }
        }

        private static CharacterHalo HaloOf(CharacterPresentation presentation)
        {
            if (presentation.Attention == AttentionLevel.ActionRequired || presentation.Activity == CharacterActivity.WaitingForHuman)
            {
                return CharacterHalo.NeedsYou;
            }
            switch (presentation.Activity)
            {
                case CharacterActivity.Failed:
                    return CharacterHalo.Failed;
                case CharacterActivity.Unknown:
                    return CharacterHalo.Unknown;
                case CharacterActivity.Verifying:
                    return CharacterHalo.Verifying;
                case CharacterActivity.TurnFinished:
                    // A finished turn that needs attention finished with failing tests.
                    return presentation.Attention == AttentionLevel.Notice ? CharacterHalo.Failed : CharacterHalo.Finished;
                default:
                    return CharacterHalo.None;
            }
        }
    }
}
