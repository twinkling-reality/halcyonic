using System;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class NewWorkSafetyTests
{
    private static CommandFactory Commands() => new(new ClientInfo
    {
        Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest",
    });

    [Test]
    public void ReviewShowsEveryCharacterBeforeTheLastPageCanConfirm()
    {
        var objective = new string('W', 4000);
        var review = new NewWorkReview(new string('P', 200), "OpenCode", "Local model",
            "on this Mac, tools declared", "ollama/local:latest", objective);
        Assert.That(review.PageCount, Is.GreaterThan(10));
        Assert.That(review.CanConfirm, Is.False);
        Assert.That(review.Pages.All(page => page.Split('\n').All(line => line.Length <= 24)), Is.True);
        var shown = string.Concat(review.Pages).Replace("\n", "");
        Assert.That(shown, Does.Contain(new string('P', 200)));
        Assert.That(shown, Does.Contain("OpenCode"));
        Assert.That(shown, Does.Contain("Local model"));
        Assert.That(shown, Does.Contain("ollama/local:latest"));
        Assert.That(shown, Does.Contain(objective));
        while (!review.CanConfirm) review.Next();
        Assert.That(review.Page, Is.EqualTo(review.PageCount - 1));
        review.Previous();
        Assert.That(review.CanConfirm, Is.False);
        review.Next();
        Assert.That(review.CanConfirm, Is.True);
    }

    [Test]
    public void ReviewMakesLineBreaksAndInvisibleTextVisible()
    {
        var review = new NewWorkReview("Project", "Runtime", "Model", "unknown", "ref",
            "First\nsecond\tthird\u202E");
        var shown = string.Concat(review.Pages);
        Assert.That(shown, Does.Contain("‹line break›"));
        Assert.That(shown, Does.Contain("‹tab›"));
        Assert.That(shown, Does.Contain("‹U+202E›"));
    }

    [Test]
    public void ProjectionCompletesACommandEvenWhenItsAcknowledgementIsLost()
    {
        var command = Commands().CreateWorkstream(Guid.NewGuid().ToString("D"), "Title", "Objective");
        var submission = new NewWorkSubmission(command);
        submission.LostAcknowledgement(new CommandOutcomeUnknownException(command.CommandId, "The socket closed."));
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.OutcomeUnknown));
        submission.Observe(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Accepted,
        });
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.OutcomeUnknown));
        submission.Observe(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Completed,
            Result = new WorkstreamCreatedResult { WorkstreamId = Guid.NewGuid().ToString("D") },
        });
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.Completed));
        Assert.That(submission.EffectiveRecord!.Result, Is.InstanceOf<WorkstreamCreatedResult>());
    }

    [Test]
    public void CompletionArrivingBeforeTheLostAcknowledgementStillWins()
    {
        var command = Commands().CreateProject("Project");
        var submission = new NewWorkSubmission(command);
        submission.Observe(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Completed,
            Result = new ProjectCreatedResult { ProjectId = Guid.NewGuid().ToString("D") },
        });
        submission.LostAcknowledgement(new CommandOutcomeUnknownException(command.CommandId, "The socket closed."));
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.Completed));
    }

    [Test]
    public void WrongCommandRecordCannotClearAnUnknownOutcome()
    {
        var command = Commands().CreateProject("Project");
        var submission = new NewWorkSubmission(command);
        submission.LostAcknowledgement(new CommandOutcomeUnknownException(command.CommandId, "The socket closed."));
        submission.Observe(new CommandView { CommandId = Guid.NewGuid().ToString("D"), Status = CommandStatus.Completed });
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.OutcomeUnknown));
    }

    [Test]
    public void ACommandKnownNotToHaveBeenSentCanBeRetried()
    {
        var command = Commands().CreateProject("Project");
        var submission = new NewWorkSubmission(command);
        submission.LostAcknowledgement(new SessionUnavailableException("Not connected."));
        Assert.That(submission.State, Is.EqualTo(NewWorkSubmissionState.NotSent));
    }
}
