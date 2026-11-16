#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>Owns the temporary preview. Every departure restores the applied palette; only Apply or Restore saves.</summary>
    public sealed class AppearanceColumn : IMenuColumn
    {
        public const string Choose = "appearance-choose";
        public const string Preview = "appearance-preview";
        public const string Apply = "appearance-apply";
        public const string Cancel = "appearance-cancel";
        public const string ReadRestore = "appearance-read-restore";
        public const string Restore = "appearance-restore";

        /// <summary>Under the redesign, a panel theme chosen (its key "dark" or "light"): applied, kept and in force at once.</summary>
        public const string ChoosePanels = "appearance-panels";
        private const string Example = "appearance-example";
        private readonly AppearanceSettings settings;
        private readonly Action saved;
        private GlazePalette chosen;
        private bool preview;
        private bool restoring;
        private int page;

        public AppearanceColumn(AppearanceSettings settings, Action saved)
        {
            this.settings = settings;
            this.saved = saved;
            chosen = settings.Applied;
        }

        public event Action? Changed;
        public event Action? Closed;

        public MenuFrame? Frame
        {
            get
            {
                var close = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);
                // The redesign's glass (the lead's 13:33Z): Dark and Light are the page's choice, the one in force chosen; choosing one
                // applies it at once, so there is no preview or apply step for two looks a person sees by choosing.
                if (RoomDesign.Enabled && PanelThemes.Offered)
                {
                    var panels = new[] { PanelTheme.Dark, PanelTheme.Light }.Select(theme => new PageLine(PanelThemes.Words(theme),
                        action: ChoosePanels, key: PanelThemes.Key(theme), choice: true, chosen: theme == settings.Panels, answerMark: AnswerMark.Single)).ToList();
                    return new MenuFrame("Appearance", new Footer(close), lines: panels);
                }
                if (restoring)
                {
                    return new MenuFrame("Restore Default?", new Footer(close,
                        rare: new Prompt(Cancel, "Cancel", GlazeIcon.Close),
                        farRight: new Prompt(Restore, "Restore Default", GlazeIcon.ResetPosition, main: true)),
                        lines: new[] { new PageLine(GlazePalette.Default.Name + " Interface"), new PageLine("Your comfort and space settings are kept.", rows: 2) });
                }
                if (!preview)
                {
                    var lines = GlazePalette.Presets.Select(palette => new PageLine(palette.Name, fact: palette == settings.Applied ? "Current" : null,
                        action: Choose, key: palette.Key, choice: true, chosen: palette == chosen, answerMark: AnswerMark.Single)).ToList();
                    lines.Add(new PageLine("Restore Default", action: ReadRestore, opens: true));
                    return new MenuFrame("Appearance", new Footer(close,
                        farRight: new Prompt(Preview, "Preview Theme", GlazeIcon.OpenNow, main: true)), lines: lines);
                }
                var footer = new Footer(close, rare: new Prompt(Cancel, "Cancel", GlazeIcon.Close),
                    farRight: new Prompt(Apply, "Apply Theme", GlazeIcon.Change, main: true));
                // The button says only where it goes, as every pager does (Footer.NextPageWords): a count on it was misread (M2).
                var examples = Specimen(page).ToList();
                examples.Add(new PageLine(page == 4 ? "First Examples" : "Next Examples",
                    action: Footer.NextPage, opens: true, control: LineControl.Action));
                return new MenuFrame("Preview: " + chosen.Name, footer, lines: examples);
            }
        }

        /// <summary>Actual navigation/choice/input/provenance rendering, with inert example controls and no commands.</summary>
        private static IReadOnlyList<PageLine> Specimen(int page) => page switch
        {
            0 => new[]
            {
                new PageLine("Navigation Example", action: Example, opens: true),
            },
            1 => new[]
            {
                new PageLine("Selected Choice", action: Example, choice: true, chosen: true, answerMark: AnswerMark.Single),
                new PageLine("Other Choice", action: Example, choice: true, answerMark: AnswerMark.Single),
            },
            2 => new[]
            {
                new PageLine("Your Answer", tone: LineTone.Secondary),
                new PageLine("Input Example", action: Example, control: LineControl.Input, inputFocused: true),
            },
            3 => new[]
            {
                new PageLine("Unavailable Action", action: Example, control: LineControl.Action, available: false),
                new PageLine(StateLanguage.WordOf(WorkState.WaitingForYou), icon: GlazeIcon.WaitingForYou, tone: LineTone.Waiting),
            },
            _ => new[]
            {
                new PageLine(StateLanguage.WordOf(WorkState.FinishedThisRound), icon: GlazeIcon.FinishedThisRound, tone: LineTone.Good),
                new PageLine(LineWords.Theirs("The example is ready."), chip: SectionLine.AgentChip),
            },
        };

        public void Act(string id, string? key)
        {
            switch (id)
            {
                case ChoosePanels when RoomDesign.Enabled && PanelThemes.Offered && PanelThemes.Find(key) is PanelTheme theme:
                    if (theme == settings.Panels) return;
                    settings.ApplyPanels(theme);
                    PanelThemes.Set(theme);
                    saved();
                    break;
                case Choose when !preview && !restoring && key != null && GlazePalette.Find(key) is GlazePalette palette:
                    chosen = palette;
                    break;
                case Preview when !preview && !restoring:
                    preview = true;
                    page = 0;
                    Glaze.SetPalette(chosen);
                    break;
                case Apply when preview:
                    settings.Apply(chosen);
                    Glaze.SetPalette(settings.Applied);
                    saved();
                    preview = false;
                    break;
                case Cancel:
                    Rollback();
                    restoring = false;
                    break;
                case ReadRestore when !preview:
                    restoring = true;
                    break;
                case Restore when restoring:
                    settings.Apply(GlazePalette.Default);
                    chosen = settings.Applied;
                    restoring = false;
                    Glaze.SetPalette(settings.Applied);
                    saved();
                    break;
                case Footer.NextPage when preview:
                    page = (page + 1) % 5;
                    break;
                case Footer.Close:
                    FocusLeft();
                    Closed?.Invoke();
                    return;
                default: return;
            }
            Changed?.Invoke();
        }

        private void Rollback()
        {
            preview = false;
            chosen = settings.Applied;
            Glaze.SetPalette(settings.Applied);
        }

        public void FocusLeft() { Rollback(); restoring = false; Changed?.Invoke(); }
        public void Drawn(MenuFrame drawn, Footer? sidePanel) { }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string text) { }
        public void Said(string words) { }
        public void Tick() { }
    }
}
