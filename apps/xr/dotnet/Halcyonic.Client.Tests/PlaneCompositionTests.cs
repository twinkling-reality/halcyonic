using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>A composition on one plane facing the eyes (ADR 0026), placed as one panel.</summary>
public class PlaneCompositionTests
{
    private const float Radians = MathF.PI / 180f;

    /// <summary>
    /// Lane V's hero: a menu column of 32 degrees and a file column of 36, each a subject, a row of
    /// sections and a page, the file's page the taller; about 64 by 32 degrees in all.
    /// </summary>
    private static PlaneComposition Hero(float zoom = 1f) => new(new[]
    {
        new PlaneColumn(PlaneComposition.Units(32f), PlaneComposition.Units(3f), PlaneComposition.Units(4f), PlaneComposition.Units(18f)),
        new PlaneColumn(PlaneComposition.Units(36f), PlaneComposition.Units(3f), PlaneComposition.Units(4f), PlaneComposition.Units(22f)),
    }, zoom);

    private static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    [Test]
    public void ThePlaneFacesTheEyesSquarelyAtItsCentreAndIsNeverRolled()
    {
        var composition = Hero();
        var direction = new PanelDirection(-8f, -34f, true, false);
        var (forward, right, up) = PlaneComposition.Axes(direction);
        Assert.That(right.Y, Is.Zero, "its rows run level");
        Assert.That(Dot(forward, right), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(Dot(forward, up), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(up.Y, Is.GreaterThan(0f), "its up is up");

        // The centre of every part's corners lies straight along the line of sight, at the plane's distance.
        var corners = composition.Parts.SelectMany(part => new[] { -1f, 1f }.SelectMany(x => new[] { -1f, 1f }
            .Select(y => PlaneComposition.PointOf(direction, part.Right + x * part.Width / 2f, part.Up + y * part.Height / 2f)))).ToList();
        var middle = ((corners.Min(c => c.X) + corners.Max(c => c.X)) / 2f, (corners.Min(c => c.Y) + corners.Max(c => c.Y)) / 2f, (corners.Min(c => c.Z) + corners.Max(c => c.Z)) / 2f);
        var length = MathF.Sqrt(Dot(middle, middle));
        var off = MathF.Acos(Math.Clamp(Dot(middle, forward) / length, -1f, 1f)) / Radians;
        Assert.That(off, Is.LessThan(0.5f), "square to the eyes at its centre, within half a degree");
        foreach (var part in composition.Parts)
        {
            var point = PlaneComposition.PointOf(direction, part.Right, part.Up);
            Assert.That(Dot(point, forward), Is.EqualTo(PlaneComposition.Distance).Within(1e-5f), "every part on the one plane");
        }
    }

    [Test]
    public void PartsStandADegreeApartColumns15MillimetresAndTheColumnsStartAndEndOnOneLine()
    {
        var composition = Hero();
        var partGap = Glaze.MetersAt(PlaneComposition.PartGapDegrees, PlaneComposition.Distance);
        Assert.That(partGap, Is.EqualTo(0.008f).Within(0.0001f), "a degree at 0.46 m is 8 mm");
        foreach (var column in composition.Parts.GroupBy(part => part.Column))
        {
            var stacked = column.OrderBy(part => part.Index).ToList();
            Assert.That(stacked.Select(part => part.Width).Distinct().Count(), Is.EqualTo(1), "one width a column: its edges line up");
            for (var index = 1; index < stacked.Count; index++)
            {
                Assert.That((stacked[index - 1].Bottom - stacked[index].Top) * PlaneComposition.Distance, Is.EqualTo(partGap).Within(1e-6f));
            }
        }
        var columns = composition.Parts.GroupBy(part => part.Column).OrderBy(group => group.Key).ToList();
        var leftRight = columns[0].First().Right + columns[0].First().Width / 2f;
        var rightLeft = columns[1].First().Left;
        Assert.That((rightLeft - leftRight) * PlaneComposition.Distance, Is.EqualTo(PlaneComposition.ColumnGapMeters).Within(1e-6f));
        Assert.That(columns.Select(column => column.Max(part => part.Top)).Distinct().Count(), Is.EqualTo(1), "they start on one line");
        Assert.That(columns.Select(column => column.Min(part => part.Bottom)).Max() - columns.Select(column => column.Min(part => part.Bottom)).Min(),
            Is.EqualTo(0f).Within(1e-6f), "they end on one line");
        var menuPage = composition.Parts.Single(part => part.Column == 0 && part.Index == 2);
        Assert.That(menuPage.Height, Is.GreaterThan(PlaneComposition.Units(18f)), "the shorter column's page stretches down to the taller's bottom");
        Assert.That(composition.Width, Is.EqualTo(PlaneComposition.Units(32f) + PlaneComposition.Units(36f) + PlaneComposition.ColumnGapMeters / PlaneComposition.Distance).Within(1e-6f));
    }

    [Test]
    public void ItOpensUnderEveryLabelAsOnePanel()
    {
        foreach (var zoom in new[] { 1f, Comfort.LargerTextScale })
        {
            var composition = Hero(zoom);
            var characters = Lineups.RaisedArc(plateDegrees: zoom > 1f ? 11f : 10.5f);
            var opened = characters[3];
            var direction = composition.Place(opened.Yaw, opened, characters);
            var size = composition.Size;
            Assert.That(direction.Clear, Is.True, $"at {zoom}");
            Assert.That(direction.Above, Is.False);
            Assert.That(direction.Elevation, Is.InRange(WorkspacePlacement.Lowest(size), WorkspacePlacement.HighestDegrees));
            var corners = WorkspacePlacement.CornerElevation(direction.Elevation + size.HalfHeightDegrees, size.HalfWidthDegrees);
            Assert.That(corners, Is.LessThanOrEqualTo(characters.Min(character => character.Lowest) - 1f), $"a degree or more under every label at {zoom}");
        }
    }

    [Test]
    public void ACompositionGrownForLargerTextGrowsWholeItsGapsWithIt()
    {
        var standard = Hero();
        var larger = Hero(Comfort.LargerTextScale);
        Assert.That(larger.Width, Is.EqualTo(standard.Width * Comfort.LargerTextScale).Within(1e-5f));
        Assert.That(larger.Height, Is.EqualTo(standard.Height * Comfort.LargerTextScale).Within(1e-5f));
        for (var index = 0; index < standard.Parts.Count; index++)
        {
            Assert.That(larger.Parts[index].Right, Is.EqualTo(standard.Parts[index].Right * Comfort.LargerTextScale).Within(1e-5f));
            Assert.That(larger.Parts[index].Up, Is.EqualTo(standard.Parts[index].Up * Comfort.LargerTextScale).Within(1e-5f));
        }
    }

    [Test]
    public void A32DegreeTallCompositionGoesLowerAndIsReadWithTheHeadTippedTheMostAllowed()
    {
        var composition = new PlaneComposition(new[] { new PlaneColumn(PlaneComposition.Units(30f), PlaneComposition.Units(32f)) });
        var size = composition.Size;
        Assert.That(2f * size.HalfHeightDegrees, Is.EqualTo(32f).Within(1e-3f));
        Assert.That(WorkspacePlacement.TallerBy(size), Is.EqualTo(6f).Within(1e-3f));
        Assert.That(WorkspacePlacement.Lowest(size), Is.EqualTo(WorkspacePlacement.LowestDegrees - 6f).Within(1e-3f));
        var quest3S = new ViewField(48, 48, 45, 45);
        Assert.That(WorkspacePlacement.ReadingPitch(size, WorkspacePlacement.Lowest(size, quest3S) - 5f, quest3S), Is.EqualTo(WorkspacePlacement.MostReadingPitchDegrees),
            "below the field's floor, the head tips the most it may, and no more");
        Assert.That(WorkspacePlacement.Lowest(size, quest3S),
            Is.EqualTo(Math.Max(WorkspacePlacement.LowestDegrees - 6f, quest3S.LowestCenter(size.HalfWidthDegrees, size.HalfHeightDegrees) - 8f)).Within(1e-3f));
    }

    [Test]
    public void TextShrinksAwayFromThePlanesCentreAndSmallTextTakesTheContentsSizeWhereItWouldReadUnder14Dp()
    {
        Assert.That(PlaneComposition.ShrinkAt(0f, 0f), Is.EqualTo(1f));
        // A word straight across: farther by 1/cos and seen square on, so cos of the angle.
        var across = MathF.Tan(30f * Radians);
        Assert.That(PlaneComposition.ShrinkAt(across, 0f), Is.EqualTo(MathF.Cos(30f * Radians)).Within(1e-5f));
        // Straight up: farther by 1/cos and seen at a slant, so cos squared.
        Assert.That(PlaneComposition.ShrinkAt(0f, MathF.Tan(20f * Radians)), Is.EqualTo(MathF.Pow(MathF.Cos(20f * Radians), 2f)).Within(1e-5f));
        Assert.That(PlaneComposition.ShrinkAt(0.3f, -0.2f), Is.EqualTo(PlaneComposition.ShrinkAt(-0.3f, 0.2f)), "the same in every direction");

        Assert.That(PlaneComposition.SmallTextDegreesAt(0f, 0f), Is.EqualTo(Glaze.Menu.LabelDegrees), "15 dp at the centre");
        Assert.That(PlaneComposition.SmallTextDegreesAt(MathF.Tan(15f * Radians), 0f), Is.EqualTo(Glaze.Menu.LabelDegrees));
        // A file and its side panel, about 60 degrees wide: a count at the far right reads 0.81 degrees at 15 dp.
        Assert.That(Glaze.Menu.LabelDegrees * PlaneComposition.ShrinkAt(across, 0f), Is.LessThan(Glaze.MinimumTextDegrees));
        Assert.That(PlaneComposition.SmallTextDegreesAt(across, 0f), Is.EqualTo(Glaze.Menu.BodyDegrees), "there it takes the content's 18 dp");
        Assert.That(Glaze.Menu.BodyDegrees * PlaneComposition.ShrinkAt(across, 0f), Is.GreaterThanOrEqualTo(Glaze.MinimumTextDegrees), "which reads");
        // 15 dp reads where the shrink is 14/15 or more.
        var edge = MathF.Sqrt(MathF.Pow(15f / 14f, 2f) - 1f);
        Assert.That(PlaneComposition.SmallTextDegreesAt(edge * 0.99f, 0f), Is.EqualTo(Glaze.Menu.LabelDegrees));
        Assert.That(PlaneComposition.SmallTextDegreesAt(edge * 1.01f, 0f), Is.EqualTo(Glaze.Menu.BodyDegrees));
    }

    [Test]
    public void AColumnNeedsAWidthAndAPartAndACompositionAColumn()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaneColumn(0f, 1f));
        Assert.Throws<ArgumentException>(() => new PlaneColumn(1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaneColumn(1f, 0.1f, -0.1f));
        Assert.Throws<ArgumentException>(() => new PlaneComposition(Array.Empty<PlaneColumn>()));
    }
    [TestCase(1f)]
    [TestCase(1.15f)]
    public void SharedHeaderIsMeasuredAboveAllConnectedColumnsAndInsideCenteredWorkspace(float scale)
    {
        var heading = MenuPage.Words(2) + MenuPage.Target() + 3f * MenuPage.TargetGap;
        var body = new[] { new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), MenuPage.Subject(1, true), MenuPage.Content(MenuPage.Rows(2))) };
        var plain = new PlaneComposition(body, scale);
        var framed = new PlaneComposition(body, scale, heading);
        Assert.That(framed.Height - plain.Height, Is.EqualTo((heading + Glaze.MetersAt(PlaneComposition.PartGapDegrees, 1f)) * scale).Within(1e-6f));
        Assert.That(framed.Parts[0].Up + framed.Parts[0].Height / 2f, Is.EqualTo(framed.Height / 2f - (heading + Glaze.MetersAt(PlaneComposition.PartGapDegrees, 1f)) * scale).Within(1e-6f));
        Assert.That(MenuPage.Inside(framed, new PanelDirection(0f, 0f, true, false), MenuPage.Quest3S), Is.True);
        var text = scale > 1f ? TextSize.Larger : TextSize.Standard;
        Assert.That(MenuPage.Height(text, 1, header: heading, centered: true), Is.GreaterThan(MenuPage.Rows(2)), "the full field stays usable without a character label band");
    }

    [TestCase(TextSize.Standard)]
    [TestCase(TextSize.Larger)]
    public void IndependentConsolePacksAgainstOccupiedShelfWithoutChangingItsPosition(TextSize text)
    {
        var heading = MenuPage.Words(2) + MenuPage.Target() + 3f * MenuPage.TargetGap;
        var position = new PanelDirection(0f, 0f, true, false);
        var shelf = new[] { new BodyInView(0f, -27f, 3f, lowest: -32f, halfWidth: 8f) };
        var available = MenuPage.Height(text, 1, header: heading, centered: true, placement: position, occupied: shelf);
        var clear = MenuPage.Height(text, 1, header: heading, centered: true, placement: position);
        Assert.That(available, Is.GreaterThan(0f));
        Assert.That(available, Is.LessThan(clear), "visible agent geometry constrains height rather than relocating the console");
        var scale = text == TextSize.Larger ? Comfort.LargerTextScale : 1f;
        var packed = new PlaneComposition(new[] { new PlaneColumn(PlaneComposition.Units(Glaze.Menu.FileColumnDegrees), MenuPage.Subject(1, true), MenuPage.Sections, MenuPage.Content(available)) }, scale, heading);
        Assert.That(WorkspacePlacement.Clears(position, shelf, packed.Size), Is.True);
        Assert.That(MenuPage.Inside(packed, position, MenuPage.Quest3S), Is.True);
    }

}
