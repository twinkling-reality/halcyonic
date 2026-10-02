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
                failures.AddRange(EveryIconShows(badges, marks, actions));
                failures.AddRange(GlazeChecks.MicrophoneOnlyWhereHeld(actions.Select(action => action.Button), "component render"));
                failures.AddRange(IconAloneKeepsPresses());
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
            failures.AddRange(GlazeChecks.TargetsLargeEnough(buttons.Where(button => !button.Button.Static).Select(button => button.Button), eyes, "component render"));
            failures.AddRange(GlazeChecks.NothingCut(root.GetComponentsInChildren<TMP_Text>(false), "component render"));
            failures.AddRange(GlazeChecks.IconsBesideWords(root, eyes, "component render"));
            foreach (var label in root.GetComponentsInChildren<TMP_Text>(false)) failures.AddRange(GlazeChecks.NotFromIcons(label, "component render"));
            foreach (var (button, what) in buttons)
            {
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
        private static IEnumerable<string> EveryIconShows(List<StateBadgeView> badges, List<MarkTag> marks, List<(GlazeButton Button, string What, GlazeIcon Icon)> actions)
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
            foreach (GlazeIcon icon in Enum.GetValues(typeof(GlazeIcon)))
            {
                if (!shown.Contains(icon)) yield return "component render: nothing in the gallery shows the " + icon + " icon.";
            }
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
