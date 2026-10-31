#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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
    /// The character's life on its figure, as the view steps it (the art direction's character handoff, 2026-10-30, section 7;
    /// AUDIT B1 to B3), checked by behaviour so the checks hold for any figure: Last Known holds every value between two frames
    /// 2 s apart while Status Unknown still moves; the hop only with the computer's own passed check observed in this round,
    /// never Round Ended alone, a Ready from no known round, or Checking; "Put Away" on the tag for 1.2 s before the figure
    /// folds, then the tag goes; arrival growing from a seed with the tag reading Starting at once; Keep Still holding the
    /// body exactly; and its sounds as long and as loud as the table says. Each check has a witness that must be caught.
    /// Every name is synthetic.
    /// </summary>
    public static class CharacterRender
    {
        private const float Frame = 1f / 72f;
        private static readonly DateTimeOffset Round = new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

        public static void Check()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var result = new List<string>();
                var wasStill = GlazeMotion.Still;
                try
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    GlazeMotion.Still = false;
                    result.AddRange(LastKnownHoldsAndUnknownMoves());
                    result.AddRange(HopOnlyWithThisRoundsObservedCheck());
                    result.AddRange(GreenOnlyWithThisRoundsObservedCheck());
                    result.AddRange(PutAwaySaysItFirst());
                    result.AddRange(ArrivalGrowsAndSaysStarting());
                    result.AddRange(KeepStillHoldsTheBody());
                    result.AddRange(SoundsAsTheTableSays());
                }
                catch (Exception error) { result.Add(error.ToString()); }
                finally { GlazeMotion.Still = wasStill; WorkspaceRender.KeepFontAssetsAsCommitted(); }
                foreach (var failure in result) Debug.LogError("Halcyonic: character render: " + failure);
                if (result.Count == 0) Debug.Log("Halcyonic: character render: every check passed");
                return result;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static CharacterPresentation Presentation(CharacterActivity activity, bool stale = false, AttentionLevel attention = AttentionLevel.None) =>
            new CharacterPresentation("w-life", "Make the towers wobble", activity, CharacterPresenter.LabelOf(activity), attention, new List<string>(), 0,
                synthetic: true, recorded: false, stale: stale);

        private static (CharacterView View, GameObject Root) Made(string name)
        {
            var root = new GameObject("Character life witness " + name);
            return (CharacterView.Create(root.transform, "w-life"), root);
        }

        /// <summary>Everything the figure shows that can move: the body's place, turn and size, its eyes and its light.</summary>
        private static string Pose(CharacterView view)
        {
            var body = view.Body;
            var (open, lookX, lookY, ink, halo, uneven) = view.PoseForRender;
            return string.Join(" ", new[] { body.localPosition.x, body.localPosition.y, body.localPosition.z, body.localRotation.x, body.localRotation.y,
                    body.localRotation.z, body.localRotation.w, body.localScale.x, open, lookX, lookY, ink, halo, uneven }
                .Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
        }

        private static List<string> Run(CharacterView view, float seconds)
        {
            var poses = new List<string>();
            for (var frame = 0; frame < Mathf.RoundToInt(seconds / Frame); frame++)
            {
                view.AdvanceForRender(Frame);
                poses.Add(Pose(view));
            }
            return poses;
        }

        private static IEnumerable<string> LastKnownHoldsAndUnknownMoves()
        {
            var failures = new List<string>();
            var (view, root) = Made("last known");
            try
            {
                view.Show(Presentation(CharacterActivity.Working));
                Run(view, 3f);
                view.Show(Presentation(CharacterActivity.Working, stale: true));
                var held = Run(view, 2f);
                if (held.Distinct().Count() != 1) failures.Add("last known: the figure moved while the computer was unreachable (" + held.Distinct().Count() + " poses in 2 s)");
                if (view.Life.Look.Moves) failures.Add("last known: the life still says it moves");
                // The witness: the same view live again must move, or the check above proves nothing.
                view.Show(Presentation(CharacterActivity.Working));
                if (Run(view, 2f).Distinct().Count() < 10) failures.Add("last known witness: a live figure did not move, so stillness proves nothing");
                view.Show(Presentation(CharacterActivity.Unknown));
                if (Run(view, 2f).Distinct().Count() < 10) failures.Add("status unknown: the figure stood still, as only Last Known may");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            // First shown while lost: frozen in the state the journal last held, and its tag says Last Known (AUDIT, CV1a).
            var (first, firstRoot) = Made("first shown lost");
            try
            {
                first.Show(Presentation(CharacterActivity.Working, stale: true));
                var poses = Run(first, 2f);
                if (poses.Distinct().Count() != 1) failures.Add("first shown lost: the figure moved");
                if (!first.Life.Look.Equals(KitFigureLook.Unreachable(KitFigureLook.Of(WorkState.Working, false))))
                    failures.Add("first shown lost: its look is not the journal's last known state, dimmed");
                if (first.Label.Badge.Shown is not { State: WorkState.Working, LastKnown: true }) failures.Add("first shown lost: the tag does not say Last Known with the journal's state");
                if (first.Body.localScale.x < 0.999f) failures.Add("first shown lost: the figure is not at full size");
            }
            finally { UnityEngine.Object.DestroyImmediate(firstRoot); }
            return failures;
        }

        private static float HopOf(CharacterActivity before, CharacterActivity after, KitMoment? shown, DateTimeOffset? roundStartedAt)
        {
            var (view, root) = Made("hop");
            try
            {
                view.RoundStartedAt = roundStartedAt;
                view.Show(Presentation(before));
                Run(view, 1f);
                var rest = view.HeldLift;
                view.Show(Presentation(after), shown);
                var highest = float.NegativeInfinity;
                for (var frame = 0; frame < Mathf.RoundToInt(0.8f / Frame); frame++)
                {
                    view.AdvanceForRender(Frame);
                    highest = Mathf.Max(highest, view.Body.localPosition.y - rest);
                }
                return highest;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static IEnumerable<string> HopOnlyWithThisRoundsObservedCheck()
        {
            var failures = new List<string>();
            var check = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, Round.AddMinutes(3));
            var ready = KitMoment.Ready("Tower Toss", TimeSpan.FromMinutes(3), check, tryRoute: true);
            // The float alone reaches about 3.3 thousandths of a unit; the hop about 13.
            const float hop = 0.009f;
            var observed = HopOf(CharacterActivity.Working, CharacterActivity.TurnFinished, ready, Round);
            if (observed < hop) failures.Add("passed check: no hop with the computer's own passed check observed this round (" + observed.ToString("0.0000", CultureInfo.InvariantCulture) + ")");
            foreach (var (name, after, shown, start) in new (string, CharacterActivity, KitMoment?, DateTimeOffset?)[]
            {
                ("round ended alone", CharacterActivity.TurnFinished, null, Round),
                ("a ready from no known round", CharacterActivity.TurnFinished, ready, null),
                ("a ready from an earlier round", CharacterActivity.TurnFinished, ready, Round.AddMinutes(5)),
                ("checking with a ready", CharacterActivity.Verifying, ready, Round),
            })
            {
                var lift = HopOf(CharacterActivity.Working, after, shown, start);
                if (lift >= hop) failures.Add(name + ": it hopped (" + lift.ToString("0.0000", CultureInfo.InvariantCulture) + "), as only the observed passed check may");
            }
            return failures;
        }

        /// <summary>The green light of a passed check (AUDIT F1): never for a round that only ended, and there with this round's check.</summary>
        private static IEnumerable<string> GreenOnlyWithThisRoundsObservedCheck()
        {
            var failures = new List<string>();
            var check = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, Round.AddMinutes(3));
            var ready = KitMoment.Ready("Tower Toss", TimeSpan.FromMinutes(3), check, tryRoute: true);
            foreach (var (name, shown, start, lit) in new (string, KitMoment?, DateTimeOffset?, bool)[]
            {
                ("round ended alone", null, Round, false),
                ("a ready from no known round", ready, null, false),
                ("the passed check observed this round", ready, Round, true),
            })
            {
                var (view, root) = Made("green " + name);
                try
                {
                    view.RoundStartedAt = start;
                    view.Show(Presentation(CharacterActivity.Working));
                    Run(view, 0.5f);
                    view.Show(Presentation(CharacterActivity.TurnFinished), shown);
                    Run(view, 1.5f);
                    var halo = view.PoseForRender.Halo;
                    if (lit && halo < 0.1f) failures.Add(name + ": no green light with the computer's own passed check (" + halo.ToString("0.00", CultureInfo.InvariantCulture) + ")");
                    if (!lit && halo > 0.01f) failures.Add(name + ": a light glows (" + halo.ToString("0.00", CultureInfo.InvariantCulture) + ") with no passed check of this round");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            return failures;
        }

        private static IEnumerable<string> PutAwaySaysItFirst()
        {
            var failures = new List<string>();
            var (view, root) = Made("put away");
            try
            {
                view.Show(Presentation(CharacterActivity.TurnFinished));
                Run(view, 1f);
                if (view.Label.Badge.Shown?.Word == CharacterLife.PutAwayWord) failures.Add("put away witness: the tag said Put Away before anyone put it away");
                view.PutAway();
                for (var frame = 0; frame < Mathf.RoundToInt(1.15f / Frame); frame++)
                {
                    view.AdvanceForRender(Frame);
                    if (view.Label.Badge.Shown?.Word != CharacterLife.PutAwayWord) { failures.Add("put away: the tag did not say Put Away at " + (frame * Frame).ToString("0.00", CultureInfo.InvariantCulture) + " s"); break; }
                    if (view.Body.localScale.x < 0.999f) { failures.Add("put away: the figure began to fold before its word had stood 1.2 s"); break; }
                }
                Run(view, 0.5f);
                if (view.Body.localScale.x > 1e-4f) failures.Add("put away: the figure had not folded by 1.65 s");
                Run(view, 0.3f);
                if (!view.Gone || view.Label.gameObject.activeInHierarchy) failures.Add("put away: the tag did not go after the fold");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }

        private static IEnumerable<string> ArrivalGrowsAndSaysStarting()
        {
            var failures = new List<string>();
            var (view, root) = Made("arrival");
            try
            {
                view.Show(Presentation(CharacterActivity.Starting));
                view.Arrive();
                view.AdvanceForRender(Frame);
                if (view.Body.localScale.x > 0.05f) failures.Add("arrival: the figure did not start as a seed");
                if (view.Label.Badge.Shown?.State != WorkState.Starting) failures.Add("arrival: the tag did not read Starting from the first frame");
                Run(view, 1f);
                if (Mathf.Abs(view.Body.localScale.x - 1f) > 1e-4f) failures.Add("arrival: the figure was not at full size after 1 s");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }

        private static IEnumerable<string> KeepStillHoldsTheBody()
        {
            var failures = new List<string>();
            var (view, root) = Made("keep still");
            try
            {
                view.Show(Presentation(CharacterActivity.Working));
                Run(view, 1f);
                GlazeMotion.Still = true;
                Run(view, 1f);
                var poses = Run(view, 3f);
                if (poses.Distinct().Count() != 1) failures.Add("keep still: the figure moved (" + poses.Distinct().Count() + " poses in 3 s), blinks included (ADR 0027 item 6)");
            }
            finally { GlazeMotion.Still = false; UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }

        private static IEnumerable<string> SoundsAsTheTableSays()
        {
            var failures = new List<string>();
            const int rate = 48000;
            foreach (CharacterSound sound in Enum.GetValues(typeof(CharacterSound)))
            {
                foreach (KitFigureFeature feature in Enum.GetValues(typeof(KitFigureFeature)))
                {
                    var samples = CharacterSoundPlayer.Render(sound, feature, rate);
                    var tones = CharacterSounds.Tones(sound, feature);
                    var expected = (int)Math.Ceiling(tones.Max(tone => tone.StartMs + tone.DurationMs) / 1000 * rate);
                    var what = sound + " " + feature;
                    if (samples.Length != expected) failures.Add(what + ": " + samples.Length + " samples, not " + expected);
                    var peak = samples.Max(Mathf.Abs);
                    var level = (float)Math.Pow(10, CharacterSounds.LevelDb(sound) / 20);
                    if (peak < level * 0.2f || peak > level * 1.6f) failures.Add(what + ": peak " + peak.ToString("0.0000", CultureInfo.InvariantCulture) + " against its level " + level.ToString("0.0000", CultureInfo.InvariantCulture));
                    if (Mathf.Abs(samples[samples.Length - 1]) > level * 0.01f) failures.Add(what + ": it does not end in silence");
                }
            }
            return failures;
        }
    }
}
