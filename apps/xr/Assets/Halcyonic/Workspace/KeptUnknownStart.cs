#nullable enable
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The id of a start whose outcome is unknown, kept on this device for the computer it was sent to
    /// (<see cref="ControlPlaneSettings.Computer"/>), so a restart still blocks a blind retry there and
    /// another computer's start never shows here. Read afresh at every use, so New project and the entry
    /// panel each see what the other kept or cleared at once; with no computer configured, as in the
    /// demonstration, none is kept.
    /// </summary>
    public sealed class KeptUnknownStart : IKeptCommand
    {
        /// <summary>Where the entry panel kept one for every computer alike, before each computer had its own.</summary>
        private const string Preference = "halcyonic.new-work.unresolved-command-id";

        /// <summary>A start kept before each computer had its own was moved to the first computer read, once a run.</summary>
        private static bool earlierMoved;

        public string? Id
        {
            get
            {
                if (!(Key() is string key)) return null;
                MoveEarlier(key);
                var id = PlayerPrefs.GetString(key, "");
                return id.Length == 0 ? null : id;
            }
            set
            {
                if (!(Key() is string key)) return;
                if (value == null) PlayerPrefs.DeleteKey(key);
                else PlayerPrefs.SetString(key, value);
                PlayerPrefs.Save();
            }
        }

        private static string? Key() => ControlPlaneSettings.Computer() is string computer ? Preference + "." + computer : null;

        /// <summary>
        /// One kept for every computer alike, by a build before this one, goes to the computer this
        /// device reaches now, as that build would have shown it, unless that computer keeps its own.
        /// </summary>
        private static void MoveEarlier(string key)
        {
            if (earlierMoved) return;
            earlierMoved = true;
            if (!PlayerPrefs.HasKey(Preference)) return;
            var earlier = PlayerPrefs.GetString(Preference, "");
            if (earlier.Length > 0 && PlayerPrefs.GetString(key, "").Length == 0) PlayerPrefs.SetString(key, earlier);
            PlayerPrefs.DeleteKey(Preference);
            PlayerPrefs.Save();
        }
    }
}
