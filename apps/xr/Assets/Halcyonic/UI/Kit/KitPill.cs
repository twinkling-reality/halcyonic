#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Draws a capsule button's plate in the room's glossy capsule material (<see cref="KitPillMaterial"/>; shader
    /// <c>Halcyonic/Kit Pill</c>): a five-stop vertical gradient in its meaning (green, red or blue), a bottom inner neon
    /// glow, a crisp white top gloss band inset from the top and sides, and a 1.5 dp darker outer rim. Attached directly to
    /// the button's <see cref="Surface"/> plate, using one shared instanced material per <c>(Meaning, State)</c> while the
    /// shader reads <c>_Shape</c> and <c>_Fill.a</c> from the plate's own property block.
    /// </summary>
    public sealed class KitPill : MonoBehaviour
    {
        private const string ShaderName = "Halcyonic/Kit Pill";

        private static readonly int Stop0Id = Shader.PropertyToID("_Stop0");
        private static readonly int Stop1Id = Shader.PropertyToID("_Stop1");
        private static readonly int Stop2Id = Shader.PropertyToID("_Stop2");
        private static readonly int Stop3Id = Shader.PropertyToID("_Stop3");
        private static readonly int Stop4Id = Shader.PropertyToID("_Stop4");
        private static readonly int RimId = Shader.PropertyToID("_Rim");
        private static readonly int GlowId = Shader.PropertyToID("_Glow");
        private static readonly int GlossId = Shader.PropertyToID("_Gloss");
        private static readonly int SpecId = Shader.PropertyToID("_Spec");
        private static readonly int GlowGeomId = Shader.PropertyToID("_GlowGeom");

        private static readonly Dictionary<(KitPillMeaning Meaning, KitPillState State), Material> Materials =
            new Dictionary<(KitPillMeaning, KitPillState), Material>();

        private Surface plate = null!;
        private Material? surfaceMaterial;

        /// <summary>Whether the plate is currently drawn as a glossy Aero capsule rather than a flat surface.</summary>
        public bool Shown { get; private set; }

        /// <summary>The capsule's meaning (green, red or blue).</summary>
        public KitPillMeaning Meaning { get; private set; }

        /// <summary>The capsule's interaction state (rest, pointed or pressed).</summary>
        public KitPillState State { get; private set; }

        /// <summary>The capsule's size in its parent's units.</summary>
        public Vector2 Size { get; private set; }

        public static KitPill Attach(Surface plate)
        {
            var pill = plate.gameObject.AddComponent<KitPill>();
            pill.plate = plate;
            pill.surfaceMaterial = plate.Renderer.sharedMaterial;
            return pill;
        }

        public void Draw(Vector2 size, KitPillMeaning meaning, KitPillState state, float opacity = 1f)
        {
            _ = opacity;
            if (meaning == KitPillMeaning.None)
            {
                Hide();
                return;
            }
            Shown = true;
            Size = size;
            Meaning = meaning;
            State = state;
            var material = MaterialFor(meaning, state);
            if (plate.Renderer.sharedMaterial != material)
                plate.Renderer.sharedMaterial = material;
        }

        public void Hide()
        {
            if (!Shown) return;
            Shown = false;
            Meaning = KitPillMeaning.None;
            if (surfaceMaterial != null && plate.Renderer.sharedMaterial != surfaceMaterial)
                plate.Renderer.sharedMaterial = surfaceMaterial;
        }

        private static Material MaterialFor(KitPillMeaning meaning, KitPillState state)
        {
            var unit = KitGlass.Units(1f);
            if (!Materials.TryGetValue((meaning, state), out var material) || material == null)
            {
                var shader = Shader.Find(ShaderName);
                if (shader == null)
                {
                    Debug.LogError("Halcyonic: the shader " + ShaderName + " is missing, so glossy capsule buttons cannot render.");
                    var fallback = Shader.Find("Hidden/InternalErrorShader");
                    shader = fallback != null ? fallback : Shader.Find("Sprites/Default");
                }
                material = new Material(shader)
                {
                    name = "Kit pill " + meaning + " " + state,
                    enableInstancing = true,
                };
                var stops = KitPillMaterial.Stops(meaning);
                material.SetVector(Stop0Id, StopVector(meaning, stops[0].At, state));
                material.SetVector(Stop1Id, StopVector(meaning, stops[1].At, state));
                material.SetVector(Stop2Id, StopVector(meaning, stops[2].At, state));
                material.SetVector(Stop3Id, StopVector(meaning, stops[3].At, state));
                material.SetVector(Stop4Id, StopVector(meaning, stops[4].At, state));
                var rim = KitPillMaterial.Rim(meaning);
                var glow = KitPillMaterial.Glow(meaning);
                var (crownTop, crownBottom) = KitPillMaterial.CrownAlpha(meaning);
                var glowAlpha = KitPillMaterial.GlowAlphaOf(meaning);
                var catchAlpha = KitPillMaterial.TopCatchAlpha(meaning);
                material.SetVector(RimId, new Vector4(rim.R / 255f, rim.G / 255f, rim.B / 255f, 1f));
                material.SetVector(GlowId, new Vector4(glow.R / 255f, glow.G / 255f, glow.B / 255f, (float)glowAlpha));
                material.SetVector(GlossId, new Vector4(KitPillMaterial.GlossMidShare, KitPillMaterial.GlossEndShare, (float)KitPillMaterial.GlossHeightShare, (float)crownTop));
                material.SetVector(SpecId, new Vector4((float)crownBottom, (float)KitPillMaterial.GlowSideInsetShare, (float)KitPillMaterial.GlowHeightShare, (float)catchAlpha));
                Materials[(meaning, state)] = material;
            }
            material.SetVector(GlowGeomId, new Vector4((float)KitPillMaterial.GlowBottomInsetDp, (float)KitPillMaterial.GlowReach, unit, KitPillMaterial.RimDp));
            return material;
        }

        private static Vector4 StopVector(KitPillMeaning meaning, double at, KitPillState state)
        {
            var srgb = KitPillMaterial.At(meaning, at, state);
            return new Vector4((float)srgb[0], (float)srgb[1], (float)srgb[2], (float)at);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Materials.Clear();
        }
    }
}
