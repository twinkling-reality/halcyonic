#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Renders the Usage left glance over the stage, with the characters 2.4 m away and on a desk
    /// half a meter away: the chip on the project rail, then the panel open with each kind of answer.
    /// It checks that the chip stays in the room the rail leaves it and clear of the rail's buttons;
    /// that the open panel covers no character's body or label plate, a degree or more from each, and
    /// stays within the space the workspace may take; that its targets are large enough and 12 mm
    /// apart, its words large enough and none of its own cut short; that four windows show on one page
    /// and more page with where they come from on every page; that each meter pictures the words under
    /// it from the same share, and only its track while a read is in flight; that Refresh waits while
    /// reading and is not offered in the demonstration; that only a failure is said in red; and that an
    /// agent name from outside shows by the one rule. It saves each render in
    /// apps/xr/Builds/UsageLeftRenders, which git ignores, with a close-up at a Quest 3's 25 pixels
    /// per degree and the whole panel. In the editor: Halcyonic > Render Usage Left Over the Stage. In
    /// batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1 when a check fails.
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

        /// <summary>One way the person may meet the glance.</summary>
        private readonly struct Answer
        {
            public Answer(string suffix, UsageLeftPresentation? shown, bool reading = false, bool demonstration = false, bool hostile = false)
            {
                Suffix = suffix;
                Shown = shown;
                Reading = reading;
                Demonstration = demonstration;
                Hostile = hostile;
            }

            public string Suffix { get; }

            /// <summary>What the glance shows; null is the chip alone.</summary>
            public UsageLeftPresentation? Shown { get; }

            /// <summary>A read is in flight.</summary>
            public bool Reading { get; }

            /// <summary>The recorded demonstration plays, so there is nowhere to read.</summary>
            public bool Demonstration { get; }

            /// <summary>An agent's name from outside, written to break the label.</summary>
            public bool Hostile { get; }
        }

        private static IEnumerable<Answer> Answers()
        {
            var now = DateTimeOffset.UtcNow;
            var zone = TimeZoneInfo.Local;
            UsageLeftPresentation Present(UsageLimitsResponse response) => UsageLeftPresenter.Present(response, now, zone);
            var codex = new[] { ("Codex", "codex") };
            yield return new Answer("closed", null);
            yield return new Answer("available", Present(Available(now, codex)));
            yield return new Answer("four", Present(Available(now, new[] { ("Claude", "claude-code"), ("Codex", "codex") }, synthetic: true)));
            yield return new Answer("six", Present(Available(now, new[] { ("Claude", "claude-code"), ("Codex", "codex"), ("OpenCode", "opencode") })));
            yield return new Answer("refreshing", Present(Available(now, codex)), reading: true);
            yield return new Answer("not-set-up", Present(new UnauthorizedUsageLimits
            {
                Reason = new ErrorInfo { Code = "insufficient_scope", Message = "The credential lacks limits:read." },
            }));
            yield return new Answer("no-reading", Present(new UnavailableUsageLimits
            {
                Reason = new ErrorInfo { Code = "not_captured", Message = "Seorak has not captured a provider limit." },
            }));
            yield return new Answer("reading", UsageLeftPresenter.Message(UsageLeftPresenter.Reading), reading: true);
            yield return new Answer("partial", Present(Available(now, codex, complete: false)));
            yield return new Answer("unreachable", UsageLeftPresenter.Unreachable());
            yield return new Answer("demo", UsageLeftPresenter.Message(UsageLeftPresenter.NotInDemo), demonstration: true);
            yield return new Answer("untrusted", Present(Available(now, new[] { (WorkspaceRender.Hostile("agent"), "render-agent") })), hostile: true);
        }

        /// <summary>Each agent's two windows: the five-hour one seen minutes ago, the weekly one two days ago.</summary>
        private static UsageLimitsResponse Available(DateTimeOffset now, IEnumerable<(string Label, string Agent)> agents, bool complete = true, bool synthetic = false)
        {
            string At(TimeSpan offset) => now.Add(offset).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            UsageLimit Reading(string label, string agent, UsageLimitWindow window, double used, TimeSpan seen, TimeSpan resets) => new UsageLimit
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
            var readings = new List<UsageLimit>();
            var used = new[] { 40.2, 61.5, 7.9, 97.4, 0.0, 100.0 };
            foreach (var (label, agent) in agents)
            {
                readings.Add(Reading(label, agent, UsageLimitWindow.Rolling5h, used[readings.Count % used.Length], TimeSpan.FromMinutes(-12), TimeSpan.FromHours(2)));
                readings.Add(Reading(label, agent, UsageLimitWindow.Weekly, used[readings.Count % used.Length], TimeSpan.FromDays(-2), TimeSpan.FromDays(5)));
            }
            return new AvailableUsageLimits
            {
                Source = new EvaluationSource { System = "seorak", Synthetic = synthetic, ApiVersion = "v1" },
                Complete = complete,
                Readings = readings,
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
                foreach (var answer in Answers())
                {
                    var shown = answer.Shown;
                    glance.ShowForRender(shown, targets, surface, answer.Reading, answer.Demonstration);
                    // The rail steps out of the way while the panel is open, as on the headset.
                    rail.Root.gameObject.SetActive(shown == null);
                    var what = name + " " + answer.Suffix;
                    WorkspaceRender.ForceMeshes(root);
                    var render = WorkspaceRender.Render(camera, texture);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + answer.Suffix + ".png"), render.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(render);
                    var closeUp = WorkspaceRender.CloseUp(camera, texture, shown == null ? rail.Root : glance.Panel);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + answer.Suffix + "-closeup.png"), closeUp.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(closeUp);

                    if (shown == null)
                    {
                        failures.AddRange(EntryRender.NothingOfOursCut(glance.Shown, what));
                        failures.AddRange(ChipFits(what, rail, glance));
                        continue;
                    }
                    var whole = Whole(camera, texture, glance.Panel);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + answer.Suffix + "-panel.png"), whole.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(whole);

                    var frame = glance.Frame;
                    failures.AddRange(ClearOfTheStage(what, camera, glance, characters, eyes));
                    failures.AddRange(Fits(what, glance, eyes));
                    if (!answer.Hostile) failures.AddRange(EntryRender.NothingOfOursCut(glance.Shown, what, frame));
                    else failures.AddRange(WorkspaceRender.AllShowLiterally(glance.Panel.gameObject, "usage left render " + what, eyes));
                    failures.AddRange(Says(what, frame, shown, answer));
                    failures.AddRange(MetersPictureTheirWords(what, frame, answer.Reading));
                    if (shown.Rows.Count > 2 * 2)
                    {
                        // More windows than four page, and every page says where they come from and what is unknown.
                        if (frame.Pages < 2) failures.Add(what + ": " + shown.Rows.Count + " windows show on one page, so the case checks no paging.");
                        for (var page = 1; page < frame.Pages; page++)
                        {
                            frame.Page = page;
                            frame.Show(frame.Shown!);
                            WorkspaceRender.ForceMeshes(root);
                            var paged = what + " page " + (page + 1);
                            var pageImage = Whole(camera, texture, glance.Panel);
                            File.WriteAllBytes(Path.Combine(folder, name + "-" + answer.Suffix + "-" + (page + 1).ToString(CultureInfo.InvariantCulture) + "-panel.png"), pageImage.EncodeToPNG());
                            UnityEngine.Object.DestroyImmediate(pageImage);
                            failures.AddRange(Fits(paged, glance, eyes));
                            failures.AddRange(EntryRender.NothingOfOursCut(glance.Shown, paged, frame));
                            failures.AddRange(Says(paged, frame, shown, answer));
                            failures.AddRange(MetersPictureTheirWords(paged, frame, answer.Reading));
                        }
                    }
                    else if (frame.Pages > 1) failures.Add(what + ": " + shown.Rows.Count + " windows take " + frame.Pages + " pages; four or fewer show on one.");
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

        /// <summary>
        /// The open panel covers no character's body or label plate on the render, stands a degree or
        /// more from each as the eyes see them, and stays within the space the workspace may take: its
        /// edges no farther out than the workspace's, which is the same frame.
        /// </summary>
        private static IEnumerable<string> ClearOfTheStage(string what, Camera camera, UsageLeftGlance glance,
            List<(CharacterView View, CharacterTarget Target)> characters, Vector3 eyes)
        {
            var failures = new List<string>();
            var frame = glance.Frame;
            var rect = WorkspaceRender.ScreenRect(camera, frame.transform, frame.Size);
            var outline = Rect.MinMaxRect(rect.xMin, rect.yMin, rect.xMax, rect.yMax);
            var near = new List<GlazeChecks.Extent> { GlazeChecks.Of("the Usage left panel", eyes, frame.gameObject) };
            foreach (var (view, target) in characters)
            {
                if (WorkspaceRender.Covered(camera, target, rect)) failures.Add(what + ": " + view.WorkstreamId + "'s body is behind the panel.");
                if (EntryRender.Overlap(outline, WorkspaceRender.LabelRect(camera, view))) failures.Add(what + ": the panel covers " + view.WorkstreamId + "'s label.");
                near.Add(GlazeChecks.Of(view.WorkstreamId + "'s label", eyes, view.Label.gameObject));
                near.Add(WorkspaceRender.BodyExtent(view, eyes));
            }
            failures.AddRange(GlazeChecks.Apart(near).Where(failure => failure.Contains("the Usage left panel")).Select(failure => what + ": " + failure));
            var reach = PanelFrame.HeightDegrees / 2f;
            var half = frame.Size.y / 2f * glance.Panel.lossyScale.y;
            var top = Elevation(glance.Panel.position + glance.Panel.up * half - eyes);
            var bottom = Elevation(glance.Panel.position - glance.Panel.up * half - eyes);
            Debug.Log("Halcyonic: usage left render " + what + ": the panel spans " + WorkspaceRender.Degrees(-top) + " to "
                + WorkspaceRender.Degrees(-bottom) + " degrees below eye level.");
            if (bottom < WorkspacePlacement.LowestDegrees - reach - 0.01f || top > WorkspacePlacement.HighestDegrees + reach + 0.01f)
            {
                failures.Add(what + ": the panel reaches outside the space the workspace may take.");
            }
            return failures;
        }

        /// <summary>
        /// The panel as the eyes see it: its targets 60 dp, 48 for Close and the pager, 12 mm apart and
        /// inside the panel; every word at least the caption's size; the list's lines inside the body.
        /// </summary>
        private static IEnumerable<string> Fits(string what, UsageLeftGlance glance, Vector3 eyes)
        {
            var failures = new List<string>();
            var frame = glance.Frame;
            var buttons = frame.Buttons.Where(button => !button.Static).ToList();
            failures.AddRange(GlazeChecks.TargetsLargeEnough(buttons, eyes, what));
            failures.AddRange(GlazeChecks.MicrophoneOnlyWhereHeld(buttons, what));
            failures.AddRange(GlazeChecks.TextLargeEnough(glance.Panel.gameObject, eyes, what));
            var gap = Glaze.TargetGapMeters / PanelFrame.Distance;
            for (var a = 0; a < buttons.Count; a++)
            {
                for (var b = a + 1; b < buttons.Count; b++)
                {
                    if (!Apart(PanelFrame.RectOf(buttons[a]), PanelFrame.RectOf(buttons[b]), gap)) failures.Add(what + ": " + buttons[a].name + " and " + buttons[b].name + " are closer than 12 mm.");
                }
            }
            var size = frame.Size;
            foreach (var button in buttons)
            {
                var rect = PanelFrame.RectOf(button);
                if (rect.xMin < -size.x / 2f || rect.xMax > size.x / 2f || rect.yMin < -size.y / 2f || rect.yMax > size.y / 2f)
                {
                    failures.Add(what + ": " + button.name + " runs past the panel's edge.");
                }
            }
            var area = frame.ListArea;
            foreach (var (label, _) in frame.ShownLines)
            {
                label.ForceMeshUpdate();
                var bottom = label.transform.localPosition.y - label.textInfo.lineCount * GlazeText.LineHeight(label);
                if (bottom < area.yMin - 1e-3f) failures.Add(what + ": " + label.name + " runs below the body: " + label.text);
            }
            return failures;
        }

        /// <summary>
        /// Close stands in the header; Refresh is on the bar, waiting while a read is in flight, and not
        /// offered where there is nothing to read; where the windows come from shows under the title
        /// whenever they show; and only a failure is said in red, Usage left not being set up as plainly
        /// as any other answer.
        /// </summary>
        private static IEnumerable<string> Says(string what, PanelFrame frame, UsageLeftPresentation shown, Answer answer)
        {
            var close = frame.ButtonFor(PanelModel.Close);
            if (close == null) yield return what + ": the panel offers no Close.";
            else if (PanelFrame.RectOf(close).yMax < frame.Size.y / 2f - GlazeTokens.Units(2f)) yield return what + ": Close does not stand in the header.";
            var refresh = frame.ButtonFor(UsageLeftScreens.Refresh);
            if (answer.Demonstration)
            {
                if (refresh != null) yield return what + ": Refresh is offered in the demonstration, where there is nothing to read.";
            }
            else if (refresh == null) yield return what + ": the panel offers no Refresh.";
            else if (refresh.Available == answer.Reading) yield return what + ": Refresh " + (answer.Reading ? "takes a press while a read is in flight." : "waits with no read in flight.");
            var labels = frame.Labels.ToList();
            if (shown.Source != null && !labels.Any(label => label.text == LabelText.ForTextMeshPro(shown.Source)))
            {
                yield return what + ": the panel does not say where the windows come from: " + shown.Source;
            }
            if (shown.Rows.Count > 0 && !labels.Any(label => label.text == LabelText.ForTextMeshPro(shown.Note)))
            {
                yield return what + ": the panel does not say " + shown.Note;
            }
            if (shown.Rows.Count == 0)
            {
                var line = frame.ShownLines.FirstOrDefault().Label;
                var expected = shown.Failed ? GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Failure).Foreground) : GlazeTokens.Text;
                if (line == null) yield return what + ": the panel says nothing.";
                else if (line.color != expected) yield return what + ": \"" + shown.Note + "\" is said in " + line.color + ", not " + expected + ".";
            }
        }

        /// <summary>
        /// Each meter stands at its line's right end, clear of the line's words and inside the body,
        /// pictures the "at most" words under it from the same share, its fill that share of its track,
        /// and shows only its track while a read is in flight.
        /// </summary>
        private static IEnumerable<string> MetersPictureTheirWords(string what, PanelFrame frame, bool reading)
        {
            var lines = frame.ShownLines;
            var area = frame.ListArea;
            var meters = frame.ShownMeters;
            var meteredLines = lines.Count(line => line.Row.Meter.HasValue);
            if (meters.Count != meteredLines) yield return what + ": " + meters.Count + " meters show for " + meteredLines + " windows.";
            foreach (var (track, filled, row) in meters)
            {
                var index = -1;
                for (var each = 0; each < lines.Count; each++)
                {
                    if (lines[each].Row == row) index = each;
                }
                if (index < 0 || index + 1 >= lines.Count)
                {
                    yield return what + ": a meter shows without its words.";
                    continue;
                }
                var share = Mathf.RoundToInt(row.Meter!.Value * 100f);
                var words = lines[index + 1].Row.Title;
                if (!words.StartsWith("At most " + share.ToString(CultureInfo.InvariantCulture) + "% left", StringComparison.Ordinal))
                {
                    yield return what + ": a meter of " + share + "% stands over \"" + words + "\".";
                }
                var expected = reading ? 0f : row.Meter.Value * track.width;
                if (Mathf.Abs(filled - expected) > 1e-4f) yield return what + ": a meter of " + share + "% fills " + filled + " of " + track.width + " where " + expected + " was expected.";
                var label = lines[index].Label;
                if (label.transform.localPosition.x + label.rectTransform.sizeDelta.x > track.xMin - GlazeTokens.Units(0.5f))
                {
                    yield return what + ": the words \"" + label.text + "\" reach the meter beside them.";
                }
                if (track.xMax > area.xMax + 1e-4f || track.yMin < area.yMin - 1e-4f || track.yMax > area.yMax + 1e-4f)
                {
                    yield return what + ": a meter stands outside the body.";
                }
            }
        }

        /// <summary>The whole panel, the eyes turned to its middle, a little wider than the panel: to see every edge, not to judge size.</summary>
        private static Texture2D Whole(Camera camera, RenderTexture texture, Transform panel)
        {
            var rotation = camera.transform.rotation;
            var fieldOfView = camera.fieldOfView;
            camera.transform.rotation = Quaternion.LookRotation(panel.position - camera.transform.position, Vector3.up);
            camera.fieldOfView = PanelFrame.WidthDegrees + 6f;
            var image = WorkspaceRender.Render(camera, texture);
            camera.transform.rotation = rotation;
            camera.fieldOfView = fieldOfView;
            return image;
        }

        /// <summary>Two outlines at least <paramref name="gap"/> apart one way or the other.</summary>
        private static bool Apart(Rect a, Rect b, float gap)
        {
            var dx = Mathf.Max(a.xMin - b.xMax, b.xMin - a.xMax);
            var dy = Mathf.Max(a.yMin - b.yMax, b.yMin - a.yMax);
            return dx >= gap * 0.99f || dy >= gap * 0.99f;
        }

        private static float Elevation(Vector3 toward) =>
            Mathf.Atan2(toward.y, new Vector2(toward.x, toward.z).magnitude) * Mathf.Rad2Deg;

        /// <summary>
        /// The chip stays inside the rail's lower row, 12 mm or more from every other rail button in
        /// it, and its label is whole.
        /// </summary>
        private static IEnumerable<string> ChipFits(string what, ProjectRail rail, UsageLeftGlance glance)
        {
            var chip = glance.Chip;
            var x = chip.transform.localPosition.x;
            var left = x - chip.Width / 2f;
            var right = x + chip.Width / 2f;
            var gap = Glaze.TargetGapMeters / rail.Root.lossyScale.x;
            if (right > ProjectRail.Width / 2f + 1e-4f) yield return what + ": the chip runs past the rail's right end.";
            if (left < -ProjectRail.Width / 2f - 1e-4f) yield return what + ": the chip runs past the rail's left end.";
            if (chip.transform.localPosition.y > 0f) yield return what + ": the chip is not in the rail's lower row.";
            foreach (var button in rail.Shown)
            {
                if (button == chip || Mathf.Abs(button.transform.localPosition.y - chip.transform.localPosition.y) > 1e-3f) continue;
                var otherLeft = button.transform.localPosition.x - button.Width / 2f;
                var otherRight = button.transform.localPosition.x + button.Width / 2f;
                if (otherRight > left - gap + 1e-4f && otherLeft < right + gap - 1e-4f) yield return what + ": the chip is closer than 12 mm to the rail's " + button.name + ".";
            }
            chip.Label.ForceMeshUpdate();
            if (chip.Label.isTextTruncated) yield return what + ": the chip cuts its label short: " + chip.Label.text;
        }
    }
}
