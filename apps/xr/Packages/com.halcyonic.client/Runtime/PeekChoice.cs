#nullable enable
using System;
using System.Numerics;

namespace Halcyonic.Client
{
    /// <summary>What brought up a peek.</summary>
    public enum PeekSource
    {
        /// <summary>A hand ray points at the character, or a finger is about to poke it.</summary>
        Hand,

        /// <summary>The person's gaze rested on the character.</summary>
        Gaze,
    }

    /// <summary>What the XR layer knows in one frame, for <see cref="PeekChoice"/>.</summary>
    public struct PeekInput
    {
        /// <summary>The app lacks input focus: nothing is peeked or opened.</summary>
        public bool Suspended;

        /// <summary>
        /// The workstream whose workspace is open, or null. Its character is never peeked, and while
        /// a workspace is open only a hand peeks, so reading it never brings up peeks.
        /// </summary>
        public string? Open;

        /// <summary>The character a hand ray points at or a finger is about to poke, or null.</summary>
        public string? Pointed;

        /// <summary>A hand ray or a finger is on any target: a character, the workspace or a button.</summary>
        public bool HandOnTarget;

        /// <summary>The character the gaze interactor hovers, or null.</summary>
        public string? Gazed;

        /// <summary>Degrees between where the head faces and the center of the gazed character's body.</summary>
        public float GazedOffCenter;

        /// <summary>Where the head faces, in any fixed frame; only how fast it turns is used.</summary>
        public Vector3 HeadForward;
    }

    /// <summary>
    /// Decides which one character shows its peek, how visible it is, and what a look and pinch
    /// opens. A hand pointing at a character peeks at once. The gaze peeks only after it has rested
    /// on one character, near the middle of the view, for <see cref="DwellSeconds"/>, while the head
    /// turns slower than <see cref="TurningDegreesPerSecond"/>, so turning the head across the stage
    /// brings up nothing. One peek shows at a time: a new one fades in only after the last has faded
    /// out. The character whose workspace is open is never peeked, and while a workspace is open the
    /// gaze peeks nothing. Allocates nothing per frame.
    /// </summary>
    public sealed class PeekChoice
    {
        /// <summary>How long the gaze must rest on a character, calmly and near the middle of the view, before it peeks.</summary>
        public const float DwellSeconds = 0.5f;

        /// <summary>Above this head speed the gaze is not resting: the dwell starts again once the head slows.</summary>
        public const float TurningDegreesPerSecond = 20f;

        /// <summary>A gaze peek starts only for a character this close to where the head faces.</summary>
        public const float CenterDegrees = 7f;

        /// <summary>A gaze peek stays while its character is this close to where the head faces.</summary>
        public const float KeepDegrees = 11f;

        /// <summary>How long a gaze peek stays after the gaze has left its character, so a glance aside does not flicker it.</summary>
        public const float LingerSeconds = 0.3f;

        public const float FadeInSeconds = 0.25f;

        public const float FadeOutSeconds = 0.15f;

        /// <summary>A gaze peek at least this visible counts as showing, for a look and pinch.</summary>
        public const float PinchOpacity = 0.5f;

        /// <summary>How quickly the head speed follows each frame's turn, so one jittery frame does not count as turning.</summary>
        private const float SpeedSmoothingSeconds = 0.08f;

        /// <summary>Frames longer than this say nothing reliable about the head's speed.</summary>
        private const float LongestFrameSeconds = 0.25f;

        private bool timed;
        private float lastTime;
        private Vector3 lastForward;
        private string? restingOn;
        private float restingSince;
        private string? gazePeek;
        private float gazeLeftAt = float.NaN;

        /// <summary>The character whose peek is visible or fading, or null.</summary>
        public string? Shown { get; private set; }

        /// <summary>What brought up the peek of <see cref="Shown"/>.</summary>
        public PeekSource Source { get; private set; }

        /// <summary>How visible the peek of <see cref="Shown"/> is, from 0 to 1.</summary>
        public float Opacity { get; private set; }

        /// <summary>The character the peek is for now, or null: it may still be fading in behind another fading out.</summary>
        public string? Wanted { get; private set; }

        /// <summary>How fast the head turns, in degrees per second, smoothed over a few frames.</summary>
        public float HeadSpeed { get; private set; }

