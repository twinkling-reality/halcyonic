#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The redesign's Type or Talk bar (DESIGN's SYSTEM-v4, 2026-11-05; unit 1) on the workspace's real host, bound to the
    /// frame a task's file draws, with the redesign on (<see cref="RoomDesign.Enabled"/>): at rest, open on a draft,
    /// listening and sent. Each state stands under the kit in the field, says the product's words whole in the one type
    /// size, is as wide as <see cref="RoomBar"/> says, keeps its controls inside the capsule, hides the composer's own
    /// well, mic and Send, and takes its presses as the file's own actions.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] BarStates = { "31-bar-rest", "31-bar-open", "31-bar-listening", "31-bar-listening-no-level", "31-bar-sent" };

        /// <summary>What a microphone recorded, 50 ms a step, rising then falling: real samples the level is taken from, as hold to talk takes it.</summary>
        private static LevelHistory RecordedLevel()
        {
            var history = new LevelHistory();
            var samples = new float[800];
            for (var step = 0; step < LevelHistory.Bars; step++)
            {
                var amplitude = 0.004f + 0.25f * Mathf.Sin(Mathf.PI * step / (LevelHistory.Bars - 1));
                for (var index = 0; index < samples.Length; index++) samples[index] = amplitude * Mathf.Sin(index * 0.3f);
                history.Add(samples, samples.Length);
            }
            return history;
        }

        /// <summary>The design's own draft, short enough for the bar's lines at both sizes.</summary>
        private const string BarDraft = "Add a golden balloon worth five points, and remove the old backup file.";

        private static Shot BarShot(string state, Transform root, Vector3 eyes)
        {
            var (open, voice, sent, expected, caption) = state switch
            {
                "31-bar-open" => (true, VoiceStage.Idle, false, RoomBarState.Open, "The bar open on a draft, Send offered once read, synthetic data"),
                "31-bar-listening" => (false, VoiceStage.Listening, false, RoomBarState.Listening, "The bar listening, the level from recorded samples, synthetic data"),
                "31-bar-listening-no-level" => (false, VoiceStage.Listening, false, RoomBarState.Listening, "The bar listening where nothing records a level, synthetic data"),
                "31-bar-sent" => (false, VoiceStage.Idle, true, RoomBarState.Sent, "The bar after Send, the run not started yet, synthetic data"),
                _ => (false, VoiceStage.Idle, false, RoomBarState.Rest, "The bar at rest: Type or Talk, synthetic data"),
            };
            var shot = new Shot { Caption = caption };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var workspace = Running(ShortReport());
            var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" }));
            var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
            if (open) screen.Instruction.Restore(workspace.Execution!.ExecutionId, BarDraft, false, 0);
            var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4));
            var binding = KitFileBinding.Of(frame, steering.Armed, workspace, open ? screen.Instruction.Words : "", new ConnectionStatus(ConnectionPhase.Live),
                SelectedProject, ProductionSpine(workspace), new KitRowModel("workspace", LineWords.Ours("Move Workspace"), GlazeIcon.Move));
            if (binding == null)
            {
                shot.Failures.Add("the kit has no binding to this frame");
                return shot;
            }
            var host = KitWorkspaceHost.Create(root);
            host.Accepting = () => true;
            host.Voice = () => voice;
            host.AwaitingStart = () => sent;
            var recorded = state == "31-bar-listening" ? RecordedLevel() : null;
            host.MicLevel = () => recorded;
            var acted = new List<string>();
            host.Acted += (_, action, _, _, _) => acted.Add(action);
            var was = RoomDesign.Enabled;
            RoomDesign.Enabled = true;
            try { host.Show(binding, MenuColumn.File, new object()); }
            finally { RoomDesign.Enabled = was; }
            host.SettleForRender();
            var bar = host.Bar;
            foreach (var press in new[] { bar.Field, bar.Mic.Press, bar.Send.Press }) press.SettleForRender();
            bar.SettleForRender();
            // The level is the microphone's: each bar the level of its 50 ms, and none where nothing records one.
            Require(bar.LevelShown == (recorded != null), recorded != null ? "no level stands while the microphone records" : "a level stands where nothing records one");
            if (recorded != null)
                for (var index = 0; index < LevelHistory.Bars; index++)
                    Require(Mathf.Abs(bar.LevelBar(index) - (0.2f + 0.8f * recorded[index])) < 1e-4f, "level bar " + index + " does not follow what was recorded");

            Require(bar.gameObject.activeSelf && bar.Shown?.State == expected, "the bar shows " + (bar.Shown?.State.ToString() ?? "nothing") + ", not " + expected);
            if (bar.Shown is RoomBar shown)
            {
                const float Tolerance = 1e-4f;
                // As wide as SYSTEM-v4 gives it, wider only where its one line of words would else be cut (at the larger size).
                var given = RoomType.Units(shown.WidthDp);
                Require(bar.Size.x >= given - Tolerance && (shown.State == RoomBarState.Open ? Mathf.Abs(bar.Size.x - given) < Tolerance : true),
                    "the capsule is " + bar.Size.x.ToString("0.0000") + " wide, under its " + shown.WidthDp + " dp");
                if (bar.Size.x > given + Tolerance)
                    Debug.Log("Halcyonic: spatial kit: " + state + ": the bar widens to " + (bar.Size.x / RoomType.Units(1f)).ToString("0") + " dp for its words at this text size.");
                bar.Words.ForceMeshUpdate(true);
                Require(!bar.Words.isTextTruncated, "the bar's words \"" + bar.Words.text + "\" are cut");
                Require(bar.Words.text == LabelText.ForTextMeshPro(shown.Words.Text), "the bar reads \"" + bar.Words.text + "\", not \"" + shown.Words.Text + "\"");
                Require(Mathf.Approximately(bar.Words.fontSize, GlazeTokens.FontSize(RoomType.Scaled(RoomGeometry.TypeDp))), "the bar's words are not in the one type size");
                foreach (var control in new[] { bar.Mic, bar.Send }.Where(control => control.gameObject.activeSelf))
                {
                    var centre = (Vector2)control.transform.localPosition;
                    Require(Mathf.Abs(centre.x) + control.Size.x / 2f <= bar.Size.x / 2f + Tolerance && Mathf.Abs(centre.y) + control.Size.y / 2f <= bar.Size.y / 2f + Tolerance,
                        "the bar's " + control.name + " stands outside its capsule");
                }
                // Send stands only where it can send (BUILD-SPEC-v4 1.4: no disabled look).
                Require(bar.Send.gameObject.activeSelf == (shown.SendShown && shown.SendAvailable), "Send's showing does not follow the bar");
                // DESIGN's chips (BUILD-SPEC-v4 1.4): the mic at rest, the main chip while it listens; Send the main chip where it stands.
                if (bar.Mic.gameObject.activeSelf)
                    Require(bar.Mic.LookNow() == (shown.State == RoomBarState.Listening ? RoomButtonLook.Main : RoomButtonLook.Rest), "the mic shows " + bar.Mic.LookNow());
                if (bar.Send.gameObject.activeSelf)
                    Require(bar.Send.LookNow() == RoomButtonLook.Main, "Send shows " + bar.Send.LookNow());
                // The old kit is not drawn at all beside the redesign (the owner, 2026-11-05): its spine, slate and rail are gone.
                Require(!host.View.gameObject.activeInHierarchy, "the old kit still draws beside the redesign's bar");
            }

            // Placed as the plane places the kit's host: centred on what it holds, the bar alone, scaled for the text size.
            shot.Elevation = CentreFor(host.Size.y * GlazeText.Scale);
            shot.Centre = Place(host.transform, eyes, 0f, shot.Elevation);
            host.transform.localScale *= GlazeText.Scale;
            // The head tips for what the host holds: the bar alone.
            Stand(shot, host.transform, eyes, new KitCarrier[0], host.Size * GlazeText.Scale);
            var half = bar.Size / 2f;
            foreach (var corner in new[] { new Vector3(-half.x, -half.y, 0f), new Vector3(half.x, -half.y, 0f), new Vector3(-half.x, half.y, 0f), new Vector3(half.x, half.y, 0f) })
                shot.Corners.Add(bar.transform.TransformPoint(corner));
            shot.Targets.Add(("the bar", bar.transform, new[] { bar.Field, bar.Mic.Press, bar.Send.Press }.Where(press => press.gameObject.activeInHierarchy)));
            // The capsule is DESIGN's glass (BUILD-SPEC-v4 1.2): its glass draws, the flat placeholder does not.
            Require(bar.Capsule.Glass.gameObject.activeSelf && !bar.Capsule.Drawn.gameObject.activeSelf
                && bar.Capsule.Glass.sharedMaterial != null && bar.Capsule.Glass.sharedMaterial.shader.name == "Halcyonic/Room Glass",
                "the bar's capsule is not drawn in the room's glass");
            // The one sheen: a 2 s pass with its peak at the middle, then 8 s of rest; none kept still.
            var stillWas = GlazeMotion.Still;
            GlazeMotion.Still = false;
            var (_, passing) = RoomSheen.At(1f);
            var (_, resting) = RoomSheen.At(5f);
            GlazeMotion.Still = true;
            var (_, kept) = RoomSheen.At(1f);
            GlazeMotion.Still = stillWas;
            Require(Mathf.Abs(passing - RoomSheen.Peak) < 1e-4f && resting == 0f && kept == 0f,
                "the sheen is " + passing + " mid-pass, " + resting + " at rest and " + kept + " kept still, not its peak, none and none");
            shot.AfterRender = failures =>
            {
                // Its presses are the file's own: the words open it to write (Tell it) at rest and the keyboard open; Send sends.
                if (bar.Shown?.FieldAction is string field)
                {
                    bar.Field.PressForRender(501);
                    if (!acted.Contains(field)) failures.Add(state + ": the bar's words did not take the file's " + field + "; took " + string.Join(", ", acted));
                }
                if (bar.Shown is { SendShown: true, SendAvailable: true })
                {
                    bar.Send.Press.PressForRender(502);
                    if (!acted.Contains(RoomBar.SendAction)) failures.Add(state + ": Send did not take the file's " + RoomBar.SendAction + "; took " + string.Join(", ", acted));
                }
                Debug.Log("Halcyonic: spatial kit: " + state + ": the bar " + bar.Shown?.State + ", " + bar.Size.x.ToString("0.0000") + " by " + bar.Size.y.ToString("0.0000")
                    + ", presses took " + (acted.Count == 0 ? "nothing" : string.Join(", ", acted)) + ".");
            };
            return shot;
        }
    }
}
