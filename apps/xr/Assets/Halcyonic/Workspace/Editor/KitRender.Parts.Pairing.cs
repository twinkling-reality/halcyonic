#nullable enable
using System;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Pairing beside the menu as the redesign draws it (ADR 0017, amendment of 2026-11-11, in A17's language): the frames
    /// <see cref="PairingColumn"/> gives, through <see cref="RoomBar.OfFrame"/>, drawn by the room's own view. Measured (the
    /// card stands, no word joined on a dot or cut, each found computer a row the person can press, every action drawn, Close
    /// last, the step's main action first where it has one) and read in the pixels (the card's first words and every
    /// action's). Synthetic names only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] PairingStates =
        {
            "49-pairing-looking", "49-pairing-found", "49-pairing-several", "49-pairing-same-name", "49-pairing-none", "49-pairing-code",
            "49-pairing-paired", "49-pairing-refused",
        };

        private static PartShot PairingShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var column = new PairingColumn(_ => { }, () => { });
            var studio = new PairingChoice("Studio Mac", "studio-mac.local:47801");
            switch (state)
            {
                case "49-pairing-found": column.Found(new[] { studio }); break;
                case "49-pairing-several": column.Found(new[] { studio, new PairingChoice("Attic", "attic.local:47801"), new PairingChoice("Kitchen Laptop", "kitchen.local:47801") }); break;
                case "49-pairing-same-name":
                    column.Found(new[] { new PairingChoice("Studio Mac", "pretender.local:47801", true), new PairingChoice("Studio Mac", "studio-mac.local:47801", true) });
                    break;
                case "49-pairing-none": column.Found(new PairingChoice[0]); break;
                case "49-pairing-code": column.Code("Studio Mac", studio.Address); break;
                case "49-pairing-paired": column.Code("Studio Mac", studio.Address); column.Paired(); break;
                case "49-pairing-refused": column.Refused("wrong_code", 1); break;
                default: column.Looking(); break;
            }
            var frame = column.Frame!;
            var bar = RoomBar.OfFrame(frame, VoiceStage.Idle);
            Require(bar != null && bar.Card != null, "the pairing frame gives the redesign no card");
            if (bar == null) return shot;
            Require(bar.Card != null && !bar.Card.AnyDot, "the pairing card joins facts on a dot: " + (bar.Card == null ? "" : string.Join(" | ", bar.Card.Words())));
            Require(bar.Column.Count > 0 && bar.Column[bar.Column.Count - 1].Place == KitPlace.Close, "Close is not the last action");
            Require(bar.Column.Count(action => action.Main) <= 1, "more than one main action");
            if (state is "49-pairing-none" or "49-pairing-refused")
                Require(bar.Column[0].Main, "the step's way on is not the main action, first");

            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomBarView.Create(plane);
            view.Show(bar);
            view.SettleForRender();
            var buttons = view.Column.Where(button => button.gameObject.activeSelf).ToList();
            foreach (var button in buttons)
            {
                button.Press.SettleForRender();
                button.Settle();
            }
            Place(plane, eyes, 0f, shot.Elevation);

            var card = view.Card;
            Require(view.Panel.gameObject.activeSelf && card != null && !view.Body.gameObject.activeSelf, "the panel does not hold the pairing card alone");
            if (card != null)
            {
                foreach (var label in card.Shown)
                {
                    label.ForceMeshUpdate(true);
                    var words = label.GetParsedText();
                    Require(label.textInfo.characterCount > 0 && !label.isTextOverflowing && !words.Contains("…"), "\"" + words + "\" is cut");
                    Require(!words.Contains(KitMomentWords.Dot.Trim()), "\"" + words + "\" joins facts on a dot");
                    Require(!words.Contains(".local"), "\"" + words + "\" shows a host, which stays off screen");
                }
                var rows = card.RowTargets.Count();
                Require(rows == bar.Card!.List.Count, "the card lets " + rows + " of " + bar.Card.List.Count + " found computers be pressed");
                Require(card.Plates.Count(each => each.What.StartsWith("slot", StringComparison.Ordinal)) == 0, "a pairing card lays a slot: host and port stay off screen");
            }
            Require(buttons.Count == bar.Column.Count, "the column draws " + buttons.Count + " of " + bar.Column.Count + " actions");

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                int Light(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                int Lightest(TMPro.TMP_Text label)
                {
                    var bounds = label.textBounds;
                    var most = 0;
                    for (var step = 0; step <= 80; step++)
                        most = Mathf.Max(most, Light(At(dark, label.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 80f), bounds.max.y - KitType.LinePitch(label) * 0.5f, 0f)))));
                    return most;
                }
                if (view.Card is RoomCardView shown && shown.Shown.Count > 0 && Lightest(shown.Shown[0]) < 150) failures.Add("the card's first words are not in the pixels");
                foreach (var button in buttons)
                    if (Lightest(button.Words) < 150) failures.Add(button.Words.text + ": no words in the pixels");
                Debug.Log("Halcyonic: parts: " + name + ": the pairing card " + (view.Card == null ? "missing" : view.Card.Size.y.ToString("0.0", CultureInfo.InvariantCulture) + " units tall")
                    + ", " + buttons.Count + " actions: " + string.Join(", ", buttons.Select(button => button.Words.text)) + ".");
            };
            return shot;
        }
    }
}
