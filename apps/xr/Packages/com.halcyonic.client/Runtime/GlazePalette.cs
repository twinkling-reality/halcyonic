#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Halcyonic.Client
{
    /// <summary>Immutable semantic colours, separate from type, geometry and motion.</summary>
    public sealed class GlazePalette
    {
        public GlazeColor Panel { get; }
        public GlazeColor Raised { get; }
        public GlazeColor Control { get; }
        public GlazeColor ControlHover { get; }
        public GlazeColor ControlPressed { get; }
        public GlazeColor Well { get; }
        public GlazeColor Text { get; }
        public GlazeColor TextSecondary { get; }
        public GlazeColor TextDisabled { get; }
        public GlazeColor Outline { get; }
        private GlazePalette(string key, string name, GlazeColor panel, GlazeColor raised, GlazeColor control, GlazeColor controlHover, GlazeColor controlPressed, GlazeColor well, GlazeColor text, GlazeColor textSecondary, GlazeColor textDisabled, GlazeColor outline, IReadOnlyDictionary<GlazeTone, GlazeToneColors> tones)
        {
            Key = key;
            Name = name;
            Panel = panel;
            Raised = raised;
            Control = control;
            ControlHover = controlHover;
            ControlPressed = controlPressed;
            Well = well;
            Text = text;
            TextSecondary = textSecondary;
            TextDisabled = textDisabled;
            Outline = outline;
            this.tones = new ReadOnlyDictionary<GlazeTone, GlazeToneColors>(new Dictionary<GlazeTone, GlazeToneColors>(tones));
        }

        private readonly IReadOnlyDictionary<GlazeTone, GlazeToneColors> tones;
        public string Key { get; }
        public string Name { get; }
        public GlazeColor Focus => GlazeColor.Hex(0xFFFFFF);
        public GlazeColor Selection => GlazeColor.Hex(0xFFFFFF);
        public GlazeColor Ground => GlazeColor.Hex(0xFFFFFF);
        public GlazeColor EdgeLight => GlazeColor.Hex(0xFFFFFF);
        public GlazeToneColors Tone(GlazeTone tone) => tones.TryGetValue(tone, out var value) ? value : throw new ArgumentOutOfRangeException(nameof(tone));
        private static GlazePalette Make(bool graphite, bool instrument = false)
        {
            var Panel = GlazeColor.Hex(0x1B222D);
            var Raised = GlazeColor.Hex(0x242D3A);
            var Control = GlazeColor.Hex(0x303C4E);
            var ControlHover = GlazeColor.Hex(0x3B4A61);
            var ControlPressed = GlazeColor.Hex(0x46566F);
            var Well = GlazeColor.Hex(0x151B24);
            var Text = GlazeColor.Hex(0xEEF2F6);
            var TextSecondary = GlazeColor.Hex(0xAAB5C2);
            var TextDisabled = GlazeColor.Hex(0x8A96A5);
            var Outline = GlazeColor.Hex(0x73839A);
            var tones = new Dictionary<GlazeTone, GlazeToneColors>
            {
                [GlazeTone.Neutral] = new GlazeToneColors(TextSecondary, Raised, Control, Text),
                [GlazeTone.Active] = new GlazeToneColors(GlazeColor.Hex(0xABDEF3), GlazeColor.Hex(0x173044), GlazeColor.Hex(0x8FD0EC), GlazeColor.Hex(0x0B1422)),
                [GlazeTone.Attention] = new GlazeToneColors(GlazeColor.Hex(0xF7CF70), GlazeColor.Hex(0x3D3216), GlazeColor.Hex(0xF3C04F), GlazeColor.Hex(0x1C1404)),
                [GlazeTone.Success] = new GlazeToneColors(GlazeColor.Hex(0x8ADBB1), GlazeColor.Hex(0x183528), GlazeColor.Hex(0x6CCB9A), GlazeColor.Hex(0x08170F)),
                [GlazeTone.Failure] = new GlazeToneColors(GlazeColor.Hex(0xFF9F94), GlazeColor.Hex(0x3F1D1A), GlazeColor.Hex(0xEE7F73), GlazeColor.Hex(0x1F0806)),
                [GlazeTone.Unknown] = new GlazeToneColors(GlazeColor.Hex(0xB7C0CB), GlazeColor.Hex(0x262D36), GlazeColor.Hex(0xB7C0CB), GlazeColor.Hex(0x1B222D)),
                [GlazeTone.Simulated] = new GlazeToneColors(GlazeColor.Hex(0xCBBBF6), GlazeColor.Hex(0x2B2442), GlazeColor.Hex(0xCBBBF6), GlazeColor.Hex(0x1B222D)),
                [GlazeTone.Accent] = new GlazeToneColors(GlazeColor.Hex(0xA3C3FF), GlazeColor.Hex(0x22324F), GlazeColor.Hex(0x7FA9F8), GlazeColor.Hex(0x0B1422)),
            };
            if (graphite)
            {
                Panel = GlazeColor.Hex(0x20242A);
                Raised = GlazeColor.Hex(0x2A3038);
                Control = GlazeColor.Hex(0x363F49);
                ControlHover = GlazeColor.Hex(0x45505D);
                ControlPressed = GlazeColor.Hex(0x526071);
                Well = GlazeColor.Hex(0x191D23);
                tones[GlazeTone.Neutral] = new GlazeToneColors(TextSecondary, Raised, Control, Text);
                tones[GlazeTone.Accent] = new GlazeToneColors(GlazeColor.Hex(0xB3D0FA), GlazeColor.Hex(0x263650), GlazeColor.Hex(0x98BAF0), GlazeColor.Hex(0x0B1422));
            }
            if (instrument)
            {
                Panel = GlazeColor.Hex(0x242726); Raised = GlazeColor.Hex(0x2E3330);
                Control = GlazeColor.Hex(0x3A423D); ControlHover = GlazeColor.Hex(0x49534C); ControlPressed = GlazeColor.Hex(0x59645C);
                Well = GlazeColor.Hex(0x1D211F); Text = GlazeColor.Hex(0xE9E7E0); TextSecondary = GlazeColor.Hex(0xB9BEB9);
                TextDisabled = GlazeColor.Hex(0x949D96); Outline = GlazeColor.Hex(0x78887C);
                tones[GlazeTone.Neutral] = new GlazeToneColors(TextSecondary, Raised, Control, Text);
                tones[GlazeTone.Accent] = new GlazeToneColors(GlazeColor.Hex(0xA3C3FF), GlazeColor.Hex(0x22324F), GlazeColor.Hex(0x7FA9F8), GlazeColor.Hex(0x0B1422));
            }
            return new GlazePalette(instrument ? "neutral" : graphite ? "graphite" : "glaze", instrument ? "Neutral" : graphite ? "Graphite" : "Glaze",
                Panel, Raised, Control, ControlHover, ControlPressed, Well, Text, TextSecondary, TextDisabled, Outline, tones);
        }

        public static GlazePalette Glaze { get; } = Make(false);
        public static GlazePalette Graphite { get; } = Make(true);
        public static GlazePalette Neutral { get; } = Make(false, true);
        public static GlazePalette Default => Neutral;
        /// <summary>Compatibility alias for the comparison fixture; never a separate saved preset.</summary>
        public static GlazePalette InstrumentStudy => Neutral;
        public static IReadOnlyList<GlazePalette> Presets { get; } = Array.AsReadOnly(new[] { Neutral, Glaze, Graphite });
        public static GlazePalette? Find(string key) => key == Neutral.Key ? Neutral : key == Glaze.Key ? Glaze : key == Graphite.Key ? Graphite : null;
    }
}
