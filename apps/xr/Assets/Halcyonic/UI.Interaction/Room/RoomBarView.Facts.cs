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
            if (LayCard(bar, width) is float tall)
            {
                if (facts != null) facts.Hide();
                return tall;
            }
            if (bar.Facts == null || bar.Facts.Count == 0 || bar.Wide)
            {
                if (facts != null) facts.Hide();
                return null;
            }
            if (facts == null) facts = RoomRowsView.Create(panel.transform, KitType.Order.Words);
            return facts.Show(bar.Facts, width);
        }

        private RoomCardView? card;

        /// <summary>The card the panel shows now (<see cref="RoomBar.Card"/>); null where its words are rows or one block.</summary>
        public RoomCardView? Card => card != null && card.gameObject.activeSelf ? card : null;

        /// <summary>
        /// Lays the bar's card <paramref name="width"/> wide and returns how tall it stands; where the bar has none, or no
        /// panel, hides it and returns null. A card stands in the body's place as the rows do, and before them: a moment
        /// gives one or the other.
        /// </summary>
        private float? LayCard(RoomBar bar, float width)
        {
            if (bar.Card == null || bar.Wide)
            {
                if (card != null) card.Hide();
                return null;
            }
            if (card == null)
            {
                card = RoomCardView.Create(panel.transform, KitType.Order.Words);
                // A row picked from a card's list acts as any action does: its own control, raised to the bar's owner.
                card.RowPressed += control => Acted?.Invoke(control);
            }
            return card.Show(bar.Card, width);
        }

        private RoomTag? plate;

        /// <summary>The plate that names the agent and its level while it works; null where none stands.</summary>
        public RoomTag? Plate => plate != null && plate.gameObject.activeSelf ? plate : null;

        /// <summary>
        /// Stands the bar's plate (<see cref="RoomBar.Plate"/>, its level a chip after the name) with its middle at
        /// <paramref name="at"/>, where the row stood, or hides it where the bar has none. Never pressed: it is a name tag, not an action.
        /// </summary>
        private void LayPlate(RoomBar bar, Vector3 at)
        {
            if (bar.Plate is not LineWords said)
            {
                HidePlate();
                return;
            }
            if (plate == null) plate = RoomTag.Create(transform);
            plate.Show(said, chip: bar.PlateLevel, plate: true);
            plate.transform.localPosition = at;
        }

        private void HidePlate()
        {
            if (plate != null) plate.Hide();
        }

        /// <summary>Puts the rows where the body's words start: the panel's top left, inside its padding.</summary>
        private void PlaceFacts(Vector3 bodyAt)
        {
            if (facts != null && facts.gameObject.activeSelf) facts.transform.localPosition = bodyAt;
            if (card != null && card.gameObject.activeSelf) card.transform.localPosition = bodyAt;
        }
    }
}
