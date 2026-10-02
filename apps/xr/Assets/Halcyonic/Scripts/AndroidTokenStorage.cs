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
    /// first look is read (docs/internal/architecture/SECURITY.md).
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
            catch (AndroidJavaException)
            {
                return null;
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
            catch (AndroidJavaException)
            {
                // Refused, as for a file another user made: reported by what lstat finds next.
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

        /// <summary>What lstat says of the path, or null when there is nothing there or it can't be looked at.</summary>
        private static Stat? Lstat(string path)
        {
            try
            {
                using var os = new AndroidJavaClass("android.system.Os");
                using var constants = new AndroidJavaClass("android.system.OsConstants");
                using var stat = os.CallStatic<AndroidJavaObject>("lstat", path);
                return Describe(stat, constants);
            }
            catch (AndroidJavaException)
            {
                return null;
            }
        }

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
        private static byte[]? ReadAll(AndroidJavaObject descriptor, int size)
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
                        AndroidJNI.DeleteLocalRef(thrown);
                        return null;
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
    }
}
#endif
