#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using UnityEngine;

namespace Halcyonic.XR.UI.Editor
{
    public static partial class GlazeRender
    {
        /// <summary>
        /// The motion strips' first row, in degrees up: below the gallery's top right, where this editor's render
        /// draws words leaning though their mesh and transform stand upright (seen 2026-10-07, cause not found).
        /// </summary>
        private const float StripTop = 5f;

        /// <summary>
        /// Motion (ADR 0027), drawn as three strips. A wait's shimmer: Sent… and Hold to talk writing
        /// down, a third, a half and four fifths into the sweep, then under Keep things still; its words, in the
        /// secondary tone so the lift shows, move while the prompt waits, stand in their own colours once it no
        /// longer does, and under Keep things still stand steady in the active tone, a still highlight that holds
        /// 4.5:1 on the glass over white and gives way to their own colours once the wait ends. Hold to talk listening: the active tone, its
        /// microphone at three points of its pulse, then under Keep things still; it grows and shrinks while it
        /// listens, stands at its own size once the voice is idle, and stands still under Keep things still.
        /// A badge changing state: its colours cross-fade over a state's time, easing in and out, under Keep
        /// things still too. None allocates a frame, nor adds a renderer, so none adds a draw call.
        /// </summary>
        private static IEnumerable<string> Motion(string folder, Camera camera, RenderTexture texture)
        {
            var failures = new List<string>();
            var hidden = new List<Transform>();
            foreach (Transform holder in gallery)
            {
                if (holder.gameObject.activeSelf && holder.name != "Panel") hidden.Add(holder);
            }
            foreach (var holder in hidden) holder.gameObject.SetActive(false);
            var made = new List<Transform>();
            var still = GlazeMotion.Still;
            try
            {
                var close = new Prompt(Footer.Close, EntryText.Close, GlazeIcon.Close, PromptKind.Close);
                var sent = new Prompt(WorkspaceScreens.Sent, WorkspaceText.Sent, GlazeIcon.SendAnswer, main: true, available: false, reason: FileScreens.SentWaiting, waits: true);
                var writing = new Prompt(FileScreens.SpeakAnswer, VoiceText.WritingDownWords, GlazeIcon.WritingDown, holds: true, waits: true);
                var waiting = new Footer(close, secondary: writing, farRight: sent);
                var half = GlazeTokens.Units(Glaze.Menu.FileColumnDegrees) / 2f;
                var phases = new[] { 1f / 3f, 0.5f, 0.8f };
                // A wait's still highlight: the active tone's text, as "Listening" is drawn.
                var highlight = GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Foreground);
                var footers = new List<FooterView>();
                for (var row = 0; row <= phases.Length; row++)
                {
                    var holder = Holder("Motion " + row, 0f, StripTop - row * 4.5f);
                    made.Add(holder);
                    var footer = FooterView.Create(holder, "Footer", 0);
                    footer.Show(waiting, -half, half, 0f);
                    footers.Add(footer);
                }

                foreach (var footer in footers)
                {
                    foreach (var (_, button) in footer.Shown) button.Label.ForceMeshUpdate();
                }
                GlazeMotion.Still = false;
                for (var row = 0; row < phases.Length; row++)
                {
                    foreach (var slot in new[] { PromptSlot.Secondary, PromptSlot.FarRight }) footers[row].ShimmerOf(slot)?.Draw(phases[row] * Glaze.ShimmerSeconds);
                }
                GlazeMotion.Still = true;
                foreach (var slot in new[] { PromptSlot.Secondary, PromptSlot.FarRight }) footers[phases.Length].ShimmerOf(slot)?.Draw(0.5f * Glaze.ShimmerSeconds);
                GlazeMotion.Still = false;
                File.WriteAllBytes(Path.Combine(folder, "gallery-motion.png"), Render(camera, texture).EncodeToPNG());

                foreach (var (slot, what) in new[] { (PromptSlot.FarRight, "Sent…"), (PromptSlot.Secondary, "Hold to talk writing down") })
                {
                    var probe = footers[0];
                    var shimmer = probe.ShimmerOf(slot);
                    var label = probe[slot]?.Label;
                    if (shimmer == null || label == null)
                    {
                        failures.Add("component render: " + what + " has no shimmer on its words.");
                        continue;
                    }
                    if (!shimmer.Waits) failures.Add("component render: " + what + " waits, but its shimmer is off.");
                    // In the secondary tone, the band's lift toward white shows: on near-white words it would not.
                    shimmer.Draw(-1f);
                    var own = Colours(label);
                    shimmer.Draw(0.5f * Glaze.ShimmerSeconds);
                    var lift = own.Zip(Colours(label), (before, after) => after.r - before.r).DefaultIfEmpty(0).Max();
                    if (lift < 30) failures.Add("component render: " + what + "'s shimmer lifts its words by " + lift + " of 255 at most, too little to see; a wait's words draw in the secondary tone.");
                    shimmer.Draw(0.3f * Glaze.ShimmerSeconds);
                    var early = Colours(label);
                    shimmer.Draw(0.7f * Glaze.ShimmerSeconds);
                    var late = Colours(label);
                    if (!shimmer.Lifted || early.SequenceEqual(late)) failures.Add("component render: " + what + "'s words did not move while it waits.");

                    // Kept still, the wait shows a still highlight: every letter steady in the active tone, wherever the sweep would be.
                    GlazeMotion.Still = true;
                    foreach (var phase in new[] { 0f, 0.3f, 0.5f, 0.7f, 0.95f })
                    {
                        shimmer.Draw(phase * Glaze.ShimmerSeconds);
                        if (shimmer.Lifted || !shimmer.Highlighted || !AllIn(label, highlight))
                        {
                            failures.Add("component render: " + what + "'s words, " + phase + " into the sweep under Keep things still, are not every letter steady in the active tone.");
                        }
                    }
                    GlazeMotion.Still = false;

                    // Let move again, the highlight gives way to the shimmer.
                    shimmer.Draw(0.7f * Glaze.ShimmerSeconds);
                    if (shimmer.Highlighted || !shimmer.Lifted) failures.Add("component render: " + what + "'s still highlight stayed once things move again.");
                    // The wait over, the same footer shown again without it: the shimmer stops and every letter is its own colour.
                    probe.Show(new Footer(close, secondary: new Prompt(FileScreens.SpeakAnswer, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true),
                        farRight: new Prompt(FileScreens.SendAnswer, WorkspaceText.Label(WorkspaceAction.Answer), GlazeIcon.SendAnswer, main: true)), -half, half, 0f);
                    shimmer.Draw(0.7f * Glaze.ShimmerSeconds);
                    if (shimmer.Waits || shimmer.Lifted || !InOwnColours(label)) failures.Add("component render: " + what + "'s words kept moving once it no longer waits.");
                    GlazeMotion.Still = true;
                    shimmer.Draw(0.7f * Glaze.ShimmerSeconds);
                    if (shimmer.Highlighted || !InOwnColours(label)) failures.Add("component render: " + what + "'s words kept the still highlight once it no longer waits.");
                    GlazeMotion.Still = false;
                    probe.Show(waiting, -half, half, 0f);
                    foreach (var (_, button) in probe.Shown) button.Label.ForceMeshUpdate();
                }
                failures.AddRange(MotionAllocatesNothing("a wait's shimmer", now => footers[0].ShimmerOf(PromptSlot.Secondary)!.Draw(now)));
                GlazeMotion.Still = true;
                failures.AddRange(MotionAllocatesNothing("a wait's still highlight", now => footers[0].ShimmerOf(PromptSlot.Secondary)!.Draw(now)));
                GlazeMotion.Still = false;
                // The still highlight's words hold 4.5:1 on the menu's glass over white, the brightest room behind it.
                var onGlass = GlazeChecks.Contrast(highlight, GlazeTokens.ColorOf(Glaze.Menu.GlassOverWhite));
                if (onGlass < 4.5f) failures.Add("component render: a wait's still highlight holds " + onGlass.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":1 on the glass over white; 4.5:1 at least.");
                // The same prompts, none waiting, draw with as many renderers: a wait adds no draw call.
                var talk = new Prompt(FileScreens.SpeakAnswer, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true);
                var renderers = footers[0].GetComponentsInChildren<Renderer>(false).Length;
                footers[0].Show(new Footer(close, secondary: talk,
                    farRight: new Prompt(WorkspaceScreens.Sent, WorkspaceText.Sent, GlazeIcon.SendAnswer, main: true, available: false, reason: FileScreens.SentWaiting)), -half, half, 0f);
                if (footers[0].GetComponentsInChildren<Renderer>(false).Length != renderers)
                {
                    failures.Add("component render: a waiting footer draws " + renderers + " renderers, not as many as the same prompts not waiting; a wait adds no draw call.");
                }

                // Hold to talk listening, the voice's stage drawn on it (Prompt.Voiced): the active tone and a pulsing microphone.
                foreach (var holder in made) holder.gameObject.SetActive(false);
                var listening = new Footer(close, secondary: talk.Voiced(VoiceStage.Listening));
                var pulses = new[] { 0.15f, 0.3f, 0.5f };
                var talks = new List<GlazeButton>();
                for (var row = 0; row <= pulses.Length; row++)
                {
                    var holder = Holder("Listening " + row, 0f, StripTop - row * 4.5f);
                    made.Add(holder);
                    var footer = FooterView.Create(holder, "Footer", 0);
                    footer.Show(listening, -half, half, 0f);
                    talks.Add(footer[PromptSlot.Secondary]!);
                }
                foreach (var each in talks) each.Label.ForceMeshUpdate();
                GlazeMotion.Still = false;
                for (var row = 0; row < pulses.Length; row++) talks[row].Pulse(pulses[row] * Glaze.ListeningPulseSeconds);
                GlazeMotion.Still = true;
                talks[pulses.Length].Pulse(0.5f * Glaze.ListeningPulseSeconds);
                GlazeMotion.Still = false;
                File.WriteAllBytes(Path.Combine(folder, "gallery-listening.png"), Render(camera, texture).EncodeToPNG());
                foreach (var each in talks)
                {
                    // Halcyonic's own words never lean: only an agent's do.
                    if (Mathf.Abs(Shear(each.Label)) > 0.05f) failures.Add("component render: Hold to talk listening leans " + Shear(each.Label) + "; Halcyonic's own words never lean.");
                }

                var held = talks[0];
                var active = GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Active).Foreground);
                if (!held.Listens || held.Label.text != VoiceText.ListeningWords || !Near(held.Label.color, active))
                {
                    failures.Add("component render: Hold to talk listening reads \"" + held.Label.text + "\", listening " + held.Listens + ", not \"Listening\" in the active tone.");
                }
                held.Pulse(0.1f * Glaze.ListeningPulseSeconds);
                var small = held.IconGrowth;
                held.Pulse(0.5f * Glaze.ListeningPulseSeconds);
                var swollen = held.IconGrowth;
                if (swollen - small < Glaze.ListeningPulseDepth / 2f) failures.Add("component render: Hold to talk's microphone grew " + (swollen - small) + " while it listens; it pulses.");
                if (Mathf.Abs(swollen - 1f - Glaze.ListeningPulseDepth) > 1e-3f) failures.Add("component render: at the top of its pulse the microphone grew " + (swollen - 1f) + ", not " + Glaze.ListeningPulseDepth + ".");
                GlazeMotion.Still = true;
                held.Pulse(0.5f * Glaze.ListeningPulseSeconds);
                if (!Mathf.Approximately(held.IconGrowth, 1f)) failures.Add("component render: Hold to talk's microphone still pulsed under Keep things still.");
                GlazeMotion.Still = false;
                failures.AddRange(MotionAllocatesNothing("the listening pulse", held.Pulse));
                held.Pulse(0.5f * Glaze.ListeningPulseSeconds);
                // The voice idle again, the same footer shown with Hold to talk as it was: the microphone at its own size.
                var idle = held.GetComponentInParent<FooterView>();
                idle.Show(new Footer(close, secondary: talk), -half, half, 0f);
                held.Pulse(0.5f * Glaze.ListeningPulseSeconds);
                if (held.Listens || !Mathf.Approximately(held.IconGrowth, 1f) || Near(held.Label.color, active))
                {
                    failures.Add("component render: the voice idle, Hold to talk still listened, its microphone at " + held.IconGrowth + " of its size.");
                }

