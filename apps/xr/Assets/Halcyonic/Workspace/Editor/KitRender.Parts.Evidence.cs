#nullable enable
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The project screen's evidence pages on the kit's card (the lead's 2026-11-17 20:50Z): Outcome from Seorak's evaluation and
    /// Reasoning from Salidium's understanding, each from an answer the demonstration recorded in the exact field shape the
    /// plane's reads return, and each honest state: nothing read yet, not recorded, not readable (the reason in words), and a run
    /// whose source gave only part of its counts. Measured and read as every card state is (<see cref="CardShot(string, Transform, Vector3, string)"/>).
    /// </summary>
    public static partial class KitRender
    {
        /// <summary>The recorded answers the states take: the demonstration's third run, its answer with a failing test and one item left.</summary>
        private const string EvidenceRun = "01a0ec64-84c4-7213-b98a-e6deb68246e6";
        private const int EvidenceUnderstood = 15, EvidenceMeasured = 14;

        private static JToken EvidenceRecorded(string source, int at) =>
            JObject.Parse(Resources.Load<TextAsset>("HalcyonicDemonstration").text)[source]![EvidenceRun]![at]!["response"]!;

        private static T EvidenceWithout<T>(string availability, string code) where T : class => HalcyonicJson.Deserialize<T>(new JObject
        {
            ["execution_id"] = EvidenceRun,
            ["result"] = new JObject { ["availability"] = availability, ["reason"] = new JObject { ["code"] = code, ["message"] = "developer words" } },
        }.ToString());

        private static RoomCard? EvidenceCard(string state)
        {
            switch (state)
            {
                case "47-card-evidence-outcome": return EvidencePage.Of(HalcyonicJson.Deserialize<EvaluationResponse>(EvidenceRecorded("evaluation", EvidenceMeasured).ToString())).Card(0);
                case "47-card-evidence-outcome-before": return EvidencePage.Of((EvaluationResponse?)null).Card(0);
                case "47-card-evidence-outcome-not-recorded": return EvidencePage.Of(EvidenceWithout<EvaluationResponse>("not_found", "not_captured")).Card(0);
                case "47-card-evidence-outcome-unreadable": return EvidencePage.Of(EvidenceWithout<EvaluationResponse>("unavailable", "unreachable")).Card(0);
                case "47-card-evidence-reasoning": return EvidencePage.Of(HalcyonicJson.Deserialize<UnderstandingResponse>(EvidenceRecorded("understanding", EvidenceUnderstood).ToString())).Card(0);
                case "47-card-evidence-reasoning-partial":
                    // The source gave its passed count alone: the row says that count and never a guessed total.
                    var partial = HalcyonicJson.Deserialize<UnderstandingResponse>(EvidenceRecorded("understanding", EvidenceUnderstood).ToString());
                    var run = ((AvailableUnderstanding)partial.Result).Understanding.Verification.LatestByMethod.First();
                    if (run.Counts != null) run.Counts.Total = null;
                    return EvidencePage.Of(partial).Card(0);
                case "47-card-evidence-reasoning-not-allowed": return EvidencePage.Of(EvidenceWithout<UnderstandingResponse>("unauthorized", "credential_rejected")).Card(0);
                default: return null;
            }
        }
    }
}
