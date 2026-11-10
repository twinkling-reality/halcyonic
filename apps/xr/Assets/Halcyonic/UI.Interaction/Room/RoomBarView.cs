#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The redesign's instruction (DESIGN's FINISH-3, 2026-11-05), drawn from <see cref="RoomBar"/> and centred on its own
    /// origin. At rest no field and no capsule: Hold to Talk (main) and Type float side by side at home size, 32 dp apart.
    /// Held, Hold to Talk reads Listening and the microphone's real level (a <see cref="LevelHistory"/>, never an animation)
    /// stands after its words inside its halo; Type goes. A typed draft or an instruction's confirmation is a pair: the
    /// information panel (<see cref="RoomSurface"/>, 440 dp) holds the person's words, its sentence bar the one thing to say,
    /// and the column beside it the actions, left-aligned at the hand-target pitch, their feet on the panel's foot. Sent, or
    /// why nothing can be written, stands as words alone. An approval (DESIGN 2.4) is the same pair: its request in mono, at
    /// most eight lines before its confirmation and part by part under it, eight of the panel's own lines a part, what it asks in the sentence
    /// bar, Sending sweeping there once answered, and Confirm behind its lock, a ring that fills over
    /// <see cref="RoomBar.ConfirmLockSeconds"/>. It raises presses only; its owner turns them into the file's actions.
    /// </summary>
    public sealed partial class RoomBarView : MonoBehaviour
    {
        public const float LevelWidthDp = 150f;
        public const float LevelHeightDp = 28f;
        public const float LevelGapDp = 4f;
        public const float CaretSeconds = 0.53f;

        /// <summary>The plate under words that stand alone: its padding beside and over them, in dp (the agent's cursor label's own).</summary>
        public const float AlonePadXDp = 16f;
        public const float AlonePadYDp = 6f;

        /// <summary>The plate under words that stand alone.</summary>
        public RoomSurface AlonePlate => alonePlate;

        /// <summary>The row's feet under the pair's foot line, in dp: DESIGN stands the pair on pitch -17.6 and the row's feet on -23.5.</summary>
        public const float RowFeetDp = (RoomGeometry.FeetPitch - RoomGeometry.BarFootPitch) * RoomGeometry.DpPerDegree;

        /// <summary>Where the pair's foot line stands, up from this view's middle, in its own units: the line the panel and the column's lowest action stand on.</summary>
        public float Foot { get; private set; }

        /// <summary>Where the panel's place ends at the right, from this view's middle, in its own units.</summary>
        public float PanelRight { get; private set; }

        /// <summary>
        /// The column's actions at most: a question's page of four answers with the file's way on and back (DESIGN 2.9); a
        /// frame offering more stays the file's own (<see cref="KitWorkspaceHost.Holds"/>). One more place holds the band's
        /// next part.
        /// </summary>
        public const int ColumnMost = 6;

        private RoomButton talk = null!;
        private RoomButton type = null!;
        private RoomButton more = null!;
        private GlazeShimmer talkSweep = null!;
        private readonly Transform[] levelBars = new Transform[LevelHistory.Bars];
        private Transform levelRoot = null!;
        private TextMeshPro alone = null!;
        private RoomSurface alonePlate = null!;
        private GlazeShimmer aloneSweep = null!;
        private RoomSurface panel = null!;
        private TextMeshPro body = null!;
        private TextMeshPro request = null!;
        private TextMeshPro measureBody = null!;
        private TextMeshPro measureRequest = null!;
        private TextMeshPro sentence = null!;
        private GlazeShimmer sentenceSweep = null!;
        private GlazeShimmer bodySweep = null!;
        private RoomButton? bandNext;
        private RoomButton? locking;
        private float lockedAt;
        private float lockAged;
        private bool lockLifted;
        private Surface caret = null!;
        private KitPress field = null!;
        private readonly List<RoomButton> column = new List<RoomButton>();
        private readonly Dictionary<RoomButton, KitControl> controls = new Dictionary<RoomButton, KitControl>();
        private RoomBar? shown;

        /// <summary>An action pressed: the file's control at its place. Hold to Talk and Talk Again raise <see cref="TalkPressed"/> instead.</summary>
        public event Action<KitControl>? Acted;

        /// <summary>Hold to Talk or Talk Again pressed: held, it records (the file's held Talk).</summary>
        public event Action? TalkPressed;

        /// <summary>Hold to Talk or Talk Again let go, or dropped (true).</summary>
        public event Action<bool>? TalkReleased;

        /// <summary>The microphone's real level while it records, or null where nothing records (a release build).</summary>
        public Func<LevelHistory?> Level { get; set; } = () => null;

        /// <summary>Hold to Talk, the same action while it listens, so the hand that holds it keeps holding it.</summary>
        public RoomButton Talk => talk;

        /// <summary>The row's third action, after Type, where the model gives one; hidden otherwise.</summary>
        public RoomButton More => more;

        public RoomButton Type => type;

        public IReadOnlyList<RoomButton> Column => column;

        public RoomSurface Panel => panel;

        public TextMeshPro Body => body;

        /// <summary>The request's exact text, in mono, where the panel holds one.</summary>
        public TextMeshPro Request => request;

        /// <summary>The action standing behind Confirm's lock now, or null.</summary>
        public RoomButton? Locking => locking != null && !lockLifted ? locking : null;

        /// <summary>Whether the sentence sweeps: a state under way.</summary>
        public bool SentenceWaits => sentenceSweep.Waits;

        /// <summary>Whether the panel's one line sweeps: a state under way with no sentence bar.</summary>
        public bool BodyWaits => bodySweep.Waits;

        /// <summary>
        /// The words of the way to the next part of the agent's words on the band (the band's own, as "Next Part · 2 of 3"),
        /// set by the band's owner before <see cref="Show"/>; null where the band shows its last part or nothing. It stands
        /// as the column's last action and raises <see cref="BandNextPressed"/>.
        /// </summary>
        public string? BandNext { get; set; }

        /// <summary>The action that turns the band to its next part, where it stands now.</summary>
        public RoomButton? BandNextButton => bandNext != null && bandNext.gameObject.activeSelf ? bandNext : null;

        public event Action? BandNextPressed;

        public TextMeshPro Sentence => sentence;

        public TextMeshPro Alone => alone;

        public KitPress Field => field;

        /// <summary>Whether the caret stands after the words now.</summary>
        public bool CaretShown => caret.gameObject.activeSelf;

        public RoomBar? Shown => shown;

        /// <summary>Whether the level's bars stand now.</summary>
        public bool LevelShown => levelRoot.gameObject.activeSelf;

        /// <summary>A level bar's height now, 0.2 to 1 of the level's height, for the renders.</summary>
        public float LevelBar(int bar) => levelBars[bar].localScale.y;

        /// <summary>What it holds now, as laid, in its own units, centred on its origin.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>Every press it shows now.</summary>
        public IEnumerable<KitPress> Presses => new[] { talk.Press, type.Press, more.Press, field }.Concat(column.Select(button => button.Press))
            .Where(press => press.gameObject.activeInHierarchy);

        /// <summary>
        /// The shown action that raises the file's <paramref name="action"/> (with <paramref name="key"/>, where given), for
        /// the renders and journeys, which press it as a hand does (<see cref="KitPress.PressForRender"/>); null where none
        /// shows. Hold to Talk, which is held, is <see cref="Talk"/>.
        /// </summary>
        public RoomButton? Button(string action, string? key = null) => controls
            .FirstOrDefault(each => each.Key.gameObject.activeInHierarchy && each.Value.Action == action && (key == null || each.Value.Key == key)).Key;

        /// <summary>The control a shown button raises, for the renders; null where it raises none.</summary>
        public KitControl? ControlOf(RoomButton button) => controls.TryGetValue(button, out var control) ? control : null;

        public static RoomBarView Create(Transform parent)
        {
            var go = new GameObject("Instruction");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomBarView>();
            view.talk = RoomButton.Create(go.transform, "Hold to Talk", RoomButtonSize.Home);
            view.talk.Pressed += () => view.TalkPressed?.Invoke();
            view.talk.Press.Target.Released += cancelled => view.TalkReleased?.Invoke(cancelled);
            view.talkSweep = GlazeShimmer.On(view.talk.Words);
            view.type = RoomButton.Create(go.transform, "Type", RoomButtonSize.Home);
            view.type.Pressed += () => view.Raise(view.type);
            // A third action of the row, where the model gives one (the agent and its level): action size, on the row's line.
            view.more = RoomButton.Create(go.transform, "Row action", RoomButtonSize.Action);
            view.more.Pressed += () => view.Raise(view.more);
            view.levelRoot = new GameObject("Level").transform;
            view.levelRoot.SetParent(go.transform, false);
            var slot = RoomType.Units(LevelWidthDp) / LevelHistory.Bars;
            var full = RoomType.Units(LevelHeightDp);
            for (var index = 0; index < LevelHistory.Bars; index++)
            {
                // Each bar drawn once at the level's full height about its centre, then scaled: a frame draws nothing.
                var holder = new GameObject("Bar " + index).transform;
                holder.SetParent(view.levelRoot, false);
                holder.localPosition = new Vector3(slot * (index + 0.5f), 0f, 0f);
                var surface = Surface.Create(holder, "Bar", KitType.Order.Control + 4);
                var barWidth = slot / 2f;
                surface.Draw(new Vector2(barWidth, full), Mathf.Min(RoomType.Units(2f), barWidth / 2f), new Color(1f, 1f, 1f, 0.9f));
                holder.localScale = new Vector3(1f, 0.2f, 1f);
                view.levelBars[index] = holder;
            }
            view.levelRoot.gameObject.SetActive(false);
            view.alone = RoomType.Create(go.transform, "Words alone", Color.white, KitType.Order.Words, TextAlignmentOptions.Center);
            view.alone.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            view.alone.textWrappingMode = TextWrappingModes.NoWrap;
            view.aloneSweep = GlazeShimmer.On(view.alone);
            view.panel = RoomSurface.Create(go.transform, "Information panel", RoomSurfaceKind.Information, KitType.Order.Plate);
            view.alonePlate = RoomSurface.Create(go.transform, "Words alone plate", RoomSurfaceKind.Bar, KitType.Order.Plate);
            view.alonePlate.gameObject.SetActive(false);
            view.body = RoomType.Create(view.panel.transform, "Words", Color.white, KitType.Order.Words);
            view.body.textWrappingMode = TextWrappingModes.Normal;
            view.bodySweep = GlazeShimmer.On(view.body);
            view.request = RoomType.CreateMono(view.panel.transform, "Request", Color.white, KitType.Order.Words);
            view.request.textWrappingMode = TextWrappingModes.Normal;
            view.request.overflowMode = TextOverflowModes.Overflow;
            view.request.gameObject.SetActive(false);
            // Never drawn: what counts the panel's lines for the file, laid exactly as the words above are.
            view.measureBody = RoomType.Create(go.transform, "Words measure", Color.white, KitType.Order.Words);
            view.measureBody.textWrappingMode = TextWrappingModes.Normal;
            view.measureBody.GetComponent<MeshRenderer>().enabled = false;
            view.measureRequest = RoomType.CreateMono(go.transform, "Request measure", Color.white, KitType.Order.Words);
            view.measureRequest.textWrappingMode = TextWrappingModes.Normal;
            view.measureRequest.overflowMode = TextOverflowModes.Overflow;
            view.measureRequest.GetComponent<MeshRenderer>().enabled = false;
            view.sentence = RoomType.Create(view.panel.transform, "Sentence", Color.white, KitType.Order.Words);
            view.sentence.textWrappingMode = TextWrappingModes.Normal;
            view.sentenceSweep = GlazeShimmer.On(view.sentence);
            view.caret = Surface.Create(view.panel.transform, "Caret", KitType.Order.Words);
            view.caret.Draw(new Vector2(RoomType.Units(RoomGeometry.CaretDp), RoomType.Units(RoomGeometry.CaretHeightDp)), RoomType.Units(1f), Color.white);
            var fieldGo = new GameObject("Field");
            fieldGo.transform.SetParent(view.panel.transform, false);
            view.field = KitPress.Create(fieldGo, Vector2.one * RoomType.Units(RoomButton.HeightDp));
            view.field.Pressed += () => { if (view.shown?.Field is KitControl control) view.Acted?.Invoke(control); };
            for (var index = 0; index < ColumnMost + 1; index++)
            {
                var button = RoomButton.Create(go.transform, "Action " + index);
                button.Pressed += () => view.Raise(button);
                button.Press.Target.Released += cancelled =>
                {
                    if (view.controls.TryGetValue(button, out var control) && control.Place == KitPlace.Talk) view.TalkReleased?.Invoke(cancelled);
                };
                view.column.Add(button);
            }
            go.SetActive(false);
            return view;
        }

        private void Raise(RoomButton button)
        {
            if (ReferenceEquals(button, bandNext))
            {
                BandNextPressed?.Invoke();
                return;
            }
            if (!controls.TryGetValue(button, out var control)) return;
            if (control.Place == KitPlace.Talk) TalkPressed?.Invoke();
            else Acted?.Invoke(control);
        }

        /// <summary>Shows <paramref name="bar"/>, centred on its origin; hides for <see cref="RoomBarState.Hidden"/>.</summary>
        public void Show(RoomBar bar)
        {
            if (bar.State == RoomBarState.Hidden && !bar.Working)
            {
                Hide();
                return;
            }
            shown = bar;
            controls.Clear();
            bandNext = null;
            gameObject.SetActive(true);
            if (bar.Pair) LayPair(bar);
            else
            {
                locking = null;
                panel.gameObject.SetActive(false);
                foreach (var button in column) button.Hide();
                if (bar.Working) LayWork(bar);
                else if (bar.Row.Count > 0) LayRow(bar, 0f);
                else LayAlone(bar);
                if (!bar.Working)
                {
                    // With no pair the foot line is still DESIGN's, over the row's feet.
                    Foot = -Size.y / 2f + RoomType.Units(RowFeetDp);
                    PanelRight = -(RoomType.Units(RoomGeometry.PanelDp) + RoomType.Units(RoomGeometry.PairGapDp) + RoomType.Units(RoomGeometry.ColumnDp)) / 2f + RoomType.Units(RoomGeometry.PanelDp);
                }
            }
        }

        /// <summary>
        /// At work (DESIGN 2.3): no panel, its place left to the working field; Stop alone where the column stands, its core
        /// on the foot line, a rest action; and the row, where the file still offers an instruction, centred under the foot
        /// line with its feet where DESIGN stands them. The whole is centred on this view's middle.
        /// </summary>
        private void LayWork(RoomBar bar)
        {
            alone.gameObject.SetActive(false);
            alonePlate.gameObject.SetActive(false);
            var panelWidth = RoomType.Units(RoomGeometry.PanelDp);
            var gap = RoomType.Units(RoomGeometry.PairGapDp);
            var target = RoomType.Units(RoomButton.HeightDp);
            var stops = bar.Column.Count > 0;
            var core = stops ? column[0].Measure(bar.Column[0].Words, 0f) : Vector2.zero;
            var total = panelWidth + gap + Mathf.Max(RoomType.Units(RoomGeometry.ColumnDp), core.x);
            var rowed = bar.Row.Count > 0;
            // From the foot line: the column's one action above it, the row's feet under it.
            var above = stops ? Mathf.Max(target, core.y) : 0f;
            var below = rowed ? RoomType.Units(RowFeetDp) : 0f;
            var foot = (below - above) / 2f;
            if (rowed) LayRow(bar, foot - below + target / 2f);
            else
            {
                talk.Hide();
                type.Hide();
            more.Hide();
                levelRoot.gameObject.SetActive(false);
            }
            var rowWidth = rowed ? Size.x : 0f;
            if (stops)
            {
                var action = bar.Column[0];
                column[0].Show(action.Words, action.Icon, action.Main, available: true, new Vector2(-total / 2f + panelWidth + gap + core.x / 2f, foot + core.y / 2f), core.x);
                controls[column[0]] = action.Control;
            }
            Foot = foot;
            PanelRight = -total / 2f + panelWidth;
            Size = new Vector2(Mathf.Max(total, rowWidth), above + below);
        }

        /// <summary>For renders: the level as it is now, as a person sees it, and the caret standing.</summary>
        public void SettleForRender()
        {
            FollowLevel();
            if (shown?.Caret == true) caret.gameObject.SetActive(true);
        }

        /// <summary>For renders, which have no clock: as if <paramref name="seconds"/> more had passed since Confirm arrived.</summary>
        public void AgeLockForRender(float seconds)
        {
            lockAged += seconds;
            FollowLock();
        }

        public void Hide()
        {
            shown = null;
            controls.Clear();
            locking = null;
            bandNext = null;
            // Each part goes off with it, so nothing of a moment that ended reads as standing under the hidden whole.
            talk.Hide();
            type.Hide();
            more.Hide();
            foreach (var button in column) button.Hide();
            panel.gameObject.SetActive(false);
            alone.gameObject.SetActive(false);
            alonePlate.gameObject.SetActive(false);
            levelRoot.gameObject.SetActive(false);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Confirm's lock: from its arrival its ring fills over <see cref="RoomBar.ConfirmLockSeconds"/> while it takes no press,
        /// then it is the main action. The time since is taken first, so an unchanged clock gives exactly none.
        /// </summary>
        private void FollowLock()
        {
            if (locking == null || lockLifted) return;
            var progress = (Time.unscaledTime - lockedAt + lockAged) / RoomBar.ConfirmLockSeconds;
            if (progress >= 1f)
            {
                lockLifted = true;
                locking.Lock(null);
            }
            else locking.Lock(Mathf.Max(0f, progress));
        }

        private void Update()
        {
            if (shown == null) return;
            FollowLevel();
            FollowLock();
            if (shown.Caret && !GlazeMotion.Still)
            {
                var on = Mathf.Repeat(Time.unscaledTime, 2f * CaretSeconds) < CaretSeconds;
                if (caret.gameObject.activeSelf != on) caret.gameObject.SetActive(on);
            }
        }

        /// <summary>Each bar the level of its 50 ms, 20 to 100 percent of the level's height.</summary>
        private void FollowLevel()
        {
            if (!levelRoot.gameObject.activeSelf || Level() is not LevelHistory level) return;
            for (var index = 0; index < LevelHistory.Bars; index++)
            {
                var height = 0.2f + 0.8f * level[index];
                var holder = levelBars[index];
                if (!Mathf.Approximately(holder.localScale.y, height)) holder.localScale = new Vector3(1f, height, 1f);
            }
        }

        /// <summary>The row: its actions side by side at home size, 32 dp apart, centred; the level inside Listening's halo.</summary>
        private void LayRow(RoomBar bar, float up)
        {
            alone.gameObject.SetActive(false);
            alonePlate.gameObject.SetActive(false);
            var listening = bar.State == RoomBarState.Listening && Level() != null;
            var buttons = new List<(RoomButton Button, RoomAction Action, float Width)>();
            foreach (var action in bar.Row)
            {
                // Hold to Talk, then Type (or what stands in its place), then a third action where the model gives one.
                var button = action.Place == KitPlace.Talk ? talk : action.Place == KitPlace.Field ? type : more;
                var width = button.Measure(action.Words) + (button == talk && listening ? RoomType.Units(LevelGapDp + LevelWidthDp) : 0f);
                buttons.Add((button, action, width));
            }
            var gap = RoomType.Units(RoomGeometry.RowGapDp);
            var total = buttons.Sum(each => each.Width) + gap * (buttons.Count - 1);
            var left = -total / 2f;
            foreach (var (button, action, width) in buttons)
            {
                button.Show(action.Words, action.Icon, action.Main, available: true, new Vector2(left + width / 2f, up), width);
                if (button != talk) controls[button] = action.Control;
                left += width + gap;
            }
            if (!bar.Row.Any(action => action.Place == KitPlace.Talk)) talk.Hide();
            if (!bar.Row.Any(action => action.Place == KitPlace.Field)) type.Hide();
            if (!bar.Row.Any(action => action.Place != KitPlace.Talk && action.Place != KitPlace.Field)) more.Hide();
            talkSweep.Waits = bar.State is RoomBarState.Listening or RoomBarState.Transcribing;
            levelRoot.gameObject.SetActive(listening);
            if (listening)
            {
                // After the words, inside the halo: its right edge where the core's padding starts.
                var right = talk.transform.localPosition.x + talk.Core.x / 2f - RoomType.Units(talk.RightDp);
                levelRoot.localPosition = new Vector3(right - RoomType.Units(LevelWidthDp), up, -RoomType.Units(1.5f));
                FollowLevel();
            }
            Size = new Vector2(total, RoomType.Units(RoomButton.HeightDp));
        }

        /// <summary>Words alone, centred: Sent · Awaiting Start, sweeping while it waits, or why nothing can be written.</summary>
        private void LayAlone(RoomBar bar)
        {
            talk.Hide();
            type.Hide();
            more.Hide();
            levelRoot.gameObject.SetActive(false);
            RoomType.Size(alone);
            GlazeText.SetLiteral(alone, bar.Words.Text);
            var width = alone.GetPreferredValues(alone.text).x + RoomType.Units(1f);
            KitType.Lay(alone, width, 1);
            // The label is laid about its own middle, which is this view's.
            alone.transform.localPosition = new Vector3(0f, 0f, -RoomType.Units(0.5f));
            aloneSweep.Waits = bar.State == RoomBarState.Sent;
            alone.gameObject.SetActive(true);
            // Words that stand alone stand on a plate of the panel's material, one line, as a name tag does: with nothing
            // under them they are lost over a bright wall or a lit screen behind the room.
            var plate = new Vector2(width + 2f * RoomType.Units(AlonePadXDp), RoomType.Units(RoomGeometry.LineDp) + 2f * RoomType.Units(AlonePadYDp));
            alonePlate.transform.localPosition = Vector3.zero;
            alonePlate.Draw(plate);
            alonePlate.gameObject.SetActive(true);
            Size = new Vector2(plate.x, RoomType.Units(RoomButton.HeightDp));
        }

        /// <summary>The width the panel's words wrap in: its 440 dp less its padding at each side, which grows with the text.</summary>
        private static float BodyWidth => RoomType.Units(RoomGeometry.PanelDp) - 2f * RoomType.Scaled(RoomGeometry.PanelPadDp);

        /// <summary>Sets what the panel reads on <paramref name="label"/> as it stands there: a request literal in mono, else words by their runs, as prose.</summary>
        private static void Set(TextMeshPro label, LineWords words, bool mono)
        {
            if (mono)
            {
                RoomType.SizeMono(label);
                GlazeText.SetLiteral(label, words.Text);
                return;
            }
            RoomType.Size(label);
            var runs = GlazeRuns.Of(label);
            runs.Set(words);
            runs.SetProseMode(true);
        }

        /// <summary>
        /// The lines the panel wraps <paramref name="words"/> to at the person's text size, as it would stand them (a request in
        /// mono, else prose): what the file counts a request's or an instruction's rows by while the panel draws its
        /// confirmation, so the file's parts are the panel's own lines, eight a part.
        /// </summary>
        public int Lines(LineWords words, bool mono)
        {
            var label = mono ? measureRequest : measureBody;
            Set(label, words, mono);
            KitType.Lay(label, BodyWidth, 200);
            var lines = Mathf.Max(1, label.textInfo.lineCount);
            // The measure holds no words once it has counted them: it is never drawn, and nothing should read it as shown.
            if (mono) label.text = "";
            else GlazeRuns.Of(label).Set(LineWords.None);
            return lines;
        }

        /// <summary>
        /// Lays <paramref name="label"/> so that no line ends in the separator: where one would, the break stands in the
        /// separator's place, the dot dropped (WORDS rule 2). For labels of Halcyonic's own words, set literally.
        /// </summary>
        private static (int Lines, float Width, float Height) BreakAtSeparators(TextMeshPro label, float width, int maxLines)
        {
            var laid = KitType.Lay(label, width, maxLines);
            for (var pass = 0; pass < 8; pass++)
            {
                var info = label.textInfo;
                var at = -1;
                for (var index = 0; index < info.lineCount - 1 && at < 0; index++)
                {
                    var last = info.characterInfo[info.lineInfo[index].lastVisibleCharacterIndex];
                    if (last.character == '·') at = last.index;
                }
                var text = label.text;
                if (at < 1 || at + 1 >= text.Length || text[at - 1] != ' ' || text[at + 1] != ' ') break;
                label.text = text.Substring(0, at - 1) + "\n" + text.Substring(at + 2);
                laid = KitType.Lay(label, width, maxLines);
            }
            return laid;
        }

        /// <summary>The label's own text of its lines from <paramref name="from"/>, <paramref name="rows"/> of them at most, as it is laid now, never cut inside a doubled backslash.</summary>
        private static string Rows(TextMeshPro label, int from, int rows)
        {
            var text = label.text;
            var info = label.textInfo;
            var count = Mathf.Max(1, info.lineCount);
            var first = Mathf.Min(from, count - 1);
            var last = (int)Mathf.Min((long)from + rows, count);
            var start = LabelText.CharacterStart(text, info.characterInfo[info.lineInfo[first].firstCharacterIndex].index);
            var end = last < count ? LabelText.CharacterStart(text, info.characterInfo[info.lineInfo[last].firstCharacterIndex].index) : text.Length;
            return text.Substring(start, end - start).TrimEnd();
        }

        /// <summary>The pair: the panel at the left with the words and the sentence bar at its foot, the column at its right.</summary>
        private void LayPair(RoomBar bar)
        {
            talk.Hide();
            type.Hide();
            more.Hide();
            alone.gameObject.SetActive(false);
            alonePlate.gameObject.SetActive(false);
            levelRoot.gameObject.SetActive(false);
            var unit = RoomType.Units(1f);
            var panelWidth = RoomType.Units(RoomGeometry.PanelDp);
            // Larger text scales every length inside the panel, its padding too; the panel's width stays (DESIGN's units).
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var inset = RoomType.Scaled(RoomGeometry.SentenceInsetDp);
            var line = RoomType.Scaled(RoomGeometry.LineDp);

            var wordsWidth = BodyWidth;
            // What the panel reads: a request in mono, exact and literal, or the person's words; under a confirmation the rows
            // the file's part names, of the panel's own lines; a request before its answer at most eight of them. Cut between
            // lines, never inside one.
            var reading = bar.Mono ? request : body;
            body.gameObject.SetActive(false);
            request.gameObject.SetActive(false);
            Set(reading, bar.Body, bar.Mono);
            if (bar.Lines != null && !bar.Mono)
            {
                // The file's own lines, each on a line of its own, literal: no line then ends in a separator (WORDS rule 2).
                GlazeRuns.Of(reading).Set(LineWords.None);
                reading.richText = false;
                reading.parseCtrlCharacters = true;
                reading.text = string.Join("\n", bar.Lines.Select(each => each.ForTextMeshPro().Text));
            }
            var laid = KitType.Lay(reading, wordsWidth, 200);
            // Halcyonic's own words break only between segments and drop the dot there (WORDS rule 2). Only where no outside
            // words stand in the label, whose places in its text a changed text would move.
            if (!bar.Mono && bar.Part == null && GlazeRuns.Of(reading).Theirs.Count == 0) laid = BreakAtSeparators(reading, wordsWidth, 200);
            var from = bar.Part?.FromRow ?? 0;
            var most = bar.Part != null ? bar.Part.Rows : bar.Mono ? RoomBar.RequestLines : int.MaxValue;
            if (from > 0 || reading.textInfo.lineCount > most)
            {
                // The piece is the label's own spelling, each backslash doubled: made words again before the label takes them, so
                // the person reads exactly what will be sent or run (AUDIT, 2026-11-10).
                Set(reading, LineWords.Theirs(LabelText.FromTextMeshPro(Rows(reading, from, most))), bar.Mono);
                laid = KitType.Lay(reading, wordsWidth, 200);
            }
            var empty = bar.Body.Text.Length == 0;
            var bodyHeight = Mathf.Max(line, empty ? line : laid.Height);
            // Where the bar gives rows, FIELD's rows draw them (each under a hairline, a mark before a step's words) and decide the height.
            var rowsTall = LayFacts(bar, wordsWidth);
            if (rowsTall is float tall) bodyHeight = Mathf.Max(line, tall);
            bodySweep.Waits = bar.Waits && bar.Sentence == null && !bar.Mono && !empty;

            float barHeight = 0f;
            if (bar.Sentence is string said)
            {
                RoomType.Size(sentence);
                GlazeText.SetLiteral(sentence, said);
                var saidHeight = BreakAtSeparators(sentence, panelWidth - 2f * inset - 2f * RoomType.Scaled(RoomGeometry.SentencePadXDp), 3).Height;
                barHeight = Mathf.Max(RoomType.Scaled(RoomGeometry.SentenceDp), saidHeight + 2f * RoomType.Scaled(RoomGeometry.SentencePadYDp));
            }
            sentence.gameObject.SetActive(bar.Sentence != null);
            sentenceSweep.Waits = bar.Sentence != null && bar.Waits;
            // The body 28 dp in from the top and sides; 18 dp under it before the sentence bar, which stands 14 dp in; 28 dp under it with no bar.
            // With nothing from the computer to say there is no panel: the choices take its place.
            var panelHeight = bar.Wide ? 0f : pad + bodyHeight + (barHeight > 0f ? RoomType.Scaled(RoomGeometry.BodyFootDp) + barHeight + inset : pad);

            // The column: the lowest action's core stands on the panel's foot line as DESIGN's page stands it (its target reaches
            // 6.4 dp under the line), the others above it, targets 12 mm apart. An answer's words may wrap, so an action may
            // stand taller than a line: each is laid by its own height. Last, where the
            // band holds more of the agent's words, the way to its next part, the main action where the file offers none.
            var target = RoomType.Units(RoomButton.HeightDp);
            // An answer wraps inside the information panel's width wherever it stands (DESIGN 1.4): the column's actions are content-sized.
            var wrap = panelWidth;
            var count = Mathf.Min(bar.Column.Count, ColumnMost);
            var paging = bar.Band != null && BandNext != null;
            var places = count + (paging ? 1 : 0);
            var cores = new Vector2[places];
            var widest = 0f;
            for (var index = 0; index < places; index++)
            {
                cores[index] = index < count
                    ? column[index].Measure(bar.Column[index].Words, bar.Column[index].Mark != AnswerMark.None ? wrap : 0f)
                    : column[index].Measure(BandNext!, 0f);
                widest = Mathf.Max(widest, cores[index].x);
            }
            var rises = new float[places];
            for (var index = places - 1; index >= 0; index--)
                rises[index] = index == places - 1 ? cores[index].y / 2f
                    : rises[index + 1] + Mathf.Max(target, cores[index + 1].y) / 2f + KitType.Units(SpatialKit.TargetGapDegrees) + Mathf.Max(target, cores[index].y) / 2f;
            var columnHeight = places == 0 ? 0f : rises[0] + Mathf.Max(target, cores[0].y) / 2f;
            var gap = RoomType.Units(RoomGeometry.PairGapDp);
            var total = bar.Wide ? Mathf.Max(panelWidth + gap + RoomType.Units(RoomGeometry.ColumnDp), widest)
                : panelWidth + gap + Mathf.Max(RoomType.Units(RoomGeometry.ColumnDp), widest);
            var height = Mathf.Max(panelHeight, columnHeight);
            var foot = -height / 2f;
            var left = -total / 2f;

            panel.InnerBar = barHeight > 0f ? ((panelHeight - inset - barHeight) / unit, barHeight / unit, inset / unit) : ((float, float, float)?)null;
            panel.transform.localPosition = new Vector3(left + panelWidth / 2f, foot + panelHeight / 2f, 0f);
            if (!bar.Wide) panel.Draw(new Vector2(panelWidth, panelHeight));
            panel.gameObject.SetActive(!bar.Wide);
            var top = panelHeight / 2f;
            body.transform.localPosition = new Vector3(-panelWidth / 2f + pad, top - pad, -RoomType.Units(0.5f));
            request.transform.localPosition = body.transform.localPosition;
            reading.gameObject.SetActive(!empty && rowsTall == null);
            PlaceFacts(body.transform.localPosition);
            if (bar.Sentence != null)
                sentence.transform.localPosition = new Vector3(-panelWidth / 2f + inset + RoomType.Scaled(RoomGeometry.SentencePadXDp),
                    -panelHeight / 2f + inset + barHeight - RoomType.Scaled(RoomGeometry.SentencePadYDp), -RoomType.Units(0.5f));

            // The caret after the last word, or at the start of an empty field.
            var caretAt = new Vector2(-panelWidth / 2f + pad, top - pad - line / 2f);
            if (!empty && !bar.Mono && body.textInfo.characterCount > 0)
            {
                var info = body.textInfo;
                var last = info.characterInfo[info.characterCount - 1];
                var lastLine = info.lineInfo[Mathf.Max(0, info.lineCount - 1)];
                caretAt = new Vector2(body.transform.localPosition.x + last.xAdvance + RoomType.Units(2f),
                    body.transform.localPosition.y + (lastLine.ascender + lastLine.descender) / 2f);
            }
            caret.transform.localPosition = new Vector3(caretAt.x, caretAt.y, -RoomType.Units(0.6f));
            caret.gameObject.SetActive(bar.Caret);

            // The field's target: the panel above its sentence bar, ending 12 mm before the column's targets (the pair's gap is 16 dp).
            field.gameObject.SetActive(bar.Field != null);
            if (bar.Field != null)
            {
                var fieldHeight = panelHeight - (barHeight > 0f ? barHeight + inset : 0f);
                var fieldWidth = Mathf.Min(panelWidth, panelWidth + gap - KitType.Units(SpatialKit.TargetGapDegrees));
                field.transform.localPosition = new Vector3(-panelWidth / 2f + fieldWidth / 2f, top - fieldHeight / 2f, -RoomType.Units(0.5f));
                field.Resize(new Vector2(fieldWidth, Mathf.Max(fieldHeight, RoomType.Units(RoomButton.HeightDp))));
                field.Available = true;
            }

            var columnLeft = bar.Wide ? left : left + panelWidth + gap;
            RoomButton? locks = null;
            for (var index = 0; index < column.Count; index++)
            {
                var button = column[index];
                if (index >= places)
                {
                    button.Hide();
                    continue;
                }
                var centre = new Vector2(columnLeft + cores[index].x / 2f, foot + rises[index]);
                if (index >= count)
                {
                    // The band's next part: the way on while no other stands, since the agent's words are read before they are answered.
                    button.Show(BandNext!, GlazeIcon.Next, !bar.Column.Any(each => each.Main), available: true, centre, cores[index].x);
                    bandNext = button;
                    continue;
                }
                var action = bar.Column[index];
                button.Show(action.Words, action.Icon, action.Main, available: true, centre, cores[index].x, action.Mark, action.Chosen,
                    action.Mark != AnswerMark.None ? wrap : 0f);
                controls[button] = action.Control;
                if (action.Locks) locks = button;
            }
            // Confirm's lock starts when it arrives and holds through the frames the file draws again while it stands.
            if (locks == null) locking = null;
            else if (!ReferenceEquals(locking, locks))
            {
                locking = locks;
                lockedAt = Time.unscaledTime;
                lockAged = 0f;
                lockLifted = false;
            }
            if (locking != null && !lockLifted) FollowLock();
            Foot = foot;
            PanelRight = left + panelWidth;
            Size = new Vector2(total, height);
        }
    }
}
