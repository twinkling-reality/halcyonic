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
    /// The focused work: one opaque instrument of three regions, each placed by what its words are. The context spine
    /// at the left says where the person is; the reading slate in the middle, nearest the person, holds the selected
    /// task's identity and state, what its agent said in full, and the person's own draft; the evidence rail at the
    /// right holds what was recorded. They are one instrument: the slate in front, the spine and the rail recessed
    /// bands <see cref="SpatialKit.RecessMeters"/> behind it, reaching under it and shaded where it stands over them.
    /// It is laid out on one plane facing the eyes and drawn on one gentle curve round them (the plane is the
    /// fallback). The instrument's one light runs along the slate's top in the work's state tone, which its words
    /// also say. Its top stays put and its height follows its content up to <see cref="SpatialKit.SlateMostAt"/>.
    /// Centred on its transform, built in units of the plane's distance.
    /// </summary>
    public sealed class KitWorkView : MonoBehaviour
    {
        public const string RailHeading = "Activity";
        public const string EarlierWords = "Earlier activity";
        public const string NothingReported = "Nothing reported by the agent yet.";

        private KitCarrier spineCarrier = null!;
        private KitCarrier slate = null!;
        private KitCarrier rail = null!;
        private KitSpine spine = null!;
        private TextMeshPro context = null!;
        private TextMeshPro contextIcon = null!;
        private KitStateSignal state = null!;
        private TextMeshPro marks = null!;
        private TextMeshPro title = null!;
        private Surface divider = null!;
        private KitMessage message = null!;
        private TextMeshPro nothing = null!;
        private KitComposer composer = null!;
        private KitDecisionBar decision = null!;
        private KitNotice notice = null!;
        private KitAvatarZone avatar = null!;
        private TextMeshPro asks = null!;
        private KitChip request = null!;
        private TextMeshPro question = null!;
        private readonly List<TextMeshPro> notes = new List<TextMeshPro>();
        private readonly List<float> noteTops = new List<float>();
        private readonly List<KitButton> tabs = new List<KitButton>();
        private readonly List<Vector2> tabCentres = new List<Vector2>();
        private float tabsHeight;
        private readonly List<TextMeshPro> said = new List<TextMeshPro>();
        private readonly List<float> saidTops = new List<float>();
        private float saidHeight;
        private float requestTop;
        private float questionTop;
        private TextMeshPro railHeading = null!;
        private KitRow earlier = null!;
        private readonly List<KitRow> routes = new List<KitRow>();
        private KitTrail trail = null!;
        private Vector2 size;

        public KitCarrier Slate => slate;
        public KitCarrier SpineCarrier => spineCarrier;
        public KitCarrier Rail => rail;
        public KitSpine Spine => spine;
        public KitStateSignal State => state;
        public TextMeshPro Title => title;
        public KitMessage Message => message;
        public KitComposer Composer => composer;
        public KitDecisionBar Decision => decision;
        public KitNotice Notice => notice;
        public KitAvatarZone Avatar => avatar;
        public TextMeshPro Asks => asks;
        public KitChip Request => request;
        public TextMeshPro AskedQuestion => question;
        public IReadOnlyList<TextMeshPro> Notes => notes;

        /// <summary>The file's sections as tabs under the header, as shown.</summary>
        public IEnumerable<KitButton> Tabs => tabs.Where(tab => tab.gameObject.activeSelf);

        /// <summary>The file's own lines under the state (<see cref="KitWorkModel.Said"/>), as shown.</summary>
        public IEnumerable<TextMeshPro> Said => said.Where(line => line.gameObject.activeSelf);
        public KitTrail Trail => trail;
        public KitRow Earlier => earlier;

        /// <summary>The task's routes at the rail's foot, as shown, in the frame's order.</summary>
        public IEnumerable<KitRow> Routes => routes.Where(route => route.gameObject.activeSelf);

        /// <summary>The instrument's extent as laid out, in units of its distance.</summary>
        public Vector2 Size => size;

        /// <summary>Everything read shows whole as last laid out: the agent's report, where there is one, and the person's words.</summary>
        public bool ReadsWhole => (!message.gameObject.activeSelf || message.Complete)
            && (!composer.gameObject.activeSelf || composer.ShownDraftLines >= composer.DraftLines);

        /// <summary>A spine row, the rail's Earlier activity, its pager or one of its routes was pressed.</summary>
        public event Action<string>? RowPressed;

        public IEnumerable<KitCarrier> Carriers => new[] { spineCarrier, slate, rail };

        /// <summary>Every hand target the view shows.</summary>
        public IEnumerable<KitPress> Targets => SpineTargets.Concat(SlateTargets).Concat(RailTargets);

        public IEnumerable<KitPress> SpineTargets
        {
            get
            {
                yield return spine.Switch.WorkspaceTarget;
                yield return spine.Switch.CompactTarget;
                foreach (var row in spine.Rows) yield return row.Press;
            }
        }

        public IEnumerable<KitPress> SlateTargets
        {
            get
            {
                if (composer.gameObject.activeSelf)
                {
                    yield return composer.Field;
                    foreach (var button in new[] { composer.Close, composer.Rare, composer.Talk, composer.Send })
                        if (button.gameObject.activeSelf) yield return button.Press;
                }
                foreach (var tab in Tabs) yield return tab.Press;
                if (avatar.gameObject.activeSelf && avatar.Action.gameObject.activeSelf) yield return avatar.Action.Press;
                if (decision.gameObject.activeSelf)
                    foreach (var button in new[] { decision.Deny, decision.Approve, decision.Cancel, decision.Yes })
                        if (button.gameObject.activeSelf) yield return button.Press;
            }
        }

        public IEnumerable<KitPress> RailTargets
        {
            get
            {
                if (earlier.gameObject.activeSelf) yield return earlier.Press;
                foreach (var route in Routes) yield return route.Press;
            }
        }

        public static KitWorkView Create(Transform parent, string name = "Focused work")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<KitWorkView>();
            view.spineCarrier = KitCarrier.Create(go.transform, "Context spine", KitType.Order.Carrier);
            view.rail = KitCarrier.Create(go.transform, "Evidence rail", KitType.Order.Carrier);
            view.slate = KitCarrier.Create(go.transform, "Reading slate", KitType.Order.Carrier + 3);
            view.spine = KitSpine.Create(view.spineCarrier.transform);
            view.spine.RowPressed += key => view.RowPressed?.Invoke(key);
            var face = view.slate.transform;
            view.contextIcon = GlazeIcons.Create(face, "Project mark", SpatialKit.IconDegrees, KitType.Secondary, KitType.Order.Words);
            view.context = KitType.Create(face, "Project", KitRole.Body, KitType.Secondary, KitType.Order.Words, TextAlignmentOptions.Left);
            view.context.rectTransform.pivot = new Vector2(0f, 0.5f);
            view.context.textWrappingMode = TextWrappingModes.NoWrap;
            view.state = KitStateSignal.Create(face, "State");
            view.marks = KitType.Create(face, "Marks", KitRole.Body, KitType.ToneText(GlazeTone.Simulated), KitType.Order.Words, TextAlignmentOptions.Right);
            view.marks.rectTransform.pivot = new Vector2(1f, 0.5f);
            view.title = KitType.Create(face, "Task", KitRole.Identity, KitType.Text, KitType.Order.Words);
            view.divider = Surface.Create(face, "Divider", KitType.Order.Plate);
            view.message = KitMessage.Create(face, "Agent's report");
            view.nothing = KitType.Create(face, "Nothing reported", KitRole.Body, KitType.Secondary, KitType.Order.Words);
            view.composer = KitComposer.Create(face);
            view.decision = KitDecisionBar.Create(face);
            view.notice = KitNotice.Create(view.rail.transform);
            view.avatar = KitAvatarZone.Create(view.slate.transform);
            view.avatar.gameObject.SetActive(false);
            view.asks = KitType.Create(face, "What it asks", KitRole.Strong, KitType.Text, KitType.Order.Words);
            view.request = KitChip.Create(face, "Request");
            view.question = KitType.Create(face, "Its question", KitRole.Body, KitType.Text, KitType.Order.Words, leading: SpatialKit.ReadingLeading);
            view.railHeading = KitType.Create(view.rail.transform, "Rail heading", KitRole.Heading, KitType.Secondary, KitType.Order.Words);
            view.earlier = KitRow.Create(view.rail.transform, "Earlier activity");
            view.earlier.Pressed += key => view.RowPressed?.Invoke(key);
            view.trail = KitTrail.Create(view.rail.transform, "Trail");
            return view;
        }

        /// <summary>The rail's row for the file's Activity pager, where <see cref="Show"/> is given a binding with one.</summary>
        public const string PagerKey = "activity:pager";

        /// <summary>The prefix of the rail's rows for the task's routes, followed by the route's place in the binding's order.</summary>
        public const string RouteKey = "activity:route:";

        /// <summary>
        /// Lays the whole instrument out for <paramref name="model"/>, centred on the view's transform. Given the
        /// <paramref name="binding"/> to a task's file, its controls are the frame's own (<see cref="KitFileBinding"/>):
        /// the composer shows only what the frame offers, in its words, and the rail shows the page of Activity the frame
        /// shows, its pager in the frame's words.
        /// </summary>
        public void Show(KitWorkModel model, KitFileBinding? binding = null)
        {
            this.binding = binding;
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var spineWidth = PlaneComposition.Units(SpatialKit.SpineDegrees);
            var slateWidth = PlaneComposition.Units(SpatialKit.SlateDegrees);
            var railWidth = PlaneComposition.Units(SpatialKit.RailDegrees);
            var width = spineWidth + slateWidth + railWidth;
            var inner = slateWidth - 2f * pad;
            var most = PlaneComposition.Units(SpatialKit.SlateMostAt(GlazeText.Scale));

            // The slate's header: the project, the state, then the task's name, once.
            var y = pad;
            var header = LayHeader(model, inner);
            y += header + KitType.Scaled(0.9f);
            var dividerAt = y;
            y += KitType.Scaled(0.9f);
            // The foot's height first: the decision bar while an approval waits, else the composer. What is read takes
            // what remains, and pages rather than shrinks.
            var composerHeight = ShowFoot(model, Vector2.zero, inner);
            tabsHeight = LayTabs(inner);
            y += tabsHeight;
            saidHeight = LaySaid(model, inner);
            y += saidHeight;
            var gap = KitType.Scaled(1.2f);
            var room = Mathf.Max(KitType.Units(4f), most - y - gap - composerHeight - pad);
            var reading = LayReading(model, inner, room);
            var readingTop = y;
            y += reading + gap;
            var composerTop = y;
            y += composerHeight + pad;
            // The regions share one height: the slate's, or the spine's where its content needs more.
            var spineInner = spineWidth - 2f * pad;
            var spineNeeds = spine.Show(model.Spine, model.SpineFoot, Vector2.zero, spineInner, 0f) + 2f * pad
                + 2f * KitType.Units(SpatialKit.SlateRiseDegrees);
            var height = Mathf.Max(y, spineNeeds);
            if (height > y) composerTop += height - y;
            // The slate stands past its neighbours above and below: in front of them.
            var rise = KitType.Units(SpatialKit.SlateRiseDegrees);
            var sides = height - 2f * rise;
            var radius = KitType.Units(SpatialKit.InstrumentRadiusDegrees);
            var tone = Glaze.Tone(model.Badge.LastKnown ? GlazeTone.Unknown : model.Badge.Tone);
            // The instrument's one light runs along the slate's top in the work's state tone; its words say the same state.
            slate.Draw(new Vector2(slateWidth, height), radius, SpatialKit.Slate, lit: true, light: KitType.Mix(SpatialKit.Slate, tone.Strong, 0.92f));
            // One instrument: the slate in front, the spine and the rail recessed bands behind it, reaching under it and
            // shaded where it stands over them.
            var left = -width / 2f;
            var recess = SpatialKit.RecessMeters / PlaneComposition.Distance;
            var overlap = KitType.Units(SpatialKit.InstrumentRadiusDegrees);
            slate.transform.localPosition = new Vector3(left + spineWidth + slateWidth / 2f, 0f, 0f);
            spineCarrier.transform.localPosition = new Vector3(left + (spineWidth + overlap) / 2f, 0f, recess);
            rail.transform.localPosition = new Vector3(left + spineWidth + slateWidth + (railWidth - overlap) / 2f, 0f, recess);
            spineCarrier.Draw(new Vector2(spineWidth + overlap, sides), radius, SpatialKit.Side);
            rail.Draw(new Vector2(railWidth + overlap, sides), radius, SpatialKit.Side);
            spineCarrier.Shade(left: false, right: true, inset: overlap);
            rail.Shade(left: true, right: false, inset: overlap);
            size = new Vector2(width, height);

            // Slate contents, from its own top left.
            var slateLeft = -slateWidth / 2f + pad;
            var top = height / 2f;
            PlaceHeader(slateLeft, top - pad, inner);
            // No rule under the header: the space and the type's own steps divide it.
            divider.gameObject.SetActive(false);
            PlaceTabs(slateLeft, top - readingTop + saidHeight + tabsHeight);
            PlaceSaid(slateLeft, top - readingTop + saidHeight);
            PlaceReading(slateLeft, top - readingTop);
            // The task's place, on the slate's top edge at its right.
            avatar.gameObject.SetActive(model.Avatar != null);
            if (model.Avatar?.Present is CharacterPresentation present) avatar.ShowPresent(present, new Vector2(slateWidth, height));
            else if (model.Avatar is KitAvatar absent) avatar.ShowAbsent(absent.Why!, absent.WayBack, absent.WayBackIcon, new Vector2(slateWidth, height), slateWidth * 0.75f);
            ShowFoot(model, new Vector2(slateLeft, top - composerTop), inner);

            // The spine, inside its own padding, the part under the slate left empty.
            var sideTop = sides / 2f;
            spine.Show(model.Spine, model.SpineFoot, new Vector2(-(spineWidth + overlap) / 2f + pad, sideTop - pad), spineInner, sides - 2f * pad);

            // The rail: its heading, any earlier activity that did not fit, then the newest recorded events.
            var railInner = railWidth - 2f * pad;
            var railLeft = -(railWidth + overlap) / 2f + overlap + pad;
            KitType.Size(railHeading, KitRole.Heading);
            GlazeText.SetLiteral(railHeading, RailHeading);
            var (_, _, headingHeight) = KitType.Lay(railHeading, railInner, 1);
            railHeading.transform.localPosition = new Vector3(railLeft, sideTop - pad, -0.0005f);
            var railY = pad + headingHeight + KitType.Scaled(0.6f);
            // A notice about what happened stands over the activity it points to; the trail takes the room left.
            if (model.Notice != null) railY += notice.Show(model.Notice, new Vector2(railLeft, sideTop - railY), railInner) + KitType.Scaled(0.6f);
            else notice.Hide();
            var earlierAt = railY;
            // The task's own routes stand at the rail's foot, in the frame's words and order; the trail takes the room left.
            var routesHeight = ShowRoutes(railLeft, -sideTop + pad, railInner);
            var routesRoom = routesHeight > 0f ? routesHeight + KitType.Scaled(0.6f) : 0f;
            var trailRoom = sides - railY - pad - routesRoom;
            // Bound, the rail shows the page of Activity the file shows, and its pager turns it; no history of its own.
            var entries = model.Trail;
            if (binding?[KitPlace.Pager] is KitControl pager)
            {
                entries = model.Trail.Where(entry => binding!.Frame.Lines.Any(line => line.Action == null && line.Words == entry.Entry.Text)).ToList();
                var pagerHeight = earlier.Show(new KitRowModel(PagerKey, LineWords.Ours(pager.Words), pager.Icon ?? GlazeIcon.Next), new Vector2(railLeft, sideTop - railY), railInner);
                railY += pagerHeight + KitType.Scaled(0.5f);
                trail.Show(entries, railInner, sides - railY - pad - routesRoom);
                trail.transform.localPosition = new Vector3(railLeft, sideTop - railY, 0f);
                // A page that shows none of what was recorded, as one holding only the routes, says nothing of it: never that nothing was recorded.
                trail.gameObject.SetActive(entries.Count > 0 || model.Trail.Count == 0);
                return;
            }
            trail.gameObject.SetActive(true);
            trail.Show(entries, railInner, trailRoom);
            if (binding != null) earlier.Hide();
            else if (trail.Earlier > 0)
            {
                // Some did not fit: Earlier activity opens the whole history, and the trail gives it the room it needs.
                var earlierModel = new KitRowModel("activity:earlier", LineWords.Ours(EarlierWords), GlazeIcon.LastKnown,
                    end: trail.Earlier == 1 ? "1 more" : trail.Earlier + " more", endTone: GlazeTone.Accent);
                var earlierHeight = earlier.Show(earlierModel, new Vector2(railLeft, sideTop - railY), railInner);
                railY += earlierHeight + KitType.Scaled(0.5f);
                trail.Show(model.Trail, railInner, sides - railY - pad - routesRoom);
                earlier.Show(new KitRowModel("activity:earlier", LineWords.Ours(EarlierWords), GlazeIcon.LastKnown,
                    end: trail.Earlier == 1 ? "1 more" : trail.Earlier + " more", endTone: GlazeTone.Accent), new Vector2(railLeft, sideTop - earlierAt), railInner);
            }
            else earlier.Hide();
            trail.transform.localPosition = new Vector3(railLeft, sideTop - railY, 0f);
        }

        /// <summary>
        /// The binding's routes as rows standing up from <paramref name="bottom"/> at <paramref name="left"/>, in the frame's
        /// order and words, each opening more; none unbound. Returns their height.
        /// </summary>
        private float ShowRoutes(float left, float bottom, float width)
        {
            var shown = binding?.Routes.ToList() ?? new List<KitControl>();
            while (routes.Count < shown.Count)
            {
                var made = KitRow.Create(rail.transform, "Route");
                made.Pressed += key => RowPressed?.Invoke(key);
                routes.Add(made);
            }
            KitRowModel Row(int at) => new KitRowModel(RouteKey + at, LineWords.Ours(shown[at].Words));
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var heights = shown.Select((_, at) => routes[at].Show(Row(at), new Vector2(left, bottom), width)).ToList();
            var total = heights.Sum() + gap * Mathf.Max(0, shown.Count - 1);
            var top = bottom + total;
            for (var at = 0; at < shown.Count; at++)
            {
                routes[at].Show(Row(at), new Vector2(left, top), width);
                top -= heights[at] + gap;
            }
            for (var at = shown.Count; at < routes.Count; at++) routes[at].Hide();
            return total;
        }

        /// <summary>The slate's foot at <paramref name="topLeft"/>: the decision bar while an approval waits, else the composer, asking for an answer where a question waits.</summary>
        private float ShowFoot(KitWorkModel model, Vector2 topLeft, float inner)
        {
            if (model.Need is KitNeed need)
            {
                composer.gameObject.SetActive(false);
                return need.Confirming is KitConfirmation confirming
                    ? decision.ShowConfirming(topLeft, inner, need.DenyWords, need.ApproveWords, confirming.Approving, confirming.Ask, confirming.Confirm, confirming.Read, confirming.ReadFirst)
                    : decision.ShowAsked(topLeft, inner, need.DenyWords, need.ApproveWords, need.DenyOffered, need.ApproveOffered);
            }
            decision.Hide();
            composer.gameObject.SetActive(true);
            composer.Answering = model.Ask != null;
            if (binding != null)
                return composer.Show(topLeft, inner, model.Draft, ComposerRows, model.Sendable, model.WhyNotSend, binding[KitPlace.Close],
                    binding[KitPlace.Stop] ?? binding[KitPlace.Discard], binding[KitPlace.Talk], binding[KitPlace.Send],
                    caption: binding.Frame.Lines.Any(line => line.Action == null && line.Words == FileScreens.YourInstruction));
            return composer.Show(topLeft, inner, model.Draft, ComposerRows, model.VoiceOffered, model.Sendable, model.WhyNotSend);
        }

        /// <summary>
        /// Measures what the slate reads: the approval it waits on (what it asks, the request whole, where it runs and
        /// what each answer does), the question it asks, or else the agent's report. Returns its height.
        /// </summary>
        private float LayReading(KitWorkModel model, float inner, float room)
        {
            var need = model.Need;
            var ask = need == null ? model.Ask : null;
            message.gameObject.SetActive(need == null && ask == null && model.Report != null);
            nothing.gameObject.SetActive(need == null && ask == null && model.Report == null);
            asks.gameObject.SetActive(need != null || ask != null);
            request.gameObject.SetActive(need?.Request != null);
            question.gameObject.SetActive(ask != null);
            foreach (var note in notes) note.gameObject.SetActive(false);
            noteTops.Clear();
            if (need == null && ask == null)
            {
                if (model.Report != null) return message.Show(model.Report, inner, room);
                KitType.Size(nothing, KitRole.Body);
                GlazeText.SetLiteral(nothing, NothingReported);
                var (_, _, nothingHeight) = KitType.Lay(nothing, inner, 2);
                return nothingHeight;
            }
            KitType.Size(asks, KitRole.Strong);
            GlazeRuns.Of(asks).Set(need != null ? need.Asks : ask!.Heading);
            var (_, _, asksHeight) = KitType.Lay(asks, inner, 2);
            var y = asksHeight + KitType.Scaled(0.5f);
            if (ask != null)
            {
                // The agent's question, its own words, leaning as outside text does.
                KitType.Size(question, KitRole.Body, SpatialKit.ReadingLeading);
                GlazeRuns.Of(question).Set(LineWords.Theirs(ask.Question));
                var (_, _, questionHeight) = KitType.Lay(question, inner, 8);
                questionTop = y;
                return y + questionHeight;
            }
            if (need!.Request != null)
            {
                // The request whole, literal, as the runtime reported it, on a chip's ground as evidence is.
                var chip = request.Show(LineWords.Theirs(need.Request), null, KitType.Secondary, inner, 8);
                requestTop = y;
                y += chip.y + KitType.Scaled(0.8f);
            }
            var lines = new List<(string Words, bool Leads)>();
            if (need.ScopeNote != null) lines.Add((need.ScopeNote, true));
            foreach (var line in need.Notes) lines.Add((line, false));
            for (var index = 0; index < lines.Count; index++)
            {
                while (notes.Count <= index) notes.Add(KitType.Create(slate.transform, "Note " + notes.Count, KitRole.Body, KitType.Text, KitType.Order.Words, leading: SpatialKit.ReadingLeading));
                var note = notes[index];
                note.gameObject.SetActive(true);
                KitType.Size(note, KitRole.Body, SpatialKit.ReadingLeading);
                // Where an approved command runs leads, in the reading colour; what each answer does follows, quieter.
                note.color = lines[index].Leads ? KitType.Text : KitType.Secondary;
                GlazeText.SetLiteral(note, lines[index].Words);
                var (_, _, noteHeight) = KitType.Lay(note, inner, 3);
                noteTops.Add(y);
                y += noteHeight + KitType.Scaled(0.35f);
            }
            return y - KitType.Scaled(0.35f);
        }

        /// <summary>The rows the composer's well shows of the person's words.</summary>
        public const int ComposerRows = 3;

        private KitFileBinding? binding;

        /// <summary>The prefix of a tab's key: the rest is the file's own key for the section.</summary>
        public const string SectionKey = KitFileBinding.SectionRowKey;

        /// <summary>
        /// Lays the file's sections, where the bound frame has them, as one row of tabs under the header, as the file's own
        /// frame stands them; a row that would not fit wraps to the next. The one showing stands on a raised fill, the others
        /// as their outline, each a whole target 12 mm from the next. Returns their height, with the gap under them.
        /// </summary>
        private float LayTabs(float inner)
        {
            var sections = binding?.Sections.ToList() ?? new List<KitControl>();
            foreach (var tab in tabs) tab.Hide();
            tabCentres.Clear();
            if (sections.Count == 0) return 0f;
            var gap = KitType.Units(SpatialKit.TargetGapDegrees);
            var x = 0f;
            var y = 0f;
            for (var index = 0; index < sections.Count; index++)
            {
                while (tabs.Count <= index)
                {
                    var made = KitButton.Create(slate.transform, "Section " + tabs.Count, KitButtonRole.Quiet);
                    var at = tabs.Count;
                    made.Pressed += () => { if (binding?.Sections.ElementAtOrDefault(at) is KitControl pressed) RowPressed?.Invoke(SectionKey + pressed.Key); };
                    tabs.Add(made);
                }
                var tab = tabs[index];
                var section = sections[index];
                tab.Role = section.Chosen ? KitButtonRole.Secondary : KitButtonRole.Quiet;
                var width = tab.Measure(section.Words, null);
                if (x > 0f && x + width > inner + 1e-5f) { x = 0f; y += KitButton.Height + gap; }
                tabCentres.Add(new Vector2(x + width / 2f, y + KitButton.Height / 2f));
                tab.Show(section.Words, null, Vector2.zero, width);
                x += width + gap;
            }
            return y + KitButton.Height + KitType.Scaled(0.9f);
        }

        private void PlaceTabs(float left, float top)
        {
            for (var index = 0; index < tabCentres.Count; index++)
                tabs[index].transform.localPosition = new Vector3(left + tabCentres[index].x, top - tabCentres[index].y, -KitType.Units(0.05f));
        }

        /// <summary>
        /// Lays the file's own lines under the state (<see cref="KitWorkModel.Said"/>): a notice, an attention note, that
        /// something waits on Waiting, why nothing can be sent, the newest command's feedback, each whole, in the tone
        /// its line has. Returns their height, with the gap under them.
        /// </summary>
        private float LaySaid(KitWorkModel model, float inner)
        {
            foreach (var line in said) line.gameObject.SetActive(false);
            saidTops.Clear();
            var y = 0f;
            for (var index = 0; index < model.Said.Count; index++)
            {
                while (said.Count <= index) said.Add(KitType.Create(slate.transform, "Said " + said.Count, KitRole.Body, KitType.Text, KitType.Order.Words));
                var line = said[index];
                var page = model.Said[index];
                line.gameObject.SetActive(true);
                KitType.Size(line, KitRole.Body);
                line.color = page.Tone switch
                {
                    LineTone.Waiting => KitType.ToneText(GlazeTone.Attention),
                    LineTone.Problem => KitType.ToneText(GlazeTone.Failure),
                    LineTone.Secondary => KitType.Secondary,
                    _ => KitType.Text,
                };
                GlazeRuns.Of(line).Set(page.Runs);
                var (_, _, height) = KitType.Lay(line, inner, 3);
                saidTops.Add(y);
                y += height + KitType.Scaled(0.35f);
            }
            return model.Said.Count == 0 ? 0f : y + KitType.Scaled(0.6f);
        }

        private void PlaceSaid(float left, float top)
        {
            for (var index = 0; index < saidTops.Count; index++)
                said[index].transform.localPosition = new Vector3(left, top - saidTops[index], -0.0005f);
        }

        private void PlaceReading(float left, float top)
        {
            message.transform.localPosition = new Vector3(left, top, 0f);
            nothing.transform.localPosition = new Vector3(left, top, -0.0005f);
            asks.transform.localPosition = new Vector3(left, top, -0.0005f);
            question.transform.localPosition = new Vector3(left, top - questionTop, -0.0005f);
            if (request.gameObject.activeSelf)
                request.transform.localPosition = new Vector3(left + request.Size.x / 2f, top - requestTop - request.Size.y / 2f, -0.0003f);
            for (var index = 0; index < noteTops.Count; index++)
                notes[index].transform.localPosition = new Vector3(left, top - noteTops[index], -0.0005f);
        }

        private float headerContext;
        private float headerTitle;
        private float headerHeight;

        /// <summary>Measures the header: the project and the state on one line, the task's name under them.</summary>
        private float LayHeader(KitWorkModel model, float inner)
        {
            var stateSize = state.Show(model.Badge, onPill: true);
            KitType.Size(context, KitRole.Body);
            GlazeRuns.Of(context).SetIdentity(model.ProjectName);
            var iconRoom = KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale) + KitType.Scaled(0.4f);
            var markWords = string.Join(" · ", model.Marks.Select(mark => mark.Word));
            marks.gameObject.SetActive(markWords.Length > 0);
            var markWidth = 0f;
            if (markWords.Length > 0)
            {
                KitType.Size(marks, KitRole.Body);
                GlazeText.SetLiteral(marks, markWords);
                (_, markWidth, _) = KitType.Lay(marks, inner / 3f, 1);
                markWidth += KitType.Scaled(0.6f);
            }
            KitType.Lay(context, Mathf.Max(KitType.Units(4f), inner - iconRoom - stateSize.x - markWidth - KitType.Scaled(1f)), 1);
            headerContext = Mathf.Max(stateSize.y, KitType.LinePitch(context));
            KitType.Size(title, KitRole.Identity);
            GlazeRuns.Of(title).SetIdentity(model.TaskTitle);
            var (_, _, titleHeight) = KitType.Lay(title, inner, 2);
            headerTitle = titleHeight;
            headerHeight = headerContext + KitType.Scaled(0.4f) + titleHeight;
            return headerHeight;
        }

        private void PlaceHeader(float left, float top, float inner)
        {
            var middle = top - headerContext / 2f;
            var iconEm = KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale);
            contextIcon.fontSize = GlazeTokens.FontSize(iconEm);
            contextIcon.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
            GlazeIcons.Show(contextIcon, GlazeIcon.Folder);
            contextIcon.transform.localPosition = new Vector3(left + iconEm / 2f, middle, -0.0005f);
            context.transform.localPosition = new Vector3(left + iconEm + KitType.Scaled(0.4f), middle, -0.0005f);
            state.transform.localPosition = new Vector3(left + inner - state.Size.x / 2f, middle, -0.0003f);
            marks.transform.localPosition = new Vector3(left + inner - state.Size.x - KitType.Scaled(0.6f), middle, -0.0005f);
            title.transform.localPosition = new Vector3(left, top - headerContext - KitType.Scaled(0.4f), -0.0005f);
        }

        /// <summary>The instrument's outer corners, in the world, the recessed regions' included.</summary>
        public IEnumerable<Vector3> Corners() => Carriers.SelectMany(carrier => carrier.Corners());
    }
}
