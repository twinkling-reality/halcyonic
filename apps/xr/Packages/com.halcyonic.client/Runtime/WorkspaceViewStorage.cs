#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Halcyonic.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client
{
    /// <summary>Bounded local view storage. Parsed records remain untrusted recovery data.</summary>
    public static class WorkspaceViewStorage
    {
        public const int MaximumBytes = 4 * 1024 * 1024;

        public static bool TryParse(string? json, out WorkspaceViewStore? store)
        {
            store = null;
            if (json == null || json.Length > MaximumBytes || Encoding.UTF8.GetByteCount(json) > MaximumBytes
                || !WorkspaceContinuity.HasStorageSyntax(json)) return false;
            try
            {
                using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 8 };
                var token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read() || !(token is JObject value) || value.Count != 3
                    || value["version"]?.Type != JTokenType.Integer || (long)value["version"]! != 1
                    || !(value["views"] is JArray views)
                    || !(value["selected"]?.Type == JTokenType.Null || value["selected"]?.Type == JTokenType.String)) return false;
                var parsed = new WorkspaceViewStore { Selected = (string?)value["selected"] };
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var view in views)
                {
                    if (!WorkspaceContinuity.TryParse(view.ToString(Formatting.None), out var continuation)
                        || continuation == null || !ids.Add(continuation.HandoffId)) return false;
                    parsed.Views.Add(continuation);
                }
                if (parsed.Selected != null && !ids.Contains(parsed.Selected)) return false;
                store = parsed;
                return true;
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException || error is OverflowException)
            {
                return false;
            }
        }

        /// <summary>Never edits or evicts a caller's records; failure produces no replacement file content.</summary>
        public static bool TrySerialize(WorkspaceViewStore store, out string? json)
        {
            json = null;
            if (store == null) return false;
            try
            {
                using var text = new BoundedWriter();
                using (var writer = new JsonTextWriter(text))
                    JsonSerializer.Create(HalcyonicJson.Strict).Serialize(writer, store);
                var candidate = text.ToString();
                if (!TryParse(candidate, out _)) return false;
                json = candidate;
                return true;
            }
            catch (Exception error) when (error is JsonException || error is ArgumentException
                || error is OverflowException || error is StoreTooLargeException)
            {
                return false;
            }
        }

        // Bound allocation while serializing an in-memory model, before the exact UTF-8/schema check.
        // ASCII is the smallest encoding, so more than MaximumBytes UTF-16 units can never fit.
        private sealed class BoundedWriter : StringWriter
        {
            public BoundedWriter() : base(CultureInfo.InvariantCulture) { }
            private void Check(int additional)
            {
                if (additional > MaximumBytes - GetStringBuilder().Length) throw new StoreTooLargeException();
            }
            public override void Write(char value) { Check(1); base.Write(value); }
            public override void Write(string? value) { Check(value?.Length ?? 0); base.Write(value); }
            public override void Write(char[] buffer, int index, int count) { Check(count); base.Write(buffer, index, count); }
        }
        private sealed class StoreTooLargeException : Exception { }
    }
}
