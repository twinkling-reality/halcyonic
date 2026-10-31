#nullable enable
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>What surrounds the person: their real room, seen through passthrough, or a virtual space.</summary>
    public enum RoomSpace
    {
        Room,
        Virtual,
    }

    public enum PassthroughState
    {
        Off,
        Starting,
        Running,

        /// <summary>The headset does not support it, or it failed to start.</summary>
        Unavailable,
    }

    /// <summary>What the app knows of the person's room.</summary>
    public enum RoomScan
    {
        /// <summary>Not read yet, or not read because the virtual space is shown.</summary>
        NotRead,

        /// <summary>Asking the person for access to the room's layout (spatial data).</summary>
        AskingAccess,

        Reading,

        /// <summary>The person did not allow access to the room's layout.</summary>
        NoAccess,

        /// <summary>Asked again and still not allowed; only the headset's settings can allow it now.</summary>
        AccessOff,

        /// <summary>No room is set up on this headset.</summary>
        NotSetUp,

        /// <summary>Rooms are set up, but the person is in none of them.</summary>
        OutsideRooms,

        /// <summary>The layout could not be read for another reason, such as no support for it.</summary>
        Unavailable,

        Read,
    }

    /// <summary>Where the stage stands.</summary>
    public enum StagePlacement
    {
        /// <summary>In front of the person, the stage's own placement.</summary>
        InFront,

        /// <summary>Looking for a surface; the stage stands in front of the person meanwhile.</summary>
        Searching,

        /// <summary>On a surface chosen now, kept with a spatial anchor for the next session.</summary>
        OnSurface,

        /// <summary>On a surface chosen now, which could not be kept for the next session.</summary>
        OnSurfaceThisSession,

        /// <summary>On a surface kept from an earlier session.</summary>
        BackOnSurface,

        /// <summary>The room has no surface that fits the lineup in comfortable reach and view.</summary>
        NoSurface,

        /// <summary>The surface's anchor is not tracked for now; the stage stands in front of the person until it is.</summary>
        LostSurface,

        /// <summary>A shelf explicitly positioned by the person, without an asserted real surface or durable anchor.</summary>
        OnShelfThisSession,
    }

    /// <summary>Something the person can do to put the characters on their desk, offered and never forced.</summary>
    public enum RoomOffer
    {
        None,

        /// <summary>Ask again for access to the room's layout.</summary>
        AllowRoomAccess,

        /// <summary>Run the headset's space setup, which captures the room and its furniture.</summary>
        SetUpRoom,
    }

    /// <summary>
    /// The room placement's state, and what follows from it: which space is shown, what is offered,
    /// and the words for the person. Every state that leaves the stage in front of the person says
    /// so and why, in one short line.
    /// </summary>
    public sealed class RoomStatus
    {
        public static readonly RoomStatus Initial =
            new RoomStatus(RoomSpace.Room, PassthroughState.Off, RoomScan.NotRead, StagePlacement.InFront, null);

        public RoomStatus(RoomSpace preferred, PassthroughState passthrough, RoomScan scan, StagePlacement placement, SurfaceKind? surface)
        {
            Preferred = preferred;
            Passthrough = passthrough;
            Scan = scan;
            Placement = placement;
            Surface = surface;
        }

        /// <summary>The space the person chose; the real room unless they chose otherwise.</summary>
        public RoomSpace Preferred { get; }

        public PassthroughState Passthrough { get; }

        public RoomScan Scan { get; }

        public StagePlacement Placement { get; }

        /// <summary>The kind of surface the stage stands on, when known.</summary>
        public SurfaceKind? Surface { get; }

        /// <summary>The real room where passthrough works and the person has not chosen the virtual space; otherwise the virtual space.</summary>
        public RoomSpace Shown =>
            Preferred == RoomSpace.Room && Passthrough != PassthroughState.Unavailable ? RoomSpace.Room : RoomSpace.Virtual;

        /// <summary>Whether the person can switch between the spaces: not while passthrough is unavailable.</summary>
        public bool CanSwitch => Passthrough != PassthroughState.Unavailable;

        /// <summary>Whether a placement on a real surface is in use.</summary>
        public bool OnSurface =>
            Shown == RoomSpace.Room && Placement is StagePlacement.OnSurface or StagePlacement.OnSurfaceThisSession or StagePlacement.BackOnSurface;

        /// <summary>The switch's label, which says what pressing it does.</summary>
        public string SwitchLabel => Shown == RoomSpace.Room ? "Show Virtual Space" : "Show My Room";

        /// <summary>What is offered, given whether the headset can run its space setup from the app.</summary>
        public RoomOffer Offer(bool canSetUpRoom)
        {
            if (Shown != RoomSpace.Room || OnSurface) return RoomOffer.None;
            if (Scan == RoomScan.NoAccess) return RoomOffer.AllowRoomAccess;
            if (!canSetUpRoom) return RoomOffer.None;
            if (Scan is RoomScan.NotSetUp or RoomScan.OutsideRooms) return RoomOffer.SetUpRoom;
            if (Scan == RoomScan.Read && Placement == StagePlacement.NoSurface) return RoomOffer.SetUpRoom;
            return RoomOffer.None;
        }

        public static string OfferLabel(RoomOffer offer) => offer switch
        {
            RoomOffer.AllowRoomAccess => "Allow Room Access",
            RoomOffer.SetUpRoom => "Set Up Room",
            _ => "",
        };

        /// <summary>One short line for the person: where their agents are, and why.</summary>
        public string Line
        {
            get
            {
                if (Shown == RoomSpace.Virtual)
                {
                    return Passthrough == PassthroughState.Unavailable
                        ? "Passthrough Unavailable · Virtual Space"
                        : "Virtual Space";
                }
                var desk = Surface == SurfaceKind.Desk;
                if (Placement == StagePlacement.OnShelfThisSession) return "Shelf Stays This Session";
                switch (Placement)
                {
                    case StagePlacement.OnSurface:
                        return desk ? "Agents on Your Desk" : "Agents on Nearby Surface";
                    case StagePlacement.OnSurfaceThisSession:
                        return desk ? "Agents on Desk · This Session" : "Agents Nearby · This Session";
                    case StagePlacement.BackOnSurface:
                        return desk ? "Agents Back on Desk" : "Agents Back Where Left";
                    case StagePlacement.LostSurface:
                        return "Placement Lost · Agents Tucked Away";
                }
                switch (Scan)
                {
                    case RoomScan.AskingAccess:
                        return "Allow Room Access · Agents Can Use Desk";
                    case RoomScan.NoAccess:
                        return "Room Access Unavailable · Place Shelf";
                    case RoomScan.AccessOff:
                        return "Room Access Off · Allow in Headset Settings";
                    case RoomScan.NotSetUp:
                        return "Room Not Set Up · Place Shelf";
                    case RoomScan.OutsideRooms:
                        return "Outside Set-Up Rooms · Place Shelf";
                    case RoomScan.Unavailable:
                        return "Room Layout Unavailable · Place Shelf";
                }
                return Placement == StagePlacement.NoSurface
                    ? "No Free Desk in Reach · Place Shelf"
                    : "Agents Off · Place Shelf";
            }
        }

        public RoomStatus With(
            RoomSpace? preferred = null,
            PassthroughState? passthrough = null,
            RoomScan? scan = null,
            StagePlacement? placement = null,
            SurfaceKind? surface = null,
            bool clearSurface = false) =>
            new RoomStatus(
                preferred ?? Preferred,
                passthrough ?? Passthrough,
                scan ?? Scan,
                placement ?? Placement,
                clearSurface ? null : surface ?? Surface);

        /// <summary>Every line and label this type can show, for checks on the app's words.</summary>
        public static IEnumerable<string> AllWords()
        {
            foreach (RoomSpace preferred in System.Enum.GetValues(typeof(RoomSpace)))
            foreach (PassthroughState passthrough in System.Enum.GetValues(typeof(PassthroughState)))
            foreach (RoomScan scan in System.Enum.GetValues(typeof(RoomScan)))
            foreach (StagePlacement placement in System.Enum.GetValues(typeof(StagePlacement)))
            foreach (var surface in new SurfaceKind?[] { null, SurfaceKind.Desk, SurfaceKind.Other })
            {
                var status = new RoomStatus(preferred, passthrough, scan, placement, surface);
                yield return status.Line;
                yield return status.SwitchLabel;
            }
            foreach (RoomOffer offer in System.Enum.GetValues(typeof(RoomOffer)))
            {
                if (offer != RoomOffer.None) yield return OfferLabel(offer);
            }
        }

        public override string ToString() => $"{Shown} ({Preferred} preferred), passthrough {Passthrough}, room {Scan}, stage {Placement}";
    }
}
