#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Halcyonic.XR.Sound.Editor
{
    /// <summary>
    /// The stage's sound settings, both ways (<see cref="StageSound"/>): the project names Meta XR
    /// Audio's spatializer and the editor has it; the choice between it and Unity's panning; and each
    /// source as either setting makes it, every level and distance unchanged, the dropped connection's
    /// spread kept on Unity's panning.
    /// </summary>
    public static class SoundCheck
    {
        [MenuItem("Halcyonic/Check the Stage's Sound Settings")]
        public static void Menu()
        {
            var failures = Run();
            EditorUtility.DisplayDialog("Sound check", failures.Count == 0 ? "Every check passed." : string.Join("\n", failures), "OK");
        }

        /// <summary>The batch entry point: exits with 0 when every check passes, 1 otherwise.</summary>
        public static void Check()
        {
            var failures = Run();
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> Run()
        {
            var failures = new List<string>();
            var named = AudioSettings.GetSpatializerPluginName();
            if (named != StageSound.Spatializer) failures.Add($"the project's spatializer is \"{named}\", not \"{StageSound.Spatializer}\".");
            var available = AudioSettings.GetSpatializerPluginNames();
            if (!available.Contains(StageSound.Spatializer))
            {
                failures.Add($"the editor has no spatializer named \"{StageSound.Spatializer}\"; it has: {string.Join(", ", available)}.");
            }

            // The choice: the spatializer only when it is the one loaded and the option to pan is not set.
            Expect(failures, StageSound.HeadRelated(StageSound.Spatializer, panned: false), true, "the spatializer loaded, no option");
            Expect(failures, StageSound.HeadRelated(StageSound.Spatializer, panned: true), false, "the spatializer loaded, the option to pan set");
            Expect(failures, StageSound.HeadRelated(string.Empty, panned: false), false, "no spatializer loaded");
            Expect(failures, StageSound.HeadRelated(null, panned: false), false, "no spatializer named");
            Expect(failures, StageSound.HeadRelated("Another spatializer", panned: false), false, "another spatializer loaded");

            // Each source, both ways.
            var host = new GameObject("Sound check");
            try
            {
                foreach (var headRelated in new[] { true, false })
                {
                    foreach (var spread in new[] { 0f, StageSound.StageSpread })
                    {
                        var source = host.AddComponent<AudioSource>();
                        StageSound.Configure(source, spread, headRelated, 0.5f);
                        var what = (headRelated ? "with the spatializer" : "panned") + (spread == 0f ? ", a point" : ", spread");
                        Expect(failures, source.spatialize, headRelated && spread == 0f, what + ": through the spatializer");
                        Expect(failures, source.spatialBlend, 1f, what + ": fully spatial");
                        Expect(failures, source.dopplerLevel, 0f, what + ": no Doppler");
                        Expect(failures, source.rolloffMode, AudioRolloffMode.Logarithmic, what + ": rolloff");
                        Expect(failures, source.minDistance, StageSound.FullLevelWithin, what + ": full level within");
                        Expect(failures, source.maxDistance, StageSound.QuietestFrom, what + ": no quieter beyond");
                        Expect(failures, source.spread, spread, what + ": spread");
                        Expect(failures, source.volume, 0.5f, what + ": level");
                        Expect(failures, source.playOnAwake || source.loop, false, what + ": plays only when scheduled, once");
                        Object.DestroyImmediate(source);
                    }
                }
            }
            catch (Exception error)
            {
                failures.Add(error.ToString());
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
            foreach (var failure in failures) Debug.LogError("Halcyonic: sound check: " + failure);
            if (failures.Count == 0) Debug.Log("Halcyonic: sound check: every check passed; the spatializer is " + named + ".");
            return failures;
        }

        private static void Expect<T>(List<string> failures, T actual, T expected, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(actual, expected)) failures.Add($"{what}: {actual}, expected {expected}.");
        }
    }
}
