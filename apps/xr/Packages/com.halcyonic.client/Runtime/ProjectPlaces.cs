#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>What a place of a project is (R3: readers match on the kind, never on its words).</summary>
    public enum ProjectPlaceKind
    {
        /// <summary>The project's own app, open in the headset's browser (<see cref="HeadsetPlay"/>).</summary>
        PlayInHeadset,
    }

    /// <summary>One place a project offers here and now: what it is, the words a person sees and says, and whose it is.</summary>
    public sealed class ProjectPlace
    {
        public ProjectPlace(ProjectPlaceKind kind, string label, string projectId)
        {
            Kind = kind;
            Label = label ?? throw new ArgumentNullException(nameof(label));
            ProjectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
        }

        public ProjectPlaceKind Kind { get; }

        /// <summary>The words drawn on the press and named to the assistant as a place it may pick.</summary>
        public string Label { get; }

        public string ProjectId { get; }
    }

    /// <summary>
    /// The places a project offers where it is shown: the one list a press and a spoken request both read (the lead's
    /// decision of 2026-11-12, LOOP's ADR draft: places are the client's, named to the assistant as <c>places</c> and
    /// picked by index as <c>go_to</c>, gone to at once with no press). A place is not a command: going there may send
    /// one the host undoes (Play starts a preview, which stopping undoes), never one that needs a review. Each lane's
    /// place joins this list as an entry; none gets a column, route or tool of its own (R5).
    /// </summary>
    public static class ProjectPlaces
    {
        /// <summary>The most places a request names (LOOP's draft: <c>places</c>, at most 16 labels).</summary>
        public const int Most = 16;

        /// <summary>
        /// The places <paramref name="project"/> offers now, in the order a screen draws them, given the control plane this
        /// headset reaches (<paramref name="target"/>), whether a demonstration shows, and whether the app can relay a
        /// preview to a paired computer (<paramref name="relays"/>) in a build that carries the relay
        /// (<paramref name="relayBuilt"/>). Empty where the project offers none.
        /// </summary>
        public static IReadOnlyList<ProjectPlace> Of(ProjectView project, ControlPlaneTarget? target, bool demonstration, bool relays = false,
            bool relayBuilt = true)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            var places = new List<ProjectPlace>();
            if (HeadsetPlay.Stands(HeadsetPlay.For(project, target, demonstration, relays: relays, relayBuilt: relayBuilt)))
                places.Add(new ProjectPlace(ProjectPlaceKind.PlayInHeadset, HeadsetPlay.ActionWords, project.ProjectId));
            return places.Count > Most ? places.GetRange(0, Most) : places;
        }

        /// <summary>The labels a request names as its <c>places</c>, in the list's order, so an index the assistant picks is an index here.</summary>
        public static IReadOnlyList<string> Labels(IReadOnlyList<ProjectPlace> places)
        {
            if (places == null) throw new ArgumentNullException(nameof(places));
            var labels = new List<string>(places.Count);
            foreach (var place in places) labels.Add(place.Label);
            return labels;
        }

        /// <summary>The place a request's <c>go_to</c> index names in <paramref name="places"/>, or null where it names none.</summary>
        public static ProjectPlace? At(IReadOnlyList<ProjectPlace> places, int index)
        {
            if (places == null) throw new ArgumentNullException(nameof(places));
            return index >= 0 && index < places.Count ? places[index] : null;
        }
    }
}
