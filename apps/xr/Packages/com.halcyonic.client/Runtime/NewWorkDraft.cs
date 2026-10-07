#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The choices a person makes before starting work from the headset. A runtime's models are kept
    /// with those served on this Mac first, in the runtime's order, then the rest (<see cref="Elsewhere"/>).
    /// For a runtime that lists its models, a model on this Mac is chosen for the person, and said so
    /// (<see cref="Preferred"/>); a model that
    /// runs elsewhere, or where it runs is not known, is never chosen for them and takes a second,
    /// deliberate press (<see cref="ChooseModel"/>), since the person's code and instructions go
    /// there. No start is built without a model for such a runtime.
    /// </summary>
    public sealed class NewWorkDraft
    {
        private readonly CommandFactory commands;
        private readonly List<RuntimeModel> models = new List<RuntimeModel>();

        public NewWorkDraft(CommandFactory commands) => this.commands = commands;

        public string? ProjectId { get; set; }

        public RuntimeDescriptor? Runtime { get; private set; }

        public string Objective { get; set; } = "";

        public IReadOnlyList<RuntimeModel> Models => models;

        public RuntimeModel? Model { get; private set; }

        public string? ModelProblem { get; private set; }

        /// <summary>The model was chosen for the person, because it runs on this Mac; they have not chosen it themselves.</summary>
        public bool ModelPreselected { get; private set; }

        /// <summary>A model that runs elsewhere, pressed once: pressing it again chooses it.</summary>
        public RuntimeModel? PendingModel { get; private set; }

        /// <summary>Whether a model runs on this Mac, the only kind chosen without a deliberate second press.</summary>
        public static bool RunsHere(RuntimeModel model) => model.Served == ModelServed.ThisMac;

        /// <summary>Where the models that do not run on this Mac begin in <see cref="Models"/>, or its count when there are none.</summary>
        public int Elsewhere
        {
            get
            {
                var index = models.FindIndex(model => !RunsHere(model));
                return index < 0 ? models.Count : index;
            }
        }

        /// <summary>
        /// The model on this Mac to choose for the person, or null when none is listed: one that declares
        /// tool calling before one that does not, then the one with the larger context, then the
        /// runtime's own order. The list says neither which model the runtime uses by default nor how
        /// large a model is, so neither can decide.
        /// </summary>
        public static RuntimeModel? Preferred(IReadOnlyList<RuntimeModel> listed)
        {
            RuntimeModel? best = null;
            foreach (var model in listed)
            {
                if (!RunsHere(model)) continue;
                if (best == null || Better(model, best)) best = model;
            }
            return best;
        }

        private static bool Better(RuntimeModel model, RuntimeModel than)
        {
            var tools = (model.ToolCalling == ModelToolCalling.Declared).CompareTo(than.ToolCalling == ModelToolCalling.Declared);
            if (tools != 0) return tools > 0;
            return (model.ContextTokens ?? 0) > (than.ContextTokens ?? 0);
        }

        public void ChooseRuntime(RuntimeDescriptor runtime)
        {
            Runtime = runtime;
            models.Clear();
            Model = null;
            ModelPreselected = false;
            PendingModel = null;
            ModelProblem = runtime.ModelChoice == ModelChoice.Listed ? "Reading this agent app's models…" : null;
        }

        public void SetModels(RuntimeModelsResponse response)
        {
            if (Runtime == null || Runtime.RuntimeId != response.RuntimeId) return;
            models.Clear();
            Model = null;
            ModelPreselected = false;
            PendingModel = null;
            if (response.Result is AvailableModels available)
            {
                // The Mac's models first: OpenCode lists hosted models before the local ones.
                foreach (var model in available.Models)
                {
                    if (RunsHere(model)) models.Add(model);
                }
                foreach (var model in available.Models)
                {
                    if (!RunsHere(model)) models.Add(model);
                }
                ModelProblem = models.Count == 0 ? EntryText.NoModels : null;
                Model = Preferred(models);
                ModelPreselected = Model != null;
            }
            else if (response.Result is UnavailableModels unavailable)
            {
                ModelProblem = EntryText.ModelsUnread(unavailable.Reason.Code);
            }
        }

        /// <summary>
        /// The models couldn't be asked for, <paramref name="connected"/> saying whether the computer was
        /// connected to ask. Takes no words: a failed request's are the control plane's own.
        /// </summary>
        public void ModelReadFailed(bool connected)
        {
            models.Clear();
            Model = null;
            ModelPreselected = false;
            PendingModel = null;
            ModelProblem = connected ? EntryText.ModelsUnreadable : EntryText.ModelsNotConnected;
        }

        /// <summary>Focus went to another window: a first press on a model that runs elsewhere no longer counts.</summary>
        public void FocusLeft() => PendingModel = null;

        /// <summary>
        /// Chooses a model from the runtime's current list. A model on this Mac is chosen at once; one
        /// that runs elsewhere, or where it runs is not known, is chosen only when pressed a second time
        /// in a row, and the first press leaves the current choice as it was. Returns whether it is chosen.
        /// </summary>
        public bool ChooseModel(RuntimeModel model)
        {
            if (!models.Contains(model)) throw new ArgumentException("Choose a model from the selected runtime's current list.", nameof(model));
            if (!RunsHere(model) && PendingModel != model)
            {
                PendingModel = model;
                return false;
            }
            PendingModel = null;
            Model = model;
            ModelPreselected = false;
            return true;
        }

        public string? Problem
        {
            get
            {
                if (ProjectId == null) return "Choose a project.";
                if (Runtime == null || !Runtime.Capabilities.StartExecution) return "Choose a runtime that can start work.";
                if (Runtime.ModelChoice == ModelChoice.Listed && Model == null) return ModelProblem ?? "Choose a model.";
                if (string.IsNullOrWhiteSpace(Objective)) return "Type an objective.";
                if (Objective.Length > 4000) return "The objective must be at most 4,000 characters.";
                return null;
            }
        }

        /// <summary>The longest workstream title, Halcyonic's ellipsis included.</summary>
        public const int MaxTitleLength = 200;

        /// <summary>
        /// The workstream's title: the objective as one plain line (<see cref="LabelText.Plain"/>), and
        /// where that would pass <see cref="MaxTitleLength"/>, cut between two of the person's
        /// characters, never inside one or inside a code point Plain spelled, with Halcyonic's own
        /// ellipsis.
        /// </summary>
        public string Title
        {
            get
            {
                var (typed, cut) = TitleSource;
                return LabelText.Plain(typed) + (cut ? "…" : "");
            }
        }

        /// <summary>
        /// What of the objective the title holds, as the person typed it, and whether Halcyonic cut it
        /// there, so a review can spell the typed text and show the ellipsis as Halcyonic's own.
        /// </summary>
        public (string Typed, bool Cut) TitleSource => TitleSourceOf(Objective);

        /// <summary>What of <paramref name="objective"/> a title holds, as typed, and whether Halcyonic cut it there.</summary>
        public static (string Typed, bool Cut) TitleSourceOf(string objective)
        {
            objective = (objective ?? "").Trim();
            if (LabelText.Plain(objective).Length <= MaxTitleLength) return (objective, false);
            // Where each character ends; Plain of a longer prefix never gets shorter.
            var ends = new List<int>();
            for (var index = 0; index < objective.Length; index++)
            {
                if (char.IsHighSurrogate(objective[index]) && index + 1 < objective.Length && char.IsLowSurrogate(objective[index + 1])) index++;
                ends.Add(index + 1);
            }
            int low = 0, high = ends.Count - 1, fits = 0;
            while (low <= high)
            {
                var middle = (low + high) / 2;
                if (LabelText.Plain(objective.Substring(0, ends[middle])).Length <= MaxTitleLength - 1)
                {
                    fits = ends[middle];
                    low = middle + 1;
                }
                else high = middle - 1;
            }
            return (objective.Substring(0, fits).TrimEnd(), true);
        }

    }
}
