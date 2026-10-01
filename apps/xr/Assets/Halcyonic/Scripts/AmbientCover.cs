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
    /// has focus is inactive, so the banner, with what waits for the person, shows again. It counts
    /// in the editor too, so renders see what the headset would.
    /// </summary>
    [ExecuteAlways]
    public sealed class AmbientCover : MonoBehaviour
    {
        private static readonly HashSet<AmbientCover> active = new HashSet<AmbientCover>();

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

        /// <summary>Marks <paramref name="owner"/> as covering the banner's place while it is active.</summary>
        public static AmbientCover Add(GameObject owner, bool panel)
        {
            var cover = owner.AddComponent<AmbientCover>();
            cover.Panel = panel;
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

        /// <summary>Forgets every cover when play mode starts without a domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            active.Clear();
            Changed = null;
        }
    }
}
