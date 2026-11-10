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
    /// The spatial kit standing in for a task's file in the independent workspace (D3.4): it shows <see cref="KitWorkView"/>
    /// from a <see cref="KitFileBinding"/>, the file's sections as tabs on the slate and the task's routes at the rail's foot, and raises, in the plane's own terms, what each of its controls raises: the
    /// frame's own action and key with that frame, Hold to talk's prompt held and let go, a context row's key with the
    /// owner it was shown for, and that the frame has been drawn once every control has settled. Its connected switch
    /// asks for the existing Compact and Open routes (D3.5): Compact on the spine with the frame shown, Workspace from the
    /// compact presence it shows where the closed bar stands, and the thumb moves only on the presentation confirmed. It
    /// routes nothing itself: the plane and the director decide, by the rules they apply to the file's own views and the
    /// closed bar. Laid out centred on <see cref="Part"/>, in units of the plane's distance at the standard text size, as
    /// a frame view is.
    /// </summary>
    public sealed class KitWorkspaceHost : MonoBehaviour
    {
        /// <summary>
        /// Whether the selected task is drawn by the kit rather than the file's own views: on in every build since
        /// 2026-11-03 (KIT-ON), so the kit is what the headset shows; the file's own views still draw wherever the
        /// kit's binding gives up (a side panel, an action the kit has no place for), as they always did.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>The rows of Activity a frame takes before the kit has laid any out.</summary>
        private const int DefaultRailRows = 8;

        private KitWorkView view = null!;
        private KitPresenceView presence = null!;
        private RoomBarView bar = null!;
        private RoomBand band = null!;

        /// <summary>The band of the agent's own words low in the view (DESIGN 1.6): a question's prompt while the redesign draws it.</summary>
        public RoomBand Band => band;

        private RoomField field = null!;
        private RoomCursor cursor = null!;

        /// <summary>
        /// While the redesign draws: where its foot line stands, up from this host's middle, in the host's own units. The
        /// panel and the column's lowest action stand on it and grow upward; the row and the band hang under it. The
        /// plane sets the host so this line stands on DESIGN's pitch (<see cref="RoomGeometry.FeetPitch"/>).
        /// </summary>
        public float FootLine { get; private set; }

        /// <summary>The working field, shown round the panel's place and the character while the agent works (the redesign).</summary>
        public RoomField Field => field;

        /// <summary>The agent's cursor, on the field, saying the step the runtime reports (the redesign).</summary>
        public RoomCursor Cursor => cursor;

        /// <summary>From the pair's foot line to the band's top edge, in dp: pitch -17.6 to -24.5 (DESIGN 1.1, 1.6).</summary>
        public const float BandUnderDp = (RoomWords.TopPitch - RoomGeometry.FeetPitch) * -RoomGeometry.DpPerDegree;

        /// <summary>The redesign's instruction (the row, its moments and its words), while <see cref="RoomDesign.Enabled"/> stands it in place of the kit.</summary>
        public RoomBarView Bar => bar;

        /// <summary>What voice is doing now, as the plane last drew it.</summary>
        public Func<VoiceStage> Voice { get; set; } = () => VoiceStage.Idle;

        /// <summary>The microphone's real level while hold to talk records; null where nothing records.</summary>
        public Func<LevelHistory?> MicLevel { get => bar.Level; set => bar.Level = value; }

        /// <summary>Whether the bound task's run was asked for and has not started.</summary>
        public Func<bool> AwaitingStart { get; set; } = () => false;
        private TextMeshPro measure = null!;
        private KitFileBinding? binding;
        private MenuColumn column;
        private object? owner;
        private MenuFrame? drawn;
        private float shownAt;
        private Prompt? held;
        private float heldAt = -1f;
        private bool holding;
        private bool compact;

        public KitWorkView View => view;

        /// <summary>The compact presence, shown by <see cref="ShowCompact"/> where the closed bar stands.</summary>
        public KitPresenceView Presence => presence;

        /// <summary>Whether it shows the compact presence rather than the task's file.</summary>
        public bool ShowsCompact => compact && gameObject.activeSelf;

        /// <summary>What the plane stands at its slot and scales to its distance and the text size.</summary>
        public Transform Part => transform;

        /// <summary>Its extent as laid out, in units of the plane's distance at the standard text size.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>Where its words can stand, relative to its centre, in units of the plane's distance at the standard text size, each with its em.</summary>
        public IReadOnlyList<TextBand> TextBands { get; private set; } = new TextBand[0];

        /// <summary>The rows of Activity's page the rail takes, for its row budget.</summary>
        public int RailRows { get; private set; } = DefaultRailRows;

        /// <summary>The rows the composer's well shows of the person's words.</summary>
        public int ComposerRows => KitWorkView.ComposerRows;

        /// <summary>The characters standing on it, for the curve: none while its avatar zone stays hidden (D3.8).</summary>
        public IReadOnlyList<Transform> Bodies { get; } = new Transform[0];

        /// <summary>Every control shown has stood long enough for a press to count (<see cref="GlazeButton.SettleSeconds"/>).</summary>
        public bool Settled => (binding != null || compact) && Time.unscaledTime - shownAt >= GlazeButton.SettleSeconds;

        /// <summary>Presses are passed over while false, as the context column's are.</summary>
        public Func<bool> Accepting { get; set; } = () => false;

        /// <summary>A press of a bound control: the frame's own action and key, with that frame; never a side panel.</summary>
        public event Action<MenuColumn, string, string?, MenuFrame?, SidePanel?>? Acted;

        /// <summary>Hold to talk held for <see cref="GlazeButton.HoldSeconds"/>, with the frame's own prompt.</summary>
        public event Action<MenuColumn, Prompt, MenuFrame?, SidePanel?>? HoldStarted;

        /// <summary>A hold ended: let go on it, or dropped, which always passes so a recording ends.</summary>
        public event Action<MenuColumn, Prompt, bool>? HoldEnded;

        /// <summary>A context row of the spine, by its key (project:, place:Projects, place:Tasks, workspace, task:), with the owner it was shown for.</summary>
        public event Action<string, object>? ContextRow;

        /// <summary>The frame shown has been drawn and every control settled, once per frame shown.</summary>
        public event Action<MenuColumn, MenuFrame>? Drawn;

        /// <summary>The spine switch's Compact pressed, with the frame shown, as a frame view's Compact raises it; the thumb waits for the presentation.</summary>
        public event Action<MenuColumn, MenuFrame>? CompactPressed;

        /// <summary>The compact presence's Workspace pressed, as the closed bar's Open raises it; the thumb waits for the presentation.</summary>
        public event Action? Opened;

        /// <summary>The composer's environment selector pill pressed.</summary>
        public event Action? EnvironmentPressed;

        /// <summary>The composer's project selector pill pressed.</summary>
        public event Action? ProjectPressed;

        /// <summary>The composer's agent selector pill pressed.</summary>
        public event Action? AgentPressed;

        public static KitWorkspaceHost Create(Transform parent)
        {
            var go = new GameObject("Spatial kit workspace");
            go.transform.SetParent(parent, false);
            var host = go.AddComponent<KitWorkspaceHost>();
            host.view = KitWorkView.Create(go.transform);
            host.view.AllowBoundSelectors = true;
            host.measure = KitType.Create(go.transform, "Composer measure", KitRole.Body, KitType.Text, KitType.Order.Words, leading: SpatialKit.ReadingLeading);
            host.measure.GetComponent<MeshRenderer>().enabled = false;
            var composer = host.view.Composer;
            composer.FieldPressed += () => host.Press(KitPlace.Field);
            // Where Send stands: Send, or on a question's page its press on, or the answers' Yes; beside Close: Stop or Discard
            // draft, or a question's way back, or the confirmation's Cancel.
            composer.SendPressed += () => host.Press(host.First(KitPlace.Forward, KitPlace.Yes, KitPlace.Send));
            composer.ClosePressed += () => host.Press(KitPlace.Close);
            composer.RarePressed += () => host.Press(host.First(KitPlace.Back, KitPlace.Cancel, KitPlace.Stop, KitPlace.Discard));
            composer.EnvironmentPressed += () => { if (host.Settled && host.Accepting()) host.EnvironmentPressed?.Invoke(); };
            composer.ProjectPressed += () => { if (host.Settled && host.Accepting()) host.ProjectPressed?.Invoke(); };
            composer.AgentPressed += () => { if (host.Settled && host.Accepting()) host.AgentPressed?.Invoke(); };
            composer.Talk.Pressed += host.OnTalkPressed;
            composer.Talk.Press.Target.Released += host.OnTalkReleased;
            // The redesign's actions are the frame's own controls; Hold to Talk is held, as the composer's is.
            host.bar = RoomBarView.Create(go.transform);
            host.bar.Acted += control => { if (host.binding != null) host.Acted?.Invoke(host.column, control.Action, control.Key, control.Frame, null); };
            host.bar.TalkPressed += host.OnTalkPressed;
            host.bar.TalkReleased += host.OnTalkReleased;
            host.band = RoomBand.Create(go.transform);
            host.field = RoomField.Create(go.transform);
            host.cursor = RoomCursor.Create(go.transform);
            host.bar.BandNextPressed += host.OnBandNext;
            var decision = host.view.Decision;
            decision.DenyPressed += () => host.Press(KitPlace.Deny);
            decision.ApprovePressed += () => host.Press(KitPlace.Approve);
            decision.CancelPressed += () => host.Press(KitPlace.Cancel);
            decision.YesPressed += () => host.Press(KitPlace.Yes);
            host.view.RowPressed += host.OnRow;
            host.view.Spine.Switch.CompactRequested += () => { if (host.binding != null) host.CompactPressed?.Invoke(host.column, host.binding.Frame); };
            host.presence = KitPresenceView.Create(go.transform);
            host.presence.Switch.WorkspaceRequested += () => { if (host.compact) host.Opened?.Invoke(); };
            foreach (var press in host.presence.Targets) press.Accepting = () => host.Accepting();
            host.presence.gameObject.SetActive(false);
            go.SetActive(false);
            return host;
        }

        /// <summary>
        /// Whether the kit draws <paramref name="binding"/> whole at the current text size: the person's words within the
        /// composer's rows, the request whole, and laid out, the agent's report whole beside everything the frame offers.
        /// Where it can't, as a long report beside the whole composer at the larger size, the file draws the frame instead.
        /// </summary>
        public bool Holds(KitFileBinding binding)
        {
            // A frame with no binding of its own stands only in the redesign, where a reader takes it and its column has room.
            if (binding.Plain)
                return RoomDesign.Enabled && RoomBar.Of(binding, Voice(), AwaitingStart()) is { Pair: true } page && page.Column.Count <= RoomBarView.ColumnMost;
            var model = binding.Model;
            // The redesign's panel holds every approval and every instruction under confirmation, read part by part, eight of
            // its own lines a part: but only where the file counted what is read by the panel's lines (RoomBarView.Lines), so
            // the file's parts are the panel's. Where it counted otherwise, the file draws the frame itself.
            // A question's page the redesign reads, with no more actions than its column holds; any other stays the file's own.
            if (RoomDesign.Enabled && model.Ask != null)
                return RoomBar.Of(binding, Voice(), AwaitingStart()) is { State: RoomBarState.Question } asking && asking.Column.Count <= RoomBarView.ColumnMost;
            // A moment outside the task's loop that draws the pair (the offered instructions, and any in RoomBar.Moments), likewise.
            if (RoomDesign.Enabled && RoomBar.Of(binding, Voice(), AwaitingStart()) is { FromMoment: true, Pair: true } moment)
                return moment.Column.Count <= RoomBarView.ColumnMost;
            // At work, and Stop's confirmation: nothing of either is measured, so the redesign always holds them.
            if (RoomDesign.Enabled && (model.Stops != null || RoomBar.Of(binding, Voice(), AwaitingStart()).Working)) return true;
            if (RoomDesign.Enabled && (model.Instruction != null || model.Need != null))
            {
                var read = RoomBar.Of(binding, Voice(), AwaitingStart());
                return read.Rows is not int rows || bar.Lines(read.Body, read.Mono) == rows;
            }
            if (model.Draft.Length > 0 && RowsOf(model.Draft) > ComposerRows) return false;
            if (model.Need?.Request is string request && RowsAt(request, SlateInner() - 2f * KitType.Scaled(1f)) > 8) return false;
            var shown = this.binding;
            view.Show(model, binding);
            var whole = view.ReadsWhole;
            if (shown is { Plain: false }) view.Show(shown.Model, shown);
            return whole;
        }

        /// <summary>The rows <paramref name="words"/> wrap to in the composer's well at the current text size.</summary>
        public int RowsOf(string words) => RowsAt(words, SlateInner() - 2f * KitType.Scaled(0.75f));

        /// <summary>Shows <paramref name="binding"/>, its frame from <paramref name="column"/>, the spine's context rows raised with <paramref name="owner"/>.</summary>
        public void Show(KitFileBinding binding, MenuColumn column, object owner)
        {
            var changed = compact || this.binding == null || !ReferenceEquals(this.binding.Frame, binding.Frame) || !ReferenceEquals(this.owner, owner);
            this.binding = binding;
            this.column = column;
            this.owner = owner;
            compact = false;
            gameObject.SetActive(true);
            presence.gameObject.SetActive(false);
            var zoom = GlazeText.Scale;
            // A plain binding has nothing for the old kit to show: only the redesign draws it.
            view.gameObject.SetActive(!binding.Plain);
            if (!binding.Plain)
            {
                view.transform.localScale = Vector3.one / zoom;
                view.Show(binding.Model, binding);
                // Rows are made as they are first shown: each takes presses only while the host does.
                foreach (var press in view.Targets) press.Accepting = () => Accepting();
                Size = view.Size / zoom;
                TextBands = Bands(view.Size, zoom);
                RailRows = Mathf.Max(1, Mathf.FloorToInt(RailRoom(view.Size) / RowPitch()));
            }
            ShowBar(binding);
            drawn = null;
            if (changed)
            {
                shownAt = Time.unscaledTime;
                foreach (var press in view.Targets) press.Unsettle();
                // Every action of the redesign too: one that keeps its words and moves on a new frame waits as any other.
                foreach (var press in bar.Presses) press.Unsettle();
                bar.Field.Unsettle();
            }
        }

        /// <summary>
        /// While the redesign is on (<see cref="RoomDesign.Enabled"/>), the old kit is not drawn at all (the owner, 2026-11-05):
        /// its spine, slate and rail and every row hide, and the room shows only the redesign's parts: the instruction's row, its
        /// draft and confirmation moments, an approval and its confirmation, or its words (<see cref="RoomBar"/>). What no
        /// redesign unit draws yet is absent: a frame asking a question shows nothing and is never reported drawn, so nothing
        /// counts as read that no one saw. Off, the kit stands whole and the instruction hides.
        /// </summary>
        private void ShowBar(KitFileBinding binding)
        {
            if (!RoomDesign.Enabled)
            {
                band.Hide();
                field.Hide();
                cursor.Hide();
                bar.Hide();
                view.gameObject.SetActive(true);
                view.Composer.ShowInstructionParts(true);
                return;
            }
            view.gameObject.SetActive(false);
            // The one sheen crosses the redesign's glass in the host's own plane.
            RoomSheen.Plane = transform;
            // As the kit's view was, it is laid at the person's text size and scaled back by it, so the plane's own scaling
            // leaves its widths as DESIGN gives them and its words larger at the larger size.
            var zoom = GlazeText.Scale;
            bar.transform.localScale = Vector3.one / zoom;
            bar.transform.localPosition = Vector3.zero;
            var room = RoomBar.Of(binding, Voice(), AwaitingStart());
            // The agent's own words stand on the band, never in the panel; the same words again keep the part it shows.
            if (room.Band != null) band.Show(room.Band);
            else band.Hide();
            bar.BandNext = band.Words?.Next;
            bar.Show(room);
            ShowWork(room, zoom);
            if (!bar.gameObject.activeSelf)
            {
                // Nothing drawn, standing where the row would, so the plane still places a real footprint.
                var rest = new Vector2(RoomType.Units(RoomGeometry.PanelDp), RoomType.Units(RoomButton.HeightDp)) / zoom;
                Size = rest;
                FootLine = -rest.y / 2f + RoomType.Units(RoomBarView.RowFeetDp) / zoom;
                TextBands = new[] { new TextBand(-rest.x / 2f, rest.x / 2f, -rest.y / 2f, rest.y / 2f, SpatialKit.Degrees(RoomGeometry.TypeDp) * zoom) };
                return;
            }
            var size = bar.Size / zoom;
            if (band.Words != null)
            {
                // The band hangs under the pair's foot line, centred, and grows downward; the whole is centred on the host.
                // As the instruction, it lays its words at the person's text size and is scaled back by it, so its width stays.
                band.transform.localScale = Vector3.one / zoom;
                var under = RoomType.Units(BandUnderDp) / zoom;
                var hangs = band.Size / zoom;
                var whole = size.y + under + hangs.y;
                bar.transform.localPosition = new Vector3(0f, (whole - size.y) / 2f, 0f);
                band.transform.localPosition = new Vector3(0f, whole / 2f - size.y - under, 0f);
                size = new Vector2(Mathf.Max(size.x, hangs.x), whole);
            }
            Size = size;
            FootLine = bar.transform.localPosition.y + bar.Foot / zoom;
            TextBands = new[] { new TextBand(-size.x / 2f, size.x / 2f, -size.y / 2f, size.y / 2f, SpatialKit.Degrees(RoomGeometry.TypeDp) * zoom) };
            foreach (var press in bar.Presses) press.Accepting = () => Accepting();
        }

        /// <summary>
        /// At work (DESIGN 2.3) the working field stands round the panel's place, 12 degrees up from the foot line (its
        /// character part dropped, <see cref="RoomGeometry.FieldTallDp"/>), and the agent's cursor stands on the field's
        /// middle saying the step the runtime reports; a runtime that reports none shows the field alone. Anything else,
        /// and both leave.
        /// </summary>
        private void ShowWork(RoomBar room, float zoom)
        {
            if (!room.Working)
            {
                field.Leave();
                cursor.Leave();
                return;
            }
            var inner = new Vector2(RoomType.Units(RoomGeometry.PanelDp), RoomType.Units(RoomGeometry.FieldTallDp));
            var middle = new Vector3(bar.PanelRight - inner.x / 2f, bar.Foot + inner.y / 2f, 0f) / zoom;
            field.transform.localScale = Vector3.one / zoom;
            field.transform.localPosition = bar.transform.localPosition + middle;
            field.Show(RoomField.Around(inner));
            cursor.transform.localScale = Vector3.one / zoom;
            cursor.transform.localPosition = field.transform.localPosition;
            cursor.Show(room.Step);
        }

        /// <summary>
        /// The band's next part pressed: it turns, and the frame is laid again with what the band holds now. The frame counts
        /// as drawn only once the band shows its last part (<see cref="ReportDrawn"/>), so a question is read before the file
        /// takes it as read.
        /// </summary>
        private void OnBandNext()
        {
            if (binding == null || !Settled || !Accepting() || !band.Next()) return;
            ShowBar(binding);
            drawn = null;
            shownAt = Time.unscaledTime;
            foreach (var press in bar.Presses) press.Unsettle();
        }


        /// <summary>
        /// Shows the compact presence where the closed bar stands, naming what the bar names (<paramref name="model"/>):
        /// its switch's Workspace raises <see cref="Opened"/>; nothing else in it takes a press.
        /// </summary>
        public void ShowCompact(KitPresenceModel model)
        {
            EndHold(false);
            var changed = !compact || !gameObject.activeSelf;
            binding = null;
            drawn = null;
            compact = true;
            band.Hide();
            field.Hide();
            cursor.Hide();
            gameObject.SetActive(true);
            view.gameObject.SetActive(false);
            presence.gameObject.SetActive(true);
            var zoom = GlazeText.Scale;
            presence.transform.localScale = Vector3.one / zoom;
            presence.Show(model);
            Size = presence.Size / zoom;
            var em = SpatialKit.Degrees(SpatialKit.BodyDp) * zoom;
            var half = presence.Size / 2f;
            TextBands = new[] { new TextBand(-half.x / zoom, half.x / zoom, -half.y / zoom, half.y / zoom, em) };
            if (changed)
            {
                shownAt = Time.unscaledTime;
                foreach (var press in presence.Targets) press.Unsettle();
            }
        }

        /// <summary>The presentation the existing routes confirmed: both switches' thumbs go there, at once when <paramref name="immediately"/>.</summary>
        public void ShowConfirmed(bool isCompact, bool immediately = false)
        {
            view.Spine.Switch.ShowConfirmed(isCompact, immediately);
            presence.Switch.ShowConfirmed(isCompact, immediately);
        }

        public void Hide()
        {
            EndHold(false);
            binding = null;
            drawn = null;
            compact = false;
            band.Hide();
            field.Hide();
            cursor.Hide();
            gameObject.SetActive(false);
        }

        /// <summary>For renders, which wait for nothing: every control as if settled.</summary>
        public void SettleForRender()
        {
            shownAt = float.NegativeInfinity;
            foreach (var press in view.Targets) press.SettleForRender();
            foreach (var press in presence.Targets) press.SettleForRender();
            foreach (var press in bar.Presses) press.SettleForRender();
            bar.SettleForRender();
            band.Settle();
            field.Settle();
            cursor.Settle();
        }

        /// <summary>For the Editor, which runs no frames: every control as if settled, and the frame shown reported drawn as a frame would.</summary>
        public void DrawnForRender()
        {
            SettleForRender();
            ReportDrawn();
        }

        private void Update()
        {
            ReportDrawn();
            if (held != null && !holding && heldAt >= 0f && Time.unscaledTime - heldAt >= GlazeButton.HoldSeconds)
            {
                holding = true;
                HoldStarted?.Invoke(column, held, binding?.Frame, null);
            }
        }

        private void OnDisable() => EndHold(false);

        private void ReportDrawn()
        {
            if (binding == null || !Settled || ReferenceEquals(drawn, binding.Frame)) return;
            // With the redesign on, a frame is drawn only where the bar shows it: nothing else of it stands in the room yet.
            if (RoomDesign.Enabled && !bar.gameObject.activeSelf) return;
            // The agent's words still hold a part not shown: the frame is not read yet. (Stop alone does not wait for this:
            // the navigator takes it from the frame as it shows, MenuNavigator.Taking.)
            if (RoomDesign.Enabled && band.Words is { More: true }) return;
            drawn = binding.Frame;
            Drawn?.Invoke(column, binding.Frame);
        }

        /// <summary>The first of <paramref name="places"/> the binding holds a control for, else the last.</summary>
        private KitPlace First(params KitPlace[] places) => places.Where(place => binding?[place] != null).DefaultIfEmpty(places[places.Length - 1]).First();

        private void Press(KitPlace place)
        {
            if (binding?[place] is KitControl control) Acted?.Invoke(column, control.Action, control.Key, control.Frame, null);
        }

        private void OnRow(string key)
        {
            if (binding == null) return;
            if (key.StartsWith(KitFileBinding.SectionRowKey, StringComparison.Ordinal))
            {
                var section = key.Substring(KitFileBinding.SectionRowKey.Length);
                if (binding.Sections.FirstOrDefault(each => each.Key == section) is KitControl control)
                    Acted?.Invoke(column, control.Action, control.Key, control.Frame, null);
                return;
            }
            if (key.StartsWith(KitWorkView.PageRowKey, StringComparison.Ordinal))
            {
                // A question page's answer or step raises its line's own action and key, with the frame it stood on.
                if (int.TryParse(key.Substring(KitWorkView.PageRowKey.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var row)
                    && binding.Controls.Where(control => control.Place is KitPlace.Choice or KitPlace.Step).ElementAtOrDefault(row) is KitControl step)
                    Acted?.Invoke(column, step.Action, step.Key, step.Frame, null);
                return;
            }
            if (key == KitWorkView.PagerKey)
            {
                Press(KitPlace.Pager);
                return;
            }
            if (key.StartsWith(KitWorkView.RouteKey, StringComparison.Ordinal))
            {
                // A route raises its line's own action and key, with the frame it stood on.
                if (int.TryParse(key.Substring(KitWorkView.RouteKey.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var at)
                    && binding.Routes.ElementAtOrDefault(at) is KitControl route)
                    Acted?.Invoke(column, route.Action, route.Key, route.Frame, null);
                return;
            }
            if (owner != null) ContextRow?.Invoke(key, owner);
        }

        /// <summary>Hold to talk pressed: it starts once held for the hold's time, with the frame's own prompt, as a held prompt does.</summary>
        private void OnTalkPressed()
        {
            if (binding?[KitPlace.Talk] is not KitControl talk || !talk.Holds) return;
            held = binding.Frame.Footer.All.Select(each => each.Prompt).FirstOrDefault(prompt => prompt.Id == talk.Action);
            heldAt = held == null ? -1f : Time.unscaledTime;
            holding = false;
        }

        private void OnTalkReleased(bool cancelled) => EndHold(!cancelled);

        private void EndHold(bool letGo)
        {
            if (holding && held != null) HoldEnded?.Invoke(column, held, letGo);
            held = null;
            heldAt = -1f;
            holding = false;
        }

        private static float SlateInner() => PlaneComposition.Units(SpatialKit.SlateDegrees) - 2f * KitType.Units(SpatialKit.PaddingDegrees);

        private int RowsAt(string words, float width)
        {
            KitType.Size(measure, KitRole.Body, SpatialKit.ReadingLeading);
            var runs = GlazeRuns.Of(measure);
            runs.Set(LineWords.Theirs(words));
            runs.SetProseMode(true);
            var (lines, _, _) = KitType.Lay(measure, Mathf.Max(KitType.Units(4f), width), 200);
            // The measure holds no words once it has counted them: it is never drawn, and nothing should read it as shown.
            runs.Set(LineWords.None);
            return lines;
        }

        private float RowPitch()
        {
            KitType.Size(measure, KitRole.Body);
            return KitType.LinePitch(measure) + KitType.Scaled(KitTrail.EntryGapDegrees);
        }

        /// <summary>The rail's room under its heading, as the view lays it, in the view's units.</summary>
        private static float RailRoom(Vector2 size)
        {
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var sides = size.y - 2f * KitType.Units(SpatialKit.SlateRiseDegrees);
            return Mathf.Max(0f, sides - 2f * pad - KitType.Scaled(3f));
        }

        /// <summary>
        /// Where the spine's, the slate's and the rail's words can stand, relative to the centre, at the standard text size:
        /// each region's whole visible face, padding included, since some words stand just inside it.
        /// </summary>
        private static TextBand[] Bands(Vector2 size, float zoom, float up = 0f)
        {
            var spine = PlaneComposition.Units(SpatialKit.SpineDegrees);
            var slate = PlaneComposition.Units(SpatialKit.SlateDegrees);
            var left = -size.x / 2f;
            var sides = size.y / 2f - KitType.Units(SpatialKit.SlateRiseDegrees);
            var em = SpatialKit.Degrees(SpatialKit.BodyDp) * zoom;
            TextBand Band(float from, float to, float half) => new TextBand(from / zoom, to / zoom, -half / zoom + up, half / zoom + up, em);
            return new[]
            {
                Band(left, left + spine, sides),
                Band(left + spine, left + spine + slate, size.y / 2f),
                Band(left + spine + slate, size.x / 2f, sides),
            };
        }
    }
}
