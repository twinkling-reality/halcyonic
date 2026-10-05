#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI.Editor
{
    public static partial class GlazeRender
    {
        /// <summary>
        /// A wait's shimmer on a footer's prompts (ADR 0027), drawn as a strip: Sent… and Hold to talk writing
        /// down, a third, a half and four fifths into the sweep, then under Keep badges still. Its words move
        /// while the prompt waits, stand in their own colours once it no longer does, and stand still, every
        /// letter its own colour, under Keep badges still.
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
            }
            finally
            {
                GlazeMotion.Still = still;
                foreach (var holder in made) Object.DestroyImmediate(holder.gameObject);
                foreach (var holder in hidden) holder.gameObject.SetActive(true);
            }
            return failures;
        }

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
