#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Unity.Profiling;
using UnityEngine;

namespace Halcyonic.XR.UI.Editor
{
    public static partial class GlazeRender
    {
        /// <summary>
        /// A footer's motion (ADR 0027), drawn as two strips. A wait's shimmer: Sent… and Hold to talk writing
        /// down, a third, a half and four fifths into the sweep, then under Keep badges still; its words, in the
        /// secondary tone so the lift shows, move while the prompt waits, stand in their own colours once it no
        /// longer does, and stand still under Keep badges still. Hold to talk listening: the active tone, its
        /// microphone at three points of its pulse, then under Keep badges still; it grows and shrinks while it
        /// listens, stands at its own size once the voice is idle, and stands still under Keep badges still.
        /// Neither allocates a frame, nor adds a renderer, so neither adds a draw call.
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
                var footers = new List<FooterView>();
                for (var row = 0; row <= phases.Length; row++)
                {
                    var holder = Holder("Motion " + row, 0f, 12f - row * 7f);
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

                    GlazeMotion.Still = true;
                    shimmer.Draw(0.7f * Glaze.ShimmerSeconds);
                    if (shimmer.Lifted || !InOwnColours(label)) failures.Add("component render: " + what + "'s words still moved under Keep badges still.");
                    GlazeMotion.Still = false;

                    shimmer.Draw(0.7f * Glaze.ShimmerSeconds);
                    // The wait over, the same footer shown again without it: the shimmer stops and every letter is its own colour.
                    probe.Show(new Footer(close, secondary: new Prompt(FileScreens.SpeakAnswer, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true),
                        farRight: new Prompt(FileScreens.SendAnswer, WorkspaceText.Label(WorkspaceAction.Answer), GlazeIcon.SendAnswer, main: true)), -half, half, 0f);
                    shimmer.Draw(0.7f * Glaze.ShimmerSeconds);
                    if (shimmer.Waits || shimmer.Lifted || !InOwnColours(label)) failures.Add("component render: " + what + "'s words kept moving once it no longer waits.");
                    probe.Show(waiting, -half, half, 0f);
                    foreach (var (_, button) in probe.Shown) button.Label.ForceMeshUpdate();
                }
                failures.AddRange(MotionAllocatesNothing("a wait's shimmer", now => footers[0].ShimmerOf(PromptSlot.Secondary)!.Draw(now)));
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
                    var holder = Holder("Listening " + row, 0f, 12f - row * 7f);
                    made.Add(holder);
                    var footer = FooterView.Create(holder, "Footer", 0);
                    footer.Show(listening, -half, half, 0f);
                    talks.Add(footer[PromptSlot.Secondary]!);
                }
                GlazeMotion.Still = false;
                for (var row = 0; row < pulses.Length; row++) talks[row].Pulse(pulses[row] * Glaze.ListeningPulseSeconds);
                GlazeMotion.Still = true;
                talks[pulses.Length].Pulse(0.5f * Glaze.ListeningPulseSeconds);
                GlazeMotion.Still = false;
                File.WriteAllBytes(Path.Combine(folder, "gallery-listening.png"), Render(camera, texture).EncodeToPNG());

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
                if (!Mathf.Approximately(held.IconGrowth, 1f)) failures.Add("component render: Hold to talk's microphone still pulsed under Keep badges still.");
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
            }
            finally
            {
                GlazeMotion.Still = still;
                foreach (var holder in made) Object.DestroyImmediate(holder.gameObject);
                foreach (var holder in hidden) holder.gameObject.SetActive(true);
            }
            return failures;
        }

        /// <summary>
        /// A motion's frames allocate nothing (ADR 0027): <paramref name="draw"/> called at sixty frames' times,
        /// the fewest bytes of three tries, as the drag's check counts them.
        /// </summary>
        private static IEnumerable<string> MotionAllocatesNothing(string what, System.Action<float> draw)
        {
            var failures = new List<string>();
            using var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            for (var frame = 0; frame < 4; frame++) draw(frame / 72f);
            var probe = recorder.CurrentValue;
            var kept = new byte[256];
            if (recorder.CurrentValue - probe < kept.Length)
            {
                failures.Add("component render: this editor cannot count allocations, so " + what + " cannot be checked.");
                return failures;
            }
            var bytes = long.MaxValue;
            for (var repeat = 0; repeat < 3; repeat++)
            {
                var before = recorder.CurrentValue;
                for (var frame = 0; frame < 60; frame++) draw(frame / 72f);
                bytes = System.Math.Min(bytes, recorder.CurrentValue - before);
            }
            if (bytes > 0) failures.Add("component render: sixty frames of " + what + " allocate " + bytes + " bytes; a frame of motion allocates nothing.");
            Debug.Log("Halcyonic: component render: sixty frames of " + what + " allocate " + bytes + " bytes.");
            return failures;
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
