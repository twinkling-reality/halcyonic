#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client
{
    /// <summary>Untrusted local view/draft recovery. Never grants permission to execute an action.</summary>
    public static class WorkspaceContinuity
    {
        public const int MaximumBytes = 256 * 1024;
        private static readonly Regex Uuid = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\\z", RegexOptions.CultureInvariant);
        private static readonly Regex EntityId = new Regex("^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\\z", RegexOptions.CultureInvariant);
        private static readonly Regex Digest = new Regex("^[0-9a-f]{64}\\z", RegexOptions.CultureInvariant);

        /// <summary>
        /// Opaque private identity, including credential changes. Do not log it or send it to the host.
        /// Both surfaces hash the same compact JSON array; host normalization does not resolve DNS.
        /// </summary>
        public static string ConnectionKey(ControlPlaneTarget target)
        {
            var endpoint = target.Endpoint;
            var paired = target.Pairing;
            var scheme = paired == null ? "ws" : "wss";
            if (endpoint.Scheme != scheme || endpoint.AbsolutePath != "/realtime" || endpoint.Query.Length != 0
                || endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0
                || (paired == null && endpoint.Host != "127.0.0.1"))
                throw new ArgumentException("This endpoint cannot share workspace continuity.", nameof(target));
            var host = paired?.Host ?? endpoint.Host;
            if (host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal))
                host = host.Substring(1, host.Length - 2);
            if (host.Length == 0 || host.Length > 253 || host.Any(c => c <= 32 || c >= 127 || "/\\?#@%[]".IndexOf(c) >= 0))
                throw new ArgumentException("This host cannot share workspace continuity.", nameof(target));
            if (paired != null && (!EntityId.IsMatch(paired.DeviceId)
                || !Regex.IsMatch(paired.Credential, "^hlcd_[A-Za-z0-9_-]{1,4096}\\z", RegexOptions.CultureInvariant)
                || !Digest.IsMatch(paired.CertificateSha256)))
                throw new ArgumentException("This pairing cannot share workspace continuity.", nameof(target));
            var material = JsonConvert.SerializeObject(new object?[]
            {
                "halcyonic-workspace-v1", scheme, host.ToLowerInvariant(), paired?.Port ?? endpoint.Port,
                paired?.DeviceId, paired?.CertificateSha256, target.Credential,
            }, Formatting.None);
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(material))).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>Strict, bounded parsing; serializer coercions and duplicate keys are refused.</summary>
        public static bool TryParse(string? json, out WorkspaceContinuation? continuation)
        {
            continuation = null;
            if (json == null || json.Length > MaximumBytes || Encoding.UTF8.GetByteCount(json) > MaximumBytes
                || !HasStorageSyntax(json)) return false;
            try
            {
                using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 8 };
                var token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read() || !(token is JObject value)) return false;
                // Earlier v1 records omit request. Normalize only that known optional field.
                if (value.Property("request", StringComparison.Ordinal) == null) value["request"] = JValue.CreateNull();
                if (!Keys(value, "version", "handoff_id", "connection_key", "journal_id", "project_id", "workstream_id", "execution_id", "section", "page", "instruction", "request")
                    || !Integer(value["version"], 1, 1) || !TextMatches(value["handoff_id"], Uuid)
                    || !TextMatches(value["connection_key"], Digest) || !Integer(value["page"], 0, 100000)) return false;
                foreach (var key in new[] { "journal_id", "project_id", "workstream_id", "execution_id" })
                    if (value[key]!.Type != JTokenType.Null && !TextMatches(value[key], EntityId)) return false;
                if (value["section"]!.Type != JTokenType.String
                    || !new[] { "waiting", "activity", "changes", "checks" }.Contains((string?)value["section"])) return false;
                var instruction = value["instruction"]!;
                if (instruction.Type != JTokenType.Null)
                {
                    if (!(instruction is JObject draft) || !Keys(draft, "text", "spoken", "part")
                        || draft["text"]!.Type != JTokenType.String || ((string)draft["text"]!).Length > 32000
                        || draft["spoken"]!.Type != JTokenType.Boolean || !Integer(draft["part"], 0, 100000)) return false;
                }
                var request = value["request"]!;
                if (request.Type != JTokenType.Null)
                {
                    if (!(request is JObject draft) || !Keys(draft, "request_id", "text", "spoken", "part")
                        || draft["request_id"]!.Type != JTokenType.Null && !TextMatches(draft["request_id"], EntityId)
                        || draft["text"]!.Type != JTokenType.String || ((string)draft["text"]!).Length > 32000
                        || draft["spoken"]!.Type != JTokenType.Boolean || !Integer(draft["part"], 0, 100000)) return false;
                }
                continuation = value.ToObject<WorkspaceContinuation>(JsonSerializer.Create(HalcyonicJson.Strict));
                return continuation != null;
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is OverflowException)
            {
                return false;
            }
        }

        /// <summary>
        /// Validate against the current authoritative snapshot after reconnect. A draft belongs to
        /// its exact current execution; it can never be retargeted when another execution starts.
        /// This verifies context only. Capability, read and confirmation checks still apply on send.
        /// </summary>
        public static bool MatchesCurrent(WorkspaceContinuation continuation, string connectionKey, ClientProjection state)
        {
            if (!TryParse(HalcyonicJson.Serialize(continuation), out _) || continuation.ConnectionKey != connectionKey
                || state.Journal == null || continuation.JournalId != state.Journal.JournalId) return false;
            if (continuation.ProjectId == null)
                return continuation.WorkstreamId == null && continuation.ExecutionId == null && continuation.Instruction == null && continuation.Request == null;
            if (!state.Projects.ContainsKey(continuation.ProjectId)) return false;
            if (continuation.WorkstreamId == null) return continuation.ExecutionId == null && continuation.Instruction == null && continuation.Request == null;
            if (!state.Workstreams.TryGetValue(continuation.WorkstreamId, out var work)
                || work.ProjectId != continuation.ProjectId) return false;
            if (continuation.ExecutionId == null) return continuation.Instruction == null && continuation.Request == null;
            return work.CurrentExecutionId == continuation.ExecutionId
                && state.Executions.TryGetValue(continuation.ExecutionId, out var execution)
                && execution.WorkstreamId == work.WorkstreamId && execution.ProjectId == continuation.ProjectId;
        }

        private static bool Keys(JObject value, params string[] expected) =>
            value.Count == expected.Length && expected.All(key => value.Property(key, StringComparison.Ordinal) != null);
        private static bool TextMatches(JToken? token, Regex pattern) => token?.Type == JTokenType.String && token.Value<string>() is string text && pattern.IsMatch(text);
        private static bool Integer(JToken? token, long minimum, long maximum) =>
            token?.Type == JTokenType.Integer && (long)token >= minimum && (long)token <= maximum;

        /// <summary>Syntax guard for bounded local recovery records; caller enforces bytes and schema.</summary>
        internal static bool HasStorageSyntax(string json) => new ContinuationSyntax(json).Valid();

        // Json.NET also accepts JavaScript syntax. The native handoff reader does not: check the
        // JSON value forms this contract uses before Json.NET checks keys, duplicates and types.
        private sealed class ContinuationSyntax
        {
            private readonly string source;
            private int at;
            public ContinuationSyntax(string source) => this.source = source;
            public bool Valid() { var valid = Value(0); Space(); return valid && at == source.Length; }
            private bool Value(int depth)
            {
                Space();
                if (depth > 8 || at == source.Length) return false;
                if (source[at] == '"') return Text();
                if (Take('['))
                {
                    Space();
                    if (Take(']')) return true;
                    do
                    {
                        if (!Value(depth + 1)) return false;
                        Space();
                        if (Take(']')) return true;
                    } while (Take(','));
                    return false;
                }
                if (Take('{'))
                {
                    Space();
                    if (Take('}')) return true;
                    do
                    {
                        Space();
                        if (!Text()) return false;
                        Space();
                        if (!Take(':') || !Value(depth + 1)) return false;
                        Space();
                        if (Take('}')) return true;
                    } while (Take(','));
                    return false;
                }
                foreach (var literal in new[] { "true", "false", "null" })
                    if (source.Length - at >= literal.Length
                        && string.CompareOrdinal(source, at, literal, 0, literal.Length) == 0)
                    { at += literal.Length; return true; }
                Take('-');
                if (Take('0')) return true;
                if (at == source.Length || source[at] < '1' || source[at] > '9') return false;
                while (at < source.Length && source[at] >= '0' && source[at] <= '9') at++;
                return true;
            }
            private bool Text()
            {
                if (!Take('"')) return false;
                while (at < source.Length)
                {
                    var c = source[at++];
                    if (c == '"') return true;
                    if (c < 32) return false;
                    if (c != '\\') continue;
                    if (at == source.Length) return false;
                    var escaped = source[at++];
                    if ("\"\\/bfnrt".IndexOf(escaped) >= 0) continue;
                    if (escaped != 'u' || source.Length - at < 4) return false;
                    for (var i = 0; i < 4; i++)
                    {
                        var hex = source[at++];
                        if (!(hex >= '0' && hex <= '9' || hex >= 'a' && hex <= 'f' || hex >= 'A' && hex <= 'F')) return false;
                    }
                }
                return false;
            }
            private bool Take(char c) { if (at >= source.Length || source[at] != c) return false; at++; return true; }
            private void Space() { while (at < source.Length && " \r\n\t".IndexOf(source[at]) >= 0) at++; }
        }
    }
}