        /// <summary>
        /// The character a pinch of either hand opens now, by look and pinch: its gaze peek is showing,
        /// no hand ray or finger is on any target, no workspace is open and the app has focus. Null
        /// otherwise.
        /// </summary>
        public string? PinchTarget { get; private set; }

        /// <summary>Advances to <paramref name="now"/>, in seconds, with what the XR layer sees in this frame.</summary>
        public void Update(in PeekInput input, float now)
        {
            var elapsed = timed ? Math.Max(0f, now - lastTime) : 0f;
            timed = true;
            lastTime = now;
            TrackHeadSpeed(input.HeadForward, elapsed);
            TrackGaze(input, now);

            string? wanted = null;
            var source = PeekSource.Gaze;
            if (!input.Suspended)
            {
                if (input.Pointed != null && input.Pointed != input.Open)
                {
                    wanted = input.Pointed;
                    source = PeekSource.Hand;
                }
                else if (input.Open == null && gazePeek != null)
                {
                    wanted = gazePeek;
                }
            }
            Wanted = wanted;
            Fade(wanted, source, elapsed);

            PinchTarget = !input.Suspended && input.Open == null && !input.HandOnTarget && Shown != null && Shown == wanted
                && Source == PeekSource.Gaze && Opacity >= PinchOpacity
                ? Shown
                : null;
        }

        private void TrackHeadSpeed(Vector3 forward, float elapsed)
        {
            if (forward.LengthSquared() < 1e-6f) return;
            forward = Vector3.Normalize(forward);
            if (lastForward.LengthSquared() > 0f && elapsed > 0f)
            {
                if (elapsed > LongestFrameSeconds)
                {
                    // A long frame, as after a pause: the dwell starts again once the head is seen still.
                    HeadSpeed = TurningDegreesPerSecond * 2f;
                }
                else
                {
                    var cosine = Math.Clamp(Vector3.Dot(lastForward, forward), -1f, 1f);
                    var degrees = MathF.Acos(cosine) * 180f / MathF.PI;
                    var weight = 1f - MathF.Exp(-elapsed / SpeedSmoothingSeconds);
                    HeadSpeed += (degrees / elapsed - HeadSpeed) * weight;
                }
            }
            lastForward = forward;
        }

        private void TrackGaze(in PeekInput input, float now)
        {
            if (input.Suspended || input.Open != null || input.Gazed == null)
            {
                restingOn = null;
            }
            else if (input.Gazed != restingOn)
            {
                // A new character: the dwell starts now.
                restingOn = input.Gazed;
                restingSince = now;
            }
            // Turning, or off to the side, the gaze is not resting: the dwell starts again.
            var calm = HeadSpeed <= TurningDegreesPerSecond && input.GazedOffCenter <= CenterDegrees;
            if (restingOn != null && restingOn != gazePeek && !calm) restingSince = now;

            if (input.Suspended || input.Open != null)
            {
                gazePeek = null;
                gazeLeftAt = float.NaN;
                return;
            }
            if (restingOn != null && restingOn != gazePeek && now - restingSince >= DwellSeconds)
            {
                gazePeek = restingOn;
                gazeLeftAt = float.NaN;
                return;
            }
            if (gazePeek == null) return;
            if (input.Gazed == gazePeek && input.GazedOffCenter <= KeepDegrees)
            {
                gazeLeftAt = float.NaN;
            }
            else if (float.IsNaN(gazeLeftAt))
            {
                gazeLeftAt = now;
            }
            else if (now - gazeLeftAt >= LingerSeconds)
            {
                gazePeek = null;
                gazeLeftAt = float.NaN;
            }
        }

        /// <summary>One peek at a time: the one showing fades out before another fades in.</summary>
        private void Fade(string? wanted, PeekSource source, float elapsed)
        {
            if (Shown != wanted)
            {
                if (Shown != null) Opacity = Math.Max(0f, Opacity - elapsed / FadeOutSeconds);
                if (Shown == null || Opacity <= 0f)
                {
                    Shown = wanted;
                    Opacity = 0f;
                }
            }
            if (Shown != null && Shown == wanted)
            {
                Source = source;
                Opacity = Math.Min(1f, Opacity + elapsed / FadeInSeconds);
            }
        }
    }
}
