#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class KitRender
    {
        /// <summary>The fitted shelf's worst case, a state of its own in PartsCheck (no render: its proof is measured).</summary>
        private const string ShelfWorstState = "60-shelf-worst-case";

        /// <summary>
        /// The fitted shelf's worst case (the lead's 2026-11-14T02:21Z ruling 1 (a), AUDIT's 02:23Z and 02:34Z): the width every shelf
        /// takes beside the plane at rest (CharacterStage.FittedWidth, from the rest a real plane reasons through its own geometry), its
        /// places each carrying the widest two-line label the shelf's own measure lays, at the search's nearest and farthest distance.
        /// Adjacent labels and adjacent characters stand apart by more than zero as the eyes see them, no label is cut, and every word
        /// of every label reads at or over 14 dp as the eyes see it where it is drawn, its far words included. A witness: the same
        /// shelf with its places a quarter as far apart must be caught touching.
        /// </summary>
        private static List<string> ShelfWorstCase(string size)
        {
            var failures = new List<string>();
            var root = new GameObject("Shelf worst case");
            var was = RoomDesign.Enabled;
            var wasRest = CharacterStage.PlaneRestHalfDegrees;
            RoomDesign.Enabled = true;
            try
            {
                var eyes = Vector3.zero;
                var plane = MenuPlane.Create(root.transform);
                plane.Show(new MenuBar(MenuPlace.Tasks, "One synthetic task"), null, null, null, Array.Empty<CharacterTarget>(), eyes, Vector3.forward, null, immediately: true);
                var places = CharacterStage.ShelfPlaces;
                CharacterStage.PlaneRestHalfDegrees = null;
                plane.ConsiderForm(null, eyes, places, open: false);
                if (CharacterStage.PlaneRestHalfDegrees is not Func<float> restOf)
                {
                    failures.Add(size + ": " + ShelfWorstState + ": the plane at rest said no half-width, so no shelf width was fitted");
                    return failures;
                }
                var rest = restOf();
                var width = CharacterStage.FittedWidth(places);
                var pitch = CharacterStage.PitchOf(places, width);
                Debug.Log("Halcyonic: parts: " + ShelfWorstState + " at " + size + ": the plane at rest " + rest.ToString("0.0", CultureInfo.InvariantCulture)
                    + " degrees each side; every shelf " + width.ToString("0.000", CultureInfo.InvariantCulture) + " m wide (natural "
                    + AgentShelfDraft.Width(places).ToString("0.000", CultureInfo.InvariantCulture) + "), " + places + " places "
                    + pitch.ToString("0.000", CultureInfo.InvariantCulture) + " m apart.");
                foreach (var distance in new[] { AgentShelfSearch.MinimumDistance, AgentShelfSearch.MaximumDistance })
                {
                    var at = distance.ToString("0.0", CultureInfo.InvariantCulture) + " m";
                    var laid = LayWorstShelf(root.transform, eyes, distance, places, pitch);
                    float closestLabels = float.PositiveInfinity, closestBodies = float.PositiveInfinity;
                    for (var place = 0; place + 1 < places; place++)
                    {
                        var labels = laid.Labels[place].Apart(laid.Labels[place + 1]);
                        var bodies = laid.Bodies[place].Apart(laid.Bodies[place + 1]);
                        closestLabels = Mathf.Min(closestLabels, labels);
                        closestBodies = Mathf.Min(closestBodies, bodies);
                        if (labels <= 0f) failures.Add(size + ": " + ShelfWorstState + " at " + at + ": " + laid.Labels[place] + " and " + laid.Labels[place + 1] + " touch, " + Degrees(labels) + " degrees apart");
                        if (bodies <= 0f) failures.Add(size + ": " + ShelfWorstState + " at " + at + ": " + laid.Bodies[place] + " and " + laid.Bodies[place + 1] + " touch, " + Degrees(bodies) + " degrees apart");
                    }
                    foreach (var cut in laid.Cut) failures.Add(size + ": " + ShelfWorstState + " at " + at + ": " + cut + " is cut");
                    if (laid.Smallest.Degrees < Glaze.MinimumTextDegrees - 0.0005f)
                        failures.Add(size + ": " + ShelfWorstState + " at " + at + ": " + laid.Smallest.Name + " is " + laid.Smallest.Degrees.ToString("0.000", CultureInfo.InvariantCulture)
                            + " degrees as the eyes see it where drawn, under 14 dp, " + Glaze.MinimumTextDegrees.ToString("0.000", CultureInfo.InvariantCulture) + ".");
                    Debug.Log("Halcyonic: parts: " + ShelfWorstState + " at " + size + ", " + at + ": the shelf with its labels " + Degrees(laid.Envelope) + " degrees wide; the closest labels "
                        + Degrees(closestLabels) + " degrees apart, the closest characters " + Degrees(closestBodies) + " degrees apart; the smallest word "
                        + laid.Smallest.Name + " at " + laid.Smallest.Degrees.ToString("0.000", CultureInfo.InvariantCulture) + " degrees; " + laid.Cut.Count + " labels cut.");
                    DestroyLaid(laid.Views);
                }
                // The witness: a quarter of the pitch must be caught touching, or the check above proves nothing.
                var witness = LayWorstShelf(root.transform, eyes, AgentShelfSearch.MinimumDistance, places, pitch / 4f);
                if (!Enumerable.Range(0, places - 1).Any(place => witness.Labels[place].Apart(witness.Labels[place + 1]) <= 0f))
                    failures.Add(size + ": " + ShelfWorstState + ": the touch check accepted labels a quarter as far apart");
                DestroyLaid(witness.Views);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                CharacterStage.PlaneRestHalfDegrees = wasRest;
                RoomDesign.Enabled = was;
            }
            return failures;
        }

        /// <summary>
        /// A shelf <paramref name="distance"/> metres ahead and as far below the eyes as the stage's own measure stands it, its
        /// <paramref name="places"/> characters <paramref name="pitch"/> apart, each shown with the shelf measure's widest label and its
        /// label placed for the eyes as the stage places it: each label's and each body's extent as the eyes see it, the labels cut,
        /// the smallest word seen, and the whole width with its labels.
        /// </summary>
        private static (List<CharacterView> Views, List<GlazeChecks.Extent> Labels, List<GlazeChecks.Extent> Bodies, List<string> Cut, (string Name, float Degrees) Smallest, float Envelope)
            LayWorstShelf(Transform parent, Vector3 eyes, float distance, int places, float pitch)
        {
            var pose = new Pose(eyes + new Vector3(0f, -0.45f, distance), Quaternion.identity);
            var views = new List<CharacterView>();
            var labels = new List<GlazeChecks.Extent>();
            var bodies = new List<GlazeChecks.Extent>();
            var cut = new List<string>();
            var smallest = (Name: "no word", Degrees: float.PositiveInfinity);
            for (var place = 0; place < places; place++)
            {
                var view = CharacterView.Create(parent, "shelf-worst-" + place);
                view.enabled = false;
                var x = (place - (places - 1) * 0.5f) * pitch;
                view.transform.SetPositionAndRotation(pose.position + pose.rotation * Vector3.right * x, pose.rotation * Quaternion.Euler(0f, 180f, 0f));
                view.transform.localScale = Vector3.one * AgentShelfDraft.Scale;
                // The widest label the shelf's own measure lays (CharacterStage.WriteShelfCorners): two full lines, both marks.
                view.Show(new CharacterPresentation("shelf-worst-" + place, "A complete task title with two full lines of readable words", CharacterActivity.Verifying,
                    "Checking", Halcyonic.Contracts.AttentionLevel.None, Array.Empty<string>(), 0, true, true, false));
                view.PlaceShelfLabel(eyes);
                views.Add(view);
                var label = view.Label.transform;
                labels.Add(GlazeChecks.Of("label " + place, eyes, label.GetComponentsInChildren<Renderer>(false)));
                bodies.Add(GlazeChecks.Of("character " + place, eyes, view.GetComponentsInChildren<Renderer>(false).Where(renderer => !renderer.transform.IsChildOf(label))));
                foreach (var text in label.GetComponentsInChildren<TMP_Text>(false))
                {
                    if (string.IsNullOrEmpty(text.text) || GlazeIcons.IsIcon(text)) continue;
                    text.ForceMeshUpdate();
                    if (text.isTextTruncated) cut.Add("label " + place + "'s " + text.name + " (\"" + text.text + "\")");
                    var em = text.fontSize * 0.1f * text.transform.lossyScale.y;
                    var half = text.transform.up * (em / 2f);
                    var info = text.textInfo;
                    for (var index = 0; index < info.characterCount; index++)
                    {
                        var glyph = info.characterInfo[index];
                        if (!glyph.isVisible) continue;
                        var middle = text.transform.TransformPoint((glyph.bottomLeft + glyph.topRight) / 2f);
                        var degrees = Vector3.Angle(middle + half - eyes, middle - half - eyes);
                        if (degrees < smallest.Degrees) smallest = ("label " + place + "'s " + text.name + " '" + glyph.character + "'", degrees);
                    }
                }
            }
            var envelope = labels.Concat(bodies).Max(extent => extent.Right) - labels.Concat(bodies).Min(extent => extent.Left);
            return (views, labels, bodies, cut, smallest, envelope);
        }

        private static void DestroyLaid(List<CharacterView> views)
        {
            foreach (var view in views) if (view != null) UnityEngine.Object.DestroyImmediate(view.gameObject);
        }
    }
}
