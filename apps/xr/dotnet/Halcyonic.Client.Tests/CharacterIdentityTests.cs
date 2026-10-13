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
            Assert.That(second.Feature, Is.EqualTo(first.Feature));
            Assert.That(second.BlinkEverySeconds, Is.EqualTo(first.BlinkEverySeconds));
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

    [TestCase("01a0dcf1-5a80-7000-8000-000000000001", CharacterFeature.None, 6.262745098039216)]
    [TestCase("01a0dcf1-5a80-7000-8000-000000000002", CharacterFeature.Antenna, 5.007843137254902)]
    [TestCase("workstream-demo-approval", CharacterFeature.Crest, 6.137254901960784)]
    public void FeaturesAndBlinksDoNotChangeBetweenVersions(string id, CharacterFeature feature, double blinkEvery)
    {
        // Computed independently from the same hash; the shape, hue and tone above do not change with them.
        var identity = CharacterIdentity.Of(id);
        Assert.That(identity.Feature, Is.EqualTo(feature));
        Assert.That(identity.BlinkEverySeconds, Is.EqualTo(blinkEvery).Within(1e-9));
    }

    [Test]
    public void TimeOrderedIdsSpreadOverEveryFeatureAndBlinkAtTheirOwnPace()
    {
        var identities = SequentialIds(1200).Select(CharacterIdentity.Of).ToList();
        var features = identities.GroupBy(identity => identity.Feature).ToDictionary(group => group.Key, group => group.Count());
        Assert.That(features.Keys, Is.EquivalentTo(Enum.GetValues(typeof(CharacterFeature))));
        // A fair share is 200 of each; none may be far off.
        Assert.That(features.Values, Is.All.InRange(130, 270));
        Assert.That(identities.Select(identity => identity.BlinkEverySeconds), Is.All.InRange(CharacterIdentity.FewestBlinkSeconds, CharacterIdentity.MostBlinkSeconds));
        Assert.That(identities.Select(identity => Math.Round(identity.BlinkEverySeconds, 1)).Distinct().Count(), Is.GreaterThan(30), "characters blink at many paces, not a few");
    }

    [Test]
    public void SixRealIdsRarelyShareAShapeAFeatureAndAHue()
    {
        // Ids as the control plane makes them: time ordered (UUIDv7), the same millisecond prefix for a burst of
        // tasks, random after. ADR 0013 expects two of six to share a shape, a feature and a hue in about 1 stage in
        // 25, and the whole identity, tone too, in about 1 in 50.
        var random = new Random(20261008);
        string Hex(int digits) => string.Concat(Enumerable.Range(0, digits).Select(_ => "0123456789abcdef"[random.Next(16)]));
        var stages = 4000;
        var looksAlike = 0;
        var same = 0;
        for (var stage = 0; stage < stages; stage++)
        {
            var prefix = "01a0dcf1-" + Hex(4);
            var six = Enumerable.Range(0, 6).Select(_ => CharacterIdentity.Of(prefix + "-7" + Hex(3) + "-" + "89ab"[random.Next(4)] + Hex(3) + "-" + Hex(12))).ToList();
            var pairs = six.SelectMany((one, i) => six.Skip(i + 1).Select(other => (one, other))).ToList();
            if (pairs.Any(pair => pair.one.Shape == pair.other.Shape && pair.one.Feature == pair.other.Feature && pair.one.Hue == pair.other.Hue)) looksAlike++;
            if (pairs.Any(pair => pair.one.Shape == pair.other.Shape && pair.one.Feature == pair.other.Feature && pair.one.Hue == pair.other.Hue
                && pair.one.Saturation == pair.other.Saturation)) same++;
        }
        Assert.That(looksAlike / (double)stages, Is.InRange(0.02, 0.06), "about 1 stage in 25");
        Assert.That(same / (double)stages, Is.InRange(0.008, 0.035), "about 1 stage in 50");
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
