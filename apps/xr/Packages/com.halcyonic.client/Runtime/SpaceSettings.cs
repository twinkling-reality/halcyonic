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
        public SpaceNow(RoomStatus? room, RoomOffer offer, StageArrangement? arrangement, PairingNow? pairing)
        {
            Room = room;
            Offer = offer;
            Arrangement = arrangement;
            Pairing = pairing;
        }

        public RoomStatus? Room { get; }

        public RoomOffer Offer { get; }

        public StageArrangement? Arrangement { get; }

        public PairingNow? Pairing { get; }
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

        public const string NextArrangement = "arrangement-next";

        public const string ResetPosition = "reset-position";

        public const string Pairing = "pairing";

        /// <summary>Why the room's rows take no change while no room is given (proposed by lane W, 2026-10-03).</summary>
        public const string RoomUnread = "Your room can't be read now.";

        /// <summary>Why Pairing takes no change while the pairing isn't there yet, or has gone.</summary>
        public const string PairingNotReady = "Pairing isn't ready yet.";

        /// <param name="act">Raised with a row's id when its change is pressed; for Pairing, only to pair, unpaired.</param>
        /// <param name="forget">
        /// Raised with the paired computer's address only by Settings' Yes to forgetting it, given the
        /// address as Settings armed it, and only while that is still the computer paired with nothing
        /// under way (<see cref="PairingNow.Forgets"/>). A bare change of the row never forgets.
        /// </param>
        /// <param name="pairing">The build offers pairing, a development build: Your computer's row shows, read as the pairing stands.</param>
        public static IReadOnlyList<MenuSetting> Of(Func<SpaceNow> now, Action<string> act, Action<string> forget, bool pairing = false)
        {
            // Every row stands whatever is there now: each reads afresh, so a room or a pairing that
            // comes or goes while Settings shows changes what a row says, never which rows there are.
            var rows = new List<MenuSetting>
            {
                new MenuSetting(SwitchSpace, SettingsText.YourSpace, "Around you", () => Space(now().Room), () => act(SwitchSpace)),
                new MenuSetting(TakeOffer, SettingsText.YourSpace, "Your room's layout", () => Offer(now()), () => act(TakeOffer)),
                new MenuSetting(NextArrangement, SettingsText.YourSpace, "The characters", () => Arranged(now()), () => act(NextArrangement)),
                new MenuSetting(ResetPosition, SettingsText.YourSpace, "The menu", () => new SettingNow("Where it stands", "Where it stands now",
                    "In front of you", "The menu comes back in front of you, within reach", "Reset position"), () => act(ResetPosition)),
            };
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

        /// <summary>The arrangement after <paramref name="arrangement"/>, in turn: in front, room for a window, either side of it.</summary>
        public static StageArrangement Next(StageArrangement arrangement) => arrangement switch
        {
            StageArrangement.InFront => StageArrangement.TurnedAside,
            StageArrangement.TurnedAside => StageArrangement.BesideAWindow,
            _ => StageArrangement.InFront,
        };

        private static SettingNow Space(RoomStatus? room)
        {
            if (room == null) return new SettingNow("Not known", RoomUnread, "Your room", RoomUnread, "Show my room", RoomUnread);
            var reason = room.CanSwitch ? null : "This headset can't show your room now.";
            return room.Shown == RoomSpace.Room
                ? new SettingNow("Your room", room.Line, "A virtual space", "The characters stand in a virtual space instead of your room", room.SwitchLabel, reason)
                : new SettingNow("Virtual space", room.Line, "Your room", "The characters stand in your room, seen through the headset", room.SwitchLabel, reason);
        }

        private static SettingNow Offer(SpaceNow now) => now.Room is not RoomStatus room
            ? new SettingNow("Not known", RoomUnread, "Nothing to set up", RoomUnread, "Set up this room", RoomUnread)
            : now.Offer switch
            {
                RoomOffer.AllowRoomAccess => new SettingNow("No access", room.Line, RoomStatus.OfferLabel(now.Offer),
                    "The headset asks again to read your room's layout, so the characters can stand on your desk", RoomStatus.OfferLabel(now.Offer)),
                RoomOffer.SetUpRoom => new SettingNow("Not set up", room.Line, RoomStatus.OfferLabel(now.Offer),
                    "The headset's space setup captures your room and its furniture", RoomStatus.OfferLabel(now.Offer)),
                _ => new SettingNow("Ready", room.Line, "Nothing to set up", "Your room needs nothing more", "Set up this room",
                    "Your room needs nothing set up now."),
            };

        private static SettingNow Arranged(SpaceNow now)
        {
            if (now.Room == null)
            {
                return new SettingNow("Not known", RoomUnread, "Nothing to change", RoomUnread, SettingsText.ChangeTo(StageArrangement.TurnedAside), RoomUnread);
            }
            if (now.Arrangement is not StageArrangement standing)
            {
                return new SettingNow("On your desk", SettingsText.ArrangedByRoom, "Nothing to change", SettingsText.ArrangedByRoom,
                    SettingsText.ChangeTo(StageArrangement.TurnedAside), SettingsText.ArrangedByRoom);
            }
            var next = Next(standing);
            return new SettingNow(Standing(standing), SettingsText.Arrangement(standing), SettingsText.ChangeTo(next),
                SettingsText.Arrangement(next), SettingsText.ChangeTo(next));
        }

        private static string Standing(StageArrangement arrangement) => arrangement switch
        {
            StageArrangement.TurnedAside => "Room for a window",
            StageArrangement.BesideAWindow => "Either side of a window",
            _ => "In front of you",
        };

        private static SettingNow Paired(PairingNow? pairing)
        {
            const string Pair = "Pair with a " + HostText.Noun;
            const string Forget = "Forget this " + HostText.Noun;
            const string AgainNeeded = "This headset then needs pairing again to reach it";
            if (pairing == null) return new SettingNow("Not ready", PairingNotReady, Pair, PairingNotReady, Pair, PairingNotReady);
            switch (pairing.Step)
            {
                case PairingStep.Typing:
                case PairingStep.Pairing:
                    return new SettingNow("Pairing…", "Pairing with " + HostText.Your, Pair, "Offered again once pairing finishes", Pair, "Pairing is under way.");
                case PairingStep.Forgetting:
                    return new SettingNow("Forgetting…", "Forgetting " + HostText.Your, Forget, "Offered again once forgetting finishes", Forget, "Forgetting is under way.");
            }
            if (pairing.Address == null)
            {
                return new SettingNow("Not paired", "This headset isn't paired with " + HostText.Your, Pair,
                    "Type the address and the eight-digit code pnpm pair shows on " + HostText.Your, Pair,
                    pairing.Keyboard ? null : "Pairing needs the headset's system keyboard; pair from the headset.");
            }
            // Forgetting asks first, Settings' own Yes; the address is the person's typing, shown as data.
            return new SettingNow("Paired", "Paired with " + HostText.Your + " at " + pairing.Address, Forget, AgainNeeded, Forget,
                confirm: "Yes, forget this " + HostText.Noun, valueIsData: true, about: pairing.Address);
        }
    }
}
