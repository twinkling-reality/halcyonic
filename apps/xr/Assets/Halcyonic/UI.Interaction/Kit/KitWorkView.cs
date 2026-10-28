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
        private float requestTop;
        private float questionTop;
        private TextMeshPro railHeading = null!;
        private KitRow earlier = null!;
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
        public KitTrail Trail => trail;
        public KitRow Earlier => earlier;

        /// <summary>The instrument's extent as laid out, in units of its distance.</summary>
        public Vector2 Size => size;

        /// <summary>A spine row or the rail's Earlier activity was pressed.</summary>
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
                    foreach (var button in new[] { composer.Talk, composer.Send })
                        if (button.gameObject.activeSelf) yield return button.Press;
                }
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

        /// <summary>Lays the whole instrument out for <paramref name="model"/>, centred on the view's transform.</summary>
        public void Show(KitWorkModel model)
        {
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
            var trailRoom = sides - railY - pad;
            trail.Show(model.Trail, railInner, trailRoom);
            if (trail.Earlier > 0)
            {
                // Some did not fit: Earlier activity opens the whole history, and the trail gives it the room it needs.
                var earlierModel = new KitRowModel("activity:earlier", LineWords.Ours(EarlierWords), GlazeIcon.LastKnown,
                    end: trail.Earlier == 1 ? "1 more" : trail.Earlier + " more", endTone: GlazeTone.Accent);
                var earlierHeight = earlier.Show(earlierModel, new Vector2(railLeft, sideTop - railY), railInner);
                railY += earlierHeight + KitType.Scaled(0.5f);
                trail.Show(model.Trail, railInner, sides - railY - pad);
                earlier.Show(new KitRowModel("activity:earlier", LineWords.Ours(EarlierWords), GlazeIcon.LastKnown,
                    end: trail.Earlier == 1 ? "1 more" : trail.Earlier + " more", endTone: GlazeTone.Accent), new Vector2(railLeft, sideTop - earlierAt), railInner);
            }
            else earlier.Hide();
            trail.transform.localPosition = new Vector3(railLeft, sideTop - railY, 0f);
        }

        /// <summary>The slate's foot at <paramref name="topLeft"/>: the decision bar while an approval waits, else the composer, asking for an answer where a question waits.</summary>
        private float ShowFoot(KitWorkModel model, Vector2 topLeft, float inner)
        {
            if (model.Need is KitNeed need)
            {
                composer.gameObject.SetActive(false);
                return need.Confirming is KitConfirmation confirming
                    ? decision.ShowConfirming(topLeft, inner, need.DenyWords, need.ApproveWords, confirming.Approving, confirming.Ask, confirming.Confirm, confirming.Read, confirming.ReadFirst)
                    : decision.ShowAsked(topLeft, inner, need.DenyWords, need.ApproveWords);
            }
            decision.Hide();
            composer.gameObject.SetActive(true);
            composer.Answering = model.Ask != null;
            return composer.Show(topLeft, inner, model.Draft, 3, model.VoiceOffered, model.Sendable, model.WhyNotSend);
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
