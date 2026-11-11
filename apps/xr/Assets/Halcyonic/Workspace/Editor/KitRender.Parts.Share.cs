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
    /// Share's cards (ADR 0054; <see cref="ShareColumn"/>) drawn by FIELD's card view on the information panel, with the
    /// live share's code to scan under the card where a share stands (<see cref="ShareCodeView"/>), at both text sizes,
    /// over white, the dark room and the bright room. The codes are the host's own encoding of two synthetic addresses
    /// (apps/control-plane/src/preview/share-code.ts, written here as cells). Each card is measured as History's are; each
    /// code is read back cell by cell from the pixels over white, and its frame is written alone (<c>*-code.png</c>) so
    /// macOS's own detector can read it outside Unity. In batch mode:
    /// -executeMethod Halcyonic.XR.Workspace.Editor.KitRender.ShareCheck; it exits 1 when a check fails. Images go to
    /// apps/xr/Builds/PartsRenders/Share, which git ignores.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ShareStates =
        {
            "61-share-choose", "61-share-choose-no-link", "61-share-confirm-link", "61-share-confirm-wifi", "61-share-shared-link",
            "61-share-shared-wifi", "61-share-not-shareable", "61-share-not-shared", "61-share-stopped",
        };

        /// <summary>The host's code for "http://192.168.1.20:47970/".</summary>
        private static readonly string[] ShareWifiRows = new[]
            {
                "1111111010111100101111111",
                "1000001000011100001000001",
                "1011101011100110001011101",
                "1011101000101101001011101",
                "1011101001100100101011101",
                "1000001011111000101000001",
                "1111111010101010101111111",
                "0000000000100000000000000",
                "1010001101111011000100101",
                "0010000110000111011001011",
                "1111101011010011111011101",
                "0011110000110001101011000",
                "0001001010111010111000001",
                "0010110000100011001000011",
                "1111001100010101100011101",
                "0011000101011000001000000",
                "1110111010001010111110010",
                "0000000010010011100010101",
                "1111111010111000101011001",
                "1000001000100010100010001",
                "1011101001010011111111010",
                "1011101000010110100111110",
                "1011101010010110100110011",
                "1000001001011011100110000",
                "1111111011010011101001001",
            };

        /// <summary>The host's code for a synthetic link, "https://kite-run-share.example.invalid/".</summary>
        private static readonly string[] ShareLinkRows = new[]
            {
                "11111110011011001111001111111",
                "10000010011000010100101000001",
                "10111010100110111010101011101",
                "10111010101011100100001011101",
                "10111010110110001011101011101",
                "10000010101000111000001000001",
                "11111110101010101010101111111",
                "00000000100110010001000000000",
                "10111110011111010100101111100",
                "00111001010111001101111010001",
                "11001010110011110010101110000",
                "10010001111100101010000111010",
                "00011011111000011100000001100",
                "10000000000011001011101110001",
                "01011010110011110010010011100",
                "00111101110001001000011110010",
                "00010011011100100101000101100",
                "10100001110010001111101110101",
                "10011110001100011000100110100",
                "10101001000000110001000000010",
                "10110011111100000100111110111",
                "00000000101001101001100011111",
                "11111110011011110011101011100",
                "10000010110111001011100010001",
                "10111010101110100100111110100",
                "10111010100110000111000001111",
                "10111010101101111001111111110",
                "10000010001001011010101101010",
                "11111110110100110100011000100",
            };

        [MenuItem("Halcyonic/Render Share's Cards")]
        public static void ShareMenu() => RunShare(exitWhenDone: false);

        /// <summary>Share's one entry point in batch mode: renders and checks every state, then exits 1 on any failure.</summary>
        public static void ShareCheck() => RunShare(exitWhenDone: true);

        private static void RunShare(bool exitWhenDone)
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
                    foreach (var state in ShareStates)
                    {
                        try { found.AddRange(RenderShare(state, size)); }
                        catch (Exception error) { found.Add(state + " threw " + error); }
                    }
                    return found;
                }));
            }
            catch (Exception error) { failures.Add("the share render threw " + error); }
            finally
            {
                ViewField.Current = previousField;
                GlazeMotion.Still = previousStill;
                KeepFontsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: share: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: share: every check passed.");
            if (exitWhenDone) EditorApplication.Exit(failures.Count > 0 ? 1 : 0);
        }

        private static ShareColumn ShareOf(string state)
        {
            const string project = "0193b3a0-0000-7000-8000-0000000000a1";
            const string check = "0193b3a0-0000-7000-8000-0000000000c1";
            var now = new DateTimeOffset(2026, 11, 11, 8, 30, 0, TimeSpan.Zero);
            var client = new ClientInfo { Name = "share-render", Version = "0" };
            ProjectView View(bool passed = true, ProjectShareView? share = null) => new ProjectView
            {
                ProjectId = project, Name = "Kite Run", Starter = "iwsdk-vr", CreatedAt = "2026-11-11T05:00:00.000Z", UpdatedAt = "2026-11-11T05:00:00.000Z",
                Check = new ProjectViewCheck
                {
                    CheckId = check, RecordedAt = "2026-11-11T05:00:00.000Z", Position = 4, Files = ProjectViewCheckFiles.Current,
                    Findings = new CheckFindings { Tsc = new CheckStep(), Build = new CheckStep(), Passed = passed, ReadsConfined = true },
                },
                Preview = new ProjectViewPreview { CheckId = check, Port = 47931, Files = ProjectViewPreviewFiles.Current },
                Share = share,
            };
            ShareCode Code(string[] rows) => new ShareCode { Size = rows.Length, Rows = rows.ToList() };
            switch (state)
            {
                case "61-share-not-shareable":
                    return new ShareColumn(View(passed: false));
                case "61-share-shared-link":
                case "61-share-shared-wifi":
                    var wifi = state.EndsWith("wifi", StringComparison.Ordinal);
                    var live = new ShareColumn(View(share: new ProjectShareView
                    {
                        CheckId = check, Url = wifi ? "http://192.168.1.20:47970/" : "https://kite-run-share.example.invalid/", Way = wifi ? ShareWay.Wifi : ShareWay.Link, Files = ProjectShareViewFiles.Current,
                    }));
                    live.CodeRead(new ShareCodeResponse { CheckId = check, Way = wifi ? ShareWay.Wifi : ShareWay.Link, Code = Code(wifi ? ShareWifiRows : ShareLinkRows) }, live.ShareGeneration);
                    return live;
            }
            var column = new ShareColumn(View());
            column.WaysRead(new SharingWaysResponse { Link = state == "61-share-choose-no-link" ? ShareWayState.NotSetUp : ShareWayState.Ready, Wifi = ShareWayState.Ready });
            if (state.StartsWith("61-share-choose", StringComparison.Ordinal)) return column;
            column.Choose(state == "61-share-confirm-wifi" ? ShareWay.Wifi : ShareWay.Link);
            if (state.StartsWith("61-share-confirm", StringComparison.Ordinal)) return column;
            column.Arm();
            var command = column.Start(client, now)!;
            if (state == "61-share-not-shared")
                column.Answered(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Failed, Failure = new CommandFailure { Code = "share_failed", Message = "Synthetic." } });
            else
            {
                column.Viewed(View(share: new ProjectShareView { CheckId = check, Url = "https://kite-run-share.example.invalid/", Way = ShareWay.Link, Files = ProjectShareViewFiles.Current }));
                column.Viewed(View());
                column.ShareStopped(new ProjectShareStoppedEvent { ProjectId = project, Payload = new ProjectShareStoppedPayload { CheckId = check, Reason = ShareStopReason.Ended } });
            }
            return column;
        }

        private static List<string> RenderShare(string state, string size)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ViewField.Current = Quest3S;
            GlazeMotion.Still = false;
            var name = state + " (" + size + ")";
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "PartsRenders", "Share", size));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            var root = new GameObject("Share: " + state);
            var fixture = new GameObject("Fixture camera");
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var eyes = new Vector3(0f, EyeHeight, 0f);
            try
            {
                var column = ShareOf(state);
                var card = column.Card() ?? throw new InvalidOperationException(state + " has no card: " + column.Sentence);
                var expected = state switch
                {
                    "61-share-choose" => ShareStage.Choose,
                    // With one way ready the choice is skipped to its confirmation (FIELD).
                    "61-share-choose-no-link" => ShareStage.Confirm,
                    "61-share-confirm-link" or "61-share-confirm-wifi" => ShareStage.Confirm,
                    "61-share-shared-link" or "61-share-shared-wifi" => ShareStage.Shared,
                    "61-share-not-shareable" => ShareStage.NotShareable,
                    "61-share-not-shared" => ShareStage.NotShared,
                    _ => ShareStage.Stopped,
                };
                if (column.Stage != expected) failures.Add(name + ": the column stands at " + column.Stage + ", not " + expected);
                var shot = HistoryShot(card, root.transform, eyes, name);
                ShareCodeView? code = null;
                if (column.Cells is ShareCells cells)
                {
                    // The code under the card on the panel's own ground, 60 percent of the card's width, square to the eyes.
                    var unit = RoomType.Units(1f);
                    var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
                    var width = RoomType.Units(RoomGeometry.PanelDp);
                    var plane = root.transform.GetChild(0);
                    var panel = plane.GetComponentInChildren<Halcyonic.XR.UI.RoomSurface>();
                    var cardView = plane.GetComponentInChildren<RoomCardView>();
                    var side = (width - 2f * pad) * 0.6f;
                    var tall = cardView.transform.localPosition.y;
                    code = ShareCodeView.Create(panel.transform, KitType.Order.Plate + 2);
                    code.Show(cells, side);
                    var cardTall = -(cardView.Shown.Count == 0 ? 0f : cardView.Shown.Min(label => label.transform.localPosition.y + label.textBounds.min.y));
                    code.transform.localPosition = new Vector3(0f, tall - cardTall - pad - side / 2f, -RoomType.Units(0.5f));
                    Debug.Log("Halcyonic: share: " + name + ": a code of " + cells.Size + " cells, " + (side / unit).ToString("0.0", CultureInfo.InvariantCulture) + " dp a side with its margin.");
                }
                else if (expected == ShareStage.Shared) failures.Add(name + ": a share stands and no code is drawn");
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
                    if (code != null && column.Cells is ShareCells shown)
                        failures.AddRange(CodeReads(camera, dark.GetPixels32(), code, shown).Select(failure => name + ": " + failure));
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
                Debug.Log("Halcyonic: share: " + name + " rendered: " + string.Join(" / ", card.Words()));
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

        /// <summary>Every cell of the code, margin included, read at its centre in the frame over the dark room: dark cells dark, light cells light.</summary>
        private static List<string> CodeReads(Camera camera, Color32[] pixels, ShareCodeView code, ShareCells cells)
        {
            var failures = new List<string>();
            var drawn = cells.Drawn;
            var cell = code.Side / drawn;
            var wrong = 0;
            var smallest = float.MaxValue;
            for (var row = 0; row < drawn; row++)
            for (var column = 0; column < drawn; column++)
            {
                var local = new Vector3(-code.Side / 2f + (column + 0.5f) * cell, code.Side / 2f - (row + 0.5f) * cell, 0f);
                var screen = camera.WorldToScreenPoint(code.transform.TransformPoint(local));
                var pixel = pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                var light = (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                var r = row - ShareCells.Margin;
                var c = column - ShareCells.Margin;
                var isDark = r >= 0 && r < cells.Size && c >= 0 && c < cells.Size && cells.Dark(r, c);
                if (isDark ? light > 80 : light < 180) wrong++;
                if (row == 0 && column == 1)
                {
                    var first = camera.WorldToScreenPoint(code.transform.TransformPoint(local - new Vector3(cell, 0f, 0f)));
                    smallest = Mathf.Min(smallest, Mathf.Abs(screen.x - first.x));
                }
            }
            if (wrong > 0) failures.Add("the code: " + wrong + " of " + (drawn * drawn) + " cells read wrong in the pixels");
            if (smallest < 3f) failures.Add("the code: a cell is " + smallest.ToString("0.0", CultureInfo.InvariantCulture) + " pixels wide in the frame, under 3");
            Debug.Log("Halcyonic: share: the code's cells are " + smallest.ToString("0.0", CultureInfo.InvariantCulture) + " pixels wide in the frame, " + wrong + " read wrong.");
            return failures;
        }
    }
}
