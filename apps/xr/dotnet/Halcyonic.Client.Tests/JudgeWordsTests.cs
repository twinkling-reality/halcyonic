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

        // The menu's places (ADR 0026), on its bar whichever is open.
        foreach (var place in MenuBar.Places) Add(MenuBar.Word(place));
        Add(DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null));
        Add(DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null, ended: true));
        Add(UsageLeftPresenter.NotInDemo);
        // Usage while the demonstration plays: its recorded limits, and while they are read again.
        var limits = UsageLeftPresenter.Present(recording.UsageLimitsAt(DateTimeOffset.UtcNow)!, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, recorded: true);
        Add(UsageLeftScreens.Screen(limits, reading: false, canRead: true));
        Add(UsageLeftScreens.Screen(limits, reading: true, canRead: true));
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

    /// <summary>
    /// Every word a menu frame (ADR 0026) shows: its subject and pill, each section's words, each
    /// line's words and fact and chip, its source and why a prompt can't be taken now, its side panel's
    /// subject, facts and lines, and every prompt's words and reason. Named field by field, so a word a
    /// new field carries is added here on purpose.
    /// </summary>
    internal static IEnumerable<string> WordsOf(MenuFrame frame)
    {
        yield return frame.Subject;
        if (frame.Pill != null) yield return StateLanguage.WordOf(frame.Pill.State);
        foreach (var section in frame.Sections) yield return section.Words;
        foreach (var line in frame.Lines)
        {
            foreach (var word in WordsOf(line)) yield return word;
        }
        if (frame.Source != null) yield return frame.Source;
        if (frame.Reason != null) yield return frame.Reason;
        if (frame.Side is SidePanel side)
        {
            yield return side.Subject;
            foreach (var fact in side.Facts)
            {
                yield return fact.Name;
                yield return fact.Value;
            }
            foreach (var line in side.Lines)
            {
                foreach (var word in WordsOf(line)) yield return word;
            }
            if (side.Source != null) yield return side.Source;
        }
        foreach (var (_, prompt) in frame.Footer.All)
        {
            yield return prompt.Words;
            if (prompt.Reason != null) yield return prompt.Reason;
        }
    }

    private static IEnumerable<string> WordsOf(PageLine line)
    {
        yield return line.Words;
        if (line.Fact != null) yield return line.Fact;
        if (line.Chip != null) yield return line.Chip;
    }

    [Test]
    public void TheRecordedUsageLimitsNameNoBrand()
    {
        var recording = Demonstration.Recording();
        var shown = UsageLeftPresenter.Present(recording.UsageLimitsAt(DateTimeOffset.UtcNow)!, DateTimeOffset.UtcNow, TimeZoneInfo.Utc, recorded: true);
        var words = recording.UsageLimits!.Readings.Select(reading => reading.Label)
            .Concat(shown.Rows.SelectMany(row => new[] { row.Title, row.Text }))
            .Append(shown.Source!).Append(shown.Note).ToList();
        Assert.That(words, Has.Member("Recorded for the demo, not from any account"));
        foreach (var word in words)
        {
            foreach (var brand in Brands)
            {
                Assert.That(Regex.IsMatch(word, @"\b" + Regex.Escape(brand) + @"\b"), Is.False, "\"" + word + "\" names " + brand);
            }
        }
    }

    [Test]
    public void EveryWordOfAMenuFrameIsGathered()
    {
        // A side panel shows facts or lines, never both, so the frame is gathered once with each.
        var withFacts = new SidePanel("Changed files", facts: new[] { new SideFact("Lines", "71 added") }, source: "Simulated explanation");
        var withLines = new SidePanel("Checks", lines: new[] { new PageLine("src/a.ts", wordsAreData: true, fact: "New") });
        MenuFrame Frame(SidePanel side) => new MenuFrame("Add rate limiting",
            new Footer(
                close: new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close),
                farRight: new Prompt(WorkspaceScreens.TellIt, "Tell it", GlazeIcon.TellIt, main: true, available: false, reason: "Nothing to tell it now.")),
            subjectIsData: true,
            sections: new[] { new FrameSection("changes", "Changes", chosen: true) },
            lines: new[]
            {
                new PageLine("2 files changed", fact: "now", chip: "Inferred", action: "open", key: "what-changed", opens: true, chosen: true),
            },
            source: "Simulated checks",
            side: side);
        Assert.That(WordsOf(Frame(withFacts)), Is.SupersetOf(new[]
        {
            "Add rate limiting", "Changes", "2 files changed", "now", "Inferred", "Simulated checks", "Nothing to tell it now.",
            "Changed files", "Lines", "71 added", "Simulated explanation", "Close", "Tell it",
        }));
        Assert.That(WordsOf(Frame(withLines)), Is.SupersetOf(new[] { "Checks", "src/a.ts", "New" }));
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
