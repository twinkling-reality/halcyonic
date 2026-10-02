using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The competition's rules forbid brand names, logos and slogans in what judges see, and the judge
/// build is the recorded demonstration. This walks every point of every path of the bundled
/// recording and gathers every word the client core gives the headset there: each character's badge,
/// marks and peek, each workspace tab with its sections as recorded, the line above the stage, the
/// panels a judge can open (Connect projects, More tasks, Create, Usage left and Settings), and
/// checks them against names of products, companies and platforms.
/// </summary>
public class JudgeWordsTests
{
    /// <summary>Names a judge must not read: agent apps, models, companies, platforms and our own earlier products.</summary>
    private static readonly string[] Brands =
    {
        "Claude", "Anthropic", "OpenAI", "Codex", "OpenCode", "GPT", "Gemini", "Llama", "Qwen", "Ollama", "Mistral",
        "Meta", "Quest", "Oculus", "Horizon", "Apple", "Mac", "macOS", "iPhone", "Android", "Google", "YouTube",
        "GitHub", "Microsoft", "Windows", "Unity", "Salidium", "Seorak", "Stripe", "Postgres", "PostgreSQL",
        "Redis", "Docker", "AWS", "npm", "React",
    };

    [Test]
    public void NoBrandIsShownAlongAnyPathOfTheDemonstration()
    {
        var words = WordsAJudgeCanRead();
        var found = new SortedDictionary<string, SortedSet<string>>();
        foreach (var text in words)
        {
            foreach (var brand in Brands)
            {
                if (!Regex.IsMatch(text, @"\b" + Regex.Escape(brand) + @"\b")) continue;
                if (!found.TryGetValue(brand, out var where)) found[brand] = where = new SortedSet<string>();
                where.Add(text.Length > 120 ? text.Substring(0, 120) + "…" : text);
            }
        }
        var report = string.Join("\n", found.Select(entry => entry.Key + ": " + string.Join(" | ", entry.Value.Take(4))));
        TestContext.Out.WriteLine(report);
        Assert.That(found.Keys, Is.Empty, report);
        Assert.That(words.Count, Is.GreaterThan(100), "the walk reached the words");
    }

    /// <summary>Every distinct string the client core gives the headset along every path of the demonstration.</summary>
    private static HashSet<string> WordsAJudgeCanRead()
    {
        var recording = Demonstration.Recording();
        var words = new HashSet<string>();
        void Add(object? model)
        {
            if (model == null) return;
            if (model is string text)
            {
                if (text.Length > 0) words.Add(text);
                return;
            }
            Gather(JToken.FromObject(model, Serializer), words);
        }

        Add(DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null));
        Add(DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null, ended: true));
        Add(UsageLeftPresenter.NotInDemo);
        Add(EntryText.DemoCannotStart);
        foreach (var field in typeof(SettingsText).GetFields().Where(field => field.IsLiteral)) Add(field.GetRawConstantValue());
        foreach (StageArrangement arrangement in Enum.GetValues(typeof(StageArrangement)))
        {
            Add(SettingsText.Arrangement(arrangement));
            Add(SettingsText.ChangeTo(arrangement));
        }

        var factory = new CommandFactory(Samples.Client);
        Walk(recording, 0, new List<EventMessage>(), (index, before) =>
        {
            var node = recording.Nodes[index];
            var state = new ClientProjection();
            state.ApplyWelcome(recording.Welcome);
            state.ApplySnapshot(recording.Snapshot.Snapshot, new StateChanges());
            var log = new ActivityLog();
            void Apply(EventMessage message)
            {
                var changes = new StateChanges();
                state.ApplyEvent(message, changes);
                log.Record(changes.Events);
            }
            foreach (var message in before) Apply(message);
            for (var played = 1; played <= node.Events.Count; played++)
            {
                var message = node.Events[played - 1].Message;
                Apply(message);
                if (played < node.Events.Count && node.Events[played].At == node.Events[played - 1].At) continue;
                if (played == node.Events.Count && node.EndingSnapshot != null)
                {
                    state.ApplySnapshot(node.EndingSnapshot.Snapshot, new StateChanges());
                }
                var overview = WorkOverview.Of(state, new StageVisibility(), _ => true);
                Add(EntryScreens.ConnectProjects(overview, connected: false, demonstration: true));
                Add(EntryScreens.MoreTasks(overview, connected: false));
                foreach (var workstream in state.Workstreams.Values)
                {
                    var workspace = WorkspacePresenter.Present(workstream, state, log, live: true);
                    Add(StateLanguage.WordOf(StateLanguage.BadgeOf(workspace.Character).State));
                    Add(StateLanguage.MarksOf(workspace.Character));
                    Add(PeekCard.Of(workspace));
                    var executionId = workspace.Execution?.ExecutionId;
                    var presets = executionId == null
                        ? Array.Empty<PresetInstruction>()
                        : node.BranchesAfter(played)
                            .Where(branch => branch.Answer.Kind == DemonstrationAnswerKind.Instruct && branch.Answer.ExecutionId == executionId)
                            .Select(branch => new PresetInstruction(branch.Answer.Label!, branch.Answer.Text!))
                            .ToArray();
                    foreach (WorkspaceQuestion question in Enum.GetValues(typeof(WorkspaceQuestion)))
                    {
                        Add(WorkspaceScreens.Screen(workspace, new WorkspaceSteering(factory), new WorkspaceScreen { Question = question, Presets = presets }));
                    }
                    if (executionId == null) continue;
                    // Every answer whole, with room for every line, so no word a judge could page to escapes.
                    var understanding = recording.UnderstandingAt(executionId, index, played);
                    var understood = understanding == null
                        ? null
                        : new IntelligenceRead<UnderstandingResponse>(understanding.Response, understanding.ReadAt, recorded: true);
                    if (understood != null)
                    {
                        foreach (UnderstandPrompt prompt in Enum.GetValues(typeof(UnderstandPrompt)))
                        {
                            Add(UnderstandingPresenter.Present(prompt, executionId, understood, false, null, DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
                        }
                    }
                    var evaluation = recording.EvaluationAt(executionId, index, played);
                    var measured = evaluation == null
                        ? null
                        : new IntelligenceRead<EvaluationResponse>(evaluation.Response, evaluation.ReadAt, recorded: true);
                    if (understood != null || measured != null)
                    {
                        Add(CheckedPresenter.Present(executionId, understood, false, null, measured, false, null, DateTimeOffset.UtcNow, TimeZoneInfo.Utc));
                    }
                }
            }
        });
        return words;
    }

    private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
    {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Error = (_, args) => args.ErrorContext.Handled = true,
    });

    private static void Gather(JToken token, HashSet<string> words)
    {
        switch (token)
        {
            case JValue { Type: JTokenType.String } value:
                var text = (string?)value;
                if (!string.IsNullOrEmpty(text)) words.Add(text);
                break;
            case JContainer container:
                foreach (var child in container.Children()) Gather(child, words);
                break;
        }
    }

    /// <summary>Visits every node with the events played before it on its way from the beginning.</summary>
    private static void Walk(DemonstrationRecording recording, int index, List<EventMessage> before, Action<int, List<EventMessage>> visit)
    {
        visit(index, before);
        var node = recording.Nodes[index];
        foreach (var branch in node.Branches)
        {
            Walk(recording, branch.Node, before.Concat(node.Events.Take(branch.After).Select(e => e.Message)).ToList(), visit);
        }
    }
}
