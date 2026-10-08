#nullable enable

namespace Halcyonic.Client
{
    /// <summary>How large Halcyonic's reading text is: as designed, or a step larger.</summary>
    public enum TextSize
    {
        Standard,
        Larger,
    }

    /// <summary>Halcyonic's sounds: as they are, quieter, or off.</summary>
    public enum SoundLevel
    {
        On,
        Quieter,
        Off,
    }

    /// <summary>
    /// The person's comfort settings (ADR 0023), kept on the device: reading text a step larger,
    /// Keep things still (ADR 0027), which stops everything that keeps moving on its own, and sounds
    /// quieter or off. What each means for the interface, and the words Settings says them in, are here.
    /// </summary>
    public sealed class Comfort
    {
        /// <summary>
        /// Reading text a step larger: by 15 percent, so body text of 18 dp reads at about 21. A
        /// foreground panel grows whole by it; on the stage, titles, the peek and the banner take it,
        /// and badge words and the rail's buttons, which stand where space is fixed, keep their size.
        /// </summary>
        public const float LargerTextScale = 1.15f;

        /// <summary>Quieter sounds, at half their amplitude, 6 dB down.</summary>
        public const float QuieterVolume = 0.5f;

        public const string Heading = "Comfort";

        public TextSize Text { get; set; }

        /// <summary>
        /// Keep things still (ADR 0027): nothing keeps moving on its own. A wait shows a still highlight in
        /// place of its shimmer, Starting's and Working's icons don't turn, Waiting for you doesn't breathe,
        /// Hold to talk's microphone doesn't pulse, and the characters stand at rest. What the person causes,
        /// a press, a slide, a file opening, still moves. Saved as before, so a headset that kept badges
        /// still keeps things still.
        /// </summary>
        public bool Still { get; set; }

        public SoundLevel Sounds { get; set; }

        /// <summary>How much larger than designed reading text is drawn.</summary>
        public float TextScale => Text == TextSize.Larger ? LargerTextScale : 1f;

        /// <summary>How loud Halcyonic's sounds play, from 0 to 1.</summary>
        public float Volume => Sounds switch
        {
            SoundLevel.On => 1f,
            SoundLevel.Quieter => QuieterVolume,
            _ => 0f,
        };

        /// <summary>The button that changes the text's size, to the other one.</summary>
        public string TextButton => Text == TextSize.Larger ? "Make text standard" : "Make text larger";

        /// <summary>The button that keeps things still, or lets them move again.</summary>
        public string MotionButton => Still ? "Let things move" : "Keep things still";

        /// <summary>The level the sound button steps to: on, quieter, off, and on again.</summary>
        public SoundLevel NextSounds => Sounds switch
        {
            SoundLevel.On => SoundLevel.Quieter,
            SoundLevel.Quieter => SoundLevel.Off,
            _ => SoundLevel.On,
        };

        /// <summary>The one button for sounds, named for the level it steps to, so three levels take one place.</summary>
        public string SoundButton => NextSounds switch
        {
            SoundLevel.Quieter => "Make sounds quieter",
            SoundLevel.Off => "Turn sounds off",
            _ => "Turn sounds on",
        };

        /// <summary>The settings in one line for the device to keep.</summary>
        public string Save() =>
            "text=" + (Text == TextSize.Larger ? "larger" : "standard")
            + ";motion=" + (Still ? "still" : "moving")
            + ";sounds=" + Sounds switch
            {
                SoundLevel.Quieter => "quieter",
                SoundLevel.Off => "off",
                _ => "on",
            };

        /// <summary>The settings the device kept, as <see cref="Save"/> wrote them; anything it can't read stays as designed.</summary>
        public static Comfort Load(string? saved)
        {
            var comfort = new Comfort();
            if (string.IsNullOrEmpty(saved)) return comfort;
            foreach (var part in saved!.Split(';'))
            {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length != 2) continue;
                switch (pair[0], pair[1])
                {
                    case ("text", "larger"):
                        comfort.Text = TextSize.Larger;
                        break;
                    case ("motion", "still"):
                        comfort.Still = true;
                        break;
                    case ("sounds", "quieter"):
                        comfort.Sounds = SoundLevel.Quieter;
                        break;
                    case ("sounds", "off"):
                        comfort.Sounds = SoundLevel.Off;
                        break;
                }
            }
            return comfort;
        }
    }
}
