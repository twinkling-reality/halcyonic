#nullable enable
using System;
using System.Globalization;

namespace Halcyonic.Client
{
    /// <summary>What the headset answered when asked to hide or show its boundary.</summary>
    public enum BoundaryAnswer
    {
        Done,

        /// <summary>The headset keeps its boundary on now, as it may while it judges that necessary.</summary>
        NotAllowed,

        /// <summary>This headset, runtime or build can't change the boundary.</summary>
        Unsupported,

        Failed,
    }

    /// <summary>The boundary as Settings shows it: never hidden until the headset has said so.</summary>
    public enum BoundaryStep
    {
        /// <summary>Halcyonic is not asking to hide it.</summary>
        Off,

        /// <summary>Asked to hide it; the headset hasn't said it did.</summary>
        Asked,

        /// <summary>The headset said it hid it.</summary>
        Hidden,

        /// <summary>The headset kept it on when asked; Halcyonic asks again while the room shows.</summary>
        KeptOn,
    }

    /// <summary>The boundary's row in Settings, read each time it is drawn.</summary>
    public sealed class BoundaryNow
    {
        /// <param name="roomShows">Passthrough is running, the only time the boundary can be hidden.</param>
        public BoundaryNow(BoundaryStep step, bool roomShows)
        {
            Step = step;
            RoomShows = roomShows;
        }

        public BoundaryStep Step { get; }

        public bool RoomShows { get; }
    }

    /// <summary>The headset's side of passthrough and its boundary, which the room's view gives.</summary>
    public interface IPassthroughDevice
    {
        /// <summary>Why passthrough can't start, or null where it can.</summary>
        string? CannotStart();

        /// <summary>Turns passthrough on and shows its layer.</summary>
        void TurnOn();

        /// <summary>Turns passthrough off and hides its layer.</summary>
        void TurnOff();

        bool Initialized { get; }

        bool InitFailed { get; }

        bool LayerShown { get; }

        /// <summary>Asks the headset, directly and now, to hide or show its boundary, and keeps its own retry in step.</summary>
        BoundaryAnswer AskBoundary(bool hidden);
    }

    /// <summary>
    /// The person's real room behind the scene: passthrough's start, its failures and its stop, and the
    /// boundary hidden only while the room shows. Unavailable when the headset does not support it, it
    /// fails to start, or it has not started within <see cref="StartSeconds"/>.
    /// </summary>
    public sealed class RoomPassthrough
    {
        public const float StartSeconds = 6f;

        /// <summary>Said once a session, the first time the headset reports the boundary hidden.</summary>
        public const string HiddenNotice = "Your boundary is hidden while your room shows. Settings, Your space brings it back.";

        private readonly IPassthroughDevice device;
        private readonly Action<string> log;
        private readonly bool boundaryOffered;
        private float startedAt;
        private bool focused = true;
        private bool paused;
        private bool quitting;

        /// <summary>Halcyonic asks for the boundary hidden now, with OVRManager's flag set to match.</summary>
        private bool asked;

        /// <summary>The headset answered the ask with anything but done.</summary>
        private bool refused;

        /// <summary>The headset's last report: hidden, shown, or not reported yet.</summary>
        private bool? heardHidden;

        private bool noticeGiven;

        /// <param name="log">One line for the log per change, with no prefix.</param>
        /// <param name="boundaryOffered">The build may hide the boundary: a development build.</param>
        public RoomPassthrough(IPassthroughDevice device, Action<string> log, bool boundaryOffered)
        {
            this.device = device;
            this.log = log;
            this.boundaryOffered = boundaryOffered;
        }

        public PassthroughState State { get; private set; } = PassthroughState.Off;

        /// <summary>Why passthrough is unavailable, for the log.</summary>
        public string? Problem { get; private set; }

        /// <summary>The boundary's row, or null where the build doesn't offer it.</summary>
        public BoundaryNow? Boundary => boundaryOffered ? new BoundaryNow(Step, State == PassthroughState.Running) : null;

        /// <summary>The person chose this session to hide the boundary while the room shows.</summary>
        public bool BoundaryChosen { get; private set; }

        // What Halcyonic asks leads, so the row's change always matches the toggle; Hidden needs the headset's report.
        private BoundaryStep Step =>
            asked && heardHidden == true ? BoundaryStep.Hidden
            : asked && refused ? BoundaryStep.KeptOn
            : asked ? BoundaryStep.Asked
            : BoundaryStep.Off;

