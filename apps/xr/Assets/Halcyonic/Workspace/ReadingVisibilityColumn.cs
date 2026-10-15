#nullable enable
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>Explicit local-only synthetic comparison, on the normal workspace camera path.
    /// No command, model, readback, room capture, saved preference or camera/layer mutation.</summary>
    public sealed class ReadingVisibilityColumn : IMenuColumn
    {
        private readonly MenuFrame frame = new MenuFrame("Reading visibility check", new Footer(
            new Prompt(Footer.Close, "Return to workspace", GlazeIcon.Back, PromptKind.Close)), lines: new[] {
                new PageLine("1 · Material", key: "reading-face"),
                new PageLine("2 · Opaque", key: "reading-opaque"),
                new PageLine("3 · Translucent", key: "reading-room"),
            }).Focus();
        private readonly Shader opaqueShader, maskShader;
        private GameObject? patches;
        private Material? opaque, mask;
        private Mesh? mesh;
        private MenuFrame? drawn;
        private (bool AppFocus, bool Suspended, int EyeWidth, int EyeHeight)? previousMetadata;
        public float PatchHeight { get; private set; }
        public ReadingVisibilityColumn(Shader opaqueShader, Shader maskShader)
        {
            this.opaqueShader = opaqueShader != null ? opaqueShader : throw new ArgumentNullException(nameof(opaqueShader));
            this.maskShader = maskShader != null ? maskShader : throw new ArgumentNullException(nameof(maskShader));
        }
        public event Action? Changed { add { } remove { } }
        public event Action? Closed;
        public MenuFrame Frame => frame;
        public void Drawn(MenuFrame shown, Footer? sideFooter) { if (shown == frame && sideFooter == null) drawn = shown; }
        public void Act(string id, string? key) { if (drawn == frame && id == Footer.Close) Closed?.Invoke(); }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string words) { }
        public void Said(string words) { }
        public void Tick()
        {
            var metadata = (Application.isFocused, FocusGuard.InputSuspended, UnityEngine.XR.XRSettings.eyeTextureWidth, UnityEngine.XR.XRSettings.eyeTextureHeight);
            if (previousMetadata == metadata) return;
            previousMetadata = metadata;
            Debug.Log("Halcyonic: reading visibility comparison: frame=" + Time.frameCount
                + ", app-focus=" + Application.isFocused + ", input-suspended=" + FocusGuard.InputSuspended
                + ", eye-width=" + UnityEngine.XR.XRSettings.eyeTextureWidth + ", eye-height=" + UnityEngine.XR.XRSettings.eyeTextureHeight
                + "; synthetic patches 1=production face, 2=independent opaque, 3=alpha mask .15; no eye-buffer alpha measured.");
        }
        public void Paint(MenuPlane plane)
        {
            var view = plane.Shown.FirstOrDefault(item => item.View.Frame == frame).View;
            if (FocusGuard.InputSuspended || view == null || plane.Opening(plane.Shown.First(item => item.View == view).Kind)) { HidePatches(); return; }
            if (patches != null) return;
            patches = new GameObject("Synthetic reading comparison patches"); patches.transform.SetParent(view.transform, false);
            opaque = new Material(opaqueShader) { hideFlags = HideFlags.DontSave };
            mask = new Material(maskShader) { hideFlags = HideFlags.DontSave, renderQueue = 5000 };
            mesh = new Mesh { name = "Diagnostic unit quad", vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(-.5f,.5f,0), new Vector3(.5f,.5f,0), new Vector3(.5f,-.5f,0) }, triangles = new[] { 0,1,2,0,2,3 } };
            foreach (var row in view.ShownLines)
            {
                if (row.Model.Key != "reading-face" && row.Model.Key != "reading-opaque" && row.Model.Key != "reading-room") continue;
                var anchor = new GameObject(row.Model.Key); anchor.transform.SetParent(patches.transform, false);
                var lineHeight = GlazeText.LineHeight(row.Words);
                PatchHeight = lineHeight * .7f;
                // All three controls occupy the same right-aligned measured part of their own row.
                // Text remains at the left; nothing is placed in an unallocated blank second line.
                anchor.transform.SetPositionAndRotation(row.Words.transform.position
                    + row.Words.transform.right * (row.Words.rectTransform.rect.width - .16f - MenuPage.Grid) * row.Words.transform.lossyScale.x
                    - row.Words.transform.up * lineHeight * row.Words.transform.lossyScale.y / 2f, row.Words.transform.rotation);
                anchor.transform.localScale = Vector3.one * (row.Words.transform.lossyScale.x / patches.transform.lossyScale.x);
                if (row.Model.Key == "reading-face")
                {
                    var face = Surface.Create(anchor.transform, "Production opaque witness", 35);
                    face.DrawCoatedInstrument(new Vector2(.16f, PatchHeight), .002f, GlazeTokens.ColorOf(Glaze.Panel));
                    face.transform.localPosition = new Vector3(.08f, 0f, -.003f);
                }
                else if (row.Model.Key == "reading-opaque")
                {
                    Quad(anchor.transform, "Opaque black", .04f, Color.black, opaque);
                    Quad(anchor.transform, "Opaque white", .12f, Color.white, opaque);
                }
                else Quad(anchor.transform, "Known .15 alpha mask", .08f, new Color(0, 0, 0, .15f), mask, .16f);
            }
            Tick();
        }
        private void Quad(Transform parent, string name, float x, Color colour, Material material, float width = .08f)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(x, 0, -.003f); go.transform.localScale = new Vector3(width, PatchHeight, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.sortingOrder = 35;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
            var properties = new MaterialPropertyBlock(); properties.SetColor("_Color", colour.linear); renderer.SetPropertyBlock(properties);
        }
        private static void Release(UnityEngine.Object value) { if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
        private void HidePatches()
        {
            if (patches != null) { patches.SetActive(false); Release(patches); patches = null; }
            if (opaque != null) { Release(opaque); opaque = null; }
            if (mask != null) { Release(mask); mask = null; }
            if (mesh != null) { Release(mesh); mesh = null; }
        }
        public void FocusLeft() { drawn = null; HidePatches(); previousMetadata = null; }
    }
}
#endif
