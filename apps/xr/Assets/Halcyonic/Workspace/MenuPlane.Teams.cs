#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The open project screens that are not the one brought forward (the lead's 2026-11-17T18:55Z (d); FIELD's 19:13Z places sheet):
    /// each stands over its own team, <see cref="RoomLayout.TeamScreenDegrees"/> across, still, a window of its own standing in the room
    /// as visionOS stands its windows (R04, the lead's held film/design/references/visionos-window-example.png). Where each stands is the
    /// places' legibility arithmetic (RoomLayout.Screens), not a style. Pressing one brings it forward as the work.
    /// </summary>
    public sealed partial class MenuPlane
    {
        private Transform? teams;
        private readonly Dictionary<string, (KitScreen Screen, KitPress Press)> teamScreens = new Dictionary<string, (KitScreen, KitPress)>(StringComparer.Ordinal);
        private readonly List<string> teamsGone = new List<string>();

        /// <summary>Raised when the person presses a team's screen, with its project: the director brings it forward.</summary>
        public event Action<string>? TeamScreenPressed;

        /// <summary>The team screens standing now, by project, with where each stands.</summary>
        public IReadOnlyDictionary<string, RoomPose> TeamScreenPoses => teamPoses;

        private readonly Dictionary<string, RoomPose> teamPoses = new Dictionary<string, RoomPose>(StringComparer.Ordinal);

        /// <summary>The team screen standing for <paramref name="projectId"/>, if one does.</summary>
        public KitScreen? TeamScreen(string projectId) => teamScreens.TryGetValue(projectId, out var made) ? made.Screen : null;

        /// <summary>
        /// Stands <paramref name="screens"/>, each on its bearing at its distance from <paramref name="eyes"/>; takes away any not given.
        /// </summary>
        /// <summary>The count of screens waiting off the page (AUDIT3's TS2), under the outermost team screen; null while none waits.</summary>
        public TMPro.TextMeshPro? TeamScreensWaiting => waitingWords != null && waitingWords.gameObject.activeSelf ? waitingWords : null;

        private TMPro.TextMeshPro? waitingWords;

        public void ShowTeamScreens(IReadOnlyList<(string ProjectId, KitScreenModel Model, float Bearing, float Distance)> screens, Vector3 eyes, int waiting = 0)
        {
            if (screens.Count == 0)
            {
                HideTeamScreens();
                return;
            }
            if (teams == null)
            {
                teams = new GameObject("Team screens").transform;
                teams.SetParent(transform, false);
            }
            if (!teams.gameObject.activeSelf) teams.gameObject.SetActive(true);
            var at = new System.Numerics.Vector3(eyes.x, eyes.y, eyes.z);
            teamsGone.Clear();
            foreach (var id in teamScreens.Keys)
            {
                var kept = false;
                foreach (var screen in screens) kept |= screen.ProjectId == id;
                if (!kept) teamsGone.Add(id);
            }
            foreach (var id in teamsGone)
            {
                var gone = teamScreens[id].Screen.gameObject;
                teamScreens.Remove(id);
                teamPoses.Remove(id);
                gone.SetActive(false);
                if (Application.isPlaying) Destroy(gone);
                else DestroyImmediate(gone);
            }
            foreach (var (projectId, model, bearing, distance) in screens)
            {
                if (!teamScreens.TryGetValue(projectId, out var made))
                {
                    var screen = KitScreen.Create(teams);
                    screen.name = "Team screen";
                    var captured = projectId;
                    // Its press on a child of its own, square on the face, as the room's other presses stand on their hosts.
                    var pressHost = new GameObject("Bring forward");
                    pressHost.transform.SetParent(screen.transform, false);
                    var press = KitPress.Create(pressHost, Vector2.one);
                    press.Pressed += () => TeamScreenPressed?.Invoke(captured);
                    made = (screen, press);
                    teamScreens[projectId] = made;
                }
                // Stood before it draws: the kit bends a surface's mesh about the eyes from where it stands as it draws.
                var pose = RoomLayout.TeamScreen(at, bearing, distance, SpatialKit.ScreenAspect);
                Stand(made.Screen.transform, pose, Reach(pose, eyes));
                made.Screen.Show(model, RoomLayout.TeamScreenDegrees);
                made.Press.Resize(made.Screen.Size);
                teamPoses[projectId] = pose;
            }
            // Screens past what stands at once (RoomLayout.ScreensPaged) are named by their count, never hidden silently: "{n} More", the
            // places' own words (AUDIT2's), under the outermost screen.
            var outermost = screens[screens.Count - 1].ProjectId;
            if (waiting > 0 && teamScreens.TryGetValue(outermost, out var last))
            {
                if (waitingWords == null)
                {
                    waitingWords = KitType.Create(teams, "Screens waiting", KitRole.Label, KitType.Secondary, KitType.Order.Words, TMPro.TextAlignmentOptions.Center, room: true);
                    waitingWords.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
                }
                waitingWords.transform.SetParent(last.Screen.transform, false);
                KitType.Size(waitingWords, KitRole.Label);
                GlazeText.SetLiteral(waitingWords, waiting.ToString(System.Globalization.CultureInfo.InvariantCulture) + " More");
                waitingWords.rectTransform.pivot = new Vector2(0.5f, 1f);
                waitingWords.transform.localPosition = new Vector3(0f, -last.Screen.Size.y / 2f - KitType.Units(1.5f), -0.0006f);
                waitingWords.gameObject.SetActive(true);
            }
            else if (waitingWords != null) waitingWords.gameObject.SetActive(false);
        }

        /// <summary>Takes every team screen away.</summary>
        public void HideTeamScreens()
        {
            if (teams == null) return;
            foreach (var made in teamScreens.Values)
            {
                if (made.Screen == null) continue;
                if (Application.isPlaying) Destroy(made.Screen.gameObject);
                else DestroyImmediate(made.Screen.gameObject);
            }
            teamScreens.Clear();
            teamPoses.Clear();
            teams.gameObject.SetActive(false);
        }
    }
}
