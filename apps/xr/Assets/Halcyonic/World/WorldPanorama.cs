#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Halcyonic.XR.World
{
    /// <summary>
    /// The shell a rendered world is shown on: a sphere round the person's eyes, each point pushed out to
    /// the distance the render's own depth gives it, so near things stand nearer and the two eyes see them
    /// apart. The picture is a full equirectangular panorama: its middle column is straight ahead, its top
    /// row straight up. Without a depth the shell is one sphere, far away.
    /// </summary>
    internal static class WorldPanorama
    {
        /// <summary>Nothing of the shell stands nearer than this, in meters: nearer, a wrong depth would cross the eyes.</summary>
        public const float Nearest = 0.8f;

        /// <summary>Nor farther than this, inside the cameras' far plane.</summary>
        public const float Farthest = 900f;

        /// <summary>The direction a point of the picture lies in: across (0 to 1, 0.5 ahead) and up (0 to 1, 1 the zenith).</summary>
        public static Vector3 Direction(float u, float v)
        {
            var longitude = (u - 0.5f) * 2f * Mathf.PI;
            var latitude = (v - 0.5f) * Mathf.PI;
            var level = Mathf.Cos(latitude);
            return new Vector3(Mathf.Sin(longitude) * level, Mathf.Sin(latitude), Mathf.Cos(longitude) * level);
        }

        /// <param name="depth">Distances from the eyes in meters, row by row from the bottom, or null for none.</param>
        public static Mesh Shell(float[]? depth, int depthWidth, int depthHeight, int across = 512, int down = 256)
        {
            if (depth != null && depth.Length != depthWidth * depthHeight) throw new ArgumentException("the depth is not the size it says", nameof(depth));
            var vertices = new List<Vector3>((across + 1) * (down + 1));
            var uvs = new List<Vector2>(vertices.Capacity);
            var triangles = new List<int>(across * down * 6);
            for (var j = 0; j <= down; j++)
            {
                var v = (float)j / down;
                for (var i = 0; i <= across; i++)
                {
                    var u = (float)i / across;
                    var distance = depth == null ? Farthest : Sample(depth, depthWidth, depthHeight, u, v);
                    vertices.Add(Direction(u, v) * Mathf.Clamp(distance, Nearest, Farthest));
                    uvs.Add(new Vector2(u, v));
                }
            }
            for (var j = 0; j < down; j++)
            {
                for (var i = 0; i < across; i++)
                {
                    var a = j * (across + 1) + i;
                    var b = a + across + 1;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                    triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                }
            }
            var mesh = new Mesh { name = "Halcyonic world shell", hideFlags = HideFlags.DontSave, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * (Farthest * 2f + 10f));
            return mesh;
        }

        /// <summary>The nearest of the depths round a point: a shell that errs near hides less than one that errs far.</summary>
        private static float Sample(float[] depth, int width, int height, float u, float v)
        {
            var x = Mathf.Clamp(Mathf.RoundToInt(u * (width - 1)), 0, width - 1);
            var y = Mathf.Clamp(Mathf.RoundToInt(v * (height - 1)), 0, height - 1);
            var nearest = float.MaxValue;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var sx = ((x + dx) % width + width) % width;
                    var sy = Mathf.Clamp(y + dy, 0, height - 1);
                    var value = depth[sy * width + sx];
                    if (value > 0f && value < nearest) nearest = value;
                }
            }
            return nearest == float.MaxValue ? Farthest : nearest;
        }
    }
}
