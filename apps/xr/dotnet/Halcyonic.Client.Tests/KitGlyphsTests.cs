using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The lead's 2026-11-17T22:10Z unit: the accented Latin letters every kit face holds, one list for the atlases and the review.</summary>
public class KitGlyphsTests
{
    [Test]
    public void LatinHoldsTheSupplementAndExtendedAButTheSoftHyphenAndTheDeprecatedLetter()
    {
        Assert.That(KitGlyphs.Latin.Length, Is.EqualTo(0x180 - 0xA0 - 2));
        Assert.That("Café, señor, über, Łódź".All(character => character < 0x7F || KitGlyphs.Latin.Contains(character)), Is.True);
        // By char, never a string match: a culture-aware match ignores the soft hyphen and finds it anywhere.
        Assert.That((KitGlyphs.Latin.IndexOf('\u00AD'), KitGlyphs.Latin.IndexOf('\u0149')), Is.EqualTo((-1, -1)));
        Assert.That(KitGlyphs.Latin.Select(character => (int)character).All(KitGlyphs.HoldsLatin), Is.True);
        Assert.That(new[] { 0x7E, 0x9F, 0xAD, 0x149, 0x180, 0x4E2D }.Any(KitGlyphs.HoldsLatin), Is.False, "ASCII, controls, the soft hyphen, ŉ and past Extended-A are not in the list");
    }
}
