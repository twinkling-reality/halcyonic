#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Halcyonic.Client
{
    /// <summary>
    /// Notes the review shows beside a first task that names a web address or something to run, by
    /// fixed rules and never by a model: the person's own idea, or the companion's proposal, can carry
    /// text someone else put there, and the first task becomes the agent's instruction. A note asks the
    /// person to check; it never stops them, and its absence proves nothing: the rules catch the common
    /// forms, not every one.
    /// </summary>
    public static class TaskWarnings
    {
        public const string WebAddress = "This task names a web address: check it before you start.";
        public const string Command = "This task names something to run on " + HostText.Your + ": check it before you start.";

        private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(100);

        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        /// <summary>
        /// A scheme and host (defanged hxxp too), a www. host, an IPv4 address, localhost, or a bare
        /// host with a top-level domain followed by a path or a port, as in example.com/setup.sh.
        /// </summary>
        private static readonly Regex Address = new Regex(
            @"\b(?:h[xt]{2}ps?|ftp|wss?)://\S|\bwww\.[a-z0-9-]+\.[a-z]|\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b|\blocalhost\b"
            + @"|\b[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?(?:\.[a-z0-9-]{1,63})*\.[a-z]{2,24}(?=/|:\d)",
            Options, Limit);

        /// <summary>
        /// Something a shell would run: a command that deletes, fetches, logs in elsewhere, escalates or
        /// changes permissions, with what makes it a command rather than a word; curl, wget, ssh and scp
        /// with a flag, an address, a host, a path or a quote; an interpreter given code or a script; a script run from a path; a package
        /// manager installing or running; git sending or rewriting; text piped into a shell; a
        /// backquoted command; or command substitution.
        /// </summary>
        private static readonly Regex Shell = new Regex(
            @"\bsudo\s+\S"
            + @"|\b(?:rm|chmod|chown|rsync|dd|mkfs|nc|netcat|eval|exec|kill|pkill|launchctl|osascript|powershell)\s+(?:-|/|~|\.{1,2}/|\$|['""]|https?://|\d)"
            + @"|\b(?:curl|wget|ssh|scp)\s+(?:-|[a-z][a-z0-9+.-]*://|['""$/~.]|[\w.-]+@|[\w-]+(?:\.[\w-]+)*\.[a-z]{2,24}\b)"
            + @"|\b(?:bash|sh|zsh|fish|python3?|node|deno|bun|perl|ruby|php)\s+(?:-[ce]\b|\S+\.(?:sh|py|js|mjs|ts|rb|pl|php)\b)"
            + @"|(?:^|[\s(])\.{1,2}/[\w.-]+"
            + @"|\b(?:npm|pnpm|yarn|pip3?|brew|apt(?:-get)?|gem|cargo|go)\s+(?:install|add|i|exec|run|get|dlx|x)\b|\b(?:npx|pipx|uvx)\s+\S"
            + @"|\bgit\s+(?:push|clone|config|remote|reset\s+--hard|clean\s+-)"
            + @"|\|\s*(?:sudo\s+)?(?:sh|bash|zsh|python3?|node|perl|ruby)\b|`[^`\r\n]{1,200}`|\$\([^)\r\n]{1,200}\)",
            Options, Limit);

        /// <summary>The notes for <paramref name="firstTask"/>, in the order they show; none for most tasks.</summary>
        public static IReadOnlyList<string> Of(string? firstTask)
        {
            var notes = new List<string>();
            if (string.IsNullOrEmpty(firstTask)) return notes;
            var text = Normal(firstTask!);
            if (Matches(Address, text)) notes.Add(WebAddress);
            if (Matches(Shell, text)) notes.Add(Command);
            return notes;
        }

        /// <summary>
        /// The task as the rules read it: compatibility forms folded (full-width letters to plain ones)
        /// and invisible format characters, such as a zero-width space inside a word, taken out.
        /// </summary>
        private static string Normal(string text)
        {
            string folded;
            try
            {
                folded = text.Normalize(NormalizationForm.FormKC);
            }
            catch (Exception)
            {
                // Half a surrogate pair cannot be normalized; the review spells it as its code point. Any other
                // failure, as of a broken normalizer on the headset, leaves the text as written too.
                folded = text;
            }
            var visible = new StringBuilder(folded.Length);
            foreach (var character in folded)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.Format) visible.Append(character);
            }
            return visible.ToString();
        }

        /// <summary>A rule that runs out of time counts as matched: the person is asked to check.</summary>
        private static bool Matches(Regex rule, string text)
        {
            try
            {
                return rule.IsMatch(text);
            }
            catch (RegexMatchTimeoutException)
            {
                return true;
            }
        }
    }
}
