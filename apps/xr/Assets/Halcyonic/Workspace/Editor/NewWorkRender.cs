#nullable enable
using System;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>Renders and measures the longest line and the page controls of the new work review.</summary>
    public static class NewWorkRender
    {
        private const int Pixels = 1024;
        private const float ButtonHeight = 0.058f;

        [MenuItem("Halcyonic/Check New Work Review")]
        public static void Check()
        {
            var root = new GameObject("New work review render");
            var texture = new RenderTexture(Pixels, Pixels, 24, RenderTextureFormat.ARGB32);
            var white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            white.SetPixel(0, 0, Color.white);
            white.Apply();
            var sprite = Sprite.Create(white, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            try
            {
                var plate = new GameObject("Plate");
                plate.transform.SetParent(root.transform, false);
                plate.transform.localPosition = new Vector3(0f, 0f, 0.01f);
                plate.transform.localScale = new Vector3(NewWorkPanel.Width, NewWorkPanel.Height, 1f);
                var plateRenderer = plate.AddComponent<SpriteRenderer>();
                plateRenderer.sprite = sprite;
                plateRenderer.color = new Color(0.06f, 0.08f, 0.11f, 1f);
                plateRenderer.sortingOrder = 10;

                var labelObject = new GameObject("Full request");
                labelObject.transform.SetParent(root.transform, false);
                var label = labelObject.AddComponent<TextMeshPro>();
                label.rectTransform.pivot = new Vector2(0f, 1f);
                label.rectTransform.sizeDelta = new Vector2(NewWorkPanel.ReviewWidth, NewWorkPanel.ReviewHeight);
                label.rectTransform.localPosition = new Vector3(-NewWorkPanel.ReviewWidth / 2f, 0.32f, -0.003f);
                label.fontSize = NewWorkPanel.ReviewSize;
                label.richText = false;
                label.parseCtrlCharacters = true;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                label.color = Color.white;
                label.sortingOrder = 12;

                var reviews = new[]
                {
                    new NewWorkReview(new string('P', 200), "Workstream", "OpenCode", "Local model",
                        "on this Mac, tools declared", "ollama/local:latest", new string('W', 4000)),
                    new NewWorkReview("Project", "Workstream", "OpenCode", "Local model",
                        "on this Mac, tools declared", "ollama/local:latest", new string('W', 24) + " 中かな🙂 \\u{4E2D}"),
                };
                foreach (var page in reviews.SelectMany(review => review.Pages))
                {
                    foreach (var line in page.Split('\n'))
                    {
                        if (line.Length > NewWorkReview.LineCharacters)
                            throw new InvalidOperationException("A review line exceeded its character limit.");
                        if (label.GetPreferredValues(line.Replace("\\", "\\\\")).x > NewWorkPanel.ReviewWidth)
                            throw new InvalidOperationException("A review line draws past the panel: " + line);
                    }
                    if (label.GetPreferredValues(page.Replace("\\", "\\\\")).y > NewWorkPanel.ReviewHeight)
                        throw new InvalidOperationException("A review page is too tall for the panel.");
                }
                label.text = reviews[1].Pages.First(page => page.Contains(new string('W', NewWorkReview.LineCharacters)))
                    .Replace("\\", "\\\\");
                label.ForceMeshUpdate();
                Button(root.transform, "Previous part", -0.20f, -0.34f);
                Button(root.transform, "Next part", 0.20f, -0.34f);
                Button(root.transform, "Change choices", -0.20f, -0.43f);
                Button(root.transform, "Yes, start work", 0.20f, -0.43f);
                BottomButton(root.transform, "Close new work", -0.20f);
                BottomButton(root.transform, "Move right", 0.20f);

                var cameraObject = new GameObject("Review camera");
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, -1.3f), Quaternion.identity);
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 0.69f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.targetTexture = texture;
                camera.Render();
                var prior = RenderTexture.active;
                RenderTexture.active = texture;
                var image = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, Pixels, Pixels), 0, 0);
                image.Apply();
                RenderTexture.active = prior;
                var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "WorkspaceRenders", "new-work-review.png"));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
                Debug.Log("Halcyonic: new work review render passed: " + path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(white);
            }
        }

        private static void Button(Transform parent, string text, float x, float y)
        {
            const float width = 0.27f;
            if (Mathf.Abs(x) + width / 2f > NewWorkPanel.Width / 2f ||
                Mathf.Abs(y) + ButtonHeight / 2f > NewWorkPanel.Height / 2f)
                throw new InvalidOperationException("A review control lies outside the panel.");
            var button = PanelButton.Create(parent, text, height: ButtonHeight, textSize: 0.20f);
            button.Show(text, new Vector2(x, y), width);
        }

        private static void BottomButton(Transform parent, string text, float x)
        {
            const float width = 0.30f;
            const float y = -0.56f;
            if (Mathf.Abs(x) + width / 2f > NewWorkPanel.Width / 2f ||
                y + ButtonHeight / 2f >= -NewWorkPanel.Height / 2f)
                throw new InvalidOperationException("A bottom control overlaps the panel.");
            var button = PanelButton.Create(parent, text, height: ButtonHeight, textSize: 0.20f);
            button.Show(text, new Vector2(x, y), width);
        }
    }
}
