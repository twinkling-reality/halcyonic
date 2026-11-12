#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class KitRender
    {
        /// <summary>
        /// The one press's settle, timed (AUDIT, 2026-11-12): approval A answered on a button, approval B drawn on the same button
        /// with the same words in the same place. A pinch 0.1 s after B arrives sends nothing; the same pinch once B has settled
        /// approves B. A negative control first: where what the button does does not change, a pinch 0.1 s after a redraw acts, the
        /// hole a new command's identity closes. At both text sizes; the button's clock is driven, so nothing waits on real time.
        /// Exits 1 on any failure.
        /// </summary>
        public static void PressSettleCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(PressSettle);
            foreach (var failure in failures) Debug.LogError("Halcyonic: press settle: " + failure);
            // The line the landing's gate counts, as every other journey ends.
            if (failures.Count == 0) Debug.Log("Halcyonic: press settle: a new request in an answered one's place settles again before a pinch counts; every check passed.");
            EditorApplication.Exit(failures.Count > 0 ? 1 : 0);
        }

        private static List<string> PressSettle(string size)
        {
            var failures = new List<string>();
            void Require(bool condition, string why) { if (!condition) failures.Add((size.Length > 0 ? size + ": " : "") + why); }
            var root = new GameObject("Press settle");
            try
            {
                var now = 10f;
                var button = RoomButton.Create(root.transform, "Approve");
                button.Clock = () => now;
                var presses = new List<string>();
                var showing = "";
                button.Pressed += () => presses.Add(showing);
                void Draw(string doing)
                {
                    showing = doing;
                    button.Show("Approve", GlazeIcon.Approve, isMain: true, available: true, Vector2.zero, RoomType.Units(160f), doing: doing);
                }
                void Pinch(float at)
                {
                    now = at;
                    button.Press.PressForRender(7);
                }

                // A settled: the person approves it.
                Draw("answer-approval\napproval-a");
                Pinch(now + PressGuard.SettleSeconds + 0.05f);
                Require(presses.Count == 1 && presses[0] == "answer-approval\napproval-a", "a settled pinch on approval A did not approve it");

                // The control: the same command redrawn at once, a pinch 0.1 s later acts (no identity changed, nothing to settle).
                Draw("answer-approval\napproval-a");
                Pinch(now + 0.1f);
                Require(presses.Count == 2, "the control: a redraw of the same command took a settle it should not need");

                // B drawn in A's place, the same words: a pinch 0.1 s after it arrives sends nothing.
                Draw("answer-approval\napproval-b");
                var arrived = now;
                Pinch(arrived + 0.1f);
                Require(presses.Count == 2, "a pinch 0.1 s after approval B arrived in A's place approved B");

                // Once B has settled, the same pinch approves B.
                Pinch(arrived + PressGuard.SettleSeconds + 0.05f);
                Require(presses.Count == 3 && presses[2] == "answer-approval\napproval-b", "a settled pinch on approval B did not approve it");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
            return failures;
        }
    }
}
