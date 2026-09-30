#nullable enable
using System;
using System.Globalization;
using System.Numerics;

namespace Halcyonic.Client
{
    /// <summary>Where the head is and which way it faces on the level, in world space: meters, and degrees from +Z toward +X.</summary>
    public readonly struct HeadSample
    {
        public HeadSample(Vector3 position, float yaw)
        {
            Position = position;
            Yaw = yaw;
        }

        public Vector3 Position { get; }

        public float Yaw { get; }
    }

    /// <summary>What the stage does about where it stands.</summary>
    public enum PlacementAction
    {
        None,

        /// <summary>Stand in front of the person, facing where they face.</summary>
        PlaceInFront,

        /// <summary>
        /// The tracking space moved under the person without the person moving: move by the same
        /// turn and shift, so the stage stays where it was around them.
        /// </summary>
        Follow,

        /// <summary>Stay where it stands, and say why in the log.</summary>
        Kept,
    }

    /// <summary>A decision of <see cref="InFrontPlacement"/>, with the reason the log gives.</summary>
    public readonly struct PlacementDecision
    {
        public PlacementDecision(PlacementAction action, string reason, float turn = 0f, Vector3 from = default, Vector3 to = default)
        {
            Action = action;
            Reason = reason;
            Turn = turn;
            From = from;
            To = to;
        }

        public static PlacementDecision None => default;

        public PlacementAction Action { get; }

        public string Reason { get; }

        /// <summary>For <see cref="PlacementAction.Follow"/>: how far the tracking space turned about the vertical, in degrees.</summary>
        public float Turn { get; }

        /// <summary>
        /// For <see cref="PlacementAction.Follow"/>: the head before and after the tracking space moved.
        /// A point p goes to To + R(Turn)(p - From), where R turns about the vertical.
        /// </summary>
        public Vector3 From { get; }

        public Vector3 To { get; }
    }

    /// <summary>
    /// Decides when the stage, standing in front of the person, is placed in front of them again:
    /// when the session starts and the head is tracked, after the app resumes from a pause, when the
    /// person recenters, and when the head jumps farther in one frame than a person can, which only
    /// a changed tracking space explains.
    /// </summary>
    /// <remarks>
    /// A reference space change (Unity's tracking origin update) counts only when the tracking space
    /// really moved, which shows as the head jumping within one frame although the person did not
    /// move. With system windows open, the session's focus can flap many times a second, each flap
    /// reporting a reference space change that moves nothing: those never move the stage. When one
    /// does move the tracking space, the stage moves with it at once (<see cref="PlacementAction.Follow"/>),
    /// so it stays where it was around the person; only a move with no focus change around it is
    /// taken for the person recentering, and then the stage is placed in front of them. Changes are
    /// watched until they have stopped for <see cref="WatchSeconds"/>, because one recenter can raise
    /// several over a few frames, and the runtime announces a change before its pose arrives.
    /// </remarks>
    public sealed class InFrontPlacement
    {
        /// <summary>How long after the last reference space change the head is watched for a jump.</summary>
        public const float WatchSeconds = 0.75f;

        /// <summary>The longest one watch lasts while changes keep coming, so the log hears of them.</summary>
        public const float LongestWatchSeconds = 3f;

        /// <summary>A focus change this long before a watch starts, or during it, counts as coming with it.</summary>
        public const float FocusWindowSeconds = 1.5f;

        /// <summary>A jump this long before a reference space change counts as its move.</summary>
        public const float EarlyJumpSeconds = 0.3f;

        /// <summary>How long requests must have stopped before placing, so repeated ones coalesce.</summary>
        public const float SettleSeconds = 0.3f;

        /// <summary>How long to wait for head tracking before placing from whatever pose there is.</summary>
        public const float TrackingTimeoutSeconds = 3f;

        /// <summary>A jump, within one short frame: more than this far, faster than a head moves.</summary>
        public const float JumpDegrees = 4f;

        public const float JumpDegreesPerSecond = 400f;

        public const float JumpMeters = 0.03f;

        public const float JumpMetersPerSecond = 2.5f;

        /// <summary>A jump this large means a new tracking space even without a reference space change.</summary>
        public const float LargeDegrees = 45f;

        public const float LargeMeters = 0.5f;

        /// <summary>A long frame, as after a pause, can hold real movement; only shorter frames are judged.</summary>
        public const float LongestJudgedFrame = 0.1f;

        private string? reason = "the session started";
        private float requestedAt = float.NaN;
        private float lastRequest = float.NaN;
        private float lastFocusChange = float.NegativeInfinity;
        private bool paused;

        private bool watching;
        private float watchStart;
        private float watchUntil;
        private bool watchMoved;
        private int watchEvents;

        private bool seen;
        private HeadSample previous;

        /// <summary>The last jump seen outside a watch, which a reference space change arriving just after it owns.</summary>
        private PlacementDecision earlyJump;
        private float earlyJumpAt = float.NegativeInfinity;
        private bool followEarlyJump;

