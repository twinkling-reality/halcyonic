#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Halcyonic.Client
{
    /// <summary>What a path names, looked at without following a link.</summary>
    public enum StoredEntry
    {
        /// <summary>Nothing, or nothing that can be looked at.</summary>
        Missing,

        /// <summary>A regular file with a single name.</summary>
        File,

        /// <summary>Anything else: a link, a pipe, a folder, or a file with several names.</summary>
        Other,
    }

    /// <summary>
    /// How the token's files are reached on a platform. Android's goes through the system's own
    /// calls, so nothing follows a link or waits on a pipe; <see cref="ManagedTokenStorage"/> does
    /// what .NET Standard allows, for the editor and tests.
    /// </summary>
    public interface ITokenStorage
    {
        /// <summary>What <paramref name="path"/> names, without following a link.</summary>
        StoredEntry Examine(string path);

        /// <summary>Whether <paramref name="path"/> is a real folder, not a link to one.</summary>
        bool IsPlainFolder(string path);

        /// <summary>
        /// The bytes of the file at <paramref name="path"/>, at most <paramref name="max"/>, read
        /// without following a link or waiting on a pipe and only while it is one regular file with a
        /// single name; null otherwise.
        /// </summary>
        byte[]? ReadFile(string path, int max);

        /// <summary>Makes the file readable and writable by this app alone; throws when it can't.</summary>
        void Restrict(string path);

        /// <summary>Removes the name at <paramref name="path"/>, never what a link points to; false when it can't.</summary>
        bool TryRemove(string path);
    }

    /// <summary>What <see cref="AccessTokenFile.Migrate"/> or <see cref="AccessTokenFile.Discard"/> did.</summary>
    public enum AccessTokenMigration
    {
        /// <summary>There was no token on shared storage.</summary>
        NothingThere,

        /// <summary>It was moved into app-private storage.</summary>
        Moved,

        /// <summary>App-private storage already held a token, which is kept.</summary>
        KeptPrivateToken,

        /// <summary>
        /// What was there was not a token the app takes: empty, too large, not in the token's form,
        /// a link, a pipe or a file with several names. It was never followed or read as a token.
        /// </summary>
        Unusable,

        /// <summary>The folder it is in is not a real folder, so nothing in it was read or removed.</summary>
        LeftInLinkedFolder,

        /// <summary>A release build found something there and did not read it.</summary>
        Discarded,
    }

    /// <summary>What happened to a token found on shared storage, and whether a copy is still there.</summary>
    public readonly struct AccessTokenMove
    {
        public AccessTokenMove(AccessTokenMigration outcome, bool restricted, bool sharedCopyRemains)
        {
            Outcome = outcome;
            Restricted = restricted;
            SharedCopyRemains = sharedCopyRemains;
        }

        public AccessTokenMigration Outcome { get; }

        /// <summary>False when a moved token's file could not be restricted further than private storage itself.</summary>
        public bool Restricted { get; }

        /// <summary>True when something is still at the old place that the app could not remove.</summary>
        public bool SharedCopyRemains { get; }
    }

    /// <summary>
    /// The access token a development build reads to reach the computer over USB. It lives in the
    /// app's private storage, which no other app can read; earlier builds read it from the app's
    /// directory on shared storage, so a development build moves a token found there in once and
    /// removes it, and a release build removes it unread (docs/internal/architecture/SECURITY.md).
    /// </summary>
    public static class AccessTokenFile
    {
        /// <summary>The largest file looked at: a token is 43 characters and a newline.</summary>
        public const int MaxBytes = 64;

        /// <summary>The token's own form: 32 random bytes in base64url, as the control plane makes it.</summary>
        private static readonly Regex TokenForm = new Regex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant);

        /// <summary>Whether <paramref name="text"/>, trimmed, is a token in its own form.</summary>
        public static bool IsToken(string text) => TokenForm.IsMatch(text.Trim());

        /// <summary>
        /// Moves a token from <paramref name="legacyPath"/> into <paramref name="privatePath"/> unless
        /// a token is already there, then removes the old name. Only a regular file with a single name,
        /// at most <see cref="MaxBytes"/>, holding a token in its own form is taken; anything else is
        /// removed without being followed or read as a token, and nothing in a folder that is a link is
        /// touched. The new file is restricted while still empty, before the token is written, and
        /// replaces nothing until it is complete; a file left half made is removed. If restricting
        /// fails, the token still leaves shared storage, for private storage keeps other apps out by
        /// itself. The two paths may name one file: the token is put back after the old name goes,
        /// unless the app is killed in the moment between the two. An old name that can't be removed
        /// is reported, never thrown.
        /// </summary>
        public static AccessTokenMove Migrate(string legacyPath, string privatePath, ITokenStorage storage)
        {
            if (string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(privatePath), StringComparison.Ordinal))
            {
                return new AccessTokenMove(AccessTokenMigration.NothingThere, true, false);
            }
            var folder = Path.GetDirectoryName(Path.GetFullPath(legacyPath));
            var entry = storage.Examine(legacyPath);
            if (entry == StoredEntry.Missing) return new AccessTokenMove(AccessTokenMigration.NothingThere, true, false);
            if (folder != null && !storage.IsPlainFolder(folder))
            {
                return new AccessTokenMove(AccessTokenMigration.LeftInLinkedFolder, true, true);
            }
            var kept = PrivateToken(privatePath);
            if (kept != null)
            {
                var removed = storage.TryRemove(legacyPath);
                // The same file under two names: put back what removing the old name took away.
                if (removed && PrivateToken(privatePath) == null) Write(privatePath, kept + "\n", storage);
                return new AccessTokenMove(AccessTokenMigration.KeptPrivateToken, true, !removed);
            }
            var bytes = entry == StoredEntry.File ? storage.ReadFile(legacyPath, MaxBytes) : null;
            var token = bytes == null ? null : Encoding.UTF8.GetString(bytes).Trim();
            if (token == null || !IsToken(token))
            {
                return new AccessTokenMove(AccessTokenMigration.Unusable, true, !storage.TryRemove(legacyPath));
            }
            var restricted = Write(privatePath, token + "\n", storage);
            return new AccessTokenMove(AccessTokenMigration.Moved, restricted, !storage.TryRemove(legacyPath));
        }

        /// <summary>
        /// Removes whatever is at <paramref name="legacyPath"/> without reading or following it, as a
        /// release build does, which never takes a token from shared storage.
        /// </summary>
        public static AccessTokenMove Discard(string legacyPath, ITokenStorage storage)
        {
            if (storage.Examine(legacyPath) == StoredEntry.Missing) return new AccessTokenMove(AccessTokenMigration.NothingThere, true, false);
            var folder = Path.GetDirectoryName(Path.GetFullPath(legacyPath));
            if (folder != null && !storage.IsPlainFolder(folder))
            {
                return new AccessTokenMove(AccessTokenMigration.LeftInLinkedFolder, true, true);
            }
            return new AccessTokenMove(AccessTokenMigration.Discarded, true, !storage.TryRemove(legacyPath));
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

        /// <summary>The token in app-private storage, or null when there is none or the file is empty, as one cut off while written.</summary>
        private static string? PrivateToken(string privatePath)
        {
            if (!File.Exists(privatePath)) return null;
            var token = File.ReadAllText(privatePath).Trim();
            return token.Length == 0 ? null : token;
        }

        /// <summary>
        /// Writes <paramref name="text"/> to <paramref name="path"/> through a new file restricted while
        /// still empty; returns whether it could be restricted. Removes the new file if it is not moved
        /// into place.
        /// </summary>
        private static bool Write(string path, string text, ITokenStorage storage)
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
                    storage.Restrict(temporary);
                }
                catch
                {
                    restricted = false;
                }
                File.WriteAllText(temporary, text);
                if (File.Exists(path))
                {
                    // Only an empty file gives way: a token written meanwhile, as with run-as, is newer.
                    if (File.ReadAllText(path).Trim().Length > 0) throw new IOException("A token was written there meanwhile.");
                    File.Delete(path);
                }
                File.Move(temporary, path);
                moved = true;
                return restricted;
            }
            finally
            {
                if (!moved && File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    /// <summary>
    /// The token's files as .NET Standard reaches them, for the editor and tests: a link is told by
    /// its attributes, and a file is opened only when it has a size, so a pipe, which has none, is
    /// never opened. It can't tell a file with several names, nor close the moment between looking
    /// and opening; Android's storage does both.
    /// </summary>
    public sealed class ManagedTokenStorage : ITokenStorage
    {
        private readonly Action<string> restrict;

        public ManagedTokenStorage(Action<string> restrict)
        {
            this.restrict = restrict;
        }

        public StoredEntry Examine(string path)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(path);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return StoredEntry.Missing;
            }
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return StoredEntry.Other;
            return StoredEntry.File;
        }

        public bool IsPlainFolder(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return false;
            }
        }

        public byte[]? ReadFile(string path, int max)
        {
            if (Examine(path) != StoredEntry.File) return null;
            var length = new FileInfo(path).Length;
            if (length <= 0 || length > max) return null;
            var bytes = File.ReadAllBytes(path);
            return bytes.Length > max ? null : bytes;
        }

        public void Restrict(string path) => restrict(path);

        public bool TryRemove(string path)
        {
            try
            {
                File.Delete(path);
                return Examine(path) == StoredEntry.Missing;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
