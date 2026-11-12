#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// The one press (the lead's decision R1, FIELD's design of 2026-11-12): every action is one pinch on one button, acting
    /// on its RELEASE, and the guard against a stray pinch lives inside that press, never in a second button or a wait.
    /// <list type="bullet">
    /// <item>A press counts only where the pinch began after the button ARRIVED (its command, words, place or visibility last
    /// changed) and, for a ray, after the ray rested on it <see cref="SettleSeconds"/>: the pinch that answered never confirms.</item>
    /// <item>Lit means it will take: <see cref="Lit"/> is what the button's hover shows; a refused pinch sinks nothing.</item>
    /// <item>A ray off the button for under <see cref="JitterSeconds"/> neither cancels a press nor restarts the rest, and a
    /// release within that time still acts (hand rays jitter on pinch and release).</item>
    /// <item>A pinch that drags past <see cref="DragDegrees"/> (a ray's turn) or <see cref="DragMetres"/> (a finger's travel)
    /// after pinch-down never acts.</item>
    /// </list>
    /// Times are seconds on one clock; nothing here reads a clock of its own, so a check drives it exactly.
    /// </summary>
    public sealed class PressGuard
    {
        /// <summary>How long a button rests in view, and a ray on it, before a pinch on it counts (Meta's hover eases in over 0.3 s).</summary>
        public const float SettleSeconds = 0.35f;

        /// <summary>A ray off the button for less than this has not left it.</summary>
        public const float JitterSeconds = 0.1f;

        /// <summary>A ray that turns more than this between pinch and release dragged; it did not press.</summary>
        public const float DragDegrees = 4f;

        /// <summary>A finger that travels more than this along the button between pinch and release dragged.</summary>
        public const float DragMetres = 0.02f;

        private float arrivedAt;
        private bool rayOn;
        private float restSince = float.PositiveInfinity;
        private float leftAt = float.NegativeInfinity;
        private bool armed;

        public PressGuard(float now) => arrivedAt = now;

        /// <summary>The button arrived anew (new command, words, place or visibility): a press under way is not one of it.</summary>
        public void Arrive(float now)
        {
            arrivedAt = now;
            armed = false;
        }

        /// <summary>For renders, which have no hands: as if the button and a ray had rested on it long ago.</summary>
        public void SettleForRender()
        {
            arrivedAt = float.NegativeInfinity;
            restSince = float.NegativeInfinity;
        }

        /// <summary>A ray came onto the button. Back within <see cref="JitterSeconds"/> of leaving, its rest goes on.</summary>
        public void RayOn(float now)
        {
            if (rayOn) return;
            rayOn = true;
            if (now - leftAt > JitterSeconds || float.IsPositiveInfinity(restSince)) restSince = now;
        }

        /// <summary>The ray left the button.</summary>
        public void RayOff(float now)
        {
            if (!rayOn) return;
            rayOn = false;
            leftAt = now;
        }

        /// <summary>A ray is on it, or left it less than <see cref="JitterSeconds"/> ago.</summary>
        public bool RayHere(float now) => rayOn || now - leftAt <= JitterSeconds;

        /// <summary>Whether a pinch made now would count: what the button's hover shows, so lit means it will take.</summary>
        public bool Lit(float now, bool ray = true) =>
            now - arrivedAt >= SettleSeconds && (!ray || (RayHere(now) && now - Rest(now) >= SettleSeconds));

        private float Rest(float now) => float.IsPositiveInfinity(restSince) ? now : restSince;

        /// <summary>A pinch began on the button; true where it counts, so the button sinks. A ray's pinch needs the ray's rest; a finger's needs only the arrival's.</summary>
        public bool Down(float now, bool ray)
        {
            armed = Lit(now, ray);
            return armed;
        }

        /// <summary>
        /// Between pinch and release, how far it has moved: a ray's turn in degrees, a finger's travel in metres. Past the
        /// threshold the press is a drag and never acts.
        /// </summary>
        public void Moved(float degrees, float metres)
        {
            if (degrees > DragDegrees || metres > DragMetres) armed = false;
        }

        /// <summary>
        /// The pinch let go; true where it acts: it counted at pinch-down, did not drag, and, for a ray, let go on the
        /// button (or within <see cref="JitterSeconds"/> of leaving it). Cancelled by the system, it never acts.
        /// </summary>
        public bool Up(float now, bool ray, bool cancelled = false)
        {
            var acts = armed && !cancelled && (!ray || RayHere(now));
            armed = false;
            return acts;
        }

        /// <summary>A counted pinch is held now: the button shows its press.</summary>
        public bool Armed => armed;
    }
}
