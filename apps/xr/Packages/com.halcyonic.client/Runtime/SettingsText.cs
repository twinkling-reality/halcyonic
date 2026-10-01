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

        /// <summary>The section for where the characters stand: the real room or a virtual space, and room for a window (<see cref="StageArrangement"/>).</summary>
        public const string YourRoom = "Your room";

        /// <summary>The section for pairing this headset with the Mac, in development builds.</summary>
        public const string YourMac = "Your Mac";

        /// <summary>Turns the characters to the person's right.</summary>
        public const string MakeRoomForWindow = "Make room for a window";

        /// <summary>Stands the characters in front of the person again.</summary>
        public const string CharactersInFront = "Characters in front";

        /// <summary>Stands at most four characters either side of a window straight ahead.</summary>
        public const string EitherSideOfWindow = "Either side of a window";

        /// <summary>On a desk, where the room placement decides where the characters stand.</summary>
        public const string ArrangedByRoom = "On your desk, the characters stand where the room puts them.";

        /// <summary>Said once on the stage's banner, the first time the person comes back from another window while the characters stand in front.</summary>
        public const string WindowOffer = "Window in the way? Settings can move the characters.";

        /// <summary>
        /// Where the characters stand now, in a line of its own under the room's: Halcyonic can't see
        /// a window, so the two arrangements made for one say it is assumed straight ahead.
        /// </summary>
        public static string Arrangement(StageArrangement arrangement) => arrangement switch
        {
            StageArrangement.TurnedAside => "The characters are turned right of a window assumed straight ahead.",
            StageArrangement.BesideAWindow => "The characters stand either side of a window assumed straight ahead.",
            _ => "The characters stand in front of you, where a window often opens.",
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
