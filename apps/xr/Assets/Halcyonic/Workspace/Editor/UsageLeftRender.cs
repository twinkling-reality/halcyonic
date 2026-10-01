#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Renders the Usage left glance over the stage, with the characters 2.4 m away and on a desk
    /// half a meter away: the chip on the project rail, then the panel open with each kind of answer.
    /// It checks that the chip stays in the room the rail leaves it and clear of the rail's buttons,
    /// that the open panel covers no character's body or label plate and stays within the space
    /// the workspace may take, that none of its own words is cut short, and that an agent name from outside shows by
    /// the one rule. It saves each render in apps/xr/Builds/UsageLeftRenders, which git ignores, with
    /// a close-up at a Quest 3's 25 pixels per degree. In the editor: Halcyonic > Render Usage Left
    /// Over the Stage. In batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1
    /// when a check fails.
    /// </summary>
    public static class UsageLeftRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;
        private const string Journal = "01a0dcf1-5a80-7000-8000-000000000001";

        [MenuItem("Halcyonic/Render Usage Left Over the Stage")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = Run();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Usage left render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = Run();
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> Run()
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "UsageLeftRenders"));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                failures.AddRange(RenderStage("far", folder, radius: CharacterStage.DefaultDistance, surfaceDrop: null));
                failures.AddRange(RenderStage("desk", folder, radius: 0.55f, surfaceDrop: 0.46f));
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: usage left render: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: usage left render: every check passed; the renders are in " + folder);
            return failures;
        }

        /// <summary>What the glance shows, as the person may meet it; null is the chip alone.</summary>
        private static IEnumerable<(string Suffix, UsageLeftPresentation? Shown, bool Hostile)> Answers()
        {
            var now = DateTimeOffset.UtcNow;
            var zone = TimeZoneInfo.Local;
            yield return ("closed", null, false);
            yield return ("available", UsageLeftPresenter.Present(Available(now, "Codex", "codex"), now, zone), false);
            yield return ("not-set-up", UsageLeftPresenter.Present(new UnauthorizedUsageLimits
            {
                Reason = new ErrorInfo { Code = "insufficient_scope", Message = "The credential lacks limits:read." },
            }, now, zone), false);
            yield return ("no-reading", UsageLeftPresenter.Present(new UnavailableUsageLimits
            {
                Reason = new ErrorInfo { Code = "not_captured", Message = "Seorak has not captured a provider limit." },
            }, now, zone), false);
            yield return ("reading", UsageLeftPresenter.Message(UsageLeftPresenter.Reading), false);
            yield return ("partial", UsageLeftPresenter.Present(Available(now, "Codex", "codex", complete: false), now, zone), false);
            yield return ("untrusted", UsageLeftPresenter.Present(Available(now, WorkspaceRender.Hostile("agent"), "render-agent"), now, zone), true);
        }

        /// <summary>Two Codex windows: the five-hour one seen minutes ago, the weekly one two days ago.</summary>
        private static UsageLimitsResponse Available(DateTimeOffset now, string label, string agent, bool complete = true)
        {
            string At(TimeSpan offset) => now.Add(offset).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            UsageLimit Reading(UsageLimitWindow window, double used, TimeSpan seen, TimeSpan resets) => new UsageLimit
            {
                Agent = agent,
                Label = label,
                Window = window,
                UsedPercent = used,
                ObservedAt = At(seen),
                ResetsAt = At(resets),
                Freshness = UsageLimitFreshness.Fresh,
                Account = new UsageLimitAccount(),
            };
            return new AvailableUsageLimits
            {
                Source = new EvaluationSource { System = "seorak", Synthetic = false, ApiVersion = "v1" },
                Complete = complete,
                Readings = new List<UsageLimit>
                {
                    Reading(UsageLimitWindow.Rolling5h, 40.2, TimeSpan.FromMinutes(-12), TimeSpan.FromHours(2)),
                    Reading(UsageLimitWindow.Weekly, 61.5, TimeSpan.FromDays(-2), TimeSpan.FromDays(5)),
                },
            };
        }

        private static IEnumerable<string> RenderStage(string name, string folder, float radius, float? surfaceDrop)
        {
            var failures = new List<string>();
            var root = new GameObject("Usage left render " + name);
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var characters = WorkspaceRender.Lineup(root.transform, eyes, radius, surfaceDrop, WorkspaceRender.Presentation);
                var targets = characters.ConvertAll(character => character.Target);
                var surface = surfaceDrop.HasValue ? EyeHeight - surfaceDrop.Value : (float?)null;
                var state = EntryRender.Portfolio(hostile: false, needsYouNow: false);
                var visibility = new StageVisibility();
                visibility.UseJournal(Journal);
                var lineup = new CharacterLineup(6);
                lineup.Update(state.Workstreams.Values);
                var overview = WorkOverview.Of(state, visibility, id => lineup.SlotOf(id) >= 0);
                var rail = ProjectRail.ForRender(root.transform, overview, surface);
                rail.ResetPosition();

                var glance = UsageLeftGlance.ForRender(rail);
                foreach (var (suffix, shown, hostile) in Answers())
                {
                    glance.ShowForRender(shown, targets, surface);
                    // The rail steps out of the way while the panel is open, as on the headset.
                    rail.Root.gameObject.SetActive(shown == null);
                    var what = name + " " + suffix;
                    WorkspaceRender.ForceMeshes(root);
                    var render = WorkspaceRender.Render(camera, texture);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + ".png"), render.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(render);
                    var closeUp = WorkspaceRender.CloseUp(camera, texture, shown == null ? rail.Root : glance.Panel);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + "-closeup.png"), closeUp.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(closeUp);

                    if (!hostile) failures.AddRange(EntryRender.NothingOfOursCut(glance.Shown, what));
                    else failures.AddRange(WorkspaceRender.AllShowLiterally(glance.Panel.gameObject, "usage left render " + what));
                    if (shown == null)
                    {
                        failures.AddRange(ChipFits(what, rail, glance));
                        continue;
                    }
                    var background = glance.Panel.GetComponentInChildren<SpriteRenderer>();
                    var panelRect = Corners(camera, background.transform, background.size);
                    var rect = new RectInt(Mathf.FloorToInt(panelRect.xMin), Mathf.FloorToInt(panelRect.yMin), Mathf.CeilToInt(panelRect.width), Mathf.CeilToInt(panelRect.height));
                    foreach (var (view, target) in characters)
                    {
                        if (WorkspaceRender.Covered(camera, target, rect)) failures.Add(what + ": " + view.WorkstreamId + "'s body is behind the panel.");
                        var label = WorkspaceRender.LabelRect(camera, view);
                        if (EntryRender.Overlap(panelRect, label)) failures.Add(what + ": the panel covers " + view.WorkstreamId + "'s label.");
                    }
                    // The panel stays within the space the workspace would take: its edges no farther out than the workspace's.
                    var reach = Mathf.Atan2(WorkspacePanel.Height / 2f * WorkspaceLayout.Scale, WorkspaceLayout.Reach) * Mathf.Rad2Deg;
                    var height = glance.Panel.GetComponentInChildren<SpriteRenderer>().size.y * WorkspaceLayout.Scale;
                    var top = Elevation(glance.Panel.position + glance.Panel.up * height / 2f - eyes);
                    var bottom = Elevation(glance.Panel.position - glance.Panel.up * height / 2f - eyes);
                    Debug.Log("Halcyonic: usage left render " + what + ": the panel spans " + WorkspaceRender.Degrees(-top) + " to "
                        + WorkspaceRender.Degrees(-bottom) + " degrees below eye level.");
                    if (bottom < WorkspacePlacement.LowestDegrees - reach - 0.01f || top > WorkspacePlacement.HighestDegrees + reach + 0.01f)
                    {
                        failures.Add(what + ": the panel reaches outside the space the workspace may take.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        /// <summary>A flat rectangle of <paramref name="size"/> on <paramref name="surface"/>'s XY plane, on the render.</summary>
        private static Rect Corners(Camera camera, Transform surface, Vector2 size, Vector3 center = default)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var corner in new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(1f, 1f), new Vector2(-1f, 1f) })
            {
                var screen = camera.WorldToScreenPoint(surface.TransformPoint(center + new Vector3(corner.x * size.x / 2f, corner.y * size.y / 2f, 0f)));
                minX = Mathf.Min(minX, screen.x);
                maxX = Mathf.Max(maxX, screen.x);
                minY = Mathf.Min(minY, screen.y);
                maxY = Mathf.Max(maxY, screen.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static float Elevation(Vector3 toward) =>
            Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;

        /// <summary>The chip stays in the room the rail leaves it, apart from every rail button.</summary>
        private static IEnumerable<string> ChipFits(string what, ProjectRail rail, UsageLeftGlance glance)
        {
            var chip = glance.Chip;
            var x = chip.transform.localPosition.x;
            var left = x - chip.Width / 2f;
            var right = x + chip.Width / 2f;
            if (right > ProjectRail.RailWidth / 2f + 1e-4f) yield return what + ": the chip runs past the rail's right end.";
            if (left < ProjectRail.RailWidth / 2f - ProjectRail.UsageLeftRoom - 1e-4f) yield return what + ": the chip leaves the room kept for Usage left.";
            foreach (var button in rail.Shown)
            {
                if (Mathf.Abs(button.transform.localPosition.y - chip.transform.localPosition.y) > 1e-3f) continue;
                var otherRight = button.transform.localPosition.x + button.Width / 2f;
                if (otherRight > left - ProjectRail.Gap / 2f) yield return what + ": the chip is too close to the rail's " + button.name + ".";
            }
        }
    }
}
