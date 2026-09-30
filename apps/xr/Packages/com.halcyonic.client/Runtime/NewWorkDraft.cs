#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>The choices a person makes before starting work from the headset.</summary>
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

        public void ChooseRuntime(RuntimeDescriptor runtime)
        {
            Runtime = runtime;
            models.Clear();
            Model = null;
            ModelProblem = runtime.ModelChoice == ModelChoice.Listed ? "Reading this runtime's models." : null;
        }

        public void SetModels(RuntimeModelsResponse response)
        {
            if (Runtime == null || Runtime.RuntimeId != response.RuntimeId) return;
            models.Clear();
            Model = null;
            if (response.Result is AvailableModels available)
            {
                models.AddRange(available.Models);
                ModelProblem = models.Count == 0 ? "This runtime lists no models." : null;
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
            ModelProblem = "Could not read models: " + reason;
        }

        public void ChooseModel(RuntimeModel model)
        {
            if (!models.Contains(model)) throw new ArgumentException("Choose a model from the selected runtime's current list.", nameof(model));
            Model = model;
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

        public WorkstreamCreateCommand CreateWorkstream()
        {
            if (Problem is string problem) throw new InvalidOperationException(problem);
            var objective = Objective.Trim();
            var title = LabelText.Plain(objective);
            if (title.Length > 200)
            {
                var end = char.IsHighSurrogate(title[198]) ? 198 : 199;
                title = title.Substring(0, end).TrimEnd() + "…";
            }
            return commands.CreateWorkstream(ProjectId!, title, objective);
        }

        public ExecutionStartCommand StartExecution(string workstreamId)
        {
            if (Problem is string problem) throw new InvalidOperationException(problem);
            return commands.StartExecution(workstreamId, Runtime!.RuntimeId, Objective.Trim(), modelRef: Model?.ModelRef);
        }
    }
}
