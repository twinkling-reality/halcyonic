#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>The state of a task as the person reads it, one word each, the same everywhere.</summary>
    public enum WorkState
    {
        NotStarted,
        Starting,
        Working,
        CheckingItsWork,

        /// <summary>An approval or a question waits for the person.</summary>
        WaitingForYou,

        /// <summary>The round ended. That says nothing about whether the work is right.</summary>
        FinishedThisRound,

        /// <summary>The round ended after checks that did not pass.</summary>
        ChecksFailed,
        CouldNotFinish,
        Stopped,

        /// <summary>Halcyonic cannot see the work now; shown instead of a guess.</summary>
        CantTellYet,
    }

    /// <summary>
    /// An icon by what it means: a state, a mark or an action. The Unity layer maps each to a glyph of
    /// the one icon set (Material Symbols Rounded, filled); no other code knows a glyph. An icon always
    /// stands beside the words it goes with, never in their place.
    /// </summary>
    public enum GlazeIcon
    {
        NotStarted,
        Starting,
        Working,
        CheckingItsWork,
        WaitingForYou,
        FinishedThisRound,
        ChecksFailed,
        CouldNotFinish,
        Stopped,
        CantTellYet,
        LastKnown,
        Practice,
        Recording,

        // Actions, each on the button that takes it, beside its words.

        Approve,
        Deny,

        /// <summary>Stop: the Stopped state's glyph, as the spec draws both.</summary>
        Stop,
        TellIt,
        SendAnswer,

        /// <summary>Hold to talk, and only that: never on approving, denying, stopping or any confirmation.</summary>
        HoldToTalk,

        /// <summary>Typing an answer instead of choosing one.</summary>
        Type,
        StartBuilding,
        StartOver,

        /// <summary>Refresh, and Try again.</summary>
        Refresh,
        Change,
        ConnectProjects,
        CreateProject,
        AddTask,
        OpenNow,
        KeepCreating,
        NotNow,

        /// <summary>Close, and Cancel.</summary>
        Close,
        Back,
        Next,
        Move,
        ResetPosition,

        /// <summary>Show every project on the stage.</summary>
        ShowAll,
        Settings,
        UsageLeft,

        /// <summary>A final press locked until everything it confirms has been read.</summary>
        Locked,

        // What a file holds, generic only, beside its name (ADR 0026): never a language's or a brand's logo.

        /// <summary>Source code.</summary>
        CodeFile,

        /// <summary>A database, or a schema or migration for one.</summary>
        DatabaseFile,

        /// <summary>Structured data: JSON, YAML, TOML and the like.</summary>
        DataFile,

        /// <summary>Writing: a README, notes, docs, and any file whose kind isn't known.</summary>
        TextFile,

        /// <summary>A picture.</summary>
        ImageFile,

        /// <summary>A script or command to run.</summary>
        ScriptFile,

        /// <summary>A build or package: a lockfile, a manifest, a container.</summary>
        PackageFile,

        Folder,

        /// <summary>A line that opens more beside it: the chevron.</summary>
        OpensMore,

        /// <summary>Hold to talk let go, while the computer writes down what was said (ADR 0027).</summary>
        WritingDown,
    }

    /// <summary>How a badge is filled: outline only, a soft container of its tone, or its tone's solid fill.</summary>
    public enum BadgeFill
    {
        Outline,
        Soft,
        Solid,
    }

    /// <summary>A badge's edge, the shape cue beside its colour: none, a solid line, or a dashed one.</summary>
    public enum BadgeEdge
    {
        None,
        Solid,
        Dashed,
    }

    /// <summary>
    /// A task's state badge: a word, an icon, a tone, a fill and an edge, with a motion for the
    /// states that move. No two states share an icon, so the state reads without the colour, and the
    /// word is always there, so it reads without the icon.
    /// </summary>
    public sealed class StateBadge
    {
        public StateBadge(WorkState state, string word, GlazeTone tone, GlazeIcon icon, BadgeFill fill, BadgeEdge edge,
            bool turns, bool breathes, int count, bool lastKnown)
        {
            State = state;
            Word = word;
            Tone = tone;
            Icon = icon;
            Fill = fill;
            Edge = edge;
            Turns = turns;
            Breathes = breathes;
            Count = count;
            LastKnown = lastKnown;
        }

        public WorkState State { get; }

        /// <summary>The state's word: <see cref="StateLanguage.WordOf"/>.</summary>
        public string Word { get; }

        public GlazeTone Tone { get; }

        /// <summary>The state's icon, or <see cref="GlazeIcon.LastKnown"/> while the state is only the last known one.</summary>
        public GlazeIcon Icon { get; }

        public BadgeFill Fill { get; }

        public BadgeEdge Edge { get; }

        /// <summary>The icon turns slowly: the work is moving.</summary>
        public bool Turns { get; }

        /// <summary>The badge breathes slowly: something needs the person. Only Waiting for you does.</summary>
        public bool Breathes { get; }

        /// <summary>How many things wait for the person, shown after the word when more than one; 0 otherwise.</summary>
        public int Count { get; }

        /// <summary>The state is the last known one while the session is not live: the badge is ghosted and still.</summary>
        public bool LastKnown { get; }

        /// <summary>The word with its count, as the badge shows it: "Waiting for you · 2".</summary>
        public string Text => Count > 1 ? Word + " · " + Count : Word;
    }

    /// <summary>A mark beside the badge saying where the work comes from: practice, a demo or a recording.</summary>
    public sealed class WorkMark
    {
        public WorkMark(string word, GlazeIcon icon)
        {
            Word = word;
            Icon = icon;
        }

        public string Word { get; }

        public GlazeIcon Icon { get; }
    }

    /// <summary>
    /// The state language (ADR 0023): one mapping from every state of a task to the word, tone, icon,
    /// fill, edge and motion its badge shows, beside the character's own cues
    /// (<see cref="CharacterCues"/>). Simulated, recorded and demo work is marked beside the badge,
    /// never folded into the state's word, and a stale state keeps its word and is ghosted.
    /// </summary>
    public static class StateLanguage
    {
        public const string Practice = "Practice";
        public const string Demo = "Demo";
        public const string Recorded = "Recorded";

        /// <summary>
        /// The state a task is in. Something waiting for the person wins over what the work is doing,
        /// as the character's warm halo does (<see cref="CharacterCues"/>); a finished round with a
        /// notice is one whose checks did not pass, as the domain derives it.
        /// </summary>
        public static WorkState StateOf(CharacterActivity activity, AttentionLevel attention)
        {
            if (attention == AttentionLevel.ActionRequired || activity == CharacterActivity.WaitingForHuman) return WorkState.WaitingForYou;
            return activity switch
            {
                CharacterActivity.Idle => WorkState.NotStarted,
                CharacterActivity.Starting => WorkState.Starting,
                CharacterActivity.Working => WorkState.Working,
                CharacterActivity.Verifying => WorkState.CheckingItsWork,
                CharacterActivity.TurnFinished => attention == AttentionLevel.Notice ? WorkState.ChecksFailed : WorkState.FinishedThisRound,
                CharacterActivity.Failed => WorkState.CouldNotFinish,
                CharacterActivity.Interrupted => WorkState.Stopped,
                CharacterActivity.Unknown => WorkState.CantTellYet,
                _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, "Unhandled activity."),
            };
        }

        /// <summary>
        /// Why Halcyonic can't tell what a task is doing, in a plain sentence, by the code of the reason the
        /// control plane gives (`runtime_connection_lost`, `control_plane_restarted`, `start_outcome_unknown`);
        /// null for a code it doesn't know. Never the reason's message: that is an agent app's or the
        /// control plane's own diagnostic, naming an app, a request or a path, which a person never reads.
        /// </summary>
        public static string? CantTellWhy(string? code) => code switch
        {
            "runtime_connection_lost" => LostTouch,
            "control_plane_restarted" => HostText.YourStart + " restarted and lost touch with the agent app.",
            "start_outcome_unknown" => "Not sure it started.",
            _ => null,
        };

        /// <summary>Said where the agent app stopped answering about a task (settled by the coordinator, 2026-10-04).</summary>
        public static readonly string LostTouch = HostText.YourStart + " lost touch with the agent app.";

        /// <summary>The state in a sentence, with why where it is known: "Can't tell what it's doing: your computer lost touch with the agent app."</summary>
        public static string CantTell(string? why) => why == null ? "Can't tell what it's doing right now." : Lead("Can't tell what it's doing", why);

        /// <summary>
        /// Why a task couldn't start or finish, with the way on, and the same without the state's own words
        /// for the peek. Never the reason's message, which is an agent app's own error, naming the app, a
        /// request or a path, or the control plane's: a start refused over its folder is said by its code
        /// (<see cref="EntryText.FolderProblem"/>); any other start, by what can be done next; a round that
        /// failed, by Tell it where it is offered now, else by adding the task again.
        /// </summary>
        public static (string Note, string Detail) CouldNotFinish(ExecutionView? execution, RuntimeDescriptor? runtime)
        {
            if (execution == null) return ("It couldn't finish.", "");
            if (execution.StartedAt == null && execution.TurnCount == 0)
            {
                var known = WorkspaceText.WhyFailed(execution.StatusReason?.Code, running: false);
                if (known != null) return (Lead("Couldn't start", known), known);
                return ("Couldn't start. " + AddItAgain, AddItAgain);
            }
            // Stopped because something else changed what it may do: telling it again can't go on with it.
            if (execution.StatusReason?.Code == "runtime_tampered") return (Lead("Couldn't finish", WorkspaceText.Tampered), WorkspaceText.Tampered);
            var next = runtime != null && WorkspacePresenter.ActionsFor(execution, runtime).Contains(WorkspaceAction.Instruct) ? TellItAgain : AddItAgain;
            return ("Couldn't finish this round. " + next, next);
        }

        /// <summary>The way on after a round that failed, where Tell it is offered (settled by the coordinator, 2026-10-04).</summary>
        public const string TellItAgain = "Tell it to try again, or what to do instead.";

        /// <summary>The way on after work that couldn't start or finish where nothing can be told to it now.</summary>
        public const string AddItAgain = "Add the task again in Projects to try again.";

        /// <summary>A state's words before a cause in a sentence of its own: "Couldn't start: this project has no folder…".</summary>
        internal static string Lead(string state, string why) => state + ": " + char.ToLowerInvariant(why[0]) + why.Substring(1);

        /// <summary>The state's word, the same on the badge, in the peek, the workspace and every list.</summary>
        public static string WordOf(WorkState state) => state switch
        {
            WorkState.NotStarted => "Not started",
            WorkState.Starting => "Starting",
            WorkState.Working => "Working",
            WorkState.CheckingItsWork => "Checking its work",
            WorkState.WaitingForYou => "Waiting for you",
            WorkState.FinishedThisRound => "Finished this round",
            WorkState.ChecksFailed => "Checks failed",
            WorkState.CouldNotFinish => "Couldn't finish",
            WorkState.Stopped => "Stopped",
            WorkState.CantTellYet => "Can't tell yet",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unhandled state."),
        };

        /// <summary>The badge a presentation shows.</summary>
        public static StateBadge BadgeOf(CharacterPresentation presentation)
        {
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));
            var state = StateOf(presentation.Activity, presentation.Attention);
            var waiting = state == WorkState.WaitingForYou ? Math.Max(presentation.PendingApprovals, presentation.AttentionNotes.Count) : 0;
            var (tone, icon, fill, edge, turns) = Look(state);
            return new StateBadge(
                state,
                WordOf(state),
                tone,
                presentation.Stale ? GlazeIcon.LastKnown : icon,
                fill,
                edge,
                turns && !presentation.Stale,
                state == WorkState.WaitingForYou && !presentation.Stale,
                waiting,
                presentation.Stale);
        }

        /// <summary>
        /// The marks beside the badge: Demo for the recorded demonstration (recorded and simulated),
        /// Practice for simulated work, Recorded for a recorded journal of real work.
        /// </summary>
        public static IReadOnlyList<WorkMark> MarksOf(CharacterPresentation presentation)
        {
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));
            if (presentation.Synthetic && presentation.Recorded) return new[] { new WorkMark(Demo, GlazeIcon.Recording) };
            if (presentation.Synthetic) return new[] { new WorkMark(Practice, GlazeIcon.Practice) };
            if (presentation.Recorded) return new[] { new WorkMark(Recorded, GlazeIcon.Recording) };
            return Array.Empty<WorkMark>();
        }

        /// <summary>
        /// Each state's look. Families share a tone (the moving states are ice, what went wrong is
        /// red) and each state has its own icon; states at rest are outlines, the unknown is dashed,
        /// what went wrong carries a red edge as well as its red.
        /// </summary>
        public static (GlazeTone Tone, GlazeIcon Icon, BadgeFill Fill, BadgeEdge Edge, bool Turns) Look(WorkState state) => state switch
        {
            WorkState.NotStarted => (GlazeTone.Neutral, GlazeIcon.NotStarted, BadgeFill.Outline, BadgeEdge.Solid, false),
            WorkState.Starting => (GlazeTone.Active, GlazeIcon.Starting, BadgeFill.Soft, BadgeEdge.None, true),
            WorkState.Working => (GlazeTone.Active, GlazeIcon.Working, BadgeFill.Soft, BadgeEdge.None, true),
            WorkState.CheckingItsWork => (GlazeTone.Active, GlazeIcon.CheckingItsWork, BadgeFill.Soft, BadgeEdge.None, false),
            WorkState.WaitingForYou => (GlazeTone.Attention, GlazeIcon.WaitingForYou, BadgeFill.Solid, BadgeEdge.None, false),
            WorkState.FinishedThisRound => (GlazeTone.Success, GlazeIcon.FinishedThisRound, BadgeFill.Soft, BadgeEdge.None, false),
            WorkState.ChecksFailed => (GlazeTone.Failure, GlazeIcon.ChecksFailed, BadgeFill.Soft, BadgeEdge.Solid, false),
            WorkState.CouldNotFinish => (GlazeTone.Failure, GlazeIcon.CouldNotFinish, BadgeFill.Soft, BadgeEdge.Solid, false),
            WorkState.Stopped => (GlazeTone.Neutral, GlazeIcon.Stopped, BadgeFill.Outline, BadgeEdge.Solid, false),
            WorkState.CantTellYet => (GlazeTone.Unknown, GlazeIcon.CantTellYet, BadgeFill.Soft, BadgeEdge.Dashed, false),
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unhandled state."),
        };
    }
}