        /// <summary>Starts passthrough, unless it is unavailable.</summary>
        public void Start(float now)
        {
            if (State is PassthroughState.Starting or PassthroughState.Running or PassthroughState.Unavailable) return;
            if (device.CannotStart() is string problem)
            {
                MarkUnavailable(problem);
                return;
            }
            device.TurnOn();
            startedAt = now;
            State = PassthroughState.Starting;
        }

        /// <summary>Stops passthrough, as for the virtual space or the scene's teardown.</summary>
        public void Stop()
        {
            if (State is PassthroughState.Off or PassthroughState.Unavailable) return;
            BoundaryChosen = false;
            ShowBoundary("passthrough stops");
            device.TurnOff();
            State = PassthroughState.Off;
        }

        /// <summary>Follows passthrough's start once a frame; returns the state.</summary>
        public PassthroughState Poll(float now)
        {
            switch (State)
            {
                case PassthroughState.Starting when device.Initialized:
                    State = PassthroughState.Running;
                    break;
                case PassthroughState.Starting when device.InitFailed:
                    MarkUnavailable("it failed to start");
                    break;
                case PassthroughState.Starting when now - startedAt > StartSeconds:
                    MarkUnavailable("it did not start within " + StartSeconds.ToString(CultureInfo.InvariantCulture) + " seconds");
                    break;
                case PassthroughState.Running when device.InitFailed:
                    MarkUnavailable("it stopped working");
                    break;
            }
            FollowBoundary();
            return State;
        }

        /// <summary>The app pauses or resumes; paused, the boundary is asked back at once.</summary>
        public void Paused(bool paused)
        {
            this.paused = paused;
            if (paused) ShowBoundary("Halcyonic pauses");
        }

        /// <summary>The app gains or loses input focus; losing it, the boundary is asked back at once.</summary>
        public void Focused(bool focused)
        {
            this.focused = focused;
            if (!focused) ShowBoundary("Halcyonic lost focus");
        }

        /// <summary>The app quits: the boundary is asked back, and never asked hidden again.</summary>
        public void Quitting()
        {
            quitting = true;
            ShowBoundary("Halcyonic quits");
        }

        /// <summary>The person's choice in Settings, for this session; asked at once where everything holds.</summary>
        public void ChooseBoundaryHidden(bool hidden)
        {
            if (!boundaryOffered) return;
            BoundaryChosen = hidden;
            if (hidden) FollowBoundary();
            else ShowBoundary("you chose Show boundary");
        }

        /// <summary>
        /// The headset reported its boundary; true the first time this session it says hidden, for the
        /// workspace's notice. Hidden while Halcyonic isn't asking, it is asked back.
        /// </summary>
        public bool HeardBoundary(bool hidden)
        {
            heardHidden = hidden;
            log("boundary: the headset says its boundary is " + (hidden ? "hidden" : "shown"));
            if (!hidden) return false;
            if (!asked)
            {
                ShowBoundary("the headset hid it while Halcyonic wasn't asking");
                return false;
            }
            refused = false;
            if (noticeGiven) return false;
            noticeGiven = true;
            return true;
        }

        /// <summary>Asks for the boundary hidden once everything holds, and back as soon as anything doesn't.</summary>
        private void FollowBoundary()
        {
            var holds = boundaryOffered && BoundaryChosen && !quitting && !paused && focused
                && State == PassthroughState.Running && device.LayerShown;
            if (holds && !asked)
            {
                asked = true;
                var answer = device.AskBoundary(true);
                refused = answer != BoundaryAnswer.Done;
                log("boundary: asked the headset to hide its boundary while the room shows; answer " + answer);
            }
            else if (!holds && asked)
            {
                ShowBoundary("the room no longer qualifies");
            }
        }

        /// <summary>Asks the headset directly for its boundary, before anything turns passthrough off.</summary>
        private void ShowBoundary(string why)
        {
            if (!asked && heardHidden != true) return;
            asked = false;
            refused = false;
            var answer = device.AskBoundary(false);
            log("boundary: asked the headset to show its boundary, because " + why + "; answer " + answer);
        }

        private void MarkUnavailable(string problem)
        {
            BoundaryChosen = false;
            ShowBoundary("passthrough is unavailable: " + problem);
            device.TurnOff();
            Problem = problem;
            State = PassthroughState.Unavailable;
        }
    }
}
