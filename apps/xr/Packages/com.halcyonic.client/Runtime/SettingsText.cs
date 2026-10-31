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

        /// <summary>An armed change's Cancel, in the place of the press it undoes.</summary>
        public const string Cancel = "Cancel";

        /// <summary>The menu's Settings page's subject (ADR 0026).</summary>
        public const string Subject = "Settings";

        /// <summary>The heading over where the person is: the room, the characters and the menu (ADR 0026).</summary>
        public const string YourSpace = "Your Space";

        /// <summary>A chosen setting's first fact: what it is now.</summary>
        public const string Now = "Now";

        /// <summary>The section for where the characters stand: the real room or a virtual space, and room for a window (<see cref="StageArrangement"/>).</summary>
        public const string YourRoom = "Your Room";

        /// <summary>The section for pairing this headset with the Mac, in development builds.</summary>
        public const string YourMac = "Your Computer";

        /// <summary>Turns the characters to the person's right.</summary>
        public const string MakeRoomForWindow = "Window Space";

        /// <summary>Stands the characters in front of the person again.</summary>
        public const string CharactersInFront = "In Front";

        /// <summary>Stands at most four characters either side of a window straight ahead.</summary>
        public const string EitherSideOfWindow = "Window Sides";

        /// <summary>On a desk, where the room placement decides where the characters stand.</summary>
        public const string ArrangedByRoom = "Placed by Room";

        /// <summary>Said once on the stage's banner, the first time the person comes back from another window while the characters stand in front.</summary>
        public const string WindowOffer = "Window in the Way? · Move Characters";

        /// <summary>
        /// Where the characters stand now, in a line of its own under the room's: Halcyonic can't see
        /// a window, so the two arrangements made for one say where they assume it, straight ahead.
        /// </summary>
        public static string Arrangement(StageArrangement arrangement) => arrangement switch
        {
            StageArrangement.TurnedAside => "Right of Window",
            StageArrangement.BesideAWindow => "Either Side of Window",
            _ => "In Front of You",
        };

        /// <summary>The button that stands the characters as <paramref name="arrangement"/> says, offered while another is chosen.</summary>
        public static string ChangeTo(StageArrangement arrangement) => arrangement switch
        {
            StageArrangement.TurnedAside => MakeRoomForWindow,
            StageArrangement.BesideAWindow => EitherSideOfWindow,
            _ => CharactersInFront,
        };
    }
}
