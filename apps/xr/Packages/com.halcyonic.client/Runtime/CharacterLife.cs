#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>Where a character looks; the character view finds the point and splits the turn with <see cref="CharacterLife.Aim"/>.</summary>
    public enum CharacterGaze
    {
        /// <summary>Its project's screen.</summary>
        Screen,

        /// <summary>The person's eyes.</summary>
        Person,

        /// <summary>Down and to one side (<see cref="CharacterPose.AvertSide"/>), on purpose, after holding the person's gaze.</summary>
        Away,

        /// <summary>The person's lap, so the face stays in view from the seat.</summary>
        Lap,

        /// <summary>A neighbouring character, or its partner in a meeting.</summary>
        Neighbour,

        /// <summary>The moment card it just released.</summary>
        Moment,
    }

    /// <summary>The short accents a character gives now and then while it works (C3, C4): one at a time.</summary>
    public enum CharacterOneShot
    {
        None,
        GlanceAtPerson,
        GlanceAtNeighbour,
        EyeDart,
        HeadRoll,
        EighthTurn,
        FeatureTwitch,
    }

    /// <summary>
    /// A character's pose at one moment, for any figure: offsets from its home in metres (x to the person's right, z away
    /// from the person), its own lifts in metres, angles in degrees, the eyes' shift in the figure's heights, lids 0 open
    /// to 1 closed. The figure applies it to its own parts; nothing here knows its shape.
    /// </summary>
    public struct CharacterPose : IEquatable<CharacterPose>
    {
        /// <summary>The figure's place inside its home: the wander, a meeting.</summary>
        public double XM, ZM;

        /// <summary>The float, vertical and sideways, and its roll.</summary>
        public double FloatYM, FloatXM, RollDegrees;

        /// <summary>The posture held and a departure's rise, without the float or the hop.</summary>
        public double LiftM;

        /// <summary>A hop: the passed check's, or a speaker's in a meeting.</summary>
        public double HopM;

        /// <summary>The body's height scale through a hop's squash, 1 at rest; its width takes the inverse.</summary>
        public double Squash;

        /// <summary>The body's turn from facing the person, positive to the person's right; leaning back is positive.</summary>
        public double TurnDegrees, LeanDegrees;

        /// <summary>How far the head trails the body's float across the gap.</summary>
        public double HeadLagM;

        /// <summary>The head rolled and pitched down by its posture and accents, and a nod on top (down positive).</summary>
        public double HeadRollDegrees, HeadPitchDegrees, NodDegrees;

        public CharacterGaze Gaze;

        /// <summary>Which side a look away goes: -1 the person's left, 1 their right.</summary>
        public int AvertSide;

        /// <summary>The eyes' extra shift sideways, in the figure's heights: reading darts and an eye dart.</summary>
        public double EyeDartH;

        /// <summary>How closed the eyes are by blinks, pulses and an arrival, over the look's own lids.</summary>
        public double Lids;

        /// <summary>The figure's size: 0 folded, 1 present, through an arrival's spring.</summary>
        public double Scale;

        /// <summary>The pool's level as a share of the look's: its breath.</summary>
        public double Breath;

        public CharacterOneShot OneShot;

        /// <summary>The top feature's twitch, 0 to 1 through it, or null.</summary>
        public double? Twitch;

        public bool Equals(CharacterPose other) =>
            XM == other.XM && ZM == other.ZM && FloatYM == other.FloatYM && FloatXM == other.FloatXM && RollDegrees == other.RollDegrees
            && LiftM == other.LiftM && HopM == other.HopM && Squash == other.Squash && TurnDegrees == other.TurnDegrees
            && LeanDegrees == other.LeanDegrees && HeadLagM == other.HeadLagM && HeadRollDegrees == other.HeadRollDegrees
            && HeadPitchDegrees == other.HeadPitchDegrees && NodDegrees == other.NodDegrees && Gaze == other.Gaze && AvertSide == other.AvertSide
            && EyeDartH == other.EyeDartH && Lids == other.Lids && Scale == other.Scale && Breath == other.Breath && OneShot == other.OneShot
            && Nullable.Equals(Twitch, other.Twitch);

        public override bool Equals(object? obj) => obj is CharacterPose other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(XM, ZM, FloatYM, Lids, Gaze, Scale);
    }

    /// <summary>The facts the character's look takes from the journal and the connection, kept apart from its motion.</summary>
    public static class CharacterFacts
    {
        /// <summary>
        /// The computer's own passed check, observed in this round (AUDIT B1): the Ready moment, which only a passed check of
        /// the computer's own makes, at or after <paramref name="roundStartedAt"/>. A check from an earlier round, an agent's
        /// report or an unknown round start claims nothing.
        /// </summary>
        public static bool PassedCheckThisRound(KitMoment? shown, DateTimeOffset? roundStartedAt) =>
            shown != null && shown.Kind == KitMomentKind.Ready && shown.Provenance == KitProvenance.Observed
            && shown.At is DateTimeOffset at && roundStartedAt is DateTimeOffset start && at >= start;
    }

    /// <summary>
    /// A Workstream's character alive by behaviour (the art direction's character handoff, 2026-10-30, sections 2 and 3;
    /// CHARACTER.md sections 3 to 6; motion as its approved prototype): it floats on its own rhythm, blinks, holds the
    /// person's gaze and looks away, gives one accent at a time, wanders inside its home while Running, arrives from a seed,
    /// leaves only in words, meets over a link the meeting gives it, and freezes, dimmed, while the computer is
    /// unreachable. Its look is <see cref="KitFigureLook"/> from the journal's state, the computer's own passed check
    /// observed for this round (<see cref="CharacterFacts"/>) and the control plane's reachability; the happy eyes last
    /// <see cref="KitFigureLook.HappyMs"/> (AUDIT B1 to B3). Nothing here follows progress, the agent's words or a figure's
    /// shape. Seeded by its Workstream, so the same task moves the same way each time; it allocates nothing per step.
    /// </summary>
    public sealed class CharacterLife
    {
        /// <summary>The word its tag says while the person puts its task away, at least <see cref="PutAwayWordSeconds"/> first.</summary>
        public const string PutAwayWord = "Put Away";

        public const double PutAwayWordSeconds = 1.2, FoldSeconds = 0.4, TagFadeSeconds = 0.2;

        /// <summary>The eyes' reach inside the face, sideways and up, in the figure's heights; the head turns at most this far.</summary>
        public const double EyeReachH = 0.045, EyeLiftH = 0.03, HeadReachDegrees = 35;

        /// <summary>The home: an ellipse 0.36 by 0.16 m, and the wander's pace.</summary>
        public const double HomeHalfWidthM = 0.18, HomeHalfDepthM = 0.08, SpeedMPerS = 0.12;

        /// <summary>Figures keep at least this far apart as the person sees them, so their tags never overlap.</summary>
        public const double ApartDegrees = 13;

        private const double Tau = Math.PI * 2;
        private const double Degree = Math.PI / 180;

        private readonly Random random;
        private readonly double verticalPeriod, sidewaysPeriod, rollPeriod, phase;
        private readonly List<CharacterSound> sounds = new List<CharacterSound>(4);

        private WorkState state = WorkState.NotStarted;
        private bool shown, passed, reachable = true;

        /// <summary>The control plane was lost since the last reachable show: the next is taken as a first show, never as news.</summary>
        private bool afterLoss;
        private KitFigureLook baseLook = KitFigureLook.Of(WorkState.NotStarted, false), lastKnown = KitFigureLook.Of(WorkState.NotStarted, false);
        private double now, floatClock, floatAmplitude = 1;

        private double happyUntil = double.NegativeInfinity;
        private double? hopT;
        private double hopDp = 10;
        private double? nodT;
        private double pulse;
        private double blinkIn, blink;
        private bool doubleBlink;

        private CharacterGaze gaze = CharacterGaze.Screen;
        private bool gazeChosen;
        private double gazeUntil;
        private int avertSide = 1;
        private double momentLookUntil = double.NegativeInfinity, leanUntil = double.NegativeInfinity;

        private CharacterOneShot shot;
        private double shotT, shotSeconds, nextShot;
        private int shotSide = 1;

        private double x, z, fromX, fromZ, toX, toZ, moveT, moveSeconds, nextWander;
        private bool moving;

        private double tilt, lift, lean;
        private double unknownT = double.NegativeInfinity;

        private enum Presence { Present, Arriving, Folding, Folded }

        private Presence presence = Presence.Present;
        private double presenceT, scale = 1, foldDelay, rise;
        private bool puttingAway, gone;

        private int meetingTurn = -1;
        private bool meeting, listening;

        private bool frozen;

        /// <summary>The last pose shown, which Last Known holds; at rest before the first step.</summary>
        private CharacterPose frozenPose = new CharacterPose { Squash = 1, Scale = 1, Breath = 1, AvertSide = 1 };

        public CharacterLife(string workstreamId, KitFigureFeature feature)
        {
            Feature = feature;
            random = new Random(Seed(workstreamId));
            verticalPeriod = 3.2 * Range(0.85, 1.15);
            sidewaysPeriod = 5.3 * Range(0.85, 1.15);
            rollPeriod = 7.1 * Range(0.85, 1.15);
            phase = Range(0, 10);
            blinkIn = Range(0.5, 2.6);
            nextShot = Range(4, 9);
            nextWander = Range(10, 20);
        }

        public KitFigureFeature Feature { get; }

        /// <summary>Its own float periods, vertical, sideways and roll, in seconds: each within 15 percent of the kit's.</summary>
        public (double Vertical, double Sideways, double Roll) FloatPeriods => (verticalPeriod, sidewaysPeriod, rollPeriod);

        /// <summary>The look to show now: the state's, timed happy eyes, or the last known one dimmed while unreachable.</summary>
        public KitFigureLook Look => !reachable ? KitFigureLook.Unreachable(lastKnown) : baseLook.Happy && now >= happyUntil ? WithoutHappy(baseLook) : baseLook;

        public WorkState State => state;

        /// <summary>The person's Keep Still (ADR 0027 item 6): floats, blinks, wander, one-shots, hops and meetings stop; changes fade fast.</summary>
        public bool KeepStill { get; set; }

        /// <summary>Whether a neighbour stands near enough to glance at.</summary>
        public bool HasNeighbour { get; set; }

        /// <summary>Whether a wander may go to a spot in its home (x, z in metres): the view keeps neighbours 13 degrees apart.</summary>
        public Func<double, double, bool>? SpotAllowed { get; set; }

        /// <summary>The tag's own word while it is put away, or null: the tag shows the state's word.</summary>
        public string? TagWord => puttingAway && !gone ? PutAwayWord : null;

        /// <summary>The tag's opacity: it fades only after a put-away figure has folded.</summary>
        public double TagOpacity { get; private set; } = 1;

        /// <summary>Put away and folded, its tag faded: the view may remove it.</summary>
        public bool Gone => gone;

        /// <summary>
        /// Takes the journal's facts: its <paramref name="state"/>, the computer's own passed check observed for this round, and
        /// whether the control plane is reachable. Only the passed check arriving while shown and reachable brings the happy eyes
        /// and the hop, never a first show or a reconnect.
        /// </summary>
        public void Show(WorkState state, bool passedCheckThisRound, bool controlPlaneReachable)
        {
            var passedNow = state == WorkState.FinishedThisRound && passedCheckThisRound;
            if (!controlPlaneReachable)
            {
                if (reachable) frozen = false;
                reachable = false;
                afterLoss = true;
                return;
            }
            reachable = true;
            frozen = false;
            // A passed check that landed while the headset was away shows its colour, never the happy eyes or the hop (B3).
            var news = shown && !afterLoss;
            afterLoss = false;
            if (news && passedNow && !passed)
            {
                happyUntil = now + KitFigureLook.HappyMs / 1000;
                if (!KeepStill) Hop(10, CharacterSound.Hop);
            }
            if (state == WorkState.CantTellYet && (this.state != WorkState.CantTellYet || !shown)) unknownT = 0;
            if (state != this.state || !shown) gazeUntil = now;
            var wasShown = shown;
            this.state = state;
            passed = passedNow;
            shown = true;
            baseLook = KitFigureLook.Of(state, passedNow);
            if (!wasShown) floatAmplitude = baseLook.FloatSpeed;
            lastKnown = baseLook;
        }

        /// <summary>A task starting: a seed of light grows into the figure with a spring, its eyes opening last, with its chime.</summary>
        public void Arrive()
        {
            presence = Presence.Arriving;
            presenceT = 0;
            scale = 0;
            gone = false;
            puttingAway = false;
            TagOpacity = 1;
            sounds.Add(CharacterSound.Arrive);
        }

        /// <summary>The person put its task away: "Put Away" 1.2 s, facing them with a nod, then it rises and folds; then the tag fades.</summary>
        public void PutAway()
        {
            if (puttingAway || presence == Presence.Folded) return;
            puttingAway = true;
            presence = Presence.Folding;
            presenceT = 0;
            foldDelay = PutAwayWordSeconds;
            gaze = CharacterGaze.Person;
            gazeUntil = double.PositiveInfinity;
            nodT = -0.6;
        }

        /// <summary>Characters turned off (all fold together, no word, the tag stays as the task's mark) or on again.</summary>
        public void CharactersOff(bool off)
        {
            if (off)
            {
                if (presence == Presence.Folded || puttingAway) return;
                presence = Presence.Folding;
                presenceT = 0;
                foldDelay = 0;
            }
            else if (presence == Presence.Folded && !puttingAway)
            {
                presence = Presence.Arriving;
                presenceT = 0;
            }
        }

        /// <summary>A moment leaving it: a lean back for 400 ms, an eye pulse with its voice, a look at the card for 0.7 s, then at you.</summary>
        public void MomentOut()
        {
            leanUntil = now + 0.4;
            pulse = 0.24;
            sounds.Add(CharacterSound.Voice);
            momentLookUntil = now + 0.7;
            gaze = CharacterGaze.Person;
            gazeUntil = now + 0.7 + Range(2.2, 3.8);
        }

        /// <summary>The sounds due since the last call, to play at the figure.</summary>
        public IReadOnlyList<CharacterSound> TakeSounds()
        {
            if (sounds.Count == 0) return Array.Empty<CharacterSound>();
            var taken = sounds.ToArray();
            sounds.Clear();
            return taken;
        }

        /// <summary>Advances <paramref name="seconds"/> and gives the pose; <paramref name="part"/> is its part in a meeting, if any.</summary>
        public CharacterPose Step(double seconds, CharacterMeetingPart? part = null)
        {
            if (!reachable)
            {
                // Frozen in its last pose: no float, blink, glance or wander; a still figure means no live news.
                if (!frozen)
                {
                    frozenPose.Lids = 0;
                    frozen = true;
                }
                return frozenPose;
            }
            var dt = seconds;
            now += dt;
            var still = KeepStill;
            var look = baseLook;
            var pose = new CharacterPose { Squash = 1, AvertSide = avertSide, Breath = 1 };

            var arrivalLids = Presences(dt, ref pose);
            if (presence == Presence.Folded)
            {
                pose.Scale = 0;
                frozenPose = pose;
                return pose;
            }

            // A meeting's part, from the room's meeting: its place, its facing, and its turns.
            var wasMeeting = meeting;
            meeting = part.HasValue;
            listening = false;
            if (part is CharacterMeetingPart p)
            {
                x = p.XM;
                z = p.ZM;
                moving = false;
                gaze = CharacterGaze.Neighbour;
                gazeUntil = double.PositiveInfinity;
                if (p.Turn >= 0 && p.Turn != meetingTurn && p.Speaking)
                {
                    pulse = 0.24;
                    if (!still) Hop(4, null);
                    nodT = 0;
                    sounds.Add(CharacterSound.Voice);
                }
                meetingTurn = p.Turn;
                listening = p.Facing && p.Turn >= 0 && !p.Speaking;
                pose.TurnDegrees += p.TurnDegrees;
                shot = CharacterOneShot.None;
            }
            else if (wasMeeting)
            {
                // A meeting ends with both at home.
                (x, z) = (0, 0);
                meetingTurn = -1;
                gaze = CharacterGaze.Person;
                gazeUntil = now + 1;
            }

            Wander(dt, look, still);
            Gaze();
            OneShots(dt, still);

            // Lids: blinks every 2.6 s about half the time, one in eight doubled, none under Keep Still (ADR 0027 item 6,
            // over the handoff's "blinks stay"); pulses; an arrival's opening.
            if (!still) blinkIn -= dt;
            else if (blink > 0) (blink, doubleBlink) = (0, false);
            if (blinkIn <= 0)
            {
                if (random.NextDouble() < 0.5)
                {
                    blink = 0.18;
                    doubleBlink = random.NextDouble() < 0.125;
                }
                blinkIn += 2.6;
            }
            var lids = 0.0;
            if (blink > 0)
            {
                blink -= dt;
                lids = Math.Max(lids, Math.Sin(Clamp01(1 - blink / 0.18) * Math.PI));
                if (blink <= 0 && doubleBlink)
                {
                    doubleBlink = false;
                    blink = 0.18;
                }
            }
            if (pulse > 0)
            {
                pulse = Math.Max(0, pulse - dt);
                lids = Math.Max(lids, 0.2 * Math.Sin((1 - pulse / 0.24) * Math.PI));
            }
            pose.Lids = Math.Max(lids, arrivalLids);

            // Accents.
            var shotTilt = 0.0;
            if (shot != CharacterOneShot.None)
            {
                shotT += dt;
                var u = Clamp01(shotT / shotSeconds);
                var bump = Math.Sin(u * Math.PI);
                switch (shot)
                {
                    case CharacterOneShot.HeadRoll: shotTilt = bump * 9; break;
                    case CharacterOneShot.EighthTurn: pose.TurnDegrees += bump * 45 * shotSide; break;
                    case CharacterOneShot.EyeDart: pose.EyeDartH += bump * 0.03 * shotSide; break;
                    case CharacterOneShot.FeatureTwitch: pose.Twitch = u; break;
                }
                pose.OneShot = shot;
                if (u >= 1) shot = CharacterOneShot.None;
            }
            if ((state == WorkState.Working || state == WorkState.CheckingItsWork) && gaze == CharacterGaze.Screen && !still)
                pose.EyeDartH += Math.Sin(now * 9.3 + phase) * 0.008 * (Math.Sin(now * 1.7 + phase) > 0.3 ? 1 : 0);

            // The posture held, eased; fast under Keep Still.
            var ease = still ? 25 : 1;
            var tiltTo = look.HeadRollDegrees + shotTilt + (listening ? 8 : 0);
            if (state == WorkState.CantTellYet && unknownT >= 0)
            {
                unknownT += dt;
                tiltTo += Math.Sin(Clamp01(unknownT / 1.6) * Math.PI) * 10;
            }
            tilt = Damp(tilt, tiltTo, 6 * ease, dt);
            lift = Damp(lift, look.RiseM, 5 * ease, dt);
            lean = Damp(lean, (now < leanUntil ? 3 : 0) + (part?.LeanDegrees ?? 0), 10 * ease, dt);
            pose.HeadRollDegrees = tilt;
            pose.HeadPitchDegrees = look.HeadPitchDegrees;
            pose.LeanDegrees = lean;
            pose.LiftM = lift + rise;

            if (nodT is double nod)
            {
                nodT = nod + dt;
                if (nodT >= 0) pose.NodDegrees = Math.Sin(Clamp01(nodT.Value / 0.5) * Math.PI) * 0.25 / Degree;
                if (nodT > 0.5) nodT = null;
            }

            if (hopT is double h)
            {
                hopT = h + dt;
                var u = hopT.Value / 0.52;
                if (u >= 1) hopT = null;
                else
                {
                    pose.HopM = Math.Sin(u * Math.PI) * KitFigureLook.DpToM(hopDp);
                    pose.Squash = u < 0.15 ? 1 - 0.03 * Math.Sin(u / 0.15 * Math.PI) : 1 + 0.015 * Math.Sin((u - 0.15) / 0.85 * Math.PI);
                }
            }

            // Floats on its own rhythm, quieter where the state rests; none under Keep Still.
            floatAmplitude = Damp(floatAmplitude, still ? 0 : look.FloatSpeed, 25, dt);
            floatClock += dt * look.FloatSpeed;
            pose.FloatYM = Vertical(floatClock);
            pose.FloatXM = Math.Sin((floatClock + phase) / sidewaysPeriod * Tau) * KitFigureLook.DpToM(1.5) * floatAmplitude;
            pose.RollDegrees = Math.Sin((floatClock + phase) / rollPeriod * Tau) * 2 * floatAmplitude;
            pose.HeadLagM = (Vertical(floatClock - 0.12 * look.FloatSpeed) - pose.FloatYM) * 0.8;

            pose.Breath = look.Breathes && !still ? 0.775 + 0.225 * Math.Sin(now / 2.4 * Tau) : 1;
            pose.XM = x;
            pose.ZM = z;
            pose.Gaze = now < momentLookUntil ? CharacterGaze.Moment : gaze;
            pose.AvertSide = avertSide;
            frozenPose = pose;
            return pose;
        }

        /// <summary>
        /// Splits a look <paramref name="yawDegrees"/> to the side and <paramref name="pitchDegrees"/> up, from the body
        /// facing the person, between the head (at most <see cref="HeadReachDegrees"/>) and the eyes inside the face (at most
        /// <see cref="EyeReachH"/> sideways and <see cref="EyeLiftH"/> up), as the prototype does.
        /// </summary>
        public static (double HeadYawDegrees, double HeadPitchDegrees, double EyeXH, double EyeYH) Aim(double yawDegrees, double pitchDegrees) =>
            (Math.Clamp(yawDegrees, -HeadReachDegrees, HeadReachDegrees), Math.Clamp(pitchDegrees, -HeadReachDegrees, HeadReachDegrees),
                Math.Clamp(yawDegrees * Degree * 0.5 / 0.6 * EyeReachH, -EyeReachH, EyeReachH),
                Math.Clamp(pitchDegrees * Degree * 0.5 / 0.6 * EyeLiftH, -EyeLiftH, EyeLiftH));

        /// <summary>A critically damped follow toward <paramref name="to"/> at <paramref name="rate"/> per second (head 6, eyes 14, body 3).</summary>
        public static double Damp(double from, double to, double rate, double seconds) => from + (to - from) * (1 - Math.Exp(-rate * seconds));

        /// <summary>How long a move of <paramref name="metres"/> takes, eased in and out: 1.5 times its distance over the pace, at least 0.6 s.</summary>
        public static double MoveSeconds(double metres) => Math.Max(0.6, metres / SpeedMPerS * 1.5);

        /// <summary>The ease in and out of every move: cubic.</summary>
        public static double EaseInOut(double u) => u < 0.5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;

        /// <summary>Whether two figures stand at least <see cref="ApartDegrees"/> apart in azimuth as seen from <paramref name="seat"/> (y up, -z ahead).</summary>
        public static bool ApartEnough((double X, double Y, double Z) seat, (double X, double Y, double Z) a, (double X, double Y, double Z) b)
        {
            double Azimuth((double X, double Y, double Z) p) => Math.Atan2(p.X - seat.X, -(p.Z - seat.Z)) / Degree;
            return Math.Abs(Azimuth(a) - Azimuth(b)) >= ApartDegrees;
        }

        private double Vertical(double clock) => Math.Sin((clock + phase) / verticalPeriod * Tau) * KitFigureLook.DpToM(2.5) * floatAmplitude;

        /// <summary>Arrival, fold and put-away; gives the lids an arrival holds shut.</summary>
        private double Presences(double dt, ref CharacterPose pose)
        {
            var lids = 0.0;
            presenceT += dt;
            switch (presence)
            {
                case Presence.Arriving:
                {
                    // A seed of light, then a spring to full size (damping 0.75, 11 rad/s); the eyes open last.
                    const double w = 11, damping = 0.75;
                    var wd = w * Math.Sqrt(1 - damping * damping);
                    var t = presenceT - 0.05;
                    scale = t < 0 ? 0 : 1 - Math.Exp(-damping * w * t) * Math.Cos(wd * t);
                    lids = presenceT < 0.45 ? 1 : Math.Max(0, 1 - (presenceT - 0.45) / 0.15);
                    if (presenceT > 0.7)
                    {
                        presence = Presence.Present;
                        scale = 1;
                    }
                    break;
                }
                case Presence.Folding:
                    if (presenceT >= foldDelay)
                    {
                        var v = Clamp01((presenceT - foldDelay) / FoldSeconds);
                        scale = 1 - v * v * v;
                        rise = puttingAway ? KitFigureLook.DpToM(20) * EaseInOut(v) : 0;
                        if (v >= 1)
                        {
                            presence = Presence.Folded;
                            presenceT = 0;
                            scale = 0;
                            sounds.Add(CharacterSound.Fold);
                        }
                    }
                    break;
                case Presence.Folded:
                    if (puttingAway)
                    {
                        TagOpacity = Math.Max(0, 1 - presenceT / TagFadeSeconds);
                        if (presenceT >= TagFadeSeconds) gone = true;
                    }
                    break;
                default:
                    rise = 0;
                    break;
            }
            pose.Scale = scale;
            return lids;
        }

        private void Wander(double dt, KitFigureLook look, bool still)
        {
            if (meeting || still || presence != Presence.Present) return;
            if (look.Wanders)
            {
                nextWander -= dt;
                if (!moving && nextWander <= 0)
                {
                    var angle = Range(0, Tau);
                    var r = Math.Sqrt(random.NextDouble());
                    var spotX = Math.Cos(angle) * r * HomeHalfWidthM;
                    var spotZ = Math.Sin(angle) * r * HomeHalfDepthM;
                    if (SpotAllowed == null || SpotAllowed(spotX, spotZ)) Go(spotX, spotZ);
                    nextWander = Range(10, 20);
                }
            }
            else if (!moving && (Math.Abs(x) > 1e-9 || Math.Abs(z) > 1e-9)) Go(0, 0);
            if (moving)
            {
                moveT += dt;
                var u = Clamp01(moveT / moveSeconds);
                var e = EaseInOut(u);
                x = fromX + (toX - fromX) * e;
                z = fromZ + (toZ - fromZ) * e;
                if (u >= 1)
                {
                    moving = false;
                    x = toX;
                    z = toZ;
                }
            }
        }

        private void Go(double toX, double toZ)
        {
            var distance = Math.Sqrt((toX - x) * (toX - x) + (toZ - z) * (toZ - z));
            if (distance < 1e-6)
            {
                (x, z) = (toX, toZ);
                return;
            }
            (fromX, fromZ, this.toX, this.toZ) = (x, z, toX, toZ);
            moveT = 0;
            moveSeconds = MoveSeconds(distance);
            moving = true;
        }

        /// <summary>Holds the person's gaze, looks away on purpose, returns (C8, C2); Running looks at its screen most of the time.</summary>
        private void Gaze()
        {
            if (meeting || puttingAway || (gazeChosen && now <= gazeUntil)) return;
            gazeChosen = true;
            switch (state)
            {
                case WorkState.ChecksFailed:
                case WorkState.CouldNotFinish:
                case WorkState.Stopped:
                case WorkState.CantTellYet:
                    gaze = CharacterGaze.Lap;
                    gazeUntil = now + 4;
                    return;
            }
            if (gaze == CharacterGaze.Person)
            {
                gaze = CharacterGaze.Away;
                avertSide = random.NextDouble() < 0.5 ? -1 : 1;
                gazeUntil = now + Range(0.8, 1.6);
                return;
            }
            switch (state)
            {
                case WorkState.WaitingForYou:
                    gaze = CharacterGaze.Person;
                    gazeUntil = now + Range(2.2, 3.8);
                    break;
                case WorkState.Working:
                    gaze = random.NextDouble() < 0.33 ? CharacterGaze.Person : CharacterGaze.Screen;
                    gazeUntil = now + (gaze == CharacterGaze.Person ? Range(2.2, 3.8) : Range(2, 5));
                    break;
                case WorkState.CheckingItsWork:
                    gaze = CharacterGaze.Screen;
                    gazeUntil = now + Range(3, 6);
                    break;
                default:
                    // Not Started, Starting, Round Ended: at you and at its screen in turn, a look away between.
                    gaze = gaze == CharacterGaze.Screen ? CharacterGaze.Person : CharacterGaze.Screen;
                    gazeUntil = now + Range(2.2, 3.8);
                    break;
            }
        }

        /// <summary>One accent at a time, every 4 to 9 s while Running, Checking or Round Ended (C4).</summary>
        private void OneShots(double dt, bool still)
        {
            if (still || meeting || presence != Presence.Present
                || !(state == WorkState.Working || state == WorkState.CheckingItsWork || state == WorkState.FinishedThisRound)) return;
            nextShot -= dt;
            if (nextShot > 0 || shot != CharacterOneShot.None) return;
            var kind = (CharacterOneShot)(1 + random.Next(HasNeighbour ? 6 : 5));
            if (!HasNeighbour && kind >= CharacterOneShot.GlanceAtNeighbour) kind++;
            shot = kind;
            shotT = 0;
            shotSeconds = kind == CharacterOneShot.EighthTurn ? 1.6 : 0.9;
            nextShot = Range(4, 9);
            shotSide = random.NextDouble() < 0.5 ? -1 : 1;
            if (kind == CharacterOneShot.GlanceAtPerson)
            {
                gaze = CharacterGaze.Person;
                gazeUntil = now + Range(0.9, 1.4);
            }
            else if (kind == CharacterOneShot.GlanceAtNeighbour)
            {
                gaze = CharacterGaze.Neighbour;
                gazeUntil = now + Range(0.8, 1.2);
            }
        }

        private void Hop(double dp, CharacterSound? sound)
        {
            hopT = 0;
            hopDp = dp;
            if (sound is CharacterSound s) sounds.Add(s);
        }

        private double Range(double from, double to) => from + (to - from) * random.NextDouble();

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        private static KitFigureLook WithoutHappy(KitFigureLook l) =>
            new KitFigureLook(l.Eyes, l.Lids, l.RightEyeShorter, l.RightEyeRollDegrees, false, l.Glow, l.Pool, l.PoolLevelShown, l.Breathes,
                l.Lightness, l.Saturation, l.Moves, l.Wanders, l.FloatSpeed, l.RiseM, l.HeadRollDegrees, l.HeadPitchDegrees);

        /// <summary>A stable seed from the Workstream's id (FNV-1a), never the runtime's randomised string hash.</summary>
        private static int Seed(string id)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var c in id) hash = (hash ^ c) * 16777619u;
                return (int)hash;
            }
        }
    }
}
