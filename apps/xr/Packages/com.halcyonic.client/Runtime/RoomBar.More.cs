#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>The bar's own action that opens the task's More page (SCREENS's SHAPE 11 R1; the lead's 03:06Z yes).</summary>
        public const string More = "room-more";

        /// <summary>The More page's own way back to the task (R3).</summary>
        public const string MoreBack = "room-more-back";

        /// <summary>The action's words: the system panels' own word for the control behind which secondary actions stand (AUDIT2's 03:06Z).</summary>
        public const string MoreWords = "More";

        /// <summary>The More page's way back (N2: Back goes up one level).</summary>
        public const string BackWords = "Back";

        /// <summary>
        /// The task's routes the redesign offers behind More, in the frame's own order and words, each available: Show Task Here, Ask
        /// Workspace, Character Colour, Show Screen and the rest the file offers (KitFileBinding.Routes). The results line is not a route
        /// here: the result is the moment itself (AUDIT's 03:01Z).
        /// </summary>
        public static IReadOnlyList<KitControl> MoreRoutes(KitFileBinding binding) =>
            binding.Plain ? new KitControl[0] : binding.Routes.Where(route => route.Available && route.Action != FileScreens.OpenResults).ToList();

        /// <summary>
        /// The task's bar at rest with More beside its other actions, before the way back, where the file offers a route and the bar is
        /// the plain row (no pair, not at work, nothing asked); any other bar as it is.
        /// </summary>
        public static RoomBar WithMore(KitFileBinding binding, RoomBar bar)
        {
            if (bar.State != RoomBarState.Rest || bar.Pair || bar.Working || MoreRoutes(binding).Count == 0) return bar;
            var more = new RoomAction(new KitControl(KitPlace.Step, binding.Frame, More, null, MoreWords, GlazeIcon.OpensMore, true, null, false),
                main: false, icon: GlazeIcon.OpensMore);
            var row = bar.Row.ToList();
            var back = row.FindIndex(action => action.Control.Place is KitPlace.Close or KitPlace.Cancel);
            row.Insert(back < 0 ? row.Count : back, more);
            bar.Row = row;
            return bar;
        }

        /// <summary>
        /// The task's More page in the panel's place (R2): its title the task's name, a row per route the file offers, each opening its
        /// page (the chevron), then Back to the task. Null where the file offers none: the page never stands empty.
        /// </summary>
        public static RoomBar? MorePage(KitFileBinding binding)
        {
            var routes = MoreRoutes(binding);
            if (routes.Count == 0) return null;
            var column = routes.Select(route => new RoomAction(route, main: false, icon: GlazeIcon.Next)).ToList();
            column.Add(new RoomAction(new KitControl(KitPlace.Back, binding.Frame, MoreBack, null, BackWords, GlazeIcon.Back, true, null, false),
                main: false, icon: GlazeIcon.Back));
            return new RoomBar(RoomBarState.Page, body: LineWords.TheirName(binding.Model.TaskTitle), column: column);
        }
    }
}
