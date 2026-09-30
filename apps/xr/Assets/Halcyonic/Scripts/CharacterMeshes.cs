#nullable enable
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// The meshes characters are built from, generated in code and shared: a body per shape, a quad
    /// for halos and label plates, and the band of the testing ring. Body meshes are in body units,
    /// about one unit in radius, with the face toward +z.
    /// </summary>
    internal static class CharacterMeshes
    {
        /// <summary>Vertices around the outline; enough for smooth normals where an outline turns fast.</summary>
        private const int Segments = 96;

        /// <summary>Rings of vertices from the center of the face to the center of the back.</summary>
        private const int Rings = 28;

        /// <summary>The body is a sphere pressed flatter from front to back.</summary>
        private const float Depth = 0.8f;

        private static readonly Dictionary<BodyShape, Mesh> bodies = new Dictionary<BodyShape, Mesh>();
        private static Mesh? quad;
        private static Mesh? band;

        /// <summary>
        /// Where the eyes sit for a shape, in body units: x is each eye's distance from the middle, y
        /// the height of their centers, z their size.
        /// </summary>
        public static Vector4 EyeLayout(BodyShape shape)
        {
            switch (shape)
            {
                case BodyShape.Pill:
                    return new Vector4(0.37f, 0.06f, 1.1f, 0f);
                case BodyShape.Drop:
                    return new Vector4(0.28f, -0.03f, 1.1f, 0f);
                case BodyShape.Trefoil:
                    return new Vector4(0.31f, 0.16f, 1.15f, 0f);
                case BodyShape.Blossom:
                case BodyShape.Hexagon:
                    return new Vector4(0.3f, 0.1f, 1.15f, 0f);
                default:
                    return new Vector4(0.31f, 0.12f, 1.15f, 0f);
            }
        }

        public static Mesh Body(BodyShape shape)
        {
            if (bodies.TryGetValue(shape, out var cached) && cached != null) return cached;
            var vertices = new Vector3[Segments * (Rings - 1) + 2];
            var front = 0;
            var back = vertices.Length - 1;
            vertices[front] = new Vector3(0f, 0f, Depth);
            vertices[back] = new Vector3(0f, 0f, -Depth);
            for (var ring = 1; ring < Rings; ring++)
            {
                var polar = Mathf.PI * ring / Rings;
                for (var segment = 0; segment < Segments; segment++)
                {
                    var around = 2f * Mathf.PI * segment / Segments;
                    vertices[Index(ring, segment)] = Deform(shape, Mathf.Sin(polar), around, Mathf.Cos(polar));
                }
            }

            var triangles = new List<int>(Segments * (Rings - 1) * 6);
            for (var segment = 0; segment < Segments; segment++)
            {
                var next = (segment + 1) % Segments;
                // Clockwise as seen from outside, which Unity treats as the front.
                triangles.Add(front);
                triangles.Add(Index(1, segment));
                triangles.Add(Index(1, next));
                for (var ring = 1; ring < Rings - 1; ring++)
                {
                    triangles.Add(Index(ring, segment));
                    triangles.Add(Index(ring + 1, segment));
                    triangles.Add(Index(ring + 1, next));
                    triangles.Add(Index(ring, segment));
                    triangles.Add(Index(ring + 1, next));
                    triangles.Add(Index(ring, next));
                }
                triangles.Add(Index(Rings - 1, segment));
                triangles.Add(back);
                triangles.Add(Index(Rings - 1, next));
            }

            var mesh = new Mesh { name = "Halcyonic body " + shape };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            // Shared vertices all around, so the normals are smooth with no seam.
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            bodies[shape] = mesh;
            return mesh;
        }

        /// <summary>A unit quad in the XY plane, centered, with UVs from 0 to 1.</summary>
        public static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "Halcyonic quad" };
            quad.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
            });
            quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
            quad.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            quad.RecalculateBounds();
            return quad;
        }

        /// <summary>
        /// A flat band around the Z axis, 1.3 body units in radius. U runs along it from 0 to 1 and
        /// V across it, from the inside to the outside.
        /// </summary>
        public static Mesh Band()
        {
            if (band != null) return band;
            const int steps = 96;
            const float radius = 1.3f;
            const float halfWidth = 0.05f;
            var vertices = new Vector3[(steps + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[steps * 6];
            for (var step = 0; step <= steps; step++)
            {
                var angle = 2f * Mathf.PI * step / steps;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                var u = (float)step / steps;
                vertices[step * 2] = direction * (radius - halfWidth);
                vertices[step * 2 + 1] = direction * (radius + halfWidth);
                uvs[step * 2] = new Vector2(u, 0f);
                uvs[step * 2 + 1] = new Vector2(u, 1f);
                if (step == steps) continue;
                var t = step * 6;
                triangles[t] = step * 2;
                triangles[t + 1] = step * 2 + 1;
                triangles[t + 2] = step * 2 + 2;
                triangles[t + 3] = step * 2 + 1;
                triangles[t + 4] = step * 2 + 3;
                triangles[t + 5] = step * 2 + 2;
            }
            band = new Mesh { name = "Halcyonic band" };
            band.SetVertices(vertices);
            band.SetUVs(0, uvs);
            band.SetTriangles(triangles, 0);
            band.RecalculateBounds();
            return band;
        }

        private static int Index(int ring, int segment) => 1 + (ring - 1) * Segments + segment;

        /// <summary>A point of the unit sphere, reshaped in the front plane by the shape's outline.</summary>
        private static Vector3 Deform(BodyShape shape, float radial, float around, float z)
        {
            var x = radial * Mathf.Cos(around);
            var y = radial * Mathf.Sin(around);
            var scale = Outline(shape, around);
            x *= scale;
            y *= scale;
            switch (shape)
            {
                case BodyShape.Pill:
                    x *= 1.24f;
                    y *= 0.84f;
                    break;
                case BodyShape.Drop when y > 0f:
                    // Narrowing to a rounded point at the top.
                    x *= 1f - 0.42f * y * y;
                    y *= 1f + 0.22f * y;
                    break;
            }
            return new Vector3(x, y, z * Depth);
        }

        /// <summary>The outline's radius in a direction of the front plane, around 1.</summary>
        private static float Outline(BodyShape shape, float around)
        {
            switch (shape)
            {
                case BodyShape.Clover:
                    // Four lobes on the diagonals.
                    return 1f + 0.15f * Mathf.Cos(4f * (around - Mathf.PI / 4f));
                case BodyShape.Blossom:
                    // Five petals, one on top.
                    return 1f + 0.11f * Mathf.Cos(5f * (around - Mathf.PI / 2f));
                case BodyShape.Trefoil:
                    // Three lobes, one at the bottom: a rounded triangle standing on its point.
                    return 1f + 0.13f * Mathf.Cos(3f * (around + Mathf.PI / 2f));
                case BodyShape.Hexagon:
                    return RoundedHexagon(around);
                case BodyShape.Squircle:
                {
                    var c = Mathf.Abs(Mathf.Cos(around));
                    var s = Mathf.Abs(Mathf.Sin(around));
                    return 0.9f * Mathf.Pow(c * c * c * c + s * s * s * s, -0.25f);
                }
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// The distance to the outline of a hexagon with flat top and bottom and rounded corners, found
        /// by bisection on its signed distance, so the outline has no crease for the normals to show.
        /// </summary>
        private static float RoundedHexagon(float around)
        {
            var direction = new Vector2(Mathf.Cos(around), Mathf.Sin(around));
            var inside = 0f;
            var outside = 2f;
            for (var step = 0; step < 24; step++)
            {
                var middle = (inside + outside) / 2f;
                if (HexagonDistance(direction * middle, 0.6f) - 0.32f < 0f) inside = middle;
                else outside = middle;
            }
            return (inside + outside) / 2f;
        }

        /// <summary>The signed distance to a hexagon whose flat sides are <paramref name="apothem"/> from the center.</summary>
        private static float HexagonDistance(Vector2 p, float apothem)
        {
            var k = new Vector3(-0.8660254f, 0.5f, 0.57735027f);
            p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
            var fold = 2f * Mathf.Min(k.x * p.x + k.y * p.y, 0f);
            p -= new Vector2(fold * k.x, fold * k.y);
            p -= new Vector2(Mathf.Clamp(p.x, -k.z * apothem, k.z * apothem), apothem);
            return p.magnitude * Mathf.Sign(p.y);
        }

        /// <summary>Forgets cached meshes when play mode starts without a domain reload.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            bodies.Clear();
            quad = null;
            band = null;
        }
    }
}
