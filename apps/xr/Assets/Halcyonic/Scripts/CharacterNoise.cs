#nullable enable
using UnityEngine;

namespace Halcyonic.XR
{
    /// <summary>
    /// Bakes the tileable noise the character shader reads, once at startup, so the shader samples
    /// a texture instead of computing fractal noise for every pixel. Everything is generated here
    /// from fixed seeds; no image asset is involved.
    /// </summary>
    /// <remarks>
    /// Channels: red and green are smooth fields that warp the satin flow; blue is three octaves of
    /// value noise, for the flow's bands and the fog; alpha is the distance between the two nearest
    /// points of a Voronoi pattern, times three, whose small values trace the cracks. Every channel
    /// repeats at the texture's edges.
    /// </remarks>
    internal static class CharacterNoise
    {
        public const int Size = 128;

        public static Texture2D Create()
        {
            var pixels = new Color32[Size * Size];
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var u = (float)x / Size;
                    var v = (float)y / Size;
                    var warpX = Fractal(u, v, 4, 2, 11u);
                    var warpY = Fractal(u, v, 4, 2, 23u);
                    var field = Fractal(u, v, 4, 3, 37u);
                    var crack = Mathf.Clamp01(CellEdge(u, v, 5, 53u) * 3f);
                    pixels[y * Size + x] = new Color32(Byte(warpX), Byte(warpY), Byte(field), Byte(crack));
                }
            }
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true)
            {
                name = "Halcyonic character noise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 0,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static byte Byte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        /// <summary>Octaves of value noise with a lattice of <paramref name="period"/> cells per tile, normalized to 0..1.</summary>
        private static float Fractal(float u, float v, int period, int octaves, uint seed)
        {
            var sum = 0f;
            var weight = 0f;
            var amplitude = 0.5f;
            for (var octave = 0; octave < octaves; octave++)
            {
                sum += amplitude * ValueNoise(u * period, v * period, period, seed + (uint)octave * 101u);
                weight += amplitude;
                period *= 2;
                amplitude *= 0.5f;
            }
            return sum / weight;
        }

        private static float ValueNoise(float x, float y, int period, uint seed)
        {
            var x0 = Mathf.FloorToInt(x);
            var y0 = Mathf.FloorToInt(y);
            var fx = Smooth(x - x0);
            var fy = Smooth(y - y0);
            var a = Lattice(x0, y0, period, seed);
            var b = Lattice(x0 + 1, y0, period, seed);
            var c = Lattice(x0, y0 + 1, period, seed);
            var d = Lattice(x0 + 1, y0 + 1, period, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        private static float Lattice(int x, int y, int period, uint seed) => Unit(Hash(Wrap(x, period), Wrap(y, period), seed));

        /// <summary>The distance to the second nearest minus the distance to the nearest of one jittered point per cell.</summary>
        private static float CellEdge(float u, float v, int cells, uint seed)
        {
            var x = u * cells;
            var y = v * cells;
            var cx = Mathf.FloorToInt(x);
            var cy = Mathf.FloorToInt(y);
            var nearest = float.MaxValue;
            var second = float.MaxValue;
            for (var j = -1; j <= 1; j++)
            {
                for (var i = -1; i <= 1; i++)
                {
                    var wx = Wrap(cx + i, cells);
                    var wy = Wrap(cy + j, cells);
                    var px = cx + i + Unit(Hash(wx, wy, seed));
                    var py = cy + j + Unit(Hash(wx, wy, seed ^ 0x9e3779b9u));
                    var dx = px - x;
                    var dy = py - y;
                    var distance = dx * dx + dy * dy;
                    if (distance < nearest)
                    {
                        second = nearest;
                        nearest = distance;
                    }
                    else if (distance < second)
                    {
                        second = distance;
                    }
                }
            }
            return Mathf.Sqrt(second) - Mathf.Sqrt(nearest);
        }

        private static int Wrap(int value, int period) => ((value % period) + period) % period;

        private static float Unit(uint hash) => (hash & 0xffffffu) / 16777216f;

        private static uint Hash(int x, int y, uint seed)
        {
            unchecked
            {
                var h = (uint)x * 0x8da6b343u ^ (uint)y * 0xd8163841u ^ seed * 0xcb1ab31fu;
                h ^= h >> 15;
                h *= 0x2c1b3c6du;
                h ^= h >> 12;
                h *= 0x297a2d39u;
                h ^= h >> 15;
                return h;
            }
        }
    }
}
