#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The composer's Host and Agent chips for the runtimes the owner's Mac registers and the local models by their own
    /// long names, each worded by production (KitPresenter.ChipsOf, KitMomentWords' HostChip and AgentChip): the chips of
    /// the execution the composer's Send instructs, both locked as facts, since no command carries another runtime. Every
    /// chip must show its words whole, stand inside the composer, and keep the footer's columns: two chips share a row
    /// only at the row's equal halves, else each takes the full width. The runtimes are synthetic descriptors declaring as
    /// the real ones do on macOS (Codex that an approved command may leave the sandbox, OpenCode that it stays inside one,
    /// Claude Agent nothing), and every image says synthetic data.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] ChipStates =
        {
            "28-chips-codex", "28-chips-sandbox", "28-chips-claude-agent", "28-chips-qwen", "28-chips-glimmer-sandbox", "28-chips-named-agent",
        };

        private static RuntimeDescriptor ChipRuntime(string id, string name, bool? mayLeave) => new RuntimeDescriptor
        {
            RuntimeId = "synthetic-" + id,
            Kind = id,
            DisplayName = name,
            Synthetic = true,
            ApprovalMayLeaveSandbox = mayLeave,
            Capabilities = new RuntimeCapabilities { StartExecution = true },
        };

        private static Shot ChipShot(string state, Transform root, Vector3 eyes)
        {
            var codex = ChipRuntime("codex", "Codex", true);
            var openCode = ChipRuntime("opencode", "OpenCode", false);
            var claude = ChipRuntime("claude", "Claude Agent", null);
            var named = ChipRuntime("named", "Synthetic agent app", true);
            // What each state stands for: the runtime the task's execution runs on, and the model it runs as its list names it.
            var (runtime, model, caption) = state switch
            {
                "28-chips-codex" => (codex, (string?)null, "Host and Agent chips: Codex on This Mac"),
                "28-chips-sandbox" => (openCode, null, "Host and Agent chips: OpenCode in its sandbox"),
                "28-chips-claude-agent" => (claude, null, "Host and Agent chips: Claude Agent, which declares no environment"),
                "28-chips-qwen" => (codex, "qwen3.6:35b-a3b-nvfp4 (Ollama)", "Host and Agent chips: a local model's long name on This Mac"),
                "28-chips-glimmer-sandbox" => (openCode, "muse-glimmer:30b-mlx (Ollama)", "Host and Agent chips: a local model's long name in a sandbox"),
                _ => (named, null, "Host and Agent chips: an agent app whose own name says agent"),
            };
            var chips = KitPresenter.ChipsOf(runtime, null, model);
            // A short report: these states are about the chips. Where a long name stacks them, the report's room is a row
            // shorter and the long report pages, as the slate's reading always does (KitComposer.GrowsSlateBy).
            var work = WithSelectors(WorkModel(), chips, ChipReport);
            return FocusedWork(root, eyes, true, work, caption + ", synthetic data", (view, shot) => RequireChips(view, shot, chips.Environment, chips.Agent));
        }

        /// <summary>The agent's report for the chip states, synthetic: two sentences, so the chips are what they show.</summary>
        private static readonly ActivityEntry ChipReport = new ActivityEntry(8, Day + "09:02:48.000Z", ActivityKind.Message,
            "I separated the account limit from the address limit. The sign-in tests run next.", true);

        private static KitWorkModel WithSelectors(KitWorkModel work, KitSelectors selectors, ActivityEntry? report = null) => new KitWorkModel(work.ProjectName,
            work.TaskTitle, work.Badge, work.Marks, report ?? work.Report, work.Trail, work.Draft, work.VoiceOffered, work.Sendable, work.WhyNotSend, work.Spine,
            work.SpineFoot, work.Need, work.Ask, work.Notice, work.Avatar, work.Said, selectors.Environment, selectors.Agent, selectors.EnvironmentChangeable,
            selectors.AgentChangeable);

        /// <summary>The chips as drawn: their words whole, inside the composer, on the footer's columns, both locked, as no press can change what runs.</summary>
        private static void RequireChips(KitWorkView view, Shot shot, string? environment, string? agent)
        {
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var composer = view.Composer;
            var host = composer.EnvironmentButton;
            var agentChip = composer.AgentButton;
            Require(host.gameObject.activeSelf == (environment != null), environment == null
                ? "a Host chip shows for a runtime that declares no environment" : "no Host chip shows for " + environment);
            Require(agentChip.gameObject.activeSelf == (agent != null), "the Agent chip's showing does not follow the agent's words");
            var shown = new List<(KitButton Chip, string Words)>();
            if (environment != null && host.gameObject.activeSelf) shown.Add((host, KitMomentWords.HostChip(environment)));
            if (agent != null && agentChip.gameObject.activeSelf) shown.Add((agentChip, KitMomentWords.AgentChip(agent)));
            var width = composer.Size.x;
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            const float Tolerance = 1e-4f;
            foreach (var (chip, words) in shown)
            {
                chip.Words.ForceMeshUpdate(true);
                Require(chip.Words.text == LabelText.ForTextMeshPro(words), "the chip reads \"" + chip.Words.text + "\", not \"" + words + "\"");
                var needs = chip.Words.GetPreferredValues(chip.Words.text).x;
                var room = chip.Words.rectTransform.sizeDelta.x;
                Require(!chip.Words.isTextTruncated && chip.Words.textInfo.lineCount == 1 && needs <= room + Tolerance,
                    "the chip's words \"" + words + "\" are cut: they need " + needs.ToString("0.0000") + " where the chip gives " + room.ToString("0.0000"));
                var left = chip.transform.localPosition.x - chip.Size.x / 2f;
                var right = chip.transform.localPosition.x + chip.Size.x / 2f;
                Require(left >= -Tolerance && right <= width + Tolerance,
                    "the chip \"" + words + "\" runs from " + left.ToString("0.0000") + " to " + right.ToString("0.0000") + ", past the composer's 0 to " + width.ToString("0.0000"));
                // Two chips on one row stand at its equal halves; a chip alone on its row takes the whole width.
                var rowMates = shown.Count(other => Mathf.Abs(other.Chip.transform.localPosition.y - chip.transform.localPosition.y) < Tolerance);
                var column = rowMates == 2 ? (width - gap) / 2f : width;
                Require(Mathf.Abs(chip.Size.x - column) < Tolerance,
                    "the chip \"" + words + "\" is " + chip.Size.x.ToString("0.0000") + " wide on a row of " + rowMates + ", off the footer's column of " + column.ToString("0.0000"));
            }
            foreach (var (chip, words) in shown)
                Require(!chip.Available, "the chip \"" + words + "\" takes presses though no command carries another runtime");
            if (shown.Count > 0)
                Debug.Log("Halcyonic: spatial kit: chips " + string.Join(" | ", shown.Select(each => "\"" + each.Words + "\" " + each.Chip.Size.x.ToString("0.0000")
                    + " at " + each.Chip.transform.localPosition.y.ToString("0.0000") + (each.Chip.Available ? "" : " locked")))
                    + "; composer " + width.ToString("0.0000") + " wide.");
        }
    }
}
