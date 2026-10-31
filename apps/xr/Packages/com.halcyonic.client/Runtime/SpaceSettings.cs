#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>Where pairing with the person's computer stands, in a development build.</summary>
    public enum PairingStep
    {
        Idle,

        /// <summary>The system keyboard is asking for the address or the code.</summary>
        Typing,

        Pairing,

        Forgetting,
    }

    /// <summary>Pairing with the person's computer as Settings shows it: never the code or the credential.</summary>
    public sealed class PairingNow
    {
        /// <param name="address">The paired computer's address, as pnpm pair showed it; null while unpaired.</param>
        /// <param name="keyboard">The system keyboard can open here, which pairing needs.</param>
        public PairingNow(string? address, PairingStep step, bool keyboard)
        {
            Address = address;
            Step = step;
            Keyboard = keyboard;
        }

        public string? Address { get; }

        public PairingStep Step { get; }

        public bool Keyboard { get; }

        /// <summary>
        /// Forgetting may go ahead: nothing is under way, and the computer paired now is the one whose
        /// address Settings asked about and the person said Yes to.
        /// </summary>
        public bool Forgets(string confirmedAddress) =>
            Step == PairingStep.Idle && Address != null && string.Equals(Address, confirmedAddress, StringComparison.Ordinal);
    }

    /// <summary>What Your space and Your computer show, read each time Settings is drawn.</summary>
    public sealed class SpaceNow
    {
        /// <param name="room">The room as its controls give it, or null while none do, as before they start or once they have gone.</param>
        /// <param name="arrangement">Where the characters stand, or null on a desk, where the room places them.</param>
        /// <param name="pairing">Pairing with the person's computer, or null where it isn't ready or offered.</param>
        /// <param name="menuMoved">The person dragged the menu from where it stands by itself, until Reset position puts it back.</param>
        /// <param name="boundary">The headset's boundary as the room's passthrough gives it, or null while no room controls do.</param>
        public SpaceNow(RoomStatus? room, RoomOffer offer, StageArrangement? arrangement, PairingNow? pairing, bool menuMoved = false, bool agentsEnabled = false, bool shelfPlaced = false, AgentShelfPageInfo? shelfPage = null, BoundaryNow? boundary = null)
        {
            Room = room;
            Offer = offer;
            Arrangement = arrangement;
            Pairing = pairing;
            MenuMoved = menuMoved;
            AgentsEnabled = agentsEnabled;
            ShelfPlaced = shelfPlaced;
            ShelfPage = shelfPage;
            Boundary = boundary;
        }

        public RoomStatus? Room { get; }

        public RoomOffer Offer { get; }

        public StageArrangement? Arrangement { get; }

        public PairingNow? Pairing { get; }

        public bool MenuMoved { get; }
        public bool AgentsEnabled { get; }
        public bool ShelfPlaced { get; }
        public AgentShelfPageInfo? ShelfPage { get; }
        public BoundaryNow? Boundary { get; }
    }

    /// <summary>
    /// Your space and, in a development build, Your computer as Settings' rows (ADR 0026): the room
    /// shown, what the room offers, where the characters stand, the menu's position, and pairing with
    /// the person's computer. Each row's change only raises its id, and forgetting the computer only
    /// Settings' Yes, with the address it armed; the headset's own layer does it, as the room
    /// placement, the stage and the pairing own what they change.
    /// </summary>
    public static class SpaceSettings
    {
        public const string SwitchSpace = "space-switch";

        public const string TakeOffer = "room-offer";

        public const string ToggleAgents = "agents-toggle";
        public const string PlaceAgents = "agents-place";
        public const string PreviousAgent = "agents-previous";
        public const string NextAgent = "agents-next";

        public const string ResetPosition = "reset-position";

        /// <summary>Hides the headset's boundary while the room shows, or shows it again: development builds only.</summary>
        public const string ToggleBoundary = "boundary-toggle";

        public const string Pairing = "pairing";

        /// <summary>Why the room's rows take no change while no room is given (proposed by lane W, 2026-10-03).</summary>
        public const string RoomUnread = "Room Unreadable";

        /// <summary>Why Pairing takes no change while the pairing isn't there yet, or has gone.</summary>
        public const string PairingNotReady = "Not Ready";

        /// <param name="act">Raised with a row's id when its change is pressed; for Pairing, only to pair, unpaired.</param>
        /// <param name="forget">
        /// Raised with the paired computer's address only by Settings' Yes to forgetting it, given the
        /// address as Settings armed it, and only while that is still the computer paired with nothing
        /// under way (<see cref="PairingNow.Forgets"/>). A bare change of the row never forgets.
        /// </param>
        /// <param name="pairing">The build offers pairing, a development build: Your computer's row shows, read as the pairing stands.</param>
        /// <param name="boundary">The build may hide the headset's boundary while the room shows, a development build: the Boundary row shows.</param>
        public static IReadOnlyList<MenuSetting> Of(Func<SpaceNow> now, Action<string> act, Action<string> forget, bool pairing = false, bool boundary = false)
        {
            // Every row stands whatever is there now: each reads afresh, so a room or a pairing that
            // comes or goes while Settings shows changes what a row says, never which rows there are.
            var rows = new List<MenuSetting>
            {
                new MenuSetting(SwitchSpace, SettingsText.YourSpace, "Surroundings", () => Space(now().Room), () => act(SwitchSpace)),
                new MenuSetting(TakeOffer, SettingsText.YourSpace, "Room Layout", () => Offer(now()), () => act(TakeOffer)),
                new MenuSetting(ToggleAgents, SettingsText.YourSpace, "Agents", () => Agents(now()), () => act(ToggleAgents)),
                new MenuSetting(PlaceAgents, SettingsText.YourSpace, "Agent Shelf", () => Shelf(now()), () => act(PlaceAgents)),
                new MenuSetting(ResetPosition, SettingsText.YourSpace, "Menu", () => Menu(now().MenuMoved), () => act(ResetPosition)),
            };
            if (now().ShelfPage != null)
            {
                rows.Insert(rows.Count - 1, new MenuSetting(PreviousAgent, SettingsText.YourSpace, "Previous Agent",
                    () => AgentPage(now(), previous: true), () => act(PreviousAgent)));
                rows.Insert(rows.Count - 1, new MenuSetting(NextAgent, SettingsText.YourSpace, "Next Agent",
                    () => AgentPage(now(), previous: false), () => act(NextAgent)));
            }
            if (boundary)
            {
                rows.Insert(rows.Count - 1, new MenuSetting(ToggleBoundary, SettingsText.YourSpace, "Boundary",
                    () => Boundary(now().Boundary), () => act(ToggleBoundary)));
            }
            if (pairing)
            {
                rows.Add(new MenuSetting(Pairing, HostText.YourStart, "Pairing", () => Paired(now().Pairing), () =>
                {
                    // Pressed without asking, it only pairs: forgetting is the Yes's alone.
                    if (now().Pairing is PairingNow unpaired && unpaired.Address == null) act(Pairing);
                }, armed =>
                {
                    if (armed.About is string address && now().Pairing is PairingNow paired && paired.Forgets(address)) forget(address);
                }));
            }
            return rows;
        }

        /// <summary>The menu's row: where it stands by itself, or where the person dragged it until Reset position.</summary>
        private static SettingNow Menu(bool moved) => moved
            ? new SettingNow("Moved", "Position · Moved", "Default", "Back in Front · Within Reach", "Reset Position")
            : new SettingNow("Default", "Position · Default", "Default", "Back in Front · Within Reach", "Reset Position");

        private static SettingNow Space(RoomStatus? room)
        {
            if (room == null) return new SettingNow("Unknown", RoomUnread, "Your Room", RoomUnread, "Show My Room", RoomUnread);
            var reason = room.CanSwitch ? null : "Room Unavailable";
            return room.Shown == RoomSpace.Room
                ? new SettingNow("Your Room", room.Line, "Virtual Space", "Virtual Backdrop Replaces Room", room.SwitchLabel, reason)
                : new SettingNow("Virtual Space", room.Line, "Your Room", "Room Shows Through Headset", room.SwitchLabel, reason);
        }

        private static SettingNow Offer(SpaceNow now) => now.Room is not RoomStatus room
            ? new SettingNow("Unknown", RoomUnread, "Nothing to Set Up", RoomUnread, "Set Up Room", RoomUnread)
            : room.Scan == RoomScan.NotRead
                ? new SettingNow("Not Read", room.Line, "Place Shelf", "Read Only from Shelf Preview", "Place Shelf")
            : now.Offer switch
            {
                RoomOffer.AllowRoomAccess => new SettingNow("No Access", room.Line, RoomStatus.OfferLabel(now.Offer),
                    "Headset Asks Again · Characters Can Use Desk", RoomStatus.OfferLabel(now.Offer)),
                RoomOffer.SetUpRoom => new SettingNow("Not Set Up", room.Line, RoomStatus.OfferLabel(now.Offer),
                    "Space Setup Captures Room", RoomStatus.OfferLabel(now.Offer)),
                _ => new SettingNow("Ready", room.Line, "Nothing to Set Up", "Nothing More Needed", "Set Up Room",
                    "Nothing to Set Up"),
            };

        private static SettingNow Agents(SpaceNow now) => now.AgentsEnabled
            ? new SettingNow("On", "Agents on Placed Shelf", "Off", "Workspace Alone", "Hide Agents")
            : new SettingNow("Off", "Workspace Without Characters", now.ShelfPlaced ? "On" : "Place Shelf",
                "Confirm Shelf Before Agents", now.ShelfPlaced ? "Show Agents" : "Place Agents");

        private static SettingNow AgentPage(SpaceNow now, bool previous)
        {
            var page = now.ShelfPage;
            var available = now.AgentsEnabled && now.ShelfPlaced && page != null && (previous ? page.Previous : page.Next);
            return new SettingNow(page?.Summary ?? "No Agent Selected", page?.Title ?? "Choose Agent",
                previous ? "Previous Agent" : "Next Agent", "One Label at a Time · Tasks Stay in Workspace",
                previous ? "Previous Agent" : "Next Agent", available ? null : "No " + (previous ? "Previous" : "Next") + " Agent",
                nowRuns: page?.Title != null ? LineWords.TheirName(page.Title) : null);
        }

        /// <summary>
        /// The headset's boundary, in a development build: hidden only while the room shows, and said to be
        /// hidden only once the headset reported it (2026-10-16, boundary-visibility.md).
        /// </summary>
        private static SettingNow Boundary(BoundaryNow? boundary)
        {
            const string Again = "Boundary Returns";
            if (boundary == null) return new SettingNow("Unknown", RoomUnread, "Hidden While Room Shows", RoomUnread, "Hide Boundary", RoomUnread);
            return boundary.Step switch
            {
                BoundaryStep.Asked => new SettingNow("Hide Requested", "Hide Unconfirmed", "Shown", Again, "Show Boundary"),
                BoundaryStep.Hidden => new SettingNow("Hidden", "Hidden While Room Shows", "Shown", Again, "Show Boundary"),
                BoundaryStep.KeptOn => new SettingNow("Kept On", "Headset Kept It On · Asks Again", "Shown",
                    "Stops Asking", "Stop Asking"),
                _ => new SettingNow("Shown", "Drawn as Set Up", "Hidden While Room Shows",
                    "Returns When Room Stops or App Pauses", "Hide Boundary",
                    boundary.RoomShows ? null : "Only While Room Shows"),
            };
        }

        private static SettingNow Shelf(SpaceNow now) => new SettingNow(now.ShelfPlaced ? "Placed" : "Not Placed",
            now.ShelfPlaced ? (now.ShelfPage?.Summary ?? "Placed This Session") : "No Shelf Until Apply",
            "Move Shelf", "Move Preview · Apply or Cancel", now.ShelfPlaced ? "Move Shelf" : "Place Shelf",
            now.Room == null ? RoomUnread : null);

        private static SettingNow Paired(PairingNow? pairing)
        {
            const string Pair = "Pair Computer";
            const string Forget = "Forget Computer";
            const string AgainNeeded = "Pair Again to Reconnect";
            if (pairing == null) return new SettingNow("Not Ready", PairingNotReady, Pair, PairingNotReady, Pair, PairingNotReady);
            switch (pairing.Step)
            {
                case PairingStep.Typing:
                case PairingStep.Pairing:
                    return new SettingNow("Pairing", "Pairing · Computer", Pair, "After Pairing", Pair, "Pairing Under Way");
                case PairingStep.Forgetting:
                    return new SettingNow("Forgetting", "Forgetting · Computer", Forget, "After Forgetting", Forget, "Forgetting Under Way");
            }
            if (pairing.Address == null)
            {
                return new SettingNow("Not Paired", "Headset Not Paired", Pair,
                    "Type Address and 8-Digit Code from pnpm pair", Pair,
                    pairing.Keyboard ? null : "Needs Headset Keyboard");
            }
            // Forgetting asks first, Settings' own Yes; the address is the person's typing, shown as data.
            return new SettingNow("Paired", "Paired · " + pairing.Address, Forget, AgainNeeded, Forget,
                confirm: "Confirm", valueIsData: true, about: pairing.Address);
        }
    }
}
