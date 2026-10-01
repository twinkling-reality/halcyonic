#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// Where the characters stand while the stage stands in front of the person (ADR 0023): the
    /// person's choice in Settings, kept on the device. Halcyonic cannot see a window, so the two
    /// arrangements made for one assume it straight ahead, where Horizon OS most often opens one;
    /// each reduces what a window covers and guarantees nothing. On a desk the room placement decides
    /// where the stage stands, and none of these apply.
    /// </summary>
    public enum StageArrangement
    {
        /// <summary>Six characters on an arc in front of the person: the default.</summary>
        InFront,

        /// <summary>The arc turned to the person's right, so the middle in front of them is clearer.</summary>
        TurnedAside,

        /// <summary>
        /// At most four characters either side of a window lane straight ahead, at eye level, each
        /// showing its badge and marks without its title, what waits for the person nearest the window.
        /// </summary>
        BesideAWindow,
    }
}
