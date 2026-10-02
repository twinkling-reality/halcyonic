using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// Reads a menu frame (ADR 0026) back into the answers the recorded demonstration holds, so the judge
/// path's tests can say that a file offers exactly what the recording answers, wherever the playback
/// stands. Each action has one place: Approve and Send answer and Tell it the far right's main
/// prompt, Deny the Secondary, Stop the Rare beside Close; an option of the agent's question a choice
/// row, a recorded instruction a row on Tell it's page. Ids are WorkspaceScreens', which the file's
/// pages keep.
/// </summary>
internal static class JudgePath
{
    private static readonly (PromptSlot Slot, string Id, DemonstrationAnswerKind Kind)[] Prompts =
    {
        (PromptSlot.FarRight, WorkspaceScreens.Approve, DemonstrationAnswerKind.Approve),
        (PromptSlot.Secondary, WorkspaceScreens.Deny, DemonstrationAnswerKind.Deny),
        (PromptSlot.Rare, WorkspaceScreens.Stop, DemonstrationAnswerKind.Interrupt),
        (PromptSlot.FarRight, WorkspaceScreens.SendAnswer, DemonstrationAnswerKind.Answer),
        (PromptSlot.FarRight, WorkspaceScreens.TellIt, DemonstrationAnswerKind.Instruct),
    };

    /// <summary>
    /// The answers a frame lets a judge give now: each available prompt in its own place, and each
    /// available row that raises an instruction. A prompt in the wrong place fails the test, rather
    /// than count.
    /// </summary>
    public static ISet<DemonstrationAnswerKind> Offered(MenuFrame frame)
    {
        var offered = new HashSet<DemonstrationAnswerKind>();
        foreach (var (slot, prompt) in frame.Footer.All)
        {
            var known = Prompts.Where(each => each.Id == prompt.Id).ToList();
            if (known.Count == 0) continue;
            Assert.That(known.Select(each => each.Slot), Has.Member(slot), prompt.Id + " stands in its own place");
            if (prompt.Available) offered.Add(known[0].Kind);
        }
        if (frame.Lines.Any(line => line.Pressable && line.Action == WorkspaceScreens.Preset)) offered.Add(DemonstrationAnswerKind.Instruct);
        return offered;
    }

    /// <summary>The question's options a judge can choose: the keys of the available choice rows.</summary>
    public static IReadOnlyList<string> Options(MenuFrame frame) =>
        frame.Lines.Where(line => line.Choice && line.Pressable && line.Action == WorkspaceScreens.Choose).Select(line => line.Key!).ToList();

    /// <summary>The recorded instructions a judge can send: the words of the available instruction rows.</summary>
    public static IReadOnlyList<string> Instructions(MenuFrame frame) =>
        frame.Lines.Where(line => line.Pressable && line.Action == WorkspaceScreens.Preset).Select(line => line.Words).ToList();
}

public class JudgePathTests
{
    private static Prompt Close() => new(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);

    [Test]
    public void AnApprovalOffersApproveDenyAndStopInTheirPlaces()
    {
        var frame = new MenuFrame("Add rate limiting", new Footer(
            close: Close(),
            rare: new Prompt(WorkspaceScreens.Stop, "Stop", GlazeIcon.Stop),
            secondary: new Prompt(WorkspaceScreens.Deny, "Deny", GlazeIcon.Deny),
            farRight: new Prompt(WorkspaceScreens.Approve, "Approve", GlazeIcon.Approve, main: true)));
        Assert.That(JudgePath.Offered(frame), Is.EquivalentTo(new[]
        {
            DemonstrationAnswerKind.Approve, DemonstrationAnswerKind.Deny, DemonstrationAnswerKind.Interrupt,
        }));
    }

    [Test]
    public void AQuestionOffersItsOptionsAndSendAnswer()
    {
        var frame = new MenuFrame("Add rate limiting", new Footer(
                close: Close(),
                farRight: new Prompt(WorkspaceScreens.SendAnswer, "Send answer", GlazeIcon.SendAnswer, main: true)),
            lines: new[]
            {
                new PageLine("15 minutes", action: WorkspaceScreens.Choose, key: "0", choice: true, chosen: true),
                new PageLine("1 hour", action: WorkspaceScreens.Choose, key: "1", choice: true),
            });
        Assert.That(JudgePath.Offered(frame), Is.EquivalentTo(new[] { DemonstrationAnswerKind.Answer }));
        Assert.That(JudgePath.Options(frame), Is.EqualTo(new[] { "0", "1" }));
    }

    [Test]
    public void RecordedInstructionsAreRowsAndAnUnavailablePromptIsNotOffered()
    {
        var frame = new MenuFrame("Tell it", new Footer(
                close: Close(),
                farRight: new Prompt(WorkspaceScreens.TellIt, "Tell it", GlazeIcon.TellIt, main: true, available: false, reason: "Choose what to tell it.")),
            lines: new[]
            {
                new PageLine("Count per account too", action: WorkspaceScreens.Preset, key: "0"),
                new PageLine("Change the test instead", action: WorkspaceScreens.Preset, key: "1"),
            });
        Assert.That(JudgePath.Offered(frame), Is.EquivalentTo(new[] { DemonstrationAnswerKind.Instruct }));
        Assert.That(JudgePath.Instructions(frame), Is.EqualTo(new[] { "Count per account too", "Change the test instead" }));
    }

    [Test]
    public void APromptOutOfItsPlaceFails()
    {
        var frame = new MenuFrame("Add rate limiting", new Footer(
            close: Close(),
            secondary: new Prompt(WorkspaceScreens.Stop, "Stop", GlazeIcon.Stop)));
        Assert.Throws<AssertionException>(() => JudgePath.Offered(frame));
    }
}
