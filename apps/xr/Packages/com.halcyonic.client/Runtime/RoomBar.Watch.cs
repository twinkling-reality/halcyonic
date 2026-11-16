#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>The bar's own action that opens and closes Watch It Work on a working task (SCREENS's SHAPE 11 W1; the lead's 03:03Z unit).</summary>
        public const string Watch = "room-watch";

        /// <summary>The button's words while the view is closed (AUDIT2's 03:07Z words).</summary>
        public const string WatchWords = "Watch It Work";

        /// <summary>
        /// The same control's words while the view is open: one constant, as the open word stands between the lead's 03:06Z "Close View"
        /// and AUDIT2's 03:07Z "Done Watching" (XR's 03:09Z note); never with Stop in it, beside the task's own Stop.
        /// </summary>
        public const string WatchDoneWords = "Done Watching";

        /// <summary>
        /// The task's bar at work with Watch It Work, where the model offers it (live work, its folder observed): the column is Watch It Work
        /// (the one accent: on a working task it is the one thing to do besides waiting) then the bar's own column (Stop, plain), and with the
        /// view open the same control in the same place, its words <see cref="WatchDoneWords"/>. Any other bar as it is.
        /// </summary>
        public static RoomBar WithWatch(KitFileBinding binding, RoomBar bar, bool open)
        {
            if (binding.Plain || !bar.Working || !binding.Model.Watch) return bar;
            var words = open ? WatchDoneWords : WatchWords;
            var watch = new RoomAction(new KitControl(KitPlace.Step, binding.Frame, Watch, null, words, GlazeIcon.Next, true, null, false),
                main: true, icon: GlazeIcon.Next);
            var column = new List<RoomAction> { watch };
            column.AddRange(bar.Column.Select(action => action.Main ? new RoomAction(action.Control, main: false, action.Words, icon: action.Icon) : action));
            bar.Column = column;
            return bar;
        }
    }
}
