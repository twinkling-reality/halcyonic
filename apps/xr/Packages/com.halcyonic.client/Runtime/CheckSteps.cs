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
    }
}
