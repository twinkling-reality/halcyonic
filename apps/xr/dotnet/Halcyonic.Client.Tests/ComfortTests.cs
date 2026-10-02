using System.Collections.Generic;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The comfort settings the device keeps, and how Settings says them.</summary>
public class ComfortTests
{
    [Test]
    public void AsDesignedUntilThePersonChangesIt()
    {
        var comfort = Comfort.Load(null);
        Assert.That((comfort.Text, comfort.Still, comfort.Sounds), Is.EqualTo((TextSize.Standard, false, SoundLevel.On)));
        Assert.That((comfort.TextScale, comfort.Volume), Is.EqualTo((1f, 1f)));
        Assert.That(comfort.Line, Is.EqualTo("Text is the standard size. Badges move, and sounds are on."));
        Assert.That((comfort.TextButton, comfort.MotionButton, comfort.SoundButton), Is.EqualTo(("Make text larger", "Keep badges still", "Make sounds quieter")));
    }

    [Test]
    public void TheSoundButtonStepsThroughEachLevelAndRoundAgain()
    {
        var comfort = new Comfort();
        var said = new List<(SoundLevel, string)>();
        for (var press = 0; press < 3; press++)
        {
            said.Add((comfort.Sounds, comfort.SoundButton));
            comfort.Sounds = comfort.NextSounds;
        }
        Assert.That(said, Is.EqualTo(new[]
        {
            (SoundLevel.On, "Make sounds quieter"),
            (SoundLevel.Quieter, "Turn sounds off"),
            (SoundLevel.Off, "Turn sounds on"),
        }));
        Assert.That(comfort.Sounds, Is.EqualTo(SoundLevel.On));
    }

    [Test]
    public void TheDeviceKeepsEachChoice()
    {
        var comfort = new Comfort { Text = TextSize.Larger, Still = true, Sounds = SoundLevel.Quieter };
        var kept = Comfort.Load(comfort.Save());
        Assert.That((kept.Text, kept.Still, kept.Sounds), Is.EqualTo((TextSize.Larger, true, SoundLevel.Quieter)));
        Assert.That((kept.TextScale, kept.Volume), Is.EqualTo((Comfort.LargerTextScale, Comfort.QuieterVolume)));
        Assert.That(kept.Line, Is.EqualTo("Text is a step larger. Badges stand still, and sounds are quieter."));
        Assert.That((kept.TextButton, kept.MotionButton, kept.SoundButton), Is.EqualTo(("Make text standard", "Let badges move", "Turn sounds off")));
        Assert.That(Comfort.Load(new Comfort { Sounds = SoundLevel.Off }.Save()).Volume, Is.Zero);
    }

    [Test]
    public void WhatTheDeviceCantReadStaysAsDesigned()
    {
        var comfort = Comfort.Load("text=huge;motion;sounds=loud;=;colour=blue");
        Assert.That((comfort.Text, comfort.Still, comfort.Sounds), Is.EqualTo((TextSize.Standard, false, SoundLevel.On)));
    }
}
