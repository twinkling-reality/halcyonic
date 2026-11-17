#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The information panel's arrival (DESIGN's BUILD-SPEC-v4 1.5): with a new moment the panel rises about a degree to its
    /// place and fades in over 360 ms, ease out, its glass (<see cref="RoomSurface.Shown"/>) and every word on it together.
    /// A moment is new where the bar's state, its words or its sentence changed; a bar laid again with the same moment
    /// does not arrive again. The pair's laying calls <see cref="ArrivePanel"/> once it has placed the panel, and the
    /// view's frame calls <see cref="FollowArrival"/>. Kept still (<see cref="GlazeMotion.Still"/>, as renders keep it), it
    /// stands in its place at once.
    /// </summary>
    public sealed partial class RoomBarView
    {
        private string? arrivalKey;
        private float arrivedAt;
        private Vector3 panelRest;
        private readonly List<TMP_Text> panelWords = new List<TMP_Text>();

        /// <summary>Each panel word's own alpha as laid (Laid) and the alpha the arrival last gave it (Applied).</summary>
        private readonly Dictionary<TMP_Text, (float Laid, float Applied)> panelWordsRest = new Dictionary<TMP_Text, (float, float)>();

        /// <summary>The clock the panel's arrival runs on; renders fix it.</summary>
        public Func<float> PanelClock { get; set; } = () => Time.unscaledTime;

        /// <summary>How far the panel has arrived now, 0 to 1, and how far below its place it stands, in dp.</summary>
        public RoomMove PanelArrival { get; private set; } = RoomMove.Settled;

        /// <summary>Starts the panel's arrival where <paramref name="bar"/> is a new moment, else keeps where it is; then places it.</summary>
        private void ArrivePanel(RoomBar bar)
        {
            panelRest = panel.transform.localPosition;
            var key = bar.State + "\n" + bar.Body.Text + "\n" + bar.Sentence + "\n" + (bar.Facts?.Count ?? 0);
            if (key != arrivalKey)
            {
                arrivalKey = key;
                arrivedAt = PanelClock();
            }
            panelWords.Clear();
            panel.GetComponentsInChildren(false, panelWords);
            // Each word's own alpha as laid, so the arrival fades it from there: a quiet label stays quiet, never lifted to full
            // (the lead's 2026-11-17 01:56Z MUST 2: the row notes read 228 against the words' 233 because this set every alpha to 1).
            // A word still at the alpha the arrival last gave it was not laid again: it keeps its rest; one laid again brings its own.
            foreach (var words in panelWords)
            {
                if (words == null) continue;
                if (!(panelWordsRest.TryGetValue(words, out var known) && Mathf.Approximately(words.alpha, known.Applied)))
                    panelWordsRest[words] = (words.alpha, words.alpha);
            }
            FollowArrival();
        }

        /// <summary>Places the panel where its arrival puts it now: lowered by its move, its glass and its words faded by it.</summary>
        private void FollowArrival()
        {
            if (!panel.gameObject.activeSelf || arrivalKey == null) return;
            var move = GlazeMotion.Still ? RoomMove.Settled : RoomMotion.PanelArrives(PanelClock() - arrivedAt);
            PanelArrival = move;
            panel.Shown = move.Shown;
            panel.transform.localPosition = panelRest + Vector3.down * (move.LowerDp * RoomType.Units(1f));
            foreach (var words in panelWords)
            {
                if (words == null) continue;
                var rest = panelWordsRest.TryGetValue(words, out var known) ? known.Laid : 1f;
                var applied = rest * move.Shown;
                if (!Mathf.Approximately(words.alpha, applied)) words.alpha = applied;
                panelWordsRest[words] = (rest, words.alpha);
            }
        }

        /// <summary>For renders: the panel in its place, whole.</summary>
        public void SettleArrival()
        {
            arrivedAt = PanelClock() - RoomMotion.PanelSeconds;
            FollowArrival();
        }
    }
}
