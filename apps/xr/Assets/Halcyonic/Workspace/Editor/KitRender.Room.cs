#nullable enable
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class KitRender
    {
        /// <summary>
        /// A synthetic bright room as passthrough would show it behind the instrument, built here from plain flat
        /// shapes, never from a photograph, and fixed to the world as passthrough is: every pixel follows its own ray
        /// from the eyes into a small room of planes, so straight edges stay straight and the room stays where it is
        /// whichever way the head turns. A pale front wall 2.4 m away lit by a window at the right, a side wall past a
        /// corner at the left, a framed print of flat colour blocks, and a desk 0.75 m high with a monitor 0.78 m away
        /// showing a light document, ahead and to the right: the kind of other work compact presence sits beside.
        /// Values are sRGB as a camera would record them; the brightest is the window, near white.
        /// </summary>
        private static Color[] BrightRoom(int width, int height, Camera camera)
        {
            var pixels = new Color[width * height];
            var random = new System.Random(20261016);
            var halfHeight = Mathf.Tan(camera.fieldOfView / 2f * Mathf.Deg2Rad);
            var halfWidth = halfHeight * camera.aspect;
            var rotation = camera.transform.rotation;
            for (var y = 0; y < height; y++)
            {
                // Texture rows run bottom up, as the render's do.
                var up = ((y + 0.5f) / height * 2f - 1f) * halfHeight;
                for (var x = 0; x < width; x++)
                {
                    var across = ((x + 0.5f) / width * 2f - 1f) * halfWidth;
                    pixels[y * width + x] = RoomAlong(rotation * new Vector3(across, up, 1f), random);
                }
            }
            return pixels;
        }

        // The room, in metres from the eyes: x right, y up, z ahead.
        private const float FrontWall = 2.4f;
        private const float SideWall = -1.7f;
        private const float Floor = -1.2f;
        private const float Ceiling = 1.3f;
        private const float DeskTop = -0.45f;
        private const float DeskNear = 0.28f;
        private const float DeskFar = 0.95f;
        private const float Screen = 0.78f;

        /// <summary>What the eyes see along <paramref name="ray"/>: the nearest of the room's planes it meets.</summary>
        private static Color RoomAlong(Vector3 ray, System.Random random)
        {
            var nearest = float.MaxValue;
            var colour = new Color(0.85f, 0.84f, 0.80f);
            bool Hit(float t) { if (t <= 0f || t >= nearest) return false; nearest = t; return true; }
            if (ray.z > 1e-5f)
            {
                // The monitor, standing on the desk ahead and to the right.
                var t = Screen / ray.z;
                var p = ray * t;
                if (p.x >= 0.04f && p.x <= 0.62f && p.y >= -0.43f && p.y <= -0.07f && Hit(t)) colour = MonitorAt(p.x, p.y);
                // The front wall, with its window and its print.
                t = FrontWall / ray.z;
                p = ray * t;
                if (p.x >= SideWall && p.y >= Floor && p.y <= Ceiling && Hit(t)) colour = WallAt(p.x, p.y);
            }
            if (ray.y < -1e-5f)
            {
                // The desk's top and its front edge, then the floor.
                var t = DeskTop / ray.y;
                var p = ray * t;
                if (p.z >= DeskNear && p.z <= DeskFar && Mathf.Abs(p.x) <= 1.25f && Hit(t)) colour = new Color(0.60f, 0.46f, 0.34f);
                if (ray.z > 1e-5f)
                {
                    var front = DeskNear / ray.z;
                    var q = ray * front;
                    if (q.y <= DeskTop && q.y >= DeskTop - 0.04f && Mathf.Abs(q.x) <= 1.25f && Hit(front)) colour = new Color(0.40f, 0.30f, 0.22f);
                }
                t = Floor / ray.y;
                if (Hit(t)) colour = new Color(0.52f, 0.47f, 0.42f);
            }
            if (ray.y > 1e-5f && Hit(Ceiling / ray.y)) colour = new Color(0.93f, 0.92f, 0.90f);
            if (ray.x < -1e-5f)
            {
                var t = SideWall / ray.x;
                var p = ray * t;
                if (p.y >= Floor && p.y <= Ceiling && Hit(t)) colour = new Color(0.72f, 0.71f, 0.68f) * Mathf.Lerp(0.95f, 1.05f, Mathf.Clamp01(p.z / FrontWall));
            }
            // A little grain, as a camera records, so nothing bands.
            var grain = ((float)random.NextDouble() - 0.5f) * 0.012f;
            return new Color(Mathf.Clamp01(colour.r + grain), Mathf.Clamp01(colour.g + grain), Mathf.Clamp01(colour.b + grain), 1f);
        }

        /// <summary>The front wall at a point on it: lighter toward its window, the window itself near white, a framed print at the left.</summary>
        private static Color WallAt(float x, float y)
        {
            var light = Mathf.Lerp(0.80f, 0.90f, Mathf.InverseLerp(SideWall, 1.9f, x)) + Mathf.Lerp(-0.02f, 0.03f, Mathf.InverseLerp(Floor, Ceiling, y));
            var colour = new Color(light, light * 0.985f, light * 0.95f);
            if (Inside(x, y, 0.95f, 1.05f, 1.95f, -0.25f))
            {
                var frame = !Inside(x, y, 1.0f, 1.0f, 1.9f, -0.2f);
                var bar = Mathf.Abs(x - 1.45f) < 0.02f || Mathf.Abs(y - 0.40f) < 0.02f;
                colour = frame || bar ? new Color(0.86f, 0.85f, 0.82f) : new Color(0.965f, 0.975f, 0.99f);
            }
            if (Inside(x, y, -1.35f, 0.95f, -0.45f, 0.0f))
            {
                if (!Inside(x, y, -1.31f, 0.91f, -0.49f, 0.04f)) colour = new Color(0.22f, 0.20f, 0.18f);
                else if (x < -1.05f) colour = y > 0.45f ? new Color(0.16f, 0.24f, 0.56f) : new Color(0.36f, 0.24f, 0.55f);
                else if (x < -0.77f) colour = y > 0.62f ? new Color(0.90f, 0.90f, 0.92f) : new Color(0.75f, 0.20f, 0.18f);
                else colour = y > 0.30f ? new Color(0.13f, 0.30f, 0.62f) : new Color(0.90f, 0.74f, 0.24f);
            }
            return colour;
        }

        /// <summary>The monitor at a point on it: its bezel, a dark screen, and a light document window with lines of text.</summary>
        private static Color MonitorAt(float x, float y)
        {
            if (!Inside(x, y, 0.06f, -0.09f, 0.60f, -0.38f)) return new Color(0.09f, 0.095f, 0.11f);
            if (!Inside(x, y, 0.14f, -0.12f, 0.52f, -0.35f)) return new Color(0.12f, 0.135f, 0.165f);
            var line = (-0.14f - y) / 0.018f;
            if (y < -0.14f && y > -0.33f && line - Mathf.Floor(line) < 0.45f && x < 0.17f + 0.30f * (0.6f + 0.4f * Mathf.Sin(line * 1.7f)))
                return new Color(0.62f, 0.65f, 0.70f);
            return new Color(0.84f, 0.86f, 0.89f);
        }

        /// <summary>Whether a point falls inside a box given by its left, top, right and bottom.</summary>
        private static bool Inside(float x, float y, float left, float top, float right, float bottom) => x >= left && x <= right && y <= top && y >= bottom;

        /// <summary>The dark background: a night room, as the earlier renders use.</summary>
        private static readonly Color DarkRoom = new Color(0.02f, 0.03f, 0.04f, 1f);

        /// <summary>
        /// The eye buffer laid over the room as the compositor lays an app's layer over passthrough: the render's
        /// colours are premultiplied and stored as sRGB, so each pixel, read back to linear light, adds the room's light
        /// by what of it the pixel lets through. Blended in linear light, as the project blends.
        /// </summary>
        private static Texture2D Composite(Texture2D eye, Color[]? room, Color flat)
        {
            var result = new Texture2D(eye.width, eye.height, TextureFormat.RGBA32, false);
            var pixels = eye.GetPixels();
            var flatLinear = flat.linear;
            for (var index = 0; index < pixels.Length; index++)
            {
                var pixel = pixels[index].linear;
                var under = room != null ? room[index].linear : flatLinear;
                pixels[index] = new Color(pixel.r + under.r * (1f - pixel.a), pixel.g + under.g * (1f - pixel.a), pixel.b + under.b * (1f - pixel.a), 1f).gamma;
            }
            result.SetPixels(pixels);
            result.Apply();
            return result;
        }
    }
}
