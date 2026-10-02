#nullable enable
using System;
using System.Collections.Generic;
using System.IO;

namespace Halcyonic.Client
{
    /// <summary>What <see cref="AccessTokenFile.Migrate"/> did with a token left in shared storage.</summary>
    public enum AccessTokenMigration
    {
        /// <summary>There was none.</summary>
        NothingToMove,

        /// <summary>It was moved into app-private storage and removed from shared storage.</summary>
        Moved,

        /// <summary>App-private storage already held a token, so the copy in shared storage was removed.</summary>
        RemovedStaleCopy,

        /// <summary>It was empty, and was removed.</summary>
        RemovedEmptyCopy,

        /// <summary>The private file could not be made private, so the token was left where it was.</summary>
        NotMoved,
    }

    /// <summary>
    /// The access token a development build reads to reach the computer over USB. It lives in the
    /// app's private storage, which no other app can read; earlier builds read it from the app's
    /// directory on shared storage, so a token found there is moved in once and removed
    /// (docs/internal/architecture/SECURITY.md).
    /// </summary>
    public static class AccessTokenFile
    {
        /// <summary>
        /// Moves a token from <paramref name="legacyPath"/> into <paramref name="privatePath"/>, unless
        /// a token is already there, and removes the old copy. The new file is made private by
        /// <paramref name="restrict"/> while it is still empty, before the token is written into it,
        /// and replaces nothing until it is complete. If <paramref name="restrict"/> throws, nothing is
        /// written and the old copy stays.
        /// </summary>
        public static AccessTokenMigration Migrate(string legacyPath, string privatePath, Action<string> restrict)
        {
            if (!File.Exists(legacyPath)) return AccessTokenMigration.NothingToMove;
            if (File.Exists(privatePath))
            {
                File.Delete(legacyPath);
                return AccessTokenMigration.RemovedStaleCopy;
            }
            var token = File.ReadAllText(legacyPath).Trim();
            if (token.Length == 0)
            {
                File.Delete(legacyPath);
                return AccessTokenMigration.RemovedEmptyCopy;
            }
            var directory = Path.GetDirectoryName(Path.GetFullPath(privatePath));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = privatePath + ".new";
            File.WriteAllText(temporary, string.Empty);
            try
            {
                restrict(temporary);
            }
            catch
            {
                File.Delete(temporary);
                return AccessTokenMigration.NotMoved;
            }
            File.WriteAllText(temporary, token + "\n");
            File.Move(temporary, privatePath);
            File.Delete(legacyPath);
            return AccessTokenMigration.Moved;
        }

        /// <summary>The first non-empty token in <paramref name="paths"/>, or null when none holds one.</summary>
        public static string? Read(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var token = File.ReadAllText(path).Trim();
                if (token.Length > 0) return token;
            }
            return null;
        }
    }
}
