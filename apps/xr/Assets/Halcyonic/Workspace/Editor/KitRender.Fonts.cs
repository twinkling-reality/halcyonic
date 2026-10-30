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
    /// The room's six faces drawn whole (the owner's yes, 2026-10-30; ART's cuts; validation/fonts-2026-10-30.md; built by KitFontAtlas):
    /// each role on an opaque carrier in the face <see cref="KitType"/> gives it, at both text sizes, every glyph from the face itself,
    /// never its fallback, and outside text in a line standing upright with its run kept. The words are synthetic samples.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] FontStates = { "22-fonts" };

        /// <summary>Each role, and a line of what it will carry: Halcyonic's words, then the outside text it shows, if any.</summary>
        private static readonly (KitRole Role, string Ours, string Theirs)[] FontSamples =
        {
            (KitRole.Display, "Tower Toss", ""),
            (KitRole.Title, "Run Command?", ""),
            (KitRole.Body, "Codex asks \u00B7 10:42 \u00B7 ", "\u201CYes\u201D \u2014 run it 0123456789"),
            (KitRole.Strong, "Run \u00B7 Read Command \u00B7 ", "Tower Toss"),
            (KitRole.Label, "Agent \u00B7 Tower Toss \u00B7 Reported", ""),
            (KitRole.Mono, "", "npm install matter-js --save-dev # 0O 1lI \u2026"),
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
            foreach (var (role, ours, theirs) in FontSamples)
            {
                var face = KitType.FaceName(role);
                var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Halcyonic/UI/Resources/HalcyonicUI/" + face + ".asset");
                Require(asset != null, face + " has no atlas");
                if (asset == null) continue;
                var label = KitType.Create(view, face, role, KitType.Text, KitType.Order.Words, room: true);
                Require(label.font == asset, role + " is not drawn in " + face);
                label.textWrappingMode = TextWrappingModes.NoWrap;
                var line = (ours.Length > 0 ? LineWords.Ours(ours) : LineWords.None) + (theirs.Length > 0 ? LineWords.Theirs(theirs) : LineWords.None);
                var runs = GlazeRuns.Of(label).Set(line);
                Require(runs.Count == (theirs.Length > 0 ? 1 : 0), face + "'s outside text lost its run");
                var (lines, _, height) = KitType.Lay(label, width - 2f * pad, 1);
                Require(lines == 1 && !label.isTextTruncated, face + "'s sample is cut");
                label.transform.localPosition = new Vector3(-width / 2f + pad, y, -0.0005f);
                y -= height + KitType.Units(SpatialKit.GridDegrees * 2f);
                labels.Add((face, asset, label));
            }
            // The instrument candidate keeps its faces until the room replaces it (the lead's call, 2026-10-30): a label made
            // without the room's scope stays out of the room's faces, sized again or not.
            var instrument = KitType.Create(view, "Instrument", KitRole.Body, KitType.Text, KitType.Order.Words);
            KitType.Size(instrument, KitRole.Strong);
            Require(!KitType.IsRoomFace(instrument.font), "an instrument label took a room face");
            instrument.gameObject.SetActive(false);
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
                // Upright: no letter of the line, outside text included, is sheared, so each glyph's left edge stands plumb.
                Require(drawn.All(character =>
                {
                    var corners = info.meshInfo[character.materialReferenceIndex].vertices;
                    return Mathf.Abs(corners[character.vertexIndex].x - corners[character.vertexIndex + 1].x) < 1e-5f;
                }), face + " slants a letter");
            }
            FacesTheEyes(shot, view, carriers, eyes);
            Debug.Log("Halcyonic: spatial kit: faces " + string.Join(", ", labels.Select(each => each.Face + " " + each.Label.textInfo.characterCount + " glyphs")) + ".");
            return shot;
        }
    }
}
