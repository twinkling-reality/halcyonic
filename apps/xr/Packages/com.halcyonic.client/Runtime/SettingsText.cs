#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// The words of the Settings sheet the rail opens (ADR 0023), where the controls for the room and
    /// for pairing with the Mac live, instead of floating low to either side of the person.
    /// </summary>
    public static class SettingsText
    {
        public const string Settings = "Settings";

        public const string Close = "Close";

        /// <summary>The section for where the characters stand: the real room or a virtual space, and room for a window.</summary>
        public const string YourRoom = "Your room";

        /// <summary>The section for pairing this headset with the Mac, in development builds.</summary>
        public const string YourMac = "Your Mac";

        public const string MakeRoomForWindow = "Make room for a window";

        public const string CharactersInFront = "Characters in front";
    }
}
