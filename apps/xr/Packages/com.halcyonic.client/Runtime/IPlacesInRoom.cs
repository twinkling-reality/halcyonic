#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// A local overlay that places something in the room while it stands (an agent's shelf, a character's colour preview): the plane
    /// stands in its narrow form meanwhile, so the room has space for what is placed, at every text size (the lead's 2026-11-13T17:11Z
    /// ruling: Larger never costs a feature). The director reads it; nothing else.
    /// </summary>
    public interface IPlacesInRoom
    {
        /// <summary>Whether it ended by applying a shelf the stage has yet to stand: the plane waits for that shelf before deciding its width.</summary>
        bool ShelfApplied { get; }
    }
}
