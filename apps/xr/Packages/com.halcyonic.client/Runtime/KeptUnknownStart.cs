#nullable enable
using System;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>Strings this device keeps across a restart; in Unity, its PlayerPrefs.</summary>
    public interface IDevicePreferences
    {
        /// <summary>The string kept under <paramref name="key"/>, or null when none is.</summary>
        string? Get(string key);

        /// <summary>Keeps <paramref name="value"/> under <paramref name="key"/> at once; null removes it.</summary>
        void Set(string key, string? value);
    }

    /// <summary>
    /// The id of a start whose outcome is unknown, kept on this device for the journal it was sent to,
    /// as drafts are, so a restart still blocks a blind retry there and another journal's start never
    /// shows here, whether the computer is reached over USB or paired. Read afresh at every use, so New
    /// project and the entry panel each see what the other kept or cleared at once; with no live
    /// journal yet, as before the first snapshot or in the demonstration, none is read or kept.
    /// </summary>
    public sealed class KeptUnknownStart : IKeptCommand
    {
        /// <summary>Where the entry panel kept one for every journal alike, before each journal had its own; each journal's is under it.</summary>
        public const string Preference = "halcyonic.new-work.unresolved-command-id";

        private readonly IDevicePreferences preferences;
        private readonly Func<ClientProjection?> state;
        private bool earlierMoved;

        /// <param name="state">The session's projection, whose live journal the id is kept for.</param>
        public KeptUnknownStart(IDevicePreferences preferences, Func<ClientProjection?> state)
        {
            this.preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public string? Id
        {
            get
            {
                if (!(Key() is string key)) return null;
                MoveEarlier(key);
                var id = preferences.Get(key);
                return string.IsNullOrEmpty(id) ? null : id;
            }
            set
            {
                if (Key() is string key) preferences.Set(key, string.IsNullOrEmpty(value) ? null : value);
            }
        }

        private string? Key() => state()?.Journal is JournalInfo journal && journal.Origin == JournalOrigin.Live ? Preference + "." + journal.JournalId : null;

        /// <summary>
        /// One kept for every journal alike, by a build before this one, goes to the first journal read,
        /// as that build would have shown it there, unless that journal keeps its own.
        /// </summary>
        private void MoveEarlier(string key)
        {
            if (earlierMoved) return;
            earlierMoved = true;
            var earlier = preferences.Get(Preference);
            if (earlier == null) return;
            if (earlier.Length > 0 && string.IsNullOrEmpty(preferences.Get(key))) preferences.Set(key, earlier);
            preferences.Set(Preference, null);
        }
    }
}
