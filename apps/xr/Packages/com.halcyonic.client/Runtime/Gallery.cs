#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>The gallery's groups, in the order it shows them: what needs the person first.</summary>
    public enum GallerySection
    {
        NeedsYou,
        WorkingNow,
        Recent,
        Quiet,
    }

    /// <summary>Which side put an item in its group: this computer's own tasks, or the catalog's report.</summary>
    public enum GallerySectionSource
    {
        Halcyonic,
        Catalog,
    }

    /// <summary>
    /// One project in the gallery: a Halcyonic project, a project of the person's catalog, or both when
    /// the person linked them. Names from either lean, as outside words. Its lines are Halcyonic's words,
    /// but they say two kinds of fact: what this computer observed of its tasks, and what the catalog
    /// reported, so a view marks the reported one as the catalog's (ADR 0040).
    /// </summary>
    public sealed class GalleryItem
    {
        internal GalleryItem(
            string key,
            string? projectId,
            string? catalogId,
            LineWords name,
            LineWords? owner,
            LineWords? group,
            GallerySection section,
            GallerySectionSource sectionFrom,
            LineWords? observedLine,
            LineWords? reportedLine,
            LineWords? detail,
            bool hasCharacter,
            bool onYourComputer,
            LineWords place,
            bool placeInferred,
            bool sample,
            DateTimeOffset? lastActivity)
        {
            Key = key;
            ProjectId = projectId;
            CatalogId = catalogId;
            Name = name;
            Owner = owner;
            Group = group;
            Section = section;
            SectionFrom = sectionFrom;
            ObservedLine = observedLine;
            ReportedLine = reportedLine;
            Detail = detail;
            HasCharacter = hasCharacter;
            OnYourComputer = onYourComputer;
            Place = place;
            PlaceInferred = placeInferred;
            Sample = sample;
            LastActivity = lastActivity;
        }

        /// <summary>"h:" and the Halcyonic project's id, or "c:" and the catalog's, unique in the gallery.</summary>
        public string Key { get; }

        public string? ProjectId { get; }

        public string? CatalogId { get; }

        /// <summary>The person's name for a Halcyonic project, else the catalog's, leaning.</summary>
        public LineWords Name { get; }

        /// <summary>The account or organisation the catalog's primary repository belongs to; null for a project only Halcyonic knows.</summary>
        public LineWords? Owner { get; }

        /// <summary>The group the person put it in, in the catalog; null when none.</summary>
        public LineWords? Group { get; }

        public GallerySection Section { get; }

        /// <summary>Whose state put it in <see cref="Section"/>: the more urgent of the two, this computer's on a tie.</summary>
        public GallerySectionSource SectionFrom { get; }

        /// <summary>
        /// Its tasks, as this computer observed them (<see cref="ProjectsText.Work"/>), for every Halcyonic
        /// project, kept even where the catalog's state is the more urgent; null for a catalog-only item.
        /// </summary>
        public LineWords? ObservedLine { get; }

        /// <summary>The catalog's state, as Phat Porcupine reported it, in Halcyonic's words (<see cref="GalleryText.State"/>); null without a catalog project.</summary>
        public LineWords? ReportedLine { get; }

        /// <summary>Its first line: the observed one where there is one, else the reported one.</summary>
        public LineWords Line => ObservedLine ?? ReportedLine ?? LineWords.None;

        /// <summary><see cref="Line"/> is the catalog's report, to be marked as such.</summary>
        public bool LineReported => ObservedLine == null;

        /// <summary>What the catalog says it waits on, such as a pull request's title, leaning; null when nothing.</summary>
        public LineWords? Detail { get; }

        /// <summary>A Halcyonic project with work: only those have a character, never the catalog's agents.</summary>
        public bool HasCharacter { get; }

        /// <summary>A Halcyonic project bound to a folder on the computer: the only place this client knows.</summary>
        public bool OnYourComputer { get; }

        /// <summary>
        /// Where it is, as far as this client knows: "On your computer" for a Halcyonic project with a
        /// folder; for a catalog-only item, "Not linked to a project here", or the folder the computer
        /// matched by its rule, leaning, with <see cref="PlaceInferred"/>.
        /// </summary>
        public LineWords Place { get; }

        /// <summary><see cref="Place"/> is a folder the computer matched by a rule, never confirmed by the person.</summary>
        public bool PlaceInferred { get; }

        /// <summary>From a sample or simulated catalog, never shown among real projects.</summary>
        public bool Sample { get; }

        public DateTimeOffset? LastActivity { get; }
    }

    /// <summary>
    /// The projects gallery (ADR 0039): every Halcyonic project, always, and the person's catalog when
    /// it was read, merged where the person linked them and ordered by what needs the person first.
    /// A sample catalog stays apart under its label. Built on the main thread from what the client holds.
    /// </summary>
    public sealed class Gallery
    {
        private Gallery(IReadOnlyList<GalleryItem> items, IReadOnlyList<GalleryItem> samples, string? note, string? age)
        {
            Items = items;
            Samples = samples;
            Note = note;
            Age = age;
        }

        /// <summary>Real projects, in order: by group, then those with a character, then the newest.</summary>
        public IReadOnlyList<GalleryItem> Items { get; }

        /// <summary>A sample or simulated catalog's projects, in the same order, apart from the real ones.</summary>
        public IReadOnlyList<GalleryItem> Samples { get; }

        /// <summary>Over <see cref="Samples"/>, when there are any.</summary>
        public string? SampleLabel => Samples.Count == 0 ? null : GalleryText.SampleProjects;

        /// <summary>Why the catalog isn't here, or null when it is or wasn't asked for yet.</summary>
        public string? Note { get; }

        /// <summary>How old the catalog is, when one was read.</summary>
        public string? Age { get; }

        /// <param name="catalog">The latest catalog read, or null before one came back.</param>
        /// <param name="readError">Why this headset's last read failed (<see cref="CatalogFeed.Error"/>), or null.</param>
        /// <param name="search">Words to find in a name or an owner, in any case; blank finds everything.</param>
        public static Gallery Build(
            ClientProjection state,
            WorkOverview overview,
            IntelligenceRead<CatalogResponse>? catalog,
            string? readError,
            DateTimeOffset now,
            TimeZoneInfo zone,
            string? search = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (overview == null) throw new ArgumentNullException(nameof(overview));
            var available = catalog?.Response as AvailableCatalog;
            var sample = available != null && (available.Source.Sample || available.Source.Synthetic);
            var groups = available?.Groups.ToDictionary(group => group.GroupId, group => group.Name, StringComparer.Ordinal)
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            // The first catalog project that names a Halcyonic project is the one merged with it.
            var linked = new Dictionary<string, CatalogProject>(StringComparer.Ordinal);
            if (available != null && !sample)
            {
                foreach (var project in available.Projects)
                {
                    foreach (var id in project.Local.ProjectIds)
                    {
                        if (state.Projects.ContainsKey(id) && !linked.ContainsKey(id)) linked[id] = project;
                    }
                }
            }

            var items = new List<GalleryItem>();
            var merged = new HashSet<CatalogProject>();
            foreach (var summary in overview.Projects)
            {
                if (!state.Projects.TryGetValue(summary.ProjectId, out var project)) continue;
                linked.TryGetValue(summary.ProjectId, out var partner);
                if (partner != null) merged.Add(partner);
                items.Add(OfHalcyonic(state, summary, project, partner, groups, now));
            }
            var samples = new List<GalleryItem>();
            foreach (var project in available?.Projects ?? new List<CatalogProject>())
            {
                if (merged.Contains(project)) continue;
                (sample ? samples : items).Add(OfCatalog(project, groups, sample));
            }

            var note = catalog?.Response is CatalogResponse response ? GalleryText.Why(response, catalog.Recorded) : null;
            if (readError != null) note = GalleryText.Unread(readError);
            var age = available == null ? null : GalleryText.Age(available.Sync, now, zone);
            return new Gallery(Order(Find(items, search)), Order(Find(samples, search)), note, age);
        }

        private static GalleryItem OfHalcyonic(
            ClientProjection state,
            ProjectSummary summary,
            ProjectView project,
            CatalogProject? partner,
            IReadOnlyDictionary<string, string> groups,
            DateTimeOffset now)
        {
            var times = state.Workstreams.Values
                .Where(workstream => workstream.ProjectId == project.ProjectId)
                .Select(workstream => GalleryText.Parse(workstream.UpdatedAt))
                .Append(GalleryText.Parse(project.UpdatedAt))
                .Where(time => time != null)
                .Select(time => time!.Value)
                .ToList();
            DateTimeOffset? last = times.Count == 0 ? (DateTimeOffset?)null : times.Max();
            var section = summary.NeedsYou > 0 ? GallerySection.NeedsYou
                : summary.Active > 0 ? GallerySection.WorkingNow
                : summary.Work > 0 || (last is DateTimeOffset at && now - at <= Quiet) ? GallerySection.Recent
                : GallerySection.Quiet;
            var from = GallerySectionSource.Halcyonic;
            LineWords? reported = null;
            LineWords? detail = null;
            if (partner != null)
            {
                var (theirs, theirLine) = GalleryText.State(partner.State.State);
                reported = LineWords.Ours(theirLine);
                // The more urgent of the two places it; this computer's own tasks keep their line either way.
                if (theirs < section)
                {
                    section = theirs;
                    from = GallerySectionSource.Catalog;
                }
                detail = Detail(partner);
            }
            return new GalleryItem(
                "h:" + project.ProjectId,
                project.ProjectId,
                partner?.CatalogId,
                LineWords.TheirName(project.Name),
                partner == null ? null : Owner(partner),
                partner == null ? null : Group(partner, groups),
                section,
                from,
                LineWords.Ours(ProjectsText.Work(summary)),
                reported,
                detail,
                hasCharacter: summary.Work > 0,
                onYourComputer: project.Location != null,
                LineWords.Ours(project.Location != null ? GalleryText.OnYourComputer : GalleryText.NoFolder),
                placeInferred: false,
                sample: false,
                last);
        }

        private static GalleryItem OfCatalog(CatalogProject project, IReadOnlyDictionary<string, string> groups, bool sample)
        {
            var (section, line) = GalleryText.State(project.State.State);
            var suggestion = project.Local.Suggestions.FirstOrDefault();
            var place = suggestion == null
                ? LineWords.Ours(GalleryText.NotLinked)
                : LineWords.Ours(GalleryText.MaybeOnYourComputer) + LineWords.TheirName(suggestion.FolderName);
            return new GalleryItem(
                "c:" + project.CatalogId,
                null,
                project.CatalogId,
                LineWords.TheirName(project.Name),
                Owner(project),
                Group(project, groups),
                section,
                GallerySectionSource.Catalog,
                null,
                LineWords.Ours(line),
                Detail(project),
                hasCharacter: false,
                onYourComputer: false,
                place,
                placeInferred: suggestion != null,
                sample,
                GalleryText.Parse(project.State.LastActivityAt));
        }

        /// <summary>A Halcyonic project with no work, untouched this long, is quiet.</summary>
        private static readonly TimeSpan Quiet = TimeSpan.FromDays(30);

        private static LineWords? Owner(CatalogProject project)
        {
            var primary = project.Repositories.FirstOrDefault(repository => repository.Primary) ?? project.Repositories.FirstOrDefault();
            var slash = primary?.FullName.IndexOf('/') ?? -1;
            return slash > 0 ? LineWords.TheirName(primary!.FullName.Substring(0, slash)) : null;
        }

        private static LineWords? Group(CatalogProject project, IReadOnlyDictionary<string, string> groups) =>
            project.GroupId != null && groups.TryGetValue(project.GroupId, out var name) ? LineWords.TheirName(name) : null;

        private static LineWords? Detail(CatalogProject project)
        {
            var title = project.Attention.FirstOrDefault(attention => attention.PullRequest != null)?.PullRequest?.Title;
            return title == null ? null : LineWords.Theirs(title);
        }

        private static IEnumerable<GalleryItem> Find(IEnumerable<GalleryItem> items, string? search)
        {
            var words = search?.Trim();
            if (string.IsNullOrEmpty(words)) return items;
            return items.Where(item =>
                item.Name.Text.IndexOf(words, StringComparison.OrdinalIgnoreCase) >= 0
                || (item.Owner?.Text.IndexOf(words, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
        }

        private static IReadOnlyList<GalleryItem> Order(IEnumerable<GalleryItem> items) => items
            .OrderBy(item => item.Section)
            .ThenByDescending(item => item.HasCharacter)
            .ThenByDescending(item => item.LastActivity ?? DateTimeOffset.MinValue)
            .ThenBy(item => item.Name.Text, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Key, StringComparer.Ordinal)
            .ToList();
    }
}