        /// <summary>Asks for the stage to be placed in front of the person, for the reason given.</summary>
        public void Request(string why, float now)
        {
            if (reason == null)
            {
                reason = why;
                requestedAt = now;
            }
            lastRequest = now;
        }

        /// <summary>The runtime reported a reference space change, such as Unity's tracking origin update.</summary>
        public void OriginChanged(float now)
        {
            if (!watching)
            {
                watching = true;
                watchStart = now;
                watchMoved = false;
                watchEvents = 0;
                if (now - earlyJumpAt <= EarlyJumpSeconds)
                {
                    // The pose arrived before its announcement: that jump was this change's.
                    watchMoved = true;
                    followEarlyJump = true;
                    earlyJumpAt = float.NegativeInfinity;
                }
            }
            watchEvents++;
            watchUntil = Math.Min(now + WatchSeconds, watchStart + LongestWatchSeconds);
        }

        /// <summary>The app gained or lost input focus.</summary>
        public void FocusChanged(float now) => lastFocusChange = now;

        /// <summary>The app paused or resumed. Only a resume after a real pause places the stage again, since the person may have moved.</summary>
        public void Paused(bool isPaused, float now)
        {
            if (isPaused)
            {
                paused = true;
                return;
            }
            if (!paused) return;
            paused = false;
            Request("the app resumed", now);
        }

        /// <summary>
        /// Called once a frame, after the head has moved, with the frame's length. Returns what the
        /// stage should do now: <see cref="PlacementAction.None"/> in most frames.
        /// </summary>
        public PlacementDecision Poll(float now, float deltaTime, HeadSample? head, bool headTracked)
        {
            if (float.IsNaN(requestedAt)) requestedAt = now;
            if (float.IsNaN(lastRequest)) lastRequest = now;

            var jumped = false;
            var large = false;
            var jump = PlacementDecision.None;
            if (head.HasValue)
            {
                var current = head.Value;
                if (seen && deltaTime > 0f && deltaTime < LongestJudgedFrame)
                {
                    var turn = DeltaAngle(previous.Yaw, current.Yaw);
                    var moved = Vector3.Distance(previous.Position, current.Position);
                    jumped = (MathF.Abs(turn) >= JumpDegrees && MathF.Abs(turn) / deltaTime >= JumpDegreesPerSecond)
                        || (moved >= JumpMeters && moved / deltaTime >= JumpMetersPerSecond);
                    large = MathF.Abs(turn) > LargeDegrees || moved > LargeMeters;
                    jump = new PlacementDecision(PlacementAction.Follow, "", turn, previous.Position, current.Position);
                }
                previous = current;
                seen = true;
            }
            else
            {
                seen = false;
            }

            if (followEarlyJump)
            {
                followEarlyJump = false;
                return Follow(earlyJump, "the tracking space moved just before a reference space change");
            }

            if (watching)
            {
                if (jumped)
                {
                    watchMoved = true;
                    return Follow(jump, "the tracking space moved with a reference space change");
                }
                if (now >= watchUntil)
                {
                    watching = false;
                    var withFocus = lastFocusChange >= watchStart - FocusWindowSeconds;
                    if (watchMoved && !withFocus)
                    {
                        Request("the person recentered: the tracking space moved, with no focus change", now);
                    }
                    else
                    {
                        var times = watchEvents == 1 ? "once" : watchEvents.ToString(CultureInfo.InvariantCulture) + " times";
                        return new PlacementDecision(PlacementAction.Kept, watchMoved
                            ? "the tracking space moved during a focus change, and the stage moved with it"
                            : "the reference space changed " + times + (withFocus ? " during focus changes" : "") + " and nothing moved");
                    }
                }
            }
            else if (large)
            {
                if (now - lastFocusChange <= FocusWindowSeconds)
                {
                    return Follow(jump, "the tracking space jumped during a focus change");
                }
                Request("the head moved farther in one frame than a person can", now);
            }
            else if (jumped)
            {
                // Perhaps the tracking correcting itself, perhaps a change announced late: keep it.
                earlyJump = jump;
                earlyJumpAt = now;
            }

            if (reason == null || now - lastRequest < SettleSeconds) return PlacementDecision.None;
            if (!headTracked && now - requestedAt < TrackingTimeoutSeconds) return PlacementDecision.None;
            var why = reason;
            reason = null;
            return new PlacementDecision(PlacementAction.PlaceInFront, why);
        }

        private static PlacementDecision Follow(PlacementDecision jump, string why) =>
            new PlacementDecision(PlacementAction.Follow, why, jump.Turn, jump.From, jump.To);

        /// <summary>The signed turn from one heading to another, between -180 and 180 degrees.</summary>
        private static float DeltaAngle(float from, float to)
        {
            var delta = (to - from) % 360f;
            if (delta > 180f) delta -= 360f;
            if (delta < -180f) delta += 360f;
            return delta;
        }
    }
}
