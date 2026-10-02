using System;
using System.IO;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class AccessTokenFileTests
{
    private string directory = null!;
    private string legacy = null!;
    private string private_ = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Directory.CreateTempSubdirectory("halcyonic-token-").FullName;
        legacy = Path.Combine(directory, "shared", "access-token");
        private_ = Path.Combine(directory, "files", "access-token");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(directory, recursive: true);

    private static void OwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Test]
    public void ATokenInSharedStorageMovesIntoPrivateStorageOnceAndLeavesNoCopy()
    {
        File.WriteAllText(legacy, "  the-token\n");
        var restricted = 0;
        long sizeWhenRestricted = -1;
        var outcome = AccessTokenFile.Migrate(legacy, private_, path =>
        {
            restricted++;
            sizeWhenRestricted = new FileInfo(path).Length;
            OwnerOnly(path);
        });
        Assert.That(outcome, Is.EqualTo(AccessTokenMigration.Moved));
        Assert.That(restricted, Is.EqualTo(1));
        Assert.That(sizeWhenRestricted, Is.EqualTo(0), "the file is made private before the token is written into it");
        Assert.That(File.Exists(legacy), Is.False, "no copy is left on shared storage");
        Assert.That(File.ReadAllText(private_), Is.EqualTo("the-token\n"));
        Assert.That(File.Exists(private_ + ".new"), Is.False);
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(File.GetUnixFileMode(private_), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
        Assert.That(AccessTokenFile.Migrate(legacy, private_, OwnerOnly), Is.EqualTo(AccessTokenMigration.NothingToMove));
        Assert.That(AccessTokenFile.Read(new[] { legacy, private_ }), Is.EqualTo("the-token"));
    }

    [Test]
    public void ATokenAlreadyInPrivateStorageWinsAndTheSharedCopyIsRemoved()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(private_)!);
        File.WriteAllText(private_, "kept\n");
        File.WriteAllText(legacy, "shared\n");
        Assert.That(AccessTokenFile.Migrate(legacy, private_, _ => Assert.Fail("nothing new is written")), Is.EqualTo(AccessTokenMigration.RemovedStaleCopy));
        Assert.That(File.Exists(legacy), Is.False);
        Assert.That(File.ReadAllText(private_), Is.EqualTo("kept\n"));
    }

    [Test]
    public void AnEmptyOrOversizedSharedCopyIsRemovedAndNothingIsWritten()
    {
        foreach (var content in new[] { " \n", new string('x', (int)AccessTokenFile.MaxBytes + 1) })
        {
            File.WriteAllText(legacy, content);
            Assert.That(AccessTokenFile.Migrate(legacy, private_, OwnerOnly), Is.EqualTo(AccessTokenMigration.RemovedUnusableCopy));
            Assert.That(File.Exists(legacy), Is.False);
            Assert.That(File.Exists(private_), Is.False);
        }
    }

    [Test]
    public void WhenTheNewFileCannotBeRestrictedTheTokenStillLeavesSharedStorage()
    {
        File.WriteAllText(legacy, "the-token\n");
        var outcome = AccessTokenFile.Migrate(legacy, private_, _ => throw new InvalidOperationException("chmod failed"));
        Assert.That(outcome, Is.EqualTo(AccessTokenMigration.MovedUnrestricted));
        Assert.That(File.ReadAllText(private_), Is.EqualTo("the-token\n"));
        Assert.That(File.Exists(private_ + ".new"), Is.False);
        Assert.That(File.Exists(legacy), Is.False, "private storage is closed to other apps by itself, so the shared copy goes");
    }

    [Test]
    public void TwoNamesForOneFileNeverLoseTheToken()
    {
        File.WriteAllText(legacy, "the-token\n");
        Assert.That(AccessTokenFile.Migrate(legacy, legacy, OwnerOnly), Is.EqualTo(AccessTokenMigration.NothingToMove));
        Assert.That(File.ReadAllText(legacy), Is.EqualTo("the-token\n"));
        if (OperatingSystem.IsWindows()) return;
        // The private folder reached through a link from where the shared copy was looked for.
        var linked = Path.Combine(directory, "linked");
        Directory.CreateSymbolicLink(linked, Path.GetDirectoryName(legacy)!);
        Assert.That(AccessTokenFile.Migrate(Path.Combine(linked, "access-token"), legacy, OwnerOnly), Is.EqualTo(AccessTokenMigration.RemovedStaleCopy));
        Assert.That(File.ReadAllText(legacy), Is.EqualTo("the-token\n"), "deleting the other name took nothing away");
    }

    [Test]
    public void AHalfMadeFileIsRemovedWhenTheMoveCannotFinish()
    {
        File.WriteAllText(legacy, "the-token\n");
        // The owner writes a token with run-as between the check and the move.
        Assert.Throws<IOException>(() => AccessTokenFile.Migrate(legacy, private_, path =>
        {
            OwnerOnly(path);
            File.WriteAllText(private_, "written-by-the-owner\n");
        }));
        Assert.That(File.Exists(private_ + ".new"), Is.False, "no copy is left beside it");
        Assert.That(File.ReadAllText(private_), Is.EqualTo("written-by-the-owner\n"));
        Assert.That(AccessTokenFile.Migrate(legacy, private_, OwnerOnly), Is.EqualTo(AccessTokenMigration.RemovedStaleCopy), "the next run removes the shared copy");
    }

    [Test]
    public void ReadingTakesTheFirstNonEmptyToken()
    {
        var empty = Path.Combine(directory, "empty");
        File.WriteAllText(empty, "\n");
        File.WriteAllText(legacy, "second\n");
        Assert.That(AccessTokenFile.Read(new[] { Path.Combine(directory, "missing"), empty, legacy }), Is.EqualTo("second"));
        Assert.That(AccessTokenFile.Read(new[] { Path.Combine(directory, "missing") }), Is.Null);
    }
}
