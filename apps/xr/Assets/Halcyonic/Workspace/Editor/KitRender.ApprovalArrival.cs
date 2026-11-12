#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class KitRender
    {
        /// <summary>
        /// AUDIT's timing half (2026-11-12), with Approve in one press: approval A answered, approval B drawn in its place with the same
        /// words, mark and place; a pinch the moment B arrives (well inside PressGuard.SettleSeconds) takes nothing, and the same pinch after
        /// the settle approves B. Through the kit's own host and bar, at both text sizes. Exits 1 on any failure.
        /// </summary>
        public static void ApprovalArrivalCheck()
        {
            var failures = new List<string>();
            try
            {
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    var size = variant.Length == 0 ? "Standard" : variant;
                    try { return ApprovalArrival().Select(failure => size + ": " + failure).ToList(); }
                    catch (Exception error) { return new List<string> { size + ": threw " + error }; }
                }));
            }
            catch (Exception error) { failures.Add("threw " + error); }
            foreach (var failure in failures) Debug.LogError("Halcyonic: approval arrival: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: approval arrival: every check passed");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> ApprovalArrival()
        {
            var failures = new List<string>();
            var root = new GameObject("Approval arrival check");
            var was = RoomDesign.Enabled;
            RoomDesign.Enabled = true;
            try
            {
                var host = KitWorkspaceHost.Create(root.transform);
                host.Accepting = () => true;
                host.Voice = () => VoiceStage.Idle;
                host.AwaitingStart = () => false;
                host.MicLevel = () => null;
                var acted = new List<(string Action, string? Key)>();
                host.Acted += (_, action, key, _, _) => acted.Add((action, key));
                var factory = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var steering = new WorkspaceSteering(factory, () => DateTimeOffset.UtcNow);
                var screen = new FileScreen { Section = FileSection.Waiting, Speak = true };
                var workspace = SelectedWorkspace(waiting: true, command: AskCommand);
                KitFileBinding? Bind() => KitFileBinding.Of(FileScreens.Screen(workspace, steering, screen, new AnswerRoom(4)), steering.Armed, workspace, "",
                    new ConnectionStatus(ConnectionPhase.Live), SelectedProject, ProductionSpine(workspace),
                    new KitRowModel("workspace", LineWords.Ours("Move Workspace"), GlazeIcon.Move), roomDesign: true);

                // Approval A, drawn and settled.
                var first = Bind();
                if (first == null) return new List<string> { "approval A has no binding" };
                var approvalA = workspace.Execution!.PendingApprovals[0].ApprovalId;
                var owner = new object();
                host.Show(first, MenuColumn.File, owner);
                host.SettleForRender();
                // The control: A's own frame shown again, nothing changed, pinched at once, acts. So a pinch the render takes at once
                // is counted, and a refusal below is a settle's (the host's for a new frame, or the button's for a new command), never
                // the harness's.
                host.Show(first, MenuColumn.File, owner);
                var approveA = host.Bar.Button(FileScreens.Approve);
                if (approveA == null) return new List<string> { "approval A drew no Approve" };
                approveA.Press.PressForRender(801);
                if (!acted.Any(each => each.Action == FileScreens.Approve))
                    failures.Add("control: an unchanged Approve pinched at once did not act (acted: " + string.Join(", ", acted) + ")");
                if (host.Bar.ControlOf(approveA)?.Key != approvalA) failures.Add("A's Approve is not keyed by A: " + host.Bar.ControlOf(approveA)?.Key);

                // Approval B drawn in A's place: the same words, mark and place, another approval.
                workspace.Execution.PendingApprovals[0].ApprovalId = approvalA + "-b";
                var second = Bind();
                if (second == null) return failures.Append("approval B has no binding").ToList();
                acted.Clear();
                host.Show(second, MenuColumn.File, owner);
                var approveB = host.Bar.Button(FileScreens.Approve);
                if (approveB == null) return failures.Append("approval B drew no Approve").ToList();
                if (host.Bar.ControlOf(approveB)?.Key != approvalA + "-b") failures.Add("B's Approve is not keyed by B: " + host.Bar.ControlOf(approveB)?.Key);
                // The quick second pinch, at once, inside the settle: it takes nothing.
                approveB.Press.PressForRender(802);
                if (acted.Count > 0) failures.Add("a pinch the moment B arrived acted: " + string.Join(", ", acted));
                // After the settle, the same pinch approves B.
                host.SettleForRender();
                approveB.Press.PressForRender(803);
                if (!acted.Any(each => each.Action == FileScreens.Approve && each.Key == approvalA + "-b"))
                    failures.Add("after the settle the pinch did not approve B (acted: " + string.Join(", ", acted) + ")");
            }
            finally
            {
                RoomDesign.Enabled = was;
                UnityEngine.Object.DestroyImmediate(root);
            }
            return failures;
        }
    }
}
