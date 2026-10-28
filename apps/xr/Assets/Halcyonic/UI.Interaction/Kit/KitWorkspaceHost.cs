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
        /// <summary>Whether the selected task is drawn by the kit rather than the file's own views; off until a headset session says otherwise.</summary>
        public static bool Enabled { get; set; }

        /// <summary>The rows of Activity a frame takes before the kit has laid any out.</summary>
        private const int DefaultRailRows = 8;

        private KitWorkView view = null!;
        private KitPresenceView presence = null!;
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

        public static KitWorkspaceHost Create(Transform parent)
        {
            var go = new GameObject("Spatial kit workspace");
            go.transform.SetParent(parent, false);
            var host = go.AddComponent<KitWorkspaceHost>();
            host.view = KitWorkView.Create(go.transform);
            host.measure = KitType.Create(go.transform, "Composer measure", KitRole.Body, KitType.Text, KitType.Order.Words, leading: SpatialKit.ReadingLeading);
            host.measure.GetComponent<MeshRenderer>().enabled = false;
            var composer = host.view.Composer;
            composer.FieldPressed += () => host.Press(KitPlace.Field);
            composer.SendPressed += () => host.Press(KitPlace.Send);
            composer.ClosePressed += () => host.Press(KitPlace.Close);
            composer.RarePressed += () => host.Press(host.binding?[KitPlace.Stop] != null ? KitPlace.Stop : KitPlace.Discard);
            composer.Talk.Pressed += host.OnTalkPressed;
            composer.Talk.Press.Target.Released += host.OnTalkReleased;
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
            var model = binding.Model;
            if (model.Draft.Length > 0 && RowsOf(model.Draft) > ComposerRows) return false;
            if (model.Need?.Request is string request && RowsAt(request, SlateInner() - 2f * KitType.Scaled(1f)) > 8) return false;
            var shown = this.binding;
            view.Show(model, binding);
            var whole = view.ReadsWhole;
            if (shown != null) view.Show(shown.Model, shown);
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
            view.gameObject.SetActive(true);
            var zoom = GlazeText.Scale;
            view.transform.localScale = Vector3.one / zoom;
            view.Show(binding.Model, binding);
            // Rows are made as they are first shown: each takes presses only while the host does.
            foreach (var press in view.Targets) press.Accepting = () => Accepting();
            Size = view.Size / zoom;
            TextBands = Bands(view.Size, zoom);
            RailRows = Mathf.Max(1, Mathf.FloorToInt(RailRoom(view.Size) / RowPitch()));
            if (changed)
            {
                shownAt = Time.unscaledTime;
                drawn = null;
                foreach (var press in view.Targets) press.Unsettle();
            }
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
            gameObject.SetActive(false);
        }

        /// <summary>For renders, which wait for nothing: every control as if settled.</summary>
        public void SettleForRender()
        {
            shownAt = float.NegativeInfinity;
            foreach (var press in view.Targets) press.SettleForRender();
            foreach (var press in presence.Targets) press.SettleForRender();
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
            drawn = binding.Frame;
            Drawn?.Invoke(column, binding.Frame);
        }

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
        private static TextBand[] Bands(Vector2 size, float zoom)
        {
            var spine = PlaneComposition.Units(SpatialKit.SpineDegrees);
            var slate = PlaneComposition.Units(SpatialKit.SlateDegrees);
            var left = -size.x / 2f;
            var sides = size.y / 2f - KitType.Units(SpatialKit.SlateRiseDegrees);
            var em = SpatialKit.Degrees(SpatialKit.BodyDp) * zoom;
            TextBand Band(float from, float to, float half) => new TextBand(from / zoom, to / zoom, -half / zoom, half / zoom, em);
            return new[]
            {
                Band(left, left + spine, sides),
                Band(left + spine, left + spine + slate, size.y / 2f),
                Band(left + spine + slate, size.x / 2f, sides),
            };
        }
    }
}
