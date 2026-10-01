#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Something shown where the stage's banner goes, under the characters (ADR 0023): a foreground
    /// panel, such as the workspace or the entry panel, or the peek. While one is active the banner
    /// steps aside, since nothing on the interface overlaps; a panel says itself whether what it
    /// shows is live, and the peek says it of its character. A panel folded while another window
    /// has focus is inactive, so the banner, with what waits for the person and which panel is still
    /// open (<see cref="OpenPanel"/>), shows again. It counts in the editor too, so renders see what
    /// the headset would.
    /// </summary>
    [ExecuteAlways]
    public sealed class AmbientCover : MonoBehaviour
    {
        private static readonly HashSet<AmbientCover> active = new HashSet<AmbientCover>();
        private static readonly List<AmbientCover> every = new List<AmbientCover>();

        private Func<string?>? openAs;

        /// <summary>A foreground panel, rather than the peek.</summary>
        public bool Panel { get; private set; }

        /// <summary>Whether anything covers the banner's place now.</summary>
        public static bool Any => active.Count > 0;

        /// <summary>Whether a foreground panel shows now: the peek then goes above its character.</summary>
        public static bool PanelShowing
        {
            get
            {
                foreach (var cover in active)
                {
                    if (cover.Panel) return true;
                }
                return false;
            }
        }

        /// <summary>Raised when something starts or stops covering the banner's place.</summary>
        public static event Action? Changed;

        /// <summary>
        /// The name of the foreground panel open now, as its owner says it, whether it shows or is
        /// folded while another window has focus: what the banner says is still open meanwhile. Null
        /// while none is open.
        /// </summary>
        public static string? OpenPanel
        {
            get
            {
                foreach (var cover in every)
                {
                    if (cover != null && cover.openAs?.Invoke() is string name) return name;
                }
                return null;
            }
        }

        /// <summary>
        /// Marks <paramref name="owner"/> as covering the banner's place while it is active. A panel
        /// gives <paramref name="openAs"/>, its name while it is open, folded or not, and null while it is closed.
        /// </summary>
        public static AmbientCover Add(GameObject owner, bool panel, Func<string?>? openAs = null)
        {
            var cover = owner.AddComponent<AmbientCover>();
            cover.Panel = panel;
            cover.openAs = openAs;
            every.Add(cover);
            return cover;
        }

        private void OnEnable()
        {
            if (active.Add(this)) Changed?.Invoke();
        }

        private void OnDisable()
        {
            if (active.Remove(this)) Changed?.Invoke();
        }

        private void OnDestroy() => every.Remove(this);

        /// <summary>Forgets every cover when play mode starts without a domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            active.Clear();
            every.Clear();
            Changed = null;
        }
    }
}
