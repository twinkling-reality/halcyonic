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
    /// The information panel's arrival (DESIGN's BUILD-SPEC-v4 1.5): with a new moment the panel rises about a degree to its
    /// place and fades in over 360 ms, ease out, its glass (<see cref="RoomSurface.Shown"/>) and every word on it together.
    /// A moment is new where the bar's state, its words or its sentence changed; a bar laid again with the same moment
    /// does not arrive again. The pair's laying calls <see cref="ArrivePanel"/> once it has placed the panel, and the
    /// view's frame calls <see cref="FollowArrival"/>. Kept still (<see cref="GlazeMotion.Still"/>, as renders keep it), it
    /// stands in its place at once.
    /// </summary>
    public sealed partial class RoomBarView
    {
        private string? arrivalKey;
        private float arrivedAt;
        private Vector3 panelRest;
        private readonly List<TMP_Text> panelWords = new List<TMP_Text>();

        /// <summary>Each panel word's own alpha as laid (Laid) and the alpha the arrival last gave it (Applied).</summary>
        private readonly Dictionary<TMP_Text, (float Laid, float Applied)> panelWordsRest = new Dictionary<TMP_Text, (float, float)>();

        /// <summary>The clock the panel's arrival runs on; renders fix it.</summary>
        public Func<float> PanelClock { get; set; } = () => Time.unscaledTime;

        /// <summary>
        /// How long a new moment's panel waits before it arrives, by its state: an asking moment waits while its character
        /// flies to it, so the panel fades and rises beside the character once it lands (the lead's 2026-11-17T02:15Z). None
        /// by default.
        /// </summary>
        public Func<RoomBarState, float> HoldArrival { get; set; } = _ => 0f;

        /// <summary>Raised once for each new moment the panel shows, with the moment, as its arrival starts.</summary>
        public event Action<RoomBar>? MomentArrived;

        private float arrivalHold;

        /// <summary>
        /// Whether the panel stands whole: past its hold (the flying character) and past the build-up of its last line, or Still, or no
        /// moment shown. Its answers take a press only from then (AUDIT3's FL1: approve only what is shown), each counting its settle
        /// from the build-up's end.
        /// </summary>
        public bool PanelBuilt => GlazeMotion.Still || !arrivalAsks || !panel.gameObject.activeSelf || arrivalKey == null
            || PanelClock() - arrivedAt - arrivalHold >= RoomMotion.PanelArrivalSeconds(panelWords.Count);

        /// <summary>
        /// Whether the moment arriving asks the person (a question or a request to approve): only its answers wait for the panel to
        /// stand whole (AUDIT3's FL1). A page's own column (Character Colour's Choose, Preview, Apply) takes a press as it stands
        /// (XR's 2026-11-17T22:0xZ: the gate held every column's presses and the colour walk's Apply never took).
        /// </summary>
        private bool arrivalAsks;

        /// <summary>The panel's own answers (its column), which take a press only once it stands whole (<see cref="PanelBuilt"/>).</summary>
        public IEnumerable<KitPress> AnswerPresses => column.Select(button => button.Press);

        private bool builtBefore = true;

        private readonly List<RoomShape> panelShapes = new List<RoomShape>();

        /// <summary>The panel's marks the arrival hid (an answer's circle, a divider), so it shows only those again.</summary>
        private readonly HashSet<MeshRenderer> marksHeld = new HashSet<MeshRenderer>();

        /// <summary>The panel's words the arrival held off whole until their line arrives, so it turns only those on again.</summary>
        private readonly HashSet<TMP_Text> wordsHeld = new HashSet<TMP_Text>();

        /// <summary>How far the panel has arrived now, 0 to 1, and how far below its place it stands, in dp.</summary>
        public RoomMove PanelArrival { get; private set; } = RoomMove.Settled;

        /// <summary>Starts the panel's arrival where <paramref name="bar"/> is a new moment, else keeps where it is; then places it.</summary>
        private void ArrivePanel(RoomBar bar)
        {
            panelRest = panel.transform.localPosition;
            var key = bar.State + "\n" + bar.Body.Text + "\n" + bar.Sentence + "\n" + (bar.Facts?.Count ?? 0);
            if (key != arrivalKey)
            {
                arrivalKey = key;
                arrivedAt = PanelClock();
                arrivalHold = HoldArrival(bar.State);
                arrivalAsks = bar.State == RoomBarState.Question || bar.State == RoomBarState.Request;
                builtBefore = PanelBuilt;
                MomentArrived?.Invoke(bar);
            }
            panelWords.Clear();
            panel.GetComponentsInChildren(false, panelWords);
            // Each word's own alpha as laid, so the arrival fades it from there: a quiet label stays quiet, never lifted to full
            // (the lead's 2026-11-17 01:56Z MUST 2: the row notes read 228 against the words' 233 because this set every alpha to 1).
            // A word still at the alpha the arrival last gave it was not laid again: it keeps its rest; one laid again brings its own.
            foreach (var words in panelWords)
            {
                if (words == null) continue;
                if (!(panelWordsRest.TryGetValue(words, out var known) && Mathf.Approximately(words.alpha, known.Applied)))
                    panelWordsRest[words] = (words.alpha, words.alpha);
            }
            // Each block of words is a line of the build-up, in reading order: top first, then left first on one line.
            panelWords.Sort((a, b) =>
            {
                var ay = a.transform.position.y;
                var by = b.transform.position.y;
                return Mathf.Abs(ay - by) > 1e-4f ? by.CompareTo(ay) : a.transform.position.x.CompareTo(b.transform.position.x);
            });
            FollowArrival();
        }

        /// <summary>Places the panel where its arrival puts it now: lowered by its move, its glass and its words faded by it.</summary>
        private void FollowArrival()
        {
            if (!panel.gameObject.activeSelf || arrivalKey == null) return;
            // Whole now, not a moment ago: each answer's settle counts from here, so no pinch made while it built takes.
            var built = PanelBuilt;
            if (built && !builtBefore) foreach (var press in AnswerPresses) press.Unsettle();
            builtBefore = built;
            var since = PanelClock() - arrivedAt - arrivalHold;
            var move = GlazeMotion.Still ? RoomMove.Settled : since < 0f ? RoomMove.Gone : RoomMotion.PanelArrives(since);
            PanelArrival = move;
            panel.Shown = move.Shown;
            panel.transform.localPosition = panelRest + Vector3.down * ((since < 0f ? RoomMotion.PanelRiseDp : move.LowerDp) * RoomType.Units(1f));
            for (var index = 0; index < panelWords.Count; index++)
            {
                var words = panelWords[index];
                if (words == null) continue;
                var rest = panelWordsRest.TryGetValue(words, out var known) ? known.Laid : 1f;
                // The panel's lines build up after its glass, a line at a time in reading order, each from its own rest.
                var applied = GlazeMotion.Still ? rest : rest * move.Shown * RoomMotion.PanelLineArrives(since, index).Shown;
                if (!Mathf.Approximately(words.alpha, applied)) words.alpha = applied;
                panelWordsRest[words] = (rest, words.alpha);
                // Glyphs drawn in their own colour (an answer's circle, a rule) keep it whatever the words' alpha, so words not yet
                // arrived are held off whole, their marks with them (AUDIT's 2026-11-17T20:25Z: the circles stood before the panel).
                var wordsStand = applied > 0.01f;
                if (!wordsStand && words.enabled) { words.enabled = false; wordsHeld.Add(words); }
                else if (wordsStand && wordsHeld.Remove(words)) words.enabled = true;
            }
            // Its marks (an answer's circle, a divider) come with their own line, never before the glass and its words (AUDIT's
            // 2026-11-17T20:25Z: the circles hung in the room before the panel): each stands once the nearest line has half arrived.
            panelShapes.Clear();
            panel.GetComponentsInChildren(false, panelShapes);
            foreach (var shape in panelShapes)
            {
                var drawn = shape.Renderer;
                if (drawn == null) continue;
                var line = 0f;
                if (!GlazeMotion.Still)
                {
                    var nearest = -1;
                    var gap = float.MaxValue;
                    for (var index = 0; index < panelWords.Count; index++)
                    {
                        if (panelWords[index] == null) continue;
                        var apart = Mathf.Abs(panelWords[index].transform.position.y - shape.transform.position.y);
                        if (apart < gap) { gap = apart; nearest = index; }
                    }
                    line = nearest < 0 ? move.Shown : move.Shown * RoomMotion.PanelLineArrives(since, nearest).Shown;
                }
                var stands = GlazeMotion.Still || line >= 0.5f;
                if (!stands && drawn.enabled) { drawn.enabled = false; marksHeld.Add(drawn); }
                else if (stands && marksHeld.Remove(drawn)) drawn.enabled = true;
            }
        }

        /// <summary>For renders: the panel in its place, whole.</summary>
        public void SettleArrival()
        {
            arrivedAt = PanelClock() - arrivalHold - RoomMotion.PanelArrivalSeconds(panelWords.Count);
            // A render's settle is whole at once: its presses keep the settle the render gives them.
            builtBefore = true;
            FollowArrival();
        }
    }
}
