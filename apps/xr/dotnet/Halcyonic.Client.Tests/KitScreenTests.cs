using System;
using System.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// A picked project's screen and its bar (ADR 0040, rule 2): a picture only from the computer's own check screenshot,
/// Try it only after a passed check of the computer's own, the one clear action only where it applies, and a bar that
/// folds while nothing is wanted.
/// </summary>
public class KitScreenTests
{
    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 10, 29, 10, 38, 0, TimeSpan.Zero);

    private static KitMoment Passed() => KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, At);

    private static KitScreenModel Screen(bool isVr = true, KitMoment? check = null, bool tryRoute = true, DateTimeOffset? picture = null, bool barOpen = true,
        bool working = false, DateTimeOffset? newestChange = null) =>
        new KitScreenModel("Tower Toss", "Codex", "Ready for a change", isVr, check, tryRoute, picture, barOpen, working, newestChange);

    [Test]
    public void NoPictureWithoutTheChecksScreenshot()
    {
        var screen = Screen();
        Assert.That(screen.ShowsPicture, Is.False);
        Assert.That(screen.PictureLabel(At, TimeZoneInfo.Utc), Is.Null, "nothing is labelled as a build without a picture of one");
        Assert.That(screen.Monogram, Is.EqualTo("TT"), "the name, its state and a neutral mark stand instead");
    }

    [Test]
    public void APictureSaysWhenTheCheckTookIt()
    {
        var screen = Screen(picture: At);
        Assert.That(screen.ShowsPicture, Is.True);
        Assert.That(screen.PictureLabel(At.AddHours(2), TimeZoneInfo.Utc), Is.EqualTo("Last Check · Today 10:38 · Observed"));
        Assert.That(screen.PictureLabel(At.AddDays(1), TimeZoneInfo.Utc), Is.EqualTo("Last Check · 29 Oct 10:38 · Observed"), "the day where it isn't today");
    }

    [Test]
    public void TryItLeavesWhenTheWorkStartsAgain()
    {
        Assert.That(Screen(check: Passed(), working: true).Actions, Does.Not.Contain(KitScreenAction.TryIt), "a turn after the check may change what Try it opens");
    }

    [Test]
    public void TryItLeavesWhenTheComputerSawAChangeAfterTheCheck()
    {
        Assert.That(Screen(check: Passed(), newestChange: At.AddMinutes(1)).Actions, Does.Not.Contain(KitScreenAction.TryIt), "a change after the check is unchecked");
        Assert.That(Screen(check: Passed(), newestChange: At.AddMinutes(-1)).Actions, Does.Contain(KitScreenAction.TryIt), "a change the check came after was checked");
        var newer = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, At.AddMinutes(2));
        Assert.That(Screen(check: newer, newestChange: At.AddMinutes(1)).Actions, Does.Contain(KitScreenAction.TryIt), "a new passed check brings it back");
        var untimed = KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: true, null);
        Assert.That(Screen(check: untimed, newestChange: At).Actions, Does.Not.Contain(KitScreenAction.TryIt), "with no time on the check, a change can't be shown older");
    }

    [Test]
    public void TryItOnlyAfterAPassedCheckOfTheComputersOwnAndWithARoute()
    {
        Assert.That(Screen(check: Passed()).Actions, Does.Contain(KitScreenAction.TryIt));
        Assert.That(Screen(check: null).Actions, Does.Not.Contain(KitScreenAction.TryIt), "no check");
        Assert.That(Screen(check: KitMoment.HostCheck(KitCheckScope.Builds, ended: true, passed: false, At)).Actions, Does.Not.Contain(KitScreenAction.TryIt), "a failed check");
        Assert.That(Screen(check: KitMoment.HostCheck(KitCheckScope.Builds, ended: false, passed: null, At)).Actions, Does.Not.Contain(KitScreenAction.TryIt), "a check under way");
        Assert.That(Screen(check: KitMoment.AgentCheck("Codex", "npm run build", "exit 0", At)).Actions, Does.Not.Contain(KitScreenAction.TryIt), "the agent's own check");
        Assert.That(Screen(check: Passed(), tryRoute: false).Actions, Does.Not.Contain(KitScreenAction.TryIt), "no route to try it");
    }

    [Test]
    public void TheOneClearActionOnlyWhereItApplies()
    {
        var web = Screen(isVr: false);
        var vr = Screen(isVr: true);
        Assert.That(web.Actions.First(), Is.EqualTo(KitScreenAction.MakeAVrVersion), "a project that is not VR yet is offered one clear action first");
        Assert.That(web.Primary, Is.EqualTo(KitScreenAction.MakeAVrVersion));
        Assert.That(vr.Actions, Does.Not.Contain(KitScreenAction.MakeAVrVersion));
        Assert.That(vr.Primary, Is.EqualTo(KitScreenAction.ChangeIt));
        Assert.That(web.Actions, Does.Contain(KitScreenAction.ChangeIt), "changing it stays one press away");
        Assert.That(vr.Actions.Count(action => action == vr.Primary), Is.EqualTo(1));
    }

    [Test]
    public void TheDoorsAreAlwaysThere()
    {
        var actions = Screen().Actions;
        foreach (var door in new[] { KitScreenAction.Steps, KitScreenAction.Why, KitScreenAction.HowItWent })
            Assert.That(actions, Does.Contain(door));
    }

    [Test]
    public void TheBarFoldsWhileNothingIsWanted()
    {
        var folded = Screen(barOpen: false);
        Assert.That(folded.Folded, Is.True);
        Assert.That(folded.Shown, Is.Empty, "a folded bar offers nothing but its handle");
        Assert.That(Screen(barOpen: true).Shown, Is.EqualTo(Screen(barOpen: true).Actions));
    }

    [Test]
    public void TheIdentityHueComesFromTheNameAlone()
    {
        Assert.That(KitScreenModel.HueOf("Tower Toss"), Is.EqualTo(KitScreenModel.HueOf("Tower Toss")));
        Assert.That(KitScreenModel.HueOf("Tower Toss"), Is.Not.EqualTo(KitScreenModel.HueOf("Recipe Box")));
        foreach (var name in new[] { "", "a", "Tower Toss", "Moon Garden", "\u00e9t\u00e9" })
            Assert.That(KitScreenModel.HueOf(name), Is.InRange(0.0, 359.999));
        Assert.That(Screen().IdentityHue, Is.EqualTo(KitScreenModel.HueOf("Tower Toss")));
    }

    [Test]
    public void TheMonogramIsTwoLettersOfTheName()
    {
        Assert.That(KitScreenModel.MonogramOf("Tower Toss"), Is.EqualTo("TT"));
        Assert.That(KitScreenModel.MonogramOf("moon garden club"), Is.EqualTo("MG"));
        Assert.That(KitScreenModel.MonogramOf("bonk"), Is.EqualTo("BO"));
        Assert.That(KitScreenModel.MonogramOf("3d-maze 2"), Is.EqualTo("DM"), "letters only");
        Assert.That(KitScreenModel.MonogramOf("  "), Is.EqualTo(""), "no letters, no mark");
    }

    [Test]
    public void EveryActionHasItsWords()
    {
        foreach (KitScreenAction action in Enum.GetValues(typeof(KitScreenAction)))
            Assert.That(KitScreenModel.Words(action), Is.Not.Empty, action.ToString());
        Assert.That(KitScreenModel.Words(KitScreenAction.MakeAVrVersion), Is.EqualTo("Make VR Version"));
        Assert.That(KitScreenModel.Words(KitScreenAction.HowItWent), Is.EqualTo("Outcome"));
    }
}
