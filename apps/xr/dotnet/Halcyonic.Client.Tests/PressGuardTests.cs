#nullable enable
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The one press (R1): one pinch acting on release, the stray-pinch guard inside it, never a second button or a wait.</summary>
public sealed class PressGuardTests
{
    private const float Settle = PressGuard.SettleSeconds;

    private static PressGuard Rested(float arrived = 0f)
    {
        var guard = new PressGuard(arrived);
        guard.RayOn(arrived);
        return guard;
    }

    [Test]
    public void An_aimed_pinch_acts_on_its_release_over_the_button()
    {
        var guard = Rested();
        Assert.That(guard.Lit(Settle), Is.True, "lit once it will take");
        Assert.That(guard.Down(Settle + 0.1f, ray: true), Is.True, "the pinch counts: the button sinks");
        Assert.That(guard.Armed, Is.True);
        Assert.That(guard.Up(Settle + 0.2f, ray: true), Is.True, "and acts on release");
        Assert.That(guard.Armed, Is.False);
    }

    [Test]
    public void The_pinch_that_answered_never_confirms_what_arrives_under_it()
    {
        // Approve is pressed; its Confirm arrives in the same place at once, under the same ray, the pinch still held.
        var guard = Rested();
        guard.Arrive(1f);
        Assert.That(guard.Lit(1f + Settle / 2f), Is.False, "not lit before it has stood its settle");
        Assert.That(guard.Down(1f + 0.05f, ray: true), Is.False, "a pinch just after it arrived does not count");
        Assert.That(guard.Up(1f + 0.5f, ray: true), Is.False, "so its release does nothing");
        Assert.That(guard.Down(1f + Settle + 0.01f, ray: true), Is.True, "a new pinch once it settled does");
        Assert.That(guard.Up(1f + Settle + 0.1f, ray: true), Is.True);
    }

    [Test]
    public void A_pinch_under_way_when_the_button_arrives_anew_is_not_one_of_it()
    {
        var guard = Rested();
        Assert.That(guard.Down(1f, ray: true), Is.True);
        guard.Arrive(1.1f);
        Assert.That(guard.Up(1.2f, ray: true), Is.False, "its words or command changed under the pinch");
    }

    [Test]
    public void A_ray_must_rest_on_the_button_but_a_finger_needs_only_the_arrival()
    {
        var guard = new PressGuard(0f);
        guard.RayOn(5f);
        Assert.That(guard.Down(5f + 0.1f, ray: true), Is.False, "a ray that just swept onto it");
        guard.Up(5.2f, ray: true);
        Assert.That(guard.Down(5f + Settle + 0.01f, ray: true), Is.True, "a ray that rested");
        guard.Up(5.5f, ray: true);
        var poked = new PressGuard(0f);
        Assert.That(poked.Down(Settle, ray: false), Is.True, "a finger touches what it means to");
        Assert.That(poked.Up(Settle + 0.1f, ray: false), Is.True);
    }

    [Test]
    public void A_ray_that_jitters_off_for_under_a_tenth_keeps_its_rest_and_its_press()
    {
        var guard = Rested();
        guard.RayOff(0.2f);
        guard.RayOn(0.25f);
        Assert.That(guard.Lit(Settle), Is.True, "a 50 ms flicker did not restart the rest");
        Assert.That(guard.Down(0.5f, ray: true), Is.True);
        guard.RayOff(0.6f);
        Assert.That(guard.Up(0.65f, ray: true), Is.True, "a release 50 ms after the ray slipped off still acts");
        guard.RayOn(1f);
        guard.RayOff(1.2f);
        guard.RayOn(1.5f);
        Assert.That(guard.Lit(1.6f), Is.False, "off for 300 ms: the rest starts again");
    }

    [Test]
    public void Sliding_off_before_letting_go_or_dragging_does_nothing()
    {
        var guard = Rested();
        Assert.That(guard.Down(1f, ray: true), Is.True);
        guard.RayOff(1.1f);
        Assert.That(guard.Up(1.4f, ray: true), Is.False, "let go off the button: the desk's escape");
        guard.RayOn(2f);
        Assert.That(guard.Down(3f, ray: true), Is.True);
        guard.Moved(PressGuard.DragDegrees + 1f, 0f);
        Assert.That(guard.Up(3.2f, ray: true), Is.False, "a ray that turned past the threshold dragged");
        Assert.That(guard.Down(4f, ray: true), Is.True);
        guard.Moved(1f, 0.005f);
        Assert.That(guard.Up(4.2f, ray: true), Is.True, "a small wobble is still a press");
        Assert.That(guard.Down(5f, ray: true), Is.True);
        Assert.That(guard.Up(5.2f, ray: true, cancelled: true), Is.False, "the system cancelled the pinch");
    }

    [Test]
    public void A_render_presses_with_no_hands()
    {
        var guard = new PressGuard(10f);
        guard.SettleForRender();
        Assert.That(guard.Down(10f, ray: false), Is.True);
        Assert.That(guard.Up(10f, ray: false), Is.True);
    }
}
