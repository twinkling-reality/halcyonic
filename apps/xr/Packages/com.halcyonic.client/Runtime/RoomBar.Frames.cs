#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// A reader of a frame the task's binding does not bind (a page of the file with no place of its own, or the frame of
    /// another column that stands beside the menu, as New Project's steps): it answers with the redesign's view of the
    /// frame, or null where the frame is not its. Every action it shows is a control made from the frame's own line or
    /// prompt, so a press raises the frame's own action and nothing else.
    /// </summary>
    public delegate RoomBar? RoomFrameMoment(MenuFrame frame, VoiceStage voice);

    public sealed partial class RoomBar
    {
        /// <summary>
        /// The readers, asked in this order; the first that answers draws the frame. A reader lives in a file of its own
        /// (a partial of this class holding one static function) and has one line here, above the general reader, which
        /// stays last and takes every frame the others leave (<see cref="General"/>).
        /// </summary>
        private static readonly RoomFrameMoment[] Frames =
        {
            Agent,
            NewProject,
            Projects,
            Folders,
            Tasks,
            Menu,
            Pairing,
            Carded,
            Ask,
            General,
        };

        /// <summary>
        /// The redesign's view of <paramref name="frame"/>, a frame the task's binding does not bind. The general reader, last,
        /// takes any frame the others leave, so this is null only for a frame with nothing in it to draw at all, which it never
        /// is. A frame with a side panel goes to the general reader alone: no specific reader draws a side panel, and one that
        /// took the frame would leave the panel the person opened unseen (AUDIT's D, 2026-11-13T16:47Z).
        /// </summary>
        public static RoomBar? OfFrame(MenuFrame frame, VoiceStage voice)
        {
            if (frame.Side != null)
            {
                var side = General(frame, voice);
                if (side != null) side.FromMoment = true;
                return side;
            }
            foreach (var reader in Frames)
                if (reader(frame, voice) is RoomBar own)
                {
                    own.FromMoment = true;
                    return own;
                }
            return null;
        }
    }
}
