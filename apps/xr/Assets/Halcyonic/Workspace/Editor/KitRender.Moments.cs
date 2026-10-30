#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The moments that come out of a task's character (ADR 0040, rule 4), on a synthetic build of Tower Toss: sent, the
    /// runtime's report of an edit, the computer's reading of the file with a checkpoint diff, a question, a question that
    /// stays shown while newer moments arrive, a check the agent ran, a check of the computer's own that failed, ready with
    /// Try it, a skipped reading, and one frame of a moment coming out. Every name, command, time and outcome is synthetic.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] MomentStates =
        {
            "21-moment-sent", "21-moment-reported", "21-moment-seen", "21-moment-question", "21-moment-question-newer",
            "21-moment-agent-check", "21-moment-failed", "21-moment-ready", "21-moment-skipped", "21-moment-popping", "21-moment-question-long",
            "21-moment-longest", "21-moment-says",
        };

        private static readonly DateTimeOffset MomentAt = new DateTimeOffset(2026, 10, 29, 10, 42, 0, TimeSpan.Zero);

        private static KitMomentSlot MomentSlot(string state)
        {
            const string codex = "Codex";
            var sent = KitMoment.Sent(codex);
            var says = KitMoment.AgentSays(codex, "I'll make the towers wobble when a ball hits them.", MomentAt);
            var edit = KitMoment.ReportedEdit(codex, KitFileChange.Changed, "src/towers.ts", MomentAt);
            var question = KitMoment.Question(codex, "npm install matter-js", KitSandbox.MayLeave, complete: true, MomentAt);
            var passed = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, MomentAt.AddMinutes(3));
            return state switch
            {
                "21-moment-sent" => new KitMomentSlot(new[] { sent }, working: true),
                "21-moment-reported" or "21-moment-popping" => new KitMomentSlot(new[] { sent, says, edit }, working: true),
                "21-moment-seen" => new KitMomentSlot(new[] { sent, says, edit,
                    KitMoment.ObservedChange(KitFileChange.Changed, "src/towers.ts", 41, 54, MomentAt, new KitCheckpointDiff(14, 3), undoOn: true) }, working: true),
                "21-moment-question" => new KitMomentSlot(new[] { sent, says, question }, working: true),
                "21-moment-question-long" => new KitMomentSlot(new[] { sent, says, KitMoment.Question(codex,
                    "dotnet test apps/xr/dotnet/Halcyonic.Client.Tests --filter \"FullyQualifiedName~KitMomentTests|FullyQualifiedName~KitScreenTests|FullyQualifiedName~SpatialKitTests\" --logger trx --results-directory ./TestResults/kit",
                    KitSandbox.MayLeave, complete: true, MomentAt) }, working: true),
                "21-moment-question-newer" => new KitMomentSlot(new[] { sent, says, question, KitMoment.AgentSays(codex, "Waiting for your answer.", MomentAt) }, working: true),
                "21-moment-agent-check" => new KitMomentSlot(new[] { sent, edit, KitMoment.AgentCheck(codex, "npm run build", "exit 1", MomentAt) }, working: true),
                // The longest Title today's words make, which must read whole at the larger size in two lines.
                "21-moment-longest" => new KitMomentSlot(new[] { sent, KitMoment.HostCheck(KitCheckScope.BuildsAndOpens, ended: true, passed: true, MomentAt) }, working: true),
                "21-moment-says" => new KitMomentSlot(new[] { sent, says }, working: true),
                "21-moment-failed" => new KitMomentSlot(new[] { sent, edit, KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, MomentAt) }, working: true),
                "21-moment-ready" => new KitMomentSlot(new[] { sent, edit, passed, KitMoment.Ready("Tower Toss", TimeSpan.FromSeconds(400), passed, tryRoute: true) }, working: false),
                _ => new KitMomentSlot(new[] { sent, edit, KitMoment.NotObserved("too many files", MomentAt) }, working: true),
            };
        }

        private static bool SameColour(Color a, Color b) => Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) && Mathf.Approximately(a.b, b.b);

        /// <summary>The one primary at rest is filled with light, n7, its word in n0; never the accent (ADR 0040 as amended 2026-10-30).</summary>
        private static bool PrimaryIsLight(KitButton button) =>
            SameColour(button.Plate.Fill, KitType.Colour(SpatialKit.Text)) && SameColour(button.Words.color, KitType.Colour(SpatialKit.OnFill));

        private static GlazeColor Stored(Color colour) =>
            new GlazeColor((byte)Mathf.RoundToInt(colour.r * 255f), (byte)Mathf.RoundToInt(colour.g * 255f), (byte)Mathf.RoundToInt(colour.b * 255f));

        /// <summary>
        /// A pointed primary is told from its rest at 3:1 or more on its face (AUDIT's A1C-S1; the art direction's ring): painted
        /// pointed, a ring stands outside its n7 capsule, at least 3:1 against the face it stands on; painted at rest again, the
        /// ring is gone and it is as it was.
        /// </summary>
        private static bool PrimaryPointedShows(KitButton button)
        {
            button.PaintForRender(pointed: true, pressed: false);
            var ring = button.Ring;
            var shows = ring.gameObject.activeSelf && SameColour(button.Plate.Fill, KitType.Colour(SpatialKit.Text))
                && new[] { SpatialKit.Carrier, SpatialKit.FaceTop, SpatialKit.FaceBottom }.All(face => GlazeColor.Contrast(Stored(ring.Fill), face) >= 3);
            button.PaintForRender(pointed: false, pressed: false);
            return shows && !button.Ring.gameObject.activeSelf && PrimaryIsLight(button);
        }

        /// <summary>A word answer at rest is its word alone: no fill, no ring, its word in n6 (the art direction's answers).</summary>
        private static bool WordIsBare(KitButton button) =>
            button.Plate.Fill.a < 0.01f && (button.Plate.Edge.a < 0.01f || button.Plate.EdgeWidth <= 0f) && SameColour(button.Words.color, KitType.Secondary);

        /// <summary>
        /// A label in one of the room's faces keeps that face's role's line pitch (<see cref="KitType.RoomLeading"/>); each
        /// face carries one role in the room, so the face says which. Labels in any other face are the instrument's.
        /// </summary>
        private static bool LeadsAsItsRole(TMP_Text label)
        {
            if (!KitType.IsRoomFace(label.font)) return true;
            var role = new[] { KitRole.Display, KitRole.Title, KitRole.Body, KitRole.Strong, KitRole.Label, KitRole.Mono }.First(each => KitType.Face(each) == label.font);
            return Mathf.Abs(label.lineSpacing - KitType.LineSpacingFor(label, KitType.RoomLeading(role))) < 0.01f;
        }

        private static Shot MomentShot(string state, Transform root, Vector3 eyes)
        {
            var slot = MomentSlot(state);
            var moment = slot.Moments[slot.Shown!.Value];
            var shot = new Shot { Caption = "A moment out of the task's character: " + state.Substring("21-moment-".Length).Replace('-', ' ') + ", synthetic data" };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }

            var card = KitMomentCard.Create(root);
            card.Show(slot);
            var popping = state == "21-moment-popping";
            shot.Moving = popping;
            shot.Elevation = 0f;
            shot.Centre = Place(card.transform, eyes, 0f, shot.Elevation);
            var placed = card.transform.localScale;
            card.GrowsFromLeft = popping;
            card.Pop(popping ? 0.45f : 1f);
            // Popping scales only the card's body: the placement's scale stands.
            Require(card.transform.localScale == placed, "the pop overwrote the placement's scale");
            var carriers = card.Carriers.ToList();
            Stand(shot, card.transform, eyes, carriers, card.Size);
            shot.Targets.Add(("moment", card.transform, card.Targets));
            // The card is glass (A2): its face's opacity and its truth are held on the glass, each with its own witness.
            Require(carriers.All(carrier => carrier.Glass != null), "the card is not drawn in glass");
            var panels = new List<(KitCarrier, IEnumerable<TMP_Text>)> { (card.Carrier, card.Words.Cast<TMP_Text>().ToList()) };
            if (card.Earlier.gameObject.activeSelf) panels.Add((card.Earlier, new TMP_Text[0]));
            foreach (var failure in GlassTruth(panels)) shot.Failures.Add(failure);
            Require((card.Carrier.Glass?.Tone).Equals(KitMomentCard.ToneOf(moment.Tone)), "the glass is lit in a tone the moment does not carry");
            shot.AfterEye = (camera, eye, found) => found.AddRange(GlassFaceHolds(card.Carrier, camera, eye, "the moment"));

            // Who says it: the eyebrow says the source; a ring for the runtime's report, a disc for what the computer saw.
            Require(card.Eyebrow.text == LabelText.ForTextMeshPro(moment.Source), "the moment does not say who says it as its source is");
            var disc = card.Mark.Fill.a > 0.5f;
            Require(disc == (moment.Provenance == KitProvenance.Observed), "the mark is a disc on the runtime's report, or a ring on what the computer saw");
            // Colour only for its meaning: amber on a question, red on a failure, green on a pass, each with its glyph.
            // The mark's sign (ART's A3a review): "?" for a question, "!" for a failure, a check for a pass; nothing else.
            Require((card.Tone.gameObject.activeSelf || card.Symbol.gameObject.activeSelf) == (moment.Tone != KitMomentTone.Neutral), "a tone stands where nothing means it, or is missing");
            Require(card.Tone.gameObject.activeSelf == (moment.Tone == KitMomentTone.Passed), "a pass's check stands where nothing passed, or is missing");
            Require(!card.Symbol.gameObject.activeSelf || card.Symbol.text == (moment.Tone == KitMomentTone.Attention ? "?" : "!"), "the mark's sign is not its meaning's");
            if (card.Symbol.gameObject.activeSelf)
            {
                card.Symbol.ForceMeshUpdate(true);
                var sign = card.Symbol.textInfo;
                Require(sign.characterCount == 1 && sign.characterInfo[0].isVisible && sign.characterInfo[0].character == card.Symbol.text[0], "the mark's sign is not drawn whole");
            }
            if (moment.Tone != KitMomentTone.Neutral)
                Require((moment.Tone == KitMomentTone.Attention) == (moment.Kind == KitMomentKind.Question), "amber stands on something that is not a question");
            // A command is cut by its lines, never sideways: no ellipsis drawn in a well (ART's A3a review; a gap the cut check let through).
            if (card.Well.gameObject.activeSelf)
            {
                card.Well.Words.ForceMeshUpdate(true);
                var info = card.Well.Words.textInfo;
                Require(!info.characterInfo.Take(info.characterCount).Any(character => character.isVisible && character.character == '\u2026'), "the well cuts its command sideways with an ellipsis");
            }
            // Where a moment offers a next step, one primary carries it (a failure's Read Output among them).
            Require(!card.Actions.Any(KitMomentCard.IsPrimary) || card.Shown.Count(each => each.Button.Role == KitButtonRole.Primary) == 1, "the next step is not the one primary");
            // The count: only what stands before the shown moment, on the earlier edge, and what came after a waiting question.
            var counted = string.Join(" · ", new[] { slot.Earlier, slot.Newer }.Where(words => words != null));
            Require(card.Count.gameObject.activeSelf == (counted.Length > 0) && (counted.Length == 0 || card.Count.text == counted), "the count is not the model's earlier and newer moments");
            Require(card.Earlier.gameObject.activeSelf == (slot.Earlier != null), "the earlier edge stands without earlier moments, or is missing");
            // The actions beneath, the model's and in this order: the one primary first, at full width, then every other in the
            // model's order (the art direction's answers; AUDIT's A3A-M1). A command shown cut can only be refused (SECURITY.md).
            var modelActions = moment.Kind == KitMomentKind.Question && !card.ReadsWhole
                ? new[] { KitMomentAction.ReadIt }.Concat(slot.Actions.Where(action => action != KitMomentAction.YesRunIt)).ToList()
                : slot.Actions.ToList();
            var leading = modelActions.Where(KitMomentCard.IsPrimary).Take(1).ToList();
            var expectedActions = leading.Concat(modelActions.Where(action => !leading.Contains(action))).ToList();
            Require(card.Shown.Select(each => each.Action).SequenceEqual(expectedActions),
                "the actions are not the slot's in their order (" + string.Join(", ", card.Shown.Select(each => each.Action)) + "), or Yes stands on a command shown cut");
            if (moment.Kind == KitMomentKind.Question && !card.ReadsWhole)
                Require(!card.Shown.Any(each => each.Action == KitMomentAction.YesRunIt) && card.Shown.Any(each => each.Action == KitMomentAction.No),
                    "a command shown cut can be approved");
            Require(card.Shown.Count(each => each.Button.Role == KitButtonRole.Primary) <= 1, "more than one primary action");
            Require(card.Shown.Where(each => each.Button.Role == KitButtonRole.Primary).All(each => PrimaryIsLight(each.Button)), "the primary is not filled with light, its word in n0");
            Require(card.Shown.Where(each => each.Button.Role == KitButtonRole.Primary).All(each => PrimaryPointedShows(each.Button)), "a pointed primary is not told from its rest at 3:1");
            Require(card.Shown.All(each => each.Button.Plate.CornerRadius >= each.Button.Size.y / 2f - 1e-5f), "an answer is not a capsule");
            Require(card.Shown.Where(each => each.Button.Role != KitButtonRole.Primary).All(each => each.Button.Role == KitButtonRole.Word && WordIsBare(each.Button)),
                "an answer other than the primary has a shape at rest");
            // No locked answer in the card's top 40 percent (the art direction's rule, with the bloom that peaks there).
            foreach (var (action, button) in card.Shown.Where(each => !each.Button.Available))
            {
                var fromTop = card.transform.InverseTransformPoint(card.Carrier.transform.TransformPoint(new Vector3(0f, card.Size.y / 2f, 0f))).y
                    - card.transform.InverseTransformPoint(button.transform.position).y;
                Require(fromTop >= 0.4f * card.Size.y, KitMomentWords.Action(action) + " is locked in the card's top 40 percent");
            }
            // The room's type scale: the Title in Title, every room label at its role's own line pitch.
            if (moment.Title != null)
                Require(card.Title.font == KitType.Face(KitRole.Title) && Mathf.Approximately(card.Title.fontSize, GlazeTokens.FontSize(KitType.Em(KitRole.Title))), "the Title is not in Title");
            foreach (var label in card.GetComponentsInChildren<TextMeshPro>(true))
                Require(LeadsAsItsRole(label), label.name + " does not keep its role's line pitch");
            // Their names keep their run wherever they stand; their command stands whole in its run in the well.
            var theirsShown = (moment.Title != null ? GlazeRuns.Of(card.Title).Theirs.Count : 0) + card.Meta.Sum(label => GlazeRuns.Of(label).Theirs.Count)
                + (moment.Prose != null ? GlazeRuns.Of(card.Prose).Theirs.Count : 0);
            var theirsModel = (moment.Title?.Runs.Count(run => run.Theirs) ?? 0) + moment.Meta.Sum(segment => segment.Runs.Count(run => run.Theirs))
                + (moment.Prose?.Runs.Count(run => run.Theirs) ?? 0);
            Require(theirsShown == theirsModel, "outside text is not exactly where it is theirs (" + theirsShown + " runs shown, " + theirsModel + " theirs)");
            if (moment.Well != null) Require(GlazeRuns.Of(card.Well.Words).Theirs.Count == 1, "the command in the well is not theirs in one run");
            Require(card.Meta.Where(label => GlazeRuns.Of(label).Theirs.Count > 0 && GlazeRuns.Of(label).Theirs[0].Start == 0 && GlazeRuns.Of(label).Theirs[0].End == label.text.Length)
                .All(label => label.font == KitType.Face(KitRole.Mono)), "a path or name of theirs in the metadata is not in their monospace");
            var everything = card.Words.Select(label => label.text ?? "").ToList();
            Require(!everything.Any(text => text.IndexOf("no change", StringComparison.OrdinalIgnoreCase) >= 0), "something reads as no changes");
            // Every part reads whole, by name; only the long command shows cut, in its well, with what is left said.
            Require(card.CutParts.Count == 0, "cut: " + string.Join(", ", card.CutParts));
            if (state == "21-moment-question-long")
                Require(card.WellCut && card.Well.Rest.text == LabelText.ForTextMeshPro(KitMomentWords.CutMore(card.Well.HiddenLines)),
                    "the long command read whole, or its cut line does not say what is left");
            else Require(!card.WellCut, "a command is cut in its well");
            Require(popping ? card.Popped < 1f : card.Popped >= 1f, "the moment is not as far out as the frame says");
            FacesTheEyes(shot, card.transform, carriers, eyes);
            Debug.Log("Halcyonic: spatial kit: moment " + state + " " + Spread(shot, eyes) + "; " + card.Shown.Count + " actions.");
            return shot;
        }
    }
}
