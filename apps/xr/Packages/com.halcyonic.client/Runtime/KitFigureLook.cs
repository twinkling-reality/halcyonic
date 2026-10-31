#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>The figure's top feature, its identity by form, given in task order (the art direction's character, 2026-10-30).</summary>
    public enum KitFigureFeature
    {
        Antenna,
        Fins,
        Crest,
        Loop,
        Twin,
        Brim,
    }

    /// <summary>
    /// How a Workstream's figure looks in one state (the art direction's character handoff, 2026-10-30, section 2; AUDIT C1 to
    /// C4): its eyes' tone, the lids, the pool of light under it, the body's dimming and the posture it holds, from the
    /// journal's <see cref="WorkState"/> alone plus two facts held apart: the computer's own passed check, observed, and the
    /// control plane unreachable. Nothing here follows progress, the agent's words or its reported checks: Checking is never
    /// the passed look, and Round Ended alone stays neutral. The motion (floats, gaze, wander, blinks, hops, meetings) is the
    /// character view's, from these values. Lids are the fraction of the eye hidden from the top.
    /// </summary>
    public readonly struct KitFigureLook : IEquatable<KitFigureLook>
    {
        /// <summary>The figure's full height without its top feature, in metres, and the distance it is seen from.</summary>
        public const double HeightM = 0.22, SeenFromM = 1.9;

        /// <summary>The eye glow's level, and its level while the computer is unreachable.</summary>
        public const double GlowLevel = 0.55, UnreachableGlowLevel = 0.15;

        /// <summary>The pool's level, and Round Ended's neutral pool.</summary>
        public const double PoolLevel = 0.55, RoundEndedPoolLevel = 0.40;

        /// <summary>The passed look's happy eyes stand this long after the computer's passed check, then the eyes come back.</summary>
        public const double HappyMs = 1200;

        /// <summary>
        /// The figure's own neutral tones (the art direction's n7, n5 and n4 of 2026-10-30), held here so the kit's word
        /// colours can change without changing a figure's eyes.
        /// </summary>
        public static GlazeColor NeutralTone => GlazeColor.Hex(0xDADDE0);

        public static GlazeColor QuietTone => GlazeColor.Hex(0xA0A5AC);

        public static GlazeColor LostTone => GlazeColor.Hex(0x767B82);

        /// <summary>The figure's state tones (the art direction's 2026-10-30): live, needs you, passed, failed, its own as the neutrals are.</summary>
        public static GlazeColor LiveTone => GlazeColor.Hex(0x59CFF1);

        public static GlazeColor NeedsTone => GlazeColor.Hex(0xFABA59);

        public static GlazeColor PassedTone => GlazeColor.Hex(0x69D98D);

        public static GlazeColor FailedTone => GlazeColor.Hex(0xF7857D);

        /// <summary>Unreachable: the body at half its lightness and 30 percent of its saturation.</summary>
        public const double UnreachableLightness = 0.5, UnreachableSaturation = 0.3;

        public KitFigureLook(GlazeColor eyes, double lids, double rightEyeShorter, double rightEyeRollDegrees, bool happy, double glow,
            GlazeColor? pool, double poolLevel, bool breathes, double lightness, double saturation, bool moves, bool wanders,
            double floatSpeed, double riseM, double headRollDegrees, double headPitchDegrees)
        {
            Eyes = eyes;
            Lids = lids;
            RightEyeShorter = rightEyeShorter;
            RightEyeRollDegrees = rightEyeRollDegrees;
            Happy = happy;
            Glow = glow;
            Pool = pool;
            PoolLevelShown = pool.HasValue ? poolLevel : 0;
            Breathes = breathes;
            Lightness = lightness;
            Saturation = saturation;
            Moves = moves;
            Wanders = wanders;
            FloatSpeed = floatSpeed;
            RiseM = riseM;
            HeadRollDegrees = headRollDegrees;
            HeadPitchDegrees = headPitchDegrees;
        }

        /// <summary>The eyes' tone, unlit; their glow takes it too.</summary>
        public GlazeColor Eyes { get; }

        /// <summary>The fraction of each eye hidden from its top: 0 open, 1 closed.</summary>
        public double Lids { get; }

        /// <summary>Status Unknown: the eye on the person's right 30 percent shorter and rolled, held (C4).</summary>
        public double RightEyeShorter { get; }

        public double RightEyeRollDegrees { get; }

        /// <summary>The happy arcs, only with the computer's own passed check, for <see cref="HappyMs"/>.</summary>
        public bool Happy { get; }

        /// <summary>The glow behind each eye, as a level of the eyes' tone.</summary>
        public double Glow { get; }

        /// <summary>The pool's tone, or none.</summary>
        public GlazeColor? Pool { get; }

        /// <summary>The pool's level, 0 where none shows.</summary>
        public double PoolLevelShown { get; }

        /// <summary>The pool breathes over 2400 ms (Starting, Running, Checking, Needs You).</summary>
        public bool Breathes { get; }

        /// <summary>The body's lightness and saturation, 1 and 1 but while unreachable.</summary>
        public double Lightness { get; }

        public double Saturation { get; }

        /// <summary>Any motion at all: none while the computer is unreachable, so a still figure means no news.</summary>
        public bool Moves { get; }

        /// <summary>Wanders its home: Running only.</summary>
        public bool Wanders { get; }

        /// <summary>The float's speed, 1 its own; 0.6 for the quiet states, 0.5 after a failure.</summary>
        public double FloatSpeed { get; }

        /// <summary>The posture held: risen (positive) or sunk, in metres; the head rolled and pitched down, in degrees.</summary>
        public double RiseM { get; }

        public double HeadRollDegrees { get; }

        public double HeadPitchDegrees { get; }

        /// <summary>
        /// The look of <paramref name="state"/>; <paramref name="passedCheckObserved"/> is the computer's own passed check, observed
        /// in the journal for this round, never the agent's report: it changes only Round Ended.
        /// </summary>
        public static KitFigureLook Of(WorkState state, bool passedCheckObserved)
        {
            var live = LiveTone;
            var sunk3Dp = -DpToM(3);
            return state switch
            {
                WorkState.NotStarted => Quiet(QuietTone, 0.30, pool: null, 0, floatSpeed: 0.6, riseM: 0),
                WorkState.Starting => Lit(live, 0, wanders: false),
                WorkState.Working => Lit(live, 0, wanders: true),
                WorkState.CheckingItsWork => Lit(live, 0.15, wanders: false),
                WorkState.WaitingForYou => new KitFigureLook(NeedsTone, 0, 0, 0, false, GlowLevel, NeedsTone, PoolLevel, true, 1, 1, true, false, 1, 0.008, 8, 0),
                WorkState.FinishedThisRound when passedCheckObserved => new KitFigureLook(PassedTone, 0, 0, 0, true, GlowLevel, PassedTone, PoolLevel, false, 1, 1, true, false, 1, 0, 0, 0),
                WorkState.FinishedThisRound => Quiet(NeutralTone, 0.25, NeutralTone, RoundEndedPoolLevel, floatSpeed: 0.6, riseM: -0.003),
                WorkState.ChecksFailed or WorkState.CouldNotFinish => new KitFigureLook(FailedTone, 0.35, 0, 0, false, GlowLevel, FailedTone, PoolLevel, false, 1, 1, true, false, 0.5, sunk3Dp, 0, 8),
                WorkState.Stopped => Quiet(QuietTone, 1, pool: null, 0, floatSpeed: 0.6, riseM: -0.006),
                WorkState.CantTellYet => new KitFigureLook(QuietTone, 0.10, 0.30, 11, false, GlowLevel, null, 0, false, 1, 1, true, false, 1, 0, 4, 0),
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "a state the figure has no look for"),
            };
        }

        /// <summary>
        /// The headset lost the computer: the last known look frozen, its lids held, the eyes n4, the glow low, no pool, the
        /// body dimmed, and no motion at all (Last Known). A deliberate state, never a vanish.
        /// </summary>
        public static KitFigureLook Unreachable(KitFigureLook lastKnown) =>
            new KitFigureLook(LostTone, lastKnown.Lids, lastKnown.RightEyeShorter, lastKnown.RightEyeRollDegrees, false, UnreachableGlowLevel,
                null, 0, false, UnreachableLightness, UnreachableSaturation, false, false, 0, lastKnown.RiseM, lastKnown.HeadRollDegrees, lastKnown.HeadPitchDegrees);

        /// <summary>A figure's top feature by its task's place in order: identity by form, never by colour.</summary>
        public static KitFigureFeature FeatureFor(int taskOrder) => (KitFigureFeature)(((taskOrder % 6) + 6) % 6);

        /// <summary>A length in dp at the figure's distance, in metres (16 dp a degree).</summary>
        public static double DpToM(double dp) => Math.Tan(SpatialKit.Degrees((float)dp) * Math.PI / 180) * SeenFromM;

        private static KitFigureLook Lit(GlazeColor tone, double lids, bool wanders) =>
            new KitFigureLook(tone, lids, 0, 0, false, GlowLevel, tone, PoolLevel, true, 1, 1, true, wanders, 1, 0, 0, 0);

        private static KitFigureLook Quiet(GlazeColor eyes, double lids, GlazeColor? pool, double poolLevel, double floatSpeed, double riseM) =>
            new KitFigureLook(eyes, lids, 0, 0, false, GlowLevel, pool, poolLevel, false, 1, 1, true, false, floatSpeed, riseM, 0, 0);

        public bool Equals(KitFigureLook other) =>
            Eyes.Equals(other.Eyes) && Lids == other.Lids && RightEyeShorter == other.RightEyeShorter && RightEyeRollDegrees == other.RightEyeRollDegrees
            && Happy == other.Happy && Glow == other.Glow && Nullable.Equals(Pool, other.Pool) && PoolLevelShown == other.PoolLevelShown
            && Breathes == other.Breathes && Lightness == other.Lightness && Saturation == other.Saturation && Moves == other.Moves
            && Wanders == other.Wanders && FloatSpeed == other.FloatSpeed && RiseM == other.RiseM && HeadRollDegrees == other.HeadRollDegrees
            && HeadPitchDegrees == other.HeadPitchDegrees;

        public override bool Equals(object? obj) => obj is KitFigureLook other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Eyes, Lids, Pool, Happy, Moves);
    }
}
