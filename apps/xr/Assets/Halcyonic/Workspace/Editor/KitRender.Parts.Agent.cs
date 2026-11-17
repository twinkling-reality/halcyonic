#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// The agent and its level (the redesign's addendum A1) as the redesign's own view draws its page: the computer's fact
    /// in the panel, the two levels as answers with their marks, Change and Back in the column; the page without levels;
    /// and the agents and models as answers, three a page with their way to more. Every frame comes from the page itself
    /// (<see cref="AgentColumn"/>) through <see cref="RoomBar.Agent"/>, with synthetic names.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] AgentStates = { "44-agent-choose", "44-agent-chosen", "44-agent-plain", "44-agent-models", "44-agent-models-more" };

        private static readonly string[] AgentNames = { "Glimmer 30B", "Ember 7B", "Tide 12B", "Kite 4B", "Lantern 9B" };

        private static PartShot AgentShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var options = state == "44-agent-models-more" ? 5 : 3;
            var offer = new AgentOffer(AgentNames[0], listsModels: true, offersNeverAsk: state != "44-agent-plain", neverAsk: state == "44-agent-chosen",
                AgentNames.Take(options).Select((each, index) => new AgentOption("option-" + index.ToString(CultureInfo.InvariantCulture), each, index == 0)).ToList());
            var page = new AgentColumn(() => offer, _ => { }, _ => { });
            if (state.StartsWith("44-agent-models", StringComparison.Ordinal)) page.Act(AgentColumn.Change, null);
            var frame = page.Frame!;
            var bar = RoomBar.Agent(frame, VoiceStage.Idle)!;
            var expected = state switch
            {
                "44-agent-plain" => new[] { "Change", "Back" },
                "44-agent-models" => new[] { "Glimmer 30B", "Ember 7B", "Tide 12B", "Back" },
                "44-agent-models-more" => new[] { "Glimmer 30B", "Ember 7B", "Tide 12B", "Next Choices", "Back" },
                _ => new[] { "Ask Me First", "Don't Ask Me", "Change", "Back" },
            };
            Require(bar.Column.Select(action => action.Words).SequenceEqual(expected), "the page offers " + string.Join(", ", bar.Column.Select(action => action.Words)));

            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomBarView.Create(plane);
            view.Show(bar);
            view.SettleForRender();
            var buttons = view.Column.Where(button => button.gameObject.activeSelf).ToList();
            foreach (var button in buttons)
            {
                button.Press.SettleForRender();
                button.Settle();
            }
            Place(plane, eyes, 0f, shot.Elevation);

            // What the view drew of it: the agent's card in the panel (its name as the title, what it is as its line, never
            // joined on a dot: the addendum's A17), and the page's own actions down the column, none of them main.
            var card = view.Card;
            var shownWords = card == null ? new List<string>() : card.Shown.Select(label => label.GetParsedText()).ToList();
            Require(view.Panel.gameObject.activeSelf && card != null && !view.Body.gameObject.activeSelf && shownWords.Count == 2 && shownWords[0] == "Glimmer 30B"
                && (shownWords[1] == AgentColumn.ModelWords || shownWords[1] == AgentColumn.AgentAppWords),
                "the panel does not hold the agent's card alone: \"" + string.Join(" / ", shownWords) + "\"");
            Require(!view.Sentence.gameObject.activeInHierarchy || string.IsNullOrEmpty(view.Sentence.text), "the panel has a sentence the page did not give");
            Require(buttons.Count == expected.Length, buttons.Count + " actions stand for " + expected.Length);
            for (var index = 0; index < buttons.Count && index < expected.Length; index++)
            {
                var button = buttons[index];
                var action = bar.Column[index];
                Require(button.Words.text == expected[index].Replace(" · ", "\n"), "action " + index + " reads \"" + button.Words.text + "\"");
                Require(view.ControlOf(button) == action.Control, expected[index] + " does not raise the page's own action");
                Require(button.Mark == action.Mark && button.Chosen == action.Chosen, expected[index] + " shows mark " + button.Mark + (button.Chosen ? ", chosen" : ""));
                Require(button.LookNow() == RoomButtonLook.Rest, expected[index] + " shows " + button.LookNow() + ", and nothing here is the main action");
                Require(button.transform.localPosition.x - button.Core.x / 2f > view.Panel.transform.localPosition.x + view.Panel.Size.x / 2f, expected[index] + " does not stand beside the panel");
                if (index > 0) Require(button.transform.localPosition.y < buttons[index - 1].transform.localPosition.y - button.Core.y * 0.9f, expected[index] + " does not stand under " + expected[index - 1]);
                var glyph = action.Mark != AnswerMark.None ? (action.Chosen ? RoomGlyph.Check : (RoomGlyph?)null)
                    : expected[index] == "Change" ? RoomGlyph.Pencil : expected[index] == "Back" ? RoomGlyph.Back : RoomGlyph.Next;
                Require(button.Glyph.Glyph == glyph, expected[index] + " carries " + (button.Glyph.Glyph?.ToString() ?? "no glyph"));
            }
            Require(buttons.Count(button => button.Chosen) == (state == "44-agent-plain" ? 0 : 1), "not exactly one answer is marked");

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                int Light(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                // An answer's mark is a ring (ae8a40df4, the one language): chosen, a white ring at full white round a white check; not chosen,
                // the ring's line at 80 percent and no check. Read by the ring's rim and the check's stroke, never by the glass in the ring's
                // middle (as 8d6018242 reads the ring): a chosen rim at the bright floor the old disc met, its check as bright, and no check
                // on an answer not chosen.
                var marks = new List<string>();
                foreach (var button in buttons.Where(button => button.Mark != AnswerMark.None))
                {
                    // The mark stands where the chip would: as far from its core's left edge as the chosen answer's check is from its own.
                    var chosen = buttons.First(each => each.Chosen);
                    var inset = chosen.Glyph.transform.localPosition.x + chosen.Core.x / 2f;
                    var centre = button.transform.TransformPoint(new Vector3(-button.Core.x / 2f + inset, 0f, 0f));
                    var radius = RoomType.Scaled(RoomButton.MarkDp) / 2f;
                    var rim = 0;
                    foreach (var inside in new[] { 0.5f, 1f, 1.5f })
                        for (var step = 0; step < 72; step++)
                        {
                            var turn = step * Mathf.PI * 2f / 72f;
                            var along = (radius - RoomType.Scaled(inside)) * new Vector3(Mathf.Cos(turn), Mathf.Sin(turn), 0f);
                            rim = Mathf.Max(rim, Light(At(dark, centre + button.transform.TransformVector(along))));
                        }
                    // The check's first stroke, at the chosen check's own place in the ring: the same offset in this answer's ring.
                    var tick = RoomGlyphs.Of(RoomGlyph.Check)[0].Middles().First();
                    var chosenCentre = chosen.transform.TransformPoint(new Vector3(-chosen.Core.x / 2f + inset, 0f, 0f));
                    var tickAt = chosen.Glyph.transform.TransformPoint(new Vector3(tick.x / RoomGlyphs.Grid - 0.5f, 0.5f - tick.y / RoomGlyphs.Grid, 0f)) - chosenCentre + centre;
                    var check = Light(At(dark, tickAt));
                    marks.Add(button.Words.text + " rim " + rim.ToString(CultureInfo.InvariantCulture) + " check " + check.ToString(CultureInfo.InvariantCulture));
                    if (button.Chosen && rim < 150) failures.Add(button.Words.text + " is chosen and its ring's rim reads " + rim + " of 255");
                    if (button.Chosen && check < 150) failures.Add(button.Words.text + " is chosen and its check reads " + check + " of 255");
                    if (!button.Chosen && check > 90) failures.Add(button.Words.text + " is not chosen and its ring holds a check reading " + check + " of 255");
                }
                // Every action's words are drawn: along each of its lines (an action's parts stand on lines of their own, never joined on
                // the dot), ink holding 4.5 to 1 against its own ground in either polarity, light ink at least as bright as before.
                foreach (var button in buttons)
                    if (InkFault(button.Words, world => At(dark, world), 170) is string fault) failures.Add(button.Words.text + ": " + fault);
                if (view.Card is RoomCardView shownCard && shownCard.Shown.Count > 0)
                {
                    var title = shownCard.Shown[0];
                    var bounds = title.textBounds;
                    var most = 0;
                    for (var step = 0; step <= 120; step++)
                        most = Mathf.Max(most, Light(At(dark, title.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 120f), bounds.max.y - KitType.LinePitch(title) * 0.5f, 0f)))));
                    if (most < 170) failures.Add("the agent's name is not in the pixels, the lightest reads " + most + " of 255");
                }
                Debug.Log("Halcyonic: parts: " + name + ": " + buttons.Count + " actions beside the fact, each the page's own" + (marks.Count > 0 ? "; marks " + string.Join(", ", marks) : "") + ".");
            };
            return shot;
        }
    }
}
