#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>What the compact presence keeps in view: the retained task, its state, and what waits anywhere in the workspace.</summary>
    public sealed class KitPresenceModel
    {
        public KitPresenceModel(string taskTitle, StateBadge badge, int waiting)
        {
            TaskTitle = taskTitle;
            Badge = badge;
            Waiting = waiting;
        }

        public string TaskTitle { get; }

        public StateBadge Badge { get; }

        /// <summary>How many tasks wait for the person across the workspace, as known now.</summary>
        public int Waiting { get; }

        /// <summary>The count said in full, as there is room for it (WORDS.md): a count keeps its noun and its verb.</summary>
        public string WaitingWords => Waiting == 0 ? "Nothing is waiting for you."
            : Waiting == 1 ? "1 task is waiting for you." : Waiting + " tasks are waiting for you.";
    }

    /// <summary>
    /// The compact presence: the whole instrument folded to one small opaque piece that stays beside other work. The
    /// connected switch stands at its left, its thumb on Compact; beside it the retained task's name, its state in
    /// icon and word, and how many tasks wait for the person anywhere, in words. It accepts no work input: its only
    /// targets are the switch's two positions, and Workspace forwards to the existing Open route, which restores the
    /// retained task and draft and asks for fresh input authority. Nothing in it moves on its own except the state's
    /// own icon. Centred on its transform, built in units of the plane's distance.
    /// </summary>
    public sealed class KitPresenceView : MonoBehaviour
    {
        private KitCarrier carrier = null!;
        private KitModeSwitch modeSwitch = null!;
        private TextMeshPro title = null!;
        private KitStateSignal state = null!;
        private TextMeshPro waitingIcon = null!;
        private TextMeshPro waiting = null!;
        private Vector2 size;

        public KitCarrier Carrier => carrier;
        public KitModeSwitch Switch => modeSwitch;
        public TextMeshPro Title => title;
        public KitStateSignal State => state;
        public TextMeshPro Waiting => waiting;
        public Vector2 Size => size;

        public IEnumerable<KitPress> Targets
        {
            get
            {
                yield return modeSwitch.WorkspaceTarget;
                yield return modeSwitch.CompactTarget;
            }
        }

        public static KitPresenceView Create(Transform parent, string name = "Compact presence")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<KitPresenceView>();
            view.carrier = KitCarrier.Create(go.transform, "Presence", KitType.Order.Carrier + 3);
            view.modeSwitch = KitModeSwitch.Create(view.carrier.transform);
            view.title = KitType.Create(view.carrier.transform, "Task", KitRole.Strong, KitType.Text, KitType.Order.Words);
            view.state = KitStateSignal.Create(view.carrier.transform, "State");
            view.waitingIcon = GlazeIcons.Create(view.carrier.transform, "Waiting mark", SpatialKit.IconDegrees, KitType.ToneText(GlazeTone.Attention), KitType.Order.Words);
            view.waiting = KitType.Create(view.carrier.transform, "What waits", KitRole.Body, KitType.ToneText(GlazeTone.Attention), KitType.Order.Words, TextAlignmentOptions.Left);
            view.waiting.rectTransform.pivot = new Vector2(0f, 0.5f);
            return view;
        }

        public void Show(KitPresenceModel model)
        {
            var pad = KitType.Scaled(1.1f);
            var width = PlaneComposition.Units(SpatialKit.PresenceDegrees) * Mathf.Lerp(1f, GlazeText.Scale, 0.5f);
            var switchSize = modeSwitch.Layout(modeSwitch.NarrowestWidth());
            var textLeft = pad + switchSize.x + KitType.Scaled(1.1f);
            var textWidth = width - textLeft - pad;

            KitType.Size(title, KitRole.Strong);
            GlazeRuns.Of(title).SetIdentity(model.TaskTitle);
            var (_, _, titleHeight) = KitType.Lay(title, textWidth, 2);
            var stateSize = state.Show(model.Badge, onPill: false);
            KitType.Size(waiting, KitRole.Body);
            GlazeText.SetLiteral(waiting, model.WaitingWords);
            waiting.color = model.Waiting > 0 ? KitType.ToneText(GlazeTone.Attention) : KitType.Secondary;
            var iconEm = KitType.Units(SpatialKit.IconDegrees * GlazeText.Scale);
            waitingIcon.gameObject.SetActive(model.Waiting > 0);
            var waitingLeft = model.Waiting > 0 ? iconEm + KitType.Scaled(0.4f) : 0f;
            var (_, _, waitingHeight) = KitType.Lay(waiting, textWidth - waitingLeft, 1);
            var textHeight = titleHeight + KitType.Scaled(0.3f) + stateSize.y + KitType.Scaled(0.2f) + waitingHeight;
            var height = Mathf.Max(switchSize.y, textHeight) + 2f * pad;
            size = new Vector2(width, height);
            var tone = Glaze.Tone(model.Badge.LastKnown ? GlazeTone.Unknown : model.Badge.Tone);
            carrier.Draw(size, KitType.Units(SpatialKit.InstrumentRadiusDegrees), SpatialKit.Slate, SpatialKit.Edge, lit: true,
                frame: KitType.Mix(SpatialKit.Slate, tone.Strong, 0.55f), light: KitType.Mix(SpatialKit.Slate, tone.Strong, 0.92f));

            var left = -width / 2f;
            modeSwitch.transform.localPosition = new Vector3(left + pad + switchSize.x / 2f, 0f, 0f);
            var top = textHeight / 2f;
            title.transform.localPosition = new Vector3(left + textLeft, top, -0.0005f);
            var stateY = top - titleHeight - KitType.Scaled(0.3f) - stateSize.y / 2f;
            state.transform.localPosition = new Vector3(left + textLeft + stateSize.x / 2f, stateY, -0.0003f);
            var waitingY = stateY - stateSize.y / 2f - KitType.Scaled(0.2f) - waitingHeight / 2f;
            if (model.Waiting > 0)
            {
                waitingIcon.fontSize = GlazeTokens.FontSize(iconEm);
                waitingIcon.rectTransform.sizeDelta = new Vector2(iconEm, iconEm);
                GlazeIcons.Show(waitingIcon, GlazeIcon.WaitingForYou);
                waitingIcon.transform.localPosition = new Vector3(left + textLeft + iconEm / 2f, waitingY, -0.0005f);
            }
            waiting.transform.localPosition = new Vector3(left + textLeft + waitingLeft, waitingY, -0.0005f);
        }

        public IEnumerable<Vector3> Corners() => carrier.Corners();
    }
}
