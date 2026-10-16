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

        bool AgentsEnabled { get; }
        bool ShelfPlaced { get; }
        void ToggleAgents();
        void PlaceAgents();

        /// <summary>Explicit preview for one still-current task; selection changes only after Apply.</summary>
        void PlaceTask(string workstreamId, string title, System.Func<bool> current);

        /// <summary>Shows the real room where a virtual space shows, and the other way round.</summary>
        void SwitchSpace();

        /// <summary>Does what <see cref="Offer"/> offers.</summary>
        void TakeOffer();

        /// <summary>The headset's boundary, or null where the build doesn't offer hiding it.</summary>
        BoundaryNow? Boundary { get; }

        /// <summary>Hides the boundary while the room shows, or shows it again.</summary>
        void ToggleBoundary();

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
