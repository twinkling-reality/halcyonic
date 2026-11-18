#nullable enable
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The evidence view as a story anyone can follow (the owner's 2026-11-18 direction; <see cref="EvidenceFlowView.Tell"/>), from the
    /// demonstration's recorded run "Paginate the order history endpoint" in the reads' own shape: the whole story, a moment of its reveal,
    /// the same run read early (before its check ran), and before any read. Measured: every place stands and faces the person, keeps
    /// the kit's reading size at its distance, and carries no path, command, source class or sibling product on its face.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] FlowStates = { "63-evidence-story", "63-evidence-story-reveal", "63-evidence-story-early", "63-evidence-story-before" };

        private const string FlowRun = "01a0ec64-6fac-77a9-827e-bdcf356af403";

        private static JObject FlowRecording() => JObject.Parse(Resources.Load<TextAsset>("HalcyonicDemonstration").text);

        /// <summary>The run's own instruction as the recording holds it: the person's words, never typed here.</summary>
        private static string? FlowAsked(JObject recording) =>
            recording.SelectTokens("$..executions[*]").OfType<JObject>().FirstOrDefault(execution => (string?)execution["execution_id"] == FlowRun)?["instruction"]?.ToString();

        private static PartShot FlowShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = -6f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var recording = FlowRecording();
            var asked = FlowAsked(recording);
            Require(asked != null, "the recording holds no instruction for the run");
            T Read<T>(string source, int at) where T : class => HalcyonicJson.Deserialize<T>(recording[source]![FlowRun]![at]!["response"]!.ToString());
            var at = state == "63-evidence-story-early" ? 3 : 7;
            var before = state == "63-evidence-story-before";
            // The run's own closing words where the recording holds them (CORE's de2048182 records them), the agent's plain sentences.
            var said = recording.SelectTokens("$..executions[*]").OfType<JObject>().LastOrDefault(execution => (string?)execution["execution_id"] == FlowRun && execution["closing_words"] is JObject)?["closing_words"];
            var closing = said == null || before ? null : new EvidenceStory.ClosingWords((string?)said["did"], (string?)said["why"], (string?)said["how"], (string?)said["left"]);
            var story = EvidenceStory.Of(asked, before ? null : Read<UnderstandingResponse>("understanding", at), closing);
            var strip = EvidenceStrip.Of(before ? null : (Read<EvaluationResponse>("evaluation", at).Result as AvailableEvaluation)?.Evaluation);

            var holder = new GameObject("Flow").transform;
            holder.SetParent(root, false);
            holder.localPosition = eyes;
            var view = EvidenceFlowView.Create(holder);
            var clock = 100f;
            view.Clock = () => clock;
            view.Tell(story, strip?.Shown(null) ?? new EvidenceStrip.Stat[0], strip?.Notes ?? new string[0], strip, null, before ? null : EvidenceStory.Words.Done);
            // The arrival a moment in (the status and the ask stood, the account not yet), else the story stood whole.
            clock += state == "63-evidence-story-reveal" ? 0.6f : 10f;
            view.AdvanceStory();

            var standing = view.StoryPlaces.Where(pair => pair.Value.Glass.transform.parent.gameObject.activeSelf).ToList();
            if (state == "63-evidence-story-reveal") Require(standing.Count > 0 && standing.Count < story.Places.Count, "the reveal does not stand the places one by one: " + standing.Count);
            else Require(standing.Count == story.Places.Count, "not every place stands: " + standing.Count + " of " + story.Places.Count);
            foreach (var (kind, (glass, card)) in standing)
            {
                var placeAt = glass.transform.parent.position - holder.position;
                Require(Vector3.Dot(glass.transform.parent.forward.normalized, new Vector3(placeAt.x, 0f, placeAt.z).normalized) > 0.9f, kind + " does not face the person");
                var words = card.GetComponentsInChildren<TMPro.TextMeshPro>(false).Select(text => text.text).ToList();
                Require(!words.Any(text => text.Contains("Salidium") || text.Contains("Seorak")), kind + " names a sibling product");
                Require(!words.Any(text => text.Contains("make test") || text.Contains("src/") || text.Contains("Observed") || text.Contains("Inferred")), kind + " shows something technical on its face");
            }
            if (before) Require(story.Without?.State == "No Conclusions", "before any read the story does not say so");
            else Require(view.Strip != null, "the figures row does not stand");
            return shot;
        }
    }
}
