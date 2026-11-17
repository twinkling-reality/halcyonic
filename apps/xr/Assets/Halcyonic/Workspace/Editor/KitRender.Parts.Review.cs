#nullable enable
using System;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The finished turn's review (TASKS, FIELD's tile f6) laid by the host as the kit check's 38-result-review lays it, so the
    /// parts run measures the review's own panel width into the envelope at both text sizes (XR 03:4xZ: the record is measured,
    /// never typed). Held here: it stands as one dialog, ReviewDialogDp wide, every action inside its panel. Synthetic data.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ReviewStates = { "38-review-dialog" };

        private static PartShot ReviewShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var was = RoomDesign.Enabled;
            RoomDesign.Enabled = true;
            try
            {
                var plane = new GameObject("Plane").transform;
                plane.SetParent(root, false);
                var host = KitWorkspaceHost.Create(plane);
                host.Accepting = () => true;
                host.Voice = () => VoiceStage.Idle;
                host.AwaitingStart = () => false;
                host.MicLevel = () => null;
                var factory = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var steering = new WorkspaceSteering(factory, () => DateTimeOffset.UtcNow);
                var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
                var workspace = WithActivity(SelectedWorkspace(waiting: false, status: ExecutionStatus.Completed),
                    new[] { new ActivityEntry(1, Day + "09:04:00.000Z", ActivityKind.Message, ResultSays, true) });
                var binding = KitFileBinding.Of(FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4)), steering.Armed, workspace, "",
                    new ConnectionStatus(ConnectionPhase.Live), SelectedProject, ProductionSpine(workspace),
                    new KitRowModel("workspace", LineWords.Ours("Move Workspace"), GlazeIcon.Move), roomDesign: true, result: ResultFor("38-result-review"));
                Require(binding != null, "the review has no binding");
                if (binding == null) return shot;
                host.Show(binding, MenuColumn.File, new object());
                host.DrawnForRender();
                Place(plane, eyes, 0f, shot.Elevation);

                var bar = host.Bar;
                Require(bar.Shown is RoomBar shown && shown.Result && shown.AsDialog, "the review does not stand as one dialog");
                Require(Mathf.Abs(bar.LaidPanelDp - RoomBarView.ReviewDialogDp) < 0.5f, "the review's panel is " + bar.LaidPanelDp.ToString("0") + " dp, not " + RoomBarView.ReviewDialogDp.ToString("0"));
                var panel = bar.Panel;
                var left = panel.transform.localPosition.x - panel.Size.x / 2f;
                var right = panel.transform.localPosition.x + panel.Size.x / 2f;
                var foot = panel.transform.localPosition.y - panel.Size.y / 2f;
                foreach (var button in bar.Column.Where(each => each.gameObject.activeSelf))
                    Require(button.transform.localPosition.x - button.Core.x / 2f >= left - 1e-3f && button.transform.localPosition.x + button.Core.x / 2f <= right + 1e-3f
                        && button.transform.localPosition.y - button.Core.y / 2f >= foot - 1e-3f, button.Words.text + " stands outside the review's panel");
                Debug.Log("Halcyonic: parts: " + name + ": the review laid " + bar.Size.x.ToString("0.000") + " by " + bar.Size.y.ToString("0.000") + " units with a panel " + bar.LaidPanelDp.ToString("0") + " dp wide.");
            }
            finally { RoomDesign.Enabled = was; }
            return shot;
        }
    }
}
