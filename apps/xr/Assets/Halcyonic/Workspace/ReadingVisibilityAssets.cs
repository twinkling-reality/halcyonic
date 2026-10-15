#nullable enable
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>Explicit development-build scene references; never loads a guessed fallback shader.</summary>
    public sealed class ReadingVisibilityAssets : MonoBehaviour
    {
        public Shader Opaque = null!;
        public Shader Mask = null!;
        public static ReadingVisibilityAssets? Current { get; private set; }
        private void Awake() => Current = this;
#if UNITY_EDITOR
        /// <summary>Explicit edit-mode fixture registration; runtime uses the normal Awake lifecycle.</summary>
        public void RegisterForEditor() => Current = this;
#endif
        private void OnDestroy() { if (Current == this) Current = null; }
    }
}
#endif
