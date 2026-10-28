using System;
using System.Numerics;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

[TestFixture]
public class PlaneCurveTests
{
    private const float Distance = 0.46f;
    private static readonly PlaneCurve Curve = new(Distance);

    // atan2 of the cross and dot products: acos loses about 0.02 degrees in single precision at a degree.
    private static float Degrees(Vector3 a, Vector3 b) => MathF.Atan2(Vector3.Cross(a, b).Length(), Vector3.Dot(a, b)) * 180f / MathF.PI;

    /// <summary>A vertical em centred at a point of the flat plane, as the eyes see it: the plane stands <paramref name="z"/> beyond the forward distance.</summary>
    private static float PlaneTall(float x, float y, float z, float em) =>
        Degrees(new Vector3(x, y + em / 2f, Distance + z), new Vector3(x, y - em / 2f, Distance + z));

    [Test]
    public void APointLaidOutComesBackFromWhereTheCurveDrawsIt()
    {
        foreach (var point in new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(0.27f, 0.16f, 0.01f), new Vector3(-0.27f, -0.2f, 0.01f),
            new Vector3(0.1f, 0.05f, -0.004f), new Vector3(-1.2f, 0.3f, 0.02f),
        })
        {
            var back = Curve.Unmap(Curve.Map(point));
            Assert.That(Vector3.Distance(back, point), Is.LessThan(1e-5f), point.ToString());
        }
    }

    [Test]
    public void TheMappingIsTheInteractionSdksCylinderBend()
    {
        // CanvasCylinder.MeshTransform and MeshInverseTransform, measured from the cylinder's front point.
        foreach (var (x, y) in new[] { (0.27f, 0.16f), (-0.13f, -0.08f), (0.4f, 0f) })
        {
            var front = Curve.Map(new Vector3(x, y, 0f)) - new Vector3(0f, 0f, Distance);
            Assert.That(front.X, Is.EqualTo(MathF.Sin(x / Distance) * Distance).Within(1e-6f));
            Assert.That(front.Y, Is.EqualTo(y));
            Assert.That(front.Z, Is.EqualTo(MathF.Cos(x / Distance) * Distance - Distance).Within(1e-6f));
            Assert.That(Curve.Unmap(front + new Vector3(0f, 0f, Distance)).X, Is.EqualTo(MathF.Atan2(front.X, front.Z + Distance) * Distance).Within(1e-6f));
        }
    }

    [Test]
    public void EveryColumnFacesTheEyesAtItsBandsRadius()
    {
        foreach (var x in new[] { -0.3f, -0.1f, 0f, 0.2f, 0.35f })
        {
            var forward = Curve.Map(new Vector3(x, 0f, 0f));
            var recessed = Curve.Map(new Vector3(x, 0f, 0.01f));
            Assert.That(new Vector2(forward.X, forward.Z).Length(), Is.EqualTo(0.46f).Within(1e-6f));
            Assert.That(new Vector2(recessed.X, recessed.Z).Length(), Is.EqualTo(0.47f).Within(1e-6f), "a recessed band stands its depth farther out");
            Assert.That(MathF.Atan2(recessed.X, recessed.Z), Is.EqualTo(MathF.Atan2(forward.X, forward.Z)).Within(1e-6f), "bands bend by angle, so they stay aligned");
        }
        Assert.That(Curve.SpreadDegrees(2f * Distance * MathF.Tan(30.5f * MathF.PI / 180f)), Is.EqualTo(2f * MathF.Tan(30.5f * MathF.PI / 180f) * 180f / MathF.PI).Within(1e-3f),
            "a width spreads arc over radius: the plane's 61 degrees of layout spread 67.5 on the curve");
    }

    [Test]
    public void OneDegreeColumnsStayWithinTheirChordError()
    {
        Assert.That(PlaneCurve.ChordError(1f, Distance), Is.EqualTo(Distance * (1f - MathF.Cos(0.5f * MathF.PI / 180f))).Within(1e-9f));
        Assert.That(PlaneCurve.ChordError(1f, Distance), Is.LessThan(0.00002f), "under 0.02 mm, far under a pixel at 0.46 m");
        var width = Distance * 60f * MathF.PI / 180f;
        Assert.That(Curve.Columns(width, 1f), Is.EqualTo(60));
        Assert.That(Curve.Columns(width * 1.001f, 1f), Is.EqualTo(61), "never a column wider than asked");
        Assert.That(Curve.Columns(0f, 1f), Is.EqualTo(1));
        Assert.That(Curve.Columns(0.005f, 1f), Is.EqualTo(1), "5 mm at 0.46 m spreads 0.62 degrees");
        Assert.That(Curve.Columns(0.01f, 1f), Is.EqualTo(2), "10 mm spreads 1.25 degrees");

        // A strip of those columns: the midpoint of each straight edge is at most the chord error inside the curve.
        var columns = Curve.Columns(width, 1f);
        var worst = 0f;
        for (var i = 0; i < columns; i++)
        {
            var a = Curve.Map(new Vector3(-width / 2f + width * i / columns, 0f, 0f));
            var b = Curve.Map(new Vector3(-width / 2f + width * (i + 1) / columns, 0f, 0f));
            var middle = (a + b) / 2f;
            worst = MathF.Max(worst, Distance - new Vector2(middle.X, middle.Z).Length());
        }
        Assert.That(worst, Is.EqualTo(PlaneCurve.ChordError(1f, Distance)).Within(1e-6f));
    }

    [Test]
    public void AGlyphAtASideBandsFarCornerReadsLargerOnTheCurveThanOnThePlane()
    {
        // ADR 0034: Body at a side region's far corner, 0.88 degrees tall on the plane and 0.99 on the curve. The corner
        // stands 30.5 degrees across on the plane (61 across in all), 0.16 m from the eyes' level, in a band recessed 10 mm.
        var em = Glaze.MetersAt(Glaze.Menu.BodyDegrees, Distance);
        var x = Distance * MathF.Tan(30.5f * MathF.PI / 180f);
        var corner = new Vector3(x, 0.16f, 0.01f);
        var seen = Curve.SeenSize(corner, em, em);
        Assert.That(MathF.Round(PlaneTall(x, 0.16f, 0.01f, em), 2), Is.EqualTo(0.88f));
        Assert.That(MathF.Round(seen.Tall, 2), Is.EqualTo(0.99f));
        Assert.That(seen.Tall, Is.GreaterThan(Glaze.MinimumTextDegrees));

        var centre = Curve.SeenSize(Vector3.Zero, em, em);
        // The em is sized from the line of sight (MetersAt, tan of the whole angle), so centred it reads 2 atan(tan(a) / 2), 0.0001 over.
        var design = 2f * MathF.Atan(MathF.Tan(Glaze.Menu.BodyDegrees * MathF.PI / 180f) / 2f) * 180f / MathF.PI;
        Assert.That(centre.Tall, Is.EqualTo(design).Within(1e-5f), "at the centre a glyph reads its design size");
        Assert.That(centre.Wide, Is.EqualTo(Glaze.Menu.BodyDegrees).Within(1e-3f), "across, the arc is a hair shorter than its chord's line of sight");
        var across = Curve.SeenSize(new Vector3(x, 0f, 0f), em, em);
        Assert.That(across.Wide, Is.EqualTo(centre.Wide).Within(1e-4f), "a column across reads as wide as the centre");
        Assert.That(across.Tall, Is.EqualTo(centre.Tall).Within(1e-4f));
    }

    [Test]
    public void APointOfTheFlatPlaneBendsOntoTheCurveByItsArc()
    {
        // The flat plane stands the radius in front of the eyes; its centre column is already on the curve.
        foreach (var y in new[] { -0.2f, 0f, 0.16f })
            Assert.That(Vector3.Distance(Curve.Bend(new Vector3(0f, y, Distance)), new Vector3(0f, y, Distance)), Is.LessThan(1e-7f));
        foreach (var point in new[] { new Vector3(0.27f, 0.16f, Distance), new Vector3(-0.13f, -0.08f, Distance + 0.01f), new Vector3(0.4f, 0f, Distance - 0.004f) })
        {
            var bent = Curve.Bend(point);
            Assert.That(bent, Is.EqualTo(Curve.Map(point - new Vector3(0f, 0f, Distance))), "Bend is Map of the point measured from the plane");
            Assert.That(bent.Y, Is.EqualTo(point.Y), "height is kept");
            Assert.That(new Vector2(bent.X, bent.Z).Length(), Is.EqualTo(point.Z).Within(1e-6f), "depth beyond the plane is kept as radius");
            Assert.That(MathF.Atan2(bent.X, bent.Z) * Distance, Is.EqualTo(point.X).Within(1e-6f), "a point across stands its arc round the eyes");
            Assert.That(Vector3.Distance(Curve.Unmap(bent) + new Vector3(0f, 0f, Distance), point), Is.LessThan(1e-6f), "and comes back by the inverse");
        }
        // Two points a glyph apart across keep their distance as arc on the curve, so a glyph keeps its width.
        var left = Curve.Bend(new Vector3(0.3f, 0f, Distance));
        var right = Curve.Bend(new Vector3(0.305f, 0f, Distance));
        Assert.That(Vector3.Distance(left, right), Is.EqualTo(0.005f).Within(1e-6f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.Bend(new Vector3(MathF.PI * Distance, 0f, Distance)), "past half a turn the curve meets itself");
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.Bend(Vector3.Zero), "a point at the eyes is behind no plane");
    }

    [Test]
    public void AFlatRectangleTakesTheSegmentOfTheCurveItBendsOnto()
    {
        // A target laid out 30 mm wide and 25 mm tall, its centre 0.2 m to the right and 0.05 m up.
        var segment = Curve.Segment(0.185f, 0.215f, 0.0375f, 0.0625f);
        const float ToDegrees = 180f / MathF.PI;
        Assert.That(segment.RotationDegrees, Is.EqualTo(0.2f / Distance * ToDegrees).Within(1e-4f), "centred at its centre's arc");
        Assert.That(segment.ArcDegrees, Is.EqualTo(0.03f / Distance * ToDegrees).Within(1e-4f), "as wide as its width's arc");
        Assert.That(segment.Bottom, Is.EqualTo(0.0375f));
        Assert.That(segment.Top, Is.EqualTo(0.0625f));

        bool Inside(Vector3 seen)
        {
            var angle = MathF.Atan2(seen.X, seen.Z) * ToDegrees;
            return MathF.Abs(angle - segment.RotationDegrees) <= segment.ArcDegrees / 2f + 1e-5f && seen.Y >= segment.Bottom && seen.Y <= segment.Top;
        }
        for (var i = 0; i <= 10; i++)
        for (var j = 0; j <= 10; j++)
        {
            var laid = new Vector3(0.185f + 0.03f * i / 10f, 0.0375f + 0.025f * j / 10f, 0f);
            Assert.That(Inside(Curve.Map(laid)), Is.True, "every point of the rectangle is drawn inside its segment: " + laid);
        }
        foreach (var past in new[] { new Vector3(0.1849f, 0.05f, 0f), new Vector3(0.2151f, 0.05f, 0f), new Vector3(0.2f, 0.0374f, 0f), new Vector3(0.2f, 0.0626f, 0f) })
            Assert.That(Inside(Curve.Map(past)), Is.False, "0.1 mm past an edge is outside: " + past);

        // Where a ray or a poke meets the segment comes back to the rectangle by the inverse.
        Vector3 On(float degrees, float height) => new(Distance * MathF.Sin(degrees / ToDegrees), height, Distance * MathF.Cos(degrees / ToDegrees));
        Assert.That(Vector3.Distance(Curve.Unmap(On(segment.RotationDegrees, 0.05f)), new Vector3(0.2f, 0.05f, 0f)), Is.LessThan(1e-6f), "the centre");
        Assert.That(Vector3.Distance(Curve.Unmap(On(segment.RotationDegrees - segment.ArcDegrees / 2f, segment.Bottom)), new Vector3(0.185f, 0.0375f, 0f)), Is.LessThan(1e-6f), "the bottom left corner");
        Assert.That(Vector3.Distance(Curve.Unmap(On(segment.RotationDegrees + segment.ArcDegrees / 2f, segment.Top)), new Vector3(0.215f, 0.0625f, 0f)), Is.LessThan(1e-6f), "the top right corner");
    }

    [Test]
    public void TargetsTwelveMillimetresApartStayTwelveMillimetresApartAlongTheCurve()
    {
        var left = Curve.Segment(-0.05f, 0.1f, 0f, 0.03f);
        var right = Curve.Segment(0.112f, 0.2f, 0f, 0.03f);
        var gapDegrees = right.RotationDegrees - right.ArcDegrees / 2f - (left.RotationDegrees + left.ArcDegrees / 2f);
        Assert.That(gapDegrees * MathF.PI / 180f * Distance, Is.EqualTo(0.012f).Within(1e-6f), "the arc between their edges is the 12 mm laid out");
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.Segment(0.1f, 0.05f, 0f, 0.03f), "right of its right edge");
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.Segment(0f, 0.05f, 0.03f, 0f), "above its top");
    }

    [Test]
    public void ImpossibleCurvesAndPointsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaneCurve(0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaneCurve(-0.46f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlaneCurve(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.Map(new Vector3(0f, 0f, -Distance)), "a band at or behind the axis");
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.Map(new Vector3(MathF.PI * Distance, 0f, 0f)), "past half a turn the curve meets itself");
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.Unmap(new Vector3(0f, 0.2f, 0f)), "a point on the axis has no angle");
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.Columns(-0.1f, 1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Curve.Columns(0.1f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlaneCurve.ChordError(1f, 0f));
    }
}
