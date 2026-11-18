#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The evidence view in the room (plan v2, AUDIT's 9; the owner's 2026-11-18 02:24Z shape): Seorak's strip along the top, and
    /// Salidium's diagram as places in depth around the person after mirrors-edge-catalyst-map.jpg: YOU ASKED close at the left
    /// hand, then CHANGES, HOW, CHECKS and LEFT TO DO each a step farther and higher, the route climbing away and around, and the
    /// VERDICT ahead where it ends; WHY under HOW. Each place is its tree on the kit's glass (RoomCardView's place), sized by its
    /// distance so its words keep the kit's reading size. One route line joins the places in order; a file joins the check run
    /// that verified it across places, in the class the source gives that link (dashed for inferred). A new read draws in the
    /// links it brought (feel-iron-man-3-mk42-hud-5.jpg's growing graph), never on a timer. Presentation only.
    /// </summary>
    public sealed class EvidenceFlowView : MonoBehaviour
    {
        /// <summary>Where each place stands from the person (the root at their eyes, facing +z): its bearing in degrees, distance and height in metres.</summary>
        public static readonly IReadOnlyDictionary<DiagramPlaceKind, (float Bearing, float Distance, float Height)> Spots =
            new Dictionary<DiagramPlaceKind, (float, float, float)>
            {
                [DiagramPlaceKind.Asked] = (-46f, 0.85f, -0.22f),
                [DiagramPlaceKind.Changes] = (-25f, 1.05f, -0.10f),
                [DiagramPlaceKind.How] = (-4f, 1.30f, 0.04f),
                [DiagramPlaceKind.Checks] = (17f, 1.55f, 0.18f),
                [DiagramPlaceKind.LeftToDo] = (38f, 1.80f, 0.32f),
                [DiagramPlaceKind.Verdict] = (10f, 1.10f, -0.42f),
                [DiagramPlaceKind.Why] = (-15f, 1.60f, -0.98f),
            };

        /// <summary>The strip's spot: ahead and above the places.</summary>
        public const float StripDistance = 1.9f, StripHeight = 0.86f;

        /// <summary>The distance at which a place is drawn at the kit's own size; nearer or farther scales it to keep its angle.</summary>
        public const float ReadingDistance = 1f;

        /// <summary>A place's tree width in dp.</summary>
        public const float PlaceDp = 300f;

        /// <summary>The route's and the links' width in metres at the reading distance, the dash and its gap, and how long a new link takes to draw in.</summary>
        public const float LineMetres = 0.0016f, DashMetres = 0.012f, DashGapMetres = 0.008f, GrowSeconds = 0.6f;

        private readonly Dictionary<DiagramPlaceKind, (RoomSurface Glass, RoomCardView Card)> places = new Dictionary<DiagramPlaceKind, (RoomSurface, RoomCardView)>();
        private RoomCardView? strip;
        private readonly List<RoomShape> lines = new List<RoomShape>();
        private int linesUsed;
        private EvidenceDiagram? shown;
        private readonly Dictionary<string, float> growing = new Dictionary<string, float>(System.StringComparer.Ordinal);

        /// <summary>The nodes a newer read brought, each lit by a ring at its tick that fades (feel-iron-man-3-mk42-hud-5.jpg's lit rings), with when it arrived.</summary>
        private readonly Dictionary<string, float> arrived = new Dictionary<string, float>(System.StringComparer.Ordinal);
        private readonly List<RoomShape> rings = new List<RoomShape>();

        /// <summary>How long a new node's ring stays lit, and its size in metres at the reading distance.</summary>
        public const float RingSeconds = 1.6f, RingMetres = 0.022f;

        /// <summary>The nodes lit now, for checks: each id with its ring's strength, 0 to 1.</summary>
        public IReadOnlyDictionary<string, float> Lit(float now) =>
            arrived.ToDictionary(pair => pair.Key, pair => Mathf.Clamp01(1f - (now - pair.Value) / RingSeconds));

        /// <summary>The clock new links draw in by; the render fixes it.</summary>
        public System.Func<float> Clock { get; set; } = () => Time.time;

        public static EvidenceFlowView Create(Transform parent)
        {
            var go = new GameObject("Evidence flow");
            go.transform.SetParent(parent, false);
            return go.AddComponent<EvidenceFlowView>();
        }

        /// <summary>The places as drawn, for checks: each its glass and its tree.</summary>
        public IReadOnlyDictionary<DiagramPlaceKind, (RoomSurface Glass, RoomCardView Card)> Places => places;

        /// <summary>The strip as drawn; null where Seorak gave none.</summary>
        public RoomCardView? Strip => strip;

        /// <summary>The lines drawn: the route's and the links' pieces, each named for what it joins.</summary>
        public IReadOnlyList<RoomShape> Lines => lines.Take(linesUsed).ToList();

        /// <summary>Stands <paramref name="diagram"/> and the strip's <paramref name="stats"/>; the links a newer read brought draw in.</summary>
        public void Show(EvidenceDiagram diagram, IReadOnlyList<EvidenceStrip.Stat> stats, IReadOnlyList<string> notes)
        {
            var now = Clock();
            var fresh = new HashSet<string>(diagram.NewSince(shown), System.StringComparer.Ordinal);
            foreach (var link in diagram.Links.Where(link => fresh.Contains(link.From) || fresh.Contains(link.To)))
                if (shown != null) growing[link.From + ">" + link.To] = now;
            // Only what a NEWER read brought lights: the first read stands whole, nothing animating that no read moved.
            if (shown != null) foreach (var id in fresh) arrived[id] = now;
            shown = diagram;
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var width = RoomType.Units(PlaceDp);
            var standing = new HashSet<DiagramPlaceKind>();
            foreach (var place in diagram.Places)
            {
                standing.Add(place.Kind);
                if (!places.TryGetValue(place.Kind, out var made))
                {
                    var holder = new GameObject(place.Head).transform;
                    holder.SetParent(transform, false);
                    var glass = RoomSurface.Create(holder, "Place glass", RoomSurfaceKind.Information, KitType.Order.Plate);
                    made = (glass, RoomCardView.Create(glass.transform));
                    places[place.Kind] = made;
                }
                var (surface, card) = made;
                surface.transform.parent.gameObject.SetActive(true);
                card.gameObject.SetActive(true);
                var tall = card.Show(new RoomCard().WithPlace(place), width);
                var size = new Vector2(width + 2f * pad, tall + 2f * pad);
                surface.Draw(size);
                card.transform.localPosition = new Vector3(-width / 2f, size.y / 2f - pad, -RoomType.Units(0.5f));
                var (bearing, distance, height) = Spots[place.Kind];
                var holderTransform = surface.transform.parent;
                holderTransform.localPosition = At(bearing, distance, height);
                holderTransform.localRotation = Quaternion.LookRotation(holderTransform.localPosition - new Vector3(0f, holderTransform.localPosition.y * 0.5f, 0f));
                holderTransform.localScale = Vector3.one * (distance / ReadingDistance);
            }
            foreach (var kind in places.Keys.Where(kind => !standing.Contains(kind)).ToList()) places[kind].Glass.transform.parent.gameObject.SetActive(false);

            if (stats.Count > 0)
            {
                if (strip == null)
                {
                    var holder = new GameObject("Strip").transform;
                    holder.SetParent(transform, false);
                    strip = RoomCardView.Create(holder);
                }
                strip.gameObject.SetActive(true);
                var stripWidth = RoomType.Units(PlaceDp * 2.4f);
                strip.Show(new RoomCard().WithStrip(stats, notes), stripWidth);
                var holderTransform = strip.transform.parent;
                holderTransform.localPosition = new Vector3(0f, StripHeight, StripDistance);
                holderTransform.localRotation = Quaternion.LookRotation(holderTransform.localPosition - new Vector3(0f, StripHeight * 0.5f, 0f));
                holderTransform.localScale = Vector3.one * (StripDistance / ReadingDistance);
                strip.transform.localPosition = new Vector3(-stripWidth / 2f, 0f, 0f);
            }
            else if (strip != null) strip.gameObject.SetActive(false);
            DrawLines(diagram, now);
            DrawRings(now);
        }

        /// <summary>
        /// The story in the room (the owner's 2026-11-18 words: plain enough for someone who has never coded, beautiful, the Cluely ad as
        /// inspiration only): the middle left clear for the work itself; at the left, a short status pill over the person's ask in large
        /// words; at the right, the agent's account building line by line (what it did, why, its steps, what is left), the older lines
        /// dim and the newest bright, and under it the checks' own result as a large plain answer; at the bottom centre, the figures.
        /// The kit's own dark glass and white type, no colour of our own, no connector lines. Cards arrive as glass first, their words
        /// a moment after, the account's lines one by one.
        /// </summary>
        /// Near and facing, the cards large, as the reference's are (AUDIT's 03:19Z on the shipped frames: small, far and edge-on read
        /// poorly): each side about 26 degrees out at 0.95 m, each card about 30 degrees wide, the middle between them clear.
        public const float SideBearing = 26f, SideDistance = 0.95f, CardDp = 500f, CardGapDp = 26f;

        /// <summary>The arrival: each card's glass this long after the last, its words this long after its glass, each line of the account this long after the one before.</summary>
        public const float RevealStep = 0.35f, RevealRise = 0.4f, WordsAfter = 0.25f, LineStep = 0.45f;

        /// <summary>One card: its id, its words, its form, its glass and its view, and where it stands in its side.</summary>
        private sealed class Story
        {
            public string Id = "";
            public StoryPlace Place = null!;
            public StoryForm Form;
            public Transform Holder = null!;
            public RoomSurface Glass = null!;
            public RoomCardView Card = null!;
            public Vector3 At;
            public Vector2 Size;
            public int Order;
            public int? LinesShown;
            /// <summary>Whether the newest telling holds this card; its holder may still be hidden while it waits to arrive.</summary>
            public bool Standing;
        }

        private readonly Dictionary<string, Story> cards = new Dictionary<string, Story>(System.StringComparer.Ordinal);
        private Transform? leftSide, rightSide;
        private EvidenceStory? told;
        private string? toldStatus;
        private float revealFrom = float.NegativeInfinity;

        /// <summary>The story's places as drawn, for checks: each place by its kind, its glass and its words (a place in the account by the account's card).</summary>
        public IReadOnlyDictionary<StoryPlaceKind, (Component Glass, RoomCardView Card)> StoryPlaces =>
            told == null ? new Dictionary<StoryPlaceKind, (Component, RoomCardView)>() : told.Places
                .Select(place => (place.Kind, Card: CardOf(place.Kind)))
                .Where(each => each.Card != null && each.Card.Holder.gameObject.activeSelf)
                .ToDictionary(each => each.Kind, each => ((Component)each.Card!.Glass, each.Card!.Card));

        private Story? CardOf(StoryPlaceKind kind) => kind switch
        {
            StoryPlaceKind.Asked => cards.TryGetValue("asked", out var asked) ? asked : null,
            StoryPlaceKind.Worked => cards.TryGetValue("checks", out var checks) ? checks : null,
            StoryPlaceKind.Wrong or StoryPlaceKind.Why or StoryPlaceKind.Did or StoryPlaceKind.Left => cards.TryGetValue("account", out var account) ? account : null,
            _ => null,
        };

        /// <summary>Every card as drawn, in arrival order, for checks: its id, its form and its holder.</summary>
        public IReadOnlyList<(string Id, StoryForm Form, Transform Holder)> StoryCards =>
            cards.Values.OrderBy(card => card.Order).Select(card => (card.Id, card.Form, card.Holder)).ToList();

        /// <summary>
        /// Tells <paramref name="story"/> with the run's <paramref name="status"/> in plain words and the strip's <paramref name="stats"/>.
        /// The first telling brings the cards in one by one; a newer read redraws in place.
        /// </summary>
        public void Tell(EvidenceStory story, IReadOnlyList<EvidenceStrip.Stat> stats, IReadOnlyList<string> notes, EvidenceStrip? whole = null, IReadOnlyList<string>? chosen = null, string? status = null)
        {
            offered = (whole, chosen);
            if (told == null) revealFrom = Clock();
            told = story;
            toldStatus = status;
            foreach (var (_, (glass, _)) in places) glass.transform.parent.gameObject.SetActive(false);
            leftSide ??= Side("Left", -SideBearing);
            rightSide ??= Side("Right", SideBearing);

            StoryPlace? Of(StoryPlaceKind kind) => story.Places.FirstOrDefault(place => place.Kind == kind);
            var dealt = new List<(string Id, StoryPlace Place, StoryForm Form, bool Right)>();
            if (!string.IsNullOrWhiteSpace(status)) dealt.Add(("status", new StoryPlace(StoryPlaceKind.Asked, LineWords.HostPlain(status!)), StoryForm.Pill, false));
            if (Of(StoryPlaceKind.Asked) is StoryPlace asked) dealt.Add(("asked", asked, StoryForm.Lead, false));
            // The agent's account: one plain sentence each, in the story's order (what it did, why, how it works, what is left); never
            // the source's fragments (cause chains, step lists), which read as a jumble to someone who has never coded.
            var lines = new List<LineWords>();
            foreach (var kind in new[] { StoryPlaceKind.Wrong, StoryPlaceKind.Why, StoryPlaceKind.Did, StoryPlaceKind.Left })
                if (Of(kind) is StoryPlace place && !place.Answer.IsEmpty) lines.Add(place.Answer);
            // Said as the agent's where its own closing sentences answer (the lead's (c); AUDIT2's "The AI Says").
            var agents = story.Places.Any(place => place.From == EvidenceStory.Words.AgentSays) ? EvidenceStory.Words.AgentSays : null;
            if (lines.Count > 0) dealt.Add(("account", new StoryPlace(StoryPlaceKind.Wrong, LineWords.None, lines, agents, head: EvidenceStory.Words.Account), StoryForm.Lines, true));
            if (Of(StoryPlaceKind.Worked) is StoryPlace worked) dealt.Add(("checks", worked, StoryForm.Result, true));

            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var standing = new HashSet<string>(System.StringComparer.Ordinal);
            for (var order = 0; order < dealt.Count; order++)
            {
                var (id, place, form, right) = dealt[order];
                standing.Add(id);
                if (!cards.TryGetValue(id, out var card))
                {
                    var holder = new GameObject(id).transform;
                    holder.SetParent(right ? rightSide : leftSide, false);
                    var glass = RoomSurface.Create(holder, "Glass", RoomSurfaceKind.Information, KitType.Order.Plate);
                    card = new Story { Id = id, Holder = holder, Glass = glass, Card = RoomCardView.Create(glass.transform) };
                    cards[id] = card;
                }
                card.Place = place;
                card.Form = form;
                card.Order = order;
                card.Standing = true;
                card.Holder.gameObject.SetActive(true);
                card.Card.gameObject.SetActive(true);
                var width = RoomType.Units(form == StoryForm.Pill ? CardDp * 0.62f : CardDp) - 2f * pad;
                // Measured whole, so the glass stands at its final size before its lines arrive.
                var tall = card.Card.Show(new RoomCard().WithStory(place, form), width);
                card.Size = new Vector2(width + 2f * pad, tall + 2f * pad);
                card.Glass.Draw(card.Size);
                card.LinesShown = null;
                card.Card.transform.localPosition = new Vector3(-width / 2f, card.Size.y / 2f - pad, -RoomType.Units(0.5f));
            }
            foreach (var gone in cards.Keys.Where(id => !standing.Contains(id)).ToList())
            {
                cards[gone].Standing = false;
                cards[gone].Holder.gameObject.SetActive(false);
            }

            // Each side a column, its cards top to bottom with a gap, the left's pill on its left edge; both sides start level.
            var gap = RoomType.Units(CardGapDp);
            void Stack(IEnumerable<Story> stack, float top, bool alignLeft)
            {
                foreach (var card in stack)
                {
                    var x = alignLeft ? -(RoomType.Units(CardDp) - card.Size.x) / 2f : 0f;
                    card.At = new Vector3(x, top - card.Size.y / 2f, 0f);
                    top -= card.Size.y + gap;
                }
            }
            var topEdge = RoomType.Units(300f);
            Stack(dealt.Where(each => !each.Right).Select(each => cards[each.Id]), topEdge, alignLeft: true);
            Stack(dealt.Where(each => each.Right).Select(each => cards[each.Id]), topEdge, alignLeft: false);

            if (stats.Count > 0)
            {
                if (strip == null)
                {
                    var holder = new GameObject("Strip").transform;
                    holder.SetParent(transform, false);
                    strip = RoomCardView.Create(holder);
                }
                strip.gameObject.SetActive(true);
                var stripWidth = RoomType.Units(CardDp * 1.1f);
                strip.Show(new RoomCard().WithStrip(stats, notes), stripWidth);
                var stripHolder = strip.transform.parent;
                // Low in the clear middle, under both sides and clear of them.
                stripHolder.localPosition = new Vector3(0f, -0.78f, 1.45f);
                stripHolder.localRotation = Quaternion.LookRotation(stripHolder.localPosition - new Vector3(0f, -0.39f, 0f));
                stripHolder.localScale = Vector3.one * 1.45f;
                strip.transform.localPosition = new Vector3(-stripWidth / 2f, 0f, 0f);
                StandChoose();
            }
            else if (strip != null) strip.gameObject.SetActive(false);
            Stage(Clock());
        }

        /// <summary>A side of the story at <paramref name="bearing"/>, at the side's distance, facing the person, sized to keep the kit's reading size.</summary>
        private Transform Side(string name, float bearing)
        {
            var side = new GameObject(name).transform;
            side.SetParent(transform, false);
            side.localPosition = At(bearing, SideDistance, 0f);
            side.localRotation = Quaternion.LookRotation(side.localPosition);
            side.localScale = Vector3.one * (SideDistance / ReadingDistance);
            return side;
        }

        private (EvidenceStrip? Whole, IReadOnlyList<string>? Chosen) offered;
        private KitPress? choosePress;
        private EvidenceChooseView? chooser;

        /// <summary>The person picked which stats the strip shows (FIELD's chooser): the new choice in its order. The director keeps it and tells again.</summary>
        public event System.Action<IReadOnlyList<string>>? Chose;

        /// <summary>The chooser as drawn while open; null otherwise.</summary>
        public EvidenceChooseView? Chooser => chooser != null && chooser.gameObject.activeSelf ? chooser : null;

        /// <summary>CHOOSE, the strip's last quiet item, takes a press that opens the chooser under the strip; a press again or Done closes it.</summary>
        private void StandChoose()
        {
            if (strip == null || strip.StripChoose is not TMPro.TextMeshPro choose || offered.Whole == null) return;
            if (choosePress == null)
            {
                var target = new GameObject("Choose press");
                target.transform.SetParent(choose.transform.parent, false);
                choosePress = KitPress.Create(target, Vector2.one * RoomType.Units(RoomButton.HeightDp));
                choosePress.Pressed += ToggleChooser;
            }
            var width = RoomCursor.WordsWidth(choose, 0f);
            choosePress.transform.localPosition = choose.transform.localPosition + new Vector3(width / 2f, -RoomType.Units(10f), -RoomType.Units(1f));
            choosePress.Resize(new Vector2(width + RoomType.Units(24f), RoomType.Units(RoomButton.HeightDp)));
            if (Chooser != null) chooser!.Show(offered.Whole, offered.Chosen);
        }

        private void ToggleChooser()
        {
            if (Chooser != null)
            {
                chooser!.gameObject.SetActive(false);
                return;
            }
            if (chooser == null)
            {
                chooser = EvidenceChooseView.Create(strip!.transform.parent);
                chooser.Chosen += choice => Chose?.Invoke(choice);
                chooser.Done += () => chooser.gameObject.SetActive(false);
            }
            chooser.gameObject.SetActive(true);
            chooser.Show(offered.Whole, offered.Chosen);
            // Under the strip, its left edge with the strip's, in the strip's own frame.
            chooser.transform.localPosition = new Vector3(-RoomType.Units(CardDp * 1.4f) / 2f, RoomType.Units(260f), -RoomType.Units(2f));
        }

        /// <summary>How far card <paramref name="index"/> has arrived at <paramref name="now"/>: 0 not yet, 1 stood.</summary>
        private float Risen(int index, float now) => Mathf.Clamp01((now - revealFrom - index * RevealStep) / RevealRise);

        /// <summary>
        /// Stands each card where its arrival has it: its glass rising a little into place, its words a moment after, and the account's
        /// lines arriving one by one, each new line the bright one.
        /// </summary>
        private void Stage(float now)
        {
            if (told == null) return;
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            foreach (var card in cards.Values)
            {
                // Every card the telling holds, hidden or not: one still waiting to arrive must be stood when its time comes.
                if (!card.Standing || card.Place == null) continue;
                var risen = Risen(card.Order, now);
                var eased = 1f - (1f - risen) * (1f - risen) * (1f - risen);
                card.Holder.gameObject.SetActive(risen > 0f);
                card.Holder.localPosition = card.At - new Vector3(0f, RoomType.Units(18f) * (1f - eased), 0f);
                card.Holder.localScale = Vector3.one * (0.97f + 0.03f * eased);
                var wordsFrom = revealFrom + card.Order * RevealStep + WordsAfter;
                card.Card.gameObject.SetActive(now >= wordsFrom);
                if (card.Form == StoryForm.Lines)
                {
                    var shown = Mathf.Clamp(1 + Mathf.FloorToInt((now - wordsFrom) / LineStep), 0, card.Place.Steps.Count);
                    if (shown != card.LinesShown)
                    {
                        card.LinesShown = shown;
                        card.Card.Show(new RoomCard().WithStory(card.Place, card.Form, shown), card.Size.x - 2f * pad);
                    }
                }
            }
            for (var index = linesUsed = 0; index < lines.Count; index++) lines[index].gameObject.SetActive(false);
        }

        /// <summary>Stands the story as it is at the clock's time; a render calls it to stand a moment of the arrival.</summary>
        public void AdvanceStory() => Stage(Clock());

        /// <summary>Draws the growing links and the lit rings at the clock's time, as each frame does; a render calls it to stand a moment.</summary>
        public void Advance()
        {
            if (shown == null) return;
            DrawLines(shown, Clock());
            DrawRings(Clock());
        }

        private void Update()
        {
            if (told != null && Clock() - revealFrom < cards.Count * RevealStep + RevealRise + WordsAfter + LineStep * 12f) Stage(Clock());
            if (shown != null && growing.Count > 0) DrawLines(shown, Clock());
            if (arrived.Count > 0 || rings.Any(ring => ring.gameObject.activeSelf)) DrawRings(Clock());
        }

        /// <summary>A white ring at each newly read node's tick, fading over <see cref="RingSeconds"/>; then it is gone.</summary>
        private void DrawRings(float now)
        {
            var used = 0;
            foreach (var (id, started) in arrived.ToList())
            {
                var strength = Mathf.Clamp01(1f - (now - started) / RingSeconds);
                if (strength <= 0f || AnchorOf(id, right: false) is not Vector3 at)
                {
                    arrived.Remove(id);
                    continue;
                }
                if (used >= rings.Count) rings.Add(RoomShape.Create(transform, "Ring", KitType.Order.Words + 2));
                var ring = rings[used++];
                ring.gameObject.SetActive(true);
                ring.name = "ring " + id;
                var size = RingMetres * Mathf.Max(0.5f, at.magnitude / ReadingDistance);
                ring.Draw(new Vector2(size, size), size / 2f, new Color(1f, 1f, 1f, 0.12f * strength), new Color(1f, 1f, 1f, strength), size * 0.09f);
                ring.transform.localPosition = at - at.normalized * 0.004f;
                ring.transform.localRotation = Quaternion.LookRotation(at.normalized, Vector3.up);
            }
            for (var index = used; index < rings.Count; index++) rings[index].gameObject.SetActive(false);
        }

        /// <summary>The route through the places in order, then each link between nodes of two places; a link still growing drawn as far as it has grown.</summary>
        private void DrawLines(EvidenceDiagram diagram, float now)
        {
            linesUsed = 0;
            var route = diagram.Places.Where(place => place.Kind != DiagramPlaceKind.Why && places.ContainsKey(place.Kind))
                .Select(place => Edge(place.Kind, left: true)).ToList();
            for (var index = 0; index + 1 < route.Count; index++) Segment(route[index], route[index + 1], dashed: false, "route", 1f);
            foreach (var link in diagram.Links)
            {
                var from = AnchorOf(link.From, right: true);
                var to = AnchorOf(link.To, right: false);
                if (from == null || to == null || PlaceOf(link.From) == PlaceOf(link.To)) continue;
                var key = link.From + ">" + link.To;
                var grown = 1f;
                if (growing.TryGetValue(key, out var started))
                {
                    grown = Mathf.Clamp01((now - started) / GrowSeconds);
                    if (grown >= 1f) growing.Remove(key);
                }
                Segment(from.Value, to.Value, dashed: link.Class != DiagramClass.Observed, "link " + link.From + " to " + link.To, grown);
            }
            for (var index = linesUsed; index < lines.Count; index++) lines[index].gameObject.SetActive(false);
        }

        private Vector3 Edge(DiagramPlaceKind kind, bool left)
        {
            var (glass, _) = places[kind];
            var holder = glass.transform.parent;
            var half = RoomType.Units(PlaceDp) / 2f + RoomType.Scaled(RoomGeometry.PanelPadDp);
            return transform.InverseTransformPoint(holder.TransformPoint(new Vector3(left ? -half : half, 0f, 0f)));
        }

        private DiagramPlaceKind? PlaceOf(string id)
        {
            foreach (var place in shown?.Places ?? new DiagramPlace[0]) if (place.Nodes.Any(node => node.Id == id)) return place.Kind;
            return null;
        }

        private Vector3? AnchorOf(string id, bool right)
        {
            if (PlaceOf(id) is not DiagramPlaceKind kind || !places.TryGetValue(kind, out var made)) return null;
            if (!made.Card.Anchors.TryGetValue(id, out var anchor)) return null;
            return transform.InverseTransformPoint(made.Card.transform.TransformPoint(right ? anchor.Right : anchor.Left));
        }

        /// <summary>A straight piece from <paramref name="a"/> to <paramref name="b"/> as thin plates facing the person, dashed or whole, drawn to <paramref name="grown"/> of its length.</summary>
        private void Segment(Vector3 a, Vector3 b, bool dashed, string what, float grown)
        {
            var end = Vector3.Lerp(a, b, grown);
            var length = Vector3.Distance(a, end);
            if (length < 1e-4f) return;
            var direction = (end - a) / length;
            var step = dashed ? DashMetres + DashGapMetres : length;
            for (var at = 0f; at < length - 1e-5f; at += step)
            {
                var piece = Mathf.Min(dashed ? DashMetres : length, length - at);
                var middle = a + direction * (at + piece / 2f);
                if (linesUsed >= lines.Count) lines.Add(RoomShape.Create(transform, "Line", KitType.Order.Plate + 1));
                var line = lines[linesUsed++];
                line.gameObject.SetActive(true);
                line.name = what;
                var thick = LineMetres * Mathf.Max(0.5f, middle.magnitude / ReadingDistance);
                line.Draw(new Vector2(piece, thick), thick / 2f, new Color(1f, 1f, 1f, 0.85f));
                line.transform.localPosition = middle;
                // Facing the person: its normal along the view ray, its length along the piece (x = up cross forward).
                var away = middle.normalized;
                line.transform.localRotation = Quaternion.LookRotation(away, Vector3.Cross(away, direction));
            }
        }

        private static Vector3 At(float bearing, float distance, float height)
        {
            var radians = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians) * distance, height, Mathf.Cos(radians) * distance);
        }
    }
}