                // A badge changing from Working to Waiting for you: its colours cross-fade over a state's time, easing
                // in and out, at none, a quarter, half and all of it, then half under Keep things still, since a change is no loop.
                foreach (var holder in made) holder.gameObject.SetActive(false);
                var working = StateLanguage.BadgeOf(Character(CharacterActivity.Working, AttentionLevel.None));
                var steps = new[] { 0f, 0.25f, 0.5f, 1f, 0.5f };
                var badges = new List<StateBadgeView>();
                for (var row = 0; row < steps.Length; row++)
                {
                    var holder = Holder("State " + row, 0f, StripTop - row * 4.5f);
                    made.Add(holder);
                    var each = StateBadgeView.Create(holder, "Badge", 1);
                    each.Show(working);
                    badges.Add(each);
                }
                var (workingFill, workingWord) = (badges[0].DrawnFill, badges[0].Word.color);
                // The editor runs no frames, so there a badge takes its new state at once unless a render steps it.
                badges[0].Show(Waiting);
                if (badges[0].Changing) failures.Add("component render: in the editor a badge shown another state is still cross-fading, which no frame would end.");
                var (waitingFill, waitingWord) = (badges[0].DrawnFill, badges[0].Word.color);
                if (Near(workingFill, waitingFill)) failures.Add("component render: Working and Waiting for you fill alike, so a badge's change shows nothing.");
                badges[0].Show(working);
                StateBadgeView.CrossFadesInEditor = true;
                for (var row = 0; row < steps.Length; row++)
                {
                    GlazeMotion.Still = row == steps.Length - 1;
                    badges[row].Show(Waiting);
                    badges[row].Change(steps[row] * Glaze.StateSeconds);
                }
                GlazeMotion.Still = false;
                File.WriteAllBytes(Path.Combine(folder, "gallery-state.png"), Render(camera, texture).EncodeToPNG());
                for (var row = 0; row < steps.Length; row++)
                {
                    var along = Glaze.EaseInOut(steps[row]);
                    var when = (steps[row] * 100f).ToString("0") + " percent through its change" + (row == steps.Length - 1 ? " under Keep things still" : "");
                    if (!Near(badges[row].DrawnFill, Color.Lerp(workingFill, waitingFill, along)))
                    {
                        failures.Add("component render: a badge changing from Working to Waiting for you, " + when + ", does not draw its pill "
                            + (along * 100f).ToString("0") + " percent of the way.");
                    }
                    if (!Near(badges[row].Word.color, workingWord) && !Near(badges[row].Word.color, waitingWord))
                    {
                        failures.Add("component render: a badge changing from Working to Waiting for you, " + when + ", draws its word in neither state's colour.");
                    }
                    if (badges[row].Changing != steps[row] < 1f) failures.Add("component render: a badge " + when + " is " + (badges[row].Changing ? "still" : "no longer") + " changing.");
                }
                // Each way, at every twentieth of the change, the word reads on the pill as drawn over a plate:
                // at least 3:1, as large text must, for the quarter second it lasts.
                var plate = GlazeTokens.ColorOf(Glaze.Panel);
                foreach (var (from, to, way) in new[] { (working, Waiting, "Working to Waiting for you"), (Waiting, working, "Waiting for you to Working") })
                {
                    var probe = badges[1];
                    StateBadgeView.CrossFadesInEditor = false;
                    probe.Show(from);
                    StateBadgeView.CrossFadesInEditor = true;
                    probe.Show(to);
                    var faintest = float.MaxValue;
                    for (var step = 0; step <= 20; step++)
                    {
                        if (step > 0) probe.Change(Glaze.StateSeconds / 20f);
                        var fill = probe.DrawnFill;
                        faintest = Mathf.Min(faintest, GlazeChecks.Contrast(probe.Word.color, GlazeChecks.Over(new Color(fill.r, fill.g, fill.b), fill.a, plate)));
                    }
                    if (faintest < 3f) failures.Add("component render: a badge changing from " + way + " drops its word to " + faintest.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":1 on its pill; it holds 3:1 throughout.");
                    Debug.Log("Halcyonic: component render: a badge changing from " + way + " keeps its word at " + faintest.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ":1 or more on its pill.");
                }
                badges[1].Show(working);
                badges[1].Show(Waiting);
                failures.AddRange(MotionAllocatesNothing("a badge's change", _ => badges[1].Change(Glaze.StateSeconds / 1000f)));
            }
            finally
            {
                StateBadgeView.CrossFadesInEditor = false;
                GlazeMotion.Still = still;
                foreach (var holder in made) Object.DestroyImmediate(holder.gameObject);
                foreach (var holder in hidden) holder.gameObject.SetActive(true);
            }
            return failures;
        }

        /// <summary>
        /// A motion's frames allocate nothing (ADR 0027): <paramref name="draw"/> called at sixty frames' times,
        /// a frame failing only where it allocated in every try (<see cref="GlazeChecks.Allocations"/>).
        /// </summary>
        private static IEnumerable<string> MotionAllocatesNothing(string what, System.Action<float> draw)
        {
            var failures = new List<string>();
            if (!(GlazeChecks.Allocations(frame => draw(frame / 72f), 60) is (int every, int some, long bytes)))
            {
                failures.Add("component render: this editor cannot count allocations, so " + what + " cannot be checked.");
                return failures;
            }
            if (every > 0) failures.Add("component render: " + every + " of sixty frames of " + what + " allocate in each of 3 tries; a frame of motion allocates nothing.");
            Debug.Log("Halcyonic: component render: sixty frames of " + what + ": " + every + " allocate in every try, " + some + " in some; the quietest try counts "
                + bytes + " bytes on every thread.");
            return failures;
        }

        /// <summary>How far a label's first letter's top stands right of its foot, for each unit of its height, as its mesh is now.</summary>
        private static float Shear(TMPro.TMP_Text label)
        {
            var info = label.textInfo;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible) continue;
                var vertices = info.meshInfo[character.materialReferenceIndex].vertices;
                var foot = vertices[character.vertexIndex];
                var top = vertices[character.vertexIndex + 1];
                return (top.x - foot.x) / Mathf.Max(top.y - foot.y, 1e-6f);
            }
            return 0f;
        }

        /// <summary>Two colours the same to the eye, alpha aside.</summary>
        private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

        /// <summary>The colour of each visible letter's first corner, as drawn now.</summary>
        private static List<Color32> Colours(TMPro.TMP_Text label)
        {
            var colours = new List<Color32>();
            var info = label.textInfo;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (character.isVisible) colours.Add(info.meshInfo[character.materialReferenceIndex].colors32[character.vertexIndex]);
            }
            return colours;
        }

        /// <summary>Every visible letter is drawn in <paramref name="colour"/>, at its own opacity.</summary>
        private static bool AllIn(TMPro.TMP_Text label, Color colour)
        {
            Color32 wanted = colour;
            var info = label.textInfo;
            var any = false;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible) continue;
                var drawn = info.meshInfo[character.materialReferenceIndex].colors32[character.vertexIndex];
                if (drawn.r != wanted.r || drawn.g != wanted.g || drawn.b != wanted.b) return false;
                any = true;
            }
            return any;
        }

        /// <summary>Every visible letter is drawn in its own colour.</summary>
        private static bool InOwnColours(TMPro.TMP_Text label)
        {
            var info = label.textInfo;
            for (var index = 0; index < info.characterCount; index++)
            {
                var character = info.characterInfo[index];
                if (!character.isVisible) continue;
                var drawn = info.meshInfo[character.materialReferenceIndex].colors32[character.vertexIndex];
                if (!drawn.Equals(character.color)) return false;
            }
            return true;
        }
    }
}
