#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// Which projects' work the stage shows: every project, the default, or the projects the person
    /// chose. Kept on this device for each journal, since project ids mean nothing in another one; a
    /// journal seen for the first time shows every project.
    /// </summary>
    /// <remarks>
    /// A presentation choice, never journaled and never an authorization boundary: every authenticated
    /// client still receives all work. Hiding a project hides its characters only; its work, and what
    /// needs the person in it, stays counted in the project rail and listed in More work
    /// (<see cref="WorkOverview"/>). Once the person has chosen, a project created elsewhere later is
    /// hidden until they show it; one created from the headset is shown by the entry panel.
    /// </remarks>
    public sealed class StageVisibility
    {
        /// <summary>How many journals' choices are remembered, the most recently used kept.</summary>
        public const int Journals = 8;

        private const int SavedVersion = 1;

        /// <summary>The remembered choices, the most recently used first; a null set is every project.</summary>
        private readonly List<(string Journal, HashSet<string>? Chosen)> remembered = new List<(string, HashSet<string>?)>();

        private string? journal;
        private HashSet<string>? chosen;

        /// <summary>The journal the choice applies to, or null before a state has been seen.</summary>
        public string? JournalId => journal;

        /// <summary>Every project shows, including ones that appear later.</summary>
        public bool All => chosen == null;

        /// <summary>Changes whenever what the stage shows may have changed.</summary>
        public int Version { get; private set; }

        /// <summary>Whether a project's work stands on the stage.</summary>
        public bool Shows(string projectId) => chosen == null || chosen.Contains(projectId);

        /// <summary>
        /// Applies the choice remembered for <paramref name="journalId"/>, or every project for a
        /// journal not seen before. Returns whether anything changed.
        /// </summary>
        public bool UseJournal(string? journalId)
        {
            if (journalId == journal) return false;
            journal = journalId;
            var found = remembered.FindIndex(entry => entry.Journal == journalId);
            chosen = found < 0 || remembered[found].Chosen == null ? null : new HashSet<string>(remembered[found].Chosen!, StringComparer.Ordinal);
            Version++;
            return true;
        }

        /// <summary>Shows every project, including ones that appear later.</summary>
        public void ShowAll()
        {
            if (chosen == null) return;
            chosen = null;
            Changed();
        }

        /// <summary>Shows a project's work. Does nothing while every project shows.</summary>
        public void Show(string projectId)
        {
            if (chosen == null || !chosen.Add(projectId)) return;
            Changed();
        }

        /// <summary>
        /// Hides a project's work. While every project shows, the choice becomes every other project in
        /// <paramref name="known"/>, the projects the state holds now.
        /// </summary>
        public void Hide(string projectId, IEnumerable<string> known)
        {
            if (chosen == null)
            {
                chosen = new HashSet<string>(known, StringComparer.Ordinal);
                chosen.Remove(projectId);
            }
            else if (!chosen.Remove(projectId)) return;
            Changed();
        }

        /// <summary>Shows a hidden project or hides a shown one.</summary>
        public void Toggle(string projectId, IEnumerable<string> known)
        {
            if (Shows(projectId)) Hide(projectId, known);
            else Show(projectId);
        }

        /// <summary>The remembered choices as text, for a device preference.</summary>
        public string Save()
        {
            var journals = new JArray();
            foreach (var (id, projects) in remembered)
            {
                journals.Add(new JObject
                {
                    ["journal"] = id,
                    ["projects"] = projects == null ? JValue.CreateNull() : new JArray(projects.OrderBy(value => value, StringComparer.Ordinal)),
                });
            }
            return new JObject { ["version"] = SavedVersion, ["journals"] = journals }.ToString(Formatting.None);
        }

        /// <summary>
        /// Reads what <see cref="Save"/> wrote. Anything unreadable is forgotten, so every project
        /// shows: a presentation preference never keeps work off the stage because it was damaged.
        /// </summary>
        public static StageVisibility Load(string? saved)
        {
            var visibility = new StageVisibility();
            if (string.IsNullOrWhiteSpace(saved)) return visibility;
            try
            {
                var root = JObject.Parse(saved!);
                if (root.Value<int?>("version") != SavedVersion || root["journals"] is not JArray journals) return visibility;
                foreach (var item in journals.OfType<JObject>())
                {
                    if (item["journal"] is not JValue { Type: JTokenType.String } journalValue) continue;
                    var id = (string)journalValue!;
                    if (visibility.remembered.Any(entry => entry.Journal == id)) continue;
                    HashSet<string>? projects = null;
                    if (item["projects"] is JArray list)
                    {
                        projects = new HashSet<string>(list.Where(value => value.Type == JTokenType.String).Select(value => (string)value!), StringComparer.Ordinal);
                    }
                    else if (item["projects"]?.Type != JTokenType.Null) continue;
                    visibility.remembered.Add((id, projects));
                    if (visibility.remembered.Count == Journals) break;
                }
            }
            catch (JsonException)
            {
                visibility.remembered.Clear();
            }
            return visibility;
        }

        private void Changed()
        {
            Version++;
            if (journal == null) return;
            remembered.RemoveAll(entry => entry.Journal == journal);
            remembered.Insert(0, (journal, chosen == null ? null : new HashSet<string>(chosen, StringComparer.Ordinal)));
            if (remembered.Count > Journals) remembered.RemoveRange(Journals, remembered.Count - Journals);
        }
    }
}
