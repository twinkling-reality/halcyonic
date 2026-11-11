#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// Whether the headset draws the redesign (DESIGN's SYSTEM-v4, the owner's yes on 2026-11-05) where it is built so far,
    /// in place of the kit's parts it replaces. Off here by default; the Unity layer turns it on in development builds and
    /// the editor (ON-1), so every journey runs through it, and a release build keeps the older views until every frame a
    /// person can reach has a reader of the redesign.
    /// </summary>
    public static class RoomDesign
    {
        public static bool Enabled { get; set; }
    }
}
