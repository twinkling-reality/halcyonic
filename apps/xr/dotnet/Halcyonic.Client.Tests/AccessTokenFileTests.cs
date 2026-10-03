using System;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class AccessTokenFileTests
{
    /// <summary>A token in its own form: 43 characters of base64url.</summary>
    private static readonly string Token = new string('a', 40) + "_-Z";

    private string directory = null!;
    private string legacy = null!;
    private string private_ = null!;
    private ManagedTokenStorage storage = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Directory.CreateTempSubdirectory("halcyonic-token-").FullName;
        legacy = Path.Combine(directory, "shared", "access-token");
        private_ = Path.Combine(directory, "files", "access-token");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        storage = new ManagedTokenStorage(OwnerOnly);
    }

    [TearDown]
    public void TearDown()
    {
        if (!OperatingSystem.IsWindows())
        {
            foreach (var folder in Directory.GetDirectories(directory, "*", SearchOption.AllDirectories))
            {
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        Directory.Delete(directory, recursive: true);
    }

    private static void OwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Skips a test that needs file modes to bind, which they don't for root.</summary>
    private static void ModesBind()
    {
        if (Environment.IsPrivilegedProcess) Assert.Ignore("root reads and writes whatever the mode says");
    }

    private static void MakeFifo(string path)
    {
        using var mkfifo = Process.Start("mkfifo", path)!;
        mkfifo.WaitForExit();
        Assert.That(mkfifo.ExitCode, Is.EqualTo(0));
    }

    [Test]
    public void ATokenInSharedStorageMovesIntoPrivateStorageOnceAndLeavesNoCopy()
    {
        File.WriteAllText(legacy, "  " + Token + "\n");
        var restricted = new ManagedTokenStorage(path =>
        {
            Assert.That(new FileInfo(path).Length, Is.EqualTo(0), "the file is made private before the token is written into it");
            OwnerOnly(path);
        });
        var move = AccessTokenFile.Migrate(legacy, private_, restricted);
        Assert.That(move.Outcome, Is.EqualTo(AccessTokenMigration.Moved));
        Assert.That(move.Restricted, Is.True);
        Assert.That(move.SharedCopyRemains, Is.False);
        Assert.That(File.Exists(legacy), Is.False, "no copy is left on shared storage");
        Assert.That(File.ReadAllText(private_), Is.EqualTo(Token + "\n"));
        Assert.That(File.Exists(private_ + ".new"), Is.False);
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(File.GetUnixFileMode(private_), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
        Assert.That(AccessTokenFile.Migrate(legacy, private_, storage).Outcome, Is.EqualTo(AccessTokenMigration.NothingThere));
        Assert.That(AccessTokenFile.Read(new[] { legacy, private_ }), Is.EqualTo(Token));
    }

    [Test]
    public void ATokenAlreadyInPrivateStorageWinsAndTheSharedCopyIsRemoved()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(private_)!);
        File.WriteAllText(private_, "kept\n");
        File.WriteAllText(legacy, Token + "\n");
        var move = AccessTokenFile.Migrate(legacy, private_, storage);
        Assert.That(move.Outcome, Is.EqualTo(AccessTokenMigration.KeptPrivateToken));
        Assert.That(move.SharedCopyRemains, Is.False);
        Assert.That(File.Exists(legacy), Is.False);
        Assert.That(File.ReadAllText(private_), Is.EqualTo("kept\n"));
    }

    [Test]
    public void AnEmptyPrivateFileIsNoTokenAndGivesWayToTheSharedOne()
    {
        // A write through run-as cut off midway leaves an empty file behind.
        Directory.CreateDirectory(Path.GetDirectoryName(private_)!);
        File.WriteAllText(private_, string.Empty);
        File.WriteAllText(legacy, Token + "\n");
        Assert.That(AccessTokenFile.Migrate(legacy, private_, storage).Outcome, Is.EqualTo(AccessTokenMigration.Moved));
        Assert.That(File.ReadAllText(private_), Is.EqualTo(Token + "\n"));
        Assert.That(File.Exists(legacy), Is.False);
    }

    [Test]
    public void WhatIsNotATokenInItsOwnFormIsRemovedUnused()
    {
        foreach (var content in new[] { " \n", new string('a', AccessTokenFile.MaxBytes + 1), "{\"credential\":\"x\"}", Token.Substring(1), Token + "=" })
        {
            File.WriteAllText(legacy, content);
            var move = AccessTokenFile.Migrate(legacy, private_, storage);
            Assert.That(move.Outcome, Is.EqualTo(AccessTokenMigration.Unusable), content);
            Assert.That(move.SharedCopyRemains, Is.False, content);
            Assert.That(File.Exists(legacy), Is.False, content);
            Assert.That(File.Exists(private_), Is.False, content);
        }
    }

    [Test]
    public void ALinkOrAPipeAtTheOldPlaceIsRemovedAndNeverFollowedOrOpened()
    {
        if (OperatingSystem.IsWindows()) return;
        var pairing = Path.Combine(directory, "halcyonic-pairing.json");
        File.WriteAllText(pairing, "{\"credential\":\"secret\"}");

        File.CreateSymbolicLink(legacy, pairing);
        Assert.That(AccessTokenFile.Migrate(legacy, private_, storage).Outcome, Is.EqualTo(AccessTokenMigration.Unusable));
        Assert.That(File.Exists(legacy), Is.False, "the link is gone");
        Assert.That(File.ReadAllText(pairing), Does.Contain("secret"), "what it pointed to is untouched");

        // A second name for the pairing: its contents are not a token, so nothing is copied.
        using (var link = Process.Start("ln", new[] { pairing, legacy })!) link.WaitForExit();
        Assert.That(AccessTokenFile.Migrate(legacy, private_, storage).Outcome, Is.EqualTo(AccessTokenMigration.Unusable));
        Assert.That(File.Exists(private_), Is.False);
        Assert.That(File.ReadAllText(pairing), Does.Contain("secret"));

        // A pipe is never opened, so nothing waits on it.
        MakeFifo(legacy);
        var timer = Stopwatch.StartNew();
        Assert.That(AccessTokenFile.Migrate(legacy, private_, storage).Outcome, Is.EqualTo(AccessTokenMigration.Unusable));
        Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
        Assert.That(File.Exists(legacy), Is.False);
    }

    [Test]
    public void NothingInAFolderThatIsALinkIsReadOrRemoved()
    {
        if (OperatingSystem.IsWindows()) return;
        var elsewhere = Path.Combine(directory, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "access-token"), Token + "\n");
        var linked = Path.Combine(directory, "linked");
        Directory.CreateSymbolicLink(linked, elsewhere);
        var move = AccessTokenFile.Migrate(Path.Combine(linked, "access-token"), private_, storage);
        Assert.That(move.Outcome, Is.EqualTo(AccessTokenMigration.LeftInLinkedFolder));
        Assert.That(move.SharedCopyRemains, Is.True);
        Assert.That(File.Exists(private_), Is.False);
        Assert.That(File.Exists(Path.Combine(elsewhere, "access-token")), Is.True);
    }

    [Test]
    public void ACopyThatCannotBeRemovedIsReportedNeverThrown()
    {
        if (OperatingSystem.IsWindows()) return;
        ModesBind();
        File.WriteAllText(legacy, Token + "\n");
        // As for a file another user made: the folder lets this one read it, not remove it.
        File.SetUnixFileMode(Path.GetDirectoryName(legacy)!, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var move = AccessTokenFile.Migrate(legacy, private_, storage);
        Assert.That(move.Outcome, Is.EqualTo(AccessTokenMigration.Moved));
        Assert.That(move.SharedCopyRemains, Is.True);
        Assert.That(File.ReadAllText(private_), Is.EqualTo(Token + "\n"));
        var again = AccessTokenFile.Migrate(legacy, private_, storage);
        Assert.That(again.Outcome, Is.EqualTo(AccessTokenMigration.KeptPrivateToken), "every later start says so again, and goes on");
        Assert.That(again.SharedCopyRemains, Is.True);
    }

    [Test]
    public void AFailingCallIsNeverTakenForNothingThereNotATokenOrRemoved()
    {
        if (OperatingSystem.IsWindows()) return;
        ModesBind();
        var shared = Path.GetDirectoryName(legacy)!;
        File.WriteAllText(legacy, Token + "\n");

        // A folder that can't be searched: looking at the token fails, which is not "nothing there".
        File.SetUnixFileMode(shared, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Assert.Throws<TokenStorageException>(() => AccessTokenFile.Migrate(legacy, private_, storage));
        Assert.Throws<TokenStorageException>(() => AccessTokenFile.Discard(legacy, storage));
        File.SetUnixFileMode(shared, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.That(File.ReadAllText(legacy), Is.EqualTo(Token + "\n"));
        Assert.That(File.Exists(private_), Is.False);

        // A token that can't be read is not "not a token", so it is not removed.
        File.SetUnixFileMode(legacy, UnixFileMode.None);
        Assert.Throws<TokenStorageException>(() => AccessTokenFile.Migrate(legacy, private_, storage));
        Assert.That(File.Exists(legacy), Is.True);
        Assert.That(File.Exists(private_), Is.False);
        File.SetUnixFileMode(legacy, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        // A removal that fails, not refused: the token is moved, the failure is thrown, and the next
        // run removes the copy.
        Assert.Throws<TokenStorageException>(() => AccessTokenFile.Migrate(legacy, private_, new FailingRemoval(storage)));
        Assert.That(File.ReadAllText(private_), Is.EqualTo(Token + "\n"));
        Assert.That(File.Exists(legacy), Is.True);
        var next = AccessTokenFile.Migrate(legacy, private_, storage);
        Assert.That(next.Outcome, Is.EqualTo(AccessTokenMigration.KeptPrivateToken));
        Assert.That(next.SharedCopyRemains, Is.False);
        Assert.That(File.Exists(legacy), Is.False);
    }

    [Test]
    public void TwoNamesForOneFileKeepTheTokenWhenLookingAfterRemovingFails()
    {
        if (OperatingSystem.IsWindows()) return;
        var real = Path.Combine(directory, "real");
        Directory.CreateDirectory(Path.Combine(real, "files"));
        File.WriteAllText(Path.Combine(real, "files", "access-token"), Token + "\n");
        var linked = Path.Combine(directory, "user0");
        Directory.CreateSymbolicLink(linked, real);
        // The old name goes, and then looking whether it is gone fails.
        var failing = new FailingRemoval(storage, removeFirst: true);
        Assert.Throws<TokenStorageException>(() =>
            AccessTokenFile.Migrate(Path.Combine(linked, "files", "access-token"), Path.Combine(real, "files", "access-token"), failing));
        Assert.That(File.ReadAllText(Path.Combine(real, "files", "access-token")), Is.EqualTo(Token + "\n"), "the token is put back");
    }

    [Test]
    public void OnlyNothingThereALinkAndARefusalAreOutcomes()
    {
        foreach (var call in new[] { "lstat", "open", "remove" })
        {
            Assert.That(TokenStorageException.Classify(call, "ENOENT"), Is.EqualTo(FailedCall.Missing), call);
            Assert.That(TokenStorageException.Classify(call, "ENOTDIR"), Is.EqualTo(FailedCall.Missing), call);
            foreach (var errno in new[] { "EIO", "ENOMEM", "EMFILE", "ENXIO", "EINTR", null })
            {
                Assert.That(TokenStorageException.Classify(call, errno), Is.EqualTo(FailedCall.Unexpected), call + " " + errno);
            }
        }
        Assert.That(TokenStorageException.Classify("open", "ELOOP"), Is.EqualTo(FailedCall.Link));
        Assert.That(TokenStorageException.Classify("lstat", "ELOOP"), Is.EqualTo(FailedCall.Unexpected), "lstat never follows a link, so ELOOP there is a failure");
        Assert.That(TokenStorageException.Classify("remove", "ELOOP"), Is.EqualTo(FailedCall.Unexpected));
        foreach (var errno in new[] { "EACCES", "EPERM", "EROFS" })
        {
            Assert.That(TokenStorageException.Classify("remove", errno), Is.EqualTo(FailedCall.Refused), errno);
            Assert.That(TokenStorageException.Classify("lstat", errno), Is.EqualTo(FailedCall.Unexpected), "a look refused is not nothing there: " + errno);
            Assert.That(TokenStorageException.Classify("open", errno), Is.EqualTo(FailedCall.Unexpected), "an open refused is not \"not a token\": " + errno);
        }
    }

    [Test]
    public void AnErrnoIsReadFromAndroidsOwnWording()
    {
        Assert.That(TokenStorageException.ErrnoNameIn("android.system.ErrnoException: lstat failed: ENOENT (No such file or directory)"), Is.EqualTo("ENOENT"));
        Assert.That(TokenStorageException.ErrnoNameIn("android.system.ErrnoException: open failed: ELOOP (Too many symbolic links encountered)"), Is.EqualTo("ELOOP"));
        Assert.That(TokenStorageException.ErrnoNameIn("remove failed: EACCES (Permission denied)"), Is.EqualTo("EACCES"));
        Assert.That(TokenStorageException.ErrnoNameIn("android.system.ErrnoException: read failed: errno 4095 (Unknown error 4095)"), Is.Null);
        Assert.That(TokenStorageException.ErrnoNameIn("java.lang.SecurityException: ENOENT (not an errno)"), Is.Null);
        Assert.That(TokenStorageException.ErrnoNameIn(null), Is.Null);
    }

    /// <summary>Storage whose removals fail for a reason other than being refused, before or after the name goes.</summary>
    private sealed class FailingRemoval : ITokenStorage
    {
        private readonly ITokenStorage inner;
        private readonly bool removeFirst;

        public FailingRemoval(ITokenStorage inner, bool removeFirst = false)
        {
            this.inner = inner;
            this.removeFirst = removeFirst;
        }

        public StoredEntry Examine(string path) => inner.Examine(path);

        public bool IsPlainFolder(string path) => inner.IsPlainFolder(path);

        public byte[]? ReadFile(string path, int max) => inner.ReadFile(path, max);

        public void Restrict(string path) => inner.Restrict(path);

        public bool TryRemove(string path)
        {
            if (removeFirst) File.Delete(path);
            throw new TokenStorageException(removeFirst ? "lstat failed with EIO" : "remove failed with EIO");
        }
    }

    [Test]
    public void WhenTheNewFileCannotBeRestrictedTheTokenStillLeavesSharedStorage()
    {
        File.WriteAllText(legacy, Token + "\n");
        var move = AccessTokenFile.Migrate(legacy, private_, new ManagedTokenStorage(_ => throw new InvalidOperationException("chmod failed")));
        Assert.That(move.Outcome, Is.EqualTo(AccessTokenMigration.Moved));
        Assert.That(move.Restricted, Is.False);
        Assert.That(File.ReadAllText(private_), Is.EqualTo(Token + "\n"));
        Assert.That(File.Exists(legacy), Is.False, "private storage keeps other apps out by itself, so the shared copy goes");
    }

    [Test]
    public void TwoNamesForOneFileNeverLoseTheToken()
    {
        File.WriteAllText(legacy, Token + "\n");
        Assert.That(AccessTokenFile.Migrate(legacy, legacy, storage).Outcome, Is.EqualTo(AccessTokenMigration.NothingThere));
        Assert.That(File.ReadAllText(legacy), Is.EqualTo(Token + "\n"));
        if (OperatingSystem.IsWindows()) return;
        // A linked folder higher up, as /data/user/0 and /data/data name one place on Android.
        var real = Path.Combine(directory, "real");
        Directory.CreateDirectory(Path.Combine(real, "files"));
        File.WriteAllText(Path.Combine(real, "files", "access-token"), Token + "\n");
        var linked = Path.Combine(directory, "user0");
        Directory.CreateSymbolicLink(linked, real);
        var move = AccessTokenFile.Migrate(Path.Combine(linked, "files", "access-token"), Path.Combine(real, "files", "access-token"), storage);
        Assert.That(move.Outcome, Is.EqualTo(AccessTokenMigration.KeptPrivateToken));
        Assert.That(File.ReadAllText(Path.Combine(real, "files", "access-token")), Is.EqualTo(Token + "\n"), "removing the other name took nothing away");
    }

    [Test]
    public void ATokenWrittenWhileTheMoveRunsIsKeptAndNoHalfMadeFileStays()
    {
        File.WriteAllText(legacy, Token + "\n");
        // The owner writes a new token with run-as between the check and the move.
        var racing = new ManagedTokenStorage(path =>
        {
            OwnerOnly(path);
            File.WriteAllText(private_, "written-by-the-owner\n");
        });
        Assert.Throws<IOException>(() => AccessTokenFile.Migrate(legacy, private_, racing));
        Assert.That(File.Exists(private_ + ".new"), Is.False, "no copy is left beside it");
        Assert.That(File.ReadAllText(private_), Is.EqualTo("written-by-the-owner\n"), "the newer token is never replaced");
        Assert.That(AccessTokenFile.Migrate(legacy, private_, storage).Outcome, Is.EqualTo(AccessTokenMigration.KeptPrivateToken), "the next run removes the shared copy");
        Assert.That(File.Exists(legacy), Is.False);
    }

    [Test]
    public void AReleaseBuildRemovesWhatIsThereUnread()
    {
        Assert.That(AccessTokenFile.Discard(legacy, storage).Outcome, Is.EqualTo(AccessTokenMigration.NothingThere));
        File.WriteAllText(legacy, Token + "\n");
        var move = AccessTokenFile.Discard(legacy, storage);
        Assert.That(move.Outcome, Is.EqualTo(AccessTokenMigration.Discarded));
        Assert.That(move.SharedCopyRemains, Is.False);
        Assert.That(File.Exists(legacy), Is.False);
        Assert.That(File.Exists(private_), Is.False, "nothing is taken from it");
        if (OperatingSystem.IsWindows()) return;

        var pairing = Path.Combine(directory, "halcyonic-pairing.json");
        File.WriteAllText(pairing, "{\"credential\":\"secret\"}");
        File.CreateSymbolicLink(legacy, pairing);
        Assert.That(AccessTokenFile.Discard(legacy, storage).Outcome, Is.EqualTo(AccessTokenMigration.Discarded));
        Assert.That(File.Exists(legacy), Is.False);
        Assert.That(File.ReadAllText(pairing), Does.Contain("secret"), "a link is removed, never what it points to");

        MakeFifo(legacy);
        var timer = Stopwatch.StartNew();
        Assert.That(AccessTokenFile.Discard(legacy, storage).Outcome, Is.EqualTo(AccessTokenMigration.Discarded));
        Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)), "a pipe is removed at once, never opened");

        ModesBind();
        File.WriteAllText(legacy, Token + "\n");
        File.SetUnixFileMode(Path.GetDirectoryName(legacy)!, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Assert.That(AccessTokenFile.Discard(legacy, storage).SharedCopyRemains, Is.True);
    }

    [Test]
    public void OnlyATokenInItsOwnFormIsATokenAndReadingTakesTheFirst()
    {
        Assert.That(AccessTokenFile.IsToken(Token + "\n"), Is.True);
        Assert.That(AccessTokenFile.IsToken(Token.Substring(1)), Is.False);
        Assert.That(AccessTokenFile.IsToken(Token.Substring(1) + "+"), Is.False);
        var empty = Path.Combine(directory, "empty");
        File.WriteAllText(empty, "\n");
        File.WriteAllText(legacy, "second\n");
        Assert.That(AccessTokenFile.Read(new[] { Path.Combine(directory, "missing"), empty, legacy }), Is.EqualTo("second"));
        Assert.That(AccessTokenFile.Read(new[] { Path.Combine(directory, "missing") }), Is.Null);
    }
}
