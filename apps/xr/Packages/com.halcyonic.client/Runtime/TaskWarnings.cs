#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Halcyonic.Client
{
    /// <summary>
    /// Notes the review shows beside a first task that names a web address or a command to run, by
    /// fixed rules and never by a model: the person's own idea, or the companion's proposal, can carry
    /// text someone else put there, and the first task becomes the agent's instruction. A note asks the
    /// person to check; it never stops them, and its absence proves nothing.
    /// </summary>
    public static class TaskWarnings
    {
        public const string WebAddress = "This task names a web address: check it before you start.";
        public const string Command = "This task names a command to run: check it before you start.";

        private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(100);

        /// <summary>A scheme and host, a www. host, or an IPv4 address.</summary>
        private static readonly Regex Address = new Regex(
            @"\b(?:https?|ftp|wss?)://\S|\bwww\.[A-Za-z0-9-]+\.[A-Za-z]|\b\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limit);

        /// <summary>
        /// A command that deletes, fetches, escalates or runs another shell, followed by what makes it a
        /// command rather than a word (a flag, a path, a variable, a quote, a number or a web address);
        /// sudo with anything; text piped into a shell; a backquoted command; or command substitution.
        /// </summary>
        private static readonly Regex Shell = new Regex(
            @"\bsudo\s+\S"
            + @"|\b(?:rm|curl|wget|chmod|chown|ssh|scp|rsync|dd|mkfs|nc|netcat|eval|exec|kill|pkill|launchctl|osascript|powershell)\s+(?:-|/|~|\.{1,2}/|\$|['""]|https?://|\d)"
            + @"|\|\s*(?:sudo\s+)?(?:sh|bash|zsh|python3?|node|perl|ruby)\b|`[^`\r\n]{1,200}`|\$\([^)\r\n]{1,200}\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limit);

        /// <summary>The notes for <paramref name="firstTask"/>, in the order they show; none for most tasks.</summary>
        public static IReadOnlyList<string> Of(string? firstTask)
        {
            var notes = new List<string>();
            if (string.IsNullOrEmpty(firstTask)) return notes;
            if (Matches(Address, firstTask!)) notes.Add(WebAddress);
            if (Matches(Shell, firstTask!)) notes.Add(Command);
            return notes;
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
