#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// The room's companion character (ADR 0040; ROBOT6): a floating pearl-white porcelain ovoid with an inverted-teardrop
    /// torso and curved upper saddle collar well (#141C26), a recessed waist seam groove (#0B0E13) and glowing belt, two
    /// detached tapered capsule-wing arms with an 11 mm air gap, a floating rounded-conical dome head with a recessed
    /// gloss-black front oval visor (#030406), two tilted oval LED scanline eyes, and a 3-layer spatial ground projection
    /// on the floor beneath it. Everything is drawn through two instanced shaders (<c>Halcyonic/Kit Pearl</c> and
    /// <c>Halcyonic/Kit Figure Light</c>) driven by <see cref="MaterialPropertyBlock"/>.
    /// </summary>
    public sealed class KitFigure : MonoBehaviour
    {
        private static readonly int BaseId = Shader.PropertyToID("_Base");
        private static readonly int UnderId = Shader.PropertyToID("_Under");
        private static readonly int ToneId = Shader.PropertyToID("_Tone");
        private static readonly int TintId = Shader.PropertyToID("_Tint");
        private static readonly int GroundParamsId = Shader.PropertyToID("_GroundParams");

        private static Material? pearlMaterial, lightMaterial;

        private Transform lifted = null!, head = null!, pool = null!;
        private readonly Transform[] eyes = new Transform[2];
        private MeshFilter bodyFilter = null!, headFilter = null!, poolFilter = null!;
        private readonly MeshFilter[] eyeFilters = new MeshFilter[2];
        private MeshRenderer bodyDrawn = null!, headDrawn = null!, poolDrawn = null!;
        private readonly MeshRenderer[] eyeDrawn = new MeshRenderer[2];
        private MaterialPropertyBlock? pearlBlock, eyeBlock, poolBlock;
        private GlazeColor? identityToneOverride;

        public KitFigureLook? Look { get; private set; }

        public KitFigureFeature Feature { get; private set; }

        public KitFigureMeshes.Detail Detail { get; private set; }

        public float Height => (float)KitFigureLook.HeightM;

        public Transform Lifted => lifted;

        public Transform Head => head;

        public IReadOnlyList<Transform> Eyes => eyes;

        public Transform Pool => pool;

        public bool ShowPool => pool.gameObject.activeSelf;

        public IReadOnlyList<MeshRenderer> EyeRenderers => eyeDrawn;

        public MeshRenderer PoolRenderer => poolDrawn;

        public IEnumerable<MeshRenderer> Renderers
        {
            get
            {
                yield return bodyDrawn;
                yield return headDrawn;
                yield return eyeDrawn[0];
                yield return eyeDrawn[1];
                if (pool.gameObject.activeSelf) yield return poolDrawn;
            }
        }

        public int Triangles =>
            Count(bodyFilter) + Count(headFilter) + Count(eyeFilters[0]) + Count(eyeFilters[1]) + (pool.gameObject.activeSelf ? Count(poolFilter) : 0);

        private static int Count(MeshFilter filter) => filter.sharedMesh != null ? filter.sharedMesh.triangles.Length / 3 : 0;

        public static KitFigure Create(
            Transform parent,
            KitFigureFeature feature = KitFigureFeature.Antenna,
            KitFigureMeshes.Detail detail = KitFigureMeshes.Detail.Near,
            string name = "Character figure")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var figure = go.AddComponent<KitFigure>();
            figure.Build(feature, detail);
            figure.Show(KitFigureLook.Of(WorkState.NotStarted, false));
            return figure;
        }

        private void Build(KitFigureFeature feature, KitFigureMeshes.Detail detail)
        {
            Feature = feature;
            Detail = detail;

            pool = Part(transform, "Ground projection", KitFigureMeshes.Pool(), LightMaterial(), out poolFilter, out poolDrawn);
            pool.localPosition = new Vector3(0f, -KitFigureMeshes.PoolBelowM, 0f);

            lifted = new GameObject("Lifted").transform;
            lifted.SetParent(transform, false);

            Part(lifted, "Body", KitFigureMeshes.Body(detail), PearlMaterial(), out bodyFilter, out bodyDrawn);

            head = Part(lifted, "Head", KitFigureMeshes.Head(feature, detail), PearlMaterial(), out headFilter, out headDrawn);
            head.localPosition = new Vector3(0f, KitFigureMeshes.HeadY, 0f);

            for (var i = 0; i < 2; i++)
                eyes[i] = Part(head, i == 0 ? "Left eye" : "Right eye", KitFigureMeshes.Eye(false), LightMaterial(), out eyeFilters[i], out eyeDrawn[i]);
        }

        private static Transform Part(Transform parent, string name, Mesh mesh, Material material, out MeshFilter filter, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go.transform;
        }

        public static GlazeColor IdleBeltTone => GlazeColor.Hex(0xB9C0C8);

        public static GlazeColor StoppedBeltTone => GlazeColor.Hex(0x7A828E);

        public static GlazeColor ActiveToneFor(KitFigureLook look, GlazeColor? identityOverride = null)
        {
            if (identityOverride is GlazeColor over) return over;
            if (!look.Moves) return look.Eyes;
            if (look.Pool is GlazeColor pool) return pool;
            return look.Lids >= 0.90 ? StoppedBeltTone : IdleBeltTone;
        }

        /// <summary>Overrides the figure's accent tone with its family identity hue for the 6-character family lineup.</summary>
        public void SetIdentityToneOverride(GlazeColor? tone)
        {
            identityToneOverride = tone;
            if (Look is KitFigureLook current) Show(current);
        }

        public void Show(KitFigureLook look, double blink = 0, double breath = 1)
        {
            Look = look;
            var isStopped = look.Moves && look.Lids >= 0.90;
            var isUnknown = look.Moves && look.RightEyeShorter > 0.01;
            var isIdle = look.Moves && !look.Pool.HasValue && !isStopped && !isUnknown;

            var riseY = isStopped ? -0.012f : (float)look.RiseM;
            lifted.localPosition = new Vector3(0f, riseY, 0f);
            lifted.localRotation = Quaternion.identity;
            head.localPosition = new Vector3(0f, KitFigureMeshes.HeadY, 0f);
            var headPitch = !look.Moves ? (float)look.HeadPitchDegrees * 0.35f
                : isStopped ? 10.3f
                : (float)look.HeadPitchDegrees * 0.75f;
            var headRoll = isUnknown ? 15.5f : (float)look.HeadRollDegrees;
            head.localRotation = Quaternion.Euler(headPitch, 0f, headRoll);

            var familyHue = identityToneOverride ?? SpatialKit.FigureFamily(Feature);
            var activeTone = ActiveToneFor(look, identityToneOverride);
            var eyeTone = identityToneOverride ?? look.Eyes;

            var baseColour = BodyColour(look);
            var under = Dim(SpatialKit.GroundLight, look);
            var glowScale = !look.Moves ? 0.18f : isStopped ? 0.25f : (isIdle || isUnknown) ? 0.80f : 1f;
            var dashSign = isUnknown ? -1f : 1f;
            var sheenShare = (look.Pool.HasValue || identityToneOverride.HasValue) ? 0.11f : 0.04f;

            pearlBlock ??= new MaterialPropertyBlock();
            pearlBlock.SetVector(BaseId, Srgb(baseColour, (float)look.Lightness * 2f));
            pearlBlock.SetVector(UnderId, Srgb(under, glowScale));
            pearlBlock.SetVector(ToneId, Srgb(activeTone, dashSign));
            pearlBlock.SetVector(TintId, Srgb(familyHue, sheenShare));
            bodyDrawn.SetPropertyBlock(pearlBlock);
            headDrawn.SetPropertyBlock(pearlBlock);

            var eyeMesh = KitFigureMeshes.Eye(look.Happy);
            var lids = Mathf.Clamp01(Mathf.Max((float)look.Lids, (float)blink));
            var open = Mathf.Max(0.08f, 1f - lids);
            for (var i = 0; i < 2; i++)
            {
                var side = i == 0 ? -1f : 1f;
                var shorter = i == 1 ? 1f - (float)look.RightEyeShorter : 1f;
                var height = KitFigureMeshes.EyeHeightM * open * shorter;
                var drop = (KitFigureMeshes.EyeHeightM - height) * 0.5f;
                var baseTilt = look.Happy ? 0f : -side * KitFigureMeshes.EyeTiltDegrees;
                var roll = baseTilt + (i == 1 ? (float)look.RightEyeRollDegrees : 0f);
                eyeFilters[i].sharedMesh = eyeMesh;
                eyes[i].localPosition = new Vector3(side * KitFigureMeshes.EyeAcrossM, KitFigureMeshes.EyeUpM - drop, KitFigureMeshes.EyeForwardM);
                eyes[i].localRotation = Quaternion.Euler(0f, 0f, roll);
                eyes[i].localScale = new Vector3(KitFigureMeshes.EyeWidthM, height, 1f);
            }

            eyeBlock ??= new MaterialPropertyBlock();
            eyeBlock.SetVector(ToneId, Srgb(eyeTone, (float)look.Glow));
            eyeBlock.SetVector(GroundParamsId, new Vector4(1f, 0.52f, 1f, look.Happy ? 0f : 1f));
            eyeDrawn[0].SetPropertyBlock(eyeBlock);
            eyeDrawn[1].SetPropertyBlock(eyeBlock);

            // The 3-layer spatial ground projection is shown for all live states and hidden in Last Known (!look.Moves).
            var showGround = look.Moves;
            pool.gameObject.SetActive(showGround);
            if (showGround)
            {
                var poolColour = activeTone;
                var baseLevel = isStopped ? 0.22f
                    : isUnknown ? 0.48f
                    : isIdle ? 0.52f
                    : (look.PoolLevelShown < 0.45 ? 0.68f : 0.92f);
                var level = Mathf.Clamp01(baseLevel * (float)breath);
                var aoScale = Mathf.Clamp(1f + riseY * 18f, 0.86f, 1.16f);
                var aoAlpha = Mathf.Clamp(0.46f - riseY * 12f, 0.30f, 0.56f);
                var ringPulse = look.FloatSpeed > 1.2 ? 1.08f : (float)breath;

                poolBlock ??= new MaterialPropertyBlock();
                poolBlock.SetVector(ToneId, Srgb(poolColour, level));
                poolBlock.SetVector(GroundParamsId, new Vector4(aoScale, aoAlpha, ringPulse, 1f));
                poolDrawn.SetPropertyBlock(poolBlock);
            }
        }

        public static GlazeColor BodyColour(KitFigureLook look) => Dim(SpatialKit.Pearl, look);

        private static GlazeColor Dim(GlazeColor colour, KitFigureLook look)
        {
            var scale = look.Lightness;
            var grey = (colour.R * 0.2126 + colour.G * 0.7152 + colour.B * 0.0722) * scale;
            var sat = look.Saturation;
            byte Channel(byte c) => (byte)Math.Clamp(Math.Round(grey + (c * scale - grey) * sat), 0, 255);
            return new GlazeColor(Channel(colour.R), Channel(colour.G), Channel(colour.B));
        }

        private static Vector4 Srgb(GlazeColor colour, float w) => new Vector4(colour.R / 255f, colour.G / 255f, colour.B / 255f, w);

        public static Material PearlMaterial()
        {
            if (pearlMaterial != null) return pearlMaterial;
            pearlMaterial = Resources.Load<Material>("HalcyonicUI/KitPearl");
            if (pearlMaterial == null)
            {
                var shader = Shader.Find("Halcyonic/Kit Pearl");
                if (shader == null) throw new InvalidOperationException("Halcyonic/Kit Pearl shader is missing.");
                pearlMaterial = new Material(shader) { name = "KitPearl (fallback)" };
            }
            pearlMaterial.enableInstancing = true;
            return pearlMaterial;
        }

        public static Material LightMaterial()
        {
            if (lightMaterial != null) return lightMaterial;
            lightMaterial = Resources.Load<Material>("HalcyonicUI/KitFigureLight");
            if (lightMaterial == null)
            {
                var shader = Shader.Find("Halcyonic/Kit Figure Light");
                if (shader == null) throw new InvalidOperationException("Halcyonic/Kit Figure Light shader is missing.");
                lightMaterial = new Material(shader) { name = "KitFigureLight (fallback)" };
            }
            lightMaterial.enableInstancing = true;
            return lightMaterial;
        }
    }
}
