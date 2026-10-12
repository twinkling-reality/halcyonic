#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>A category index over current supported settings. A leaf owns its own changes and rollback.</summary>
    public sealed class SettingsHubColumn : IMenuColumn
    {
        public const string OpenCategory = "settings-open-category";
        public const string Back = "settings-back";
        public const string Appearance = "Appearance";
        private readonly IMenuHost host;
        private readonly IReadOnlyList<MenuSetting> settings;
        private readonly AppearanceSettings appearance;
        private readonly Action appearanceSaved;
        private IMenuColumn? leaf;
        private int page;
        private string? given;

        public SettingsHubColumn(IMenuHost host, IReadOnlyList<MenuSetting> settings, AppearanceSettings appearance, Action appearanceSaved)
        {
            this.host = host;
            this.settings = settings;
            this.appearance = appearance;
            this.appearanceSaved = appearanceSaved;
        }

        public event Action? Changed;
        public event Action? Closed;

        private IReadOnlyList<string> Categories => new[] { Appearance, Comfort.Heading, SettingsText.YourSpace, SettingsText.YourMac }
            .Where(group => group == Appearance || settings.Any(setting => setting.Group == group)).ToList();

        public MenuFrame? Frame
        {
            get
            {
                if (leaf?.Frame is MenuFrame frame)
                {
                    return frame.Footer[PromptSlot.Rare] == null && frame.Footer.All.Count() < 3
                        ? frame.With(frame.Footer.With(PromptSlot.Rare, new Prompt(Back, "Back", GlazeIcon.Back)), frame.Lines, frame.Side)
                        : frame;
                }
                var categories = Categories;
                var each = Math.Max(1, host.PageRows(sourceLine: false));
                var pages = Math.Max(1, (categories.Count + each - 1) / each);
                if (page >= pages) page = 0;
                given = Summary();
                var footer = new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close));
                if (pages > 1) footer = footer.WithNext(new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages), GlazeIcon.Next, PromptKind.NextPage));
                var lines = categories.Skip(page * each).Take(each).Select(group => new PageLine(LineWords.Ours(group),
                    fact: group == Appearance ? LineWords.Ours(appearance.Applied.Name) : settings.First(setting => setting.Group == group).Read().ValueRuns,
                    action: OpenCategory, key: group, opens: true)).ToList();
                return new MenuFrame(SettingsText.Subject, footer, lines: lines);
            }
        }

        public void Act(string id, string? key)
        {
            if (id == Footer.Close) { Leave(); Closed?.Invoke(); return; }
            if (id == Back && leaf != null) { Leave(); Changed?.Invoke(); return; }
            if (leaf != null) { leaf.Tick(); leaf.Act(id, key); return; }
            if (id == OpenCategory && key != null && Categories.Contains(key))
            {
                leaf = key == Appearance ? (IMenuColumn)new AppearanceColumn(appearance, appearanceSaved)
                    : new SettingsColumn(host, settings.Where(setting => setting.Group == key).ToList());
                leaf.Changed += LeafChanged;
                leaf.Closed += LeafClosed;
                Changed?.Invoke();
            }
            else if (id == Footer.NextPage) { page++; Changed?.Invoke(); }
        }

        private void LeafChanged() => Changed?.Invoke();
        private void LeafClosed() { Leave(); Closed?.Invoke(); }

        private void Leave()
        {
            var leaving = leaf;
            leaf = null;
            if (leaving == null) return;
            leaving.Changed -= LeafChanged;
            leaving.Closed -= LeafClosed;
            leaving.FocusLeft();
        }

        private string Summary() => appearance.Applied.Key + string.Join("\u0001", settings.Select(setting => setting.Read().Shown));
        public void Tick()
        {
            if (leaf != null) { leaf.Tick(); return; }
            if (given != null && given != Summary()) Changed?.Invoke();
        }
        public void FocusLeft() => leaf?.FocusLeft();
        public void Drawn(MenuFrame drawn, Footer? sidePanel) => leaf?.Drawn(drawn, sidePanel);
        public void HoldStarted(string id) => leaf?.HoldStarted(id);
        public void HoldEnded(string id, bool letGo) => leaf?.HoldEnded(id, letGo);
        public void Heard(string text) => leaf?.Heard(text);
        public void Said(string words) => leaf?.Said(words);
    }
}
