#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// The headset's web-preview relays, one per open project screen (the limits audit's #9, the lead's 2026-11-17 19:17Z; ADR
    /// 0052): several projects' live previews stand at once, each on its own loopback port. A relay whose project has a screen
    /// open lives until that screen closes; opening another never stops it. A Play with no screen open keeps the one-relay rule
    /// it always had: the next such Play takes its place, and it ends once Halora comes back to the front after the browser.
    /// Every relay ends when the computer changes and at quit. So never more run than the open screens
    /// (<see cref="WorkspacePresentationState.ScreensMost"/>) and one Play.
    /// </summary>
    public sealed class PreviewRelays
    {
        /// <summary>A relay started for a project: the loopback port its preview is served on, and what stops it.</summary>
        public readonly struct Relayed
        {
            public Relayed(int port, IDisposable stop)
            {
                Port = port;
                Stop = stop ?? throw new ArgumentNullException(nameof(stop));
            }

            public int Port { get; }

            public IDisposable Stop { get; }
        }

        private readonly Func<ControlPlaneTarget, string, Relayed> start;
        private readonly Dictionary<string, (Relayed Relay, bool Screen)> relays = new Dictionary<string, (Relayed, bool)>();
        private readonly List<string> order = new List<string>();
        private ControlPlaneTarget? target;
        private bool wentAway;

        /// <param name="start">Starts a relay of a project's preview from a computer; it throws where it cannot.</param>
        public PreviewRelays(Func<ControlPlaneTarget, string, Relayed> start) => this.start = start ?? throw new ArgumentNullException(nameof(start));

        /// <summary>The projects relayed now, oldest first.</summary>
        public IReadOnlyList<string> Projects => order;

        /// <summary>Whether any relay runs, so the process is held in the foreground.</summary>
        public bool Any => order.Count > 0;

        /// <summary>
        /// The loopback port serving <paramref name="projectId"/>'s preview from <paramref name="computer"/>, starting a relay where
        /// none serves it now; <paramref name="open"/> are the projects whose screens stand open. Null where it could not start.
        /// </summary>
        public int? PortFor(ControlPlaneTarget computer, string projectId, IReadOnlyCollection<string> open)
        {
            if (computer == null) throw new ArgumentNullException(nameof(computer));
            if (open == null) throw new ArgumentNullException(nameof(open));
            if (target != null && !target.SameAs(computer)) StopAll();
            var screen = open.Contains(projectId);
            if (relays.TryGetValue(projectId, out var serving))
            {
                // A Play's relay whose screen has since opened is that screen's from now on.
                if (screen && !serving.Screen) relays[projectId] = (serving.Relay, true);
                return serving.Relay.Port;
            }
            // The one-relay rule for a Play with no screen open: a new one takes the last one's place.
            if (!screen) foreach (var loose in order.Where(project => !relays[project].Screen).ToList()) StopOne(loose);
            Relayed started;
            try
            {
                started = start(computer, projectId);
            }
            catch (Exception)
            {
                return null;
            }
            target = computer;
            relays[projectId] = (started, screen);
            order.Add(projectId);
            wentAway = false;
            return started.Port;
        }

        /// <summary>The open screens changed: each screen's relay whose screen closed stops; a screenless Play's waits for the front.</summary>
        public void Keep(IReadOnlyCollection<string> open)
        {
            if (open == null) throw new ArgumentNullException(nameof(open));
            foreach (var project in order.Where(project => relays[project].Screen && !open.Contains(project)).ToList()) StopOne(project);
        }

        /// <summary>
        /// Halora went to the background (<paramref name="paused"/>, as the browser comes to the front) or came back: back in front
        /// after being away, a Play with no screen open is left by the browser and stops; an open screen's preview stays live.
        /// </summary>
        public void Paused(bool paused, IReadOnlyCollection<string> open)
        {
            if (open == null) throw new ArgumentNullException(nameof(open));
            if (!Any) return;
            if (paused)
            {
                wentAway = true;
                return;
            }
            if (!wentAway) return;
            wentAway = false;
            foreach (var project in order.Where(project => !open.Contains(project)).ToList()) StopOne(project);
        }

        /// <summary>Stops every relay, as when the computer changes or the app quits.</summary>
        public void StopAll()
        {
            foreach (var project in order.ToList()) StopOne(project);
            target = null;
            wentAway = false;
        }

        private void StopOne(string project)
        {
            if (!relays.TryGetValue(project, out var relayed)) return;
            relays.Remove(project);
            order.Remove(project);
            relayed.Relay.Stop.Dispose();
        }
    }
}
