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
                "20-screen-picked" => new KitScreenModel("Tower Toss", "Codex", "Ready for a change", true, passed, true, null, barOpen: true, working: false),
                "20-screen-idle" => new KitScreenModel("Tower Toss", "Codex", "Ready for a change", true, passed, true, null, barOpen: false, working: false),
                "20-screen-working" => new KitScreenModel("Tower Toss", "Codex", "Working", true,
                    KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: null, ScreenAt), true, null, barOpen: true, working: true),
                _ => new KitScreenModel("Recipe Box", "Codex", "Finished this round", false, null, true, null, barOpen: true, working: false),
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
            bar.Show(model, screen.Size.x);
            var gap = KitType.Units(BarGapDegrees);
            // Beneath the screen, slightly in front of it, as visionOS stands an ornament (R04).
            bar.transform.localPosition = new Vector3(0f, -screen.Size.y / 2f - gap - bar.Size.y / 2f, -KitType.Units(0.1f));
            var total = new Vector2(screen.Size.x, screen.Size.y + gap + bar.Size.y);
            screen.transform.localPosition = new Vector3(0f, total.y / 2f - screen.Size.y / 2f, 0f);
            bar.transform.localPosition += new Vector3(0f, total.y / 2f - screen.Size.y / 2f, 0f);
            shot.Elevation = CentreFor(total.y);
            shot.Centre = Place(view, eyes, 0f, shot.Elevation);
            var carriers = screen.Carriers.Concat(bar.Carriers).ToList();
            Stand(shot, view, eyes, carriers, total);
            shot.Targets.Add(("screen bar", bar.transform, bar.Targets));
            shot.Faces.AddRange(carriers.Select(carrier => carrier.Face));
            shot.Witness = (screen.Face.Face, new Vector3(0.4f, -0.3f, 0f), SpatialKit.Well);
            shot.OpacityNegative = screen.Face.Face;

            // The bar's edges are the screen's (ADR 0040, rule 2; ADR 0026's aligned edges).
            Require(Mathf.Abs(bar.Size.x - screen.Size.x) < 1e-5f, "the bar is not as wide as the screen");
            // The one clear action only where it applies, first; Try it only after a passed check of the computer's own, with a route.
            var actions = bar.Shown.Select(each => each.Action).ToList();
            Require(actions.SequenceEqual(model.Shown.Where(a => a != KitScreenAction.Steps && a != KitScreenAction.Why && a != KitScreenAction.HowItWent)
                    .Concat(model.Shown.Where(a => a == KitScreenAction.Steps || a == KitScreenAction.Why || a == KitScreenAction.HowItWent))),
                "the bar's actions are not the model's");
            Require(actions.Contains(KitScreenAction.TryIt) == (model.CanTry && !model.Folded), "Try it stands without a passed check of the computer's own, or is missing");
            Require(actions.Contains(KitScreenAction.MakeAVrVersion) == (!model.IsVr && !model.Folded), "Make a VR version stands where it does not apply, or is missing");
            if (!model.Folded) Require(bar.Shown[0].Action == model.Primary && bar.Shown[0].Button.Role == KitButtonRole.Primary, "the primary action is not first or not filled");
            Require(bar.Shown.Count(each => each.Button.Role == KitButtonRole.Primary) == (model.Folded ? 0 : 1), "more or less than one primary action");
            // Folded, the handle alone.
            Require(model.Folded == bar.Handle.gameObject.activeSelf && model.Folded == !bar.Carrier.gameObject.activeSelf, "the bar is not folded to its handle as the model says");
            // No picture source exists yet: nothing on the screen says it shows a build.
            var everything = view.GetComponentsInChildren<TextMeshPro>(false).Select(label => label.text ?? "").ToList();
            Require(!everything.Any(text => text.IndexOf("checked build", StringComparison.OrdinalIgnoreCase) >= 0), "the screen speaks of a build it does not show");
            Require(screen.Mark.text == model.Monogram, "the neutral mark is not the name's two letters");
            Require(screen.ReadsWhole, "the screen's title chip is cut");
            FacesTheEyes(shot, view, carriers, eyes);
            Debug.Log("Halcyonic: spatial kit: screen " + Spread(shot, eyes) + "; bar " + bar.Rows + " row(s), " + bar.Shown.Count + " actions.");
            return shot;
        }
    }
}
