#nullable enable
using System;
using System.Collections.Generic;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Procedural meshes for <see cref="KitFigure"/> (the ROBOT6 porcelain ovoid companion): an inverted-teardrop torso
    /// with a curved upper saddle collar well (#141C26), a recessed waist seam groove (#0B0E13) and glowing belt, two
    /// detached tapered capsule-wing arms with an 11 mm air gap, a floating rounded-conical dome head with a recessed
    /// gloss-black front oval visor (#030406), tilted oval LED scanline eyes (or upward happy arcs), and a 0.44 m floor
    /// quad for the 3-layer spatial ground projection. Built once per detail level and shared across instances.
    /// </summary>
    public static class KitFigureMeshes
    {
        public enum Detail
        {
            Near = 0,
            Far = 1,
        }

        public const int NearBudget = 2400, FarBudget = 600;
        public const float BodyWidthM = 0.132f, BodyHeightM = 0.156f, HeadWidthM = 0.140f, HeadHeightM = 0.098f, HeadY = 0.192f;
        public const float ArmWidthM = 0.032f, ArmHeightM = 0.111f, ArmGapM = 0.011f, ArmCantDegrees = 13.5f;
        public const float EyeWidthM = 0.025f, EyeHeightM = 0.017f, EyeAcrossM = 0.022f, EyeUpM = -0.001f, EyeForwardM = -0.060f, EyeTiltDegrees = 7f;
        public const float PoolAcrossM = 0.184f, PoolBelowM = 0.095f;

        private const float ScaleM = 0.38f, YOffsetProto = 0.265f;

        private static readonly Dictionary<Detail, Mesh> bodies = new Dictionary<Detail, Mesh>();
        private static readonly Dictionary<(KitFigureFeature, Detail), Mesh> heads = new Dictionary<(KitFigureFeature, Detail), Mesh>();
        private static Mesh? openEye, happyEye, pool;

        public static Mesh Body(Detail detail)
        {
            if (bodies.TryGetValue(detail, out var cached) && cached != null) return cached;
            var mesh = BuildBody(detail);
            bodies[detail] = mesh;
            return mesh;
        }

        public static Mesh Head(KitFigureFeature feature, Detail detail)
        {
            if (heads.TryGetValue((feature, detail), out var cached) && cached != null) return cached;
            var mesh = BuildHead(feature, detail);
            heads[(feature, detail)] = mesh;
            return mesh;
        }

        public static Mesh Eye(bool happy) => happy ? (happyEye != null ? happyEye : happyEye = BuildEye(true))
            : (openEye != null ? openEye : openEye = BuildEye(false));

        public static Mesh Pool() => pool != null ? pool : pool = BuildPool();

        private readonly struct ProfilePoint
        {
            public readonly float R;
            public readonly float Y;
            public readonly float Collar;
            public readonly float Belt;

            public ProfilePoint(float r, float y, float collar, float belt)
            {
                R = r;
                Y = y;
                Collar = collar;
                Belt = belt;
            }
        }

        private static readonly ProfilePoint[] NearProfile =
        {
            new ProfilePoint(0.000f, -0.265f, 0.00f, 0.00f),
            new ProfilePoint(0.036f, -0.254f, 0.00f, 0.00f),
            new ProfilePoint(0.072f, -0.220f, 0.00f, 0.00f),
            new ProfilePoint(0.112f, -0.152f, 0.00f, 0.00f),
            new ProfilePoint(0.140f, -0.080f, 0.00f, 0.00f),
            new ProfilePoint(0.149f, -0.039f, 0.00f, 0.00f),
            new ProfilePoint(0.150f, -0.034f, 0.00f, 0.35f),
            new ProfilePoint(0.151f, -0.031f, 0.00f, 1.00f),
            new ProfilePoint(0.152f, -0.024f, 0.00f, 1.00f),
            new ProfilePoint(0.153f, -0.021f, 0.00f, 0.35f),
            new ProfilePoint(0.154f, -0.016f, 0.00f, 0.00f),
            new ProfilePoint(0.165f,  0.030f, 0.00f, 0.00f),
            new ProfilePoint(0.174f,  0.082f, 0.00f, 0.00f),
            new ProfilePoint(0.174f,  0.106f, 0.00f, 0.00f),
            new ProfilePoint(0.170f,  0.115f, 0.00f, 0.00f),
            new ProfilePoint(0.162f,  0.112f, 0.18f, 0.00f),
            new ProfilePoint(0.130f,  0.090f, 0.52f, 0.00f),
            new ProfilePoint(0.072f,  0.052f, 0.85f, 0.00f),
            new ProfilePoint(0.000f,  0.038f, 1.00f, 0.00f),
        };

        private static readonly ProfilePoint[] FarProfile =
        {
            new ProfilePoint(0.000f, -0.265f, 0.00f, 0.00f),
            new ProfilePoint(0.056f, -0.236f, 0.00f, 0.00f),
            new ProfilePoint(0.118f, -0.132f, 0.00f, 0.00f),
            new ProfilePoint(0.150f, -0.036f, 0.00f, 0.35f),
            new ProfilePoint(0.152f, -0.026f, 0.00f, 1.00f),
            new ProfilePoint(0.154f, -0.016f, 0.00f, 0.00f),
            new ProfilePoint(0.174f,  0.082f, 0.00f, 0.00f),
            new ProfilePoint(0.170f,  0.115f, 0.00f, 0.00f),
            new ProfilePoint(0.130f,  0.090f, 0.52f, 0.00f),
            new ProfilePoint(0.000f,  0.038f, 1.00f, 0.00f),
        };

        private static void AddTriangleOutward(List<int> indices, List<Vector3> vertices, List<Vector3> normals, int i0, int i1, int i2)
        {
            var faceNormal = Vector3.Cross(vertices[i1] - vertices[i0], vertices[i2] - vertices[i0]);
            var avgNormal = normals[i0] + normals[i1] + normals[i2];
            if (Vector3.Dot(faceNormal, avgNormal) >= 0f)
            {
                indices.Add(i0);
                indices.Add(i1);
                indices.Add(i2);
            }
            else
            {
                indices.Add(i0);
                indices.Add(i2);
                indices.Add(i1);
            }
        }

        private static Mesh BuildBody(Detail detail)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            var profile = detail == Detail.Near ? NearProfile : FarProfile;
            var around = detail == Detail.Near ? 28 : 12;
            AppendTorsoLathe(vertices, normals, colors, uvs, indices, profile, around);

            var armStacks = detail == Detail.Near ? 10 : 4;
            var armSlices = detail == Detail.Near ? 14 : 8;
            AppendCapsuleWingArm(vertices, normals, colors, uvs, indices, -1f, armStacks, armSlices);
            AppendCapsuleWingArm(vertices, normals, colors, uvs, indices,  1f, armStacks, armSlices);

            var mesh = new Mesh { name = "KitFigureBody-" + detail };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AppendTorsoLathe(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color> colors,
            List<Vector2> uvs,
            List<int> indices,
            ProfilePoint[] profile,
            int around)
        {
            var baseVertex = vertices.Count;
            var rows = profile.Length;
            var cols = around + 1;

            for (var r = 0; r < rows; r++)
            {
                var pt = profile[r];
                var prev = profile[Mathf.Max(0, r - 1)];
                var next = profile[Mathf.Min(rows - 1, r + 1)];
                var tangentR = next.R - prev.R;
                var tangentY = next.Y - prev.Y;

                var uSaddle = Mathf.Clamp01((pt.Y - (-0.01f)) / (0.085f - (-0.01f)));
                var wY = uSaddle * uSaddle * (3f - 2f * uSaddle);
                var rFade = Mathf.Min(1f, pt.R / 0.035f);

                for (var s = 0; s <= around; s++)
                {
                    var u = (float)s / around;
                    var phi = u * Mathf.PI * 2f;
                    var cosPhi = Mathf.Cos(phi);
                    var sinPhi = Mathf.Sin(phi);

                    var negCos2Phi = cosPhi * cosPhi - sinPhi * sinPhi;
                    var dyProto = 0.026f * negCos2Phi * wY * rFade;

                    var x = pt.R * cosPhi * ScaleM;
                    var z = pt.R * sinPhi * ScaleM;
                    var y = (pt.Y + YOffsetProto + dyProto) * ScaleM;
                    vertices.Add(new Vector3(x, y, z));

                    Vector3 normal;
                    if (r == 0) normal = Vector3.down;
                    else if (r == rows - 1) normal = Vector3.up;
                    else
                    {
                        var nr = tangentY;
                        var ny = -tangentR;
                        normal = new Vector3(nr * cosPhi, ny, nr * sinPhi).normalized;
                    }
                    normals.Add(normal);
                    colors.Add(new Color(1f, pt.Collar, pt.Belt, 0f));
                    uvs.Add(new Vector2(u, pt.Belt));
                }
            }

            // Bottom cap fan (row 0 to row 1)
            for (var s = 0; s < around; s++)
            {
                var c0 = baseVertex + s;
                var n0 = baseVertex + cols + s;
                var n1 = baseVertex + cols + s + 1;
                indices.Add(c0);
                indices.Add(n1);
                indices.Add(n0);
            }

            // Middle quad strips (row 1 to row rows - 2)
            for (var r = 1; r < rows - 2; r++)
            {
                var rowA = baseVertex + r * cols;
                var rowB = baseVertex + (r + 1) * cols;
                for (var s = 0; s < around; s++)
                {
                    var a = rowA + s;
                    var b = rowA + s + 1;
                    var c = rowB + s;
                    var d = rowB + s + 1;
                    indices.Add(a);
                    indices.Add(c);
                    indices.Add(d);
                    indices.Add(a);
                    indices.Add(d);
                    indices.Add(b);
                }
            }

            // Top inner collar bowl fan (row rows - 2 to row rows - 1)
            var lastStrip = baseVertex + (rows - 2) * cols;
            var topCap = baseVertex + (rows - 1) * cols;
            for (var s = 0; s < around; s++)
            {
                indices.Add(lastStrip + s);
                indices.Add(topCap + s);
                indices.Add(lastStrip + s + 1);
            }
        }

        private static void AppendCapsuleWingArm(
            List<Vector3> vertices,
            List<Vector3> normals,
            List<Color> colors,
            List<Vector2> uvs,
            List<int> indices,
            float side,
            int stacks,
            int slices)
        {
            var baseVertex = vertices.Count;
            var halfSpan = 0.146f * ScaleM;
            var halfThick = 0.032f * ScaleM;
            var halfChord = 0.052f * ScaleM;

            // Positioned with an 11 mm air gap outside the torso, banked (+0.18 rad) and yawed (-0.62 rad) to contour along the V-taper.
            var center = new Vector3(side * 0.086f, 0.084f, -0.006f);
            var rotation = Quaternion.Euler(-4.5f, side * -35.5f, side * -ArmCantDegrees);

            var topIndex = baseVertex;
            var topLocal = new Vector3(0f, halfSpan, 0f);
            vertices.Add(center + rotation * topLocal);
            normals.Add(rotation * Vector3.up);
            colors.Add(new Color(1f, 0f, 0f, 0.5f));
            uvs.Add(new Vector2(0f, 0f));

            for (var i = 1; i < stacks; i++)
            {
                // Cosine vertical spacing clusters rings around the rounded wing tips for seamless shading.
                var t = 0.5f * (1f - Mathf.Cos((float)i / stacks * Mathf.PI));
                var y0 = 1f - 2f * t;
                var env = Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Pow(Mathf.Abs(2f * t - 1f), 3.2f)), 0.42f);
                var rx = halfThick * env * (1.04f - 0.18f * t);
                var rz = halfChord * env * (1.06f - 0.26f * t);
                var camber = -side * (0.016f * ScaleM) * Mathf.Sin(Mathf.PI * t);
                var sweep = -(0.010f * ScaleM) * t * (1f - 0.3f * t);

                for (var j = 0; j < slices; j++)
                {
                    var theta = (float)j / slices * Mathf.PI * 2f;
                    var cosT = Mathf.Cos(theta);
                    var sinT = Mathf.Sin(theta);

                    var lx = rx * cosT + camber;
                    var ly = y0 * halfSpan;
                    var lz = rz * sinT + sweep;

                    var tipWeight = y0 * y0 * y0;
                    var nLocal = new Vector3(cosT * 0.88f, Mathf.Max(-0.18f, tipWeight * 0.55f), sinT * 0.72f - 0.18f).normalized;

                    vertices.Add(center + rotation * new Vector3(lx, ly, lz));
                    normals.Add(rotation * nLocal);
                    colors.Add(new Color(1f, 0f, 0f, 0.5f));
                    uvs.Add(new Vector2(t, side * cosT));
                }
            }

            var bottomIndex = vertices.Count;
            var bottomLocal = new Vector3(0f, -halfSpan, -0.0026f);
            vertices.Add(center + rotation * bottomLocal);
            normals.Add(rotation * new Vector3(0f, -0.22f, -0.97f).normalized);
            colors.Add(new Color(1f, 0f, 0f, 0.5f));
            uvs.Add(new Vector2(1f, 0f));

            var firstRing = baseVertex + 1;
            for (var j = 0; j < slices; j++)
            {
                var next = (j + 1) % slices;
                AddTriangleOutward(indices, vertices, normals, topIndex, firstRing + next, firstRing + j);
            }

            for (var i = 0; i < stacks - 2; i++)
            {
                var ringA = firstRing + i * slices;
                var ringB = firstRing + (i + 1) * slices;
                for (var j = 0; j < slices; j++)
                {
                    var next = (j + 1) % slices;
                    var a = ringA + j;
                    var b = ringA + next;
                    var c = ringB + j;
                    var d = ringB + next;
                    AddTriangleOutward(indices, vertices, normals, a, b, d);
                    AddTriangleOutward(indices, vertices, normals, a, d, c);
                }
            }

            var lastRing = firstRing + (stacks - 2) * slices;
            for (var j = 0; j < slices; j++)
            {
                var next = (j + 1) % slices;
                AddTriangleOutward(indices, vertices, normals, lastRing + j, lastRing + next, bottomIndex);
            }
        }

        private static Mesh BuildHead(KitFigureFeature feature, Detail detail)
        {
            _ = feature;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();

            var uSteps = detail == Detail.Near ? 26 : 12;
            var vSteps = detail == Detail.Near ? 14 : 7;
            var cols = uSteps + 1;

            const float rx = HeadWidthM * 0.5f;   // 0.070 m (0.140 m wide)
            const float ry = 0.0620f;             // (1 + 0.58) * 0.0620 = 0.098 m tall
            const float rz = 0.0640f;             // 0.128 m deep
            const float bottomScale = 0.58f;
            const float recessDepth = 0.0055f;

            for (var iv = 0; iv <= vSteps; iv++)
            {
                var vt = (float)iv / vSteps;
                var lat = (vt - 0.5f) * Mathf.PI;
                var sinLat = Mathf.Sin(lat);
                var cosLat = Mathf.Cos(lat);

                for (var iu = 0; iu <= uSteps; iu++)
                {
                    // Seam at +Z (back of head) so u = atan2(sx, -sz) is continuous across the entire front (-Z) visor.
                    var ut = (float)iu / uSteps;
                    var lon = (ut - 0.5f) * Mathf.PI * 2f;
                    var nx = Mathf.Sin(lon) * cosLat;
                    var ny = sinLat;
                    var nz = -Mathf.Cos(lon) * cosLat;

                    var syUnit = ny < 0f ? ny * bottomScale : ny;
                    var sx = nx * rx;
                    var sy = syUnit * ry;
                    var sz = nz * rz;
                    var len = Mathf.Sqrt(sx * sx + sy * sy + sz * sz);

                    var uAngle = Mathf.Atan2(sx, -sz);
                    var vAngle = Mathf.Asin(Mathf.Clamp(sy / Mathf.Max(len, 1e-5f), -1f, 1f));
                    var vVis = vAngle - 0.02f - 0.04f * uAngle * uAngle;

                    var ax = Mathf.Abs(uAngle) / 0.76f;
                    var ay = Mathf.Abs(vVis) / 0.55f;
                    var fe = Mathf.Pow(Mathf.Pow(Mathf.Max(ax, 1e-4f), 2.15f) + Mathf.Pow(Mathf.Max(ay, 1e-4f), 2.15f), 1f / 2.15f);

                    var recess = nz < 0f ? recessDepth * SmoothStep(1.00f, 0.84f, fe) : 0f;
                    var ryNormal = Mathf.Lerp(ry * 0.82f, ry, SmoothStep(-0.70f, 0.05f, ny));
                    var nOut = new Vector3(nx / rx, ny / ryNormal, nz / rz).normalized;

                    var pos = new Vector3(sx, sy, sz) - nOut * recess;
                    if (nz < 0f && fe > 0.84f && fe < 1.02f)
                    {
                        var bezelDir = new Vector3(nx, ny * 0.8f, 0f).normalized;
                        nOut = Vector3.Lerp(nOut, bezelDir, 0.28f).normalized;
                    }

                    vertices.Add(pos);
                    normals.Add(nOut);
                    colors.Add(new Color(0f, 0f, 0f, 0f));
                    uvs.Add(new Vector2(uAngle, vAngle));
                }
            }

            // Bottom cap fan (row 0 to row 1)
            for (var iu = 0; iu < uSteps; iu++)
            {
                var c0 = iu;
                var n0 = cols + iu;
                var n1 = cols + iu + 1;
                AddTriangleOutward(indices, vertices, normals, c0, n0, n1);
            }

            // Middle quad strips (row 1 to vSteps - 1)
            for (var iv = 1; iv < vSteps - 1; iv++)
            {
                var rowA = iv * cols;
                var rowB = (iv + 1) * cols;
                for (var iu = 0; iu < uSteps; iu++)
                {
                    var a = rowA + iu;
                    var b = rowA + iu + 1;
                    var c = rowB + iu;
                    var d = rowB + iu + 1;
                    AddTriangleOutward(indices, vertices, normals, a, b, d);
                    AddTriangleOutward(indices, vertices, normals, a, d, c);
                }
            }

            // Top cap fan (row vSteps - 1 to vSteps)
            var lastRow = (vSteps - 1) * cols;
            var topRow = vSteps * cols;
            for (var iu = 0; iu < uSteps; iu++)
            {
                AddTriangleOutward(indices, vertices, normals, lastRow + iu, lastRow + iu + 1, topRow + iu);
            }

            var mesh = new Mesh { name = "KitFigureHead-" + feature + "-" + detail };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            var t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static Mesh BuildEye(bool happy)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>();
            var indices = new List<int>();

            void Add(Mesh part, Matrix4x4 transform, float kind)
            {
                var start = vertices.Count;
                var pv = part.vertices;
                var puv = part.uv;
                var pt = part.triangles;
                var color = new Color(kind * 0.5f, 0f, 0f, 1f);
                for (var i = 0; i < pv.Length; i++)
                {
                    vertices.Add(transform.MultiplyPoint3x4(pv[i]));
                    uvs.Add(puv.Length > i ? puv[i] : new Vector2(0.5f, 0.5f));
                    colors.Add(color);
                }
                for (var i = 0; i < pt.Length; i++) indices.Add(start + pt[i]);
                UnityEngine.Object.DestroyImmediate(part);
            }

            Add(Quad(), Matrix4x4.TRS(new Vector3(0f, 0f, 0.0006f), Quaternion.identity, new Vector3(1.85f, 1.85f, 1f)), 1f);
            if (happy)
                Add(HalfRing(0.54f, 0.18f, 16), Matrix4x4.Translate(new Vector3(0f, -0.36f, 0f)), 0f);
            else
                Add(SuperellipseDisc(0.5f, 20, 2.3f), Matrix4x4.identity, 0f);

            var mesh = new Mesh { name = happy ? "KitFigureEyeHappy" : "KitFigureEyeOpen" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildPool()
        {
            var mesh = Quad();
            mesh.name = "KitFigurePool";
            var vertices = mesh.vertices;
            var colors = new Color[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = new Vector3(vertices[i].x * PoolAcrossM, 0f, vertices[i].y * PoolAcrossM);
                colors[i] = new Color(1f, 0f, 0f, 1f);
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh Quad()
        {
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            return mesh;
        }

        private static Mesh SuperellipseDisc(float radius, int steps, float exponent)
        {
            var vertices = new Vector3[steps + 1];
            var uvs = new Vector2[steps + 1];
            var indices = new int[steps * 3];
            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (var i = 0; i < steps; i++)
            {
                var a = (float)i / steps * Mathf.PI * 2f;
                var cosA = Mathf.Cos(a);
                var sinA = Mathf.Sin(a);
                var rScale = Mathf.Pow(Mathf.Pow(Mathf.Abs(cosA), exponent) + Mathf.Pow(Mathf.Abs(sinA), exponent), -1f / exponent);
                var x = cosA * radius * rScale;
                var y = sinA * radius * rScale;
                vertices[i + 1] = new Vector3(x, y, 0f);
                uvs[i + 1] = new Vector2(0.5f + x / (2f * radius), 0.5f + y / (2f * radius));
                var next = i + 1 < steps ? i + 2 : 1;
                indices[i * 3] = 0;
                indices[i * 3 + 1] = next;
                indices[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = indices;
            return mesh;
        }

        private static Mesh HalfRing(float outer, float inner, int steps)
        {
            var vertices = new Vector3[(steps + 1) * 2];
            var uvs = new Vector2[vertices.Length];
            var indices = new int[steps * 6];
            for (var i = 0; i <= steps; i++)
            {
                var t = (float)i / steps;
                var a = t * Mathf.PI;
                var c = Mathf.Cos(a);
                var s = Mathf.Sin(a);
                vertices[i * 2] = new Vector3(c * inner, s * inner, 0f);
                vertices[i * 2 + 1] = new Vector3(c * outer, s * outer, 0f);
                // Keep uv.y = 0.5 along the happy arc so its centre holds the exact state sRGB tone.
                uvs[i * 2] = new Vector2(t, 0.5f);
                uvs[i * 2 + 1] = new Vector2(t, 0.5f);
                if (i == steps) continue;
                var o = i * 6;
                var v = i * 2;
                indices[o] = v;
                indices[o + 1] = v + 1;
                indices[o + 2] = v + 3;
                indices[o + 3] = v;
                indices[o + 4] = v + 3;
                indices[o + 5] = v + 2;
            }
            var mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = indices;
            return mesh;
        }
    }
}
