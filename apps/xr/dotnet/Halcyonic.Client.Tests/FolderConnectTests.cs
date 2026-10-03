using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class FolderConnectTests
{
    private static readonly CommandFactory Commands = new(new ClientInfo { Name = "halcyonic-xr", Version = "test", DeviceLabel = "Quest" });
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    private const string ProjectA = "0192a7a0-0000-7000-8000-00000000000a";

    private static LocationFolder Folder(string name, bool? repository = false, string? changed = "2026-09-30T12:00:00.000Z", params string[] usedBy) => new()
    {
        Name = name,
        Path = "/Users/person/Projects/" + name,
        Repository = repository,
        ChangedAt = repository == null ? null : changed,
        UsedBy = usedBy.ToList(),
    };

    private static LocationRoot Root(string name, params LocationFolder[] folders) => new()
    {
        Path = "/Users/person/" + name,
        Name = name,
        Status = LocationRootStatus.Available,
        Repository = false,
        ChangedAt = "2026-09-01T00:00:00.000Z",
        UsedBy = new List<string>(),
        Folders = folders.ToList(),
        FoldersTruncated = false,
    };

    private static LocationsResponse Listing(params LocationRoot[] roots) => new() { Roots = roots.ToList() };

    [Test]
    public void OffersOnlyFoldersNoProjectUsesTheNewestFirst()
    {
        var listing = Listing(Root("Projects",
            Folder("shop", true, "2026-09-29T12:00:00.000Z"),
            Folder("used", true, "2026-10-02T11:00:00.000Z", ProjectA),
            Folder("notes", false, "2026-10-01T12:00:00.000Z"),
            Folder("closed", null),
            Folder("Alpha", false, "2026-09-29T12:00:00.000Z")));
        var offers = FolderConnect.Offers(listing);
        Assert.That(offers.Select(offer => offer.RawName), Is.EqualTo(new[] { "notes", "Alpha", "shop", "closed" }),
            "a used folder is left out; equal times go by name; an unknown time comes last");
        Assert.That(offers.All(offer => offer.Folder != null), Is.True, "a root that is not a repository is not offered");
    }

    [Test]
    public void ARootIsOfferedOnlyWhenItIsARepositoryNoProjectUses()
    {
        var repository = Root("site");
        repository.Repository = true;
        var used = Root("used");
        used.Repository = true;
        used.UsedBy = new List<string> { ProjectA };
        var missing = Root("gone", Folder("lost"));
        missing.Status = LocationRootStatus.Missing;
        missing.Repository = null;
        var offers = FolderConnect.Offers(Listing(repository, used, missing));
        Assert.That(offers.Single().Folder, Is.Null);
        Assert.That(offers.Single().RawName, Is.EqualTo("site"));
        Assert.That(offers.Single().Choice.ToContract(), Is.InstanceOf<ExistingFolderChoice>());
        Assert.That(((ExistingFolderChoice)offers.Single().Choice.ToContract()).FolderName, Is.Null, "the root itself");
    }

    [Test]
    public void ConnectingSendsTheRootAndTheNameExactlyAsListed()
    {
        var odd = "  café\u202Eexe\tsite  ";
        var offer = FolderConnect.Offers(Listing(Root("Projects", Folder(odd)))).Single();
        var connection = new FolderConnection(offer, Commands);
        var command = (ProjectCreateCommand)connection.Begin();
        var choice = (ExistingFolderChoice)command.Payload.Location!;
        Assert.That(choice.Root, Is.EqualTo("/Users/person/Projects"));
        Assert.That(choice.FolderName, Is.EqualTo(odd), "never a path, and never the name as shown");
        Assert.That(command.Payload.Name, Is.EqualTo("café\u202Eexe\tsite"), "the folder's own name, without spaces at either end");
        Assert.That(offer.Name.Contains('\u202E', StringComparison.Ordinal), Is.False, "shown by LabelText's rule");
        Assert.That(offer.Name, Does.Contain("U+202E"));
        Assert.That(offer.Name.Contains('\t', StringComparison.Ordinal), Is.False);
    }

    [Test]
    public void AProjectNameFromAFolderFitsTheCommand()
    {
        Assert.That(FolderConnect.ProjectNameOf("   "), Is.EqualTo(FolderConnect.FallbackProjectName));
        Assert.That(FolderConnect.ProjectNameOf("\uFEFF"), Is.EqualTo(FolderConnect.FallbackProjectName), "nothing the control plane would take as text");
        Assert.That(FolderConnect.ProjectNameOf("\uFEFF shop \uFEFF"), Is.EqualTo("shop"));
        Assert.That(FolderConnect.ProjectNameOf(new string('a', 255)).Length, Is.EqualTo(FolderConnect.MaxProjectName));
        var emoji = new string('a', FolderConnect.MaxProjectName - 1) + "🙂🙂";
        var cut = FolderConnect.ProjectNameOf(emoji);
        Assert.That(cut, Is.EqualTo(new string('a', FolderConnect.MaxProjectName - 1)), "a character is never split in two");
        Assert.That(char.IsHighSurrogate(cut[^1]), Is.False);
    }

    [Test]
    public void ConnectedOnlyOnceTheRecordCompletedWithTheProject()
    {
        var connection = new FolderConnection(FolderConnect.Offers(Listing(Root("Projects", Folder("shop")))).Single(), Commands);
        var command = connection.Begin();
        connection.Advance(With());
        Assert.That(ConnectText.Outcome(connection), Is.EqualTo(ConnectText.Connecting));
        connection.Advance(With(new CommandView { CommandId = command.CommandId, Status = CommandStatus.Accepted }));
        Assert.That(connection.Connected, Is.False, "accepted is not done");
        Assert.That(connection.Unresolved, Is.EqualTo(command.CommandId));
        connection.Advance(With(Completed(command, new ProjectCreatedResult { ProjectId = ProjectA })));
        Assert.That(connection.Connected, Is.True);
        Assert.That(connection.ProjectId, Is.EqualTo(ProjectA));
        Assert.That(connection.Unresolved, Is.Null);
        Assert.That(ConnectText.Outcome(connection), Is.EqualTo("Connected: \u201Cshop\u201D is a project now. Add a task to start work in it."));
    }

    [Test]
    public void ARefusalSaysWhatToDoAndCanBeSentAgain()
    {
        var connection = new FolderConnection(FolderConnect.Offers(Listing(Root("Projects", Folder("shop")))).Single(), Commands);
        var command = connection.Begin();
        connection.Advance(With(new CommandView
        {
            CommandId = command.CommandId, Status = CommandStatus.Rejected,
            Rejection = new CommandRejection { Code = RejectionCode.LocationMissing, Message = "/Users/person/Projects/shop is not there." },
        }));
        Assert.That(connection.CanRetry, Is.True);
        Assert.That(ConnectText.Outcome(connection), Does.StartWith("Couldn't connect: "));
        Assert.That(ConnectText.Outcome(connection), Does.Not.Contain("/Users/person"), "the next step from the code, not the host's path");
        var again = connection.Retry();
        Assert.That(again.CommandId, Is.Not.EqualTo(command.CommandId));
    }

    [Test]
    public void AnOutcomeThatMayHaveRunIsNeverSentAgain()
    {
        var lost = new FolderConnection(FolderConnect.Offers(Listing(Root("Projects", Folder("shop")))).Single(), Commands);
        var command = lost.Begin();
        lost.AcknowledgementLost(new CommandOutcomeUnknownException(command.CommandId, "The socket closed."));
        lost.Advance(With());
        Assert.That(lost.CanRetry, Is.False);
        Assert.That(lost.Unresolved, Is.EqualTo(command.CommandId));
        Assert.That(ConnectText.Outcome(lost), Does.StartWith("Not sure whether"));
        Assert.Throws<InvalidOperationException>(() => lost.Retry());

        var failed = new FolderConnection(FolderConnect.Offers(Listing(Root("Projects", Folder("shop")))).Single(), Commands);
        var second = failed.Begin();
        failed.Advance(With(new CommandView
        {
            CommandId = second.CommandId, Status = CommandStatus.Failed,
            Failure = new CommandFailure { Code = "timeout", Message = "No answer.", Effect = FailureEffect.Unknown },
        }));
        Assert.That(failed.CanRetry, Is.False);
        Assert.That(ConnectText.Outcome(failed), Does.StartWith("Not sure whether"));

        var notSent = new FolderConnection(FolderConnect.Offers(Listing(Root("Projects", Folder("shop")))).Single(), Commands);
        notSent.Begin();
        notSent.AcknowledgementLost(new SessionUnavailableException("Not connected."));
        notSent.Advance(null);
        Assert.That(notSent.CanRetry, Is.True, "it never left the headset");
        Assert.That(ConnectText.Outcome(notSent), Is.EqualTo(ConnectText.NotConnectedYet));
    }

    [Test]
    public void TheFactsSayOnlyWhatTheComputerSawInPlainWords()
    {
        var zone = TimeZoneInfo.Utc;
        string Facts(LocationFolder folder) => ConnectText.Facts(FolderConnect.Offers(Listing(Root("Projects", folder))).Single(), Now, zone);
        Assert.That(Facts(Folder("a", true, "2026-09-29T11:00:00.000Z")), Is.EqualTo("Repository · changed 3 days ago"));
        Assert.That(Facts(Folder("b", false, "2026-10-02T11:59:30.000Z")), Is.EqualTo("Changed just now"));
        Assert.That(Facts(Folder("c", false, "2026-10-02T10:00:00.000Z")), Is.EqualTo("Changed 2 hours ago"));
        Assert.That(Facts(Folder("d", true, "2026-10-01T11:00:00.000Z")), Is.EqualTo("Repository · changed 1 day ago"));
        Assert.That(Facts(Folder("e", false, "2025-03-04T09:00:00.000Z")), Is.EqualTo("Changed on 4 Mar 2025"));
        Assert.That(Facts(Folder("f", null)), Is.EqualTo("Your computer can't look inside it"));
        Assert.That(Facts(Folder("g", false, "2026-11-01T00:00:00.000Z")), Is.EqualTo("Changed on 1 Nov 2026"), "a time ahead of this clock is said as a date");
    }

    [Test]
    public void TheWordsNameNoBrandAndUseNoEmDash()
    {
        var offer = FolderConnect.Offers(Listing(Root("Projects", Folder("shop", true)))).Single();
        var words = new[]
        {
            ConnectText.ConnectFolder, ConnectText.FoldersLine, ConnectText.NoFreeFolders, ConnectText.Connect, ConnectText.Connecting,
            ConnectText.NotConnectedYet, ConnectText.Gone, ConnectText.WhatConnectingDoes(offer), ConnectText.Facts(offer, Now, TimeZoneInfo.Utc),
        };
        foreach (var line in words)
        {
            Assert.That(line, Does.Not.Contain("Mac").And.Not.Contain("git").And.Not.Contain("Git").And.Not.Contain("\u2014"), line);
            Assert.That(line, Does.Not.Contain("path").And.Not.Contain("director"), "a person reads folder, never path or directory: " + line);
        }
    }

    [Test]
    public void TheFolderListSaysWhatIsHappeningAndOffersOnlyFreeFolders()
    {
        var zone = TimeZoneInfo.Utc;
        var reading = ConnectScreens.Folders(null, null, live: true, Now, zone);
        Assert.That(reading.Rows.Single().Title, Is.EqualTo(EntryText.ReadingFolders));
        Assert.That(reading.Actions.Primary, Is.Null, "nothing to press while it reads");
        var unread = ConnectScreens.Folders(null, "timed out", live: true, Now, zone);
        Assert.That(unread.Actions.Primary!.Id, Is.EqualTo(ConnectScreens.ReadAgain));
        Assert.That(ConnectScreens.Folders(Listing(), null, live: true, Now, zone).Rows.Single().Title, Is.EqualTo(EntryText.NoFolders));
        var allUsed = ConnectScreens.Folders(Listing(Root("Projects", Folder("shop", true, "2026-10-01T00:00:00.000Z", ProjectA))), null, live: true, Now, zone);
        Assert.That(allUsed.Rows.Single().Title, Is.EqualTo(ConnectText.NoFreeFolders));
        var away = ConnectScreens.Folders(Listing(Root("Projects", Folder("shop"))), null, live: false, Now, zone);
        Assert.That(away.Rows.Single().Title, Is.EqualTo(ConnectText.NotConnectedYet), "nothing is offered while the computer is away");

        var cut = Root("Projects", Folder("shop", true, "2026-09-29T11:00:00.000Z"), Folder("notes"));
        cut.FoldersTruncated = true;
        var list = ConnectScreens.Folders(Listing(cut), null, live: true, Now, zone);
        Assert.That(list.Lead, Does.EndWith(EntryText.FoldersCut));
        Assert.That(list.Rows.Select(row => (row.Title, row.Detail, row.TitleIsData, row.Action)), Is.EqualTo(new[]
        {
            ("notes", "Changed 2 days ago", true, ConnectScreens.ChooseFolder),
            ("shop", "Repository · changed 3 days ago", true, ConnectScreens.ChooseFolder),
        }));
        Assert.That(list.Actions.Back!.Id, Is.EqualTo(ConnectScreens.Back));

        var two = ConnectScreens.Folders(Listing(Root("Projects", Folder("shop")), Root("Work", Folder("site"))), null, live: true, Now, zone);
        Assert.That(two.Rows.Select(row => row.Detail), Is.EquivalentTo(new[] { "Changed 2 days ago · in Projects", "Changed 2 days ago · in Work" }),
            "with more than one place, each says where it is");
    }

    [Test]
    public void OneFolderOffersConnectThenSaysHowItWentWithTheNextStep()
    {
        var zone = TimeZoneInfo.Utc;
        var offer = FolderConnect.Offers(Listing(Root("Projects", Folder("shop", true, "2026-09-29T11:00:00.000Z")))).Single();
        var before = ConnectScreens.Folder(offer, null, live: true, Now, zone);
        Assert.That((before.Title, before.TitleIsData, before.Lead), Is.EqualTo(("shop", true, "Repository · changed 3 days ago")));
        Assert.That(before.Rows.Single().Title, Is.EqualTo("In \u201CProjects\u201D. Connecting makes a project called \u201Cshop\u201D that works in this folder. Nothing in it changes until you add a task."));
        Assert.That((before.Actions.Primary!.Id, before.Actions.Primary.Available), Is.EqualTo((ConnectScreens.Connect, true)));
        Assert.That(ConnectScreens.Folder(offer, null, live: false, Now, zone).Actions.Primary!.Available, Is.False, "nothing is sent while away");

        var connection = new FolderConnection(offer, Commands);
        var command = connection.Begin();
        connection.Advance(With());
        var waiting = ConnectScreens.Folder(offer, connection, live: true, Now, zone);
        Assert.That(waiting.Rows.Single().Title, Is.EqualTo(ConnectText.Connecting), "how it goes replaces what it would do");
        Assert.That(waiting.Actions.All.Select(action => action.Id), Is.EqualTo(new[] { ConnectScreens.Done }), "no second Connect while the first may run");

        connection.Advance(With(Completed(command, new ProjectCreatedResult { ProjectId = ProjectA })));
        var connected = ConnectScreens.Folder(offer, connection, live: true, Now, zone);
        Assert.That(connected.Actions.Primary!.Id, Is.EqualTo(ConnectScreens.AddTask));
        Assert.That(connected.Actions.Secondary.Single().Id, Is.EqualTo(ConnectScreens.Done));
        Assert.That(connected.Rows.Last().Tone, Is.EqualTo(GlazeTone.Success));

        var refused = new FolderConnection(offer, Commands);
        var second = refused.Begin();
        refused.Advance(With(new CommandView
        {
            CommandId = second.CommandId, Status = CommandStatus.Rejected,
            Rejection = new CommandRejection { Code = RejectionCode.LocationNotAllowed, Message = "Not one of the folders." },
        }));
        var refusedScreen = ConnectScreens.Folder(offer, refused, live: true, Now, zone);
        Assert.That(refusedScreen.Actions.Primary!.Id, Is.EqualTo(ConnectScreens.ChooseAnother));
        Assert.That(refusedScreen.Rows.Last().Tone, Is.EqualTo(GlazeTone.Failure));

        var notSent = new FolderConnection(offer, Commands);
        notSent.Begin();
        notSent.AcknowledgementLost(new SessionUnavailableException("Not connected."));
        notSent.Advance(null);
        Assert.That(ConnectScreens.Folder(offer, notSent, live: true, Now, zone).Actions.Primary!.Id, Is.EqualTo(ConnectScreens.TryAgain));
    }

    [Test]
    public void TheScreensUseNoMicrophoneNoEmDashAndNoBrand()
    {
        var zone = TimeZoneInfo.Utc;
        var offer = FolderConnect.Offers(Listing(Root("Projects", Folder("shop", true)))).Single();
        var screens = new[]
        {
            ConnectScreens.Folders(Listing(Root("Projects", Folder("shop", true))), null, true, Now, zone),
            ConnectScreens.Folder(offer, null, true, Now, zone),
        };
        foreach (var screen in screens)
        {
            Assert.That(screen.Actions.All.Any(action => action.Icon == GlazeIcon.HoldToTalk), Is.False);
            var words = new List<string> { screen.Lead ?? "" };
            words.AddRange(screen.Actions.All.Select(action => action.Label));
            words.AddRange(screen.Rows.Where(row => !row.TitleIsData).Select(row => row.Title));
            foreach (var word in words) Assert.That(word.Contains('\u2014', StringComparison.Ordinal) || word.Contains("Mac", StringComparison.Ordinal), Is.False, word);
        }
    }

    [Test]
    public void NamesThatLookTheSameAreToldApart()
    {
        var zone = TimeZoneInfo.Utc;
        var listing = Listing(Root("Projects",
            Folder("app", true, "2026-09-29T11:00:00.000Z"),
            Folder("app\u200B", false, "2026-10-02T11:00:00.000Z"),
            Folder(" APP", false, "2026-10-01T11:00:00.000Z"),
            Folder("\uFF41pp", false, "2026-10-01T10:00:00.000Z"),
            Folder("other", false)));
        var offers = FolderConnect.Offers(listing);
        Assert.That(offers.Where(offer => offer.LooksLikeAnother).Select(offer => offer.RawName),
            Is.EquivalentTo(new[] { "app", "app\u200B", " APP", "\uFF41pp" }));
        Assert.That(offers.Single(offer => offer.RawName == "other").LooksLikeAnother, Is.False);
        var list = ConnectScreens.Folders(listing, null, live: true, Now, zone);
        Assert.That(list.Rows.Where(row => row.Detail!.StartsWith(ConnectText.LooksLikeAnother, StringComparison.Ordinal)).Count(), Is.EqualTo(4));
        var one = ConnectScreens.Folder(offers.First(offer => offer.RawName == "app"), null, live: true, Now, zone);
        Assert.That(one.Rows.Select(row => row.Title), Does.Contain(ConnectText.CheckWhichOne));
    }

    [Test]
    public void ConnectWaitsAndSaysWhyWhileAnotherFolderMayStillBeConnecting()
    {
        var zone = TimeZoneInfo.Utc;
        var offers = FolderConnect.Offers(Listing(Root("Projects", Folder("shop"), Folder("notes"))));
        var shop = offers.Single(offer => offer.RawName == "shop");
        var notes = offers.Single(offer => offer.RawName == "notes");
        var screen = ConnectScreens.Folder(notes, null, live: true, Now, zone, waitingOn: shop);
        Assert.That(screen.Actions.Primary!.Available, Is.False);
        Assert.That(screen.Actions.Primary.Reason, Is.EqualTo(ConnectText.WaitingOn(shop)));
        Assert.That(screen.Actions.Primary.Reason, Does.Contain("\u201Cshop\u201D"));
    }

    private static CommandView Completed(CommandEnvelope command, CommandResult result) =>
        new() { CommandId = command.CommandId, Status = CommandStatus.Completed, Result = result };

    private static ClientProjection With(params CommandView[] commands)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Commands = commands.ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }
}
