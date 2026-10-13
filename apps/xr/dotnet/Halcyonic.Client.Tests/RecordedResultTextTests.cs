using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class RecordedResultTextTests
{
    [Test]
    public void OutputKeepsBlankLinesIndentationAndTrailingSpacesAsOutsideText()
    {
        const string text = "\n  first  \n\n    second 😀\n";
        var words = RecordedResultText.Body(text);
        Assert.That(words.Text, Is.EqualTo(text));
        Assert.That(words.AllTheirs, Is.True);
        Assert.That(words.ForTextMeshPro().Text, Is.EqualTo(text));
        Assert.That(LabelText.Plain(text), Is.EqualTo("first second 😀"), "ordinary labels keep their existing rule");
    }

    [Test]
    public void ControlsBidiIconsAndUnpairedSurrogatesUseTheExistingLiteralPolicy()
    {
        const string text = "  +\\n<color=red>\t\r\u001B[31m\u202E\uE769\uD800\n- end";
        var words = RecordedResultText.Body(text);
        Assert.That(words.Text, Is.EqualTo("  +\\n<color=red>‹U+0009›‹U+000D›‹U+001B›[31m‹U+202E›‹U+E769›‹U+D800›\n- end"));
        Assert.That(words.ForTextMeshPro().Text, Does.StartWith("  +\\\\n<color=red>"), "literal backslashes escape exactly once at the renderer boundary");
        Assert.That(RecordedResultText.Body(words.Text).Text, Is.EqualTo(words.Text));
    }
}
