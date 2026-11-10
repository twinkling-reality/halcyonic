#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The information panel's rows where a moment gives the bar its words as rows (<see cref="RoomBar.Facts"/>: New
    /// Project's recap, its review, a start's steps): they stand in the body's place, as wide as the body's words, and
    /// their height is the body's. The pair's own laying calls <see cref="LayFacts"/> where it measures the body and
    /// <see cref="PlaceFacts"/> where it places it.
    /// </summary>
    public sealed partial class RoomBarView
    {
        private RoomRowsView? facts;

        /// <summary>The rows the panel shows now; null where its words are one block.</summary>
        public RoomRowsView? Facts => facts != null && facts.gameObject.activeSelf ? facts : null;

        /// <summary>
        /// Lays the bar's rows <paramref name="width"/> wide and returns how tall they stand; where the bar has none, or
        /// no panel, hides them and returns null, and the body's own words stand as ever.
        /// </summary>
        private float? LayFacts(RoomBar bar, float width)
        {
            if (bar.Facts == null || bar.Facts.Count == 0 || bar.Wide)
            {
                if (facts != null) facts.Hide();
                return null;
            }
            if (facts == null) facts = RoomRowsView.Create(panel.transform, KitType.Order.Words);
            return facts.Show(bar.Facts, width);
        }

        /// <summary>Puts the rows where the body's words start: the panel's top left, inside its padding.</summary>
        private void PlaceFacts(Vector3 bodyAt)
        {
            if (facts != null && facts.gameObject.activeSelf) facts.transform.localPosition = bodyAt;
        }
    }
}
