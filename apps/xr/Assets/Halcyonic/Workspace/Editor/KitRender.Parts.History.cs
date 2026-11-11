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
    /// History and Restore's cards (gap 2; FIELD's A14 states in A17's card language, <see cref="HistoryColumn"/>) drawn
    /// by FIELD's card view (<see cref="RoomCardView"/>) on the information panel, at both text sizes, over white, the dark
    /// room and the bright room. Each state's card is made by the column itself from synthetic versions and reviews, so
    /// what is drawn is what the headset would show; each is measured as the card states of KitRender.Parts.Card.cs are
    /// (no word cut or joined on a dot, every block inside the card's column, a row's tile at the left and its note at the
    /// right) and read in the pixels (every label's words, every mark's ink). The column's actions are not drawn: the
    /// column beside the panel is XR's. In batch mode: -executeMethod Halcyonic.XR.Workspace.Editor.KitRender.HistoryCheck;
    /// it exits 1 when a check fails. Images go to apps/xr/Builds/PartsRenders/History, which git ignores.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] HistoryStates =
        {
            "60-history-versions", "60-history-versions-page", "60-history-none", "60-history-not-set-up", "60-history-unreadable",
            "60-history-review", "60-history-review-many", "60-history-review-outside", "60-history-add", "60-history-done", "60-history-incomplete",
            "60-history-stale", "60-history-refused",
        };

        [MenuItem("Halcyonic/Render History's Cards")]
        public static void HistoryMenu() => RunHistory(exitWhenDone: false);

        /// <summary>History's one entry point in batch mode: renders and checks every state's card, then exits 1 on any failure.</summary>
        public static void HistoryCheck() => RunHistory(exitWhenDone: true);

        private static void RunHistory(bool exitWhenDone)
        {
            var failures = new List<string>();
            var previousField = ViewField.Current;
            var previousStill = GlazeMotion.Still;
            try
            {
                failures.AddRange(GlazeChecks.AtEachTextSize(variant =>
                {
                    var size = variant.Length == 0 ? "Standard" : variant;
                    var found = new List<string>();
                    foreach (var state in HistoryStates)
                    {
                        try { found.AddRange(RenderHistory(state, size)); }
                        catch (Exception error) { found.Add(state + " threw " + error); }
                    }
                    return found;
                }));
            }
            catch (Exception error) { failures.Add("the history render threw " + error); }
            finally
            {
                ViewField.Current = previousField;
                GlazeMotion.Still = previousStill;
                KeepFontsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: history: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: history: every check passed.");
            if (exitWhenDone) EditorApplication.Exit(failures.Count > 0 ? 1 : 0);
        }

        /// <summary>The column standing in <paramref name="state"/>, from synthetic versions of a synthetic project.</summary>
        private static HistoryColumn HistoryOf(string state)
        {
            const string project = "0193b3a0-0000-7000-8000-0000000000a1";
            string Id(int n) => "0193b3a0-0000-7000-8000-" + n.ToString("x12", CultureInfo.InvariantCulture);
            var now = new DateTimeOffset(2026, 11, 11, 18, 50, 0, TimeSpan.Zero);
            var task = new WorkstreamView
            {
                WorkstreamId = "w-tail", ProjectId = project, Title = "Tail", Status = WorkstreamStatus.Completed,
                Attention = new Attention { Level = AttentionLevel.None }, CreatedAt = "2026-11-11T18:00:00.000Z", UpdatedAt = "2026-11-11T18:40:00.000Z",
            };
            var execution = new ExecutionView
            {
                ExecutionId = "e-tail", WorkstreamId = "w-tail", ProjectId = project, Instruction = "Add a tail.", Status = ExecutionStatus.Completed,
                Runtime = new RuntimeRef { RuntimeId = "mock", Kind = "mock", DisplayName = "Mock", Synthetic = true },
            };
            var projection = new ClientProjection();
            projection.ApplySnapshot(new Snapshot
            {
                Journal = new JournalInfo { JournalId = "0193b3a0-0000-7000-8000-0000000000f1", Origin = JournalOrigin.Live },
                Position = 1,
                Projects = new List<ProjectView>(),
                Workstreams = new List<WorkstreamView> { task },
                Executions = new List<ExecutionView> { execution },
                Commands = new List<CommandView>(),
                Runtimes = new List<RuntimeDescriptor>(),
            }, new StateChanges());
            CheckpointView Version(int n, CheckpointReason reason, string at) => new CheckpointView
            {
                CheckpointId = Id(n), ProjectId = project, Reason = reason, ExecutionId = reason == CheckpointReason.BeforeRestore ? null : "e-tail",
                RecordedAt = at, Tree = new string('b', 64), Files = 12,
            };
            var versions = new List<CheckpointView>
            {
                Version(1, CheckpointReason.BeforeTask, "2026-11-11T18:12:00.000Z"),
                Version(2, CheckpointReason.AfterTurn, "2026-11-11T18:40:00.000Z"),
                Version(3, CheckpointReason.BeforeRestore, "2026-11-10T09:12:00.000Z"),
            };
            if (state == "60-history-add")
                versions.Add(Version(11, CheckpointReason.BeforeAdd, "2026-11-11T18:45:00.000Z"));
            if (state == "60-history-versions-page")
                for (var n = 4; n <= 9; n++) versions.Add(Version(n, CheckpointReason.AfterTurn, "2026-11-09T1" + n.ToString(CultureInfo.InvariantCulture) + ":05:00.000Z"));
            var column = new HistoryColumn(project, "Kite Run");
            switch (state)
            {
                case "60-history-not-set-up":
                    column.Listed(new CheckpointsResponse { ProjectId = project, Result = new UnavailableCheckpoints { Reason = new ErrorInfo { Code = "undo_unavailable", Message = "Off." } } },
                        projection, now, TimeZoneInfo.Utc);
                    return column;
                case "60-history-unreadable":
                    column.ReadFailed();
                    return column;
                case "60-history-none":
                    column.Listed(new CheckpointsResponse { ProjectId = project, Result = new AvailableCheckpoints() }, projection, now, TimeZoneInfo.Utc);
                    return column;
            }
            column.Listed(new CheckpointsResponse { ProjectId = project, Result = new AvailableCheckpoints { Checkpoints = versions } }, projection, now, TimeZoneInfo.Utc);
            if (state.StartsWith("60-history-versions", StringComparison.Ordinal)) return column;
            var chosen = state == "60-history-add" ? Id(11) : Id(1);
            column.Choose(chosen);
            PathList Paths(params string[] paths) => new PathList { Paths = paths.ToList(), Total = paths.Length };
            var changedBack = state == "60-history-review-many"
                ? Paths("src/kite.js", "src/score.js", "src/wind.js", "src/sky/clouds.js", "src/sky/sun.js", "src/hud.js", "styles/main.css")
                : Paths("src/kite.js", "src/score.js");
            column.Previewed(new RestorePreviewResponse
            {
                ProjectId = project,
                Result = new AvailableRestorePreview
                {
                    Preview = new RestorePreview
                    {
                        CheckpointId = chosen, ChangedBack = changedBack, Removed = Paths("src/tail.js"), Returned = Paths("notes/plan.md"),
                        ProjectHeadMoved = state == "60-history-review-outside",
                    },
                    Review = new string('a', 64),
                },
            });
            if (state.StartsWith("60-history-review", StringComparison.Ordinal) || state == "60-history-add") return column;
            column.Arm();
            var command = column.Restore(new CommandFactory(new ClientInfo { Name = "history-render", Version = "0" }))!;
            CommandView Rejected(RejectionCode code) => new CommandView
            {
                CommandId = command.CommandId, Status = CommandStatus.Rejected, Rejection = new CommandRejection { Code = code, Message = "Synthetic." },
            };
            switch (state)
            {
                case "60-history-done":
                    column.Restored(new ProjectRestoredEvent
                    {
                        ProjectId = project,
                        Payload = new ProjectRestoredPayload
                        {
                            CheckpointId = Id(1), BeforeRestoreCheckpointId = Id(10), Tree = new string('c', 64),
                            ChangedBack = Paths("src/kite.js", "src/score.js"), Removed = Paths("src/tail.js"), Returned = Paths("notes/plan.md"),
                        },
                    });
                    break;
                case "60-history-incomplete":
                    column.RestoreIncomplete(new ProjectRestoreIncompleteEvent
                    {
                        ProjectId = project,
                        Payload = new ProjectRestoreIncompletePayload
                        {
                            CheckpointId = Id(1), Written = true, NotRestored = Paths("src/score.js"), Failure = new ErrorInfo { Code = "unwritable", Message = "Synthetic." },
                        },
                    });
                    break;
                case "60-history-stale":
                    column.Answered(Rejected(RejectionCode.ReviewStale));
                    break;
                case "60-history-refused":
                    column.Answered(Rejected(RejectionCode.ProjectNotFound));
                    break;
            }
            return column;
        }

        private static List<string> RenderHistory(string state, string size)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ViewField.Current = Quest3S;
            GlazeMotion.Still = false;
            var name = state + " (" + size + ")";
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "PartsRenders", "History", size));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            var root = new GameObject("History: " + state);
            var fixture = new GameObject("Fixture camera");
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var eyes = new Vector3(0f, EyeHeight, 0f);
            try
            {
                var column = HistoryOf(state);
                // Reading and sent show only the column's sentence, which is XR's: every state drawn here has a card.
                var card = column.Card() ?? throw new InvalidOperationException(state + " has no card: " + column.Sentence);
                var shot = HistoryShot(card, root.transform, eyes, name);
                var expected = state switch
                {
                    "60-history-versions" or "60-history-versions-page" or "60-history-none" => HistoryStage.Versions,
                    "60-history-not-set-up" => HistoryStage.NotSetUp,
                    "60-history-unreadable" => HistoryStage.Unreadable,
                    "60-history-review" or "60-history-review-many" or "60-history-review-outside" or "60-history-add" => HistoryStage.Review,
                    "60-history-done" => HistoryStage.Done,
                    "60-history-incomplete" => HistoryStage.Incomplete,
                    "60-history-stale" => HistoryStage.Stale,
                    _ => HistoryStage.Refused,
                };
                if (column.Stage != expected) shot.Failures.Add("the column stands at " + column.Stage + ", not " + expected);
                failures.AddRange(shot.Failures.Select(failure => name + ": " + failure));
                SetLayer(root, KitLayer);
                ForceMeshes(root);
                failures.AddRange(GlazeChecks.TextAsSeen(root, eyes, name));
                var camera = MakeCamera(fixture.transform, eyes, target, 0f, shot.Elevation);
                SetLayer(fixture, KitLayer);
                var image = Render(camera, target);
                Texture2D? white = null, dark = null, bright = null;
                try
                {
                    white = Composite(image, null, Color.white);
                    dark = Composite(image, null, shot.Dark);
                    bright = Composite(image, BrightRoom(Width, Height, camera), Color.white);
                    if (shot.Read != null)
                    {
                        var read = new List<string>();
                        shot.Read(camera, image.GetPixels32(), white.GetPixels32(), dark.GetPixels32(), bright.GetPixels32(), read);
                        failures.AddRange(read.Select(failure => name + ": " + failure));
                    }
                    File.WriteAllBytes(Path.Combine(folder, state + "-dark.png"), dark.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(folder, state + "-bright.png"), bright.EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(folder, state + "-white.png"), white.EncodeToPNG());
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(image);
                    if (white != null) UnityEngine.Object.DestroyImmediate(white);
                    if (dark != null) UnityEngine.Object.DestroyImmediate(dark);
                    if (bright != null) UnityEngine.Object.DestroyImmediate(bright);
                }
                Debug.Log("Halcyonic: history: " + name + " rendered: " + string.Join(" / ", card.Words()));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(fixture);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
            return failures;
        }

        /// <summary>
        /// One card on the information panel, square to the eyes, measured and read as KitRender.Parts.Card.cs's
        /// <c>CardShot</c> does (its measures, taken whole, for a card given rather than named: a call note asks FIELD for a
        /// card-taking overload so this copy can go).
        /// </summary>
        private static PartShot HistoryShot(RoomCard card, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            Require(!card.AnyDot, "the card joins facts on a dot: " + string.Join(" / ", card.Words()));
            var unit = RoomType.Units(1f);
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var width = RoomType.Units(RoomGeometry.PanelDp);
            var inner = width - 2f * pad;
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var panel = Halcyonic.XR.UI.RoomSurface.Create(plane, "Information panel", RoomSurfaceKind.Information, KitType.Order.Plate);
            var view = RoomCardView.Create(panel.transform);
            var tall = view.Show(card, inner);
            var size = new Vector2(width, tall + 2f * pad);
            panel.InnerBar = null;
            panel.Draw(size);
            view.transform.localPosition = new Vector3(-width / 2f + pad, size.y / 2f - pad, -RoomType.Units(0.5f));
            Place(plane, eyes, 0f, shot.Elevation);

            var slack = unit * 0.05f;
            foreach (var label in view.Shown)
            {
                label.ForceMeshUpdate(true);
                var words = label.GetParsedText();
                Require(label.textInfo.characterCount > 0 && !label.isTextOverflowing && !words.Contains("…"), "\"" + words + "\" is cut");
                Require(!words.Contains(KitMomentWords.Dot.Trim()), "\"" + words + "\" joins facts on a dot");
                var bounds = label.textBounds;
                var left = label.transform.localPosition.x + bounds.min.x;
                var right = label.transform.localPosition.x + bounds.max.x;
                Require(left >= -slack && right <= inner + slack, "\"" + words + "\" runs past the card's column: " + (left / unit).ToString("0.0") + " to " + (right / unit).ToString("0.0") + " dp");
            }
            foreach (var (plate, what) in view.Plates)
            {
                var x = plate.transform.localPosition.x;
                Require(x - plate.Size.x / 2f >= -slack && x + plate.Size.x / 2f <= inner + slack, what + " runs past the card's column");
            }
            if (card.Slots.Count > 0)
            {
                var slots = view.Plates.Where(each => each.What.StartsWith("slot", StringComparison.Ordinal)).ToList();
                Require(slots.Count > 0, "the slots draw no wells");
            }
            if (card.List.Count > 0)
            {
                var tiles = view.Plates.Where(each => each.What.StartsWith("row tile", StringComparison.Ordinal)).ToList();
                Require(tiles.Count == card.List.Count, "the list draws " + tiles.Count + " tiles for " + card.List.Count + " rows");
                Require(tiles.All(each => Mathf.Abs(each.Plate.transform.localPosition.x - each.Plate.Size.x / 2f) < slack), "a row's tile does not stand at the card's left");
                foreach (var row in card.List)
                {
                    // A name of our words and the person's is laid as runs on one label, which pads the person's run for its
                    // ground: compared without any spacing, every letter of the name must be there, in order.
                    static string Bare(string text) => new string(text.Where(each => !char.IsWhiteSpace(each) && each != '\u200B').ToArray());
                    var nameLabel = view.Shown.FirstOrDefault(label => Bare(label.GetParsedText()) == Bare(row.Name.Text));
                    Require(nameLabel != null, row.Name.Text + " is not shown, whole");
                    if (nameLabel != null)
                        Require(nameLabel.transform.localPosition.x + nameLabel.textBounds.min.x >= RoomType.Scaled(RoomCardView.RowTileDp + RoomCardView.RowGapDp) - unit * 1.5f,
                            row.Name.Text + " touches its tile");
                    if (row.Note == null) continue;
                    var noteLabel = view.Shown.FirstOrDefault(label => label.GetParsedText() == row.Note);
                    if (noteLabel != null)
                        Require(Mathf.Abs(noteLabel.transform.localPosition.x + RoomCursor.WordsWidth(noteLabel, 0f) - inner) < unit * 0.6f, row.Note + " does not end at the card's right");
                }
            }
            if (card.Status is RoomCardStatus status)
                Require(view.Shown.Any(label => label.GetParsedText() == status.Title), "the status's title \"" + status.Title + "\" is not shown");

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                int Light(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                foreach (var label in view.Shown)
                {
                    var bounds = label.textBounds;
                    var most = 0;
                    for (var step = 0; step <= 60; step++)
                        most = Mathf.Max(most, Light(At(dark, label.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 60f), bounds.max.y - KitType.LinePitch(label) * 0.5f, 0f)))));
                    if (most < 110) failures.Add("\"" + label.GetParsedText() + "\" is not in the pixels, the lightest reads " + most + " of 255");
                }
                foreach (var (glyph, what) in view.Glyphs)
                {
                    if (glyph.Glyph is not RoomGlyph shown) continue;
                    var ground = At(dark, glyph.transform.TransformPoint(new Vector3(0.48f, 0.48f, 0f)));
                    var apart = 0;
                    foreach (var shape in RoomGlyphs.Of(shown))
                    foreach (var middle in shape.Middles())
                    {
                        var pixel = At(dark, glyph.transform.TransformPoint(new Vector3(middle.x / RoomGlyphs.Grid - 0.5f, 0.5f - middle.y / RoomGlyphs.Grid, 0f)));
                        apart = Mathf.Max(apart, Mathf.Max(Mathf.Abs(pixel.r - ground.r), Mathf.Max(Mathf.Abs(pixel.g - ground.g), Mathf.Abs(pixel.b - ground.b))));
                    }
                    if (apart < 40) failures.Add(what + "'s mark is not in the pixels: its ink stands " + apart + " of 255 from the ground beside it");
                }
                Debug.Log("Halcyonic: history: " + name + ": a card " + (tall / unit).ToString("0.0", CultureInfo.InvariantCulture) + " dp tall, " + view.Shown.Count + " labels, "
                    + view.Plates.Count + " tiles and marks, " + view.Glyphs.Count + " glyphs.");
            };
            return shot;
        }
    }
}
