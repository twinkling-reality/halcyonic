#nullable enable
using System;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>A local preview of the deliberately shown task character; no runtime commands.</summary>
    public sealed class CharacterColourColumn : IMenuColumn, IPlacesInRoom
    {
        public const string Choose = "character-colour-choose", Preview = "character-colour-preview", Apply = "character-colour-apply", Cancel = "character-colour-cancel";
        private readonly CharacterColourSettings settings;
        private readonly CharacterColourScope scope;
        private readonly Func<bool> current;
        private readonly Func<string, bool> persist;
        private readonly object owner = new object();
        private string chosen;
        private bool preview;
        private bool ended;

        /// <inheritdoc/>
        public bool ShelfApplied => false;
        private int page;
        private string? error;
        private MenuFrame? frame, drawn;
        public CharacterColourColumn(CharacterColourSettings settings, CharacterColourScope scope, Func<bool> current, Func<string, bool> persist)
        { this.settings = settings; this.scope = scope; this.current = current; this.persist = persist; chosen = settings.Applied(scope); }
        public event Action? Changed;
        public event Action? Closed;
        public MenuFrame? Frame => ended ? null : frame ??= new MenuFrame(preview ? "Preview Colour" : FileScreens.CharacterColourWords, new Footer(
            new Prompt(Footer.Close, "Back to Task", GlazeIcon.Back, PromptKind.Close),
            rare: preview ? new Prompt(Cancel, "Cancel", GlazeIcon.Close) : null,
            // Under the redesign the general reader pages the colours, its pager saying the page and the count (SCREENS's 22:01Z critic).
            secondary: !preview && !RoomDesign.Enabled ? new Prompt(Footer.NextPage, Footer.NextPageWords(page, 2), GlazeIcon.Next, PromptKind.NextPage) : null,
            farRight: new Prompt(preview ? Apply : Preview, preview ? "Apply Colour" : "Preview Colour", preview ? GlazeIcon.Change : GlazeIcon.Next, main: true)),
            lines: preview ? new[] { new PageLine("Look at Task's Character", rows: 2), new PageLine("This Headset Only", tone: LineTone.Secondary),
                new PageLine(error ?? (chosen == "original" ? "Original Colour" : CharacterColourSettings.Name(chosen))) }
                : CharacterColourSettings.Choices.Skip(RoomDesign.Enabled ? 0 : page * 3).Take(RoomDesign.Enabled ? CharacterColourSettings.Choices.Count : 3).Select(choice => new PageLine(CharacterColourSettings.Name(choice), fact: choice == settings.Applied(scope) ? "Current" : null,
                    action: Choose, key: choice, choice: true, chosen: chosen == choice, answerMark: AnswerMark.Single)
                    // The colours a person compares stand together (the lead's 21:11Z): one group, which the reader never breaks; each
                    // shows the colour it names on this task's character (SCREENS's 22:03Z swatch).
                    .WithGroup("colours").WithSwatch(CharacterColourSettings.Swatch(choice, CharacterIdentity.Of(scope.Task)))).ToArray());
        private void Invalidate() { frame = drawn = null; Changed?.Invoke(); }
        public void Act(string id, string? key)
        {
            if (ended) return;
            if (!current()) { Leave(); return; }
            var exact = Frame;
            if (drawn != exact) return;
            if (id == Footer.Close) { Leave(); return; }
            if (id == Choose && !preview && key != null && exact!.Lines.Any(line => line.Action == Choose && line.Key == key)) chosen = key;
            else if (id == Footer.NextPage && !preview) page = (page + 1) % 2;
            else if (id == Preview && !preview) { preview = true; settings.Preview(owner, scope, chosen); }
            else if (id == Cancel && preview) { settings.Cancel(owner); preview = false; chosen = settings.Applied(scope); error = null; }
            else if (id == Apply && preview)
            {
                if (!settings.Apply(owner, scope, chosen, persist)) error = "The colour wasn't saved. Cancel or Retry.";
                else { preview = false; error = null; chosen = settings.Applied(scope); }
            }
            else return;
            Invalidate();
        }
        private void Leave() { if (ended) return; ended = true; settings.Cancel(owner); preview = false; Invalidate(); Closed?.Invoke(); }
        public void FocusLeft() { ended = true; settings.Cancel(owner); preview = false; chosen = settings.Applied(scope); error = null; Invalidate(); }
        public void Drawn(MenuFrame given, Footer? sidePanel) { if (!ended && sidePanel == null && ReferenceEquals(given, Frame) && current()) drawn = given; }
        public void Tick() { if (!ended && !current()) Leave(); }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string text) { }
        public void Said(string words) { }
    }
}
