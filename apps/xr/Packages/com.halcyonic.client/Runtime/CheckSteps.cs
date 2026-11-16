#nullable enable
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>A step of the host's check of a project (ADR 0037), in the order it runs.</summary>
    public enum CheckStepKind
    {
        TypeCheck,
        Build,
        PageDrawn,
        VrOffered,
    }

    /// <summary>What happened to a step, as the host observed it.</summary>
    public enum CheckStepResult
    {
        Passed,
        Failed,
        /// <summary>Stopped at its time limit, so no exit was seen.</summary>
        StoppedAtLimit,
        /// <summary>Not run, or not checked by the host that recorded the check.</summary>
        NotRun,
        /// <summary>Run, and the host could not tell.</summary>
        Unknown,
    }

    /// <summary>One step of a host check and its result; its seconds where the host timed it.</summary>
    public sealed class CheckStepOutcome
    {
        public CheckStepOutcome(CheckStepKind step, CheckStepResult result, double? seconds = null)
        {
            Step = step;
            Result = result;
            Seconds = seconds;
        }

        public CheckStepKind Step { get; }

        public CheckStepResult Result { get; }

        public double? Seconds { get; }
    }

    /// <summary>
    /// A host check's steps from its findings, each with its own result rather than the check's one passed or failed: the
    /// type check and the build by their exits, the page by whether something was drawn (no page, not run; drawn not known,
    /// since the host could not make the starter's picture to compare it with, unknown, never passed or failed), and VR by
    /// whether the page offered a session (absent on checks recorded before VR was checked, not run). A step that passed
    /// never says the game works (ADR 0037).
    /// </summary>
    public static class CheckSteps
    {
        public static IReadOnlyList<CheckStepOutcome> Of(CheckFindings findings)
        {
            CheckStepResult Exit(CheckStep step) =>
                step.Exit == null ? CheckStepResult.StoppedAtLimit : step.Exit == 0 ? CheckStepResult.Passed : CheckStepResult.Failed;
            var page = findings.Page == null ? CheckStepResult.NotRun : findings.Page.Drawn switch
            {
                true => CheckStepResult.Passed,
                false => CheckStepResult.Failed,
                null => CheckStepResult.Unknown,
            };
            var vr = findings.Vr == null ? CheckStepResult.NotRun : (string?)findings.Vr.Offered switch
            {
                "offered" => CheckStepResult.Passed,
                "not_offered" => CheckStepResult.Failed,
                _ => CheckStepResult.Unknown,
            };
            return new[]
            {
                new CheckStepOutcome(CheckStepKind.TypeCheck, Exit(findings.Tsc), findings.Tsc.Seconds),
                new CheckStepOutcome(CheckStepKind.Build, Exit(findings.Build), findings.Build.Seconds),
                new CheckStepOutcome(CheckStepKind.PageDrawn, page),
                new CheckStepOutcome(CheckStepKind.VrOffered, vr),
            };
        }

        /// <summary>
        /// Whether the check's verdict is not known (VRIFY, 2026-11-13): everything it saw passed and only whether the page
        /// draws its own look could not be found out. Such a check never passed, and never failed either; a check recorded
        /// before the verdict reads by <see cref="CheckFindings.Passed"/> alone.
        /// </summary>
        public static bool PartlyChecked(CheckFindings findings) => !findings.Passed && findings.Verdict == CheckFindingsVerdict.NotKnown;

        /// <summary>
        /// Whether the check found the page still the starter's picture alone, nothing new drawn yet (LINK's starter_only, the lead's
        /// 14:11Z: a fresh app never reads as broken). It never passed and never failed: said as Starter Only, quiet, never red.
        /// </summary>
        public static bool StarterOnly(CheckFindings findings) => !findings.Passed && findings.Verdict == CheckFindingsVerdict.StarterOnly;

        /// <summary>AUDIT2's words for it (2026-11-16 14:30Z), the host's own (check.ts STARTER_ONLY).</summary>
        public const string StarterOnlyWord = "Starter Only";

        public const string StarterOnlyLine = "Nothing new is drawn yet. Say what you want to see.";

        /// <summary>
        /// Whether the check's built copy may be served and played, as the host's checkServable says: a pass, or a check whose
        /// verdict is not known. Sharing and publishing still read <see cref="CheckFindings.Passed"/> alone.
        /// </summary>
        public static bool Servable(CheckFindings findings) => findings.Passed || findings.Verdict == CheckFindingsVerdict.NotKnown;
    }
}
