#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// Whether the headset draws the redesign (DESIGN's SYSTEM-v4, the owner's yes on 2026-11-05) where it is built so far,
    /// in place of the kit's parts it replaces. Off here by default; the Unity layer turns it on in every build and
    /// the editor (ON-1, ON-1b: <c>RoomDesignDefault</c>), so every journey runs through it; a frame no reader of the redesign
    /// takes still falls back to the older views until ON-2.
    /// </summary>
    public static class RoomDesign
    {
        public static bool Enabled { get; set; }
    }
}
