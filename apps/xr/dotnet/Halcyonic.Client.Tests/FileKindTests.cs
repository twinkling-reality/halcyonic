using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class FileKindTests
{
    [Test]
    public void AFilesKindComesFromItsWholeNameThenItsExtension()
    {
        foreach (var (path, kind) in new[]
        {
            ("src/middleware/rate-limit.ts", FileKind.Code),
            ("apps/xr/Assets/Halcyonic/Workspace/SectionView.cs", FileKind.Code),
            ("styles/site.css", FileKind.Code),
            ("migrations/0012_sign_in_attempts.sql", FileKind.Database),
            ("data/app.sqlite", FileKind.Database),
            ("config/settings.yaml", FileKind.Data),
            ("fixtures/v1/session-report-verified.json", FileKind.Data),
            ("Assets/Scenes/Stage.unity", FileKind.Data),
            ("Assets/Scenes/Stage.unity.meta", FileKind.Data),
            ("public/logo.png", FileKind.Image),
            ("docs/diagram.svg", FileKind.Image),
            ("scripts/deploy.sh", FileKind.Script),
            ("tools/build.ps1", FileKind.Script),
            ("gradlew", FileKind.Script),
            ("package.json", FileKind.Package),
            ("apps/web/package.json", FileKind.Package),
            ("pnpm-lock.yaml", FileKind.Package),
            ("Dockerfile", FileKind.Package),
            ("Makefile", FileKind.Package),
            ("apps/xr/dotnet/Halcyonic.Client/Halcyonic.Client.csproj", FileKind.Package),
            ("Assets/Halcyonic/Halcyonic.XR.asmdef", FileKind.Package),
            ("README.md", FileKind.Document),
            ("LICENSE", FileKind.Document),
            ("docs/notes.txt", FileKind.Document),
            ("src/", FileKind.Folder),
            (@"C:\repo\src\Program.cs", FileKind.Code),
        })
        {
            Assert.That(FileKinds.Of(path), Is.EqualTo(kind), path);
        }
    }

    [Test]
    public void ANameItDoesNotKnowReadsAsADocument()
    {
        foreach (var path in new[] { "notes.weird", "data.unknownext", ".gitignore", ".env.example.bak", "", null })
        {
            Assert.That(FileKinds.Of(path), Is.EqualTo(FileKind.Document), path ?? "null");
        }
        Assert.That(FileKinds.Of(".env"), Is.EqualTo(FileKind.Document), "a dot file without another dot has no extension");
        Assert.That(FileKinds.Of("config/.env"), Is.EqualTo(FileKind.Document));
        Assert.That(FileKinds.Of("PACKAGE.JSON"), Is.EqualTo(FileKind.Package), "names are compared without case");
        Assert.That(FileKinds.Of("Photo.JPEG"), Is.EqualTo(FileKind.Image));
    }

    [Test]
    public void EachKindShowsOneGenericGlyphOfTheAtlas()
    {
        var glyphs = Enum.GetValues(typeof(FileKind)).Cast<FileKind>().Select(FileKinds.GlyphName).ToList();
        Assert.That(glyphs, Is.EqualTo(new[] { "code", "database", "data_object", "description", "image", "terminal", "deployed_code", "folder" }));
        Assert.That(glyphs, Is.Unique);
    }
}
