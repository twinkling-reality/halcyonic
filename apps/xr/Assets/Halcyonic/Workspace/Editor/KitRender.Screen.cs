#nullable enable
using System;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// A picked project's screen and its bar (ADR 0040, rule 2), on a synthetic project: picked with its bar open and Try it
    /// after a passed check of the computer's own, idle with the bar folded to its handle, working with a check under way,
    /// and a project that is not VR yet offering Make a VR version first. The fixture stands the screen where the plane
    /// stands; the eyes see it at the same angles as at about 2 m, where the placement puts it. Every name, state and check
    /// here is synthetic.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ScreenStates = { "20-screen-picked", "20-screen-idle", "20-screen-working", "20-screen-not-vr" };

        private const float BarGapDegrees = 0.75f;

        private static readonly DateTimeOffset ScreenAt = new DateTimeOffset(2026, 10, 29, 10, 38, 0, TimeSpan.Zero);

        private static Shot ScreenShot(string state, Transform root, Vector3 eyes)
        {
            var passed = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, ScreenAt);
            var model = state switch
            {
                "20-screen-picked" => new KitScreenModel("Tower Toss", "Codex", "Ready for a change", true, passed, true, null, barOpen: true, working: false, newestChangeAt: null),
                "20-screen-idle" => new KitScreenModel("Tower Toss", "Codex", "Ready for a change", true, passed, true, null, barOpen: false, working: false, newestChangeAt: null),
                "20-screen-working" => new KitScreenModel("Tower Toss", "Codex", "Running", true,
                    KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: null, ScreenAt), true, null, barOpen: true, working: true, newestChangeAt: null),
                _ => new KitScreenModel("Recipe Box", "Codex", "Round Ended", false, null, true, null, barOpen: true, working: false, newestChangeAt: null),
            };
            var caption = state switch
            {
                "20-screen-picked" => "A picked project's screen, its bar open, Try it after a passed check, synthetic data",
                "20-screen-idle" => "The screen idle, its bar folded to a handle, synthetic data",
                "20-screen-working" => "The screen while the work goes on, a check under way, no Try it, synthetic data",
                _ => "A project that is not VR yet, Make a VR version first, synthetic data",
            };
            var shot = new Shot { Caption = caption };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }

            var view = new GameObject("Picked project").transform;
            view.SetParent(root, false);
            var screen = KitScreen.Create(view);
            screen.Show(model);
            var bar = KitScreenBar.Create(view);
            var gap = KitType.Units(BarGapDegrees);
            // The plane faces the eyes at the screen's centre, as the room stands it; the bar hangs beneath, slightly in front, as
            // visionOS stands an ornament (R04), and sizes its targets for where it stands from that point.
            var facing = Vector2.zero;
            for (var round = 0; round < 2; round++)
            {
                bar.Show(model, screen.Size.x, facing);
                var below = new Vector2(0f, -screen.Size.y / 2f - gap - bar.Size.y / 2f);
                bar.transform.localPosition = new Vector3(below.x, below.y, -KitType.Units(0.1f));
                facing = -below;
            }
            var total = new Vector2(screen.Size.x, screen.Size.y + gap + bar.Size.y);
            shot.Elevation = 0f;
            shot.Centre = Place(view, eyes, 0f, shot.Elevation);
            var carriers = screen.Carriers.Concat(bar.Carriers).ToList();
            Stand(shot, view, eyes, carriers, total);
            shot.Targets.Add(("screen bar", bar.transform, bar.Targets));
            shot.Faces.AddRange(carriers.Select(carrier => carrier.Face));
            // A bare point of the face, clear of the title chip, the mark and the name: the project's identity field.
            shot.Witness = (screen.Face.Face, new Vector3(0.42f, 0.18f, 0f), SpatialKit.IdentityField(model.IdentityHue));
            shot.OpacityNegative = screen.Face.Face;

            // Every target holds the floor at its true distance from the eyes, not only along the bar's normal (the lead's rule).
            foreach (var (action, button) in bar.Shown)
            {
                var t = button.transform;
                var half = button.Size / 2f;
                var tall = Vector3.Angle(t.TransformPoint(new Vector3(0f, half.y, 0f)) - eyes, t.TransformPoint(new Vector3(0f, -half.y, 0f)) - eyes);
                var wide = Vector3.Angle(t.TransformPoint(new Vector3(half.x, 0f, 0f)) - eyes, t.TransformPoint(new Vector3(-half.x, 0f, 0f)) - eyes);
                Require(Mathf.Min(tall, wide) >= SpatialKit.TargetDegrees - 0.005f,
                    KitScreenModel.Words(action) + " reads " + Mathf.Min(tall, wide).ToString("0.00") + " degrees at its true distance, under " + SpatialKit.TargetDegrees);
            }
            Debug.Log("Halcyonic: spatial kit: " + state + " bar targets at their true distance: the least " + bar.Shown.Select(each =>
            {
                var t = each.Button.transform; var half = each.Button.Size / 2f;
                return Vector3.Angle(t.TransformPoint(new Vector3(0f, half.y, 0f)) - eyes, t.TransformPoint(new Vector3(0f, -half.y, 0f)) - eyes);
            }).DefaultIfEmpty(0f).Min().ToString("0.00") + " degrees.");
            // The bar's edges are the screen's (ADR 0040, rule 2; ADR 0026's aligned edges).
            Require(Mathf.Abs(bar.Size.x - screen.Size.x) < 1e-5f, "the bar is not as wide as the screen");
            // Every button stands inside the bar, its padding kept, at the size it stands (XR baseline 2026-11-03: the outer
            // buttons hung past the bar's ends), and the bar holds them all at one word size and one capsule shape: the
            // owner's one type scale, and meaning carried by the fill alone.
            var barPad = KitType.Units(SpatialKit.PaddingDegrees);
            foreach (var (action, button) in bar.Shown)
            {
                var bt = button.transform;
                var halfWide = button.Size.x * bt.localScale.x / 2f;
                var halfTall = button.Size.y * bt.localScale.y / 2f;
                var inside = bar.Size.x / 2f - barPad + 1e-4f;
                Require(bt.localPosition.x - halfWide >= -inside && bt.localPosition.x + halfWide <= inside
                    && Mathf.Abs(bt.localPosition.y) + halfTall <= bar.Size.y / 2f + 1e-4f,
                    KitScreenModel.Words(action) + " runs from " + (bt.localPosition.x - halfWide).ToString("0.0000") + " to "
                    + (bt.localPosition.x + halfWide).ToString("0.0000") + ", past the bar's inside, -" + inside.ToString("0.0000") + " to " + inside.ToString("0.0000"));
                Require(Mathf.Abs(button.Plate.CornerRadius - button.Size.y / 2f) < 1e-5f,
                    KitScreenModel.Words(action) + " is not a capsule: its corners " + button.Plate.CornerRadius.ToString("0.0000") + " on a height of " + button.Size.y.ToString("0.0000"));
            }
            // The title chip clear of the project's mark: at Larger the screen narrows while the type grows (XR baseline).
            var chipBottom = screen.TitleChip.transform.localPosition.y - screen.TitleChip.Size.y / 2f;
            var markTop = screen.Mark.transform.localPosition.y + screen.Mark.rectTransform.sizeDelta.y / 2f;
            Require(!screen.Mark.gameObject.activeSelf || chipBottom - markTop >= KitType.Units(SpatialKit.TargetGapDegrees) - 1e-4f,
                "the title chip's foot stands " + SpatialKit.DegreesTo(chipBottom - markTop).ToString("0.00") + " degrees above the mark's top, closer than "
                + SpatialKit.TargetGapDegrees.ToString("0.0"));
            // The one clear action only where it applies, first; Try it only after a passed check of the computer's own, with a route.
            var actions = bar.Shown.Select(each => each.Action).ToList();
            Require(actions.SequenceEqual(model.Shown.Where(a => a != KitScreenAction.Steps && a != KitScreenAction.Why && a != KitScreenAction.HowItWent)
                    .Concat(model.Shown.Where(a => a == KitScreenAction.Steps || a == KitScreenAction.Why || a == KitScreenAction.HowItWent))),
                "the bar's actions are not the model's");
            Require(actions.Contains(KitScreenAction.TryIt) == (model.CanTry && !model.Folded), "Try it stands without a passed check of the computer's own, or is missing");
            Require(actions.Contains(KitScreenAction.MakeAVrVersion) == (!model.IsVr && !model.Folded), "Make a VR version stands where it does not apply, or is missing");
            if (!model.Folded) Require(bar.Shown[0].Action == model.Primary && bar.Shown[0].Button.Role == KitButtonRole.Primary
                && bar.Shown[0].Button.Meaning == KitPillMeaning.Blue && bar.Shown[0].Button.Pill != null, "the primary action is not first or not a blue glossy capsule");
            Require(bar.Shown.Count(each => each.Button.Role == KitButtonRole.Primary) == (model.Folded ? 0 : 1), "more or less than one primary action");
            Require(bar.Shown.Where(each => each.Button.Role == KitButtonRole.Primary).All(each => PrimaryIsLight(each.Button)), "the primary is not the blue capsule with its white word");
            Require(bar.Shown.Where(each => each.Button.Role == KitButtonRole.Primary).All(each => PrimaryPointedShows(each.Button)), "a pointed primary is not told from its rest at 3:1");
            // The room's type scale (ART's handoff): the chip's title in Title, every room label at its role's own line pitch.
            Require(screen.Title.font == KitType.Face(KitRole.Title) && Mathf.Approximately(screen.Title.fontSize, GlazeTokens.FontSize(KitType.Em(KitRole.Title))), "the chip's title is not in Title");
            foreach (var label in view.GetComponentsInChildren<TextMeshPro>(true))
                Require(LeadsAsItsRole(label), label.name + " does not keep its role's line pitch");
            // Folded, the handle alone.
            Require(model.Folded == bar.Handle.gameObject.activeSelf && model.Folded == !bar.Carrier.gameObject.activeSelf, "the bar is not folded to its handle as the model says");
            // No picture source exists yet: nothing on the screen says it shows a build.
            var everything = view.GetComponentsInChildren<TextMeshPro>(false).Select(label => label.text ?? "").ToList();
            Require(!everything.Any(text => text.IndexOf("checked build", StringComparison.OrdinalIgnoreCase) >= 0), "the screen speaks of a build it does not show");
            Require(screen.Mark.text == model.Monogram, "the neutral mark is not the name's two letters");
            Require(screen.ReadsWhole, "the screen's name or its title chip is cut");
            // Its identity: the name across the face in display type, the field in the project's own quiet hue.
            Require(screen.Name.text == LabelText.ForTextMeshPro(model.Project), "the screen does not carry the project's name");
            screen.Name.ForceMeshUpdate(true);
            Require(screen.Name.gameObject.activeInHierarchy && !screen.Name.isTextTruncated && screen.Name.textInfo.lineCount == 1
                && screen.Name.textBounds.size.x > KitType.Units(SpatialKit.Degrees(SpatialKit.DisplayDp)), "the project's name is not drawn whole");
            // One word size across the bar, read last: after the primary has been pointed at and let go, as a hand would.
            var wordSizes = bar.Shown.Select(each => each.Button.Words.fontSize * each.Button.transform.localScale.x).ToList();
            if (wordSizes.Count > 0)
                Require(wordSizes.Max() - wordSizes.Min() < 1e-3f, "the bar's words stand at more than one size, from "
                    + wordSizes.Min().ToString("0.000") + " to " + wordSizes.Max().ToString("0.000"));
            FacesTheEyes(shot, view, carriers, eyes);
            Debug.Log("Halcyonic: spatial kit: screen " + Spread(shot, eyes) + "; bar " + bar.Rows + " row(s), " + bar.Shown.Count + " actions.");
            return shot;
        }
    }
}
