using System.Linq;
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
        Assert.That((comfort.TextButton, comfort.MotionButton, comfort.SoundButton), Is.EqualTo(("Larger Text", "Keep Still", "Quieter Sounds")));
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
            (SoundLevel.On, "Quieter Sounds"),
            (SoundLevel.Quieter, "Sounds Off"),
            (SoundLevel.Off, "Sounds On"),
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
        Assert.That((kept.TextButton, kept.MotionButton, kept.SoundButton), Is.EqualTo(("Standard Text", "Allow Motion", "Sounds Off")));
        Assert.That(Comfort.Load(new Comfort { Sounds = SoundLevel.Off }.Save()).Volume, Is.Zero);
    }

    [Test]
    public void AHeadsetThatKeptBadgesStillKeepsThingsStill()
    {
        // Saved before the setting was renamed Keep things still: the same key and value.
        var kept = Comfort.Load("text=standard;motion=still;sounds=on");
        Assert.That((kept.Still, kept.MotionButton), Is.EqualTo((true, "Allow Motion")));
        Assert.That(kept.Save(), Is.EqualTo("text=standard;motion=still;sounds=on"));
    }

    [Test]
    public void SettingsSaysMotionBothWays()
    {
        var comfort = new Comfort();
        var saves = 0;
        var motion = ComfortSettings.Of(comfort, () => saves++).Single(setting => setting.Key == "moving-badges");
        Assert.That((motion.Group, motion.Name), Is.EqualTo((Comfort.Heading, "Motion")));
        var moving = motion.Read();
        Assert.That((moving.Value, moving.Now, moving.Next, moving.Does, moving.Prompt), Is.EqualTo(("On", "On", "Still",
            "Still Highlights · Characters and Badges Still", "Keep Still")));
        motion.Change();
        Assert.That((comfort.Still, saves), Is.EqualTo((true, 1)));
        var still = motion.Read();
        Assert.That((still.Value, still.Now, still.Next, still.Does, still.Prompt), Is.EqualTo(("Still", "Still", "On",
            "Waits Shimmer · Waiting Tasks Breathe · Characters Move", "Allow Motion")));
        motion.Change();
        Assert.That((comfort.Still, saves), Is.EqualTo((false, 2)));
    }

    [Test]
    public void WhatTheDeviceCantReadStaysAsDesigned()
    {
        var comfort = Comfort.Load("text=huge;motion;sounds=loud;=;colour=blue");
        Assert.That((comfort.Text, comfort.Still, comfort.Sounds), Is.EqualTo((TextSize.Standard, false, SoundLevel.On)));
    }
}
