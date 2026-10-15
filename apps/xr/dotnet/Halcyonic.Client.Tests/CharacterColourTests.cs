using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class CharacterColourTests
{
    private static CharacterColourScope Scope(string task = "task", string journal = "journal", string connection = "connection") => new(connection, journal, task);
    private static void Press(CharacterColourColumn column, string action, string? key = null)
    { var frame = column.Frame!; column.Drawn(frame, null); column.Act(action, key); }
    private static string Save(CharacterColourSettings settings, CharacterColourScope scope, string colour)
    { var owner = new object(); settings.Preview(owner, scope, colour); string saved = ""; Assert.That(settings.Apply(owner, scope, colour, text => { saved = text; return true; }), Is.True); return saved; }

    [Test]
    public void PreviewIsVisibleButNeverPersistedUntilAnExplicitDrawnApply()
    {
        var settings = new CharacterColourSettings(); var scope = Scope(); var writes = 0;
        var column = new CharacterColourColumn(settings, scope, () => true, _ => { writes++; return true; });
        Press(column, CharacterColourColumn.Choose, "cyan");
        Assert.That(settings.DisplayedHue(scope), Is.Null);
        Press(column, CharacterColourColumn.Preview);
        Assert.That(settings.DisplayedHue(scope), Is.EqualTo(197));
        Assert.That(settings.Applied(scope), Is.EqualTo("original"));
        column.Act(CharacterColourColumn.Apply, null); // New preview frame has not been drawn.
        Assert.That(writes, Is.Zero);
        Press(column, CharacterColourColumn.Apply);
        Assert.That((settings.Applied(scope), writes), Is.EqualTo(("cyan", 1)));
        column.Act(CharacterColourColumn.Apply, null);
        Assert.That(writes, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CancelOrFocusLeaveRestoresSavedOverrideInsteadOfFactoryColour(bool focus)
    {
        var settings = new CharacterColourSettings(); var scope = Scope(); Save(settings, scope, "violet"); var writes = 0;
        var column = new CharacterColourColumn(settings, scope, () => true, _ => { writes++; return true; });
        Press(column, CharacterColourColumn.Choose, "cyan"); Press(column, CharacterColourColumn.Preview);
        if (focus) column.FocusLeft(); else Press(column, CharacterColourColumn.Cancel);
        Assert.That(settings.DisplayedHue(scope), Is.EqualTo(285)); Assert.That(writes, Is.Zero);
    }

    [Test]
    public void TargetLossRollsBackAndClosesWithoutSaving()
    {
        var settings = new CharacterColourSettings(); var scope = Scope(); var current = true; var closed = 0;
        var column = new CharacterColourColumn(settings, scope, () => current, _ => throw new Exception("Must not save"));
        column.Closed += () => closed++;
        Press(column, CharacterColourColumn.Choose, "cyan"); Press(column, CharacterColourColumn.Preview);
        var old = column.Frame!; current = false; column.Tick(); column.Act(CharacterColourColumn.Apply, null);
        Assert.That(settings.DisplayedHue(scope), Is.Null); Assert.That(closed, Is.EqualTo(1));
        current = true; column.Tick(); column.Drawn(old, null);
        column.Act(CharacterColourColumn.Preview, null); column.Act(CharacterColourColumn.Apply, null);
        Assert.That(column.Frame, Is.Null, "a lost target cannot resurrect its old colour editor");
        Assert.That(closed, Is.EqualTo(1));
    }

    [Test]
    public void HiddenChoiceAndStaleFrameCannotChooseOrApply()
    {
        var settings = new CharacterColourSettings(); var scope = Scope(); var writes = 0;
        var column = new CharacterColourColumn(settings, scope, () => true, _ => { writes++; return true; });
        Press(column, CharacterColourColumn.Choose, "pink"); // On the second page only.
        Press(column, CharacterColourColumn.Preview); Assert.That(settings.DisplayedHue(scope), Is.Null);
        Press(column, CharacterColourColumn.Cancel); Press(column, Footer.NextPage); Press(column, CharacterColourColumn.Choose, "pink");
        Press(column, CharacterColourColumn.Preview); Assert.That(settings.DisplayedHue(scope), Is.EqualTo(329));
        var stale = column.Frame!; column.FocusLeft(); column.Drawn(stale, null); column.Act(CharacterColourColumn.Apply, null);
        Assert.That(writes, Is.Zero); Assert.That(settings.DisplayedHue(scope), Is.Null);
    }

    [Test]
    public void FailedPersistenceRetainsPreviewAndAppliedValueUntilCancel()
    {
        var settings = new CharacterColourSettings(); var scope = Scope(); Save(settings, scope, "violet");
        var column = new CharacterColourColumn(settings, scope, () => true, _ => false);
        Press(column, CharacterColourColumn.Choose, "cyan"); Press(column, CharacterColourColumn.Preview); Press(column, CharacterColourColumn.Apply);
        Assert.That(settings.Applied(scope), Is.EqualTo("violet")); Assert.That(settings.DisplayedHue(scope), Is.EqualTo(197));
        Assert.That(column.Frame!.Lines.Any(line => line.Words.Contains("could not be saved")), Is.True);
        Press(column, CharacterColourColumn.Cancel); Assert.That(settings.DisplayedHue(scope), Is.EqualTo(285));
    }

    [TestCase("connection")]
    [TestCase("journal")]
    [TestCase("task")]
    [TestCase("session")]
    [TestCase("snapshot")]
    [TestCase("focus")]
    [TestCase("minimized")]
    public void ExactPreviewContextLossRestoresAppliedColourWithoutSaving(string lost)
    {
        var settings = new CharacterColourSettings(); var scope = Scope(); Save(settings, scope, "violet");
        var session = new object(); var shownSession = session; var currentScope = scope;
        var fresh = true; var focused = true; var minimized = false; var writes = 0;
        var column = new CharacterColourColumn(settings, scope,
            () => ReferenceEquals(session, shownSession) && scope.Equals(currentScope) && fresh && focused && !minimized,
            _ => { writes++; return true; });
        Press(column, CharacterColourColumn.Choose, "cyan"); Press(column, CharacterColourColumn.Preview);
        Assert.That(settings.DisplayedHue(scope), Is.EqualTo(197));
        switch (lost)
        {
            case "connection": currentScope = Scope(connection: "other"); break;
            case "journal": currentScope = Scope(journal: "other"); break;
            case "task": currentScope = Scope("other"); break;
            case "session": shownSession = new object(); break;
            case "snapshot": fresh = false; break;
            case "focus": focused = false; break;
            case "minimized": minimized = true; break;
        }
        column.Tick();
        Assert.That(settings.DisplayedHue(scope), Is.EqualTo(285));
        Assert.That(settings.DisplayedHue(Scope(connection: "other")), Is.Null);
        Assert.That(column.Frame, Is.Null); Assert.That(writes, Is.Zero);
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void FileColourRouteRequiresAnAvailableCurrentDrawAndCannotRetargetAnotherJournal(TextSize size)
    {
        const string task = "550e8400-e29b-41d4-a716-446655440001";
        var state = new ClientProjection(); state.ApplySnapshot(Samples.Snapshot(1, new[] { Samples.Workstream(task) }), new StateChanges());
        var host = new FileMenuHost { Projection = state, TextSize = size };
        var available = true; var opens = 0;
        var file = new FileColumn(host, () => WorkspacePresenter.Present(state.Workstreams[task], state, new ActivityLog(), host.Live),
            new CommandFactory(Samples.Client), () => null, _ => null, () => "");
        Assert.That(file.Frame!.Lines.Any(line => line.Action == FileScreens.OpenCharacterColour), Is.False);
        file.OfferCharacterColours(() => available, () => opens++);
        file.Act(FileScreens.OpenCharacterColour, null); Assert.That(opens, Is.Zero);
        file.Drawn(file.Frame!, null); available = false;
        file.Act(FileScreens.OpenCharacterColour, null); available = true;
        file.Act(FileScreens.OpenCharacterColour, null); Assert.That(opens, Is.Zero, "restored support cannot revive the consumed row");
        file.Drawn(file.Frame!, null); file.Act(FileScreens.OpenCharacterColour, null);
        Assert.That(opens, Is.EqualTo(1));
        file.Act(FileScreens.OpenCharacterColour, null); Assert.That(opens, Is.EqualTo(1));
        file.Drawn(file.Frame!, null); file.FocusLeft(); file.Act(FileScreens.OpenCharacterColour, null);
        Assert.That(opens, Is.EqualTo(1));
        file.Drawn(file.Frame!, null);
        state.ApplySnapshot(Samples.Snapshot(1, new[] { Samples.Workstream(task) }, journal: Samples.Journal("550e8400-e29b-41d4-a716-446655440099")), new StateChanges());
        file.Act(FileScreens.OpenCharacterColour, null); Assert.That(opens, Is.EqualTo(1));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void OldPreviewOwnerCannotCancelOrApplyAReplacement()
    {
        var settings = new CharacterColourSettings(); var first = new object(); var next = new object(); var scope = Scope();
        settings.Preview(first, scope, "cyan"); settings.Preview(next, scope, "pink"); settings.Cancel(first);
        Assert.That(settings.DisplayedHue(scope), Is.EqualTo(329));
        Assert.That(settings.Apply(first, scope, "cyan", _ => true), Is.False);
        Assert.That(settings.Apply(next, Scope("other"), "pink", _ => true), Is.False);
    }

    [Test]
    public void ScopedStoreRoundTripsAndOriginalRemovesOnlyItsOwnOverride()
    {
        var settings = new CharacterColourSettings(); var scope = Scope(); Save(settings, scope, "cyan");
        Save(settings, Scope(journal: "other"), "pink"); Save(settings, Scope(connection: "other"), "violet");
        var loaded = CharacterColourSettings.Load(settings.Save());
        Assert.That(loaded.Applied(scope), Is.EqualTo("cyan"));
        Save(loaded, scope, "original");
        Assert.That(loaded.Applied(scope), Is.EqualTo("original"));
        Assert.That(loaded.Applied(Scope(journal: "other")), Is.EqualTo("pink"));
        Assert.That(loaded.Applied(Scope(connection: "other")), Is.EqualTo("violet"));
        Assert.That(loaded.Save(), Does.Not.Contain("\"colour\":\"original\""));
    }

    [Test]
    public void PruneOnlyRemovesCurrentConnectionJournalMissingTasksAndIsIdempotent()
    {
        var settings = new CharacterColourSettings(); Save(settings, Scope(), "cyan"); Save(settings, Scope("kept"), "pink");
        Save(settings, Scope(journal: "foreign"), "violet"); var writes = 0;
        Assert.That(settings.Prune("connection", "journal", new[] { "kept" }, _ => { writes++; return true; }), Is.True);
        Assert.That(settings.Applied(Scope()), Is.EqualTo("original")); Assert.That(settings.Applied(Scope("kept")), Is.EqualTo("pink"));
        Assert.That(settings.Applied(Scope(journal: "foreign")), Is.EqualTo("violet"));
        Assert.That(settings.Prune("connection", "journal", new[] { "kept" }, _ => { writes++; return true; }), Is.False);
        Assert.That(writes, Is.EqualTo(1));
    }

    [TestCase("{\"version\":2,\"colours\":[]}")]
    [TestCase("{\"version\":1,\"version\":1,\"colours\":[]}")]
    [TestCase("{\"version\":1,\"colours\":[{\"connection\":\"a\",\"journal\":\"b\",\"task\":\"c\",\"colour\":\"red\"}]}")]
    public void DamagedOrUnsupportedSettingsReturnOriginal(string encoded) => Assert.That(CharacterColourSettings.Load(encoded).Save(), Does.Contain("\"colours\":[]"));

    [Test]
    public void BoundedStoreNeverWritesBeyondCapacityAndChoicesRemainInIdentityPalette()
    {
        var settings = new CharacterColourSettings();
        for (var i = 0; i < CharacterColourSettings.Maximum; i++) Save(settings, Scope(i.ToString()), "cyan");
        var owner = new object(); var scope = Scope("overflow"); settings.Preview(owner, scope, "pink");
        Assert.That(settings.Apply(owner, scope, "pink", _ => throw new Exception("Over-limit write")), Is.False);
        foreach (var choice in CharacterColourSettings.Choices.Where(choice => choice != "original"))
            Assert.That(CharacterIdentity.Hues, Does.Contain(CharacterColourSettings.Hue(choice)!.Value));
    }
}
