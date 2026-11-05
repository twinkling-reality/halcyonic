#nullable enable
using System;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The redesign's Type or Talk bar (DESIGN's SYSTEM-v4 and its numbers of 2026-11-05), drawn from <see cref="RoomBar"/>: a
    /// capsule 64 dp tall, corner 30, 230 dp wide at rest, 340 dp while it listens or says it sent, the pair's width open,
    /// growing upward with the draft; 6 dp inside its edge, the words 18 dp in, 12 dp above and below; the mic and Send at its
    /// right, 52 dp each, standing in Meta's hand targets 12 mm apart. While it listens, the microphone's real level (a
    /// <see cref="LevelHistory"/>, never an animation) in 26 bars before "Listening"; with none, no bars. "Sent · Awaiting
    /// Start", "Listening" and "Transcribing" sweep (<see cref="GlazeShimmer"/>). Its width moves over 360 ms; kept still it
    /// stands. It raises presses only; its owner turns them into the file's actions.
    /// </summary>
    public sealed class RoomBarView : MonoBehaviour
    {
        /// <summary>The draft's lines before the composer's own rule pages it: the kit composer's rows for now, so what the kit holds is unchanged (DESIGN's 4 come with the choices panel, unit 2).</summary>
        public const int DraftLines = 3;

        public const float InsetDp = 6f;
        public const float WordsInsetDp = 18f;
        public const float WordsGapDp = 12f;
        public const float ControlGapDp = 12f;
        public const float LevelWidthDp = 150f;
        public const float LevelHeightDp = 28f;
        public const float LevelInsetDp = 16f;
        public const float LevelGapDp = 14f;
        public const float WidthSeconds = 0.36f;

        private RoomSurface capsule = null!;
        private TextMeshPro words = null!;
        private GlazeShimmer sweep = null!;
        private RoomButton mic = null!;
        private RoomButton send = null!;
        private KitPress field = null!;
        private readonly Transform[] levelBars = new Transform[LevelHistory.Bars];
        private Transform levelRoot = null!;
        private RoomBar? shown;
        private Vector2 topCentre;
        private float width = -1f;
        private float fromWidth;
        private float targetWidth;
        private float movedAt;
        private bool opening;

        /// <summary>The capsule's words pressed: open to write at rest, the keyboard while open.</summary>
        public event Action? FieldPressed;

        public event Action? SendPressed;

        /// <summary>The microphone's real level while it records, or null where nothing records (a release build).</summary>
        public Func<LevelHistory?> Level { get; set; } = () => null;

        public RoomButton Mic => mic;

        public RoomButton Send => send;

        public KitPress Field => field;

        public TextMeshPro Words => words;

        public RoomSurface Capsule => capsule;

        public RoomBar? Shown => shown;

        /// <summary>Whether the level's bars stand now.</summary>
        public bool LevelShown => levelRoot.gameObject.activeSelf;

        /// <summary>A level bar's height now, 0.2 to 1 of the level's height, for the renders.</summary>
        public float LevelBar(int bar) => levelBars[bar].localScale.y;

        /// <summary>The capsule's size as last drawn, in its own units.</summary>
        public Vector2 Size => capsule.Size;

        public static RoomBarView Create(Transform parent)
        {
            var go = new GameObject("Type or Talk bar");
            go.transform.SetParent(parent, false);
            var bar = go.AddComponent<RoomBarView>();
            bar.capsule = RoomSurface.Create(go.transform, "Capsule", RoomSurfaceKind.Bar, KitType.Order.Plate);
            bar.words = RoomType.Create(go.transform, "Words", Color.white, KitType.Order.Words);
            bar.words.textWrappingMode = TextWrappingModes.Normal;
            bar.sweep = GlazeShimmer.On(bar.words);
            var fieldGo = new GameObject("Field");
            fieldGo.transform.SetParent(go.transform, false);
            bar.field = KitPress.Create(fieldGo, Vector2.one * RoomType.Units(RoomGeometry.BarHeightDp));
            bar.field.Pressed += () => bar.FieldPressed?.Invoke();
            bar.mic = RoomButton.Create(go.transform, "Mic", round: true);
            bar.send = RoomButton.Create(go.transform, "Send", round: true);
            bar.send.Pressed += () => bar.SendPressed?.Invoke();
            bar.levelRoot = new GameObject("Level").transform;
            bar.levelRoot.SetParent(go.transform, false);
            var slot = RoomType.Units(LevelWidthDp) / LevelHistory.Bars;
            var full = RoomType.Units(LevelHeightDp);
            for (var index = 0; index < LevelHistory.Bars; index++)
            {
                // Each bar drawn once at the level's full height about its centre, then scaled: a frame draws nothing.
                var holder = new GameObject("Bar " + index).transform;
                holder.SetParent(bar.levelRoot, false);
                holder.localPosition = new Vector3(slot * (index + 0.5f), 0f, 0f);
                var surface = Surface.Create(holder, "Bar", KitType.Order.Words);
                var barWidth = slot / 2f;
                surface.Draw(new Vector2(barWidth, full), Mathf.Min(RoomType.Units(2f), barWidth / 2f), new Color(1f, 1f, 1f, 0.9f));
                holder.localScale = new Vector3(1f, 0.2f, 1f);
                bar.levelBars[index] = holder;
            }
            bar.levelRoot.gameObject.SetActive(false);
            go.SetActive(false);
            return bar;
        }

        /// <summary>Shows <paramref name="bar"/>, the capsule's top centre at <paramref name="top"/> in its parent's units.</summary>
        public void Show(RoomBar bar, Vector2 top)
        {
            var target = RoomType.Units(bar.WidthDp);
            if (width < 0f || GlazeMotion.Still) width = target;
            else if (!Mathf.Approximately(target, targetWidth))
            {
                fromWidth = width;
                movedAt = Time.unscaledTime;
                opening = target > width;
            }
            targetWidth = target;
            shown = bar;
            topCentre = top;
            sweep.Waits = bar.State is RoomBarState.Sent or RoomBarState.Listening or RoomBarState.Transcribing;
            Lay(width);
            gameObject.SetActive(true);
        }

        /// <summary>For renders: the width at its end and the level as it is now, as a person sees them once the motion ends.</summary>
        public void SettleForRender()
        {
            width = targetWidth;
            if (shown == null) return;
            Lay(width);
            FollowLevel();
        }

        public void Hide()
        {
            shown = null;
            width = -1f;
            gameObject.SetActive(false);
        }

        private void Update()
        {
            if (shown == null) return;
            if (!Mathf.Approximately(width, targetWidth))
            {
                var t = Mathf.Clamp01((Time.unscaledTime - movedAt) / WidthSeconds);
                // Opening eases out (cubic); folding runs the standard curve (0.2, 0, 0, 1), near enough as a quintic ease out.
                var eased = opening ? 1f - Mathf.Pow(1f - t, 3f) : 1f - Mathf.Pow(1f - t, 5f);
                width = GlazeMotion.Still || t >= 1f ? targetWidth : Mathf.Lerp(fromWidth, targetWidth, eased);
                Lay(width);
            }
            FollowLevel();
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

        private void Lay(float capsuleWidth)
        {
            var bar = shown!;
            var inset = RoomType.Units(InsetDp);
            var visual = RoomType.Units(RoomButton.RoundDp);
            var targetSize = RoomType.Units(RoomButton.HeightDp);
            // Drawn 12 dp apart where their targets stand 12 mm apart, else as far apart as the targets need.
            var targetGap = KitType.Units(SpatialKit.TargetGapDegrees);
            var controlGap = Mathf.Max(RoomType.Units(ControlGapDp), targetSize + targetGap - visual);
            var controls = (bar.MicShown ? visual : 0f) + (bar.SendShown ? visual + controlGap : 0f);
            var listening = bar.State == RoomBarState.Listening && Level() != null;
            var leftRoom = listening ? RoomType.Units(LevelInsetDp + LevelWidthDp + LevelGapDp) : inset + RoomType.Units(WordsInsetDp);
            // The words stand 12 dp before the first control as drawn; their target ends 12 mm before its target.
            var rightRoom = inset + controls + (controls > 0f ? RoomType.Units(ControlGapDp) : RoomType.Units(WordsInsetDp));
            RoomType.Size(words);
            GlazeText.SetLiteral(words, bar.Words.Text);
            // One line's words are never cut: at the larger size the capsule widens for them, past SYSTEM-v4's width.
            if (bar.State != RoomBarState.Open)
                capsuleWidth = Mathf.Max(capsuleWidth, leftRoom + rightRoom + words.GetPreferredValues(words.text).x + RoomType.Units(1f));
            var wordsLeft = -capsuleWidth / 2f + leftRoom;
            var wordsRight = capsuleWidth / 2f - rightRoom;
            var wordsWidth = Mathf.Max(RoomType.Units(RoomGeometry.LineDp), wordsRight - wordsLeft);
            words.color = bar.State == RoomBarState.Rest ? RoomSurface.Muted : Color.white;
            var (_, _, wordsHeight) = KitType.Lay(words, wordsWidth, bar.State == RoomBarState.Open ? DraftLines : 1);
            var oneLine = RoomType.Units(RoomGeometry.BarHeightDp);
            var height = Mathf.Max(oneLine, wordsHeight + 2f * RoomType.Units(WordsGapDp) + 2f * inset);
            var size = new Vector2(capsuleWidth, height);
            // Its top at the given point in its parent's units, whatever its own scale.
            transform.localPosition = new Vector3(topCentre.x, topCentre.y - height * transform.localScale.y / 2f, 0f);
            capsule.Draw(size);
            var wordsTop = height <= oneLine + 1e-5f ? wordsHeight / 2f : height / 2f - inset - RoomType.Units(WordsGapDp);
            words.transform.localPosition = new Vector3(wordsLeft, wordsTop, -RoomType.Units(0.5f));
            // The words' target from the capsule's left to 12 mm before the first control's target.
            var firstControl = capsuleWidth / 2f - inset - controls;
            var fieldRight = controls > 0f ? firstControl - (targetSize - visual) / 2f - targetGap : capsuleWidth / 2f - inset;
            var fieldLeft = -capsuleWidth / 2f;
            field.transform.localPosition = new Vector3((fieldLeft + fieldRight) / 2f, 0f, -RoomType.Units(0.5f));
            field.Resize(new Vector2(Mathf.Max(0f, fieldRight - fieldLeft), height));
            field.Available = bar.FieldAction != null;
            var footY = -height / 2f + inset + visual / 2f;
            var right = capsuleWidth / 2f - inset - visual / 2f;
            if (bar.SendShown)
            {
                send.Show(KitComposer.SendInstruction, GlazeIcon.SendAnswer, isMain: true, bar.SendAvailable, new Vector2(right, footY), visual);
                right -= visual + controlGap;
            }
            else send.Hide();
            if (bar.MicShown) mic.Show(VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, isMain: bar.State == RoomBarState.Listening, available: true, new Vector2(right, footY), visual);
            else mic.Hide();
            levelRoot.gameObject.SetActive(listening);
            if (listening) levelRoot.localPosition = new Vector3(-capsuleWidth / 2f + RoomType.Units(LevelInsetDp), 0f, -RoomType.Units(0.5f));
        }
    }
}
