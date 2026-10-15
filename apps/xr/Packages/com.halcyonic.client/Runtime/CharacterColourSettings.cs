#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client
{
    public sealed class CharacterColourScope : IEquatable<CharacterColourScope>
    {
        public CharacterColourScope(string connection, string journal, string task)
        { Connection = Check(connection); Journal = Check(journal); Task = Check(task); }
        public string Connection { get; }
        public string Journal { get; }
        public string Task { get; }
        private static string Check(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl)
            ? value : throw new ArgumentException("A bounded character identity is required.");
        public bool Equals(CharacterColourScope? other) => other != null && Connection == other.Connection && Journal == other.Journal && Task == other.Task;
        public override bool Equals(object? other) => other is CharacterColourScope scope && Equals(scope);
        public override int GetHashCode() => Connection.GetHashCode() ^ Journal.GetHashCode() ^ Task.GetHashCode();
    }

    /// <summary>Only this headset's body-colour overrides. Shape, features and state colours are not preferences.</summary>
    public sealed class CharacterColourSettings
    {
        public const int Maximum = 64;
        public const int MaximumBytes = 65536;
        private readonly Dictionary<CharacterColourScope, string> applied = new Dictionary<CharacterColourScope, string>();
        private object? previewOwner;
        private CharacterColourScope? previewScope;
        private string? previewChoice;
        public event Action? Changed;
        public static readonly IReadOnlyList<string> Choices = Array.AsReadOnly(new[] { "original", "cyan", "violet", "pink" });
        public static string Name(string key) => key switch { "original" => "Original", "cyan" => "Cyan", "violet" => "Violet", "pink" => "Pink", _ => throw new ArgumentException("Unknown character colour.") };
        public static double? Hue(string key) => key switch { "original" => null, "cyan" => 197.0, "violet" => 285.0, "pink" => 329.0, _ => throw new ArgumentException("Unknown character colour.") };
        public string Applied(CharacterColourScope scope) => applied.TryGetValue(scope, out var value) ? value : "original";
        public double? DisplayedHue(CharacterColourScope scope) => Hue(previewScope?.Equals(scope) == true ? previewChoice! : Applied(scope));
        public void Preview(object owner, CharacterColourScope scope, string choice)
        { _ = Hue(choice); previewOwner = owner; previewScope = scope; previewChoice = choice; Changed?.Invoke(); }
        public void Cancel(object owner)
        { if (!ReferenceEquals(previewOwner, owner)) return; previewOwner = null; previewScope = null; previewChoice = null; Changed?.Invoke(); }
        public bool Apply(object owner, CharacterColourScope scope, string choice, Func<string, bool> persist)
        {
            if (!ReferenceEquals(previewOwner, owner) || previewScope?.Equals(scope) != true || previewChoice != choice) return false;
            var next = new Dictionary<CharacterColourScope, string>(applied);
            if (choice == "original") next.Remove(scope); else next[scope] = choice;
            var encoded = Encode(next);
            if (next.Count > Maximum || System.Text.Encoding.UTF8.GetByteCount(encoded) > MaximumBytes || !persist(encoded)) return false;
            applied.Clear(); foreach (var pair in next) applied.Add(pair.Key, pair.Value);
            Cancel(owner); return true;
        }
        public bool Prune(string connection, string journal, IReadOnlyCollection<string> existing, Func<string, bool> persist)
        {
            var next = applied.Where(pair => pair.Key.Connection != connection || pair.Key.Journal != journal || existing.Contains(pair.Key.Task))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            if (next.Count == applied.Count || !persist(Encode(next))) return false;
            applied.Clear(); foreach (var pair in next) applied.Add(pair.Key, pair.Value);
            Changed?.Invoke(); return true;
        }
        private static string Encode(Dictionary<CharacterColourScope, string> entries) => new JObject(
            new JProperty("version", 1), new JProperty("colours", new JArray(entries.OrderBy(pair => pair.Key.Connection).ThenBy(pair => pair.Key.Journal).ThenBy(pair => pair.Key.Task)
                .Select(pair => new JObject(new JProperty("connection", pair.Key.Connection), new JProperty("journal", pair.Key.Journal), new JProperty("task", pair.Key.Task), new JProperty("colour", pair.Value)))))).ToString(Formatting.None);
        public string Save() => Encode(applied);
        public static CharacterColourSettings Load(string? saved)
        {
            var result = new CharacterColourSettings();
            if (string.IsNullOrEmpty(saved)) return result;
            try
            {
                if (System.Text.Encoding.UTF8.GetByteCount(saved) > MaximumBytes) return result;
                var root = JObject.Parse(saved, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (root.Properties().Count() != 2 || root["version"]?.Type != JTokenType.Integer || root.Value<int>("version") != 1 || root["colours"] is not JArray entries || entries.Count > Maximum) return result;
                foreach (var token in entries)
                {
                    if (token is not JObject entry || entry.Properties().Count() != 4 || new[] { "connection", "journal", "task", "colour" }.Any(key => entry[key]?.Type != JTokenType.String)) return new CharacterColourSettings();
                    var scope = new CharacterColourScope(entry.Value<string>("connection")!, entry.Value<string>("journal")!, entry.Value<string>("task")!);
                    var choice = entry.Value<string>("colour")!;
                    if (choice == "original" || !Choices.Contains(choice) || result.applied.ContainsKey(scope)) return new CharacterColourSettings();
                    result.applied.Add(scope, choice);
                }
                return result;
            }
            catch (Exception) { return new CharacterColourSettings(); }
        }
    }
}
