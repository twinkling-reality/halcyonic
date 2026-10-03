#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>How a setting stands now, read each time Settings is drawn.</summary>
    public sealed class SettingNow
    {
        /// <param name="value">Its value, the row's small fact, as "Standard".</param>
        /// <param name="now">What it is now, in a sentence's words, the side panel's first fact, as "The standard size".</param>
        /// <param name="next">The change its one prompt makes, named, as "A step larger", and what that does, as "Text 15 percent larger, and 3 rows a page".</param>
        /// <param name="prompt">The prompt's words, as "Make text larger": the footer's main action while the setting is chosen.</param>
        /// <param name="reason">Why the change can't be made now; null when it can.</param>
        public SettingNow(string value, string now, string next, string does, string prompt, string? reason = null)
        {
            Value = value;
            Now = now;
            Next = next;
            Does = does;
            Prompt = prompt;
            Reason = reason;
        }

        public string Value { get; }

        public string Now { get; }

        public string Next { get; }

        public string Does { get; }

        public string Prompt { get; }

        public string? Reason { get; }
    }

    /// <summary>
    /// One setting on Settings' page (ADR 0026): a row under its group's heading, its value the small
    /// fact; chosen, its side panel says what it is now and what its one change does, and that change
    /// is the footer's main action. A setting of more than two values steps to the next, its prompt
    /// naming it. Whoever owns what it sets gives it: the comfort settings (<see cref="ComfortSettings"/>),
    /// the room and the stage's arrangement, and the menu's position.
    /// </summary>
    public sealed class MenuSetting
    {
        public MenuSetting(string key, string group, string name, Func<SettingNow> read, Action change)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A setting has a key.", nameof(key));
            Key = key;
            Group = group;
            Name = name;
            Read = read;
            Change = change;
        }

        public string Key { get; }

        /// <summary>The heading it stands under, as "Your space" or "Comfort".</summary>
        public string Group { get; }

        /// <summary>Its row's words, as "Text size".</summary>
        public string Name { get; }

        public Func<SettingNow> Read { get; }

        /// <summary>Makes its one change; the owner keeps it.</summary>
        public Action Change { get; }
    }

    /// <summary>
    /// Settings, the menu's last place (ADR 0026): each setting a row under its group's heading, a page
    /// a group, its heading counting as a row; chosen, its side panel says what it is now and what its
    /// change does, and the footer offers that change as its main action, while paging waits. Settings
    /// sends nothing: each change is its owner's, on this device.
    /// </summary>
    public sealed class SettingsColumn : IMenuColumn
    {
        /// <summary>What pressing a setting's row raises, with its key.</summary>
        public const string OpenSetting = "settings-open-setting";

        /// <summary>What the chosen setting's change raises.</summary>
        public const string ChangeSetting = "settings-change";

        private readonly IMenuHost host;
        private readonly IReadOnlyList<MenuSetting> settings;
        private string? chosen;
        private int page;

        public SettingsColumn(IMenuHost host, IReadOnlyList<MenuSetting> settings)
        {
            if (settings.Select(setting => setting.Key).Distinct().Count() != settings.Count) throw new ArgumentException("Each setting has its own key.", nameof(settings));
            this.host = host;
            this.settings = settings;
        }

        public event Action? Changed;

        public event Action? Closed;

        public MenuFrame? Frame
        {
            get
            {
                var pages = Pages();
                if (chosen != null)
                {
                    var holding = pages.FindIndex(each => each.Rows.Any(setting => setting.Key == chosen));
                    if (holding < 0) chosen = null;
                    else page = holding;
                }
                if (page >= pages.Count) page = 0;
                var close = new Prompt(Footer.Close, SettingsText.Close, GlazeIcon.Close, PromptKind.Close);
                if (pages.Count == 0) return new MenuFrame(SettingsText.Subject, new Footer(close));

                var (group, rows) = pages[page];
                var lines = new List<PageLine> { new PageLine(group, tone: LineTone.Secondary) };
                lines.AddRange(rows.Select(setting => new PageLine(setting.Name, fact: setting.Read().Value, action: OpenSetting, key: setting.Key, opens: true,
                    chosen: setting.Key == chosen)));
                var footer = new Footer(close);
                SidePanel? side = null;
                if (chosen != null && settings.FirstOrDefault(setting => setting.Key == chosen) is MenuSetting open)
                {
                    var now = open.Read();
                    footer = new Footer(close, farRight: new Prompt(ChangeSetting, now.Prompt, GlazeIcon.Change, main: true, available: now.Reason == null, reason: now.Reason));
                    side = new SidePanel(open.Name, facts: new[] { new SideFact(SettingsText.Now, now.Now), new SideFact(now.Next, now.Does) });
                }
                // Paging waits while a setting is chosen.
                else if (pages.Count > 1)
                {
                    footer = footer.WithNext(new Prompt(Footer.NextPage, Footer.NextPageWords(page, pages.Count), GlazeIcon.Next, PromptKind.NextPage));
                }
                return new MenuFrame(SettingsText.Subject, footer, lines: lines, side: side);
            }
        }

        public void Act(string id, string? key)
        {
            switch (id)
            {
                case OpenSetting when key != null && settings.Any(setting => setting.Key == key):
                    chosen = chosen == key ? null : key;
                    Changed?.Invoke();
                    break;
                case ChangeSetting when chosen != null && settings.FirstOrDefault(setting => setting.Key == chosen) is MenuSetting open && open.Read().Reason == null:
                    open.Change();
                    Changed?.Invoke();
                    break;
                case SidePanel.Close when chosen != null:
                    chosen = null;
                    Changed?.Invoke();
                    break;
                case Footer.NextPage when chosen == null:
                    page++;
                    Changed?.Invoke();
                    break;
                case Footer.Close:
                    Closed?.Invoke();
                    break;
            }
        }

        public void Drawn(MenuFrame drawn, bool sidePanel)
        {
        }

        public void HoldStarted(string id)
        {
        }

        public void HoldEnded(string id, bool letGo)
        {
        }

        public void Heard(string text)
        {
        }

        public void Said(string words)
        {
        }

        public void Tick()
        {
        }

        public void FocusLeft()
        {
        }

        /// <summary>A page a group, its heading counting as one of the page's rows, split where a group holds more.</summary>
        private List<(string Group, IReadOnlyList<MenuSetting> Rows)> Pages()
        {
            var each = Math.Max(1, host.PageRows(sourceLine: false) - 1);
            var pages = new List<(string, IReadOnlyList<MenuSetting>)>();
            foreach (var group in settings.GroupBy(setting => setting.Group))
            {
                var rows = group.ToList();
                for (var start = 0; start < rows.Count; start += each) pages.Add((group.Key, rows.Skip(start).Take(each).ToList()));
            }
            return pages;
        }
    }

    /// <summary>The comfort settings (ADR 0023) as Settings' rows, under Comfort: the text's size, moving badges and sounds.</summary>
    public static class ComfortSettings
    {
        /// <param name="saved">The settings changed: the device keeps them, and the stage takes them.</param>
        public static IReadOnlyList<MenuSetting> Of(Comfort comfort, Action saved) => new[]
        {
            new MenuSetting("text-size", Comfort.Heading, "Text size", () => comfort.Text == TextSize.Larger
                    ? new SettingNow("A step larger", "A step larger", "The standard size", "Text at its standard size, and 4 rows a page", comfort.TextButton)
                    : new SettingNow("Standard", "The standard size", "A step larger", "Text 15 percent larger, and 3 rows a page", comfort.TextButton),
                () =>
                {
                    comfort.Text = comfort.Text == TextSize.Larger ? TextSize.Standard : TextSize.Larger;
                    saved();
                }),
            new MenuSetting("moving-badges", Comfort.Heading, "Moving badges", () => comfort.Still
                    ? new SettingNow("Off", "Badges stand still", "On", "Working's icon turns and Waiting for you breathes", comfort.MotionButton)
                    : new SettingNow("On", "Badges move", "Off", "Badges stand still: no icon turns and nothing breathes", comfort.MotionButton),
                () =>
                {
                    comfort.Still = !comfort.Still;
                    saved();
                }),
            new MenuSetting("sounds", Comfort.Heading, "Sounds", () => new SettingNow(Level(comfort.Sounds), Level(comfort.Sounds), Level(comfort.NextSounds),
                    comfort.NextSounds switch
                    {
                        SoundLevel.Quieter => "Sounds at half their loudness",
                        SoundLevel.Off => "No sounds",
                        _ => "Sounds at their usual loudness",
                    }, comfort.SoundButton),
                () =>
                {
                    comfort.Sounds = comfort.NextSounds;
                    saved();
                }),
        };

        private static string Level(SoundLevel level) => level switch
        {
            SoundLevel.On => "On",
            SoundLevel.Quieter => "Quieter",
            _ => "Off",
        };
    }
}
