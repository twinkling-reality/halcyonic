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
    public void AnEmptySharedCopyIsRemovedAndNothingIsWritten()
    {
        File.WriteAllText(legacy, " \n");
        Assert.That(AccessTokenFile.Migrate(legacy, private_, OwnerOnly), Is.EqualTo(AccessTokenMigration.RemovedEmptyCopy));
        Assert.That(File.Exists(legacy), Is.False);
        Assert.That(File.Exists(private_), Is.False);
    }

    [Test]
    public void WhenTheNewFileCannotBeMadePrivateNothingIsWrittenAndTheOldCopyStays()
    {
        File.WriteAllText(legacy, "the-token\n");
        var outcome = AccessTokenFile.Migrate(legacy, private_, _ => throw new InvalidOperationException("chmod failed"));
        Assert.That(outcome, Is.EqualTo(AccessTokenMigration.NotMoved));
        Assert.That(File.Exists(private_), Is.False);
        Assert.That(File.Exists(private_ + ".new"), Is.False, "no half-made file is left");
        Assert.That(File.ReadAllText(legacy), Is.EqualTo("the-token\n"));
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
