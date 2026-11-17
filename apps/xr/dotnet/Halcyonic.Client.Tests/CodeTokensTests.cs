using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The shared highlighter (the lead's 2026-11-17T03:36Z): each language's runs, what a line leaves open, and every character in one run.</summary>
public class CodeTokensTests
{
    private static List<(string Text, CodeTokenKind Kind)> Runs(string line, CodeLanguage language)
    {
        var carry = CodeCarry.None;
        return Runs(line, language, ref carry);
    }

    private static List<(string Text, CodeTokenKind Kind)> Runs(string line, CodeLanguage language, ref CodeCarry carry)
    {
        var tokens = CodeTokens.Of(line, language, ref carry);
        // Every character in exactly one run, in order.
        var at = 0;
        foreach (var token in tokens)
        {
            Assert.That(token.Start, Is.EqualTo(at), "runs leave a gap or overlap in \"" + line + "\"");
            Assert.That(token.Length, Is.GreaterThan(0));
            at += token.Length;
        }
        Assert.That(at, Is.EqualTo(line.Length));
        return tokens.Select(token => (line.Substring(token.Start, token.Length), token.Kind)).ToList();
    }

    private static CodeTokenKind KindOf(List<(string Text, CodeTokenKind Kind)> runs, string text) => runs.Single(run => run.Text == text).Kind;

    [TestCase("src/game/kite.ts", CodeLanguage.Script)]
    [TestCase("app.jsx", CodeLanguage.Script)]
    [TestCase("Assets/Kite.cs", CodeLanguage.CSharp)]
    [TestCase("tool.py", CodeLanguage.Python)]
    [TestCase("package.json", CodeLanguage.Json)]
    [TestCase("Cargo.toml", CodeLanguage.Toml)]
    [TestCase("ci.yml", CodeLanguage.Yaml)]
    [TestCase("run.sh", CodeLanguage.Shell)]
    [TestCase("README.md", CodeLanguage.Markdown)]
    [TestCase("View.swift", CodeLanguage.Swift)]
    [TestCase("notes.txt", CodeLanguage.Plain)]
    [TestCase("LICENSE", CodeLanguage.Plain)]
    public void TheLanguageComesFromTheFilesName(string path, CodeLanguage language) => Assert.That(CodeTokens.LanguageOf(path), Is.EqualTo(language));

    [Test]
    public void ScriptNamesItsKeywordsStringsNumbersCommentsTypesAndCalls()
    {
        var runs = Runs("const kite = new Kite({ tail: 12 }); // fly", CodeLanguage.Script);
        Assert.That(KindOf(runs, "const"), Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(KindOf(runs, "new"), Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(KindOf(runs, "Kite"), Is.EqualTo(CodeTokenKind.Name), "a called word is a name");
        Assert.That(KindOf(runs, "12"), Is.EqualTo(CodeTokenKind.Number));
        Assert.That(KindOf(runs, "// fly"), Is.EqualTo(CodeTokenKind.Comment));
        var more = Runs("import { Kite } from './kite.js';", CodeLanguage.Script);
        Assert.That(KindOf(more, "'./kite.js'"), Is.EqualTo(CodeTokenKind.String));
        Assert.That(KindOf(more, "Kite"), Is.EqualTo(CodeTokenKind.Type));
    }

    [Test]
    public void ABlockCommentAndATemplateStringRunOverLines()
    {
        var carry = CodeCarry.None;
        Assert.That(Runs("let a = 1; /* open", CodeLanguage.Script, ref carry).Last().Kind, Is.EqualTo(CodeTokenKind.Comment));
        Assert.That(carry, Is.EqualTo(CodeCarry.BlockComment));
        var middle = Runs("still comment", CodeLanguage.Script, ref carry);
        Assert.That(middle.Single().Kind, Is.EqualTo(CodeTokenKind.Comment));
        var close = Runs("end */ let b = 2;", CodeLanguage.Script, ref carry);
        Assert.That(close[0], Is.EqualTo(("end */", CodeTokenKind.Comment)));
        Assert.That(KindOf(close, "let"), Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(carry, Is.EqualTo(CodeCarry.None));
        Runs("const t = `line one", CodeLanguage.Script, ref carry);
        Assert.That(carry, Is.EqualTo(CodeCarry.TemplateString));
        Assert.That(Runs("line two` + x;", CodeLanguage.Script, ref carry)[0], Is.EqualTo(("line two`", CodeTokenKind.String)));
    }

    [Test]
    public void CSharpNamesItsBuiltInTypesAsKeywordsAndItsOwnTypesAsTypes()
    {
        var runs = Runs("public sealed class Kite : MonoBehaviour { private int tail = 3; }", CodeLanguage.CSharp);
        Assert.That(KindOf(runs, "public"), Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(KindOf(runs, "int"), Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(KindOf(runs, "Kite"), Is.EqualTo(CodeTokenKind.Type));
        Assert.That(KindOf(runs, "MonoBehaviour"), Is.EqualTo(CodeTokenKind.Type));
        Assert.That(KindOf(runs, "3"), Is.EqualTo(CodeTokenKind.Number));
    }

    [Test]
    public void PythonHasHashCommentsAndTripleStringsOverLines()
    {
        var carry = CodeCarry.None;
        var runs = Runs("def climb(height): # up", CodeLanguage.Python, ref carry);
        Assert.That(KindOf(runs, "def"), Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(KindOf(runs, "climb"), Is.EqualTo(CodeTokenKind.Name));
        Assert.That(KindOf(runs, "# up"), Is.EqualTo(CodeTokenKind.Comment));
        Runs("    \"\"\"Climb higher", CodeLanguage.Python, ref carry);
        Assert.That(carry, Is.EqualTo(CodeCarry.TripleString));
        Assert.That(Runs("    than before.\"\"\"", CodeLanguage.Python, ref carry).Single().Kind, Is.EqualTo(CodeTokenKind.String));
        Assert.That(carry, Is.EqualTo(CodeCarry.None));
    }

    [Test]
    public void DataFilesNameTheirKeysAndColourTheirValues()
    {
        var json = Runs("  \"name\": \"kite\", \"lift\": 0.8, \"ok\": true", CodeLanguage.Json);
        Assert.That(KindOf(json, "\"name\""), Is.EqualTo(CodeTokenKind.Name));
        Assert.That(KindOf(json, "\"kite\""), Is.EqualTo(CodeTokenKind.String));
        Assert.That(KindOf(json, "0.8"), Is.EqualTo(CodeTokenKind.Number));
        Assert.That(KindOf(json, "true"), Is.EqualTo(CodeTokenKind.Keyword));
        var toml = Runs("version = \"1.2\" # pinned", CodeLanguage.Toml);
        Assert.That(KindOf(toml, "version"), Is.EqualTo(CodeTokenKind.Name));
        Assert.That(KindOf(toml, "\"1.2\""), Is.EqualTo(CodeTokenKind.String));
        Assert.That(KindOf(toml, "# pinned"), Is.EqualTo(CodeTokenKind.Comment));
        var yaml = Runs("runs-on: ubuntu-latest", CodeLanguage.Yaml);
        Assert.That(KindOf(yaml, "runs-on"), Is.EqualTo(CodeTokenKind.Name));
    }

    [Test]
    public void ShellKeepsAHashInsideAWordAndNamesItsKeywords()
    {
        var runs = Runs("if [ -f a#b ]; then echo \"ok\"; fi # done", CodeLanguage.Shell);
        Assert.That(KindOf(runs, "if"), Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(KindOf(runs, "echo"), Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(KindOf(runs, "\"ok\""), Is.EqualTo(CodeTokenKind.String));
        Assert.That(KindOf(runs, "# done"), Is.EqualTo(CodeTokenKind.Comment));
        Assert.That(runs.Any(run => run.Text.Contains("a#b") && run.Kind == CodeTokenKind.Comment), Is.False, "a # inside a word is not a comment");
    }

    [Test]
    public void SwiftAndMarkdownAndPlainText()
    {
        var swift = Runs("func climb(_ height: Double) -> Bool { return true }", CodeLanguage.Swift);
        Assert.That(KindOf(swift, "func"), Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(KindOf(swift, "climb"), Is.EqualTo(CodeTokenKind.Name));
        Assert.That(KindOf(swift, "Double"), Is.EqualTo(CodeTokenKind.Type));
        Assert.That(Runs("# Kite Run", CodeLanguage.Markdown).Single().Kind, Is.EqualTo(CodeTokenKind.Keyword));
        Assert.That(KindOf(Runs("Run `pnpm dev` first", CodeLanguage.Markdown), "`pnpm dev`"), Is.EqualTo(CodeTokenKind.String));
        Assert.That(Runs("anything at all 12 \"x\"", CodeLanguage.Plain).Single().Kind, Is.EqualTo(CodeTokenKind.Plain), "plain text is never guessed at");
        Assert.That(CodeTokens.Of("", CodeLanguage.Script, ref Unused()), Is.Empty);
    }

    [Test]
    public void EscapesUnterminatedStringsALongRowAndTagsAreTakenAsTheyStand()
    {
        var escaped = Runs("const s = \"a \\\" b\"; x", CodeLanguage.Script);
        Assert.That(KindOf(escaped, "\"a \\\" b\""), Is.EqualTo(CodeTokenKind.String), "an escaped quote stays inside its string");
        var open = Runs("let s = 'never closed", CodeLanguage.Script);
        Assert.That(open.Last(), Is.EqualTo(("'never closed", CodeTokenKind.String)));
        // A 10 kB row: coloured up to the cap, plain after, every character still in one run.
        var longRow = "let x = 1; " + new string('a', 10_000);
        var carry = CodeCarry.None;
        var tokens = CodeTokens.Of(longRow, CodeLanguage.Script, ref carry);
        Assert.That(tokens.Sum(token => token.Length), Is.EqualTo(longRow.Length));
        Assert.That(tokens.Last().Kind, Is.EqualTo(CodeTokenKind.Plain));
        Assert.That(tokens.Last().Start + tokens.Last().Length, Is.EqualTo(longRow.Length));
        Assert.That(tokens.Count, Is.LessThan(20));
        // Tags in the file are its text (AUDIT3's C2): runs cover them as written, never read or removed.
        var tagged = "const t = \"<color=red>x</color>\"; </noparse> <b>";
        var runs = Runs(tagged, CodeLanguage.Script);
        Assert.That(string.Concat(runs.Select(run => run.Text)), Is.EqualTo(tagged));
        Assert.That(KindOf(runs, "\"<color=red>x</color>\""), Is.EqualTo(CodeTokenKind.String));
    }

    [Test]
    public void AMarkdownFenceKeepsItsCodePlainUntilItCloses()
    {
        var carry = CodeCarry.None;
        Runs("```js", CodeLanguage.Markdown, ref carry);
        Assert.That(carry, Is.EqualTo(CodeCarry.Fence));
        Assert.That(Runs("# not a heading inside a fence", CodeLanguage.Markdown, ref carry).Single().Kind, Is.EqualTo(CodeTokenKind.Plain));
        Runs("```", CodeLanguage.Markdown, ref carry);
        Assert.That(carry, Is.EqualTo(CodeCarry.None));
        Assert.That(Runs("# a heading again", CodeLanguage.Markdown, ref carry).Single().Kind, Is.EqualTo(CodeTokenKind.Keyword));
    }

    [Test]
    public void AShellHeredocStaysPlainUntilItsEndingWord()
    {
        var carry = CodeCarry.None;
        Runs("cat <<'EOF' > notes.txt", CodeLanguage.Shell, ref carry);
        Assert.That(carry, Is.EqualTo(CodeCarry.HereDoc("EOF")));
        Assert.That(Runs("if this were code # it is not", CodeLanguage.Shell, ref carry).Single().Kind, Is.EqualTo(CodeTokenKind.Plain), "a heredoc's lines are its text");
        Assert.That(carry, Is.EqualTo(CodeCarry.HereDoc("EOF")));
        Assert.That(Runs("EOF", CodeLanguage.Shell, ref carry).Single().Kind, Is.EqualTo(CodeTokenKind.Plain));
        Assert.That(carry, Is.EqualTo(CodeCarry.None));
        Assert.That(KindOf(Runs("echo done", CodeLanguage.Shell, ref carry), "echo"), Is.EqualTo(CodeTokenKind.Keyword), "code again after it");
        var hereString = CodeCarry.None;
        Runs("read x <<< \"word\"", CodeLanguage.Shell, ref hereString);
        Assert.That(hereString, Is.EqualTo(CodeCarry.None), "a here-string opens no heredoc");
    }

    private static CodeCarry unused;

    private static ref CodeCarry Unused() => ref unused;
}
