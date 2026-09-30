using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class CharacterIdentityTests
{
    private static IEnumerable<string> SequentialIds(int count) =>
        Enumerable.Range(1, count).Select(i => $"01a0dcf1-5a80-7000-8000-{i:x12}");

    [Test]
    public void TheSameWorkstreamAlwaysLooksTheSame()
    {
        foreach (var id in SequentialIds(50))
        {
            var first = CharacterIdentity.Of(id);
            var second = CharacterIdentity.Of(new string(id.ToCharArray()));
            Assert.That(second.Shape, Is.EqualTo(first.Shape));
            Assert.That(second.Hue, Is.EqualTo(first.Hue));
            Assert.That(second.Saturation, Is.EqualTo(first.Saturation));
            Assert.That(second.Value, Is.EqualTo(first.Value));
            Assert.That(second.Phase, Is.EqualTo(first.Phase));
        }
    }

    [TestCase("01a0dcf1-5a80-7000-8000-000000000001", BodyShape.Trefoil, 241.0, 0.42, 0.2762451171875)]
    [TestCase("01a0dcf1-5a80-7000-8000-000000000002", BodyShape.Round, 241.0, 0.62, 0.7735443115234375)]
    [TestCase("workstream-demo-approval", BodyShape.Trefoil, 197.0, 0.42, 0.8931427001953125)]
    public void IdentitiesDoNotChangeBetweenVersions(string id, BodyShape shape, double hue, double saturation, double phase)
    {
        // Values computed independently from FNV-1a and MurmurHash3's finalizer. A change here
        // changes the look of every existing workstream.
        var identity = CharacterIdentity.Of(id);
        Assert.That(identity.Shape, Is.EqualTo(shape));
        Assert.That(identity.Hue, Is.EqualTo(hue));
        Assert.That(identity.Saturation, Is.EqualTo(saturation));
        Assert.That(identity.Phase, Is.EqualTo(phase));
    }

    [Test]
    public void IdentityHuesKeepAwayFromTheStateColors()
    {
        foreach (var hue in CharacterIdentity.Hues)
        {
            foreach (var state in CharacterIdentity.StateHues)
            {
                var apart = Math.Abs(hue - state) % 360.0;
                apart = Math.Min(apart, 360.0 - apart);
                Assert.That(apart, Is.GreaterThanOrEqualTo(25.0), $"identity hue {hue} is too close to the state hue {state}");
            }
        }
        foreach (var id in SequentialIds(500))
        {
            Assert.That(CharacterIdentity.Hues, Does.Contain(CharacterIdentity.Of(id).Hue));
        }
    }

    [Test]
    public void TimeOrderedIdsSpreadOverEveryShapeHueAndTone()
    {
        // Workstream ids are time ordered, so neighbours differ only in their last characters.
        var identities = SequentialIds(1024).Select(CharacterIdentity.Of).ToList();
        var shapes = identities.GroupBy(identity => identity.Shape).ToDictionary(group => group.Key, group => group.Count());
        var hues = identities.GroupBy(identity => identity.Hue).ToDictionary(group => group.Key, group => group.Count());
        var tones = identities.GroupBy(identity => identity.Saturation).ToDictionary(group => group.Key, group => group.Count());

        Assert.That(shapes.Keys, Is.EquivalentTo(Enum.GetValues(typeof(BodyShape))));
        Assert.That(hues.Keys, Is.EquivalentTo(CharacterIdentity.Hues));
        Assert.That(tones, Has.Count.EqualTo(2));
        // A fair share is 128 of each shape and hue; none may be far off.
        Assert.That(shapes.Values, Is.All.InRange(80, 176));
        Assert.That(hues.Values, Is.All.InRange(80, 176));
    }

    [Test]
    public void EveryIdentityHasAPhaseBetweenZeroAndOne()
    {
        foreach (var id in SequentialIds(200))
        {
            Assert.That(CharacterIdentity.Of(id).Phase, Is.InRange(0.0, 1.0).And.LessThan(1.0));
        }
    }

    [Test]
    public void AnIdIsRequired()
    {
        Assert.Throws<ArgumentNullException>(() => CharacterIdentity.Of(null!));
    }
}
