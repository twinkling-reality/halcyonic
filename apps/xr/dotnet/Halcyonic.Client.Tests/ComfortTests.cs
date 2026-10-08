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
        Assert.That((comfort.TextButton, comfort.MotionButton, comfort.SoundButton), Is.EqualTo(("Make text larger", "Keep things still", "Make sounds quieter")));
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
        Assert.That((kept.TextButton, kept.MotionButton, kept.SoundButton), Is.EqualTo(("Make text standard", "Let things move", "Turn sounds off")));
        Assert.That(Comfort.Load(new Comfort { Sounds = SoundLevel.Off }.Save()).Volume, Is.Zero);
    }

    [Test]
    public void AHeadsetThatKeptBadgesStillKeepsThingsStill()
    {
        // Saved before the setting was renamed Keep things still: the same key and value.
        var kept = Comfort.Load("text=standard;motion=still;sounds=on");
        Assert.That((kept.Still, kept.MotionButton), Is.EqualTo((true, "Let things move")));
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
        Assert.That((moving.Value, moving.Now, moving.Next, moving.Does, moving.Prompt), Is.EqualTo(("On", "Things move", "Kept still",
            "Nothing keeps moving on its own: a wait shows a still highlight, and characters and badges stand still.", "Keep things still")));
        motion.Change();
        Assert.That((comfort.Still, saves), Is.EqualTo((true, 1)));
        var still = motion.Read();
        Assert.That((still.Value, still.Now, still.Next, still.Does, still.Prompt), Is.EqualTo(("Kept still", "Things keep still", "On",
            "Waits shimmer, a task waiting for you breathes, and characters move.", "Let things move")));
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
