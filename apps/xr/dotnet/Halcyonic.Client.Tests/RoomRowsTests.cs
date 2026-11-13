#nullable enable
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The information panel's rows: eight a page, and one line for every row's words where any row is marked.</summary>
public sealed class RoomRowsTests
{
    private static List<RoomRow> Rows(int count, params int[] marked) =>
        Enumerable.Range(0, count).Select(index => new RoomRow(LineWords.Ours("Row " + index), marked.Contains(index) ? RoomRowMark.Check : RoomRowMark.None)).ToList();

    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(8, 1)]
    [TestCase(9, 2)]
    [TestCase(16, 2)]
    [TestCase(17, 3)]
    public void Eight_rows_stand_on_a_page(int rows, int pages) => Assert.That(RoomRows.Pages(rows), Is.EqualTo(pages));

    [Test]
    public void A_page_holds_its_own_rows_in_order_and_a_page_past_the_last_is_the_last()
    {
        var rows = Rows(19);
        Assert.That(RoomRows.Page(rows, 0).Select(row => row.Words.Text), Is.EqualTo(Enumerable.Range(0, 8).Select(index => "Row " + index)));
        Assert.That(RoomRows.Page(rows, 1).First().Words.Text, Is.EqualTo("Row 8"));
        Assert.That(RoomRows.Page(rows, 2).Select(row => row.Words.Text), Is.EqualTo(new[] { "Row 16", "Row 17", "Row 18" }));
        Assert.That(RoomRows.Page(rows, 7).Select(row => row.Words.Text), Is.EqualTo(new[] { "Row 16", "Row 17", "Row 18" }));
        Assert.That(RoomRows.Page(rows, -1).First().Words.Text, Is.EqualTo("Row 0"));
        Assert.That(RoomRows.Page(Rows(0), 0), Is.Empty);
    }

    [Test]
    public void One_marked_row_moves_every_rows_words_after_the_marks_place()
    {
        Assert.That(RoomRows.Marked(Rows(3)), Is.False);
        Assert.That(RoomRows.IndentDp(Rows(3)), Is.EqualTo(0f));
        Assert.That(RoomRows.Marked(Rows(3, 1)), Is.True);
        Assert.That(RoomRows.IndentDp(Rows(3, 1)), Is.EqualTo(30f), "the mark's 20 dp and the 10 dp after it");
        Assert.That(new RoomRow(LineWords.Ours("Build")).Mark, Is.EqualTo(RoomRowMark.None));
    }

    [Test]
    public void The_hairline_blended_in_linear_light_reads_as_the_pages_white_at_12_percent()
    {
        static double Decode(double value) => value <= 0.04045 ? value / 12.92 : System.Math.Pow((value + 0.055) / 1.055, 2.4);
        static double Encode(double value) => value <= 0.0031308 ? value * 12.92 : 1.055 * System.Math.Pow(value, 1 / 2.4) - 0.055;
        var ground = RoomRows.PanelGround;
        var alpha = RoomRows.LightAlpha(RoomRows.HairlineAlpha, ground);
        Assert.That(alpha, Is.InRange(0.03f, 0.07f));
        var blended = Encode(alpha + (1 - alpha) * Decode(ground));
        Assert.That(blended, Is.EqualTo(ground + (1 - ground) * 0.12).Within(0.002), "the same light as the page's mix in sRGB");
        Assert.That(RoomRows.LightAlpha(0f, ground), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(RoomRows.LightAlpha(1f, ground), Is.EqualTo(1f).Within(1e-5f));
    }
    [Test]
    public void ARowsLabelAndStateStandApartFromItsWordsNeverJoinedOnADot()
    {
        var step = new RoomRow(LineWords.Ours("Create Project"), note: " Confirmed ");
        var fact = new RoomRow(LineWords.TheirName("Make a kite that follows my hand"), label: "Project");
        var plain = new RoomRow(LineWords.Ours("Every check passed"), label: " ", note: "");
        Assert.That((step.Words.Text, step.Note, step.Label), Is.EqualTo(("Create Project", "Confirmed", (string?)null)), "the state is its own part, trimmed");
        Assert.That((fact.Label, fact.Words.AllTheirs), Is.EqualTo(("Project", true)), "the label is ours, the words stay theirs");
        Assert.That((plain.Label, plain.Note), Is.EqualTo(((string?)null, (string?)null)), "blank parts are none");
        Assert.That(step.Words.Text.Contains("·") || fact.Words.Text.Contains("·"), Is.False);
    }
}
