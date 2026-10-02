#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>
    /// What kind of file a changed file is, as far as its name says, for the generic glyph beside it
    /// (ADR 0026): never a language's or a product's own mark, which the competition's rules forbid.
    /// A name it does not know reads as a document.
    /// </summary>
    public enum FileKind
    {
        /// <summary>Source code, styles and markup.</summary>
        Code,

        /// <summary>A database, a schema or a migration in SQL.</summary>
        Database,

        /// <summary>Structured data and settings, such as JSON, YAML or a Unity asset.</summary>
        Data,

        /// <summary>Prose, and every name this does not know.</summary>
        Document,

        /// <summary>A picture.</summary>
        Image,

        /// <summary>A shell or command script.</summary>
        Script,

        /// <summary>A package's or a build's own file, such as a manifest, a lock file or a project file.</summary>
        Package,

        /// <summary>A folder.</summary>
        Folder,
    }

    /// <summary>Tells a file's kind from its path, and the glyph each kind shows.</summary>
    public static class FileKinds
    {
        /// <summary>
        /// The kind a path names: a folder by its trailing slash, then a package's or a build's own
        /// file by its whole name, then the extension, then a document.
        /// </summary>
        public static FileKind Of(string? path)
        {
            if (string.IsNullOrEmpty(path)) return FileKind.Document;
            var plain = path!.Replace('\\', '/');
            if (plain.EndsWith("/", StringComparison.Ordinal)) return FileKind.Folder;
            var slash = plain.LastIndexOf('/');
            var name = (slash < 0 ? plain : plain.Substring(slash + 1)).ToLowerInvariant();
            if (Packages.Contains(name)) return FileKind.Package;
            var dot = name.LastIndexOf('.');
            if (dot <= 0) return Bare.Contains(name) ? FileKind.Script : FileKind.Document;
            var extension = name.Substring(dot + 1);
            return Extensions.TryGetValue(extension, out var kind) ? kind : FileKind.Document;
        }

        /// <summary>
        /// The Material Symbols Rounded glyph a kind shows (ADR 0026, Apache-2.0): generic marks
        /// only, the names the icon atlas carries.
        /// </summary>
        public static string GlyphName(FileKind kind) => kind switch
        {
            FileKind.Code => "code",
            FileKind.Database => "database",
            FileKind.Data => "data_object",
            FileKind.Image => "image",
            FileKind.Script => "terminal",
            FileKind.Package => "deployed_code",
            FileKind.Folder => "folder",
            _ => "description",
        };

        /// <summary>A package's or a build's own files, by their whole name.</summary>
        private static readonly HashSet<string> Packages = new HashSet<string>(StringComparer.Ordinal)
        {
            "package.json", "package-lock.json", "pnpm-lock.yaml", "pnpm-workspace.yaml", "yarn.lock", "bun.lockb", "deno.json",
            "cargo.toml", "cargo.lock", "go.mod", "go.sum", "pyproject.toml", "requirements.txt", "pipfile", "pipfile.lock", "poetry.lock",
            "setup.py", "setup.cfg", "gemfile", "gemfile.lock", "pom.xml", "build.gradle", "build.gradle.kts", "settings.gradle",
            "settings.gradle.kts", "dockerfile", "docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml", "makefile",
            "cmakelists.txt", "podfile", "podfile.lock", "package.swift", "package.resolved", "directory.build.props", "global.json",
            "manifest.json", "packages-lock.json",
        };

        /// <summary>Files without an extension that are scripts.</summary>
        private static readonly HashSet<string> Bare = new HashSet<string>(StringComparer.Ordinal) { "gradlew", "configure" };

        private static readonly Dictionary<string, FileKind> Extensions = Build();

        private static Dictionary<string, FileKind> Build()
        {
            var map = new Dictionary<string, FileKind>(StringComparer.Ordinal);
            void Add(FileKind kind, params string[] extensions)
            {
                foreach (var extension in extensions) map[extension] = kind;
            }
            Add(FileKind.Package, "csproj", "fsproj", "vbproj", "sln", "asmdef", "asmref", "nuspec", "podspec", "gemspec");
            Add(FileKind.Image, "png", "jpg", "jpeg", "gif", "svg", "webp", "ico", "bmp", "tif", "tiff", "psd", "heic", "avif", "exr", "hdr", "tga");
            Add(FileKind.Database, "sql", "sqlite", "sqlite3", "db", "prisma");
            Add(FileKind.Data, "json", "jsonc", "json5", "jsonl", "yaml", "yml", "toml", "xml", "csv", "tsv", "ini", "cfg", "conf", "env", "lock",
                "plist", "properties", "graphql", "gql", "proto", "unity", "prefab", "asset", "meta", "mat", "anim", "controller");
            Add(FileKind.Script, "sh", "bash", "zsh", "fish", "ps1", "psm1", "bat", "cmd", "command");
            Add(FileKind.Code, "ts", "tsx", "mts", "cts", "js", "jsx", "mjs", "cjs", "cs", "py", "rb", "go", "rs", "java", "kt", "kts", "swift", "c",
                "h", "cc", "cpp", "cxx", "hpp", "hh", "m", "mm", "php", "scala", "lua", "dart", "r", "jl", "ex", "exs", "erl", "hs", "ml", "fs", "vb",
                "pl", "clj", "zig", "nim", "css", "scss", "sass", "less", "html", "htm", "vue", "svelte", "astro", "shader", "hlsl", "glsl",
                "cginc", "compute", "uss", "uxml", "razor", "cshtml");
            Add(FileKind.Document, "md", "mdx", "txt", "rst", "adoc", "org", "pdf", "doc", "docx", "rtf", "odt");
            return map;
        }
    }
}
