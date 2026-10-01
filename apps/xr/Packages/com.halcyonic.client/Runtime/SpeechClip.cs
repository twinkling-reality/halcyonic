#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// A held clip of speech as the control plane takes it (ADR 0021): 16-bit mono PCM at 16 kHz in a
    /// WAV, 0.5 to 30 seconds. Made from whatever the microphone gives, at its own rate and channels.
    /// </summary>
    public static class SpeechClip
    {
        public const int SampleRate = 16000;
        public const double MinSeconds = 0.5;
        public const double MaxSeconds = 30;

        /// <summary>The largest clip: a 44-byte header and 30 seconds of samples.</summary>
        public const int MaxBytes = 44 + (int)(MaxSeconds * SampleRate) * 2;

        /// <summary>
        /// The first <paramref name="count"/> interleaved samples, from -1 to 1 at
        /// <paramref name="sampleRate"/> with <paramref name="channels"/> channels, mixed to mono,
        /// resampled to 16 kHz and written as a WAV; anything past 30 seconds is left out. Null when
        /// less than half a second was captured, which the control plane would refuse.
        /// </summary>
        public static byte[]? Encode(float[] samples, int count, int channels, int sampleRate)
        {
            if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
            if (sampleRate < 1000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (count < 0 || count > samples.Length) throw new ArgumentOutOfRangeException(nameof(count));
            var mono = Mono(samples, count / channels, channels);
            var resampled = Resample(mono, sampleRate);
            var length = Math.Min(resampled.Length, (int)(MaxSeconds * SampleRate));
            if (length < MinSeconds * SampleRate) return null;
            var wav = new byte[44 + length * 2];
            WriteHeader(wav, length * 2);
            for (var index = 0; index < length; index++)
            {
                var value = (short)Math.Round(Math.Max(-1f, Math.Min(1f, resampled[index])) * short.MaxValue);
                wav[44 + index * 2] = (byte)value;
                wav[44 + index * 2 + 1] = (byte)(value >> 8);
            }
            return wav;
        }

        private static float[] Mono(float[] samples, int frames, int channels)
        {
            var mono = new float[frames];
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < channels; channel++) sum += samples[frame * channels + channel];
                mono[frame] = sum / channels;
            }
            return mono;
        }

        /// <summary>
        /// To 16 kHz: going down, each output sample is the mean of the input it spans, which also keeps
        /// out most of what 16 kHz cannot carry; going up, a straight line between neighbours.
        /// </summary>
        private static float[] Resample(float[] input, int rate)
        {
            if (rate == SampleRate) return input;
            var ratio = (double)rate / SampleRate;
            var output = new float[(int)Math.Floor(input.Length / ratio)];
            for (var index = 0; index < output.Length; index++)
            {
                if (ratio > 1)
                {
                    var start = index * ratio;
                    var end = start + ratio;
                    var sum = 0.0;
                    for (var at = (int)Math.Floor(start); at < end && at < input.Length; at++)
                    {
                        var weight = Math.Min(at + 1, end) - Math.Max(at, start);
                        sum += input[at] * weight;
                    }
                    output[index] = (float)(sum / ratio);
                }
                else
                {
                    var position = index * ratio;
                    var at = (int)position;
                    var next = Math.Min(at + 1, input.Length - 1);
                    var fraction = (float)(position - at);
                    output[index] = input[at] + (input[next] - input[at]) * fraction;
                }
            }
            return output;
        }

        private static void WriteHeader(byte[] wav, int dataBytes)
        {
            void Text(int at, string value)
            {
                for (var index = 0; index < 4; index++) wav[at + index] = (byte)value[index];
            }
            void UInt32(int at, int value)
            {
                wav[at] = (byte)value;
                wav[at + 1] = (byte)(value >> 8);
                wav[at + 2] = (byte)(value >> 16);
                wav[at + 3] = (byte)(value >> 24);
            }
            void UInt16(int at, int value)
            {
                wav[at] = (byte)value;
                wav[at + 1] = (byte)(value >> 8);
            }
            Text(0, "RIFF");
            UInt32(4, 36 + dataBytes);
            Text(8, "WAVE");
            Text(12, "fmt ");
            UInt32(16, 16);
            UInt16(20, 1);
            UInt16(22, 1);
            UInt32(24, SampleRate);
            UInt32(28, SampleRate * 2);
            UInt16(32, 2);
            UInt16(34, 16);
            Text(36, "data");
            UInt32(40, dataBytes);
        }
    }
}
