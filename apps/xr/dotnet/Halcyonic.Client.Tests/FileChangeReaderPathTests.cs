using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The file read names a task's own copy only when asked (CORE's Watch It Work host, ProjectFileQuery.workstream_id).</summary>
public class FileChangeReaderPathTests
{
    [Test]
    public void TheFileReadNamesTheTaskOnlyWhenAsked()
    {
        Assert.That(FileChangeReader.FilePath("p 1", "j", "src/a b.js"), Is.EqualTo("api/projects/p%201/file?journal_id=j&path=src%2Fa%20b.js"));
        Assert.That(FileChangeReader.FilePath("p", "j", "a.js", "w 1"), Is.EqualTo("api/projects/p/file?journal_id=j&path=a.js&workstream_id=w%201"));
    }
}
