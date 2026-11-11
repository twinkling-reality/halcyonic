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
                if (words != null && !Mathf.Approximately(words.alpha, move.Shown)) words.alpha = move.Shown;
        }

        /// <summary>For renders: the panel in its place, whole.</summary>
        public void SettleArrival()
        {
            arrivedAt = PanelClock() - RoomMotion.PanelSeconds;
            FollowArrival();
        }
    }
}
