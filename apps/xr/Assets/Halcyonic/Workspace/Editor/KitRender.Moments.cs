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
                "21-moment-failed" => new KitMomentSlot(new[] { sent, edit, KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, MomentAt) }, working: true),
                "21-moment-ready" => new KitMomentSlot(new[] { sent, edit, passed, KitMoment.Ready("Tower Toss", TimeSpan.FromSeconds(400), passed, tryRoute: true) }, working: false),
                _ => new KitMomentSlot(new[] { sent, edit, KitMoment.NotObserved("too many files", MomentAt) }, working: true),
            };
        }

        private static bool SameColour(Color a, Color b) => Mathf.Approximately(a.r, b.r) && Mathf.Approximately(a.g, b.g) && Mathf.Approximately(a.b, b.b);

        /// <summary>The one primary at rest is filled with light, n7, its word in n0; never the accent (ADR 0040 as amended 2026-10-30).</summary>
        private static bool PrimaryIsLight(KitButton button) =>
            SameColour(button.Plate.Fill, KitType.Colour(SpatialKit.Text)) && SameColour(button.Words.color, KitType.Colour(SpatialKit.OnFill));

        /// <summary>
        /// A pointed primary is told from its rest at 3:1 or more (AUDIT's A1C-S1): painted pointed, it draws an n0 band inside
        /// its edge over its n7 fill; painted at rest again, it is as it was.
        /// </summary>
        private static bool PrimaryPointedShows(KitButton button)
        {
            button.PaintForRender(pointed: true, pressed: false);
            var band = button.Plate.Edge;
            var shows = button.Plate.EdgeWidth > 0f && SameColour(button.Plate.Fill, KitType.Colour(SpatialKit.Text))
                && GlazeColor.Contrast(new GlazeColor((byte)Mathf.RoundToInt(band.r * 255f), (byte)Mathf.RoundToInt(band.g * 255f), (byte)Mathf.RoundToInt(band.b * 255f)), SpatialKit.Text) >= 3;
            button.PaintForRender(pointed: false, pressed: false);
            return shows && PrimaryIsLight(button);
        }

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
            shot.Faces.AddRange(carriers.Select(carrier => carrier.Face));
            // A bare point of the face inside its bottom-left padding, where no part is ever drawn (the face's own units, -0.5 to 0.5).
            shot.Witness = (card.Carrier.Face, new Vector3(-0.46f, -0.46f, 0f), SpatialKit.Carrier);
            shot.OpacityNegative = card.Carrier.Face;

            // Who says it, as its source is: the runtime's report carries the quote mark, what the computer saw says so in words.
            var chipWords = card.Source.GetComponentsInChildren<TextMeshPro>(true).Select(label => label.text ?? "").ToList();
            Require(chipWords.Any(text => text == LabelText.ForTextMeshPro(moment.Source)), "the moment does not say who says it as its source is");
            var quoted = chipWords.Any(text => text == GlazeIconGlyphs.Of(GlazeIcon.Quote));
            Require(quoted == (moment.Provenance == KitProvenance.Reported), "the quote mark stands on what the computer saw, or is missing on the runtime's report");
            // Colour only for its meaning: amber on a question, red on a failure, green on a pass, each with its glyph.
            Require(card.Tone.gameObject.activeSelf == (moment.Tone != KitMomentTone.Neutral), "a tone stands where nothing means it, or is missing");
            if (card.Tone.gameObject.activeSelf)
            {
                var expected = KitType.Colour(moment.Tone == KitMomentTone.Attention ? SpatialKit.Attention : moment.Tone == KitMomentTone.Failure ? SpatialKit.Failure : SpatialKit.Passed);
                Require(card.Tone.color == expected, "the tone is not its meaning's colour");
                Require((moment.Tone == KitMomentTone.Attention) == (moment.Kind == KitMomentKind.Question), "amber stands on something that is not a question");
            }
            // The count: only what stands before the shown moment, and what came after a waiting question apart.
            var counted = string.Join(" · ", new[] { slot.Earlier, slot.Newer }.Where(words => words != null));
            Require(card.Count.gameObject.activeSelf == (counted.Length > 0) && (counted.Length == 0 || card.Count.text == counted), "the count is not the model's earlier and newer moments");
            Require(card.Stack.gameObject.activeSelf == (slot.Earlier != null), "the stacked edge stands without earlier moments, or is missing");
            // The actions beneath, the model's and in its order.
            // A command shown cut can only be refused: no Yes, Read it first, No kept (SECURITY.md; AUDIT's V4-M1).
            var expectedActions = moment.Kind == KitMomentKind.Question && !card.ReadsWhole
                ? new[] { KitMomentAction.ReadIt }.Concat(slot.Actions.Where(action => action != KitMomentAction.YesRunIt)).ToList()
                : slot.Actions.ToList();
            Require(card.Shown.Select(each => each.Action).SequenceEqual(expectedActions), "the actions are not the slot's, or Yes stands on a command shown cut");
            if (moment.Kind == KitMomentKind.Question && !card.ReadsWhole)
                Require(!card.Shown.Any(each => each.Action == KitMomentAction.YesRunIt) && card.Shown.Any(each => each.Action == KitMomentAction.No),
                    "a command shown cut can be approved");
            Require(card.Shown.Count(each => each.Button.Role == KitButtonRole.Primary) <= 1, "more than one primary action");
            Require(card.Shown.Where(each => each.Button.Role == KitButtonRole.Primary).All(each => PrimaryIsLight(each.Button)), "the primary is not filled with light, its word in n0");
            Require(card.Shown.Where(each => each.Button.Role == KitButtonRole.Primary).All(each => PrimaryPointedShows(each.Button)), "a pointed primary is not told from its rest at 3:1");
            // The room's type scale (ART's handoff): the subject in Title, every room label at its role's own line pitch.
            Require(card.Headline.font == KitType.Face(KitRole.Title) && Mathf.Approximately(card.Headline.fontSize, GlazeTokens.FontSize(KitType.Em(KitRole.Title))), "the subject is not in Title");
            foreach (var label in card.GetComponentsInChildren<TextMeshPro>(true))
                Require(LeadsAsItsRole(label), label.name + " does not keep its role's line pitch");
            // Their names keep their run wherever they stand; nothing reads as no changes.
            Require(GlazeRuns.Of(card.Headline).Theirs.Count == moment.Headline.Runs.Count(run => run.Theirs), "the subject's outside text is not exactly where it is theirs");
            var everything = card.GetComponentsInChildren<TextMeshPro>(false).Select(label => label.text ?? "").ToList();
            Require(!everything.Any(text => text.IndexOf("no change", StringComparison.OrdinalIgnoreCase) >= 0), "something reads as no changes");
            // Every moment reads whole, except the long command, which must show cut (with Read it) for the proof above.
            Require(state == "21-moment-question-long" ? !card.ReadsWhole : card.ReadsWhole,
                state == "21-moment-question-long" ? "the long command read whole, so the cut case went unproven" : "the moment's words are cut");
            Require(popping ? card.Popped < 1f : card.Popped >= 1f, "the moment is not as far out as the frame says");
            FacesTheEyes(shot, card.transform, carriers, eyes);
            Debug.Log("Halcyonic: spatial kit: moment " + state + " " + Spread(shot, eyes) + "; " + card.Shown.Count + " actions.");
            return shot;
        }
    }
}
