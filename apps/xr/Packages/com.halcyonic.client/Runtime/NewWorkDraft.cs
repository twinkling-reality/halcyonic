#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The choices a person makes before starting work from the headset. For a runtime that lists its
    /// models, the first model served on this Mac is chosen for the person, and said so; a model that
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

        public void ChooseRuntime(RuntimeDescriptor runtime)
        {
            Runtime = runtime;
            models.Clear();
            Model = null;
            ModelPreselected = false;
            PendingModel = null;
            ModelProblem = runtime.ModelChoice == ModelChoice.Listed ? "Reading this runtime's models." : null;
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
                models.AddRange(available.Models);
                ModelProblem = models.Count == 0 ? "This runtime lists no models." : null;
                foreach (var model in models)
                {
                    if (!RunsHere(model)) continue;
                    Model = model;
                    ModelPreselected = true;
                    break;
                }
            }
            else if (response.Result is UnavailableModels unavailable)
            {
                ModelProblem = "Models unavailable: " + unavailable.Reason.Message;
            }
        }

        public void ModelReadFailed(string reason)
        {
            models.Clear();
            Model = null;
            ModelPreselected = false;
            PendingModel = null;
            ModelProblem = "Could not read models: " + reason;
        }

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

        public string Title
        {
            get
            {
                var title = LabelText.Plain(Objective.Trim());
                if (title.Length > 200)
                {
                    var end = char.IsHighSurrogate(title[198]) ? 198 : 199;
                    title = title.Substring(0, end).TrimEnd() + "…";
                }
                return title;
            }
        }

        public WorkstreamCreateCommand CreateWorkstream()
        {
            if (Problem is string problem) throw new InvalidOperationException(problem);
            return commands.CreateWorkstream(ProjectId!, Title, Objective.Trim());
        }

        public ExecutionStartCommand StartExecution(string workstreamId)
        {
            if (Problem is string problem) throw new InvalidOperationException(problem);
            // Problem already requires one; this keeps a start without a model for a listing runtime impossible.
            if (Runtime!.ModelChoice == ModelChoice.Listed && Model == null) throw new InvalidOperationException("Choose a model.");
            return commands.StartExecution(workstreamId, Runtime.RuntimeId, Objective.Trim(), modelRef: Model?.ModelRef);
        }
    }
}
