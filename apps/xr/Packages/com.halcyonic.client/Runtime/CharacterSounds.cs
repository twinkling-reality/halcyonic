#nullable enable
using System;
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>A character's own sounds, placed at its figure (CHARACTER.md section 7); nothing loops.</summary>
    public enum CharacterSound
    {
        /// <summary>A task starts: two bells.</summary>
        Arrive,

        /// <summary>Each turn in a meeting, and the eye pulse as a moment leaves: two blips on its own pair of notes.</summary>
        Voice,

        /// <summary>The hop at the computer's own passed check, only.</summary>
        Hop,

        /// <summary>Put away, or characters turned off: a short falling sweep.</summary>
        Fold,
    }

    /// <summary>One part of a cue: a sine from one pitch to another, or a noise band sweeping, from its start, in ms.</summary>
    public readonly struct CharacterTone
    {
        public CharacterTone(double startMs, double durationMs, double fromHz, double toHz, bool noise)
        {
            StartMs = startMs;
            DurationMs = durationMs;
            FromHz = fromHz;
            ToHz = toHz;
            Noise = noise;
        }

        public double StartMs { get; }

        public double DurationMs { get; }

        public double FromHz { get; }

        public double ToHz { get; }

        public bool Noise { get; }
    }

    /// <summary>
    /// The character's cues in the kit's key, A major pentatonic (the art direction's character handoff, 2026-10-30, section 5):
    /// identity by voice, each top feature its own pair of notes.
    /// </summary>
    public static class CharacterSounds
    {
        /// <summary>The cue's level, in dB.</summary>
        public static double LevelDb(CharacterSound sound) => sound switch
        {
            CharacterSound.Arrive => -30,
            CharacterSound.Voice => -36,
            CharacterSound.Hop => -32,
            CharacterSound.Fold => -33,
            _ => throw new ArgumentOutOfRangeException(nameof(sound), sound, "a sound the character has no level for"),
        };

        /// <summary>The character's own two notes, by its top feature.</summary>
        public static (string First, string Second) VoicePair(KitFigureFeature feature) => feature switch
        {
            KitFigureFeature.Antenna => ("A6", "E6"),
            KitFigureFeature.Fins => ("C#6", "A6"),
            KitFigureFeature.Crest => ("E6", "F#6"),
            KitFigureFeature.Loop => ("F#6", "C#6"),
            KitFigureFeature.Twin => ("B5", "E6"),
            KitFigureFeature.Brim => ("A5", "C#6"),
            _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "a feature with no voice"),
        };

        /// <summary>The cue's parts; the Voice is the feature's own.</summary>
        public static IReadOnlyList<CharacterTone> Tones(CharacterSound sound, KitFigureFeature feature)
        {
            switch (sound)
            {
                case CharacterSound.Arrive:
                    return new[] { Bell(0, 260, "A5"), Bell(90, 260, "E6") };
                case CharacterSound.Voice:
                    var (first, second) = VoicePair(feature);
                    return new[] { Bell(0, 70, first), Bell(110, 70, second) };
                case CharacterSound.Hop:
                    return new[] { new CharacterTone(0, 120, Hz("E6"), Hz("A6"), false) };
                case CharacterSound.Fold:
                    return new[] { new CharacterTone(0, 160, 2400, 400, true) };
                default:
                    throw new ArgumentOutOfRangeException(nameof(sound), sound, "a sound the character does not make");
            }
        }

        /// <summary>A note's pitch in equal temperament from A4 at 440 Hz: "A5", "C#6".</summary>
        public static double Hz(string note)
        {
            var sharp = note.Length == 3 && note[1] == '#';
            var semitone = "C D EF G A B".IndexOf(note[0]);
            if (semitone < 0 || note.Length != (sharp ? 3 : 2)) throw new ArgumentException("not a note: " + note, nameof(note));
            var octave = note[note.Length - 1] - '0';
            var fromA4 = semitone + (sharp ? 1 : 0) - 9 + (octave - 4) * 12;
            return 440 * Math.Pow(2, fromA4 / 12.0);
        }

        private static CharacterTone Bell(double startMs, double durationMs, string note) => new CharacterTone(startMs, durationMs, Hz(note), Hz(note), false);
    }
}
