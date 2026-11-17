#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The character's flight to an asking moment (the lead's 2026-11-17T02:15Z), frame by frame for AUDIT's 4 s clip and
    /// stills: the shipped bot on its shelf, called at 0 s, a breath and an eased arc to beside the question panel, the panel
    /// arriving once it lands; released at 2 s, the panel leaving first, then the same arc home. Rendered only with
    /// HALCYONIC_KIT_FLIGHT=1, never a gate's run. Its place each frame is RoomMotion.Flight, the formula CharacterStage flies by.
    /// </summary>
    public static partial class KitRender
    {
        private const int FlightFrames = 41;

        private const float FlightReleasedAt = 2f;

        private static readonly string[] FlightStates = System.Environment.GetEnvironmentVariable("HALCYONIC_KIT_FLIGHT") == "1"
            ? Enumerable.Range(0, FlightFrames).Select(frame => "61-flight-" + frame.ToString("00", CultureInfo.InvariantCulture)).ToArray()
            : System.Array.Empty<string>();

        private static PartShot FlightShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = -12f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var frame = int.Parse(state.Substring("61-flight-".Length), CultureInfo.InvariantCulture);
            var seconds = frame * 0.1f;
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            Place(plane, eyes, 0f, shot.Elevation);

            // The question panel, right of the middle, and the shelf, low and to the left in front of the person.
            var panel = Halcyonic.XR.UI.RoomSurface.Create(plane, "Question", RoomSurfaceKind.Information, KitType.Order.Plate);
            var panelSize = new Vector2(RoomType.Units(RoomGeometry.PanelDp), RoomType.Units(220f));
            panel.Draw(panelSize);
            panel.transform.localPosition = new Vector3(RoomType.Units(120f), RoomType.Units(40f), 0f);
            var shelf = plane.position + plane.rotation * new Vector3(-0.30f, -0.26f, 0.05f);

            var view = CharacterView.Create(root, "flight-bot");
            view.enabled = false;
            view.Show(new CharacterPresentation("flight-bot", "Make the game", CharacterActivity.Verifying, "Working",
                Halcyonic.Contracts.AttentionLevel.ActionRequired, System.Array.Empty<string>(), 0, true, true, false));
            view.transform.localScale = Vector3.one * AgentShelfDraft.Scale;

            // Beside the panel as the director asks for it (MenuDirector.BesidePanel): to its left, at its upper third.
            var beside = MenuDirector.BesidePanel(panel.transform, panelSize);
            CharacterFlight FlightAt(float at) => RoomMotion.Flight(at, at >= FlightReleasedAt ? at - FlightReleasedAt : (float?)null);
            // As CharacterStage.Fly holds it: at or below the eyes, never within half a metre of the head.
            Vector3 Held(Vector3 point)
            {
                point.y = Mathf.Min(point.y, eyes.y);
                var away = point - eyes;
                if (away.magnitude < CharacterStage.FlightClearOfHeadM) point = eyes + away.normalized * CharacterStage.FlightClearOfHeadM;
                return point;
            }
            Vector3 PointAt(float at)
            {
                var flight = FlightAt(at);
                return Held(Vector3.Lerp(shelf, beside, flight.Along) + Vector3.up * flight.OverM);
            }
            var now = FlightAt(seconds);
            view.transform.position = PointAt(seconds);
            // Facing the person wherever it stands, as the shelf turns it (its forward away from the eyes, then half a turn).
            var flat = new Vector3(view.transform.position.x - eyes.x, 0f, view.transform.position.z - eyes.z);
            view.transform.rotation = Quaternion.LookRotation(flat.sqrMagnitude > 1e-6f ? flat.normalized : Vector3.forward) * Quaternion.Euler(0f, 180f, 0f);
            view.PlaceShelfLabel(eyes);
            view.Labels.gameObject.SetActive(now.Along <= 0f || now.TagShown >= 0.5f);
            panel.Shown = now.PanelShown;
            panel.transform.localPosition += Vector3.down * (now.PanelLowerDp * RoomType.Units(1f));

            // The whole way, sampled finely: smooth (no snap at either end), at or below the eyes, never within half a metre.
            var landed = RoomMotion.FlightBreathSeconds + RoomMotion.FlightSeconds;
            var last = PointAt(0f);
            var step = 0f;
            for (var at = 0.01f; at <= 4f; at += 0.01f)
            {
                var point = PointAt(at);
                step = Mathf.Max(step, (point - last).magnitude);
                last = point;
                Require(point.y <= eyes.y + 1e-4f, "at " + at.ToString("0.00") + " s the character flies above the eyes");
                Require((point - eyes).magnitude >= CharacterStage.FlightClearOfHeadM - 1e-4f, "at " + at.ToString("0.00") + " s the character comes within half a metre of the head");
            }
            Require(step < 0.03f, "the flight jumps " + step.ToString("0.000") + " m in 10 ms");
            Require((PointAt(0f) - shelf).magnitude < 1e-4f && (PointAt(4f) - shelf).magnitude < 1e-4f, "the flight does not start and end on its shelf");
            Require((PointAt(landed + 0.01f) - Held(beside)).magnitude < 0.01f, "the character has not landed beside the panel at " + landed.ToString("0.0") + " s");
            Require(FlightAt(landed - 0.01f).PanelShown == 0f, "the panel arrives before the character lands");
            Require(FlightAt(FlightReleasedAt + RoomMotion.PanelSeconds).Along > 0.999f && FlightAt(FlightReleasedAt + RoomMotion.PanelSeconds).PanelShown < 0.001f,
                "the character leaves before the panel has gone");
            var renderers = view.GetComponentsInChildren<Renderer>(false);
            var bounds = renderers.Length > 0 ? renderers.Select(each => each.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }) : new Bounds();
            Debug.Log("Halcyonic: parts: " + name + ": the bot at " + view.transform.position.ToString("F3") + " (eyes " + eyes.ToString("F3") + ", panel " + panel.transform.position.ToString("F3")
                + "), scale " + view.transform.lossyScale.x.ToString("0.000") + ", " + renderers.Length + " renderers, bounds " + bounds.center.ToString("F3") + " size " + bounds.size.ToString("F3")
                + ", layer " + view.gameObject.layer + ", active " + view.gameObject.activeInHierarchy + ".");
            Debug.Log("Halcyonic: parts: " + name + ": " + seconds.ToString("0.0") + " s, along " + now.Along.ToString("0.00") + ", over " + now.OverM.ToString("0.000")
                + " m, panel " + now.PanelShown.ToString("0.00") + ", tag " + now.TagShown.ToString("0.00") + ", the largest step " + step.ToString("0.000") + " m in 10 ms.");
            return shot;
        }
    }
}
