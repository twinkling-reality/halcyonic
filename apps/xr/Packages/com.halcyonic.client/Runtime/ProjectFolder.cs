#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Where a project's files live, as the person chose it from what the host listed
    /// (<c>GET /api/locations</c>): a folder already in one of the host's project roots, the root
    /// itself, or a new folder the host makes there. The headset never composes or takes apart a
    /// path: it sends back the root's path and the folder's name exactly as listed, and the host
    /// checks both again (ADR 0020). Names come from the file system, so they show by
    /// <see cref="LabelText"/>'s rule.
    /// </summary>
    public sealed class ProjectFolder
    {
        /// <summary>The host's rule for a new folder's name: one plain segment.</summary>
        public const string NewNamePattern = "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$";

        private static readonly Regex NewName = new Regex(NewNamePattern, RegexOptions.CultureInvariant);

        private ProjectFolder(string rootPath, string rootName, string? folderName, bool isNew)
        {
            RootPath = rootPath;
            RootName = rootName;
            FolderName = folderName;
            IsNew = isNew;
        }

        /// <summary>The root's path exactly as the host listed it, sent back unchanged.</summary>
        public string RootPath { get; }

        /// <summary>The root's own name, as the host listed it.</summary>
        public string RootName { get; }

        /// <summary>The folder inside the root, or null for the root itself.</summary>
        public string? FolderName { get; }

        /// <summary>The host makes the folder when the project is created.</summary>
        public bool IsNew { get; }

        /// <summary>A folder the host listed in a root, or the root itself when <paramref name="folder"/> is null.</summary>
        public static ProjectFolder Existing(LocationRoot root, LocationFolder? folder) =>
            new ProjectFolder(root.Path, root.Name, folder?.Name, false);

        /// <summary>A folder already there, named as a refusal named it: "Use that folder" after <c>location_exists</c>.</summary>
        public static ProjectFolder Existing(ProjectFolder taken) =>
            new ProjectFolder(taken.RootPath, taken.RootName, taken.FolderName, false);

        /// <summary>
        /// A new folder for the host to make in a root. Returns null when the name breaks the host's
        /// rule (<see cref="NewNamePattern"/>), which the host would refuse without journaling.
        /// </summary>
        public static ProjectFolder? New(LocationRoot root, string name)
        {
            var trimmed = (name ?? "").Trim();
            return IsValidNewName(trimmed) ? new ProjectFolder(root.Path, root.Name, trimmed, true) : null;
        }

        public static bool IsValidNewName(string name) => name != null && NewName.IsMatch(name);

        /// <summary>
        /// A choice the device kept across an app restart (<see cref="CreationDraft"/>), or null when it
        /// cannot be one: the host checks the root and the name again when it is sent, as it does any.
        /// </summary>
        public static ProjectFolder? Restore(string? rootPath, string? rootName, string? folderName, bool isNew)
        {
            if (string.IsNullOrEmpty(rootPath) || rootName == null) return null;
            if (isNew && (folderName == null || !IsValidNewName(folderName))) return null;
            if (folderName != null && folderName.Length == 0) return null;
            return new ProjectFolder(rootPath!, rootName, folderName, isNew);
        }

        /// <summary>
        /// A new folder's name from the project's name: its letters and digits, lowercase, words
        /// joined by dashes, at most 64 characters, starting with a letter or digit; "project" when
        /// nothing of it fits the rule. Always valid.
        /// </summary>
        public static string SuggestName(string projectName)
        {
            var slug = new StringBuilder();
            var dash = false;
            foreach (var character in (projectName ?? "").ToLowerInvariant())
            {
                if (character < 128 && char.IsLetterOrDigit(character))
                {
                    if (dash && slug.Length > 0) slug.Append('-');
                    dash = false;
                    slug.Append(character);
                    if (slug.Length >= 64) break;
                }
                else dash = true;
            }
            var name = slug.ToString().TrimEnd('-');
            return IsValidNewName(name) ? name : "project";
        }

        /// <summary>What the command carries: the root and the folder's name exactly as listed.</summary>
        public ProjectLocationChoice ToContract() => IsNew
            ? new NewFolderChoice { Root = RootPath, FolderName = FolderName }
            : new ExistingFolderChoice { Root = RootPath, FolderName = FolderName };

        /// <summary>
        /// The choice in words, by the one rule: for example "a new folder, greeting-card, in Projects",
        /// "storefront in Projects", or "directly in Projects".
        /// </summary>
        /// <param name="name">How a name from the file system is shown: <see cref="LabelText.Plain"/> unless given, or as it is for a review that spells it itself.</param>
        /// <param name="startOfLine">Halcyonic's own first word capitalized, for a line of its own; a name from the file system stays as it is.</param>
        public string Describe(Func<string, string>? name = null, bool startOfLine = false)
        {
            name ??= LabelText.Plain;
            var root = name(RootName);
            if (FolderName == null) return (startOfLine ? "Directly in " : "directly in ") + root;
            var folder = name(FolderName);
            return IsNew ? (startOfLine ? "A new folder, " : "a new folder, ") + folder + ", in " + root : folder + " in " + root;
        }

        /// <summary>
        /// What the listing offers, root by root: making a new folder there, the root itself, and each
        /// folder in it. A root the host lists as missing is shown, and offers nothing.
        /// </summary>
        /// <summary>A new folder's words in a place: in the folder list, and over its name as it is given.</summary>
        public static string NewFolderLabel(LocationRoot root) => "New folder in " + LabelText.Plain(root.Name);

        public static IReadOnlyList<FolderOption> Options(LocationsResponse listing)
        {
            var options = new List<FolderOption>();
            foreach (var root in listing.Roots)
            {
                var name = LabelText.Plain(root.Name);
                if (root.Status != LocationRootStatus.Available)
                {
                    options.Add(new FolderOption(root, null, FolderOptionKind.MissingRoot, name, "Not on " + HostText.Your + " right now"));
                    continue;
                }
                options.Add(new FolderOption(root, null, FolderOptionKind.NewFolder, NewFolderLabel(root), HostText.YourStart + " makes a new, empty folder"));
                options.Add(new FolderOption(root, null, FolderOptionKind.Root, "Directly in " + name, "Files go straight into " + name));
                foreach (var folder in root.Folders)
                {
                    options.Add(new FolderOption(root, folder, FolderOptionKind.Folder, LabelText.Plain(folder.Name), "In " + name));
                }
            }
            return options;
        }
    }

    public enum FolderOptionKind
    {
        /// <summary>Make a new folder in the root.</summary>
        NewFolder,

        /// <summary>The root itself.</summary>
        Root,

        /// <summary>A folder directly inside the root.</summary>
        Folder,

        /// <summary>A root the host lists but cannot find now; nothing to choose.</summary>
        MissingRoot,
    }

    /// <summary>One row of Where its files live: words by the one rule, and what choosing it means.</summary>
    public sealed class FolderOption
    {
        public FolderOption(LocationRoot root, LocationFolder? folder, FolderOptionKind kind, string label, string detail)
        {
            Root = root;
            Folder = folder;
            Kind = kind;
            Label = label;
            Detail = detail;
        }

        public LocationRoot Root { get; }

        public LocationFolder? Folder { get; }

        public FolderOptionKind Kind { get; }

        public string Label { get; }

        public string Detail { get; }

        public bool Choosable => Kind != FolderOptionKind.MissingRoot;
    }
}
