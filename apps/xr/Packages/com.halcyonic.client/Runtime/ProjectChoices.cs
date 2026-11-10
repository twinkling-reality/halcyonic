#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client
{
    /// <summary>A project's kept choice for its tasks' next starts: the agent app, the model, and the level where the person chose one.</summary>
    public sealed class ProjectChoice
    {
        public ProjectChoice(string? runtimeId, string? modelRef, bool? neverAsk)
        {
            RuntimeId = runtimeId;
            ModelRef = modelRef;
            NeverAsk = neverAsk;
        }

        /// <summary>The agent app chosen, or null where the run's own stands.</summary>
        public string? RuntimeId { get; }

        /// <summary>The model chosen, as the runtime's list names it, or null.</summary>
        public string? ModelRef { get; }

        /// <summary>The level the person chose: true never ask, false Ask First, null where they chose none (the headset's default).</summary>
        public bool? NeverAsk { get; }

        public ProjectChoice WithLevel(bool neverAsk) => new ProjectChoice(RuntimeId, ModelRef, neverAsk);
    }

    /// <summary>
    /// Every project's kept choice, as the device keeps it across restarts of the headset app: a small JSON object by project
    /// id. What cannot be read is no choice: never a guess, and never a reason to fail.
    /// </summary>
    public sealed class ProjectChoices
    {
        private readonly Dictionary<string, ProjectChoice> choices = new Dictionary<string, ProjectChoice>(StringComparer.Ordinal);

        public ProjectChoice? Of(string projectId) => choices.TryGetValue(projectId, out var choice) ? choice : null;

        public void Keep(string projectId, ProjectChoice choice) => choices[projectId] = choice;

        public static ProjectChoices Read(string? text)
        {
            var read = new ProjectChoices();
            if (string.IsNullOrWhiteSpace(text)) return read;
            try
            {
                if (JToken.Parse(text!) is not JObject all) return read;
                foreach (var entry in all.Properties())
                {
                    if (entry.Value is not JObject kept) continue;
                    string? Text(string name) => kept[name]?.Type == JTokenType.String ? (string?)kept[name] : null;
                    var level = kept["never_ask"]?.Type == JTokenType.Boolean ? (bool?)kept["never_ask"] : null;
                    read.choices[entry.Name] = new ProjectChoice(Text("runtime_id"), Text("model_ref"), level);
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
            }
            return read;
        }

        public string Write()
        {
            var all = new JObject();
            foreach (var pair in choices)
            {
                var kept = new JObject();
                if (pair.Value.RuntimeId != null) kept["runtime_id"] = pair.Value.RuntimeId;
                if (pair.Value.ModelRef != null) kept["model_ref"] = pair.Value.ModelRef;
                if (pair.Value.NeverAsk is bool level) kept["never_ask"] = level;
                all[pair.Key] = kept;
            }
            return all.ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
