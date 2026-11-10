#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The redesign's instruction (DESIGN's FINISH-3, 2026-11-05; unit 1d) on the workspace's real host, bound to the frame a
    /// task's file draws, with the redesign on (<see cref="RoomDesign.Enabled"/>) and the old kit not drawn: the row at rest,
    /// listening with and without a level, a typed draft empty and worded, a heard instruction's confirmation, and Sent. Each
    /// says the product's words whole in the one type size, stands its actions at Meta's hand target 12 mm apart, draws its
    /// panel in the translucent tonal material, and takes its presses as the file's own actions.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] BarStates =
        {
            "31-bar-rest", "31-bar-listening", "31-bar-listening-no-level", "31-bar-typing", "31-bar-typed", "31-bar-heard", "31-bar-confirm", "31-bar-confirm-part",
            "31-bar-sent",
        };

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

        /// <summary>The design's own draft, synthetic.</summary>
        private const string BarDraft = "Add a golden balloon worth five points, and remove the old backup file.";

        /// <summary>A heard instruction holding backslashes, synthetic: the confirmation must read exactly the words that will be sent.</summary>
        private const string BarConfirmWords = @"Add a golden balloon worth five points in games\balloon.js, matching \d+ points.";

        /// <summary>A heard instruction long enough for the file to cut in parts, with a backslash in its first, synthetic.</summary>
        private const string BarLongWords = @"Add a golden balloon worth five points in games\balloon.js, then a silver one worth three, then a bomb that ends the round "
            + "when it is popped. Keep the score in the top corner, count each pop once, play the short pop sound for every balloon and the low one for the bomb, "
            + "and show the best score of the day under it. When the round ends, show the three best scores with the time each was made, let the player start again "
            + "with one press, and keep every balloon inside the play area at both screen sizes. Then remove the old backup file and run the tests again.";

        private static Shot BarShot(string state, Transform root, Vector3 eyes)
        {
            var (voice, sent, expected, caption) = state switch
            {
                "31-bar-listening" => (VoiceStage.Listening, false, RoomBarState.Listening, "Hold to Talk held: Listening, the level from recorded samples, synthetic data"),
                "31-bar-listening-no-level" => (VoiceStage.Listening, false, RoomBarState.Listening, "Hold to Talk held where nothing records a level, synthetic data"),
                "31-bar-typing" => (VoiceStage.Idle, false, RoomBarState.Draft, "Type pressed: the panel is the field, only Cancel until a word, synthetic data"),
                "31-bar-typed" => (VoiceStage.Idle, false, RoomBarState.Draft, "A typed draft read: Send Instruction and Cancel, no sentence bar, synthetic data"),
                "31-bar-heard" => (VoiceStage.Idle, false, RoomBarState.Draft, "A heard draft read: Send Heard Instruction?, Send Instruction, Talk Again and Cancel, synthetic data"),
                "31-bar-confirm" => (VoiceStage.Idle, false, RoomBarState.Confirm, "An instruction's confirmation read: Confirm and Cancel, synthetic data"),
                "31-bar-confirm-part" => (VoiceStage.Idle, false, RoomBarState.Confirm, "A long instruction's confirmation, its first part: Next Part and Cancel, synthetic data"),
                "31-bar-sent" => (VoiceStage.Idle, true, RoomBarState.Sent, "Sent, the run not started yet, synthetic data"),
                _ => (VoiceStage.Idle, false, RoomBarState.Rest, "At rest: Hold to Talk and Type, synthetic data"),
            };
            var shot = new Shot { Caption = caption };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var workspace = Running(ShortReport());
            var steering = new WorkspaceSteering(new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" }), () => DateTimeOffset.UtcNow);
            var screen = new FileScreen { Section = FileSection.Activity, Speak = true };
            if (state == "31-bar-typing") screen.Instruction.Restore(workspace.Execution!.ExecutionId, "", false, 0);
            if (state == "31-bar-typed")
            {
                // Typed, and drawn once so it counts as read: Send Instruction has arrived.
                screen.Instruction.Restore(workspace.Execution!.ExecutionId, BarDraft, false, 0);
                screen.Instruction.Drawn(DateTimeOffset.UtcNow);
            }
            if (state == "31-bar-heard")
            {
                // Heard, and drawn once so it counts as read: Send Instruction stands.
                screen.Instruction.Restore(workspace.Execution!.ExecutionId, BarDraft, true, 0);
                screen.Instruction.Drawn(DateTimeOffset.UtcNow);
            }
            // What the confirmation's panel must read: the armed words, or the part of them the file shows first.
            string? armedWords = null;
            if (state is "31-bar-confirm" or "31-bar-confirm-part")
            {
                var heard = state == "31-bar-confirm" ? BarConfirmWords : BarLongWords;
                if (steering.Spoken(heard, workspace).Step != SteeringStep.Confirm) shot.Failures.Add("the heard instruction did not arm its confirmation");
                var request = steering.ToRead(workspace)!;
                var rows = MenuFrameView.RowsOf(FileScreens.RequestLine(steering, request), Glaze.Menu.FileColumnDegrees);
                // A part as tall as the file gives one: every row of a page of eight.
                screen.ReadRequest(request, rows, state == "31-bar-confirm" ? 3 : 8, steering);
                screen.RequestDrawn(0, steering, DateTimeOffset.UtcNow);
                if (state == "31-bar-confirm-part" && screen.RequestParts < 2) shot.Failures.Add("the long instruction is one part of " + rows + " rows");
                armedWords = request;
            }
            var frame = FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4));
            var binding = KitFileBinding.Of(frame, steering.Armed, workspace, screen.Instruction.Open ? screen.Instruction.Words : "", new ConnectionStatus(ConnectionPhase.Live),
                SelectedProject, ProductionSpine(workspace), new KitRowModel("workspace", LineWords.Ours("Move Workspace"), GlazeIcon.Move), roomDesign: true,
                draftSpoken: screen.Instruction.Open && screen.Instruction.Spoken);
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
            try
            {
                Require(host.Holds(binding), "the host does not hold the frame");
                host.Show(binding, MenuColumn.File, new object());
            }
            finally { RoomDesign.Enabled = was; }
            host.SettleForRender();
            var bar = host.Bar;
            const float Tolerance = 1e-4f;
            var typeSize = GlazeTokens.FontSize(RoomType.Scaled(RoomGeometry.TypeDp));
            void Whole(TMPro.TextMeshPro words, string what)
            {
                if (!words.gameObject.activeInHierarchy) return;
                words.ForceMeshUpdate(true);
                Require(!words.isTextTruncated, what + " \"" + words.text + "\" is cut");
                Require(Mathf.Approximately(words.fontSize, typeSize), what + " is not in the one type size");
            }

            // The level is the microphone's: each bar the level of its 50 ms, and none where nothing records one.
            Require(bar.LevelShown == (recorded != null), recorded != null ? "no level stands while the microphone records" : "a level stands where nothing records one");
            if (recorded != null)
            {
                for (var index = 0; index < LevelHistory.Bars; index++)
                    Require(Mathf.Abs(bar.LevelBar(index) - (0.2f + 0.8f * recorded[index])) < 1e-4f, "level bar " + index + " does not follow what was recorded");
                // Inside Listening's halo, after its words.
                var core = bar.Talk.Core;
                var talkX = bar.Talk.transform.localPosition.x;
                Require(bar.Talk.WordsEnd + talkX < bar.transform.Find("Level").localPosition.x && talkX + core.x / 2f >= bar.transform.Find("Level").localPosition.x + RoomType.Units(RoomBarView.LevelWidthDp) - Tolerance,
                    "the level does not stand after Listening's words inside its halo");
            }
            Require(bar.gameObject.activeSelf && bar.Shown?.State == expected, "it shows " + (bar.Shown?.State.ToString() ?? "nothing") + ", not " + expected);
            // The old kit is not drawn at all beside the redesign (the owner, 2026-11-05).
            Require(!host.View.gameObject.activeInHierarchy, "the old kit still draws beside the redesign");
            var actions = new List<RoomButton>();
            if (bar.Shown is RoomBar shown)
            {
                if (shown.Row.Count > 0)
                {
                    // The row: home-size actions in its order, the main one first, its core the hand target itself, 32 dp apart.
                    var row = shown.Row.Select(action => action.Place == KitPlace.Talk ? bar.Talk : bar.Type).ToList();
                    actions.AddRange(row);
                    Require(row.All(button => button.gameObject.activeSelf), "a row action is not drawn");
                    Require(!bar.Type.gameObject.activeSelf || shown.Row.Any(action => action.Place == KitPlace.Field), "Type stands while it listens");
                    for (var index = 0; index < row.Count; index++)
                    {
                        Require(row[index].LookNow() == (shown.Row[index].Main ? RoomButtonLook.Main : RoomButtonLook.Rest), row[index].name + " shows " + row[index].LookNow());
                        Require(Mathf.Abs(row[index].Core.y - RoomType.Units(RoomButton.HeightDp)) < Tolerance, row[index].name + "'s core is not the hand target");
                        Whole(row[index].Words, row[index].name + "'s words");
                        Require(row[index].Words.text == LabelText.ForTextMeshPro(shown.Row[index].Words), row[index].name + " reads \"" + row[index].Words.text + "\"");
                    }
                    for (var index = 1; index < row.Count; index++)
                    {
                        var gap = (row[index].transform.localPosition.x - row[index].Core.x / 2f) - (row[index - 1].transform.localPosition.x + row[index - 1].Core.x / 2f);
                        Require(Mathf.Abs(gap - RoomType.Units(RoomGeometry.RowGapDp)) < Tolerance, "the row's actions stand " + (gap / RoomType.Units(1f)).ToString("0.0") + " dp apart, not 32");
                    }
                }
                if (shown.Pair)
                {
                    // The panel: DESIGN's translucent tonal material, 440 dp wide, the person's words whole, the sentence in its bar.
                    var panel = bar.Panel;
                    Require(panel.gameObject.activeSelf && panel.Glass.gameObject.activeSelf && panel.Glass.sharedMaterial != null
                        && panel.Glass.sharedMaterial.shader.name == "Halcyonic/Room Glass", "the panel is not drawn in the room's material");
                    var look = Halcyonic.XR.UI.RoomSurface.Looks(panel.Kind);
                    Require(look.FillTop.a < 1f && !look.Shadow && look.EdgeDp > 0f, "the panel is not DESIGN's translucent tonal layer with a hairline and no shadow");
                    Require(Mathf.Abs(panel.Size.x - RoomType.Units(RoomGeometry.PanelDp)) < Tolerance, "the panel is not 440 dp wide");
                    Whole(bar.Body, "the panel's words");
                    Require(bar.Body.text.Length == 0 || bar.Body.text.Contains(BarDraft.Substring(0, 20)), "the panel does not hold the person's words");
                    if (armedWords != null)
                    {
                        // The confirmation reads exactly the words that will be sent: as the label parses them, each backslash once (AUDIT, 2026-11-10).
                        bar.Body.ForceMeshUpdate(true);
                        var read = bar.Body.GetParsedText();
                        Require(read.Contains("\\") && !read.Contains("\\\\"), "the panel reads \"" + read + "\", its backslashes not as written");
                        Require(state == "31-bar-confirm" ? read == armedWords : read.Length > 0 && armedWords.StartsWith(read.TrimEnd(), StringComparison.Ordinal),
                            "the panel reads \"" + read + "\", not the armed words \"" + armedWords + "\"");
                    }
                    Require((panel.InnerBar != null) == (shown.Sentence != null), "the sentence bar does not follow the sentence");
                    if (shown.Sentence != null)
                    {
                        Whole(bar.Sentence, "the sentence");
                        Require(bar.Sentence.text == LabelText.ForTextMeshPro(shown.Sentence), "the sentence reads \"" + bar.Sentence.text + "\", not \"" + shown.Sentence + "\"");
                    }
                    Require(bar.CaretShown == shown.Caret, "the caret does not follow the field");
                    // The column: its actions left-aligned, the main one first, the last one's foot on the panel's foot.
                    var drawn = bar.Column.Where(button => button.gameObject.activeSelf).ToList();
                    actions.AddRange(drawn);
                    Require(drawn.Count == shown.Column.Count, drawn.Count + " actions drawn for " + shown.Column.Count);
                    for (var index = 0; index < drawn.Count && index < shown.Column.Count; index++)
                    {
                        Require(ReferenceEquals(bar.ControlOf(drawn[index]), shown.Column[index].Control), drawn[index].Words.text + " raises another control");
                        Require(drawn[index].LookNow() == (shown.Column[index].Main ? RoomButtonLook.Main : RoomButtonLook.Rest), drawn[index].Words.text + " shows " + drawn[index].LookNow());
                        Whole(drawn[index].Words, drawn[index].Words.text);
                    }
                    if (drawn.Count > 0)
                    {
                        var lastFoot = drawn[drawn.Count - 1].transform.localPosition.y - drawn[drawn.Count - 1].Size.y / 2f;
                        var panelFoot = panel.transform.localPosition.y - panel.Size.y / 2f;
                        Require(Mathf.Abs(lastFoot - panelFoot) < 1e-3f, "the column's foot is not on the panel's foot");
                        var lefts = drawn.Select(button => button.transform.localPosition.x - button.Core.x / 2f).ToList();
                        Require(lefts.Max() - lefts.Min() < Tolerance, "the column's actions are not left-aligned");
                    }
                    for (var index = 1; index < drawn.Count; index++)
                    {
                        var gap = (drawn[index - 1].transform.localPosition.y - drawn[index - 1].Size.y / 2f) - (drawn[index].transform.localPosition.y + drawn[index].Size.y / 2f);
                        Require(gap >= KitType.Units(SpatialKit.TargetGapDegrees) - 1e-5f, "the column's targets stand under 12 mm apart");
                    }
                }
                if (shown.State is RoomBarState.Sent or RoomBarState.Words)
                {
                    Whole(bar.Alone, "the words alone");
                    Require(bar.Alone.text == LabelText.ForTextMeshPro(shown.Words.Text), "the words read \"" + bar.Alone.text + "\", not \"" + shown.Words.Text + "\"");
                }
                foreach (var button in actions)
                {
                    var renderer = button.GetComponentInChildren<MeshRenderer>();
                    Require(renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "Halcyonic/Room Halo", button.name + " is not drawn in the room's halo");
                    Require(button.Size.y >= RoomType.Units(RoomButton.HeightDp) - 1e-5f, button.name + "'s target is under Meta's hand target");
                }
            }

            // Placed as the plane places the kit's host: centred on what it holds, scaled for the text size.
            shot.Elevation = CentreFor(host.Size.y * GlazeText.Scale);
            shot.Centre = Place(host.transform, eyes, 0f, shot.Elevation);
            host.transform.localScale *= GlazeText.Scale;
            Stand(shot, host.transform, eyes, new KitCarrier[0], host.Size * GlazeText.Scale);
            var half = bar.Size / 2f;
            foreach (var corner in new[] { new Vector3(-half.x, -half.y, 0f), new Vector3(half.x, -half.y, 0f), new Vector3(-half.x, half.y, 0f), new Vector3(half.x, half.y, 0f) })
                shot.Corners.Add(bar.transform.TransformPoint(corner));
            shot.Targets.Add(("the instruction", bar.transform, bar.Presses.ToList()));
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
                // Its presses are the file's own: Type opens the instruction (Tell it), the panel's field the keyboard, each column action its control.
                var expectedActions = new List<string>();
                if (bar.Type.gameObject.activeSelf && bar.ControlOf(bar.Type) is KitControl typing)
                {
                    bar.Type.Press.PressForRender(501);
                    expectedActions.Add(typing.Action);
                }
                if (bar.Field.gameObject.activeInHierarchy && bar.Shown?.Field is KitControl field)
                {
                    bar.Field.PressForRender(502);
                    expectedActions.Add(field.Action);
                }
                var press = 503;
                foreach (var button in bar.Column.Where(button => button.gameObject.activeSelf).ToList())
                {
                    // Talk Again is held, not pressed: it starts the hold, as Hold to Talk does.
                    if (bar.ControlOf(button) is not KitControl control || control.Place == KitPlace.Talk) continue;
                    button.Press.PressForRender(press++);
                    expectedActions.Add(control.Action);
                }
                foreach (var action in expectedActions)
                    if (!acted.Contains(action)) failures.Add(state + ": a press did not take the file's " + action + "; took " + string.Join(", ", acted));
                Debug.Log("Halcyonic: spatial kit: " + state + ": " + bar.Shown?.State + ", " + bar.Size.x.ToString("0.0000") + " by " + bar.Size.y.ToString("0.0000")
                    + ", presses took " + (acted.Count == 0 ? "nothing" : string.Join(", ", acted)) + ".");
            };
            return shot;
        }
    }
}
