#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// What a project's place says about itself (the lead's 2026-11-17T18:56Z, AUDIT2's 18:57Z words): its project's name, once, in the
    /// person's own case and never in the label role; and, where it gathers a crowd, the one press that spreads it, "{n} More", n
    /// counting exactly the characters not shown (never one that needs the person, who always stands in front), then "Show Fewer"
    /// to gather it back. No press where nothing is gathered.
    /// </summary>
    public static class RoomPlaceSign
    {
        public const string MoreAction = "place-more";
        public const string FewerAction = "place-fewer";
        public const string FewerWords = "Show Fewer";

        /// <summary>"{n} More", as "7 More".</summary>
        public static string MoreWords(int count) =>
            count >= 1 ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " More"
                : throw new ArgumentOutOfRangeException(nameof(count), count, "\"More\" counts at least one character not shown.");

        /// <summary>The place's name as the person gave it, theirs; null where the project has no name the host knows.</summary>
        public static LineWords? NameOf(RoomPlace place) => string.IsNullOrWhiteSpace(place.Name) ? (LineWords?)null : LineWords.TheirName(place.Name);

        /// <summary>
        /// The place's press: "{n} More" where a crowd gathers characters, "Show Fewer" where the person spread it open, and null where
        /// the place stands every character by its own form.
        /// </summary>
        public static (string Words, string Action)? PressOf(RoomPlace place, RoomPlaces places)
        {
            if (place.Form != PlaceForm.Crowd) return null;
            if (places.Spread.Contains(place.ProjectId)) return (FewerWords, FewerAction);
            return place.More >= 1 ? (MoreWords(place.More), MoreAction) : ((string, string)?)null;
        }

        /// <summary>Spreads or gathers <paramref name="projectId"/>'s place by <paramref name="action"/>; whether anything changed.</summary>
        public static bool Act(RoomPlaces places, string action, string projectId) => action switch
        {
            MoreAction => places.Spread.Add(projectId),
            FewerAction => places.Spread.Remove(projectId),
            _ => false,
        };
    }
}
