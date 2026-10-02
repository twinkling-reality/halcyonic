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

        /// <summary>It was moved into app-private storage, made readable by this app alone, and removed from shared storage.</summary>
        Moved,

        /// <summary>
        /// It was moved and removed from shared storage, but the new file could not be restricted
        /// further than the private storage it is in.
        /// </summary>
        MovedUnrestricted,

        /// <summary>App-private storage already held a token, so the copy in shared storage was removed.</summary>
        RemovedStaleCopy,

        /// <summary>It was empty, or too large to be a token, and was removed.</summary>
        RemovedUnusableCopy,
    }

    /// <summary>
    /// The access token a development build reads to reach the computer over USB. It lives in the
    /// app's private storage, which no other app can read; earlier builds read it from the app's
    /// directory on shared storage, so a token found there is moved in once and removed
    /// (docs/internal/architecture/SECURITY.md).
    /// </summary>
    public static class AccessTokenFile
    {
        /// <summary>The largest file taken for a token: one is 43 characters.</summary>
        public const long MaxBytes = 1024;

        /// <summary>
        /// Moves a token from <paramref name="legacyPath"/> into <paramref name="privatePath"/>, unless
        /// a token is already there, and removes the old copy. The new file is made private by
        /// <paramref name="restrict"/> while it is still empty, before the token is written into it,
        /// and replaces nothing until it is complete; a file left half made is removed. If
        /// <paramref name="restrict"/> throws, the token still leaves shared storage, for private
        /// storage is closed to other apps by itself. The two paths may name one file, as through a
        /// link: the token is never lost.
        /// </summary>
        public static AccessTokenMigration Migrate(string legacyPath, string privatePath, Action<string> restrict)
        {
            if (string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(privatePath), StringComparison.Ordinal))
            {
                return AccessTokenMigration.NothingToMove;
            }
            if (!File.Exists(legacyPath)) return AccessTokenMigration.NothingToMove;
            if (File.Exists(privatePath))
            {
                var kept = File.ReadAllText(privatePath);
                File.Delete(legacyPath);
                // The same file under two names: put back what deleting the old name removed.
                if (!File.Exists(privatePath)) Write(privatePath, kept, restrict);
                return AccessTokenMigration.RemovedStaleCopy;
            }
            var token = new FileInfo(legacyPath).Length > MaxBytes ? string.Empty : File.ReadAllText(legacyPath).Trim();
            if (token.Length == 0)
            {
                File.Delete(legacyPath);
                return AccessTokenMigration.RemovedUnusableCopy;
            }
            var restricted = Write(privatePath, token + "\n", restrict);
            File.Delete(legacyPath);
            return restricted ? AccessTokenMigration.Moved : AccessTokenMigration.MovedUnrestricted;
        }

        /// <summary>
        /// Writes <paramref name="text"/> to <paramref name="path"/> through a new file restricted while
        /// still empty; returns whether it could be restricted. Removes the new file if it is not moved
        /// into place.
        /// </summary>
        private static bool Write(string path, string text, Action<string> restrict)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var temporary = path + ".new";
            var moved = false;
            try
            {
                File.WriteAllText(temporary, string.Empty);
                var restricted = true;
                try
                {
                    restrict(temporary);
                }
                catch
                {
                    restricted = false;
                }
                File.WriteAllText(temporary, text);
                File.Move(temporary, path);
                moved = true;
                return restricted;
            }
            finally
            {
                if (!moved && File.Exists(temporary)) File.Delete(temporary);
            }
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
