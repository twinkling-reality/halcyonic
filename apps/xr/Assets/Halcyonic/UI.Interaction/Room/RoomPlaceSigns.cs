#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The signs of the projects' places other than the person's shelf (the lead's 2026-11-17T18:56Z; AUDIT2's 18:57Z): each place's project
    /// name once, and its "{n} More" or "Show Fewer" press, a <see cref="RoomPlaceSignView"/> hung from the point the stage gives
    /// (<see cref="CharacterStage.PlaceCreated"/>), brought up to date each time the stage refreshes. A press spreads or gathers that place
    /// only, and the stage stands it again; nothing is journaled (presentation only, AUDIT3's MP1).
    /// </summary>
    public sealed class RoomPlaceSigns : MonoBehaviour
    {
        private CharacterStage stage = null!;
        private readonly Dictionary<string, RoomPlaceSignView> signs = new Dictionary<string, RoomPlaceSignView>(System.StringComparer.Ordinal);

        /// <summary>The sign of <paramref name="projectId"/>'s place, as it stands now; null where none does.</summary>
        public RoomPlaceSignView? SignOf(string projectId) => signs.TryGetValue(projectId, out var sign) && sign != null ? sign : null;

        /// <summary>The signs for <paramref name="shelf"/>, made once.</summary>
        public static RoomPlaceSigns On(CharacterStage shelf)
        {
            var made = shelf.GetComponent<RoomPlaceSigns>();
            if (made != null) return made;
            made = shelf.gameObject.AddComponent<RoomPlaceSigns>();
            made.stage = shelf;
            shelf.PlaceCreated += made.Made;
            shelf.Refreshed += made.Show;
            return made;
        }

        private void OnDestroy()
        {
            if (stage == null) return;
            stage.PlaceCreated -= Made;
            stage.Refreshed -= Show;
        }

        private void Made(string projectId, Transform point)
        {
            var sign = RoomPlaceSignView.Create(point);
            sign.Pressed += (project, action) =>
            {
                if (RoomPlaceSign.Act(stage.Places, action, project) == true) stage.Refresh();
            };
            signs[projectId] = sign;
        }

        private void Show()
        {
            var standing = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var (place, _) in stage.OtherPlaces)
            {
                standing.Add(place.ProjectId);
                if (SignOf(place.ProjectId) is { } sign) sign.Show(place, stage.Places);
            }
            // A place that left took its sign's point with it.
            var gone = new List<string>();
            foreach (var pair in signs)
                if (pair.Value == null || !standing.Contains(pair.Key)) gone.Add(pair.Key);
            foreach (var id in gone) signs.Remove(id);
        }
    }
}
