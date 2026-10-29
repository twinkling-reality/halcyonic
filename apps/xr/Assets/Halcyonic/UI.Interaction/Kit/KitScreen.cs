#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// A picked project's screen (ADR 0040, rule 2; R03 YouTube VR, R04 visionOS windows): one large opaque face, the
    /// project's title chip at its top left with the runtime and the state, and a neutral two-letter mark where no picture
    /// stands. A picture comes only from the computer's own check screenshot, which no check takes yet, so none is drawn
    /// here; nothing on the screen ever says "last checked build" without one. Sizes are <see cref="SpatialKit"/>'s, in units
    /// of the viewing distance; it is centred on its transform and never places or faces itself.
    /// </summary>
    public sealed class KitScreen : MonoBehaviour
    {
        private const float RadiusDegrees = 1.2f;
        private const float MarkDegrees = 10f;
        private const float MarkEmDegrees = 4.5f;
        private const float ChipInsetDegrees = 1.5f;
        private const float ChipPadDegrees = 0.9f;

        private KitCarrier face = null!;
        private Surface disc = null!;
        private TextMeshPro mark = null!;
        private KitCarrier chip = null!;
        private TextMeshPro title = null!;
        private TextMeshPro facts = null!;

        public Vector2 Size { get; private set; }

        public KitCarrier Face => face;

        public KitCarrier TitleChip => chip;

        public TextMeshPro Mark => mark;

        public TextMeshPro Title => title;

        public TextMeshPro Facts => facts;

        public IEnumerable<KitCarrier> Carriers => new[] { face, chip };

        /// <summary>Its words laid whole: the title and its facts on one line inside the chip.</summary>
        public bool ReadsWhole { get; private set; }

        public static KitScreen Create(Transform parent)
        {
            var go = new GameObject("Project screen");
            go.transform.SetParent(parent, false);
            var screen = go.AddComponent<KitScreen>();
            screen.face = KitCarrier.Create(go.transform, "Screen face");
            screen.disc = Surface.Create(go.transform, "Mark disc", KitType.Order.Plate);
            screen.mark = KitType.Create(go.transform, "Mark", KitRole.Identity, KitType.Colour(SpatialKit.TextTertiary), KitType.Order.Words, TextAlignmentOptions.Center);
            screen.mark.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            screen.mark.textWrappingMode = TextWrappingModes.NoWrap;
            screen.chip = KitCarrier.Create(go.transform, "Title chip", KitType.Order.Plate);
            screen.title = KitType.Create(screen.chip.transform, "Title", KitRole.Heading, KitType.Text, KitType.Order.Words);
            screen.title.textWrappingMode = TextWrappingModes.NoWrap;
            screen.facts = KitType.Create(screen.chip.transform, "Runtime and state", KitRole.Body, KitType.Secondary, KitType.Order.Words);
            screen.facts.textWrappingMode = TextWrappingModes.NoWrap;
            return screen;
        }

        public void Show(KitScreenModel model)
        {
            // A large part's extent: twice the tangent of its half angle, so it spans its angle seen from its centre.
            var width = 2f * KitType.Units(SpatialKit.ScreenDegrees / 2f);
            var height = 2f * KitType.Units(SpatialKit.ScreenHeightDegrees / 2f);
            Size = new Vector2(width, height);
            face.Draw(Size, KitType.Units(RadiusDegrees), SpatialKit.Well);

            // No picture source exists yet: the neutral mark stands in its place.
            var markShown = model.Monogram.Length > 0;
            disc.gameObject.SetActive(markShown);
            mark.gameObject.SetActive(markShown);
            if (markShown)
            {
                var diameter = KitType.Units(MarkDegrees);
                disc.Draw(new Vector2(diameter, diameter), diameter / 2f, KitType.Colour(SpatialKit.Raised));
                disc.transform.localPosition = new Vector3(0f, 0f, -0.0003f);
                GlazeText.SetLiteral(mark, model.Monogram);
                mark.fontSize = GlazeTokens.FontSize(KitType.Units(MarkEmDegrees));
                mark.rectTransform.sizeDelta = new Vector2(diameter, diameter);
                mark.transform.localPosition = new Vector3(0f, 0f, -0.0006f);
            }

            // The title chip: the project's name, then the runtime and the state, on one opaque carrier.
            var pad = KitType.Units(ChipPadDegrees);
            var inset = KitType.Units(ChipInsetDegrees);
            KitType.Size(title, KitRole.Heading);
            // Body, not Label: the chip stands toward the screen's corner, where Label would read under the floor (STYLE, Type).
            KitType.Size(facts, KitRole.Body);
            GlazeText.SetLiteral(title, model.Project);
            GlazeText.SetLiteral(facts, model.Runtime + " · " + model.State);
            var room = width - 2f * inset - 3f * pad;
            var (titleLines, titleWidth, titleHeight) = KitType.Lay(title, room * 0.6f, 1);
            var (factLines, factWidth, _) = KitType.Lay(facts, room - titleWidth, 1);
            ReadsWhole = titleLines == 1 && factLines == 1 && !title.isTextTruncated && !facts.isTextTruncated;
            var chipSize = new Vector2(titleWidth + factWidth + 3f * pad, titleHeight + 2f * pad * 0.6f);
            chip.Draw(chipSize, KitType.Units(SpatialKit.ControlRadiusDegrees), SpatialKit.Carrier);
            chip.transform.localPosition = new Vector3(-width / 2f + inset + chipSize.x / 2f, height / 2f - inset - chipSize.y / 2f, -0.0004f);
            title.transform.localPosition = new Vector3(-chipSize.x / 2f + pad, titleHeight / 2f, -0.0003f);
            facts.transform.localPosition = new Vector3(-chipSize.x / 2f + 2f * pad + titleWidth, titleHeight / 2f - (KitType.Em(KitRole.Heading) - KitType.Em(KitRole.Body)) * 0.8f, -0.0003f);
            gameObject.SetActive(true);
        }
    }
}
