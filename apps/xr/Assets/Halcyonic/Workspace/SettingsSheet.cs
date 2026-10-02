#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// The Settings sheet the rail opens (ADR 0023): one foreground panel at touch distance with the
    /// controls that change how Halcyonic is arranged rather than act on work, the room's and, in
    /// development builds, pairing with the Mac. Each of those adds its own section
    /// (<see cref="SettingsSection"/>), so this knows nothing of rooms or pairing; their news, said in a
    /// section's line, also shows as a short notice on the stage's banner while the sheet is closed.
    /// </summary>
    /// <remarks>
    /// It opens only from the rail, which shows only while no other foreground panel does, and it
    /// closes when the entry panel or a workspace opens: one foreground surface at a time. It opens
    /// where the entry panel would, clear of every character and label, folds while another window
    /// keeps focus, and its buttons ignore input while the app lacks focus.
    /// </remarks>
    public sealed class SettingsSheet : MonoBehaviour
    {
        /// <summary>From the eyes to the sheet, at touch distance (ADR 0023).</summary>
        public const float Distance = 0.46f;

        /// <summary>
        /// The sheet's width, every foreground panel's 44 degrees (ADR 0023): room for each section's
        /// buttons in one row and its line in few rows, which keeps it short; and the least height it
        /// keeps. With every section it stands taller than a panel, and opens lower to stay under every
        /// label (<see cref="WorkspacePlacement.Lowest"/>).
        /// </summary>
        public const float WidthDegrees = 44f;

        public const float MinHeightDegrees = 18f;

        private const float PaddingDegrees = 1.25f;
        /// <summary>Between sections, as between a panel's parts (<see cref="PanelFrame"/>): words above, a heading below.</summary>
        private const float SectionGapDegrees = 0.75f;
        private const float LineGapDegrees = 0.25f;
        private const int LineMaxLines = 2;

        private readonly List<SettingsSection> sections = new List<SettingsSection>();
        private readonly List<BodyInView> scratch = new List<BodyInView>();
        private Transform root = null!;
        private Transform content = null!;
        private Surface plate = null!;
        private PointerTarget background = null!;
        private TextMeshPro title = null!;
        private GlazeButton close = null!;
        private EntryPanel? entry;
        private WorkspaceDirector? director;
        private CharacterStage? stage;
        private bool open;
        private bool changed;
        private bool built;
        private Vector2 size;

        /// <summary>The sheet is open: the rail steps aside meanwhile.</summary>
        public bool Open => open;

        /// <summary>The sheet's root, for the editor's renders.</summary>
        public Transform Root
        {
            get
            {
                Build();
                return root;
            }
        }

        /// <summary>The sheet's size, in units of its distance.</summary>
        public Vector2 Size => size;

        /// <summary>The sheet's size as placement takes it: grown whole with the reading text's step, as every foreground panel is (<see cref="PanelFrame.Zoom"/>).</summary>
        public PanelSize PanelSize => new PanelSize(Distance, size.x / 2f * Scale, size.y / 2f * Scale);

        private static float Scale => Distance * PanelFrame.Zoom;

        /// <summary>The sections in their order, for the editor's renders.</summary>
        public IReadOnlyList<SettingsSection> Sections => sections;

        /// <summary>The sheet on the stage's object, made the first time anything asks for it.</summary>
        public static SettingsSheet On(GameObject stageObject)
        {
            var sheet = stageObject.GetComponent<SettingsSheet>();
            return sheet != null ? sheet : stageObject.AddComponent<SettingsSheet>();
        }

        /// <summary>A section of the sheet, made the first time it is asked for, its place by <paramref name="order"/>, from the top.</summary>
        public SettingsSection Section(string heading, int order)
        {
            Build();
            foreach (var section in sections)
            {
                if (section.Heading == heading) return section;
            }
            var made = new SettingsSection(this, heading, order, continues: false);
            sections.Add(made);
            Sort();
            changed = true;
            return made;
        }

        /// <summary>
        /// A part of <paramref name="of"/> under its buttons, with a line and buttons of its own but no
        /// heading, as where the characters stand is part of Your room: closer to it than a section is
        /// to the one before. Made once; asked for again, the same part.
        /// </summary>
        public SettingsSection Continuation(SettingsSection of)
        {
            Build();
            foreach (var section in sections)
            {
                if (section.Continues && section.Order == of.Order) return section;
            }
            var made = new SettingsSection(this, "", of.Order, continues: true);
            sections.Add(made);
            Sort();
            changed = true;
            return made;
        }

        /// <summary>By order from the top, each continuation right after the section it continues.</summary>
        private void Sort() => sections.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.Continues.CompareTo(b.Continues));

        /// <summary>Opens the sheet where the entry panel would open, or closes it when open.</summary>
        public void Toggle()
        {
            if (FocusGuard.InputSuspended) return;
            if (open)
            {
                Close();
                return;
            }
            open = true;
            Place(director != null ? director.Targets : Array.Empty<CharacterTarget>(), stage != null ? stage.SurfaceHeight : null);
        }

        public void Close()
        {
            open = false;
            if (built) root.gameObject.SetActive(false);
        }

        /// <summary>For the editor's renders: opens the sheet among <paramref name="characters"/>, as the rail would.</summary>
        public void OpenForRender(IEnumerable<CharacterTarget> characters, float? surfaceHeight)
        {
            Build();
            open = true;
            Place(characters, surfaceHeight);
        }

        /// <summary>Where sections put their parts: laid out down from the sheet's top edge.</summary>
        internal Transform Body => content;

        /// <summary>A section said something or offered other buttons: laid out again on the next frame, or now while open.</summary>
        internal void Changed()
        {
            changed = true;
            if (open) Layout();
        }

        private void Awake()
        {
            entry = GetComponent<EntryPanel>();
            director = GetComponent<WorkspaceDirector>();
            stage = GetComponent<CharacterStage>();
            Build();
            GlazeText.ScaleChanged += OnScaleChanged;
        }

        private void OnDestroy() => GlazeText.ScaleChanged -= OnScaleChanged;

        /// <summary>The text's size changed, from this sheet's own Comfort section: the sheet grows with it where it opens.</summary>
        private void OnScaleChanged()
        {
            if (open) Place(director != null ? director.Targets : Array.Empty<CharacterTarget>(), stage != null ? stage.SurfaceHeight : null);
        }

        private void Build()
        {
            if (built) return;
            built = true;
            root = new GameObject("Settings sheet").transform;
            root.SetParent(transform, false);
            // The stage's banner steps aside while the sheet shows where it goes, and names it while it is folded.
            AmbientCover.Add(root.gameObject, panel: true, () => open ? SettingsText.Settings : null);
            plate = Surface.Create(root, "Background", 10);
            // The background takes the ray, so nothing behind the sheet is pointed at through it.
            background = PointerTarget.Rectangle(root.gameObject, Vector2.one * 0.1f, ray: true, poke: false);
            content = new GameObject("Content").transform;
            content.SetParent(root, false);
            title = GlazeText.Create(content, "Title", GlazeType.Display, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 12);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            GlazeText.SetLiteral(title, SettingsText.Settings);
            close = GlazeButton.Create(content, "Close", ButtonRole.Secondary, compact: true);
            close.Accepting = () => !FocusGuard.InputSuspended;
            close.Pressed += Close;
            root.gameObject.SetActive(false);
        }

        private void Update()
        {
            // TryGetComponent, unlike GetComponent, allocates nothing for a component that is not there, each frame.
            if (entry == null) TryGetComponent(out entry);
            // One foreground surface at a time: the entry panel and a workspace open where the sheet is.
            if (open && ((entry != null && entry.Visible) || (director != null && director.OpenWorkstream != null))) Close();
            var shows = open && !FocusGuard.Folded;
            if (root.gameObject.activeSelf != shows) root.gameObject.SetActive(shows);
            if (changed && open) Layout();
        }

        /// <summary>Where the entry panel would open: clear of every character and label, in the comfortable band, facing the eyes.</summary>
        private void Place(IEnumerable<CharacterTarget> characters, float? surfaceHeight)
        {
            var eyes = WorkspaceVisuals.HeadPosition;
            var looking = WorkspaceVisuals.Head != null ? WorkspaceVisuals.Head.forward : Vector3.forward;
            // Laid out first, showing, so it is placed at the height it has.
            root.gameObject.SetActive(true);
            Layout();
            var (pose, _) = WorkspaceLayout.PlaceForeground(characters, eyes, looking, surfaceHeight, scratch, PanelSize);
            root.SetPositionAndRotation(pose.position, pose.rotation);
            root.localScale = Vector3.one * Scale;
        }

        /// <summary>
        /// The title and Close along the top, then each section from the top: its heading, its line
        /// and its buttons in rows, as wide as the sheet lets them.
        /// </summary>
        private void Layout()
        {
            changed = false;
            var width = 2f * GlazeTokens.Units(WidthDegrees / 2f);
            var padding = GlazeTokens.Units(PaddingDegrees);
            var inner = width - 2f * padding;
            var gap = Glaze.TargetGapMeters / Distance;
            var left = -width / 2f + padding;
            var y = -padding;

            // The title, and Close at the right end of its row.
            var closeWidth = close.Measure(SettingsText.Close, null, GlazeIcon.Close);
            var rowHeight = GlazeButton.HeightOf(true);
            close.Show(SettingsText.Close, new Vector2(width / 2f - padding - closeWidth / 2f, y - rowHeight / 2f), closeWidth, withIcon: GlazeIcon.Close);
            GlazeText.Lay(title, inner - closeWidth - gap, 1);
            var titleLine = GlazeText.LineHeight(title);
            title.transform.localPosition = new Vector3(left, y - (rowHeight - titleLine) / 2f, -0.0005f);
            y -= rowHeight;

            foreach (var section in sections) y = section.Layout(left, inner, y - (section.Continues ? LineGap : GlazeTokens.Units(SectionGapDegrees)), gap);

            var height = Mathf.Max(-y + padding, 2f * GlazeTokens.Units(MinHeightDegrees / 2f));
            size = new Vector2(width, height);
            // The parts were laid out down from the top edge; the plate centres on the root.
            content.localPosition = new Vector3(0f, height / 2f, 0f);
            plate.transform.localPosition = new Vector3(0f, 0f, GlazeTokens.Units(0.05f));
            plate.Draw(size, GlazeTokens.Units(Glaze.PanelRadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel));
            background.Resize(size);
            if (open) root.gameObject.SetActive(!FocusGuard.Folded);
        }

        /// <summary>For a section: lays out a line of text left-aligned at <paramref name="top"/>; returns where the next part starts.</summary>
        internal static float LayLine(TextMeshPro label, string text, float left, float width, float top, int maxLines)
        {
            label.gameObject.SetActive(text.Length > 0);
            if (text.Length == 0) return top;
            GlazeText.SetLiteral(label, text);
            var (lines, _) = GlazeText.Lay(label, width, maxLines);
            label.transform.localPosition = new Vector3(left, top, -0.0005f);
            return top - lines * GlazeText.LineHeight(label);
        }

        internal static float LineGap => GlazeTokens.Units(LineGapDegrees);

        internal static int MaxLines => LineMaxLines;
    }

    /// <summary>
    /// A part of the Settings sheet that a feature adds for itself (<see cref="SettingsSheet.Section"/>):
    /// a heading, a line saying how things stand, and the buttons that change them, each shown with
    /// its label while offered and hidden otherwise.
    /// </summary>
    public sealed class SettingsSection
    {
        private readonly List<GlazeButton> buttons = new List<GlazeButton>();
        private readonly Dictionary<GlazeButton, string?> labels = new Dictionary<GlazeButton, string?>();
        private readonly SettingsSheet sheet;
        private readonly TextMeshPro heading;
        private readonly TextMeshPro line;

        internal SettingsSection(SettingsSheet sheet, string headingText, int order, bool continues)
        {
            this.sheet = sheet;
            Heading = headingText;
            Order = order;
            Continues = continues;
            heading = GlazeText.Create(sheet.Body, "Heading " + headingText, GlazeType.Caption, GlazeTokens.TextSecondary, TextAlignmentOptions.TopLeft, 12, strong: true);
            heading.rectTransform.pivot = new Vector2(0f, 1f);
            GlazeText.SetLiteral(heading, headingText);
            line = GlazeText.Create(sheet.Body, "Line " + headingText, GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 12);
            line.rectTransform.pivot = new Vector2(0f, 1f);
        }

        public string Heading { get; }

        public int Order { get; }

        /// <summary>A part of the section before it, under its buttons, with no heading of its own.</summary>
        public bool Continues { get; }

        /// <summary>What the section says now, as written; empty for nothing.</summary>
        public string Line { get; private set; } = "";

        /// <summary>The section's buttons, for the editor's checks.</summary>
        public IReadOnlyList<GlazeButton> Buttons => buttons;

        public TextMeshPro LineLabel => line;

        /// <summary>Says how things stand, under the heading, by the one rule for text Halcyonic did not write; empty for nothing.</summary>
        public void Say(string text)
        {
            if (text == Line) return;
            Line = text;
            sheet.Changed();
        }

        /// <summary>A button in the section's rows, hidden until <see cref="Offer"/> gives it a label.</summary>
        public GlazeButton Button(string name, ButtonRole role)
        {
            var button = GlazeButton.Create(sheet.Body, name, role);
            button.Accepting = () => !FocusGuard.InputSuspended && sheet.Open;
            buttons.Add(button);
            labels[button] = null;
            return button;
        }

        /// <summary>Shows <paramref name="button"/> with <paramref name="label"/>, or hides it for null.</summary>
        public void Offer(GlazeButton button, string? label)
        {
            if (labels.TryGetValue(button, out var shown) && shown == label) return;
            labels[button] = label;
            sheet.Changed();
        }

        /// <summary>The heading, the line and the offered buttons in rows from <paramref name="top"/>; returns the bottom.</summary>
        internal float Layout(float left, float width, float top, float gap)
        {
            var y = SettingsSheet.LayLine(heading, Heading, left, width, top, 1);
            if (Line.Length > 0) y = SettingsSheet.LayLine(line, Line, left, width, y - SettingsSheet.LineGap, SettingsSheet.MaxLines);
            else line.gameObject.SetActive(false);
            var x = left;
            var rowTop = y - SettingsSheet.LineGap * 2f;
            var any = false;
            foreach (var button in buttons)
            {
                var label = labels[button];
                if (label == null)
                {
                    button.Hide();
                    continue;
                }
                var buttonWidth = Mathf.Min(width, button.Measure(label));
                if (any && x + buttonWidth > left + width + 1e-5f)
                {
                    // A new row, a button's height and the gap below the last.
                    x = left;
                    rowTop -= GlazeButton.HeightOf(false) + gap;
                }
                button.Show(label, new Vector2(x + buttonWidth / 2f, rowTop - GlazeButton.HeightOf(false) / 2f), buttonWidth);
                x += buttonWidth + gap;
                any = true;
            }
            return any ? rowTop - GlazeButton.HeightOf(false) : y;
        }
    }
}
