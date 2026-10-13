#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class StageRender
    {
        /// <summary>Current optional one-agent shelf: all identities, animation, contact and exact stillness, at both text sizes.</summary>
        public static void CharacterLifeCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "CharacterLifeRenders", variant));
                Directory.CreateDirectory(folder);
                var result = new List<string>();
                var previousField = ViewField.Current;
                try
                {
                    ViewField.Current = FieldChecks.Quest3S;
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    result.AddRange(CharacterProjectionEdges());
                    result.AddRange(CharacterShelfCases(folder, features: true));
                    result.AddRange(CharacterBlinks(folder));
                }
                catch (Exception error) { result.Add(error.ToString()); }
                finally { ViewField.Current = previousField; WorkspaceRender.KeepFontAssetsAsCommitted(); }
                foreach (var failure in result) Debug.LogError("Halcyonic: character life: " + failure);
                if (result.Count == 0) Debug.Log("Halcyonic: character life: every check passed; " + folder);
                return result;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>Same-frame isolation of the captured idle residuals, before any production stillness repair.</summary>
        public static void CharacterStillConvergenceCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "CharacterStillConvergenceRenders", variant));
                Directory.CreateDirectory(folder);
                var result = new List<string>();
                var previousField = ViewField.Current;
                var wasStill = GlazeMotion.Still;
                try
                {
                    ViewField.Current = FieldChecks.Quest3S;
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    foreach (var id in new[] { "feature-Round-Ears-39", "feature-Blossom-Ears-9", "feature-Squircle-Ears-78" })
                    {
                        GlazeMotion.Still = false;
                        var name = CharacterIdentity.Of(id).Shape + "-Ears";
                        var root = new GameObject("Captured idle residual witness " + name);
                        var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                        var captured = new List<Texture2D>();
                        try
                        {
                            var eyes = new Vector3(0f, EyeHeight, 0f);
                            var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                            var plane = CharacterConsole(root.transform, eyes);
                            var character = WorkspaceRender.ConfirmedPlaneAgent(root.transform, plane, eyes, Shown(id, CharacterActivity.Idle), result, name);
                            var view = character.View;
                            result.AddRange(CharacterEnvelopes(name, eyes, new List<(CharacterView View, CharacterTarget Target)> { character }));
                            view.Show(Shown(id, CharacterActivity.Idle)); view.PlaceShelfLabel(eyes); Advance(view, 1f);
                            WorkspaceRender.ForceMeshes(root);
                            GlazeMotion.Still = true;
                            // Match the original production fixture's exact frame count and step.
                            for (var frame = 0; frame < 216; frame++) view.AdvanceForRender(1f / 72f);
                            var first = ResidualMoment("before");
                            for (var frame = 0; frame < 72; frame++) view.AdvanceForRender(1f / 72f);
                            var later = ResidualMoment("after");
                            try
                            {
                                var original = ExactCharacterPixels(first.Original, later.Original);
                                var corrected = ExactCharacterPixels(first.Corrected, later.Corrected);
                                if (corrected != 0) result.Add(name + ": exact idle residual correction leaves " + corrected + " drifting pixels.");
                                if (variant == "Larger" && original == 0)
                                    result.Add(name + ": original captured idle residuals no longer reproduce the larger-text pixel drift; causal witness is inconclusive.");
                                var accounted = 0;
                                var originalBefore = first.Original.GetPixels32(); var originalAfter = later.Original.GetPixels32();
                                var correctedBefore = first.Corrected.GetPixels32(); var correctedAfter = later.Corrected.GetPixels32();
                                for (var pixel = 0; pixel < originalBefore.Length; pixel++)
                                {
                                    if (SameCharacterPixel(originalBefore[pixel], originalAfter[pixel])) continue;
                                    if (!SameCharacterPixel(correctedBefore[pixel], originalBefore[pixel])
                                        || !SameCharacterPixel(correctedAfter[pixel], originalAfter[pixel])) accounted++;
                                }
                                if (accounted != original) result.Add(name + ": the isolated correction does not account for every original drifting pixel.");
                                Debug.Log("Halcyonic: " + name + ": residual isolation original " + original + ", corrected "
                                    + corrected + ", accounted " + accounted + " pixels.");
                            }
                            finally
                            {
                                UnityEngine.Object.DestroyImmediate(first.Original); UnityEngine.Object.DestroyImmediate(first.Corrected);
                                UnityEngine.Object.DestroyImmediate(later.Original); UnityEngine.Object.DestroyImmediate(later.Corrected);
                            }

                            (Texture2D Original, Texture2D Corrected) ResidualMoment(string moment)
                            {
                                var originalRotation = view.Body.localRotation;
                                var renderers = new[] { view.Body.Find("Shell").GetComponent<Renderer>(), view.FeatureForRender.Renderer! };
                                var originals = renderers.Select(renderer =>
                                {
                                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block;
                                }).ToArray();
                                var original = WorkspaceRender.Render(camera, texture); captured.Add(original);
                                var repeated = WorkspaceRender.Render(camera, texture);
                                if (ExactCharacterPixels(original, repeated) != 0)
                                    result.Add(name + ": repeated unchanged " + moment + " frame changes pixels, so residual isolation is inconclusive.");
                                UnityEngine.Object.DestroyImmediate(repeated);
                                Texture2D? corrected = null;
                                try
                                {
                                    view.Body.localRotation = Quaternion.identity;
                                    foreach (var renderer in renderers)
                                    {
                                        var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                                        block.SetFloat("_Fog", 0f); block.SetFloat("_EyeUneven", 0f); block.SetVector("_Look", Vector4.zero);
                                        renderer.SetPropertyBlock(block);
                                    }
                                    corrected = WorkspaceRender.Render(camera, texture); captured.Add(corrected);
                                    File.WriteAllBytes(Path.Combine(folder, name + "-" + moment + "-original.png"), original.EncodeToPNG());
                                    File.WriteAllBytes(Path.Combine(folder, name + "-" + moment + "-corrected.png"), corrected.EncodeToPNG());
                                }
                                finally
                                {
                                    view.Body.localRotation = originalRotation;
                                    for (var index = 0; index < renderers.Length; index++) renderers[index].SetPropertyBlock(originals[index]);
                                    var restored = WorkspaceRender.Render(camera, texture);
                                    if (ExactCharacterPixels(original, restored) != 0)
                                        result.Add(name + ": restoring the captured " + moment + " parameters fails to restore exact pixels.");
                                    UnityEngine.Object.DestroyImmediate(restored);
                                }
                                return (original, corrected!);
                            }
                        }
                        finally
                        {
                            foreach (var image in captured) if (image != null) UnityEngine.Object.DestroyImmediate(image);
                            UnityEngine.Object.DestroyImmediate(root); texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
                        }
                    }
                }
                catch (Exception error) { result.Add(error.ToString()); }
                finally { ViewField.Current = previousField; GlazeMotion.Still = wasStill; WorkspaceRender.KeepFontAssetsAsCommitted(); }
                foreach (var failure in result) Debug.LogError("Halcyonic: character residuals: " + failure);
                return result;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        /// <summary>Unchanged scene captures isolate target/depth paths and feature shader determinism, without animation or parameter correction.</summary>
        public static void CharacterRenderDeterminismCheck() => CheckCharacterRenderDeterminism(composite: false);

        /// <summary>Actual composed-layer visibility and explicit sorting controls, after isolated feature rendering proved stable.</summary>
        public static void CharacterCompositeDeterminismCheck() => CheckCharacterRenderDeterminism(composite: true);

        /// <summary>The newly failing fixed ghost poses and original Clover/Horns held pose, without changing their parameters.</summary>
        public static void CharacterProblemCompositionCheck() => CheckCharacterRenderDeterminism(composite: true, problematic: true);

        /// <summary>Private exact-source material/shader controls for the now-isolated pair interaction.</summary>
        public static void CharacterPairStateCheck() => CheckCharacterRenderDeterminism(composite: true, problematic: true, privatePairStates: true);

        private static void CheckCharacterRenderDeterminism(bool composite, bool problematic = false, bool privatePairStates = false)
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", privatePairStates ? "CharacterPairStateRenders" : problematic ? "CharacterProblemComposition" : composite ? "CharacterCompositeDeterminism" : "CharacterRenderDeterminism", variant));
                Directory.CreateDirectory(folder);
                var result = new List<string>();
                var previousField = ViewField.Current; var wasStill = GlazeMotion.Still;
                try
                {
                    ViewField.Current = FieldChecks.Quest3S;
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    var crest = Enumerable.Range(0, 10000).Select(n => "feature-Round-Crest-" + n.ToString(CultureInfo.InvariantCulture))
                        .First(id => CharacterIdentity.Of(id).Shape == BodyShape.Round && CharacterIdentity.Of(id).Feature == CharacterFeature.Crest);
                    var cases = problematic
                        ? new[] { (Id: "feature-Round-Ears-39", Activity: CharacterActivity.Idle, Stale: true),
                            (Id: "feature-Round-Ears-39", Activity: CharacterActivity.Starting, Stale: true),
                            (Id: crest, Activity: CharacterActivity.Idle, Stale: true), (Id: crest, Activity: CharacterActivity.Starting, Stale: true),
                            (Id: "feature-Clover-Horns-39", Activity: CharacterActivity.Idle, Stale: false) }
                        : new[] { (Id: "feature-Round-Ears-39", Activity: CharacterActivity.Idle, Stale: false),
                            (Id: "feature-Blossom-Ears-9", Activity: CharacterActivity.Idle, Stale: false),
                            (Id: "feature-Squircle-Ears-78", Activity: CharacterActivity.Idle, Stale: false) };
                    foreach (var sample in cases)
                    {
                        var id = sample.Id;
                        GlazeMotion.Still = false;
                        var identity = CharacterIdentity.Of(id);
                        var name = identity.Shape + "-" + identity.Feature + (problematic ? "-" + sample.Activity + (sample.Stale ? "-last-known" : "-live") : "");
                        var root = new GameObject("Unchanged render witness " + name);
                        var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                        Material? unlit = null;
                        try
                        {
                            var eyes = new Vector3(0f, EyeHeight, 0f);
                            var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                            var plane = CharacterConsole(root.transform, eyes);
                            var character = WorkspaceRender.ConfirmedPlaneAgent(root.transform, plane, eyes, Shown(id, CharacterActivity.Idle), result, name);
                            var view = character.View;
                            if (!problematic || !sample.Stale)
                            {
                                result.AddRange(CharacterEnvelopes(name, eyes, new List<(CharacterView View, CharacterTarget Target)> { character }));
                                view.Show(Shown(id, CharacterActivity.Idle)); view.PlaceShelfLabel(eyes); Advance(view, 1f);
                                WorkspaceRender.ForceMeshes(root); GlazeMotion.Still = true;
                                for (var frame = 0; frame < 216; frame++) view.AdvanceForRender(1f / 72f);
                            }
                            else
                            {
                                // Reproduce the actual composition gate's ordered live/paused state history.
                                foreach (CharacterActivity activity in Enum.GetValues(typeof(CharacterActivity)))
                                {
                                    foreach (var stale in new[] { false, true })
                                    {
                                        view.Show(Shown(id, activity, stale)); view.PlaceShelfLabel(eyes); Advance(view, 1f);
                                        WorkspaceRender.ForceMeshes(root);
                                    }
                                    if (activity == sample.Activity) break;
                                }
                            }
                            var feature = view.FeatureForRender.Renderer!;
                            var shell = view.Body.Find("Shell").GetComponent<Renderer>();
                            var renderers = root.GetComponentsInChildren<Renderer>(true);
                            var visibility = renderers.Select(renderer => renderer.enabled).ToArray();
                            if (privatePairStates)
                            {
                                var originalMaterial = feature.sharedMaterial;
                                var privateMaterials = new List<Material>(); var privateShaders = new List<Shader>();
                                var source = File.ReadAllText(Path.Combine(Application.dataPath, "Halcyonic", "Characters", "Shaders", "CharacterBody.shader"));
                                try
                                {
                                    Series("baseline-production", freshDepth: null);
                                    var copy = new Material(originalMaterial) { hideFlags = HideFlags.HideAndDontSave };
                                    privateMaterials.Add(copy); feature.sharedMaterial = copy;
                                    Series("original-shader-material-copy", freshDepth: null);
                                    ShaderVariant("exact-source-copy", text => text);
                                    copy.renderQueue = originalMaterial.renderQueue - 1; feature.sharedMaterial = copy;
                                    Series("distinct-earlier-queue", freshDepth: null);
                                    ShaderVariant("legacy-feature-zwrite-on", text => ReplacePairState(text, "ZWrite [_DepthWrite]", "ZWrite On"));
                                    ShaderVariant("feature-zwrite-off", text => ReplacePairState(text, "ZWrite [_DepthWrite]", "ZWrite Off"));
                                    ShaderVariant("feature-depth-bias", text => ReplacePairState(text, "ZWrite [_DepthWrite]", "ZWrite On\n            Offset 0, -1"));
                                    ShaderVariant("disable-batching", text => ReplacePairState(text,
                                        "\"ForceNoShadowCasting\" = \"True\"", "\"ForceNoShadowCasting\" = \"True\" \"DisableBatching\" = \"True\""));
                                }
                                finally
                                {
                                    feature.sharedMaterial = originalMaterial;
                                    foreach (var material in privateMaterials) UnityEngine.Object.DestroyImmediate(material);
                                    foreach (var shader in privateShaders) UnityEngine.Object.DestroyImmediate(shader);
                                }
                                Series("restored-baseline-production", freshDepth: null);

                                void ShaderVariant(string path, Func<string, string> state)
                                {
                                    // Name-only copy is an explicit compiler/identity control for every named-state variant.
                                    var shaderName = "Hidden/Halcyonic/Pair " + path + " " + name + " " + variant;
                                    var exact = ReplacePairState(source, "Shader \"Halcyonic/Character Body\"", "Shader \"" + shaderName + "\"");
                                    var modified = state(exact);
                                    File.WriteAllText(Path.Combine(folder, name + "-" + path + "-shader.txt"), modified);
                                    var shader = ShaderUtil.CreateShaderAsset(modified);
                                    shader.hideFlags = HideFlags.HideAndDontSave; privateShaders.Add(shader);
                                    if (ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Private character shader " + path + " failed compilation.");
                                    var material = new Material(originalMaterial) { shader = shader, hideFlags = HideFlags.HideAndDontSave };
                                    privateMaterials.Add(material); feature.sharedMaterial = material;
                                    Series(path, freshDepth: null);
                                }
                            }
                            else if (problematic)
                            {
                                var originalMaterial = feature.sharedMaterial;
                                Texture2D? beforeMoment = null;
                                try
                                {
                                    beforeMoment = WorkspaceRender.Render(camera, texture);
                                    Series("full-current", freshDepth: null);
                                    ProblemComposition("shell-feature-only", renderer => renderer == shell || renderer == feature);
                                    ProblemComposition("shell-only", renderer => renderer == shell);
                                    ProblemComposition("feature-only-body-shader", renderer => renderer == feature);
                                    ProblemComposition("feature-noncharacter", renderer => renderer == feature || !renderer.transform.IsChildOf(view.transform));
                                    ProblemComposition("feature-only-before-unlit", renderer => renderer == feature);
                                    var shader = Shader.Find("Unlit/Color");
                                    if (shader == null) result.Add(name + ": built-in Unlit/Color diagnostic shader is unavailable.");
                                    else
                                    {
                                        unlit = new Material(shader) { color = view.FeatureForRender.Colour, renderQueue = originalMaterial.renderQueue };
                                        feature.sharedMaterial = unlit;
                                        Series("feature-only-unlit", freshDepth: null);
                                    }
                                }
                                finally
                                {
                                    feature.sharedMaterial = originalMaterial;
                                    for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = visibility[index];
                                }
                                try
                                {
                                    if (!sample.Stale)
                                    {
                                        for (var frame = 0; frame < 72; frame++) view.AdvanceForRender(1f / 72f);
                                        var afterMoment = WorkspaceRender.Render(camera, texture);
                                        try
                                        {
                                            File.WriteAllBytes(Path.Combine(folder, name + "-216-frames.png"), beforeMoment!.EncodeToPNG());
                                            File.WriteAllBytes(Path.Combine(folder, name + "-288-frames.png"), afterMoment.EncodeToPNG());
                                            Debug.Log("Halcyonic: unchanged character render: " + name + ": captured 216→288 difference "
                                                + ExactCharacterPixels(beforeMoment!, afterMoment) + " exact RGBA pixels.");
                                            SaveCharacterPixelDifferences(Path.Combine(folder, name + "-216-to-288-pixels.txt"), beforeMoment!, afterMoment);
                                        }
                                        finally { UnityEngine.Object.DestroyImmediate(afterMoment); }
                                        Series("full-current-after-288", freshDepth: null);
                                    }
                                    else Series("full-restored-current", freshDepth: null);
                                }
                                finally { if (beforeMoment != null) UnityEngine.Object.DestroyImmediate(beforeMoment); }
                                void ProblemComposition(string path, Func<Renderer, bool> include)
                                {
                                    for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = visibility[index] && include(renderers[index]);
                                    Series(path, freshDepth: null);
                                }
                            }
                            else if (composite)
                            {
                                var featureOrder = feature.sortingOrder; var shellOrder = shell.sortingOrder;
                                var originalMaterial = feature.sharedMaterial;
                                var legacyMaterial = new Material(originalMaterial) { hideFlags = HideFlags.HideAndDontSave };
                                legacyMaterial.SetFloat("_DepthWrite", 1f);
                                try
                                {
                                    Composition("shell-feature-only", renderer => renderer == shell || renderer == feature);
                                    Composition("character-all-parts", renderer => renderer.transform.IsChildOf(view.transform));
                                    Composition("feature-noncharacter", renderer => renderer == feature || !renderer.transform.IsChildOf(view.transform));
                                    Composition("full-minus-shell", renderer => renderer != shell);
                                    Composition("shell-only", renderer => renderer == shell);
                                    for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = visibility[index];
                                    shell.sortingOrder = 0; feature.sortingOrder = 0; feature.sharedMaterial = legacyMaterial;
                                    Series("full-legacy-depth-writing-equal-order", freshDepth: null);
                                    feature.sharedMaterial = originalMaterial;
                                    shell.sortingOrder = 0; feature.sortingOrder = 1;
                                    Series("full-feature-after-shell", freshDepth: null);
                                    feature.sortingOrder = -1;
                                    Series("full-feature-before-shell", freshDepth: null);
                                }
                                finally
                                {
                                    shell.sortingOrder = shellOrder; feature.sortingOrder = featureOrder;
                                    feature.sharedMaterial = originalMaterial; UnityEngine.Object.DestroyImmediate(legacyMaterial);
                                    for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = visibility[index];
                                }
                                Series("full-restored-original-order", freshDepth: null);
                                void Composition(string path, Func<Renderer, bool> include)
                                {
                                    for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = visibility[index] && include(renderers[index]);
                                    Series(path, freshDepth: null);
                                }
                            }
                            else
                            {
                                var enabled = camera.enabled; var msaa = camera.allowMSAA;
                                Series("reused-current", freshDepth: null);
                                try { camera.enabled = false; Series("disabled-camera", freshDepth: null); }
                                finally { camera.enabled = enabled; }
                                try { camera.allowMSAA = false; Series("no-camera-msaa", freshDepth: null); }
                                finally { camera.allowMSAA = msaa; }
                                Series("fresh-depth24", freshDepth: 24);
                                Series("fresh-depth32", freshDepth: 32);
                                var originalMaterial = feature.sharedMaterial;
                                try
                                {
                                    foreach (var renderer in renderers) renderer.enabled = renderer == feature;
                                    Series("feature-only-body-shader", freshDepth: null);
                                    var shader = Shader.Find("Unlit/Color");
                                    if (shader == null) result.Add(name + ": built-in Unlit/Color diagnostic shader is unavailable.");
                                    else
                                    {
                                        unlit = new Material(shader) { color = view.FeatureForRender.Colour, renderQueue = originalMaterial.renderQueue };
                                        feature.sharedMaterial = unlit;
                                        Series("feature-only-unlit", freshDepth: null);
                                    }
                                }
                                finally
                                {
                                    feature.sharedMaterial = originalMaterial;
                                    for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = visibility[index];
                                }
                            }

                            void Series(string path, int? freshDepth)
                            {
                                var prefix = name + "-" + path;
                                var records = new List<string> { CharacterBatchingSetting() };
                                Texture2D? previous = null;
                                var changes = 0;
                                try
                                {
                                    for (var frame = 0; frame < 8; frame++)
                                    {
                                        var currentTarget = texture;
                                        if (freshDepth.HasValue)
                                        {
                                            currentTarget = new RenderTexture(Size, Size, freshDepth.Value, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                                            currentTarget.Create(); camera.targetTexture = currentTarget;
                                        }
                                        Texture2D? image = null;
                                        try
                                        {
                                            var parameters = CharacterFrameValues(new[] { view });
                                            records.Add("frame " + frame + " enabled=" + camera.enabled + " allowMSAA=" + camera.allowMSAA
                                                + " qualityAA=" + QualitySettings.antiAliasing + " requestedAA=" + currentTarget.antiAliasing
                                                + " actualSamples=" + currentTarget.descriptor.msaaSamples + " depth=" + currentTarget.depth
                                                + " depthFormat=" + currentTarget.descriptor.depthStencilFormat + " format=" + currentTarget.graphicsFormat);
                                            records.Add("view=" + camera.worldToCameraMatrix.ToString("R") + " projection=" + camera.projectionMatrix.ToString("R")
                                                + " nonJittered=" + camera.nonJitteredProjectionMatrix.ToString("R") + " gpuProjection=" + GL.GetGPUProjectionMatrix(camera.projectionMatrix, true).ToString("R"));
                                            records.Add("light=" + Shader.GetGlobalVector("_HalcyonicKeyLight").ToString("R")
                                                + " noise=" + Shader.GetGlobalTexture("_HalcyonicNoise")?.GetInstanceID());
                                            foreach (var renderer in view.GetComponentsInChildren<Renderer>(true))
                                                records.Add(renderer.name + " localToWorld=" + renderer.localToWorldMatrix.ToString("R")
                                                    + " worldToLocal=" + renderer.worldToLocalMatrix.ToString("R") + " bounds=" + renderer.bounds.ToString("R")
                                                    + " sortingLayer=" + renderer.sortingLayerID + " sortingOrder=" + renderer.sortingOrder
                                                    + " queue=" + renderer.sharedMaterial?.renderQueue + " materialId=" + renderer.sharedMaterial?.GetInstanceID()
                                                    + " shaderId=" + renderer.sharedMaterial?.shader?.GetInstanceID() + " shader=" + renderer.sharedMaterial?.shader?.name
                                                    + " staticBatch=" + renderer.isPartOfStaticBatch + " instancing=" + renderer.sharedMaterial?.enableInstancing
                                                    + " DisableBatching=" + renderer.sharedMaterial?.GetTag("DisableBatching", false, "not declared"));
                                            image = WorkspaceRender.Render(camera, currentTarget);
                                            File.WriteAllBytes(Path.Combine(folder, prefix + "-" + frame + ".png"), image.EncodeToPNG());
                                            var after = CharacterFrameValues(new[] { view });
                                            foreach (var value in parameters.Where(pair => !after.TryGetValue(pair.Key, out var current) || current != pair.Value))
                                                result.Add(prefix + ": unchanged capture altered production value " + value.Key);
                                            records.AddRange(parameters.Select(pair => pair.Key + "=" + pair.Value));
                                            if (previous != null)
                                            {
                                                var count = ExactCharacterPixels(previous, image);
                                                records.Add("exactRGBA changed=" + count);
                                                if (count != 0)
                                                {
                                                    changes += count;
                                                    var beforePixels = previous.GetPixels32(); var afterPixels = image.GetPixels32();
                                                    var pixels = new List<string>();
                                                    for (var pixel = 0; pixel < beforePixels.Length; pixel++)
                                                    {
                                                        if (SameCharacterPixel(beforePixels[pixel], afterPixels[pixel])) continue;
                                                        if (pixels.Count < 128) pixels.Add((pixel % Size) + "," + (pixel / Size) + ": "
                                                            + beforePixels[pixel] + " -> " + afterPixels[pixel]);
                                                    }
                                                    File.WriteAllLines(Path.Combine(folder, prefix + "-" + frame + "-pixels.txt"), pixels);
                                                }
                                                UnityEngine.Object.DestroyImmediate(previous);
                                            }
                                            previous = image; image = null;
                                        }
                                        finally
                                        {
                                            if (image != null) UnityEngine.Object.DestroyImmediate(image);
                                            camera.targetTexture = texture;
                                            if (freshDepth.HasValue) { currentTarget.Release(); UnityEngine.Object.DestroyImmediate(currentTarget); }
                                        }
                                    }
                                }
                                finally { if (previous != null) UnityEngine.Object.DestroyImmediate(previous); }
                                File.WriteAllLines(Path.Combine(folder, prefix + "-parameters.txt"), records);
                                if (privatePairStates)
                                {
                                    var junction = CloseUp(camera, texture, view.Body.position, out _);
                                    try { File.WriteAllBytes(Path.Combine(folder, prefix + "-junction.png"), junction.EncodeToPNG()); }
                                    finally { UnityEngine.Object.DestroyImmediate(junction); }
                                }
                                Debug.Log("Halcyonic: unchanged character render: " + prefix + ": " + changes + " exactRGBA differences across seven unchanged pairs.");
                                // This is a diagnostic, not an acceptance threshold or a replacement for KeepStill.
                            }
                        }
                        finally
                        {
                            if (unlit != null) UnityEngine.Object.DestroyImmediate(unlit);
                            UnityEngine.Object.DestroyImmediate(root); texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
                        }
                    }
                }
                catch (Exception error) { result.Add(error.ToString()); }
                finally { ViewField.Current = previousField; GlazeMotion.Still = wasStill; WorkspaceRender.KeepFontAssetsAsCommitted(); }
                foreach (var failure in result) Debug.LogError("Halcyonic: character render diagnostic: " + failure);
                return result;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static string ReplacePairState(string source, string before, string after)
        {
            var first = source.IndexOf(before, StringComparison.Ordinal);
            if (first < 0 || first != source.LastIndexOf(before, StringComparison.Ordinal))
                throw new InvalidOperationException("Private shader diagnostic requires exactly one named state anchor.");
            return source.Substring(0, first) + after + source.Substring(first + before.Length);
        }

        private static string CharacterBatchingSetting()
        {
            // Unity's own current C# reference declares this getter internal. Read only; never change project settings.
            var method = typeof(PlayerSettings).GetMethod("GetBatchingForPlatform", System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null,
                new[] { typeof(BuildTarget), typeof(int).MakeByRefType(), typeof(int).MakeByRefType() }, null);
            if (method == null) return "Configured batching getter unavailable; actual submitted batching is not established.";
            try
            {
                var arguments = new object[] { EditorUserBuildSettings.activeBuildTarget, 0, 0 };
                method.Invoke(null, arguments);
                return "configured target=" + arguments[0] + " staticBatching=" + arguments[1] + " dynamicBatching=" + arguments[2]
                    + "; this configuration does not establish that these MPB/mesh draws were batched.";
            }
            catch (Exception) { return "Configured batching getter could not be read; actual submitted batching is not established."; }
        }

        private static void SaveCharacterPixelDifferences(string path, Texture2D a, Texture2D b)
        {
            var first = a.GetPixels32(); var second = b.GetPixels32();
            var pixels = new List<string>(); var changed = 0;
            for (var pixel = 0; pixel < first.Length; pixel++)
            {
                if (SameCharacterPixel(first[pixel], second[pixel])) continue;
                changed++;
                if (pixels.Count < 128) pixels.Add((pixel % a.width) + "," + (pixel / a.width) + ": " + first[pixel] + " -> " + second[pixel]);
            }
            pixels.Insert(0, "Exact RGBA differences: " + changed + "; first 128 listed, coordinates start at bottom left.");
            File.WriteAllLines(path, pixels);
        }

        // The causal diagnostic is stricter than the historical 2/255 RGB comparison.
        private static bool SameCharacterPixel(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;

        private static int ExactCharacterPixels(Texture2D a, Texture2D b)
        {
            var first = a.GetPixels32(); var second = b.GetPixels32();
            if (a.width != b.width || a.height != b.height) throw new InvalidOperationException("Character pixel witnesses have different dimensions.");
            var changed = 0;
            for (var pixel = 0; pixel < first.Length; pixel++) if (!SameCharacterPixel(first[pixel], second[pixel])) changed++;
            return changed;
        }

        /// <summary>All six features in existing live/paused/ghosted states: unchanged composed pixels and visible layer evidence.</summary>
        public static void CharacterFeatureCompositionCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "CharacterFeatureComposition", variant));
                Directory.CreateDirectory(folder);
                var result = new List<string>();
                var previousField = ViewField.Current; var wasStill = GlazeMotion.Still;
                try
                {
                    ViewField.Current = FieldChecks.Quest3S;
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    result.AddRange(CharacterMaterialCacheLifecycle());
                    result.AddRange(CharacterRayDepthWitness());
                    foreach (CharacterFeature kind in Enum.GetValues(typeof(CharacterFeature)))
                    {
                        GlazeMotion.Still = false;
                        var id = Enumerable.Range(0, 10000).Select(n => "feature-Round-" + kind + "-" + n.ToString(CultureInfo.InvariantCulture))
                            .First(candidate => CharacterIdentity.Of(candidate).Shape == BodyShape.Round && CharacterIdentity.Of(candidate).Feature == kind);
                        var root = new GameObject("Actual feature composition " + kind);
                        var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                        try
                        {
                            var eyes = new Vector3(0f, EyeHeight, 0f);
                            var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                            var plane = CharacterConsole(root.transform, eyes);
                            var character = WorkspaceRender.ConfirmedPlaneAgent(root.transform, plane, eyes, Shown(id, CharacterActivity.Idle), result, kind.ToString());
                            var view = character.View;
                            var shell = view.Body.Find("Shell").GetComponent<Renderer>();
                            foreach (CharacterActivity activity in Enum.GetValues(typeof(CharacterActivity)))
                            foreach (var stale in new[] { false, true })
                            {
                                var name = kind + "-" + activity + (stale ? "-last-known" : "-live");
                                view.Show(Shown(id, activity, stale)); view.PlaceShelfLabel(eyes); Advance(view, 1f);
                                WorkspaceRender.ForceMeshes(root);
                                var feature = view.FeatureForRender.Renderer;
                                if (shell.sharedMaterial.GetFloat("_DepthWrite") != 1f) result.Add(name + ": shell stopped writing its required depth.");
                                if (feature != null && (feature.sortingOrder != shell.sortingOrder - 1 || feature.sharedMaterial.shader != shell.sharedMaterial.shader
                                    || feature.sharedMaterial.GetFloat("_DepthWrite") != 0f || feature.sharedMaterial == shell.sharedMaterial))
                                    result.Add(name + ": actual feature does not use the cached non-depth-writing body surface and earlier order.");
                                var beforeValues = CharacterFrameValues(new[] { view });
                                Texture2D? first = null;
                                try
                                {
                                    first = WorkspaceRender.Render(camera, texture);
                                    File.WriteAllBytes(Path.Combine(folder, name + ".png"), first.EncodeToPNG());
                                    for (var frame = 1; frame < 8; frame++)
                                    {
                                        var repeated = WorkspaceRender.Render(camera, texture);
                                        try
                                        {
                                            var changed = ExactCharacterPixels(first, repeated);
                                            if (changed != 0)
                                            {
                                                result.Add(name + ": unchanged composed frame " + frame + " changes " + changed + " exact RGBA pixels.");
                                                File.WriteAllBytes(Path.Combine(folder, name + "-changed-" + frame + ".png"), repeated.EncodeToPNG());
                                                SaveCharacterPixelDifferences(Path.Combine(folder, name + "-changed-" + frame + "-pixels.txt"), first, repeated);
                                            }
                                        }
                                        finally { UnityEngine.Object.DestroyImmediate(repeated); }
                                    }
                                    var afterValues = CharacterFrameValues(new[] { view });
                                    if (beforeValues.Any(pair => afterValues[pair.Key] != pair.Value)) result.Add(name + ": repeated captures changed actual character values.");
                                    result.AddRange(CharacterFeatureShows(name, camera, texture, view));
                                    if (feature != null && activity == CharacterActivity.Idle)
                                        result.AddRange(CharacterOpaqueOcclusion(name, folder, camera, texture, root, view));
                                    if (feature != null && activity == CharacterActivity.Verifying)
                                        result.AddRange(CharacterRingOcclusion(name, folder, camera, texture, root, view));
                                    if (feature != null)
                                    {
                                        // The same posed camera retains the shell's front, not merely a stable feature silhouette.
                                        shell.enabled = false;
                                        Texture2D? withoutShell = null;
                                        try
                                        {
                                            withoutShell = WorkspaceRender.Render(camera, texture);
                                            File.WriteAllBytes(Path.Combine(folder, name + "-without-shell.png"), withoutShell.EncodeToPNG());
                                            if (ExactCharacterPixels(first, withoutShell) == 0) result.Add(name + ": the visible feature completely obscures its shell.");
                                        }
                                        finally { shell.enabled = true; if (withoutShell != null) UnityEngine.Object.DestroyImmediate(withoutShell); }
                                        var originalOrder = feature.sortingOrder;
                                        var originalMaterial = feature.sharedMaterial;
                                        var legacyMaterial = new Material(originalMaterial) { hideFlags = HideFlags.HideAndDontSave };
                                        legacyMaterial.SetFloat("_DepthWrite", 1f);
                                        Texture2D? legacy = null;
                                        try
                                        {
                                            feature.sortingOrder = shell.sortingOrder; feature.sharedMaterial = legacyMaterial;
                                            legacy = WorkspaceRender.Render(camera, texture);
                                            File.WriteAllBytes(Path.Combine(folder, name + "-legacy-depth-writing-equal-order.png"), legacy.EncodeToPNG());
                                        }
                                        finally
                                        {
                                            feature.sortingOrder = originalOrder; feature.sharedMaterial = originalMaterial;
                                            UnityEngine.Object.DestroyImmediate(legacyMaterial);
                                            if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);
                                        }
                                    }
                                    if ((kind == CharacterFeature.Ears || kind == CharacterFeature.Antenna || kind == CharacterFeature.Sprout || kind == CharacterFeature.Horns)
                                        && activity == CharacterActivity.Idle)
                                    {
                                        var close = CloseUp(camera, texture, view.Body.position, out _);
                                        File.WriteAllBytes(Path.Combine(folder, name + "-junction.png"), close.EncodeToPNG());
                                        UnityEngine.Object.DestroyImmediate(close);
                                    }
                                    var restored = WorkspaceRender.Render(camera, texture);
                                    try { if (ExactCharacterPixels(first, restored) != 0) result.Add(name + ": restoring visibility/order does not restore exact composed pixels."); }
                                    finally { UnityEngine.Object.DestroyImmediate(restored); }
                                }
                                finally { if (first != null) UnityEngine.Object.DestroyImmediate(first); }
                            }
                        }
                        finally { UnityEngine.Object.DestroyImmediate(root); texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
                    }
                }
                catch (Exception error) { result.Add(error.ToString()); }
                finally { ViewField.Current = previousField; GlazeMotion.Still = wasStill; WorkspaceRender.KeepFontAssetsAsCommitted(); }
                foreach (var failure in result) Debug.LogError("Halcyonic: feature composition: " + failure);
                if (result.Count == 0) Debug.Log("Halcyonic: feature composition: every check passed; " + folder);
                return result;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> CharacterMaterialCacheLifecycle()
        {
            var failures = new List<string>();
            var root = new GameObject("Generated feature material ownership witness");
            try
            {
                var first = CharacterView.Create(root.transform, "feature-Round-Ears-39");
                var second = CharacterView.Create(root.transform, "feature-Clover-Horns-39");
                var body = first.Body.Find("Shell").GetComponent<Renderer>().sharedMaterial;
                var generated = first.FeatureForRender.Renderer!.sharedMaterial;
                if (generated != second.FeatureForRender.Renderer!.sharedMaterial) failures.Add("Feature surfaces are cloned per instance rather than shared.");
                UnityEngine.Object.DestroyImmediate(first.gameObject); UnityEngine.Object.DestroyImmediate(second.gameObject);
                var reset = typeof(CharacterView).Assembly.GetType("Halcyonic.XR.CharacterMaterials")?.GetMethod("Reset",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (reset == null) failures.Add("Feature material ownership reset could not be exercised.");
                else
                {
                    reset.Invoke(null, null);
                    if (generated != null) failures.Add("Subsystem reset invoked in edit mode leaked the generated feature material.");
                    if (body == null) failures.Add("Feature cache reset destroyed the Resources-owned body material.");
                    var rebuilt = CharacterView.Create(root.transform, "feature-Round-Ears-39");
                    var another = CharacterView.Create(root.transform, "feature-Clover-Horns-39");
                    var replacement = rebuilt.FeatureForRender.Renderer!.sharedMaterial;
                    if (replacement == null || ReferenceEquals(replacement, generated)
                        || replacement != another.FeatureForRender.Renderer!.sharedMaterial || replacement.GetFloat("_DepthWrite") != 0f
                        || rebuilt.Body.Find("Shell").GetComponent<Renderer>().sharedMaterial != body || body.GetFloat("_DepthWrite") != 1f)
                        failures.Add("A feature cache reset did not recreate its owned surface while retaining body resource ownership.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }

        private static IEnumerable<string> CharacterOpaqueOcclusion(string name, string folder, Camera camera, RenderTexture texture, GameObject root, CharacterView view)
        {
            var failures = new List<string>();
            var feature = view.FeatureForRender.Renderer!;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var visible = renderers.Select(renderer => renderer.enabled).ToArray();
            GameObject? occluder = null; Material? material = null;
            try
            {
                foreach (var renderer in renderers) renderer.enabled = renderer == feature;
                var shader = Shader.Find("Unlit/Color");
                if (shader == null) return new[] { name + ": opaque occlusion shader unavailable." };
                material = new Material(shader) { color = new Color(0.6f, 0.65f, 0.7f, 1f) };
                occluder = GameObject.CreatePrimitive(PrimitiveType.Cube);
                occluder.name = "Actual opaque depth witness"; occluder.transform.SetParent(root.transform, false);
                occluder.GetComponent<Renderer>().sharedMaterial = material;
                var bounds = feature.bounds;
                var direction = (bounds.center - camera.transform.position).normalized;
                var distance = Vector3.Distance(bounds.center, camera.transform.position);
                var radius = bounds.extents.magnitude;
                occluder.transform.rotation = Quaternion.LookRotation(direction);
                occluder.transform.localScale = new Vector3(radius * 4f, radius * 4f, 0.01f);
                foreach (var front in new[] { true, false })
                {
                    occluder.transform.position = camera.transform.position + direction * (distance + (front ? -1f : 1f) * (radius + 0.03f));
                    Texture2D? with = null; Texture2D? without = null;
                    try
                    {
                        feature.enabled = true; with = WorkspaceRender.Render(camera, texture);
                        feature.enabled = false; without = WorkspaceRender.Render(camera, texture);
                        feature.enabled = true;
                        File.WriteAllBytes(Path.Combine(folder, name + (front ? "-opaque-in-front.png" : "-opaque-behind.png")), with.EncodeToPNG());
                        var changed = ExactCharacterPixels(with, without);
                        if (front && changed != 0) failures.Add(name + ": feature paints over an actual opaque foreground occluder.");
                        if (!front && changed == 0) failures.Add(name + ": an opaque background hides the foreground feature.");
                    }
                    finally
                    {
                        feature.enabled = true;
                        if (with != null) UnityEngine.Object.DestroyImmediate(with);
                        if (without != null) UnityEngine.Object.DestroyImmediate(without);
                    }
                }
            }
            finally
            {
                for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = visible[index];
                if (occluder != null) UnityEngine.Object.DestroyImmediate(occluder);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
            return failures;
        }

        private static IEnumerable<string> CharacterRingOcclusion(string name, string folder, Camera camera, RenderTexture texture, GameObject root, CharacterView view)
        {
            var failures = new List<string>();
            var feature = view.FeatureForRender.Renderer!;
            var shell = view.Body.Find("Shell").GetComponent<Renderer>();
            var ring = view.Body.Find("Ring").GetComponent<Renderer>();
            if (!ring.enabled) return new[] { name + ": actual Checking ring is not visible for its occlusion witness." };
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var visible = renderers.Select(renderer => renderer.enabled).ToArray();
            var images = new List<Texture2D>();
            try
            {
                var full = Shot("ring-full", renderer => true);
                var withoutRing = Shot("ring-hidden", renderer => renderer != ring);
                var featureOnly = Shot("feature-only", renderer => renderer == feature);
                var shellOnly = Shot("shell-only", renderer => renderer == shell);
                var ringOnly = Shot("ring-only", renderer => renderer == ring);
                var originalMaterial = feature.sharedMaterial;
                var later = new Material(originalMaterial) { renderQueue = ring.sharedMaterial.renderQueue + 1 };
                Texture2D reference;
                try
                {
                    // At samples where the ring is geometrically behind the feature, this is the painter-order reference.
                    feature.sharedMaterial = later;
                    reference = Shot("reference-feature-after-ring", renderer => true);
                }
                finally { feature.sharedMaterial = originalMaterial; UnityEngine.Object.DestroyImmediate(later); }
                var actual = full.GetPixels32(); var absent = withoutRing.GetPixels32();
                var rearReference = reference.GetPixels32();
                var featurePixels = featureOnly.GetPixels32(); var shellPixels = shellOnly.GetPixels32(); var ringPixels = ringOnly.GetPixels32();
                var featureMesh = CharacterRayMesh(feature); var shellMesh = CharacterRayMesh(shell); var ringMesh = CharacterRayMesh(ring);
                var behindFeature = 0; var behindShell = 0; var foreground = 0; var visibleForeground = 0;
                for (var pixel = 0; pixel < actual.Length; pixel++)
                {
                    if (ringPixels[pixel].r == 0 && ringPixels[pixel].g == 0 && ringPixels[pixel].b == 0) continue;
                    var ray = camera.ScreenPointToRay(new Vector3(pixel % Size + 0.5f, pixel / Size + 0.5f, 0f));
                    var ringDepth = CharacterRayDepth(ringMesh, ray, cullBack: false);
                    if (float.IsPositiveInfinity(ringDepth)) continue;
                    var shellDepth = CharacterRayDepth(shellMesh, ray);
                    var featureDepth = CharacterRayDepth(featureMesh, ray);
                    var shellDrawn = shellPixels[pixel].r != 0 || shellPixels[pixel].g != 0 || shellPixels[pixel].b != 0;
                    var featureDrawn = featurePixels[pixel].r != 0 || featurePixels[pixel].g != 0 || featurePixels[pixel].b != 0;
                    if (shellDrawn && shellDepth + 0.00001f < ringDepth && shellDepth < featureDepth)
                    {
                        behindShell++;
                        if (!SameCharacterPixel(actual[pixel], absent[pixel])) failures.Add(name + ": rear ring paints over its depth-writing shell at " + pixel % Size + "," + pixel / Size + ".");
                    }
                    else if (featureDrawn && featureDepth + 0.00001f < ringDepth && featureDepth < shellDepth)
                    {
                        behindFeature++;
                        if (!SameCharacterPixel(actual[pixel], rearReference[pixel])) failures.Add(name + ": later transparent ring differs from its actual rear-feature reference at " + pixel % Size + "," + pixel / Size + ".");
                    }
                    else if (ringDepth + 0.00001f < Mathf.Min(shellDepth, featureDepth))
                    {
                        foreground++;
                        if (!SameCharacterPixel(actual[pixel], absent[pixel])) visibleForeground++;
                    }
                }
                if (behindShell == 0) failures.Add(name + ": actual ring has no rear-shell sample, so its depth relationship was not exercised.");
                if (visibleForeground == 0) failures.Add(name + ": actual foreground ring disappears from the composition.");
                Debug.Log("Halcyonic: " + name + ": ring depth samples behind shell=" + behindShell + ", behind feature=" + behindFeature + ", foreground=" + foreground + ", visible foreground=" + visibleForeground
                    + "; no overlap sample is not a claim of generic transparent occlusion support.");

                Texture2D Shot(string suffix, Func<Renderer, bool> includes)
                {
                    for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = visible[index] && includes(renderers[index]);
                    var image = WorkspaceRender.Render(camera, texture); images.Add(image);
                    File.WriteAllBytes(Path.Combine(folder, name + "-" + suffix + ".png"), image.EncodeToPNG());
                    return image;
                }
            }
            finally
            {
                for (var index = 0; index < renderers.Length; index++) renderers[index].enabled = visible[index];
                foreach (var image in images) UnityEngine.Object.DestroyImmediate(image);
            }
            return failures;
        }

        private static (Vector3[] Vertices, int[] Triangles) CharacterRayMesh(Renderer renderer)
        {
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            return (mesh.vertices.Select(vertex => renderer.transform.TransformPoint(vertex)).ToArray(), mesh.triangles);
        }

        private static float CharacterRayDepth((Vector3[] Vertices, int[] Triangles) mesh, Ray ray, bool cullBack = true)
        {
            var nearest = float.PositiveInfinity;
            for (var triangle = 0; triangle < mesh.Triangles.Length; triangle += 3)
            {
                var a = mesh.Vertices[mesh.Triangles[triangle]]; var b = mesh.Vertices[mesh.Triangles[triangle + 1]]; var c = mesh.Vertices[mesh.Triangles[triangle + 2]];
                var first = b - a; var second = c - a; var cross = Vector3.Cross(ray.direction, second); var determinant = Vector3.Dot(first, cross);
                if (cullBack ? determinant <= 1e-12f : Mathf.Abs(determinant) <= 1e-12f) continue; // Ring Cull Off; body Cull Back.
                var from = ray.origin - a; var u = Vector3.Dot(from, cross) / determinant;
                if (u < 0f || u > 1f) continue;
                var along = Vector3.Cross(from, first); var v = Vector3.Dot(ray.direction, along) / determinant;
                if (v < 0f || u + v > 1f) continue;
                var distance = Vector3.Dot(second, along) / determinant;
                if (distance > 0f) nearest = Mathf.Min(nearest, distance);
            }
            return nearest;
        }

        private static IEnumerable<string> CharacterRayDepthWitness()
        {
            var failures = new List<string>();
            var vertices = new[] { new Vector3(-1f, -1f, 1f), new Vector3(0f, 1f, 1f), new Vector3(1f, -1f, 1f) };
            var front = (Vertices: vertices, Triangles: new[] { 0, 1, 2 });
            var back = (Vertices: vertices, Triangles: new[] { 2, 1, 0 });
            var ray = new Ray(Vector3.zero, Vector3.forward);
            if (CharacterRayDepth(front, ray) != 1f || !float.IsPositiveInfinity(CharacterRayDepth(back, ray))
                || CharacterRayDepth(back, ray, cullBack: false) != 1f
                || !float.IsPositiveInfinity(CharacterRayDepth(front, new Ray(new Vector3(3f, 0f, 0f), Vector3.forward)))
                || !float.IsPositiveInfinity(CharacterRayDepth(front, new Ray(new Vector3(0f, 0f, 2f), Vector3.forward))))
                failures.Add("Actual ring depth classifier failed a front/back, missed-ray or behind-ray negative.");
            return failures;
        }

        private static MenuPlane CharacterConsole(Transform root, Vector3 eyes)
        {
            var plane = MenuPlane.Create(root);
            var frame = ProjectsScreens.Projects(new ProjectsScreens.State
            {
                Demonstration = true, TextSize = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard,
            });
            plane.Show(new MenuBar(MenuPlace.Projects, "Projects"), frame, null, null, Array.Empty<CharacterTarget>(),
                eyes, Vector3.forward, null, immediately: true);
            for (var frameIndex = 0; frameIndex < 10; frameIndex++) plane.FrameForRender(0.05f);
            WorkspaceRender.ForceMeshes(root.gameObject);
            return plane;
        }

        private static List<string> CharacterShelfCases(string folder, bool features)
        {
            var failures = new List<string>();
            var wasStill = GlazeMotion.Still;
            try
            {
                GlazeMotion.Still = false;
                var ids = features
                    ? (from BodyShape shape in Enum.GetValues(typeof(BodyShape))
                       from CharacterFeature feature in Enum.GetValues(typeof(CharacterFeature))
                       select Enumerable.Range(0, 10000).Select(n => "feature-" + shape + "-" + feature + "-" + n.ToString(CultureInfo.InvariantCulture))
                         .First(id => CharacterIdentity.Of(id).Shape == shape && CharacterIdentity.Of(id).Feature == feature)).ToArray()
                    : new[] { "shelf-state-witness" };
                foreach (var id in ids)
                {
                    var identity = CharacterIdentity.Of(id);
                    var name = features ? identity.Shape + "-" + identity.Feature : "all-states";
                    var root = new GameObject("Current character shelf " + name);
                    var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
                    try
                    {
                        var eyes = new Vector3(0f, EyeHeight, 0f);
                        var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                        var plane = CharacterConsole(root.transform, eyes);
                        if (root.GetComponentsInChildren<CharacterTarget>(false).Length != 0)
                            failures.Add(name + ": character-free console created a character target.");
                        var original = plane.Direction;
                        var character = WorkspaceRender.ConfirmedPlaneAgent(root.transform, plane, eyes, Shown(id, CharacterActivity.Idle), failures, name);
                        var view = character.View;
                        if (root.GetComponentsInChildren<CharacterTarget>(false).Length != 1)
                            failures.Add(name + ": confirmed shelf does not contain exactly one supported target.");
                        var characters = new List<(CharacterView View, CharacterTarget Target)> { character };
                        failures.AddRange(CharacterEnvelopes(name, eyes, characters));
                        view.Show(Shown(id, CharacterActivity.Idle));
                        view.PlaceShelfLabel(eyes);
                        Advance(view, 1f);
                        WorkspaceRender.ForceMeshes(root);
                        var image = WorkspaceRender.Render(camera, texture);
                        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(image);
                        failures.AddRange(CharacterFeatureShows(name, camera, texture, view));
                        failures.AddRange(KeptStill(name, folder, camera, texture, characters));
                        if (!features)
                        {
                            foreach (CharacterActivity activity in Enum.GetValues(typeof(CharacterActivity)))
                            {
                                view.Show(Shown(id, activity));
                                view.PlaceShelfLabel(eyes);
                                failures.AddRange(CharacterStateStill(name + "-" + activity, folder, camera, texture, view));
                            }
                        }
                        if (plane.Direction.Yaw != original.Yaw || plane.Direction.Elevation != original.Elevation)
                            failures.Add(name + ": the optional shelf moved the independent console.");
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(root);
                        texture.Release(); UnityEngine.Object.DestroyImmediate(texture);
                    }
                }
            }
            finally { GlazeMotion.Still = wasStill; }
            return failures;
        }

        private static IEnumerable<string> CharacterStateStill(string name, string folder, Camera camera, RenderTexture texture, CharacterView view)
        {
            var failures = new List<string>();
            var wasStill = GlazeMotion.Still;
            Texture2D? first = null;
            Texture2D? later = null;
            try
            {
                GlazeMotion.Still = true;
                Advance(view, 3f);
                var before = CharacterFrameValues(new[] { view });
                first = WorkspaceRender.Render(camera, texture);
                Advance(view, 1f);
                later = WorkspaceRender.Render(camera, texture);
                SaveCharacterFrameDifferences(folder, name, before, CharacterFrameValues(new[] { view }));
                File.WriteAllBytes(Path.Combine(folder, name + "-still-before.png"), first.EncodeToPNG());
                File.WriteAllBytes(Path.Combine(folder, name + "-still-after.png"), later.EncodeToPNG());
                var difference = WorkspaceRender.Compare(first, later, new RectInt(0, 0, Size, Size));
                if (difference.Changed != 0) failures.Add(name + ": kept-still state changes " + difference.Changed + " pixels by up to "
                    + (difference.Largest * 255f).ToString("0", CultureInfo.InvariantCulture) + " of 255.");
            }
            finally
            {
                GlazeMotion.Still = wasStill;
                if (first != null) UnityEngine.Object.DestroyImmediate(first);
                if (later != null) UnityEngine.Object.DestroyImmediate(later);
            }
            return failures;
        }

        private static IEnumerable<string> CharacterFeatureShows(string name, Camera camera, RenderTexture texture, CharacterView view)
        {
            var failures = new List<string>();
            var identity = CharacterIdentity.Of(view.WorkstreamId);
            var (feature, color) = view.FeatureForRender;
            if ((feature != null) != (identity.Feature != CharacterFeature.None))
                failures.Add(name + ": feature presence differs from the declared identity.");
            if (feature == null) return failures;
            Color.RGBToHSV(color, out var hue, out var saturation, out _);
            foreach (var stateHue in CharacterIdentity.StateHues)
                if (saturation > 0.1f && Mathf.Abs(Mathf.DeltaAngle(hue * 360f, (float)stateHue)) < 25f)
                    failures.Add(name + ": feature hue resembles a state cue.");
            if (Mathf.Abs(Mathf.DeltaAngle(hue * 360f, (float)identity.Hue)) > 20f)
                failures.Add(name + ": feature does not retain the body's hue.");
            var with = WorkspaceRender.Render(camera, texture);
            Texture2D? without = null;
            try
            {
                feature.enabled = false;
                without = WorkspaceRender.Render(camera, texture);
                if (WorkspaceRender.Compare(with, without, new RectInt(0, 0, Size, Size)).Changed == 0)
                    failures.Add(name + ": disabling only the feature changes no rendered pixels.");
            }
            finally
            {
                feature.enabled = true;
                UnityEngine.Object.DestroyImmediate(with);
                if (without != null) UnityEngine.Object.DestroyImmediate(without);
            }
            return failures;
        }

        /// <summary>Exact production values, retained alongside both images before any stillness repair.</summary>
        private static SortedDictionary<string, string> CharacterFrameValues(IEnumerable<CharacterView> views)
        {
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            var block = new MaterialPropertyBlock();
            foreach (var view in views)
            {
                var prefix = view.WorkstreamId + "/";
                values[prefix + "clock"] = view.ClockForRender.ToString("R", CultureInfo.InvariantCulture);
                foreach (var part in view.GetComponentsInChildren<Transform>(true))
                {
                    var key = prefix + part.name + "#" + part.GetInstanceID().ToString(CultureInfo.InvariantCulture) + "/";
                    values[key + "position"] = part.localPosition.ToString("R");
                    values[key + "rotation"] = part.localRotation.ToString("R");
                    values[key + "scale"] = part.localScale.ToString("R");
                    if (!part.TryGetComponent<Renderer>(out var renderer)) continue;
                    values[key + "enabled"] = renderer.enabled.ToString();
                    if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_DepthWrite"))
                        values[key + "material depth"] = renderer.sharedMaterial.GetFloat("_DepthWrite").ToString("R", CultureInfo.InvariantCulture);
                    renderer.GetPropertyBlock(block);
                    foreach (var property in new[] { "_Clock", "_Flow", "_FlowSpeed", "_Crack", "_Fog", "_Ghost", "_Saturation", "_EyeKind", "_EyeOpen", "_EyeUneven", "_EyeInk" })
                        values[key + property] = block.GetFloat(property).ToString("R", CultureInfo.InvariantCulture);
                    foreach (var property in new[] { "_BodyColor", "_Look", "_EyeLayout", "_Color" })
                        values[key + property] = block.GetVector(property).ToString("R");
                }
            }
            return values;
        }

        private static void SaveCharacterFrameDifferences(string folder, string name, SortedDictionary<string, string> before, SortedDictionary<string, string> after)
        {
            var differences = before.Where(pair => !after.TryGetValue(pair.Key, out var value) || value != pair.Value)
                .Select(pair => pair.Key + ": " + pair.Value + " -> " + (after.TryGetValue(pair.Key, out var value) ? value : "missing")).ToArray();
            File.WriteAllLines(Path.Combine(folder, name + "-kept-still-values-before.txt"), before.Select(pair => pair.Key + ": " + pair.Value));
            File.WriteAllLines(Path.Combine(folder, name + "-kept-still-values-after.txt"), after.Select(pair => pair.Key + ": " + pair.Value));
            File.WriteAllLines(Path.Combine(folder, name + "-kept-still-differences.txt"), differences);
            Debug.Log("Halcyonic: " + name + ": kept-still value differences: " + string.Join("; ", differences));
        }

        private static IEnumerable<string> CharacterProjectionEdges()
        {
            var root = new GameObject("Character projection edge witness");
            var mesh = new Mesh();
            try
            {
                // Endpoints reach only 35.264 degrees; the edge's middle reaches 45 degrees.
                // This fails a vertices-only implementation as well as the old box-corner witness.
                mesh.vertices = new[] { new Vector3(-1f, 1f, 1f), new Vector3(1f, 1f, 1f), new Vector3(0f, 0f, 1f) };
                mesh.triangles = new[] { 0, 1, 2 };
                mesh.RecalculateBounds();
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = root.AddComponent<MeshRenderer>();
                var extent = GlazeChecks.MeshOutline("edge witness", Vector3.zero, new[] { renderer });
                if (Mathf.Abs(extent.Top - 45f) > 1e-4f || Mathf.Abs(extent.Bottom) > 1e-4f)
                    return new[] { "Character triangle projection misses a vertical edge extremum." };
                // Rotation makes box corners phantom points beyond the actual triangle.
                root.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                extent = GlazeChecks.MeshOutline("rotated edge witness", Vector3.zero, new[] { renderer });
                var box = GlazeChecks.Of("rotated box", Vector3.zero, new[] { renderer });
                if (box.Right - extent.Right < 10f)
                    return new[] { "Character triangle projection includes a phantom bounding-box corner." };
                return Array.Empty<string>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>
        /// Regression samples of actual shell/feature vertices, every shape and feature, in moving
        /// and held state poses. These samples supplement the conservative scale bound; they do not find all extrema.
        /// </summary>
        private static IEnumerable<string> CharacterEnvelopes(string name, Vector3 eyes, List<(CharacterView View, CharacterTarget Target)> characters)
        {
            var failures = new List<string>();
            var wasStill = GlazeMotion.Still;
            var block = new MaterialPropertyBlock();
            try
            {
                foreach (var still in new[] { false, true })
                {
                    GlazeMotion.Still = still;
                    foreach (CharacterActivity activity in Enum.GetValues(typeof(CharacterActivity)))
                    {
                        foreach (var (view, _) in characters)
                        {
                            view.Show(new CharacterPresentation(view.WorkstreamId, "A task", activity, CharacterPresenter.LabelOf(activity), AttentionLevel.None,
                                Array.Empty<string>(), 0, false, false, false));
                        }
                        foreach (var seconds in new[] { 0.017f, 0.13f, 0.5f, 1.5f })
                        {
                            foreach (var (view, _) in characters)
                            {
                                view.AdvanceForRender(seconds);
                                view.PlaceShelfLabel(eyes);
                                WorkspaceRender.ForceMeshes(view.gameObject);
                                var pose = new Pose(view.transform.parent.position, view.transform.parent.rotation);
                                var corners = new Vector3[8];
                                CharacterStage.WriteShelfCorners(pose, AgentShelfDraft.Width(1), eyes, corners);
                                var envelope = new Bounds(Quaternion.Inverse(pose.rotation) * (corners[0] - pose.position), Vector3.zero);
                                foreach (var corner in corners) envelope.Encapsulate(Quaternion.Inverse(pose.rotation) * (corner - pose.position));
                                foreach (var filter in view.GetComponentsInChildren<MeshFilter>(false))
                                    foreach (var vertex in filter.sharedMesh.vertices)
                                        if (!envelope.Contains(Quaternion.Inverse(pose.rotation) * (filter.transform.TransformPoint(vertex) - pose.position)))
                                        { failures.Add(name + ": actual " + filter.name + " escapes the confirmed shelf envelope in " + activity + "."); break; }
                                var shellAt = view.Body.Find("Shell");
                                var bottom = shellAt.GetComponent<MeshFilter>().sharedMesh.vertices.Min(vertex => shellAt.TransformPoint(vertex).y);
                                if (!view.Grounded || Mathf.Abs(bottom - pose.position.y) > 0.0001f)
                                    failures.Add(name + ": actual shell loses grounded contact in " + activity + ".");
                                failures.AddRange(GlazeChecks.TextAsSeen(view.Label.gameObject, eyes, name + " " + activity));
                                var what = name + " " + activity + (still ? " kept still" : " moving");
                                var shell = view.Body.Find("Shell");
                                foreach (var mesh in shell.GetComponentsInChildren<MeshFilter>())
                                {
                                    foreach (var vertex in mesh.sharedMesh.vertices)
                                    {
                                        var local = view.Body.InverseTransformPoint(mesh.transform.TransformPoint(vertex));
                                        if (local.magnitude > CharacterView.BodyExtent + 1e-5f)
                                        {
                                            failures.Add(what + ": " + mesh.name + " escapes the visual/hand-target envelope.");
                                            break;
                                        }
                                    }
                                }
                                var body = WorkspaceRender.BodyExtent(view, eyes);
                                var label = GlazeChecks.Of(view.WorkstreamId + " label", eyes, view.Label.gameObject);
                                if (label.Apart(body) < OwnLabelGapDegrees) failures.Add(what + ": shell/feature touches its label.");
                                if (view.FeatureForRender.Renderer is MeshRenderer feature)
                                {
                                    if (feature.sortingOrder != shell.GetComponent<Renderer>().sortingOrder - 1
                                        || feature.sharedMaterial.GetFloat("_DepthWrite") != 0f
                                        || shell.GetComponent<Renderer>().sharedMaterial.GetFloat("_DepthWrite") != 1f)
                                        failures.Add(what + ": shell/feature order or depth ownership changed.");
                                    feature.GetPropertyBlock(block);
                                    if (block.GetFloat("_EyeInk") != 0f) failures.Add(what + ": a feature inherited eyes.");
                                    foreach (var key in new[] { "_Flow", "_Crack", "_Fog", "_Ghost", "_Saturation" })
                                    {
                                        var own = block.GetFloat(key);
                                        shell.GetComponent<Renderer>().GetPropertyBlock(block);
                                        if (Mathf.Abs(own - block.GetFloat(key)) > 1e-6f) failures.Add(what + ": feature's " + key + " differs from its task's surface state.");
                                        feature.GetPropertyBlock(block);
                                    }
                                    var mesh = feature.GetComponent<MeshFilter>().sharedMesh;
                                    if (mesh.vertexCount > 300 || mesh.GetIndexCount(0) / 3 > 588)
                                        failures.Add(what + ": feature exceeds the declared geometry budget.");
                                    if (feature.sharedMaterials.Length != 1 || feature.sharedMaterial.passCount != 1)
                                        failures.Add(what + ": feature uses more than one material pass.");
                                }
                            }
                            for (var first = 0; first < characters.Count; first++)
                            {
                                for (var other = first + 1; other < characters.Count; other++)
                                {
                                    var a = characters[first].Target;
                                    var b = characters[other].Target;
                                    if (Vector3.Distance(a.BodyPosition, b.BodyPosition) <= CharacterView.BodyRadius * (a.Scale + b.Scale))
                                        failures.Add(name + ": neighboring hand/gaze target spheres overlap in " + activity + ".");
                                }
                            }
                        }
                    }
                }
            }
            finally { GlazeMotion.Still = wasStill; }
            return failures;
        }

        /// <summary>
        /// Two fixed identities run through the actual animation clock and frame path. Their own intervals,
        /// the blink's end, and the absence of blinks when still, closed or not live are checked in pose and pixels.
        /// </summary>
        private static IEnumerable<string> CharacterBlinks(string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Character blink witnesses");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            var wasStill = GlazeMotion.Still;
            try
            {
                GlazeMotion.Still = false;
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = WorkspaceRender.MakeCamera(root.transform, eyes, texture);
                var ids = new[] { "01a0dcf1-5a80-7000-8000-000000000001", "01a0dcf1-5a80-7000-8000-000000000002" };
                var plane = CharacterConsole(root.transform, eyes);
                var views = new List<CharacterView>();
                foreach (var id in ids)
                {
                    var character = WorkspaceRender.ConfirmedPlaneAgent(root.transform, plane, eyes, Shown(id, CharacterActivity.Idle), failures, "blink " + id);
                    character.View.transform.parent.gameObject.SetActive(false);
                    views.Add(character.View);
                }
                if (Math.Abs(CharacterIdentity.Of(ids[0]).BlinkEverySeconds - CharacterIdentity.Of(ids[1]).BlinkEverySeconds) < 1)
                    failures.Add("blink witnesses do not have distinct intervals.");
                foreach (var view in views)
                {
                    view.transform.parent.gameObject.SetActive(true);
                    var period = (float)CharacterIdentity.Of(view.WorkstreamId).BlinkEverySeconds;
                    // Begin safely after one blink. Then advance to the next, using the identity's
                    // declared period rather than setting private animation state for the witness.
                    var after = (Mathf.Floor(view.ClockForRender / period) + 1) * period + 0.3f;
                    Advance(view, after - view.ClockForRender);
                    var openPose = view.PoseForRender.Open;
                    UnityEngine.Object.DestroyImmediate(Capture(view, "open"));
                    var firstBlink = (Mathf.Floor(view.ClockForRender / period) + 1) * period + Glaze.BlinkSeconds / 2f;
                    Advance(view, firstBlink - view.ClockForRender);
                    if (view.PoseForRender.Open > 0.3f) failures.Add(view.WorkstreamId + ": eyes do not blink at their identity's interval.");
                    var blink = Capture(view, "blink");
                    var shell = view.Body.Find("Shell").GetComponent<Renderer>();
                    var original = new MaterialPropertyBlock();
                    var opened = new MaterialPropertyBlock();
                    shell.GetPropertyBlock(original);
                    shell.GetPropertyBlock(opened);
                    Texture2D? openAtBlink = null;
                    try
                    {
                        // Same production frame, same clock, transforms, surface and gaze. Only the
                        // shell's EyeOpen changes, so breathing or eye wandering cannot satisfy this witness.
                        opened.SetFloat("_EyeOpen", openPose);
                        shell.SetPropertyBlock(opened);
                        openAtBlink = Capture(view, "open-at-blink");
                        if (WorkspaceRender.Compare(blink, openAtBlink, new RectInt(0, 0, Size, Size)).Changed == 0)
                            failures.Add(view.WorkstreamId + ": changing only _EyeOpen at the actual blink changes no rendered eye pixels.");
                    }
                    finally
                    {
                        shell.SetPropertyBlock(original);
                        UnityEngine.Object.DestroyImmediate(blink);
                        if (openAtBlink != null) UnityEngine.Object.DestroyImmediate(openAtBlink);
                    }
                    Advance(view, Glaze.BlinkSeconds / 2f - 0.01f);
                    if (view.PoseForRender.Open > 0.3f) failures.Add(view.WorkstreamId + ": blink ended before the shared duration.");
                    Advance(view, 0.16f);
                    if (view.PoseForRender.Open < 0.85f) failures.Add(view.WorkstreamId + ": blink fails to reopen after the shared duration.");
                    var nextBlink = firstBlink + period;
                    Advance(view, nextBlink - view.ClockForRender);
                    if (view.PoseForRender.Open > 0.3f) failures.Add(view.WorkstreamId + ": next blink fails to repeat at its declared interval.");

                    GlazeMotion.Still = true;
                    Advance(view, period);
                    if (view.PoseForRender.Open < 0.85f) failures.Add(view.WorkstreamId + ": Keep things still does not suppress blinks.");
                    var held = Capture(view, "still");
                    Advance(view, period);
                    var later = Capture(view, "still-later");
                    if (WorkspaceRender.Compare(held, later, new RectInt(0, 0, Size, Size)).Changed != 0)
                        failures.Add(view.WorkstreamId + ": kept-still character changes pixels across a blink interval.");
                    UnityEngine.Object.DestroyImmediate(held);
                    UnityEngine.Object.DestroyImmediate(later);
                    GlazeMotion.Still = false;

                    // Enter while a different eye-kind transition is still in flight.
                    view.Show(Shown(view.WorkstreamId, CharacterActivity.Failed));
                    Advance(view, 0.04f);
                    view.Show(Shown(view.WorkstreamId, CharacterActivity.Idle, stale: true));
                    var frozenClock = view.ClockForRender;
                    var stale = Capture(view, "stale");
                    var frozenPosition = view.Body.localPosition;
                    var frozenRotation = view.Body.localRotation;
                    var frozenPose = view.PoseForRender;
                    var frozenValues = CharacterFrameValues(new[] { view });
                    Advance(view, 1f);
                    var settled = Capture(view, "stale-one-second");
                    if (WorkspaceRender.Compare(stale, settled, new RectInt(0, 0, Size, Size)).Changed != 0)
                        failures.Add(view.WorkstreamId + ": last-known appearance continues settling after Show.");
                    UnityEngine.Object.DestroyImmediate(settled);
                    Advance(view, period);
                    if (view.ClockForRender != frozenClock) failures.Add(view.WorkstreamId + ": last-known animation clock advances.");
                    if (view.Body.localPosition != frozenPosition || view.Body.localRotation != frozenRotation || view.PoseForRender != frozenPose)
                        failures.Add(view.WorkstreamId + ": last-known pose continues settling after Show.");
                    var staleLater = Capture(view, "stale-later");
                    if (WorkspaceRender.Compare(stale, staleLater, new RectInt(0, 0, Size, Size)).Changed != 0)
                        failures.Add(view.WorkstreamId + ": last-known character blinks or otherwise changes rendered state.");
                    UnityEngine.Object.DestroyImmediate(stale);
                    UnityEngine.Object.DestroyImmediate(staleLater);

                    view.Show(Shown(view.WorkstreamId, CharacterActivity.Working));
                    Advance(view, 0.13f);
                    var resumedValues = CharacterFrameValues(new[] { view });
                    if (view.ClockForRender <= frozenClock || !frozenValues.Any(pair => !pair.Key.EndsWith("clock", StringComparison.Ordinal)
                        && !pair.Key.EndsWith("_Clock", StringComparison.Ordinal) && resumedValues[pair.Key] != pair.Value))
                        failures.Add(view.WorkstreamId + ": a live presentation fails to resume animation after last known.");

                    foreach (var activity in new[] { CharacterActivity.TurnFinished, CharacterActivity.Failed, CharacterActivity.Interrupted })
                    {
                        view.Show(Shown(view.WorkstreamId, activity));
                        Advance(view, 1);
                        var kind = view.Body.Find("Shell").GetComponent<Renderer>();
                        var block = new MaterialPropertyBlock();
                        kind.GetPropertyBlock(block);
                        var before = block.GetFloat("_EyeKind");
                        var beforeClock = view.ClockForRender;
                        var beforeValues = CharacterFrameValues(new[] { view });
                        Texture2D? beforePixels = activity == CharacterActivity.Interrupted ? Capture(view, "interrupted") : null;
                        Advance(view, period);
                        kind.GetPropertyBlock(block);
                        if (before == 0 || block.GetFloat("_EyeKind") != before) failures.Add(view.WorkstreamId + ": " + activity + " gains open blinking eyes.");
                        if (beforePixels != null)
                        {
                            var afterPixels = Capture(view, "interrupted-later");
                            try
                            {
                                var afterValues = CharacterFrameValues(new[] { view });
                                if (view.ClockForRender != beforeClock || beforeValues.Any(pair => afterValues[pair.Key] != pair.Value))
                                    failures.Add(view.WorkstreamId + ": Interrupted continues changing production values.");
                                if (WorkspaceRender.Compare(beforePixels, afterPixels, new RectInt(0, 0, Size, Size)).Changed != 0)
                                    failures.Add(view.WorkstreamId + ": Interrupted continues changing rendered pixels.");
                            }
                            finally { UnityEngine.Object.DestroyImmediate(beforePixels); UnityEngine.Object.DestroyImmediate(afterPixels); }
                        }
                    }
                    view.transform.parent.gameObject.SetActive(false);
                }

                Texture2D Capture(CharacterView view, string moment)
                {
                    WorkspaceRender.ForceMeshes(root);
                    var image = CloseUp(camera, texture, view.Body.position, out _);
                    File.WriteAllBytes(Path.Combine(folder, "blink-" + Array.IndexOf(ids, view.WorkstreamId).ToString(CultureInfo.InvariantCulture) + "-" + moment + ".png"), image.EncodeToPNG());
                    return image;
                }
            }
            finally
            {
                GlazeMotion.Still = wasStill;
                UnityEngine.Object.DestroyImmediate(root);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
            return failures;
        }

        private static void Advance(CharacterView view, float seconds)
        {
            while (seconds > 0.000001f)
            {
                var frame = Mathf.Min(seconds, 1f / 72f);
                view.AdvanceForRender(frame);
                seconds -= frame;
            }
        }

        private static CharacterPresentation Shown(string id, CharacterActivity activity, bool stale = false) =>
            new CharacterPresentation(id, "A task", activity, CharacterPresenter.LabelOf(activity), AttentionLevel.None, Array.Empty<string>(), 0, false, false, stale);
    }
}
