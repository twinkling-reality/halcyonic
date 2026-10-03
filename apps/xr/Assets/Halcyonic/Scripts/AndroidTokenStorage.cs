#if UNITY_ANDROID && !UNITY_EDITOR
#nullable enable
using System;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// The access token's files through Android's own system calls (android.system.Os): a path is
    /// looked at with lstat, so a link is never followed; a file is opened with O_NOFOLLOW and
    /// O_NONBLOCK, so a link that took its place is refused and a pipe never holds the app; and what
    /// was opened is checked again with fstat before a byte is read, so nothing swapped in after the
    /// first look is read (docs/internal/architecture/SECURITY.md). Only ENOENT and ENOTDIR mean
    /// nothing is there, only ELOOP from opening means a link, and only EACCES, EPERM and EROFS from
    /// removing mean the removal was refused; any other failure throws
    /// <see cref="TokenStorageException"/>, so it is never taken for one of those.
    /// </summary>
    internal sealed class AndroidTokenStorage : ITokenStorage
    {
        public StoredEntry Examine(string path)
        {
            var stat = Lstat(path);
            if (stat == null) return StoredEntry.Missing;
            return stat.Value.Regular && stat.Value.Links == 1 ? StoredEntry.File : StoredEntry.Other;
        }

        public bool IsPlainFolder(string path) => Lstat(path)?.Folder == true;

        public byte[]? ReadFile(string path, int max)
        {
            using var os = new AndroidJavaClass("android.system.Os");
            using var constants = new AndroidJavaClass("android.system.OsConstants");
            var flags = constants.GetStatic<int>("O_RDONLY") | constants.GetStatic<int>("O_NOFOLLOW") | constants.GetStatic<int>("O_NONBLOCK");
            AndroidJavaObject? descriptor = null;
            try
            {
                descriptor = os.CallStatic<AndroidJavaObject>("open", path, flags, 0);
                using var stat = os.CallStatic<AndroidJavaObject>("fstat", descriptor);
                var opened = Describe(stat, constants);
                if (!opened.Regular || opened.Links != 1 || opened.Size <= 0 || opened.Size > max) return null;
                return ReadAll(descriptor, (int)opened.Size);
            }
            catch (AndroidJavaException error)
            {
                var errno = Errno(error.Message);
                // A link that took the file's place after it was looked at, refused by O_NOFOLLOW, or
                // a file gone meanwhile: either way not one to read.
                if (descriptor == null && TokenStorageException.Classify("open", errno) != FailedCall.Unexpected) return null;
                throw Failure(descriptor == null ? "open" : "fstat", errno);
            }
            finally
            {
                if (descriptor != null)
                {
                    try
                    {
                        os.CallStatic("close", descriptor);
                    }
                    catch (AndroidJavaException)
                    {
                        // Already closed, or never open: nothing more to release.
                    }
                    descriptor.Dispose();
                }
            }
        }

        /// <summary>Mode 600: only this app's own user may read or write the file.</summary>
        public void Restrict(string path)
        {
            using var os = new AndroidJavaClass("android.system.Os");
            os.CallStatic("chmod", path, 0x180);
        }

        public bool TryRemove(string path)
        {
            try
            {
                using var os = new AndroidJavaClass("android.system.Os");
                os.CallStatic("remove", path);
            }
            catch (AndroidJavaException error)
            {
                var errno = Errno(error.Message);
                var meaning = TokenStorageException.Classify("remove", errno);
                // Refused, as for a file another user made or on storage mounted read-only: reported.
                if (meaning == FailedCall.Refused) return false;
                if (meaning != FailedCall.Missing) throw Failure("remove", errno);
            }
            return Lstat(path) == null;
        }

        private readonly struct Stat
        {
            public Stat(bool regular, bool folder, long links, long size)
            {
                Regular = regular;
                Folder = folder;
                Links = links;
                Size = size;
            }

            public bool Regular { get; }
            public bool Folder { get; }
            public long Links { get; }
            public long Size { get; }
        }

        /// <summary>What lstat says of the path, or null when there is nothing there.</summary>
        private static Stat? Lstat(string path)
        {
            try
            {
                using var os = new AndroidJavaClass("android.system.Os");
                using var constants = new AndroidJavaClass("android.system.OsConstants");
                using var stat = os.CallStatic<AndroidJavaObject>("lstat", path);
                return Describe(stat, constants);
            }
            catch (AndroidJavaException error)
            {
                var errno = Errno(error.Message);
                if (TokenStorageException.Classify("lstat", errno) == FailedCall.Missing) return null;
                throw Failure("lstat", errno);
            }
        }

        /// <summary>
        /// The errno name a failed call's exception carries. Unity's message is the Java exception's
        /// toString, and an ErrnoException's reads "lstat failed: ENOENT (No such file or directory)".
        /// </summary>
        private static string? Errno(string? message) => TokenStorageException.ErrnoNameIn(message);

        private static TokenStorageException Failure(string call, string? errno) =>
            new TokenStorageException(call + " failed with " + (errno ?? "an error that names no errno"));

        private static Stat Describe(AndroidJavaObject stat, AndroidJavaClass constants)
        {
            var mode = stat.Get<int>("st_mode");
            return new Stat(
                constants.CallStatic<bool>("S_ISREG", mode),
                constants.CallStatic<bool>("S_ISDIR", mode),
                stat.Get<long>("st_nlink"),
                stat.Get<long>("st_size"));
        }

        /// <summary>
        /// Reads up to <paramref name="size"/> bytes from an open descriptor with Os.read into a Java
        /// array, through raw JNI, since Unity's wrapper copies an array argument and would not return
        /// what was read into it.
        /// </summary>
        private static byte[] ReadAll(AndroidJavaObject descriptor, int size)
        {
            var os = AndroidJNI.FindClass("android/system/Os");
            var array = AndroidJNI.NewSByteArray(size);
            try
            {
                var read = AndroidJNI.GetStaticMethodID(os, "read", "(Ljava/io/FileDescriptor;[BII)I");
                var total = 0;
                while (total < size)
                {
                    var count = AndroidJNI.CallStaticIntMethod(os, read, new[]
                    {
                        new jvalue { l = descriptor.GetRawObject() },
                        new jvalue { l = array },
                        new jvalue { i = total },
                        new jvalue { i = size - total },
                    });
                    var thrown = AndroidJNI.ExceptionOccurred();
                    if (thrown != IntPtr.Zero)
                    {
                        AndroidJNI.ExceptionClear();
                        var text = ThrowableText(thrown);
                        AndroidJNI.DeleteLocalRef(thrown);
                        throw Failure("read", Errno(text));
                    }
                    if (count <= 0) break;
                    total += count;
                }
                var bytes = AndroidJNI.FromSByteArray(array);
                var result = new byte[total];
                Buffer.BlockCopy(bytes, 0, result, 0, total);
                return result;
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(array);
                AndroidJNI.DeleteLocalRef(os);
            }
        }

        /// <summary>A thrown Java exception's toString, as Unity's own wrapper takes it for a message.</summary>
        private static string? ThrowableText(IntPtr thrown)
        {
            var throwable = AndroidJNI.FindClass("java/lang/Throwable");
            try
            {
                var toString = AndroidJNI.GetMethodID(throwable, "toString", "()Ljava/lang/String;");
                var text = AndroidJNI.CallStringMethod(thrown, toString, Array.Empty<jvalue>());
                // Should toString itself throw, its exception is cleared and the errno stays unknown.
                AndroidJNI.ExceptionClear();
                return text;
            }
            finally
            {
                AndroidJNI.DeleteLocalRef(throwable);
            }
        }
    }
}
#endif
