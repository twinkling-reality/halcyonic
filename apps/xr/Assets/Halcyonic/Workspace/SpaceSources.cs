#nullable enable
using Halcyonic.Client;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The room's part of Settings' Your space (<see cref="SpaceSettings"/>), given to the workspace
    /// director by the room's controls, which the workspace cannot name: what the room shows and
    /// offers, where the characters stand, and the changes the rows raise.
    /// </summary>
    public interface IRoomSettings
    {
        RoomStatus Status { get; }

        RoomOffer Offer { get; }

        /// <summary>Where the characters stand, or null on a desk, where the room places them.</summary>
        StageArrangement? Arrangement { get; }

        /// <summary>Shows the real room where a virtual space shows, and the other way round.</summary>
        void SwitchSpace();

        /// <summary>Does what <see cref="Offer"/> offers.</summary>
        void TakeOffer();

        /// <summary>Stands the characters in the next arrangement (<see cref="SpaceSettings.Next"/>).</summary>
        void NextArrangement();
    }

    /// <summary>
    /// Pairing with the person's computer, Settings' Your computer, given to the workspace director in
    /// a development build by the pairing, which the workspace cannot name.
    /// </summary>
    public interface IPairingSettings
    {
        PairingNow Now { get; }

        /// <summary>Pairs with a computer, where none is paired.</summary>
        void Pair();

        /// <summary>
        /// Forgets the paired computer, only when it is the one at <paramref name="confirmedAddress"/>,
        /// the address Settings asked about and the person said Yes to (<see cref="PairingNow.Forgets"/>).
        /// </summary>
        void Forget(string confirmedAddress);
    }
}
