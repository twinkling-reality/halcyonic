#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oculus.Interaction;
using Oculus.Interaction.Editor;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Sets up hand and gaze interaction in Stage.unity: Meta's comprehensive interaction rig, added
    /// the way the Interaction SDK's "Interactions Rig" building block adds it, then adapted to the
    /// stage, and the SDK's gaze, as its gaze quick action builds it. Running it again changes
    /// nothing. In the editor: Halcyonic > Set Up Stage Interaction. In batch mode, with the editor
    /// closed, see docs/internal/runbooks/XR_DEVELOPMENT.md.
    /// </summary>
    public static class StageSetup
    {
        private const string ScenePath = "Assets/Halcyonic/Scenes/Stage.unity";

        /// <summary>OVRComprehensiveInteractionRig.prefab in com.meta.xr.sdk.interaction.ovr, as Meta's rig wizard names it.</summary>
        private const string RigPrefabGuid = "0a7d2469f24041c4284c66706f84c45e";

        private const string RigName = "OVRComprehensiveInteractionRig";

        /// <summary>OVREyeGaze.prefab in com.meta.xr.sdk.interaction.ovr, as the SDK's gaze quick action names it.</summary>
        private const string EyeGazePrefabGuid = "fa67d7a229b4f944ebe85912157d8d30";

        /// <summary>
        /// The stage is stationary. The rig's locomotion would turn, slide or teleport the person
        /// away from it (hand microgestures, controller sticks), and its locomotor's tunneling
        /// darkens the view when the head meets a collider, such as a character's.
        /// </summary>
        private static readonly string[] Locomotion =
        {
            "Locomotor",
            "MicroGesturesLocomotionHandInteractorGroup",
            "LocomotionControllerInteractorGroup",
        };

        /// <summary>Hidden when the app loses focus: the rig's hands and controllers, and its interactors, so no input arrives.</summary>
        private static readonly string[] FocusGuarded =
        {
            "OVRHandVisualLeft",
            "OVRHandVisualRight",
            "OVRControllerVisualLeft",
            "OVRControllerVisualRight",
            "ComprehensiveInteractorsLeft",
            "ComprehensiveInteractorsRight",
        };

        [MenuItem("Halcyonic/Set Up Stage Interaction")]
        public static void Apply()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var cameraRig = UnityEngine.Object.FindAnyObjectByType<OVRCameraRig>()
                ?? throw new InvalidOperationException(ScenePath + " has no camera rig.");
            var rig = cameraRig.GetComponentInChildren<OVRCameraRigRef>(true)?.gameObject ?? AddRig(cameraRig);
            HideBuildingBlockHands(cameraRig);
            foreach (var name in Locomotion)
            {
                var groups = FindAll(rig, name);
                if (groups.Count == 0) throw new InvalidOperationException("The interaction rig has no " + name + ":\n" + Describe(rig.transform, 0, 12));
                foreach (var group in groups) group.SetActive(false);
            }
            GuardFocus(rig);
            AddGaze(rig);
            var stage = UnityEngine.Object.FindAnyObjectByType<CharacterStage>()
                ?? throw new InvalidOperationException(ScenePath + " has no character stage.");
            if (stage.GetComponent<WorkspaceDirector>() == null) stage.gameObject.AddComponent<WorkspaceDirector>();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save " + ScenePath + ".");
            Debug.Log("Halcyonic: stage interaction set up.\n" + Describe(rig.transform, 0, 4));
        }

        /// <summary>What Meta's rig wizard does: the rig prefab under the camera rig, wired by the SDK's auto-wiring.</summary>
        private static GameObject AddRig(OVRCameraRig cameraRig)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(RigPrefabGuid))
                ?? throw new InvalidOperationException("The Interaction SDK's comprehensive rig prefab is missing.");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, cameraRig.transform);
            rig.name = RigName;
            UnityObjectAddedBroadcaster.HandleObjectWasAdded(rig);
            return rig;
        }

        /// <summary>
        /// The rig brings its own hand visuals, so, as Meta's wizard does, the Hand Tracking building
        /// block keeps tracking but stops drawing its hands.
        /// </summary>
        private static void HideBuildingBlockHands(OVRCameraRig cameraRig)
        {
            foreach (var hand in cameraRig.trackingSpace.GetComponentsInChildren<OVRHand>(true))
            {
                if (hand.TryGetComponent<OVRSkeletonRenderer>(out var skeletonRenderer)) skeletonRenderer.enabled = false;
                if (hand.TryGetComponent<OVRMesh>(out var mesh)) mesh.enabled = false;
                if (hand.TryGetComponent<OVRMeshRenderer>(out var meshRenderer)) meshRenderer.enabled = false;
                if (hand.TryGetComponent<SkinnedMeshRenderer>(out var skinnedMeshRenderer)) skinnedMeshRenderer.enabled = false;
            }
        }

        private static void GuardFocus(GameObject rig)
        {
            var guard = UnityEngine.Object.FindAnyObjectByType<FocusGuard>()
                ?? throw new InvalidOperationException(ScenePath + " has no focus guard.");
            var guarded = new List<GameObject>();
            foreach (var name in FocusGuarded)
            {
                guarded.Add(Find(rig, name) ?? throw new InvalidOperationException("The interaction rig has no " + name + "."));
            }
            var serialized = new SerializedObject(guard);
            var visuals = serialized.FindProperty("handVisuals");
            visuals.arraySize = guarded.Count;
            for (var index = 0; index < guarded.Count; index++)
            {
                visuals.GetArrayElementAtIndex(index).objectReferenceValue = guarded[index];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The gaze the workspace's gaze interactor follows, built as the SDK's gaze quick action
        /// builds it: the eye gaze prefab beside the rig's HMD, with a gaze conecaster. A Quest 3 has
        /// no eye tracking, and the app does not ask for it, so the gaze is the head's direction: the
        /// SDK's camera pose emulation.
        /// </summary>
        private static void AddGaze(GameObject rig)
        {
            var eyeGaze = rig.GetComponentInChildren<EyeGaze>(true);
            if (eyeGaze == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(EyeGazePrefabGuid))
                    ?? throw new InvalidOperationException("The Interaction SDK's eye gaze prefab is missing.");
                var hmd = rig.GetComponentInChildren<Hmd>(true)
                    ?? throw new InvalidOperationException("The interaction rig has no HMD.");
                var gaze = (GameObject)PrefabUtility.InstantiatePrefab(prefab, hmd.transform.parent);
                gaze.name = "OVREyeGaze";
                gaze.transform.SetSiblingIndex(hmd.transform.GetSiblingIndex() + 1);
                eyeGaze = gaze.GetComponent<EyeGaze>();
                var conecaster = new GameObject("GazeConecaster");
                conecaster.transform.SetParent(gaze.transform, false);
                conecaster.AddComponent<GazeConecaster>().InjectGaze(eyeGaze);
                UnityObjectAddedBroadcaster.HandleObjectWasAdded(gaze);
            }
            var serialized = new SerializedObject(eyeGaze);
            serialized.FindProperty("_emulateGazeWithCameraPose").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject? Find(GameObject root, string name) => FindAll(root, name).FirstOrDefault();

        private static List<GameObject> FindAll(GameObject root, string name) =>
            root.GetComponentsInChildren<Transform>(true).Where(child => child.name == name).Select(child => child.gameObject).ToList();

        private static string Describe(Transform node, int depth, int maxDepth)
        {
            var line = new StringBuilder();
            line.Append(' ', depth * 2).Append(node.name);
            if (!node.gameObject.activeSelf) line.Append(" (inactive)");
            line.Append('\n');
            if (depth < maxDepth)
            {
                foreach (Transform child in node) line.Append(Describe(child, depth + 1, maxDepth));
            }
            return line.ToString();
        }
    }
}
