#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>What a run of code is, for its colour: the theme maps each kind to a colour (FIELD's palette); Plain is the line's own.</summary>
    public enum CodeTokenKind
    {
        Plain,
        Keyword,
        String,
        Number,
        Comment,
        Type,
        Name,
    }

    /// <summary>A language the highlighter knows, from a file's name; Plain where it knows none.</summary>
    public enum CodeLanguage
    {
        Plain,
        Script,
        CSharp,
        Python,
        Json,
        Toml,
        Yaml,
        Shell,
        Markdown,
        Swift,
    }

    /// <summary>One run of a line: where it starts, how long it is, and what it is.</summary>
    public readonly struct CodeToken
    {
        public CodeToken(int start, int length, CodeTokenKind kind)
        {
            Start = start;
            Length = length;
            Kind = kind;
        }

        public int Start { get; }

        public int Length { get; }

        public CodeTokenKind Kind { get; }
    }

    /// <summary>What a line leaves open for the next: nothing, a block comment, or a string that runs over lines.</summary>
    public enum CodeCarry
    {
        None,
        BlockComment,
        TripleString,
        TemplateString,
        Fence,
    }

    /// <summary>
    /// The shared highlighter every code view uses (the lead's 2026-11-17T03:36Z ruling): a small tokenizer, no dependency,
    /// over one line at a time with what an earlier line left open, so a view runs it only on the rows it draws. It only
    /// names runs; colours are the theme's. Anything it does not know stays Plain, never a guess dressed as a kind.
    /// </summary>
    public static class CodeTokens
    {
        /// <summary>The language of a file by its name's extension; Plain where none fits.</summary>
        public static CodeLanguage LanguageOf(string path)
        {
            var name = path.Substring(path.LastIndexOf('/') + 1).ToLowerInvariant();
            if (name == "dockerfile" || name == "makefile") return CodeLanguage.Shell;
            var dot = name.LastIndexOf('.');
            var extension = dot < 0 ? "" : name.Substring(dot + 1);
            switch (extension)
            {
                case "ts": case "tsx": case "js": case "jsx": case "mjs": case "cjs": return CodeLanguage.Script;
                case "cs": return CodeLanguage.CSharp;
                case "py": return CodeLanguage.Python;
                case "json": return CodeLanguage.Json;
                case "toml": return CodeLanguage.Toml;
                case "yml": case "yaml": return CodeLanguage.Yaml;
                case "sh": case "bash": case "zsh": return CodeLanguage.Shell;
                case "md": case "markdown": return CodeLanguage.Markdown;
                case "swift": return CodeLanguage.Swift;
                default: return CodeLanguage.Plain;
            }
        }

        private static readonly HashSet<string> ScriptWords = new HashSet<string>
        {
            "abstract", "as", "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default", "delete", "do",
            "else", "enum", "export", "extends", "false", "finally", "for", "from", "function", "get", "if", "implements", "import", "in",
            "instanceof", "interface", "let", "new", "null", "of", "private", "protected", "public", "readonly", "return", "set", "static",
            "super", "switch", "this", "throw", "true", "try", "type", "typeof", "undefined", "var", "void", "while", "with", "yield",
        };

        private static readonly HashSet<string> CSharpWords = new HashSet<string>
        {
            "abstract", "as", "async", "await", "base", "bool", "break", "byte", "case", "catch", "char", "class", "const", "continue",
            "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "false", "finally", "float", "for", "foreach", "get",
            "if", "in", "init", "int", "interface", "internal", "is", "long", "namespace", "new", "null", "object", "out", "override",
            "params", "private", "protected", "public", "readonly", "record", "ref", "return", "sealed", "set", "static", "string",
            "struct", "switch", "this", "throw", "true", "try", "typeof", "uint", "using", "var", "virtual", "void", "while", "yield",
        };

        private static readonly HashSet<string> PythonWords = new HashSet<string>
        {
            "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif", "else", "except", "False",
            "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "None", "nonlocal", "not", "or", "pass", "raise",
            "return", "True", "try", "while", "with", "yield", "self",
        };

        private static readonly HashSet<string> SwiftWords = new HashSet<string>
        {
            "as", "async", "await", "break", "case", "catch", "class", "continue", "default", "defer", "do", "else", "enum", "extension",
            "false", "fileprivate", "for", "func", "guard", "if", "import", "in", "init", "internal", "is", "let", "nil", "private",
            "protocol", "public", "return", "self", "static", "struct", "switch", "throw", "throws", "true", "try", "var", "where", "while",
        };

        private static readonly HashSet<string> ShellWords = new HashSet<string>
        {
            "case", "do", "done", "elif", "else", "esac", "export", "fi", "for", "function", "if", "in", "local", "return", "then",
            "until", "while", "echo", "cd", "set", "unset", "source",
        };

        private static readonly HashSet<string> DataWords = new HashSet<string> { "true", "false", "null", "yes", "no", "on", "off" };

        private static readonly HashSet<string> NoWords = new HashSet<string>();

        private static HashSet<string> WordsOf(CodeLanguage language) => language switch
        {
            CodeLanguage.Script => ScriptWords,
            CodeLanguage.CSharp => CSharpWords,
            CodeLanguage.Python => PythonWords,
            CodeLanguage.Swift => SwiftWords,
            CodeLanguage.Shell => ShellWords,
            CodeLanguage.Json or CodeLanguage.Toml or CodeLanguage.Yaml => DataWords,
            _ => NoWords,
        };

        /// <summary>How much of a row it colours (AUDIT3's C3): past this many characters the row stays plain.</summary>
        public const int MostColoured = 400;

        /// <summary>
        /// The runs of <paramref name="line"/> in <paramref name="language"/>, every character in exactly one run, in order, the
        /// text never changed (AUDIT3's C1); <paramref name="carry"/> is what the line before left open, and comes back as what
        /// this one leaves open. One pass, linear in the line's length (C3); past <see cref="MostColoured"/> characters, plain.
        /// </summary>
        public static IReadOnlyList<CodeToken> Of(string line, CodeLanguage language, ref CodeCarry carry)
        {
            if (line.Length > MostColoured)
            {
                var head = new List<CodeToken>(Of(line.Substring(0, MostColoured), language, ref carry));
                if (head.Count > 0 && head[head.Count - 1].Kind == CodeTokenKind.Plain)
                    head[head.Count - 1] = new CodeToken(head[head.Count - 1].Start, line.Length - head[head.Count - 1].Start, CodeTokenKind.Plain);
                else head.Add(new CodeToken(MostColoured, line.Length - MostColoured, CodeTokenKind.Plain));
                return head;
            }
            var runs = new List<CodeToken>();
            if (language == CodeLanguage.Plain || line.Length == 0)
            {
                if (line.Length > 0) runs.Add(new CodeToken(0, line.Length, CodeTokenKind.Plain));
                return runs;
            }
            if (language == CodeLanguage.Markdown) return Markdown(line, ref carry);
            var words = WordsOf(language);
            var hashComments = language is CodeLanguage.Python or CodeLanguage.Shell or CodeLanguage.Toml or CodeLanguage.Yaml;
            var slashComments = language is CodeLanguage.Script or CodeLanguage.CSharp or CodeLanguage.Swift;
            var at = 0;
            void Add(int start, int end, CodeTokenKind kind)
            {
                if (end <= start) return;
                if (runs.Count > 0 && runs[runs.Count - 1].Kind == kind && runs[runs.Count - 1].Start + runs[runs.Count - 1].Length == start)
                {
                    var last = runs[runs.Count - 1];
                    runs[runs.Count - 1] = new CodeToken(last.Start, end - last.Start, kind);
                }
                else runs.Add(new CodeToken(start, end - start, kind));
            }
            // What an earlier line left open runs on first.
            if (carry == CodeCarry.BlockComment)
            {
                var close = line.IndexOf("*/", StringComparison.Ordinal);
                if (close < 0) { Add(0, line.Length, CodeTokenKind.Comment); return runs; }
                Add(0, close + 2, CodeTokenKind.Comment);
                at = close + 2;
                carry = CodeCarry.None;
            }
            else if (carry == CodeCarry.TripleString || carry == CodeCarry.TemplateString)
            {
                var mark = carry == CodeCarry.TripleString ? "\"\"\"" : "`";
                var close = line.IndexOf(mark, StringComparison.Ordinal);
                if (close < 0) { Add(0, line.Length, CodeTokenKind.String); return runs; }
                Add(0, close + mark.Length, CodeTokenKind.String);
                at = close + mark.Length;
                carry = CodeCarry.None;
            }
            // A data file's key, where a line begins with one: everything up to its ':' or '=', hyphens and dots with it.
            if (at == 0 && (language == CodeLanguage.Yaml || language == CodeLanguage.Toml))
            {
                var start = 0;
                while (start < line.Length && (line[start] == ' ' || (language == CodeLanguage.Yaml && line[start] == '-' && start + 1 < line.Length && line[start + 1] == ' '))) start++;
                var end = start;
                while (end < line.Length && (IsWordChar(line[end]) || line[end] == '-' || line[end] == '.')) end++;
                var next = NextNonSpace(line, end);
                if (end > start && (next == ':' || (language == CodeLanguage.Toml && next == '=')))
                {
                    Add(0, start, CodeTokenKind.Plain);
                    Add(start, end, CodeTokenKind.Name);
                    at = end;
                }
            }
            while (at < line.Length)
            {
                var c = line[at];
                // Comments.
                if (slashComments && c == '/' && at + 1 < line.Length && line[at + 1] == '/') { Add(at, line.Length, CodeTokenKind.Comment); break; }
                if (slashComments && c == '/' && at + 1 < line.Length && line[at + 1] == '*')
                {
                    var close = line.IndexOf("*/", at + 2, StringComparison.Ordinal);
                    if (close < 0) { Add(at, line.Length, CodeTokenKind.Comment); carry = CodeCarry.BlockComment; break; }
                    Add(at, close + 2, CodeTokenKind.Comment);
                    at = close + 2;
                    continue;
                }
                if (hashComments && c == '#' && (language != CodeLanguage.Shell || at == 0 || char.IsWhiteSpace(line[at - 1]))) { Add(at, line.Length, CodeTokenKind.Comment); break; }
                // Strings.
                if (language == CodeLanguage.Python && line.Length - at >= 3 && string.CompareOrdinal(line, at, "\"\"\"", 0, 3) == 0)
                {
                    var close = line.IndexOf("\"\"\"", at + 3, StringComparison.Ordinal);
                    if (close < 0) { Add(at, line.Length, CodeTokenKind.String); carry = CodeCarry.TripleString; break; }
                    Add(at, close + 3, CodeTokenKind.String);
                    at = close + 3;
                    continue;
                }
                if (c == '`' && language == CodeLanguage.Script)
                {
                    var close = line.IndexOf('`', at + 1);
                    if (close < 0) { Add(at, line.Length, CodeTokenKind.String); carry = CodeCarry.TemplateString; break; }
                    Add(at, close + 1, CodeTokenKind.String);
                    at = close + 1;
                    continue;
                }
                if (c == '"' || (c == '\'' && language != CodeLanguage.Swift))
                {
                    var end = at + 1;
                    while (end < line.Length && line[end] != c) end += line[end] == '\\' && language != CodeLanguage.Shell ? 2 : 1;
                    end = Math.Min(line.Length, end + 1);
                    // A data file's key is a name, its quoted value a string.
                    var keyed = (language == CodeLanguage.Json || language == CodeLanguage.Yaml) && NextNonSpace(line, end) == ':';
                    Add(at, end, keyed ? CodeTokenKind.Name : CodeTokenKind.String);
                    at = end;
                    continue;
                }
                // Numbers.
                if (char.IsDigit(c) && (at == 0 || !IsWordChar(line[at - 1])))
                {
                    var end = at + 1;
                    while (end < line.Length && (char.IsLetterOrDigit(line[end]) || line[end] == '.' || line[end] == '_')) end++;
                    Add(at, end, CodeTokenKind.Number);
                    at = end;
                    continue;
                }
                // Words: keywords, types, names called or keyed, else plain.
                if (IsWordStart(c, language))
                {
                    var end = at + 1;
                    while (end < line.Length && IsWordChar(line[end])) end++;
                    var word = line.Substring(at, end - at);
                    var next = NextNonSpace(line, end);
                    var kind = words.Contains(word) ? CodeTokenKind.Keyword
                        : next == '(' && language != CodeLanguage.Json ? CodeTokenKind.Name
                        : char.IsUpper(word[0]) && language is CodeLanguage.Script or CodeLanguage.CSharp or CodeLanguage.Swift or CodeLanguage.Python ? CodeTokenKind.Type
                        : CodeTokenKind.Plain;
                    Add(at, end, kind);
                    at = end;
                    continue;
                }
                Add(at, at + 1, CodeTokenKind.Plain);
                at++;
            }
            return runs;
        }

        /// <summary>Markdown: a heading or a fenced line as Keyword, inline code as String, the rest Plain.</summary>
        private static IReadOnlyList<CodeToken> Markdown(string line, ref CodeCarry carry)
        {
            var runs = new List<CodeToken>();
            var trimmed = line.TrimStart();
            // A fence opens or closes a block; the lines inside it are its code, kept plain, never read as Markdown.
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                carry = carry == CodeCarry.Fence ? CodeCarry.None : CodeCarry.Fence;
                runs.Add(new CodeToken(0, line.Length, CodeTokenKind.Keyword));
                return runs;
            }
            if (carry == CodeCarry.Fence)
            {
                runs.Add(new CodeToken(0, line.Length, CodeTokenKind.Plain));
                return runs;
            }
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                runs.Add(new CodeToken(0, line.Length, CodeTokenKind.Keyword));
                return runs;
            }
            var at = 0;
            while (at < line.Length)
            {
                var tick = line.IndexOf('`', at);
                var close = tick < 0 ? -1 : line.IndexOf('`', tick + 1);
                if (tick < 0 || close < 0) { runs.Add(new CodeToken(at, line.Length - at, CodeTokenKind.Plain)); break; }
                if (tick > at) runs.Add(new CodeToken(at, tick - at, CodeTokenKind.Plain));
                runs.Add(new CodeToken(tick, close + 1 - tick, CodeTokenKind.String));
                at = close + 1;
            }
            return runs;
        }

        private static char NextNonSpace(string line, int from)
        {
            while (from < line.Length && line[from] == ' ') from++;
            return from < line.Length ? line[from] : '\0';
        }

        private static bool IsWordStart(char c, CodeLanguage language) => char.IsLetter(c) || c == '_' || (c == '$' && language is CodeLanguage.Script or CodeLanguage.Shell) || (c == '@' && language == CodeLanguage.CSharp);

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
    }
}
