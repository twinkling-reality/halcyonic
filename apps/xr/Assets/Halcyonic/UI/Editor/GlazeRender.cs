#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.UI.Editor
{
    /// <summary>
    /// Renders every component of the interface in every state it has, on a panel at touch distance
    /// (ADR 0023): the state badges, the last known badge and a count, the marks, every role of
    /// button at rest, pointed at, pressed, unavailable and done, compact buttons and buttons with a
    /// second line, the banner's kinds, and meters full, nearly empty, in between and waiting. It
    /// checks each by <see cref="GlazeChecks"/>: words at least the caption's size, targets at least
    /// 60 dp (48 compact), nothing of ours cut short, every badge's whole word, each button's label
    /// contrasting with its own fill as drawn, at least 4.5:1, and each meter filled to its share, or
    /// not at all while waiting; and every icon the client core names in the icon atlas and on a
    /// badge, a mark or a button, each on its own beside words, and no label of words drawing from
    /// the atlas. A token changed shows here everywhere at once. It saves the gallery at a Quest
    /// 3's 25 pixels per degree in apps/xr/Builds/GlazeRenders, which git ignores. In the editor:
    /// Halcyonic > Render Every Component. In batch mode, see docs/internal/runbooks/XR_DEVELOPMENT.md;
    /// it exits with 1 when a check fails.
    /// </summary>
    public static class GlazeRender
    {
        private const int Size = 2048;

        /// <summary>A Quest 3's resolution near the middle of its lenses.</summary>
        private const float PixelsPerDegree = 25f;

        /// <summary>Foreground panels open this far from the eyes (ADR 0023).</summary>
        private const float Distance = 0.46f;

        private const float EyeHeight = 1.2f;

        private static Transform gallery = null!;
        private static Vector3 galleryEyes;

        [MenuItem("Halcyonic/Render Every Component")]
        public static void Menu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetActiveScene().path;
            var failures = GlazeChecks.AtEachTextSize(Run);
            if (!string.IsNullOrEmpty(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            EditorUtility.DisplayDialog("Component render", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = GlazeChecks.AtEachTextSize(Run);
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <param name="variant">A folder of its own for the renders of a pass, such as the larger text's; empty for the standard pass.</param>
        private static List<string> Run(string variant)
        {
            var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "GlazeRenders", variant));
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            var root = (GameObject?)null;
            RenderTexture? texture = null;
            try
            {
                // A new scene unloads what nothing references, so the target comes after it.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                root = new GameObject("Component render");
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = Camera(root.transform, eyes, texture);
                gallery = root.transform;
                galleryEyes = eyes;

                // A panel behind it all, as every component sits on one, a little farther than they stand.
                var behind = Holder("Panel", 0f, 0f);
                behind.localPosition = eyes + Vector3.forward * (Distance * 1.25f);
                behind.localScale = Vector3.one * (Distance * 1.25f);
                var panel = Surface.Create(behind, "Panel", -1);
                panel.Draw(new Vector2(GlazeTokens.Units(90f), GlazeTokens.Units(60f)), GlazeTokens.Units(Glaze.PanelRadiusDegrees), GlazeTokens.ColorOf(Glaze.Panel));

                var first = new List<Transform>();
                var marks = new List<MarkTag>();
                var badges = Badges(marks);
                var buttons = Buttons();
                Banners();
                var meters = Meters();
                foreach (Transform holder in gallery) first.Add(holder);
                failures.AddRange(Check(folder, "gallery.png", camera, texture, root, eyes, buttons));
                failures.AddRange(GlazeChecks.BadgesSayTheirState(badges, "component render"));
                failures.AddRange(GlazeChecks.IconAtlasHoldsEveryIcon("component render"));
                failures.AddRange(MetersFilled(meters));

                // A panel's list rows on a page of their own, in the middle of the view.
                foreach (var holder in first)
                {
                    if (holder != behind) holder.gameObject.SetActive(false);
                }
                var second = new List<Transform>();
                failures.AddRange(Check(folder, "gallery-rows.png", camera, texture, root, eyes, Rows()));
                foreach (Transform holder in gallery)
                {
                    if (holder != behind && holder.gameObject.activeSelf) second.Add(holder);
                }

                // Every action's icon, on a button beside its words.
                foreach (var holder in second) holder.gameObject.SetActive(false);
                var actions = Actions();
                failures.AddRange(Check(folder, "gallery-actions.png", camera, texture, root, eyes, actions.ConvertAll(action => (action.Button, action.What))));

                // What a file holds, each kind's icon beside its word, on a page of its own.
                var third = new List<Transform>();
                foreach (Transform holder in gallery)
                {
                    if (holder != behind && holder.gameObject.activeSelf) third.Add(holder);
                }
                foreach (var holder in third) holder.gameObject.SetActive(false);
                var files = FileIcons();
                var glass = GlassSample();
                SplitHeaderSample();
                failures.AddRange(Check(folder, "gallery-files.png", camera, texture, root, eyes, new List<(GlazeButton, string)>()));
                failures.AddRange(GlassLightsFromItsTop(camera, texture, glass));

                // The menu's controls, a page of their own: prompts, rows and answers in each state.
                var fourth = new List<Transform>();
                foreach (Transform holder in gallery)
                {
                    if (holder != behind && holder.gameObject.activeSelf) fourth.Add(holder);
                }
                foreach (var holder in fourth) holder.gameObject.SetActive(false);
                var controls = MenuControls();
                failures.AddRange(Check(folder, "gallery-menu.png", camera, texture, root, eyes, controls.ConvertAll(control => (control.Button, control.What))));
                failures.AddRange(GlazeChecks.OneSelectionTreatment(root.GetComponentsInChildren<Surface>(false), eyes, "component render: the menu's controls"));
                failures.AddRange(GlazeChecks.MicrophoneOnlyWhereHeld(controls.Select(control => control.Button), "component render"));
                foreach (var holder in fourth) holder.gameObject.SetActive(true);
                foreach (var holder in third) holder.gameObject.SetActive(true);
                failures.AddRange(EveryIconShows(badges, marks, actions, files));
                failures.AddRange(GlazeChecks.MicrophoneOnlyWhereHeld(actions.Select(action => action.Button), "component render"));
                failures.AddRange(IconAloneKeepsPresses());
                failures.AddRange(TextAsSeenCatchesASlant());
                failures.AddRange(OnePlaneCatchesEachBreak());
                failures.AddRange(TypeStepsDownCatchesARise());
                failures.AddRange(TypeStepsDownReadsThePillWithItsSubject());
                failures.AddRange(OneSelectionTreatmentCatchesEachBreak());
                MeasureFooters();
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (texture != null)
                {
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture);
                }
                KeepFontAssetsAsCommitted();
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: component render: every check passed; the render is in " + folder);
            return failures;
        }

        /// <summary>
        /// Renders what shows now as <paramref name="file"/> and checks it: words large enough, targets
        /// large enough, nothing cut, and each button's label contrasting with its own fill as drawn.
        /// </summary>
        private static IEnumerable<string> Check(string folder, string file, Camera camera, RenderTexture texture, GameObject root, Vector3 eyes,
            List<(GlazeButton Button, string What)> buttons)
        {
            var failures = new List<string>();
            ForceMeshes(root);
            var render = Render(camera, texture);
            File.WriteAllBytes(Path.Combine(folder, file), render.EncodeToPNG());
            failures.AddRange(GlazeChecks.TextLargeEnough(root, eyes, "component render"));
            GlazeChecks.ListTextAsSeen(root, eyes, "component render");
            failures.AddRange(GlazeChecks.TargetsLargeEnough(buttons.Where(button => !button.Button.Static).Select(button => button.Button), eyes, "component render"));
            failures.AddRange(GlazeChecks.NothingCut(root.GetComponentsInChildren<TMP_Text>(false), "component render"));
            failures.AddRange(GlazeChecks.IconsBesideWords(root, eyes, "component render"));
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false)) failures.AddRange(GlazeChecks.NotFromIcons(label, "component render"));
            foreach (var (button, what) in buttons)
            {
                // A row or an answer has no words of its own; the view lays them on it, and the token
                // tests hold every word to its contrast on a lit shape over white.
                if (!button.Label.gameObject.activeInHierarchy) continue;
                var contrast = LabelContrast(camera, render, button);
                Debug.Log("Halcyonic: component render: " + what + "'s label reaches " + contrast.ToString("0.0", CultureInfo.InvariantCulture) + ":1 on its fill.");
                if (contrast < 4.5f) failures.Add("component render: " + what + "'s label reaches only " + contrast.ToString("0.0", CultureInfo.InvariantCulture) + ":1 on its fill.");
            }
            UnityEngine.Object.DestroyImmediate(render);
            return failures;
        }

        /// <summary>Every state's badge, a count and a last known one, and the three marks, in rows from the top.</summary>
        private static List<StateBadgeView> Badges(List<MarkTag> marks)
        {
            var badges = new List<StateBadgeView>();
            var states = (CharacterActivity[])Enum.GetValues(typeof(CharacterActivity));
            var shown = states.Select(activity => Character(activity, AttentionLevel.None)).ToList();
            shown.Add(Character(CharacterActivity.TurnFinished, AttentionLevel.Notice));
            shown.Add(Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired, waiting: 2));
            shown.Add(Character(CharacterActivity.Working, AttentionLevel.None, stale: true));
            var x = -33f;
            var y = 21f;
            foreach (var presentation in shown)
            {
                var holder = Holder("Badge " + badges.Count, 0f, 0f);
                var badge = StateBadgeView.Create(holder, "Badge", 1);
                badge.Show(StateLanguage.BadgeOf(presentation));
                var width = GlazeTokens.DegreesOf(badge.Width);
                if (x + width > 33f)
                {
                    x = -33f;
                    y -= 3f;
                }
                Aim(holder, x + width / 2f, y);
                x += width + 1.5f;
                badges.Add(badge);
            }
            x = -33f;
            y -= 3f;
            // Practice, Demo and Recorded, as the client core marks practice, the demonstration and recorded work.
            foreach (var (synthetic, recorded) in new[] { (true, false), (true, true), (false, true) })
            {
                foreach (var shownMark in StateLanguage.MarksOf(Character(CharacterActivity.Working, AttentionLevel.None, synthetic: synthetic, recorded: recorded)))
                {
                    var holder = Holder("Mark " + shownMark.Word, 0f, 0f);
                    var mark = MarkTag.Create(holder, "Mark", 1);
                    mark.Show(shownMark);
                    var width = GlazeTokens.DegreesOf(mark.Width);
                    Aim(holder, x + width / 2f, y);
                    x += width + 1.5f;
                    marks.Add(mark);
                }
            }
            return badges;
        }

        /// <summary>Every role at rest, pointed at and pressed, then unavailable, done, compact and with a second line.</summary>
        private static List<(GlazeButton Button, string What)> Buttons()
        {
            var buttons = new List<(GlazeButton, string)>();
            var roles = new (ButtonRole Role, bool On, string Label)[]
            {
                (ButtonRole.Primary, false, "Start building"),
                (ButtonRole.Secondary, false, "More options"),
                (ButtonRole.Destructive, false, "Start over"),
                (ButtonRole.Filter, true, "Storefront API"),
                (ButtonRole.Filter, false, "Recipe tracker"),
            };
            var y = 7f;
            foreach (var (pointed, pressed, state) in new[] { (false, false, "at rest"), (true, false, "pointed at"), (false, true, "pressed") })
            {
                var x = -33f;
                foreach (var (role, on, label) in roles)
                {
                    var button = GlazeButton.Create(Holder(label + " " + state, 0f, 0f), "Button", role);
                    button.On = on;
                    x = Place(button, label, null, null, x, y);
                    button.PaintForRender(pointed, pressed);
                    buttons.Add((button, label + " " + state));
                }
                y -= 5f;
            }
            var row = -33f;
            var unavailable = GlazeButton.Create(Holder("Unavailable", 0f, 0f), "Button", ButtonRole.Secondary);
            unavailable.Available = false;
            row = Place(unavailable, "Approve", null, null, row, y);
            buttons.Add((unavailable, "an unavailable button"));
            var done = GlazeButton.Create(Holder("Done", 0f, 0f), "Button", ButtonRole.Secondary);
            done.Done = true;
            row = Place(done, "Approved", null, null, row, y);
            buttons.Add((done, "a done button"));
            var counted = GlazeButton.Create(Holder("With a line", 0f, 0f), "Button", ButtonRole.Filter);
            counted.On = true;
            row = Place(counted, "Storefront API", "1 task is waiting for you", GlazeTone.Attention, row, y);
            buttons.Add((counted, "a filter with a waiting line"));
            var compact = GlazeButton.Create(Holder("Compact", 0f, 0f), "Button", ButtonRole.Secondary, compact: true);
            Place(compact, "Settings", null, null, row, y);
            buttons.Add((compact, "a compact button"));
            foreach (var (button, _) in buttons)
            {
                if (button != counted && button != compact && button != unavailable && button != done) continue;
                button.PaintForRender(false, false);
            }
            return buttons;
        }

        /// <summary>
        /// A panel's list rows: a choice at rest, pointed at, pressed and chosen, a filter on and off, a
        /// fact with its line over the title and its end word, an unavailable one and one that only says
        /// something; then a card, the attention button, a tab in the attention colour showing, and a
        /// destructive action's confirmation.
        /// </summary>
        private static List<(GlazeButton Button, string What)> Rows()
        {
            var rows = new List<(GlazeButton, string)>();
            var width = GlazeTokens.Units(19f);
            var x = -31f;
            var y = 18f;
            void Add(string what, PanelRow words, ButtonRole role, bool pointed = false, bool pressed = false, bool on = false, bool available = true, bool still = false)
            {
                var button = GlazeButton.Create(Holder("Row " + what, 0f, 0f), "Row", role);
                button.On = on;
                button.Available = available;
                button.Static = still;
                var height = button.LayRow(words, width, still ? 0f : (float?)null);
                button.ShowRow(words, Vector2.zero, new Vector2(width, height), still ? 0f : (float?)null);
                if (x + 19f > 33f)
                {
                    x = -31f;
                    y -= 9f;
                }
                Aim(button.transform.parent, x + 9.5f, y);
                x += 19f + 1.5f;
                button.PaintForRender(pointed, pressed);
                rows.Add((button, what));
            }
            var choice = new PanelRow { Title = "New folder in Projects", Detail = HostText.YourStart + " makes a new, empty folder", Action = "choose" };
            Add("a choice at rest", choice, ButtonRole.Choice);
            Add("a choice pointed at", choice, ButtonRole.Choice, pointed: true);
            Add("a choice pressed", choice, ButtonRole.Choice, pressed: true);
            Add("a chosen choice", new PanelRow { Title = "shop", Detail = EntryText.Chosen + " · In Projects", Chosen = true, Action = "choose" }, ButtonRole.Choice, on: true);
            Add("a filter shown", new PanelRow { Title = "Storefront API", Detail = "1 task waiting", DetailTone = GlazeTone.Attention, Filter = true, Chosen = true, Action = "toggle" },
                ButtonRole.Filter, on: true);
            Add("a filter hidden", new PanelRow { Title = "Recipe tracker", Detail = "Hidden · 1 waiting", DetailTone = GlazeTone.Attention, Filter = true, Action = "toggle" },
                ButtonRole.Filter);
            Add("a fact", new PanelRow { Overline = EntryText.HowItRuns, Title = "On " + HostText.Your, Detail = "Chosen for you. Change it in More options.", DetailLines = 2,
                End = EntryText.MoreOptions, Action = "options" }, ButtonRole.Choice);
            Add("an unavailable choice", new PanelRow { Title = "Code", Detail = "Not on " + HostText.Your + " right now", Available = false, Action = "choose" }, ButtonRole.Choice, available: false);
            Add("a step and how it went", new PanelRow { Title = "Create the project", Detail = "Confirmed", DetailTone = GlazeTone.Success }, ButtonRole.Choice, still: true);
            Add("a card", new PanelRow { Card = true, Title = EntryText.CreateProject, Detail = EntryText.CreateInvite, Action = "create" }, ButtonRole.Choice);
            var attention = GlazeButton.Create(Holder("Attention", 0f, 0f), "Button", ButtonRole.Attention, compact: true);
            var after = Place(attention, EntryText.OpenNow, null, null, x, y);
            attention.PaintForRender(false, false);
            rows.Add((attention, "the attention button"));
            var tab = GlazeButton.Create(Holder("Attention tab", 0f, 0f), "Button", ButtonRole.Attention, compact: true);
            tab.On = true;
            after = Place(tab, StateLanguage.WordOf(WorkState.WaitingForYou), null, null, after, y);
            tab.PaintForRender(false, false);
            rows.Add((tab, "the attention tab showing"));
            var confirm = GlazeButton.Create(Holder("Confirm destructive", 0f, 0f), "Button", ButtonRole.Destructive);
            confirm.On = true;
            Place(confirm, EntryText.ConfirmStartOver, null, null, after, y);
            confirm.PaintForRender(false, false);
            rows.Add((confirm, "a destructive action's confirmation"));
            return rows;
        }

        private static float Place(GlazeButton button, string label, string? detail, GlazeTone? tone, float x, float y, GlazeIcon? icon = null)
        {
            var width = button.Measure(label, detail, icon);
            var degrees = GlazeTokens.DegreesOf(width);
            button.Show(label, Vector2.zero, width, detail, tone, icon);
            Aim(button.transform.parent, x + degrees / 2f, y);
            return x + degrees + 1.5f;
        }

        /// <summary>A holder for one component, at the distance it was built for and facing the eyes.</summary>
        private static Transform Holder(string name, float xDegrees, float yDegrees)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(gallery, false);
            holder.localScale = Vector3.one * Distance;
            Aim(holder, xDegrees, yDegrees);
            return holder;
        }

        /// <summary>
        /// Stands a holder <paramref name="xDegrees"/> across and <paramref name="yDegrees"/> up from
        /// straight ahead, on a sphere round the eyes, so each component is seen at its own distance.
        /// </summary>
        private static void Aim(Transform holder, float xDegrees, float yDegrees)
        {
            var direction = Quaternion.Euler(-yDegrees, xDegrees, 0f) * Vector3.forward;
            holder.SetPositionAndRotation(galleryEyes + direction * Distance, Quaternion.LookRotation(direction, Vector3.up));
        }

        /// <summary>The banner's three kinds, the last with what waits for the person.</summary>
        private static void Banners()
        {
            var x = -33f;
            foreach (var (kind, text, waiting) in new[]
            {
                (BannerKind.Live, "Connected to " + HostText.Your, (string?)null),
                (BannerKind.NotLive, "Last known: can't reach " + HostText.Your + ". Trying again…", null),
                (BannerKind.Practice, DemonstrationFallback.Describe(DemonstrationReason.NotConfigured, null), EntryText.WaitingForYou(1)),
            })
            {
                var holder = Holder("Banner " + kind, 0f, 0f);
                var banner = StageBanner.Create(holder);
                banner.Show(text, kind, waiting);
                var width = GlazeTokens.DegreesOf(banner.Width);
                Aim(holder, x + width / 2f, -14f);
                x += width + 1.5f;
            }
        }

        /// <summary>Meters with the words they picture under them: at most 60%, 3% and 100% left, and one waiting for a read.</summary>
        private static List<(MeterView Meter, float Share, bool Waiting)> Meters()
        {
            var meters = new List<(MeterView, float, bool)>();
            var x = -33f;
            foreach (var (share, waiting, words) in new[]
            {
                (0.6f, false, "At most 60% left"),
                (0.03f, false, "At most 3% left"),
                (1f, false, "At most 100% left"),
                (0.39f, true, UsageLeftPresenter.Reading),
            })
            {
                var holder = Holder("Meter " + words, 0f, 0f);
                var meter = MeterView.Create(holder, "Meter", 1);
                meter.Show(share, waiting);
                var label = GlazeText.Create(holder, "Words", GlazeType.Caption, GlazeTokens.TextSecondary, TextAlignmentOptions.TopLeft, 2);
                label.rectTransform.pivot = new Vector2(0f, 1f);
                GlazeText.SetLiteral(label, words);
                GlazeText.Lay(label, MeterView.Width, 1);
                label.transform.localPosition = new Vector3(-MeterView.Width / 2f, -GlazeTokens.Units(0.6f), -0.0005f);
                Aim(holder, x + MeterView.WidthDegrees / 2f, -22f);
                x += MeterView.WidthDegrees + 2.5f;
                meters.Add((meter, share, waiting));
            }
            return meters;
        }

        /// <summary>
        /// How much room the footers the menu's pages plan need (ADR 0026), against the column each
        /// stands in: every prompt its 1.45 degree cap, a grid step and its words at the content's 18
        /// dp, the main action's a twentieth wider for its weight, and 12 mm between prompts, the row
        /// reaching 0.6 degrees past each content line, as lane V's renders lay them. Logs each
        /// prompt's width and each footer's, and fails nothing. A frame grows whole with larger text,
        /// so a footer that fits at the standard size fits at the larger one.
        /// </summary>
        private static void MeasureFooters()
        {
            var holder = Holder("Footer measure", 0f, 0f);
            var label = GlazeText.Create(holder, "Words", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, 12);
            float Units(float degrees) => GlazeTokens.Units(degrees);
            float Degrees(float units) => 2f * Mathf.Atan(units / 2f) * Mathf.Rad2Deg;
            float Width((string Words, bool Main) prompt) =>
                Units(Glaze.Menu.PromptCapDegrees) + Units(Glaze.Menu.GridDegrees) + label.GetPreferredValues(prompt.Words).x * (prompt.Main ? 1.05f : 1f) + Units(1.2f);
            var gap = Glaze.TargetGapMeters / Glaze.Menu.PlaneMeters;
            float Room(float column) => PlaneComposition.Units(column) - 2f * Units(Glaze.Menu.PaddingDegrees) + 2f * Units(0.6f);
            var cases = new (string Name, float Column, (string Words, bool Main)[] Prompts)[]
            {
                ("a file waiting for an approval", 38f, new[] { ("Close", false), ("Stop", false), ("Deny", false), ("Approve", true) }),
                ("a file waiting for an approval, without Stop", 38f, new[] { ("Close", false), ("Deny", false), ("Approve", true) }),
                ("a file waiting for an answer", 38f, new[] { ("Close", false), ("Hold to talk", false), ("Send answer", true) }),
                ("a file's Activity", 38f, new[] { ("Close", false), ("Stop", false), ("Hold to talk", false), ("Tell it", true) }),
                ("a file's Activity, without Stop", 38f, new[] { ("Close", false), ("Hold to talk", false), ("Tell it", true) }),
                ("a file's approval, confirming", 38f, new[] { ("Close", false), ("Yes, approve", false), ("Cancel", false) }),
                ("New project's Questions with Start over", 38f, new[] { ("Close", false), ("Start over", false), ("Hold to talk", false), ("Make the recap", true) }),
                ("New project's Questions", 38f, new[] { ("Close", false), ("Hold to talk", false), ("Make the recap", true) }),
                ("New project's Start over, confirming", 38f, new[] { ("Close", false), ("Cancel", false), ("Yes, start over", false) }),
                ("New project's review, confirming", 38f, new[] { ("Close", false), ("Yes, start building", false), ("Cancel", false) }),
                ("Tasks, paging", 32f, new[] { ("Close", false), ("Next page", false) }),
                ("Projects, a row chosen", 32f, new[] { ("Close", false), ("Hide from the stage", false), ("Add a task", true) }),
                ("Projects, paging", 32f, new[] { ("Close", false), ("Next page", false), ("New project", true) }),
            };
            var words = cases.SelectMany(each => each.Prompts).Distinct().OrderBy(prompt => prompt.Words);
            Debug.Log("Halcyonic: component render: footer prompts at 18 dp, in degrees: "
                + string.Join(", ", words.Select(prompt => prompt.Words + (prompt.Main ? " (main)" : "") + " " + Degrees(Width(prompt)).ToString("0.0", CultureInfo.InvariantCulture))) + ".");
            foreach (var (name, column, prompts) in cases)
            {
                var needed = prompts.Sum(Width) + gap * (prompts.Length - 1);
                var room = Room(column);
                Debug.Log("Halcyonic: component render: footer measure: " + name + " needs " + Degrees(needed).ToString("0.0", CultureInfo.InvariantCulture)
                    + " degrees of a " + column.ToString("0", CultureInfo.InvariantCulture) + " degree column's " + Degrees(room).ToString("0.0", CultureInfo.InvariantCulture)
                    + (needed <= room ? ": it fits, " + Degrees(room - needed).ToString("0.0", CultureInfo.InvariantCulture) + " to spare." : ": it does not fit, " + Degrees(needed - room).ToString("0.0", CultureInfo.InvariantCulture) + " short."));
            }
            UnityEngine.Object.DestroyImmediate(holder.gameObject);
        }

        /// <summary>
        /// Lays a one-column composition of parts <paramref name="heights"/> degrees tall, 24 wide, on the
        /// plane 30 degrees below eye level, each part an empty holder; returns its column for the checks.
        /// </summary>
        private static (Transform Holder, List<GlazeChecks.PlaneShape> Column) OneColumn(string name, params float[] heights)
        {
            var composition = new PlaneComposition(new[] { new PlaneColumn(PlaneComposition.Units(24f), heights.Select(PlaneComposition.Units).ToArray()) });
            var direction = new PanelDirection(0f, -30f, true, false);
            var holder = new GameObject(name).transform;
            holder.SetParent(gallery, false);
            var column = new List<GlazeChecks.PlaneShape>();
            foreach (var placed in composition.Parts)
            {
                var part = new GameObject("Part " + placed.Index).transform;
                part.SetParent(holder, false);
                PlaneLayout.Lay(part, galleryEyes, direction, placed);
                column.Add(new GlazeChecks.PlaneShape(part.name, part, new Vector2(placed.Width, placed.Height) * PlaneComposition.Distance));
            }
            return (holder, column);
        }

        /// <summary>
        /// The type check passes a column whose title stands above its body text, and catches body
        /// text above a larger title (ADR 0026). Fails if it fails the first or misses the second.
        /// </summary>
        private static IEnumerable<string> TypeStepsDownCatchesARise()
        {
            var failures = new List<string>();
            foreach (var (name, upper, lower) in new[] { ("stepping down", GlazeType.Title, GlazeType.Body), ("rising", GlazeType.Body, GlazeType.Title) })
            {
                var (holder, column) = OneColumn("Type " + name, 4f, 6f);
                foreach (var (part, type) in new[] { (column[0], upper), (column[1], lower) })
                {
                    var words = GlazeText.Create(part.Root, type.ToString(), type, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 12);
                    words.rectTransform.pivot = new Vector2(0f, 1f);
                    GlazeText.SetLiteral(words, type == GlazeType.Title ? "Waiting for you" : "It wants to run make migrate.");
                    GlazeText.Lay(words, PlaneComposition.Units(20f), 1);
                    var size = part.Size / PlaneComposition.Distance;
                    words.transform.localPosition = new Vector3(-size.x / 2f + GlazeTokens.Units(1f), size.y / 2f - GlazeTokens.Units(1f), -0.001f);
                }
                var found = GlazeChecks.TypeStepsDown(new[] { (IReadOnlyList<GlazeChecks.PlaneShape>)column }, galleryEyes, "component render: type " + name).ToList();
                if (upper == GlazeType.Title) failures.AddRange(found);
                else if (!found.Any(failure => failure.Contains("type only steps down"))) failures.Add("component render: the type check missed body text above a larger title.");
                else Debug.Log("Halcyonic: component render: the type check caught type " + name + ": " + string.Join(" ", found));
                UnityEngine.Object.DestroyImmediate(holder.gameObject);
            }
            return failures;
        }

        /// <summary>
        /// The type check reads a state pill on a subject's top edge with that subject, the split
        /// header's one exception (ADR 0026), and still catches the same 18 dp words there as a plain
        /// line over the 24 dp title. Fails if it fails the first or misses the second.
        /// </summary>
        private static IEnumerable<string> TypeStepsDownReadsThePillWithItsSubject()
        {
            var failures = new List<string>();
            foreach (var asPill in new[] { true, false })
            {
                var (holder, column) = OneColumn(asPill ? "Subject with its pill" : "Subject with a line over it", 4f, 6f);
                var subject = column[0].Root;
                var size = column[0].Size / PlaneComposition.Distance;
                if (asPill)
                {
                    var pill = StateBadgeView.Create(subject, "Pill", 3, pill: true);
                    pill.Show(StateLanguage.BadgeOf(Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired)));
                    pill.transform.localPosition = new Vector3(-size.x / 2f + GlazeTokens.Units(3f), size.y / 2f, -0.002f);
                }
                else
                {
                    var line = GlazeText.Create(subject, "Line", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Center, 3);
                    GlazeText.SetLiteral(line, "Waiting for you");
                    GlazeText.Lay(line, GlazeTokens.Units(10f), 1);
                    line.transform.localPosition = new Vector3(-size.x / 2f + GlazeTokens.Units(6f), size.y / 2f + GlazeText.LineHeight(line) / 2f, -0.002f);
                }
                var title = GlazeText.Create(subject, "Subject", GlazeType.Subject, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 3);
                title.rectTransform.pivot = new Vector2(0f, 1f);
                GlazeText.SetLiteral(title, "Add rate limiting");
                GlazeText.Lay(title, size.x - GlazeTokens.Units(3f), 1);
                title.transform.localPosition = new Vector3(-size.x / 2f + GlazeTokens.Units(1.5f), size.y / 2f - GlazeTokens.Units(1.2f), -0.002f);
                var body = GlazeText.Create(column[1].Root, "Body", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 3);
                body.rectTransform.pivot = new Vector2(0f, 1f);
                GlazeText.SetLiteral(body, "It wants to run make migrate.");
                GlazeText.Lay(body, GlazeTokens.Units(20f), 1);
                body.transform.localPosition = new Vector3(-size.x / 2f + GlazeTokens.Units(1.5f), column[1].Size.y / PlaneComposition.Distance / 2f - GlazeTokens.Units(1.5f), -0.002f);
                var found = GlazeChecks.TypeStepsDown(new[] { (IReadOnlyList<GlazeChecks.PlaneShape>)column }, galleryEyes, "component render: " + holder.name).ToList();
                if (asPill) failures.AddRange(found);
                else if (!found.Any(failure => failure.Contains("type only steps down"))) failures.Add("component render: the type check missed 18 dp words over a 24 dp subject.");
                UnityEngine.Object.DestroyImmediate(holder.gameObject);
            }
            return failures;
        }

        /// <summary>
        /// The selection check passes a row with one chosen shape, lit and framed, one pointed at,
        /// framed alone, and a footer whose main action's cap takes the accent; and catches an accent
        /// bar under the chosen shape (as a bar and as the accent, exactly those two), a second chosen
        /// shape lit otherwise, and a pointed shape filled (ADR 0026).
        /// </summary>
        private static IEnumerable<string> OneSelectionTreatmentCatchesEachBreak()
        {
            var failures = new List<string>();
            var accent = GlazeTokens.ColorOf(Glaze.Tone(GlazeTone.Accent).Strong);
            var litFill = new Color(1f, 1f, 1f, Glaze.Menu.LitFillOpacity);
            var litEdge = new Color(1f, 1f, 1f, Glaze.Menu.LitFrameOpacity);
            var pointedEdge = new Color(1f, 1f, 1f, Glaze.Menu.PointedFrameOpacity);
            var frame = GlazeTokens.Units(0.1f);
            Surface Shape(Transform part, string name, Vector2 size, Vector2 at, Color fill, Color edge, SurfaceSelection selection)
            {
                var shape = Surface.Create(part, name, 1);
                shape.Draw(GlazeTokens.Units(1f) * size, GlazeTokens.Units(0.75f), fill, edge, edge.a > 0f ? frame : 0f);
                shape.transform.localPosition = new Vector3(GlazeTokens.Units(at.x), GlazeTokens.Units(at.y), -0.001f);
                shape.Selection = selection;
                return shape;
            }
            var cases = new (string Name, string[] Caught, Action<Transform> Break)[]
            {
                ("as drawn", Array.Empty<string>(), _ => { }),
                ("with an accent bar under the chosen shape", new[] { "is a bar", "filled with the accent" },
                    row => Shape(row, "Bar", new Vector2(8f, 0.1f), new Vector2(-6f, -1.8f), accent, Color.clear, SurfaceSelection.None)),
                ("with a second chosen shape lit otherwise", new[] { "lit otherwise than the lit fill" },
                    row => Shape(row, "Chosen too", new Vector2(5f, 3f), new Vector2(8f, 0f), new Color(1f, 1f, 1f, 0.3f), litEdge, SurfaceSelection.Lit)),
                ("with a pointed shape filled", new[] { "more than a frame" },
                    row => Shape(row, "Pointed and filled", new Vector2(5f, 3f), new Vector2(8f, 0f), litFill, pointedEdge, SurfaceSelection.Pointed)),
            };
            foreach (var (name, caught, change) in cases)
            {
                var (holder, column) = OneColumn("Selection " + name, 5f, 6f);
                Shape(column[0].Root, "Chosen", new Vector2(8f, 3f), new Vector2(-6f, 0f), litFill, litEdge, SurfaceSelection.Lit);
                Shape(column[0].Root, "Pointed", new Vector2(5f, 3f), new Vector2(2f, 0f), Color.clear, pointedEdge, SurfaceSelection.Pointed);
                Shape(column[1].Root, "Cap", new Vector2(3f, 3f), new Vector2(8f, -1f), accent, Color.clear, SurfaceSelection.MainCap);
                change(column[0].Root);
                var found = GlazeChecks.OneSelectionTreatment(holder.GetComponentsInChildren<Surface>(false), galleryEyes, "component render: a row " + name).ToList();
                if (caught.Length == 0) failures.AddRange(found);
                else if (found.Count != caught.Length || !caught.All(words => found.Any(failure => failure.Contains(words))))
                {
                    failures.Add("component render: the selection check found " + found.Count + " things wrong with a row " + name + ", not " + string.Join(" and ", caught) + ": "
                        + string.Join(" ", found));
                }
                else Debug.Log("Halcyonic: component render: the selection check caught a row " + name + ": " + string.Join(" ", found));
                UnityEngine.Object.DestroyImmediate(holder.gameObject);
            }
            return failures;
        }

        /// <summary>
        /// The one-plane check passes a composition the plane model lays (<see cref="PlaneComposition"/>,
        /// ADR 0026), and catches each way to break one: a part turned, a part off the plane, a part
        /// rolled, the whole plane off square to the eyes, two parts too close, and columns that start
        /// or end on different lines. Fails if it fails the laid composition or misses a break.
        /// </summary>
        private static IEnumerable<string> OnePlaneCatchesEachBreak()
        {
            var failures = new List<string>();
            var composition = new PlaneComposition(new[]
            {
                new PlaneColumn(PlaneComposition.Units(20f), PlaneComposition.Units(3f), PlaneComposition.Units(12f)),
                new PlaneColumn(PlaneComposition.Units(24f), PlaneComposition.Units(3f), PlaneComposition.Units(4f), PlaneComposition.Units(10f)),
            });
            var direction = new PanelDirection(0f, -30f, true, false);
            var tilt = Quaternion.AngleAxis(2f, PlaneLayout.Facing(direction) * Vector3.right);
            var center = PlaneLayout.PointOf(galleryEyes, direction, 0f, 0f);
            var breaks = new (string Name, string Caught, Action<List<List<GlazeChecks.PlaneShape>>> Break)[]
            {
                ("as laid", "", _ => { }),
                ("with a part turned a degree", "is turned", columns => columns[0][1].Root.Rotate(Vector3.up, 1f, Space.Self)),
                ("with a part 2 mm off the plane", "off the plane", columns => columns[1][0].Root.position += columns[1][0].Root.forward * 0.002f),
                ("with a part rolled a degree", "is rolled", columns => columns[0][0].Root.Rotate(Vector3.forward, 1f, Space.Self)),
                ("tipped 2 degrees off square to the eyes", "off square", columns =>
                {
                    foreach (var part in columns.SelectMany(column => column))
                    {
                        part.Root.SetPositionAndRotation(center + tilt * (part.Root.position - center), tilt * part.Root.rotation);
                    }
                }),
                ("with two parts 4 mm apart", "mm apart on the plane", columns => columns[0][1].Root.position += columns[0][1].Root.up * 0.004f),
                ("with a column starting 2 mm low", "start on one line", columns => columns[1][0].Root.position -= columns[1][0].Root.up * 0.002f),
                ("with a column ending 2 mm high", "end on one line", columns => columns[1][2].Root.position += columns[1][2].Root.up * 0.002f),
            };
            foreach (var (name, caught, change) in breaks)
            {
                var holder = new GameObject("Plane " + name).transform;
                holder.SetParent(gallery, false);
                var columns = new List<List<GlazeChecks.PlaneShape>>();
                foreach (var placed in composition.Parts)
                {
                    if (placed.Column == columns.Count) columns.Add(new List<GlazeChecks.PlaneShape>());
                    var part = new GameObject("Column " + placed.Column + " part " + placed.Index).transform;
                    part.SetParent(holder, false);
                    PlaneLayout.Lay(part, galleryEyes, direction, placed);
                    columns[placed.Column].Add(new GlazeChecks.PlaneShape(part.name, part, new Vector2(placed.Width, placed.Height) * PlaneComposition.Distance));
                }
                change(columns);
                var found = GlazeChecks.OnePlane(columns.ConvertAll(column => (IReadOnlyList<GlazeChecks.PlaneShape>)column), galleryEyes, "component render: a composition " + name).ToList();
                if (caught.Length == 0) failures.AddRange(found);
                else if (!found.Any(failure => failure.Contains(caught)))
                {
                    failures.Add("component render: the one-plane check missed a composition " + name + ".");
                }
                else Debug.Log("Halcyonic: component render: the one-plane check caught a composition " + name + ": " + string.Join(" ", found));
                UnityEngine.Object.DestroyImmediate(holder.gameObject);
            }
            return failures;
        }

        /// <summary>
        /// The check of text as the eyes see it catches what <see cref="GlazeChecks.TextLargeEnough"/>
        /// cannot (ADR 0026): body text on an upright plate 40 degrees below eye level, which the eyes
        /// meet at a slant, reads under 14 dp, about two thirds of its size, while the same plate tipped
        /// back to face the eyes reads at its size. Fails if the check misses the first or fails the second.
        /// </summary>
        private static IEnumerable<string> TextAsSeenCatchesASlant()
        {
            var failures = new List<string>();
            var direction = Quaternion.Euler(40f, 0f, 0f) * Vector3.forward;
            foreach (var upright in new[] { true, false })
            {
                var holder = new GameObject(upright ? "Upright under the eyes" : "Facing the eyes").transform;
                holder.SetParent(gallery, false);
                holder.localScale = Vector3.one * Distance;
                holder.SetPositionAndRotation(galleryEyes + direction * Distance, upright ? Quaternion.identity : Quaternion.LookRotation(direction, Vector3.up));
                var words = GlazeText.Create(holder, "Words", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Top, 12);
                GlazeText.SetLiteral(words, "Approve the request above?");
                GlazeText.Lay(words, GlazeTokens.Units(20f), 1);
                var seen = GlazeChecks.TextAsSeen(holder.gameObject, galleryEyes, "component render: " + holder.name).ToList();
                if (upright && seen.Count == 0)
                {
                    failures.Add("component render: body text on an upright plate 40 degrees under the eyes should read under 14 dp as the eyes see it, and the check missed it.");
                }
                if (!upright) failures.AddRange(seen);
                UnityEngine.Object.DestroyImmediate(holder.gameObject);
            }
            return failures;
        }

        /// <summary>
        /// A button whose icon alone changes keeps taking presses, as Stop must when a question arrives
        /// while the hand reaches for it; new words make a new action, whose presses wait to settle.
        /// Checked on when the button last took a new action, pushed far into the past first.
        /// </summary>
        private static IEnumerable<string> IconAloneKeepsPresses()
        {
            var failures = new List<string>();
            var settled = typeof(GlazeButton).GetField("shownAt", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new MissingFieldException(nameof(GlazeButton), "shownAt");
            const float LongAgo = -100f;
            var holder = Holder("Settling", 0f, 0f);
            var button = GlazeButton.Create(holder, "Button", ButtonRole.Destructive);
            var stop = WorkspaceText.Label(WorkspaceAction.Interrupt);
            var width = button.Measure(stop, null, GlazeIcon.Stop);
            button.Show(stop, Vector2.zero, width, withIcon: GlazeIcon.Stop);
            foreach (var (icon, what) in new[] { ((GlazeIcon?)null, "losing its icon"), (GlazeIcon.Stop, "taking its icon back") })
            {
                settled.SetValue(button, LongAgo);
                button.Show(stop, Vector2.zero, width, withIcon: icon);
                if ((float)settled.GetValue(button)! != LongAgo) failures.Add("component render: Stop " + what + " made it wait to settle, so a press would be dropped.");
            }
            settled.SetValue(button, LongAgo);
            button.Show(WorkspaceText.Label(WorkspaceAction.Deny), Vector2.zero, width, withIcon: GlazeIcon.Deny);
            if ((float)settled.GetValue(button)! == LongAgo) failures.Add("component render: new words in Stop's place did not wait to settle.");
            UnityEngine.Object.DestroyImmediate(holder.gameObject);
            return failures;
        }

        /// <summary>
        /// Every icon the client core names shows in the gallery, on a badge, a mark or a button, each
        /// as its own glyph. No two states or marks share a glyph, so a state never borrows another's
        /// icon, and no two actions do; an action may share a state's, as Stop shares Stopped's.
        /// </summary>
        private static IEnumerable<string> EveryIconShows(List<StateBadgeView> badges, List<MarkTag> marks, List<(GlazeButton Button, string What, GlazeIcon Icon)> actions,
            List<(TextMeshPro Icon, GlazeIcon Kind)> files)
        {
            var stateIcons = new HashSet<GlazeIcon> { GlazeIcon.LastKnown };
            foreach (WorkState state in Enum.GetValues(typeof(WorkState))) stateIcons.Add(StateLanguage.Look(state).Icon);
            foreach (var (synthetic, recorded) in new[] { (true, false), (true, true), (false, true) })
            {
                foreach (var mark in StateLanguage.MarksOf(Character(CharacterActivity.Working, AttentionLevel.None, synthetic: synthetic, recorded: recorded))) stateIcons.Add(mark.Icon);
            }
            foreach (var group in new[] { stateIcons, new HashSet<GlazeIcon>(Enum.GetValues(typeof(GlazeIcon)).Cast<GlazeIcon>().Where(icon => !stateIcons.Contains(icon))) })
            {
                var byGlyph = new Dictionary<string, GlazeIcon>();
                foreach (var icon in group)
                {
                    var glyph = GlazeIconGlyphs.Of(icon);
                    if (byGlyph.TryGetValue(glyph, out var other)) yield return "component render: " + icon + " and " + other + " share a glyph.";
                    else byGlyph[glyph] = icon;
                }
            }
            var shown = new HashSet<GlazeIcon>();
            foreach (var badge in badges)
            {
                if (badge.Shown == null || !badge.ShowsIcon) continue;
                shown.Add(badge.Shown.Icon);
                if (badge.Icon.text != GlazeIconGlyphs.Of(badge.Shown.Icon)) yield return "component render: the " + badge.Shown.Text + " badge shows another icon than " + badge.Shown.Icon + ".";
            }
            foreach (var mark in marks)
            {
                var icon = stateIcons.FirstOrDefault(each => GlazeIconGlyphs.Of(each) == mark.Icon.text);
                if (GlazeIconGlyphs.Of(icon) == mark.Icon.text) shown.Add(icon);
                else yield return "component render: the " + mark.Word.text + " mark shows no icon of the set.";
            }
            foreach (var (button, what, icon) in actions)
            {
                if (button.Icon == null || button.Icon.text != GlazeIconGlyphs.Of(icon)) yield return "component render: " + what + " shows no " + icon + " icon.";
                else shown.Add(icon);
            }
            foreach (var (icon, kind) in files)
            {
                if (icon.text != GlazeIconGlyphs.Of(kind)) yield return "component render: the " + kind + " row shows another icon.";
                else shown.Add(kind);
            }
            foreach (GlazeIcon icon in Enum.GetValues(typeof(GlazeIcon)))
            {
                if (!shown.Contains(icon)) yield return "component render: nothing in the gallery shows the " + icon + " icon.";
            }
        }

        /// <summary>
        /// The menu's controls (ADR 0026), each in every state it shows: prompts plain, the main action,
        /// one unavailable, held, pointed at and pressed; rows and answers at rest, pointed at and chosen.
        /// </summary>
        private static List<(GlazeButton Button, string What)> MenuControls()
        {
            var shown = new List<(GlazeButton, string)>();
            var prompts = new (string Words, GlazeIcon Icon, bool Main, bool Available, bool Holds, bool Pointed, bool Pressed)[]
            {
                ("Close", GlazeIcon.Close, false, true, false, false, false),
                ("Hold to talk", GlazeIcon.HoldToTalk, false, true, true, false, false),
                ("Send answer", GlazeIcon.SendAnswer, true, true, false, false, false),
                ("Make the recap", GlazeIcon.Next, true, false, false, false, false),
                ("Stop", GlazeIcon.Stop, false, true, false, true, false),
                ("Approve", GlazeIcon.Approve, true, true, false, true, true),
            };
            var x = -30f;
            var y = 14f;
            foreach (var (words, icon, main, available, holds, pointed, pressed) in prompts)
            {
                var button = GlazeButton.Create(Holder("Prompt " + words, 0f, 0f), "Prompt", ButtonRole.Prompt);
                button.Holds = holds;
                button.Available = available;
                var width = button.MeasurePrompt(words, main);
                button.ShowPrompt(words, icon, Vector2.zero, width, main);
                var degrees = GlazeTokens.DegreesOf(width);
                if (x + degrees > 30f)
                {
                    x = -30f;
                    y -= 5f;
                }
                Aim(button.transform.parent, x + degrees / 2f, y);
                x += degrees + 1.5f;
                button.PaintForRender(pointed, pressed);
                shown.Add((button, "the prompt " + words + (main ? ", the main action" : "") + (available ? "" : ", unavailable")));
            }
            var area = new Vector2(GlazeTokens.Units(11f), GlazeTokens.Units(Glaze.MinimumTargetDegrees)) * 2f / 2f;
            area.x = 2f * GlazeTokens.Units(6f);
            var states = new (string State, bool Chosen, bool Pointed)[] { ("at rest", false, false), ("pointed at", false, true), ("chosen", true, false) };
            for (var kind = 0; kind < 2; kind++)
            {
                var role = kind == 0 ? ButtonRole.Row : ButtonRole.Answer;
                for (var index = 0; index < states.Length; index++)
                {
                    var (state, chosen, pointed) = states[index];
                    var holder = Holder((kind == 0 ? "Row " : "Answer ") + state, -16f + 16f * index, kind == 0 ? -1f : -7f);
                    var button = GlazeButton.Create(holder, "Area", role);
                    button.On = chosen;
                    button.ShowArea(Vector2.zero, area);
                    button.PaintForRender(pointed, false);
                    var words = GlazeText.Create(holder, "Words", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, 13, strong: chosen && role == ButtonRole.Answer);
                    words.rectTransform.pivot = new Vector2(0f, 0.5f);
                    GlazeText.SetLiteral(words, (kind == 0 ? "A row " : "An answer ") + state);
                    GlazeText.Lay(words, area.x - GlazeTokens.Units(Glaze.Menu.InsetDegrees) * 2f, 1);
                    words.transform.localPosition = new Vector3(-area.x / 2f + GlazeTokens.Units(Glaze.Menu.InsetDegrees), 0f, -0.0008f);
                    shown.Add((button, (kind == 0 ? "a row " : "an answer ") + state));
                }
            }
            return shown;
        }

        /// <summary>The menu's glass (ADR 0026): a subject's plate, 16 by 8 degrees, to the right of the file kinds.</summary>
        private static Surface GlassSample()
        {
            var holder = Holder("Glass", 26f, 4f);
            var glass = Surface.Create(holder, "Glass", 1);
            glass.DrawGlass(new Vector2(GlazeTokens.Units(8f), GlazeTokens.Units(4f)) * 2f);
            return glass;
        }

        /// <summary>
        /// A file's subject with the split header (ADR 0026): its glass plate, its task's state pill on
        /// the plate's top edge at its left, the badge's word at 18 dp, and its title drawn light at 24 dp.
        /// </summary>
        private static void SplitHeaderSample()
        {
            var holder = Holder("Split header", 26f, -9f);
            var size = new Vector2(GlazeTokens.Units(9f), GlazeTokens.Units(2.2f)) * 2f;
            var plate = Surface.Create(holder, "Subject plate", 1);
            plate.DrawGlass(size);
            var pill = StateBadgeView.Create(holder, "Pill", 3, pill: true);
            pill.Show(StateLanguage.BadgeOf(Character(CharacterActivity.WaitingForHuman, AttentionLevel.ActionRequired)));
            var padding = GlazeTokens.Units(Glaze.Menu.PaddingDegrees);
            pill.transform.localPosition = new Vector3(-size.x / 2f + padding + pill.Width / 2f, size.y / 2f, -0.002f);
            var title = GlazeText.Create(holder, "Subject", GlazeType.Subject, GlazeTokens.Text, TextAlignmentOptions.TopLeft, 3);
            title.rectTransform.pivot = new Vector2(0f, 1f);
            GlazeText.SetLiteral(title, "Add rate limiting");
            GlazeText.Lay(title, size.x - 2f * padding, 1);
            title.transform.localPosition = new Vector3(-size.x / 2f + padding, size.y / 2f - StateBadgeView.PillHeight / 2f - GlazeTokens.Units(Glaze.Menu.GridDegrees), -0.002f);
        }

        /// <summary>
        /// The glass lights from its top edge as drawn: just under its sheen it is brighter than at its
        /// middle, below the light's reach, and the sheen brighter still; its middle is the plain glass.
        /// </summary>
        private static IEnumerable<string> GlassLightsFromItsTop(Camera camera, RenderTexture texture, Surface glass)
        {
            var render = Render(camera, texture);
            float Brightness(float fromTop)
            {
                var point = glass.transform.TransformPoint(new Vector3(0f, 0.5f - fromTop / glass.Size.y, 0f));
                var screen = camera.WorldToScreenPoint(point);
                var colour = render.GetPixel(Mathf.RoundToInt(screen.x), Mathf.RoundToInt(screen.y));
                return (colour.r + colour.g + colour.b) / 3f;
            }
            var sheenWidth = GlazeTokens.Units(Glaze.Menu.SheenDegrees);
            var sheen = Brightness(1.65f * sheenWidth);
            var lit = Brightness(GlazeTokens.Units(0.5f));
            var middle = Brightness(glass.Size.y / 2f);
            UnityEngine.Object.DestroyImmediate(render);
            Debug.Log("Halcyonic: component render: the glass's sheen " + (sheen * 255f).ToString("0", CultureInfo.InvariantCulture) + ", under it "
                + (lit * 255f).ToString("0", CultureInfo.InvariantCulture) + ", its middle " + (middle * 255f).ToString("0", CultureInfo.InvariantCulture) + " of 255.");
            if (!(lit > middle + 3f / 255f)) yield return "component render: the glass's top is no brighter than its middle; its light from the top edge is missing.";
            if (!(sheen > lit + 3f / 255f)) yield return "component render: the glass's sheen is no brighter than the light under it.";
        }

        /// <summary>
        /// What a file holds, generic only (ADR 0026), each kind's icon beside its word at the content's
        /// size, as a page line shows a file, and the chevron of a line that opens more.
        /// </summary>
        private static List<(TextMeshPro Icon, GlazeIcon Kind)> FileIcons()
        {
            var shown = new List<(TextMeshPro, GlazeIcon)>();
            var kinds = new (GlazeIcon Icon, string Words)[]
            {
                (GlazeIcon.CodeFile, "Code"), (GlazeIcon.DatabaseFile, "Database"), (GlazeIcon.DataFile, "Data"),
                (GlazeIcon.TextFile, "Writing"), (GlazeIcon.ImageFile, "Image"), (GlazeIcon.ScriptFile, "Script"),
                (GlazeIcon.PackageFile, "Package"), (GlazeIcon.Folder, "Folder"), (GlazeIcon.OpensMore, "Opens more"),
            };
            for (var index = 0; index < kinds.Length; index++)
            {
                var (kind, words) = kinds[index];
                var holder = Holder("File " + words, index < 5 ? -12f : 8f, 10f - 4.5f * (index % 5));
                var icon = GlazeIcons.Create(holder, "Icon", Glaze.Menu.BodyDegrees, GlazeTokens.Text, 12);
                GlazeIcons.Show(icon, kind);
                var label = GlazeText.Create(holder, "Words", GlazeType.Body, GlazeTokens.Text, TextAlignmentOptions.Left, 12);
                label.rectTransform.pivot = new Vector2(0f, 0.5f);
                GlazeText.SetLiteral(label, words);
                GlazeText.Lay(label, GlazeTokens.Units(14f), 1);
                label.transform.localPosition = new Vector3(GlazeTokens.Units(Glaze.Menu.IconColumnDegrees / 2f + Glaze.Menu.GridDegrees), 0f, 0f);
                shown.Add((icon, kind));
            }
            return shown;
        }

        /// <summary>
        /// Every action's icon on a button beside its words, as the panels, the rail and Settings show
        /// them: the bar's actions at a target's height, the header's and the rail's compact.
        /// </summary>
        private static List<(GlazeButton Button, string What, GlazeIcon Icon)> Actions()
        {
            var shown = new List<(GlazeButton, string, GlazeIcon)>();
            var actions = new (GlazeIcon Icon, string Words, ButtonRole Role, bool Compact, bool Available)[]
            {
                (GlazeIcon.Approve, WorkspaceText.Label(WorkspaceAction.Approve), ButtonRole.Primary, false, true),
                (GlazeIcon.Deny, WorkspaceText.Label(WorkspaceAction.Deny), ButtonRole.Secondary, false, true),
                (GlazeIcon.Stop, WorkspaceText.Label(WorkspaceAction.Interrupt), ButtonRole.Destructive, false, true),
                (GlazeIcon.TellIt, WorkspaceText.Label(WorkspaceAction.Instruct), ButtonRole.Secondary, false, true),
                (GlazeIcon.SendAnswer, WorkspaceText.Label(WorkspaceAction.Answer), ButtonRole.Primary, false, true),
                (GlazeIcon.HoldToTalk, VoiceText.HoldToTalk, ButtonRole.Secondary, false, true),
                (GlazeIcon.Type, ProjectIdea.Questions[0].TypeLabel, ButtonRole.Secondary, false, true),
                (GlazeIcon.StartBuilding, EntryText.StartBuilding, ButtonRole.Primary, false, true),
                (GlazeIcon.StartOver, EntryText.StartOver, ButtonRole.Destructive, false, true),
                (GlazeIcon.Refresh, WorkspaceText.Refresh, ButtonRole.Secondary, true, true),
                (GlazeIcon.Refresh, EntryText.TryAgain, ButtonRole.Primary, false, true),
                (GlazeIcon.Change, EntryText.Change, ButtonRole.Secondary, false, true),
                (GlazeIcon.ConnectProjects, EntryText.ConnectProjects, ButtonRole.Secondary, false, true),
                (GlazeIcon.CreateProject, EntryText.CreateProject, ButtonRole.Secondary, false, true),
                (GlazeIcon.AddTask, EntryText.AddTask, ButtonRole.Secondary, false, true),
                (GlazeIcon.OpenNow, EntryText.OpenNow, ButtonRole.Attention, true, true),
                (GlazeIcon.KeepCreating, EntryText.KeepCreating, ButtonRole.Secondary, true, true),
                (GlazeIcon.NotNow, EntryText.NotNow, ButtonRole.Secondary, true, true),
                (GlazeIcon.Close, EntryText.Close, ButtonRole.Secondary, true, true),
                (GlazeIcon.Close, EntryText.Cancel, ButtonRole.Secondary, false, true),
                (GlazeIcon.Back, EntryText.Back, ButtonRole.Secondary, false, true),
                (GlazeIcon.Next, EntryText.CheckFirst, ButtonRole.Primary, false, true),
                (GlazeIcon.Move, EntryText.Move, ButtonRole.Secondary, true, true),
                (GlazeIcon.ResetPosition, EntryText.ResetPosition, ButtonRole.Secondary, true, true),
                (GlazeIcon.ShowAll, EntryText.ShowAll, ButtonRole.Secondary, false, true),
                (GlazeIcon.Settings, SettingsText.Settings, ButtonRole.Secondary, true, true),
                (GlazeIcon.UsageLeft, UsageLeftPresenter.Title, ButtonRole.Secondary, true, true),
                (GlazeIcon.Approve, WorkspaceText.ConfirmLabel(WorkspaceAction.Approve), ButtonRole.Primary, false, true),
                (GlazeIcon.Stop, WorkspaceText.ConfirmLabel(WorkspaceAction.Interrupt), ButtonRole.Destructive, false, true),
                (GlazeIcon.Locked, EntryText.ReadToPart(3), ButtonRole.Primary, false, false),
            };
            var x = -33f;
            var y = 21f;
            foreach (var (icon, words, role, compact, available) in actions)
            {
                var button = GlazeButton.Create(Holder("Action " + words, 0f, 0f), "Button", role, compact);
                button.Available = available;
                button.Holds = icon == GlazeIcon.HoldToTalk;
                // A confirmation's Yes, as the frame draws it: solid red for what can't be taken back.
                button.On = role == ButtonRole.Destructive && words.StartsWith("Yes", StringComparison.Ordinal);
                var degrees = GlazeTokens.DegreesOf(button.Measure(words, null, icon));
                if (x + degrees > 33f)
                {
                    x = -33f;
                    y -= 5.5f;
                }
                x = Place(button, words, null, null, x, y, icon);
                button.PaintForRender(false, false);
                shown.Add((button, words + " with its icon", icon));
            }
            return shown;
        }

        /// <summary>Each meter is filled to its share of its track, or not at all while it waits for a read.</summary>
        private static IEnumerable<string> MetersFilled(List<(MeterView Meter, float Share, bool Waiting)> meters)
        {
            foreach (var (meter, share, waiting) in meters)
            {
                var expected = waiting ? 0f : share * MeterView.Width;
                if (Mathf.Abs(meter.Filled - expected) > 1e-5f)
                {
                    yield return "component render: a meter of " + share.ToString("0.00", CultureInfo.InvariantCulture) + (waiting ? " waiting" : "") + " fills "
                        + meter.Filled.ToString("0.0000", CultureInfo.InvariantCulture) + ", not " + expected.ToString("0.0000", CultureInfo.InvariantCulture) + ".";
                }
            }
        }

        /// <summary>The contrast a button's label reaches on its own fill, as drawn: its fill sampled inside its padding, its strokes at their strongest.</summary>
        private static float LabelContrast(Camera camera, Texture2D render, GlazeButton button)
        {
            var plate = button.transform;
            var inside = camera.WorldToScreenPoint(plate.TransformPoint(new Vector3(-button.Size.x / 2f + GlazeTokens.Units(0.5f), 0f, 0f)));
            var fill = render.GetPixel(Mathf.RoundToInt(inside.x), Mathf.RoundToInt(inside.y));
            var bounds = button.Label.GetComponent<Renderer>().bounds;
            var low = camera.WorldToScreenPoint(bounds.min);
            var high = camera.WorldToScreenPoint(bounds.max);
            var rect = new RectInt(Mathf.FloorToInt(Mathf.Min(low.x, high.x)), Mathf.FloorToInt(Mathf.Min(low.y, high.y)),
                Mathf.CeilToInt(Mathf.Abs(high.x - low.x)), Mathf.CeilToInt(Mathf.Abs(high.y - low.y)));
            return GlazeChecks.Contrast(render, rect, fill);
        }

        private static CharacterPresentation Character(CharacterActivity activity, AttentionLevel attention, int waiting = 0, bool stale = false,
            bool synthetic = false, bool recorded = false) =>
            new CharacterPresentation("render", "Render", activity, CharacterPresenter.LabelOf(activity), attention,
                Enumerable.Range(0, waiting).Select(index => "Waiting " + index).ToList(), waiting, synthetic, recorded, stale);

        private static Camera Camera(Transform parent, Vector3 eyes, RenderTexture texture)
        {
            var go = new GameObject("Eyes") { tag = "MainCamera" };
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(eyes, Quaternion.identity);
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.fieldOfView = Size / PixelsPerDegree;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.targetTexture = texture;
            return camera;
        }

        private static Texture2D Render(Camera camera, RenderTexture texture)
        {
            camera.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }

        private static void ForceMeshes(GameObject root)
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
        }

        /// <summary>Drawing text in the editor marks the committed font assets changed; this check must not change them.</summary>
        private static void KeepFontAssetsAsCommitted()
        {
            foreach (var font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            {
                if (!AssetDatabase.Contains(font)) continue;
                EditorUtility.ClearDirty(font);
                if (font.material != null) EditorUtility.ClearDirty(font.material);
                if (font.atlasTextures == null) continue;
                foreach (var atlas in font.atlasTextures)
                {
                    if (atlas != null) EditorUtility.ClearDirty(atlas);
                }
            }
        }
    }
}
