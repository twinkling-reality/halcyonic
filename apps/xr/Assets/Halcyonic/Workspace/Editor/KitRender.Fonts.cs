#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The room's faces drawn whole (the owner's yes, 2026-10-30; validation/fonts-2026-10-30.md; built by KitFontAtlas): each static atlas on an
    /// opaque carrier at the size its role takes, at both text sizes, every glyph from the face itself, never its fallback.
    /// The words are synthetic samples.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] FontStates = { "22-fonts" };

        /// <summary>Each face, the em it is drawn at in degrees, and a sample of what it will carry.</summary>
        private static readonly (string Face, float Degrees, string Sample)[] FontSamples =
        {
            ("KitTitle", 1.625f, "Tower Toss \u00B7 Ready"),
            ("KitReading", 1.125f, "Codex asks to run a command \u00B7 10:42 \u00B7 \u201CYes\u201D \u2014 0123456789"),
            ("KitReadingStrong", 1.125f, "Yes, Run It \u00B7 Read It \u00B7 Steps"),
            ("KitMono", 1f, "npm install matter-js --save-dev # 0O 1lI \u2026"),
        };

        private static Shot FontShot(string state, Transform root, Vector3 eyes)
        {
            var shot = new Shot { Caption = "The room's faces, each drawn whole from its own atlas, synthetic samples" };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var view = new GameObject("Faces").transform;
            view.SetParent(root, false);
            var carrier = KitCarrier.Create(view, "Carrier");
            var width = 2f * KitType.Units(40f / 2f);
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var y = -pad;
            var labels = new List<(string Face, TMP_FontAsset Asset, TextMeshPro Label)>();
            foreach (var (face, degrees, sample) in FontSamples)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Halcyonic/UI/Resources/HalcyonicUI/" + face + ".asset");
                Require(asset != null, face + " has no atlas");
                if (asset == null) continue;
                var label = KitType.Create(view, face, KitRole.Body, KitType.Text, KitType.Order.Words);
                label.font = asset;
                label.fontSharedMaterial = asset.material;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                GlazeText.SetLiteral(label, sample);
                label.fontSize = GlazeTokens.FontSize(KitType.Units(degrees * GlazeText.Scale));
                var (lines, _, height) = KitType.Lay(label, width - 2f * pad, 1);
                Require(lines == 1 && !label.isTextTruncated, face + "'s sample is cut");
                label.transform.localPosition = new Vector3(-width / 2f + pad, y, -0.0005f);
                y -= height + KitType.Units(SpatialKit.GridDegrees * 2f);
                labels.Add((face, asset, label));
            }
            var size = new Vector2(width, -y + pad);
            carrier.Draw(size, KitType.Units(SpatialKit.ControlRadiusDegrees), SpatialKit.Carrier);
            carrier.transform.localPosition = new Vector3(0f, -size.y / 2f, 0.0002f);
            view.GetChild(0).SetAsFirstSibling();
            foreach (var each in labels) each.Label.transform.localPosition += new Vector3(0f, size.y / 2f, 0f);
            carrier.transform.localPosition += new Vector3(0f, size.y / 2f, 0f);

            shot.Elevation = 0f;
            shot.Centre = Place(view, eyes, 0f, shot.Elevation);
            var carriers = new[] { carrier };
            Stand(shot, view, eyes, carriers, size);
            shot.Faces.Add(carrier.Face);
            shot.Witness = (carrier.Face, new Vector3(-0.47f, -0.45f, 0f), SpatialKit.Carrier);
            shot.OpacityNegative = carrier.Face;
            // Every glyph from the face itself, never its fallback; real bounds.
            foreach (var (face, asset, label) in labels)
            {
                label.ForceMeshUpdate(true);
                var info = label.textInfo;
                var drawn = info.characterInfo.Take(info.characterCount).Where(character => character.isVisible).ToList();
                Require(drawn.Count > 0 && drawn.All(character => character.fontAsset == asset),
                    face + " drew a glyph from its fallback or none at all");
                Require(label.textBounds.size.x > 0f, face + " has no bounds");
            }
            FacesTheEyes(shot, view, carriers, eyes);
            Debug.Log("Halcyonic: spatial kit: faces " + string.Join(", ", labels.Select(each => each.Face + " " + each.Label.textInfo.characterCount + " glyphs")) + ".");
            return shot;
        }
    }
}
