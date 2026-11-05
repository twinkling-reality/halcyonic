#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// Whether the headset draws the redesign (DESIGN's SYSTEM-v4, the owner's yes on 2026-11-05) where it is built so far,
    /// in place of the kit's parts it replaces. Off by default while the redesign is built unit by unit, so the kit and the
    /// judge path stand whole between units; a development build's extra (<c>halcyonic.redesign</c>) and the redesign's
    /// render checks turn it on. The last unit turns it on by default.
    /// </summary>
    public static class RoomDesign
    {
        public static bool Enabled { get; set; }
    }
}
