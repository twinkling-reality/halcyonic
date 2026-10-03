using System;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A page's measures and which columns stand on the menu's plane (ADR 0026).</summary>
[TestFixture]
public class MenuPageTests
{
    private static float U(float degrees) => Glaze.MetersAt(degrees, 1f);

    /// <summary>The menu and a file side by side, each page <paramref name="page"/> tall, their subjects level.</summary>
    private static PlaneComposition MenuAndFile(float page, int subjectRows, TextSize text = TextSize.Standard)
    {
        var subject = MenuPage.Subject(subjectRows, pill: true);
        return new PlaneComposition(new[]
        {
            new PlaneColumn(PlaneComposition.Units(Glaze.Menu.MenuColumnDegrees), subject, MenuPage.Sections, MenuPage.Content(page)),
            new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), subject, MenuPage.Sections, MenuPage.Content(page)),
        }, text == TextSize.Larger ? Comfort.LargerTextScale : 1f);
    }

    [Test]
    public void LinesMeasureByTheTokens()
    {
        Assert.That(MenuPage.Target(), Is.EqualTo(U(Glaze.MinimumTargetDegrees)).Within(1e-6), "a row is 48 dp");
        Assert.That(MenuPage.Target(2), Is.GreaterThan(MenuPage.Target()), "an answer in two rows of words is taller");
        Assert.That(MenuPage.Words(2), Is.EqualTo(2f * MenuPage.Words(1)).Within(1e-6));
        Assert.That(MenuPage.TargetGap, Is.EqualTo(0.012f / 0.46f).Within(1e-6), "12 mm on the plane");
        Assert.That(MenuPage.Rows(4), Is.EqualTo(4f * MenuPage.Target() + 3f * MenuPage.TargetGap).Within(1e-6));
        Assert.That(MenuPage.HalfWidth(Glaze.Menu.FileColumnDegrees) * 2f, Is.LessThan(MenuPage.ContentWidth(Glaze.Menu.FileColumnDegrees)), "two answers share a row with the gap between their shapes");
        Assert.That(MenuPage.Subject(2, pill: true), Is.GreaterThan(MenuPage.Subject(1, pill: true)), "a second row of the title grows its plate");
        Assert.That(MenuPage.Subject(1, pill: false), Is.EqualTo(U(Glaze.Menu.SubjectDegrees)).Within(0.01f), "a one-row subject without a pill is about the plate's least");
    }

    [Test]
    public void ThePlanesTopIsWhereADesignedPanelsTopStandsInAQuest3S()
    {
        foreach (var halfWidth in new[] { 16f, 22f, 32f })
        {
            Assert.That(-(MenuPage.Quest3S.LowestCenter(halfWidth, WorkspacePlacement.DesignedHeightDegrees / 2f) + WorkspacePlacement.DesignedHeightDegrees / 2f),
                Is.EqualTo(MenuPage.TopDegrees).Within(0.05f), "at any width");
        }

        // So every height fits from there up to the most the reading pitch allows, with no gap where the head turns from level to tipped.
        for (var degrees = 20f; degrees <= 33.9f; degrees += 0.1f)
        {
            var composition = new PlaneComposition(new[] { new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), PlaneComposition.Units(degrees)) });
            Assert.That(MenuPage.Fits(composition), Is.True, degrees + " degrees tall");
        }
    }

    [Test]
    public void WhereTheStagePlacesItTheFieldIsHeldTheSameWayWhereverThePersonTurns()
    {
        var composition = MenuAndFile(MenuPage.Rows(4), subjectRows: 1);
        var half = MathF.Atan(composition.Height / 2f) * 180f / MathF.PI;
        foreach (var yaw in new[] { 0f, 35f, -120f })
        {
            Assert.That(MenuPage.Inside(composition, new PanelDirection(yaw, -MenuPage.TopDegrees - half, true, false), MenuPage.Quest3S), Is.True, "at the reference top, turned " + yaw);
            Assert.That(MenuPage.Inside(composition, new PanelDirection(yaw, -MenuPage.TopDegrees - half - 1.5f, true, false), MenuPage.Quest3S), Is.False,
                "placed 1.5 degrees lower, under lower labels, it passes the field");
        }
        Assert.That(MenuPage.Inside(composition, new PanelDirection(0f, -MenuPage.TopDegrees - half - 1.5f, true, false), new ViewField(55, 55, 48, 48)), Is.True,
            "a Quest 3's taller field holds it there");
    }

    [Test]
    public void TheMenuAndAFileFitAQuest3SWithAPageOfRowsAPageRows()
    {
        foreach (var text in new[] { TextSize.Standard, TextSize.Larger })
        {
            var page = MenuPage.Rows(MenuFrame.RowsAPage(text, sourceLine: false));
            Assert.That(MenuPage.Fits(MenuAndFile(page, subjectRows: 1, text)), Is.True, text + ": a page of its rows fits beside a file with the split header");
            Assert.That(MenuPage.Fits(MenuAndFile(page + MenuPage.Rows(1) + MenuPage.TargetGap, subjectRows: 1, text)), Is.False, text + ": a row more passes the field");
        }
    }

    [Test]
    public void ATitleInTwoRowsLeavesLessForThePage()
    {
        foreach (var text in new[] { TextSize.Standard, TextSize.Larger })
        {
            var one = MenuPage.Height(text, subjectRows: 1);
            var two = MenuPage.Height(text, subjectRows: 2);
            Assert.That(two, Is.LessThan(one), text.ToString());
            Assert.That(one - two, Is.EqualTo(U(Glaze.Menu.TitleDegrees) * Glaze.Menu.LineSpacing).Within(0.002f), text + ": by about the row");
        }
        Assert.That(MenuPage.Height(TextSize.Standard, 1), Is.GreaterThanOrEqualTo(MenuPage.Rows(4)), "a lone file holds Tasks' four rows");
        Assert.That(MenuPage.Height(TextSize.Larger, 1), Is.GreaterThanOrEqualTo(MenuPage.Rows(3)), "and three with larger text");
        Assert.That(MenuPage.Height(TextSize.Larger, 1), Is.LessThan(MenuPage.Height(TextSize.Standard, 1)), "larger text grows the frame whole, so its page holds less");
        Assert.Throws<ArgumentOutOfRangeException>(() => MenuPage.Height(TextSize.Standard, 0));
        Assert.That(MenuPage.Height(TextSize.Standard, 1, topDegrees: 19f), Is.LessThan(MenuPage.Height(TextSize.Standard, 1)), "under lower labels a page holds less");
        Assert.That(MenuPage.Height(TextSize.Standard, 1, field: new ViewField(55, 55, 48, 48)), Is.GreaterThan(MenuPage.Height(TextSize.Standard, 1)), "a Quest 3's taller field holds more");
    }

    [Test]
    public void APageAsTallAsItsHeightFitsAndAnyTallerDoesNot()
    {
        foreach (var text in new[] { TextSize.Standard, TextSize.Larger })
        {
            var zoom = text == TextSize.Larger ? Comfort.LargerTextScale : 1f;
            var height = MenuPage.Height(text, 1);
            PlaneComposition File(float page) => new PlaneComposition(new[]
            {
                new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), MenuPage.Subject(1, pill: true), MenuPage.Sections, MenuPage.Content(page)),
            }, zoom);
            Assert.That(MenuPage.Fits(File(height)), Is.True, text.ToString());
            Assert.That(MenuPage.Fits(File(height + U(0.2f))), Is.False, text.ToString());
        }
    }

    [Test]
    public void NeverThreeColumns()
    {
        var column = new PlaneColumn(PlaneComposition.Units(26f), PlaneComposition.Units(20f));
        Assert.Throws<ArgumentException>(() => _ = new PlaneComposition(new[] { column, column, column }), "the menu, a file and a side panel pass a Quest 3S's field");
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: true, fitsBeside: true).Count, Is.EqualTo(2));
    }

    [Test]
    public void TheMenuStepsAsideForAFilesSidePanelAndComesBackWhenItCloses()
    {
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: false, fitsBeside: true), Is.EqualTo(new[] { MenuColumn.Menu, MenuColumn.File }));
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: true, fitsBeside: true), Is.EqualTo(new[] { MenuColumn.File, MenuColumn.Side }),
            "a line in the file opened its side panel: the menu steps aside");
        Assert.That(MenuColumns.MenuAside(menuOpen: true, fileOpen: true, sidePanel: true, fitsBeside: true), Is.True);
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: false, fitsBeside: true), Is.EqualTo(new[] { MenuColumn.Menu, MenuColumn.File }),
            "Close details: the menu comes back where it was");
        Assert.That(MenuColumns.MenuAside(menuOpen: true, fileOpen: true, sidePanel: false, fitsBeside: true), Is.False);
    }

    [Test]
    public void TheMenuStepsAsideWhereTheTwoWouldNotFitAndComesBackWhenTheyDo()
    {
        // Tasks' four rows beside a file whose title takes two rows: the subjects stand level, so the
        // menu's column grows past the field, while the file alone, its page shorter, fits.
        PlaneComposition Beside(int subjectRows, float menuPage, float filePage) => new PlaneComposition(new[]
        {
            new PlaneColumn(PlaneComposition.Units(Glaze.Menu.MenuColumnDegrees), MenuPage.Subject(subjectRows, pill: true), MenuPage.Sections, MenuPage.Content(menuPage)),
            new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), MenuPage.Subject(subjectRows, pill: true), MenuPage.Sections, MenuPage.Content(filePage)),
        });
        var question = MenuPage.Words(2) + MenuPage.Grid + MenuPage.Target();
        var fits = MenuPage.Fits(Beside(2, MenuPage.Rows(4), question));
        Assert.That(fits, Is.False, "four rows under a two-row subject pass the field");
        Assert.That(question, Is.LessThan(MenuPage.Height(TextSize.Standard, 2)), "the file alone holds its page");
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: false, fitsBeside: fits), Is.EqualTo(new[] { MenuColumn.File }));
        Assert.That(MenuColumns.MenuAside(menuOpen: true, fileOpen: true, sidePanel: false, fitsBeside: fits), Is.True);

        var again = MenuPage.Fits(Beside(1, MenuPage.Rows(4), question));
        Assert.That(again, Is.True, "a file whose title takes one row fits beside the menu");
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: false, fitsBeside: again), Is.EqualTo(new[] { MenuColumn.Menu, MenuColumn.File }),
            "the two fit again: the menu comes back");

        // The tallest page a lone file holds passes the field beside the menu, whose plane is wider, its
        // low corners lower in a field tipped down: the menu steps aside for it.
        var tallest = MenuPage.Fits(Beside(1, MenuPage.Rows(4), MenuPage.Height(TextSize.Standard, 1)));
        Assert.That(tallest, Is.False);
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: false, fitsBeside: tallest), Is.EqualTo(new[] { MenuColumn.File }));
    }

    [Test]
    public void WithLargerTextASidePanelTakesItsFramesPlaceAndCloseDetailsBringsItBack()
    {
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: true, fitsBeside: true, sideInPlace: true), Is.EqualTo(new[] { MenuColumn.Side }),
            "the file's side panel stands where the file stood, the menu aside");
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: false, sidePanel: true, fitsBeside: true, sideInPlace: true), Is.EqualTo(new[] { MenuColumn.Side }),
            "the menu's own side panel takes the menu's place");
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: false, fitsBeside: true, sideInPlace: true), Is.EqualTo(new[] { MenuColumn.Menu, MenuColumn.File }),
            "Close details: the file and the menu are back");
        Assert.That(MenuColumns.Arrange(menuOpen: false, fileOpen: false, sidePanel: true, fitsBeside: true, sideInPlace: true), Is.Empty, "a side panel belongs to a frame");
    }

    [Test]
    public void BesideAWindowTheMenuAndAFileStandOneAtATime()
    {
        // The two together clear the characters either side of a window only far below the field: never beside one.
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: true, sidePanel: false, fitsBeside: false), Is.EqualTo(new[] { MenuColumn.File }));
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: false, sidePanel: false, fitsBeside: false), Is.EqualTo(new[] { MenuColumn.Menu }), "the file closed, the menu is back");
    }

    [Test]
    public void ClosingTheFileBringsTheMenuBackAndASidePanelBelongsToTheFrameInFront()
    {
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: false, sidePanel: false, fitsBeside: false), Is.EqualTo(new[] { MenuColumn.Menu }), "the file closed: the menu is back");
        Assert.That(MenuColumns.Arrange(menuOpen: true, fileOpen: false, sidePanel: true, fitsBeside: true), Is.EqualTo(new[] { MenuColumn.Menu, MenuColumn.Side }), "the menu's own side panel");
        Assert.That(MenuColumns.Arrange(menuOpen: false, fileOpen: true, sidePanel: true, fitsBeside: true), Is.EqualTo(new[] { MenuColumn.File, MenuColumn.Side }));
        Assert.That(MenuColumns.Arrange(menuOpen: false, fileOpen: true, sidePanel: false, fitsBeside: true), Is.EqualTo(new[] { MenuColumn.File }));
        Assert.That(MenuColumns.Arrange(menuOpen: false, fileOpen: false, sidePanel: false, fitsBeside: true), Is.Empty, "closed, the menu is its bar");
        Assert.That(MenuColumns.MenuAside(menuOpen: false, fileOpen: true, sidePanel: true, fitsBeside: true), Is.False, "a closed menu is not aside");
    }
}
