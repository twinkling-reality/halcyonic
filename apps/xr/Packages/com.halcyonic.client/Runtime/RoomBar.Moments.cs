#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// A moment of the redesign outside the task's own loop: it reads the file's frame as bound, the voice as it stands and
    /// what the task's loop made of the frame, and answers with its own view, or null where the frame is not its. Every
    /// action it shows is a control of the frame's, so a press raises the frame's own action and nothing else.
    /// </summary>
    public delegate RoomBar? RoomMoment(KitFileBinding binding, VoiceStage voice, RoomBar tasks);

    public sealed partial class RoomBar
    {
        /// <summary>
        /// The moments, asked in this order; the first that answers draws the frame. A moment lives in a file of its own
        /// (RoomBar.Choose.cs is the pattern: a partial of this class holding one private static function) and has one
        /// line here.
        /// </summary>
        private static readonly RoomMoment[] Moments =
        {
            AgentAtWork,
            Choose,
            AgentAtRest,
        };
    }
}
