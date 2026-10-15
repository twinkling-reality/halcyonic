#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Actual Show/Advance callbacks: opening, cancellation, hidden, and repositioning.</summary>
        public static void DrawLifecycleCheck()
        {
            var failures = new List<string>();
            var previousField = ViewField.Current;
            try
            {
                failures.AddRange(GlazeChecks.AtEachTextSize(_ =>
                {
                    var found = new List<string>();
                    foreach (var spatial in new[] { false, true })
                    {
                        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                            UnityEditor.SceneManagement.NewSceneMode.Single);
                        ViewField.Current = FieldChecks.Quest3S;
                        var root = new GameObject("Synthetic draw lifecycle");
                        try { CheckDrawLifecycle(root.transform, spatial, found); }
                        finally { UnityEngine.Object.DestroyImmediate(root); }
                    }
                    return found;
                }));
            }
            catch (Exception error) { failures.Add("draw lifecycle threw " + error); }
            finally { ViewField.Current = previousField; KeepFontAssetsAsCommitted(); }
            foreach (var failure in failures) Debug.LogError("Halcyonic: draw lifecycle: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: draw lifecycle: every check passed.");
            UnityEditor.EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static void CheckDrawLifecycle(Transform parent, bool spatial, List<string> failures)
        {
            var plane = MenuPlane.Create(parent);
            plane.SpatialWorkspace = spatial;
            var label = spatial ? "spatial" : "split";
            var bar = new MenuBar(MenuPlace.Tasks, "Synthetic task");
            var first = new MenuFrame("Synthetic first page", new Footer(PlaneClose),
                lines: new[] { new PageLine("No private work or commands.") });
            var next = new MenuFrame("Synthetic replacement page", new Footer(PlaneClose),
                lines: new[] { new PageLine("A distinct exact frame.") });
            var draws = new List<MenuFrameView>();
            plane.Drawn += (kind, view) =>
            {
                draws.Add(view);
                if (kind != MenuColumn.File || !plane.Shown.Any(each => each.View == view)
                    || !view.gameObject.activeInHierarchy || view.Opening || view.Closing)
                    failures.Add(label + ": a normal route reported a hidden, opening, closing, or noncurrent view drawn");
            };
            void Show(MenuFrame? frame, bool immediately = true) => plane.Show(bar, null, frame, null, Array.Empty<CharacterTarget>(),
                new Vector3(0f, EyeHeight, 0f), Vector3.forward, null, immediately: immediately);
            void Require(bool condition, string why) { if (!condition) failures.Add(label + ": " + why); }

            Show(first);
            var retained = plane.Shown.Single().View;
            Require(draws.Count == 0 && retained.Opening, "initial Show acknowledged before opening completed");
            plane.Advance(MenuPlane.OpeningSeconds / 2f);
            Require(draws.Count == 0, "half-open view acknowledged");
            Show(null);
            plane.Advance(MenuPlane.OpeningSeconds * 2f);
            Require(draws.Count == 0 && !retained.gameObject.activeSelf, "hidden cancelled opening later acknowledged");

            Show(first);
            plane.Advance(MenuPlane.OpeningSeconds * 2f);
            Require(draws.Count == 1 && ReferenceEquals(draws[0].Frame, first), "reopened exact frame failed to acknowledge once");
            draws.Clear();
            Show(null, immediately: false);
            Require(retained.Closing && draws.Count == 0, "closing view acknowledged");
            plane.Advance(MenuPlane.OpeningSeconds * 2f);
            Require(draws.Count == 0, "closing/hidden former view acknowledged");

            // Plane Drawn is a layout report, not read authority. The actual director rejects it
            // while Repositioning; this check deliberately does not pretend to exercise that consumer.
            plane.Repositioning = true;
            Show(next);
            plane.Advance(MenuPlane.OpeningSeconds * 2f);
            Require(draws.Count == 1 && ReferenceEquals(draws[0].Frame, next), "repositioning layout report changed semantics");
            draws.Clear();
            plane.Repositioning = false;
            Require(draws.Count == 0, "clearing repositioning replayed a stale layout report");
            Show(next);
            Require(draws.Count == 1 && ReferenceEquals(draws[0].Frame, next), "fresh settled Show did not report after repositioning");
        }
    }
}
