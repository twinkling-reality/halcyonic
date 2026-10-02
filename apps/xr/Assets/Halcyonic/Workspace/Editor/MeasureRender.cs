#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using TMPro;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Measures, off the device, what the interface asks of a Quest 3 (ADR 0023), for each surface as
    /// the person meets it: the stage with the rail, the stage beside a window while another window
    /// has focus, the entry panel, a workspace, Usage left and Settings, each over the stage.
    /// </summary>
    /// <remarks>
    /// For each it counts what draws: the renderers showing, an upper bound on draw calls (every
    /// material of every renderer that shows; batching only lowers it), the text labels with their
    /// characters and vertices, and the triangles. For each panel it shows the same screen again, as
    /// the panels do every half second while they show, and counts the text meshes built again, the
    /// time it takes in the editor and the bytes it allocates. And it runs every per-frame method of
    /// the parts it built for 60 frames and counts the bytes they allocate. It fails when a panel's
    /// own draws pass ADR 0023's 60, or everything showing at once passes 220; when showing an
    /// unchanged screen again builds a text mesh again; or when a frame allocates. Times are the
    /// editor's on a Mac, not a Quest's; the headset tells those
    /// (docs/internal/validation/quest-3-performance.md). In the editor: Halcyonic > Measure the
    /// Interface. In batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md; it exits with 1 when a
    /// check fails.
    /// </remarks>
    public static class MeasureRender
    {
        private const int Size = 1024;
        private const float EyeHeight = 1.2f;

        /// <summary>ADR 0023's draw call budget: a panel's own, and everything that shows at once.</summary>
        private const int PanelBudget = 60;

        private const int SceneBudget = 220;
        private const int Frames = 60;
        private const int Shows = 20;

        /// <summary>
        /// Each stretch is measured this many times, a moment apart, and the least kept: the editor's
        /// own work on other threads only ever adds to a count, and comes in bursts.
        /// </summary>
        private const int Repeats = 12;

        /// <summary>The bytes the managed heap has handed out this frame so far: read before and after a stretch, what it allocated.</summary>
        private static ProfilerRecorder allocations;

        /// <summary>Every per-frame method of the parts the scenes build: what runs on the headset each frame.</summary>
        private static readonly (Type Type, string Method)[] PerFrame =
        {
            (typeof(CharacterView), "Update"),
            (typeof(CharacterTarget), "LateUpdate"),
            (typeof(StateBadgeView), "Update"),
            (typeof(GlazeButton), "Update"),
            (typeof(PeekLabel), "LateUpdate"),
            (typeof(ProjectRail), "Update"),
            (typeof(UsageLeftGlance), "Update"),
            (typeof(SettingsSheet), "Update"),
        };

        [MenuItem("Halcyonic/Measure the Interface")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = Run();
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Interface measure", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = Run();
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> Run()
        {
            var failures = new List<string>();
            allocations = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                // Once to let the editor settle: it loads and builds what the first scenes need on
                // other threads, and every allocation there would count. Then once for the record.
                foreach (var scene in Scenes()) Measure(scene, warmUp: true).ToList();
                if (!AllocationsCounted()) failures.Add("this editor cannot count allocations, so no frame's bytes can be checked.");
                foreach (var scene in Scenes()) failures.AddRange(Measure(scene, warmUp: false));
                failures.AddRange(LeaningFollowsTheWords());
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                allocations.Dispose();
                FocusGuard.FoldForRender(null);
                WorkspaceRender.KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: interface measure: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: interface measure: every check passed.");
            return failures;
        }

        /// <summary>What a scene builds: its name, and from its root, the panel it opens, if any, and how to show its screen again.</summary>
        private sealed class Scene
        {
            public Scene(string name, Func<GameObject, (GameObject? Panel, Action? ShowAgain)> build)
            {
                Name = name;
                Build = build;
            }

            public string Name { get; }

            public Func<GameObject, (GameObject? Panel, Action? ShowAgain)> Build { get; }
        }

        private static IEnumerable<Scene> Scenes()
        {
            yield return new Scene("the stage and the rail", root =>
            {
                var (state, overview, characters) = Stage(root, besideWindow: false);
                Banner(root, AmbientText.NeedsYouLine(AmbientText.NeedsYou(state)), null, null, besideWindow: false);
                var rail = ProjectRail.ForRender(root.transform, overview, null);
                rail.ResetPosition();
                var glance = UsageLeftGlance.ForRender(rail);
                // A peek showing, as when the person looks at the waiting character.
                var peek = PeekLabel.Create(root.transform);
                var waiting = characters.First(character => character.View.Presentation?.Activity == CharacterActivity.WaitingForHuman);
                peek.Show(waiting.Target, characters.ConvertAll(character => character.Target), PeekCard.Of(WorkspaceRender.Work.Approval("Run make migrate").Present()), 1f,
                    aboveCharacter: false);
                var refresh = (Action)Delegate.CreateDelegate(typeof(Action), rail, typeof(ProjectRail).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!);
                return (rail.Root.gameObject, refresh);
            });
            yield return new Scene("beside a window, another window with focus", root =>
            {
                FocusGuard.FoldForRender(true);
                var (state, _, characters) = Stage(root, besideWindow: true);
                Banner(root, AmbientText.NeedsYouLine(AmbientText.NeedsYou(state)), AmbientText.NotShown(state.Workstreams.Count - characters.Count),
                    AmbientText.StillOpen("Add rate limiting to the sign-in endpoint"), besideWindow: true);
                return (null, null);
            });
            yield return new Scene("the entry panel, Connect projects", root =>
            {
                FocusGuard.FoldForRender(false);
                var (state, overview, characters) = Stage(root, besideWindow: false);
                var entry = EntryPanel.ForRender(root.transform, state, overview, characters.ConvertAll(character => character.Target), null);
                entry.ShowForRender(EntryPanel.Screen.Connect);
                return (entry.Root.gameObject, () => entry.Frame.Show(entry.Frame.Shown!));
            });
            yield return new Scene("a workspace, Waiting for you", root =>
            {
                Stage(root, besideWindow: false);
                var panel = Workspace(root, WorkspaceRender.Work.Approval("Run make migrate").Present());
                var model = panel.Frame.Shown!;
                return (panel.gameObject, () => panel.Show(model, null));
            });
            yield return new Scene("a workspace, Doing", root =>
            {
                Stage(root, besideWindow: false);
                var panel = Workspace(root, WorkspaceRender.Work.Running().Present());
                var model = panel.Frame.Shown!;
                return (panel.gameObject, () => panel.Show(model, null));
            });
            yield return new Scene("Usage left, four windows", root =>
            {
                var (_, overview, characters) = Stage(root, besideWindow: false);
                var rail = ProjectRail.ForRender(root.transform, overview, null);
                rail.ResetPosition();
                var glance = UsageLeftGlance.ForRender(rail);
                glance.ShowForRender(UsageLeftPresenter.Present(FourWindows(), DateTimeOffset.UtcNow, TimeZoneInfo.Utc), characters.ConvertAll(character => character.Target), null);
                rail.Root.gameObject.SetActive(false);
                return (glance.Panel.gameObject, () => glance.Frame.Show(glance.Frame.Shown!));
            });
            yield return new Scene("Settings", root =>
            {
                var (_, overview, characters) = Stage(root, besideWindow: false);
                var rail = ProjectRail.ForRender(root.transform, overview, null);
                rail.ResetPosition();
                var sheet = SettingsSheet.On(rail.gameObject);
                var room = sheet.Section(SettingsText.YourRoom, 0);
                room.Say("No free desk or table in reach, so your agents stand in front of you.");
                room.Offer(room.Button("Space switch", ButtonRole.Secondary), "Show a virtual space");
                var arrangement = sheet.Continuation(room);
                arrangement.Say(SettingsText.Arrangement(StageArrangement.InFront));
                arrangement.Offer(arrangement.Button("Arrangement 0", ButtonRole.Secondary), SettingsText.ChangeTo(StageArrangement.TurnedAside));
                arrangement.Offer(arrangement.Button("Arrangement 1", ButtonRole.Secondary), SettingsText.ChangeTo(StageArrangement.BesideAWindow));
                var mac = sheet.Section(SettingsText.YourMac, 1);
                mac.Say("Paired with " + HostText.Your + " at 192.168.1.23:47801. Connecting over Wi-Fi.");
                mac.Offer(mac.Button("Pairing", ButtonRole.Destructive), "Forget this " + HostText.Noun);
                sheet.OpenForRender(characters.ConvertAll(character => character.Target), null);
                rail.Root.gameObject.SetActive(false);
                return (sheet.Root.gameObject, null);
            });
        }

        /// <summary>
        /// The portfolio's characters as the stage stands them, in front or beside a window, with the
        /// stage's lineup choosing which and the stage's presenter drawing them.
        /// </summary>
        private static (ClientProjection State, WorkOverview Overview, List<(CharacterView View, CharacterTarget Target)> Characters) Stage(GameObject root, bool besideWindow)
        {
            var eyes = new Vector3(0f, EyeHeight, 0f);
            var state = EntryRender.Portfolio(hostile: false, needsYouNow: true);
            var lineup = new CharacterLineup(besideWindow ? CharacterStage.WindowCapacity : 6);
            lineup.Update(state.Workstreams.Values);
            var shown = lineup.Slots.Where(id => id != null).Select(id => CharacterPresenter.Present(state.Workstreams[id!], state, live: true)).ToList();
            var characters = WorkspaceRender.Lineup(root.transform, eyes, CharacterStage.DefaultDistance, null, (_, slot) => shown[slot], besideWindow: besideWindow);
            var overview = WorkOverview.Of(state, new StageVisibility(), id => lineup.SlotOf(id) >= 0);
            return (state, overview, characters);
        }

        /// <summary>The stage's banner where the stage hangs it, under the labels or under the window's lane.</summary>
        private static void Banner(GameObject root, string? waiting, string? notShown, string? stillOpen, bool besideWindow)
        {
            var radius = CharacterStage.DefaultDistance;
            var holder = new GameObject("Banner").transform;
            holder.SetParent(root.transform, false);
            var top = besideWindow ? CharacterStage.BannerTopBesideWindow(radius) : CharacterStage.BannerTop(radius, CharacterStage.DefaultHeightFromEyes);
            holder.SetPositionAndRotation(new Vector3(0f, EyeHeight + top, radius), Quaternion.identity);
            holder.localScale = Vector3.one * radius;
            StageBanner.Create(holder).Show("Connected to " + HostText.Your, BannerKind.Live, waiting, null, notShown, stillOpen);
        }

        /// <summary>A workspace at touch distance in front of the eyes, its screen as the director builds it.</summary>
        private static WorkspacePanel Workspace(GameObject root, WorkspacePresentation workspace)
        {
            var holder = new GameObject("Workspace").transform;
            holder.SetParent(root.transform, false);
            var forward = Quaternion.Euler(25f, 0f, 0f) * Vector3.forward;
            holder.SetPositionAndRotation(new Vector3(0f, EyeHeight, 0f) + forward * PanelFrame.Distance, Quaternion.LookRotation(forward, Vector3.up));
            holder.localScale = Vector3.one * PanelFrame.Distance;
            var panel = WorkspacePanel.Create(holder);
            panel.Show(WorkspaceScreens.Screen(workspace, WorkspaceRender.Steering(), new WorkspaceScreen()), null);
            return panel;
        }

        /// <summary>Two agents' two windows each, the five-hour one seen minutes ago, the weekly one a day ago.</summary>
        private static UsageLimitsResponse FourWindows()
        {
            var now = DateTimeOffset.UtcNow;
            string At(TimeSpan offset) => now.Add(offset).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            UsageLimit Reading(string label, UsageLimitWindow window, double used) => new UsageLimit
            {
                Agent = label.ToLowerInvariant(),
                Label = label,
                Window = window,
                UsedPercent = used,
                ObservedAt = At(TimeSpan.FromMinutes(window == UsageLimitWindow.Rolling5h ? -12 : -1440)),
                ResetsAt = At(TimeSpan.FromHours(window == UsageLimitWindow.Rolling5h ? 2 : 100)),
                Freshness = UsageLimitFreshness.Fresh,
                Account = new UsageLimitAccount(),
            };
            return new AvailableUsageLimits
            {
                Source = new EvaluationSource { System = "seorak", Synthetic = false, ApiVersion = "v1" },
                Complete = true,
                Readings = new List<UsageLimit>
                {
                    Reading("Claude", UsageLimitWindow.Rolling5h, 40.2),
                    Reading("Claude", UsageLimitWindow.Weekly, 61.5),
                    Reading("Codex", UsageLimitWindow.Rolling5h, 7.9),
                    Reading("Codex", UsageLimitWindow.Weekly, 97.4),
                },
            };
        }

        /// <param name="warmUp">A pass only to let the editor settle: nothing logged or checked.</param>
        private static IEnumerable<string> Measure(Scene scene, bool warmUp)
        {
            var failures = new List<string>();
            var root = new GameObject("Interface measure");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var camera = WorkspaceRender.MakeCamera(root.transform, new Vector3(0f, EyeHeight, 0f), texture);
                var (panel, showAgain) = scene.Build(root);
                WorkspaceRender.ForceMeshes(root);
                UnityEngine.Object.DestroyImmediate(WorkspaceRender.Render(camera, texture));
                if (warmUp)
                {
                    showAgain?.Invoke();
                    EachFrame(scene.Name, root, log: false).ToList();
                    return failures;
                }

                var all = Draws.Of(root);
                Debug.Log("Halcyonic: interface measure: " + scene.Name + ": " + all + ".");
                if (all.Calls > SceneBudget) failures.Add(scene.Name + ": everything showing draws up to " + all.Calls + " times, over ADR 0023's " + SceneBudget + ".");
                if (panel != null)
                {
                    var own = Draws.Of(panel);
                    Debug.Log("Halcyonic: interface measure: " + scene.Name + ", its panel alone: " + own + ".");
                    if (own.Calls > PanelBudget) failures.Add(scene.Name + ": the panel draws up to " + own.Calls + " times, over ADR 0023's " + PanelBudget + ".");
                }
                if (showAgain != null) failures.AddRange(ShownAgain(scene.Name, showAgain));
                failures.AddRange(EachFrame(scene.Name, root));
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
        /// A line whose words stay the same but which becomes the agent's, or stops being, leans or
        /// stands upright as soon as it is drawn: its mesh is built again for the lean alone, though
        /// nothing else about it changed and an unchanged label keeps its mesh. Each screen is drawn
        /// once before its line is looked at, as the headset draws it; nothing forces the meshes.
        /// </summary>
        private static IEnumerable<string> LeaningFollowsTheWords()
        {
            var failures = new List<string>();
            var root = new GameObject("Interface measure leaning");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var holder = new GameObject("Panel").transform;
                holder.SetParent(root.transform, false);
                var forward = Quaternion.Euler(18f, 0f, 0f) * Vector3.forward;
                holder.SetPositionAndRotation(eyes + forward * PanelFrame.Distance, Quaternion.LookRotation(forward, Vector3.up));
                holder.localScale = Vector3.one * PanelFrame.Distance;
                var frame = PanelFrame.Create(holder, "Frame");
                foreach (var claim in new[] { true, false, true })
                {
                    var model = new PanelModel("Leaning");
                    model.Rows.Add(new PanelRow { Line = true, Title = "I added the rate limiter and ran the tests.", Claim = claim });
                    frame.Show(model);
                    UnityEngine.Object.DestroyImmediate(WorkspaceRender.Render(camera, texture));
                    var (label, _) = frame.ShownLines[0];
                    var shear = Shear(label);
                    Debug.Log("Halcyonic: interface measure: a line " + (claim ? "of the agent's words" : "of Halcyonic's") + " leans by "
                        + shear.ToString("0.000", CultureInfo.InvariantCulture) + " of its height.");
                    if (claim != shear > 0.1f) failures.Add("a line that " + (claim ? "became the agent's words does not lean" : "stopped being the agent's words still leans") + " though its words stayed the same.");
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

        /// <summary>How far a line's first letter's top stands right of its foot, for each unit of its height, as its mesh is now.</summary>
        private static float Shear(TMP_Text label)
        {
            var info = label.textInfo;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible) continue;
                var vertices = info.meshInfo[character.materialReferenceIndex].vertices;
                var foot = vertices[character.vertexIndex];
                var top = vertices[character.vertexIndex + 1];
                return (top.x - foot.x) / Mathf.Max(top.y - foot.y, 1e-6f);
            }
            return 0f;
        }

        /// <summary>
        /// Shows the same screen again, as panels do every half second while they show: how many text
        /// meshes are built again, how long it takes in the editor and what it allocates.
        /// </summary>
        private static IEnumerable<string> ShownAgain(string what, Action showAgain)
        {
            var rebuilt = 0;
            var names = new SortedSet<string>();
            void Count(UnityEngine.Object text)
            {
                rebuilt++;
                if (text != null) names.Add(text.name);
            }
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(Count);
            try
            {
                showAgain();
                rebuilt = 0;
                names.Clear();
                var bytes = long.MaxValue;
                var milliseconds = double.MaxValue;
                for (var repeat = 0; repeat < Repeats; repeat++)
                {
                    var before = Allocated();
                    var clock = Stopwatch.StartNew();
                    for (var show = 0; show < Shows; show++) showAgain();
                    clock.Stop();
                    bytes = Math.Min(bytes, Allocated() - before);
                    milliseconds = Math.Min(milliseconds, clock.Elapsed.TotalMilliseconds);
                    System.Threading.Thread.Sleep(2);
                }
                rebuilt /= Repeats;
                Debug.Log("Halcyonic: interface measure: " + what + ", shown again: " + Per(rebuilt, Shows) + " text meshes built again, "
                    + (milliseconds / Shows).ToString("0.00", CultureInfo.InvariantCulture) + " ms and "
                    + (bytes / Shows).ToString(CultureInfo.InvariantCulture) + " bytes each time in the editor.");
                if (rebuilt > 0) return new[] { what + ": showing the same screen again builds " + Per(rebuilt, Shows) + " text meshes again: " + string.Join(", ", names) + "." };
                return Array.Empty<string>();
            }
            finally
            {
                TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(Count);
            }
        }

        /// <summary>Runs every per-frame method of the parts under <paramref name="root"/> for <see cref="Frames"/> frames: what each kind allocates a frame.</summary>
        private static IEnumerable<string> EachFrame(string what, GameObject root, bool log = true)
        {
            var failures = new List<string>();
            var kinds = new List<(string Name, int Count, Action Frame)>();
            foreach (var (type, name) in PerFrame)
            {
                var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (method == null)
                {
                    failures.Add(what + ": " + type.Name + " has no " + name + " to measure.");
                    continue;
                }
                var parts = root.GetComponentsInChildren(type, false).Where(part => part is Behaviour behaviour && behaviour.isActiveAndEnabled).ToList();
                if (parts.Count == 0) continue;
                var calls = parts.ConvertAll(part => (Action)Delegate.CreateDelegate(typeof(Action), part, method));
                kinds.Add((type.Name + "." + name, parts.Count, () =>
                {
                    foreach (var call in calls) call();
                }));
            }
            // Twice first, so what is built once, as a cached list, is not counted as each frame's.
            foreach (var kind in kinds)
            {
                kind.Frame();
                kind.Frame();
            }
            var report = new List<string>();
            foreach (var (name, count, frame) in kinds)
            {
                var bytes = long.MaxValue;
                for (var repeat = 0; repeat < Repeats; repeat++)
                {
                    var before = Allocated();
                    for (var index = 0; index < Frames; index++) frame();
                    bytes = Math.Min(bytes, Allocated() - before);
                    System.Threading.Thread.Sleep(2);
                }
                report.Add(name + " ×" + count + " " + (bytes / (double)Frames).ToString("0.#", CultureInfo.InvariantCulture) + " B");
                if (bytes > 0) failures.Add(what + ": " + name + ", on " + count + " parts, allocates " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes over " + Frames + " frames.");
            }
            if (log) Debug.Log("Halcyonic: interface measure: " + what + ", each frame: " + string.Join(", ", report) + ".");
            return failures;
        }

        /// <summary>What draws under a part: renderers with something to draw, draw calls at most, text labels, characters, vertices and triangles.</summary>
        private readonly struct Draws
        {
            private Draws(int renderers, int calls, int labels, int characters, int vertices, int triangles, int empty)
            {
                Renderers = renderers;
                Calls = calls;
                Labels = labels;
                Characters = characters;
                Vertices = vertices;
                Triangles = triangles;
                Empty = empty;
            }

            public int Renderers { get; }

            /// <summary>One for each material of each renderer that draws: what the GPU is asked at most, before batching.</summary>
            public int Calls { get; }

            public int Labels { get; }

            public int Characters { get; }

            public int Vertices { get; }

            public int Triangles { get; }

            /// <summary>Surfaces that draw nothing, as a meter's track never filled: drawn all the same.</summary>
            public int Empty { get; }

            public static Draws Of(GameObject part)
            {
                int renderers = 0, calls = 0, labels = 0, characters = 0, vertices = 0, triangles = 0, empty = 0;
                foreach (var renderer in part.GetComponentsInChildren<Renderer>(false))
                {
                    if (!renderer.enabled) continue;
                    Mesh? mesh = null;
                    if (renderer is SkinnedMeshRenderer skinned) mesh = skinned.sharedMesh;
                    else if (renderer.TryGetComponent<MeshFilter>(out var filter)) mesh = filter.sharedMesh;
                    var isText = renderer.TryGetComponent<TMP_Text>(out var text);
                    if (isText) mesh = text.mesh;
                    if (renderer is MeshRenderer && (mesh == null || mesh.vertexCount == 0)) continue;
                    renderers++;
                    calls += Math.Max(1, renderer.sharedMaterials.Length);
                    if (isText)
                    {
                        labels++;
                        characters += text.textInfo.characterCount;
                    }
                    if (renderer.TryGetComponent<Surface>(out var surface) && (surface.Size.x <= 0f || surface.Size.y <= 0f)) empty++;
                    if (mesh == null) continue;
                    vertices += mesh.vertexCount;
                    for (var sub = 0; sub < mesh.subMeshCount; sub++) triangles += (int)mesh.GetIndexCount(sub) / 3;
                }
                return new Draws(renderers, calls, labels, characters, vertices, triangles, empty);
            }

            public override string ToString() =>
                Renderers + " renderers, up to " + Calls + " draw calls, " + Labels + " text labels with " + Characters + " characters, "
                + Vertices + " vertices, " + Triangles + " triangles" + (Empty > 0 ? ", " + Empty + " surfaces drawn with no size" : "");
        }

        private static string Per(int total, int times) => ((float)total / times).ToString("0.#", CultureInfo.InvariantCulture);

        /// <summary>
        /// What the managed heap has handed out this frame so far, by Unity's own count: Unity's Mono
        /// counts no thread's allocations, and its heap's size moves only when it collects.
        /// </summary>
        private static long Allocated() => allocations.CurrentValue;

        /// <summary>Whether this editor counts allocations at all: a small array allocated must show.</summary>
        private static bool AllocationsCounted()
        {
            var before = Allocated();
            var kept = new byte[256];
            var counted = Allocated() - before;
            Debug.Log("Halcyonic: interface measure: allocating " + kept.Length + " bytes counts " + counted.ToString(CultureInfo.InvariantCulture) + ".");
            return counted >= kept.Length;
        }
    }
}
