#nullable enable
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>World-space text with Unity's built-in font, so labels need no imported assets.</summary>
    internal static class Labels
    {
        private static Font? font;

        public static TextMesh Create(Transform parent, string name, Vector3 localPosition, float characterSize)
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var label = new GameObject(name);
            label.transform.SetParent(parent, false);
            label.transform.localPosition = localPosition;
            var renderer = label.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            var text = label.AddComponent<TextMesh>();
            text.font = font;
            text.fontSize = 96;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
            return text;
        }
    }
}
