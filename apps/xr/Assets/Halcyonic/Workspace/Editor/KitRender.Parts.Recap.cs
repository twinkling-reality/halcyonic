#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// New Project's recap as the redesign draws it (the addendum's A2 in A17's language): the frame the flow's own screens make
    /// (<see cref="NewProjectScreens.Recap"/> under the redesign), through <see cref="RoomBar.NewProject"/>, drawn by the room's
    /// own view: the recap's card in the panel (the project's name, its first task, its folder, agent and level as slots) and
    /// Start Task, Change and Close in the column. Measured (the card stands, no word joined on a dot, no word cut, Start Task
    /// the one main action) and read in the pixels (the name and every action's words). Synthetic names only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] RecapStates = { "48-recap-built", "48-recap-change" };

        private static PartShot RecapShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var was = RoomDesign.Enabled;
            MenuFrame frame;
            try
            {
                RoomDesign.Enabled = true;
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                var idea = new ProjectIdea();
                idea.UseIdea("Make a kite that follows my hand, and climbs when I raise it");
                idea.Rename("Kite Run");
                var place = new LocationRoot { Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false, Folders = new List<LocationFolder>() };
                idea.ChooseFolder(ProjectFolder.New(place, "kite-run")!);
                var draft = new NewWorkDraft(commands);
                draft.ChooseRuntime(new RuntimeDescriptor
                {
                    RuntimeId = "local", DisplayName = "Ember App", Kind = "render", Synthetic = false, ModelChoice = ModelChoice.Listed, UsesProjectLocation = true, OffersNeverAsk = true,
                    Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, RespondToApproval = true, AnswerQuestion = true, Interrupt = true },
                });
                draft.SetModels(new RuntimeModelsResponse
                {
                    RuntimeId = "local",
                    Result = new AvailableModels { Models = new List<RuntimeModel> { new() { ModelRef = "render/glimmer-30b", DisplayName = "Glimmer 30B", Served = ModelServed.ThisMac, ToolCalling = ModelToolCalling.Declared } } },
                });
                draft.NeverAsk = true;
                // The recap, or its page of what can be changed: a list card picked from, the levels as answers in the column.
                frame = state == "48-recap-change"
                    ? NewProjectScreens.RecapChange(idea, true, false, null, true, draft)
                    : NewProjectScreens.Recap(idea, draft, null, true, null, null);
            }
            finally { RoomDesign.Enabled = was; }
            var bar = RoomBar.NewProject(frame, VoiceStage.Idle);
            Require(bar != null && bar.Card != null, "New Project's recap gives the redesign no card");
            if (bar == null) return shot;
            Require(bar.Card != null && !bar.Card.AnyDot, "the recap's card joins facts on a dot: " + (bar.Card == null ? "" : string.Join(" | ", bar.Card.Words())));
            var changing = state == "48-recap-change";
            if (changing)
            {
                Require(bar.Card != null && bar.Card.List.Count >= 4 && bar.Card.List.All(row => row.Press != null && row.Kind != RoomKind.None), "the page of changes is not a list of marked rows picked from");
                Require(bar.Column.Count <= RoomBar.ColumnMost && bar.Column.Count(action => action.Main) == 0, "the page of changes' column holds " + bar.Column.Count + " actions or a main one");
                Require(bar.Column.Count(action => action.Control.Place == KitPlace.Choice) == 2, "the levels do not stand in the column as answers");
            }
            else Require(bar.Column.Count > 0 && bar.Column[0].Main && bar.Column.Count(action => action.Main) == 1, "Start Task is not the one main action, first");

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

            var card = view.Card;
            Require(view.Panel.gameObject.activeSelf && card != null && !view.Body.gameObject.activeSelf, "the panel does not hold the recap's card alone");
            if (card != null)
            {
                foreach (var label in card.Shown)
                {
                    label.ForceMeshUpdate(true);
                    var words = label.GetParsedText();
                    Require(label.textInfo.characterCount > 0 && !label.isTextOverflowing && !words.Contains("…"), "\"" + words + "\" is cut");
                    Require(!words.Contains(KitMomentWords.Dot.Trim()), "\"" + words + "\" joins facts on a dot");
                }
                Require(card.Plates.Count(each => each.What.StartsWith("slot", StringComparison.Ordinal)) == bar.Card!.Slots.Count, "the card does not lay each fact as a slot");
                if (changing) Require(card.RowTargets.Count() == bar.Card!.List.Count, "a row of the page of changes takes no press");
            }
            Require(buttons.Count == bar.Column.Count, "the column draws " + buttons.Count + " of " + bar.Column.Count + " actions");

            shot.Read = (camera, eye, white, dark, bright, failures) =>
            {
                Color32 At(Color32[] pixels, Vector3 world)
                {
                    var screen = camera.WorldToScreenPoint(world);
                    return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                }
                int Light(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                int Lightest(TMPro.TMP_Text label)
                {
                    var bounds = label.textBounds;
                    var most = 0;
                    for (var step = 0; step <= 80; step++)
                        most = Mathf.Max(most, Light(At(dark, label.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 80f), bounds.max.y - KitType.LinePitch(label) * 0.5f, 0f)))));
                    return most;
                }
                if (view.Card is RoomCardView shown && shown.Shown.Count > 0 && Lightest(shown.Shown[0]) < 170) failures.Add("the project's name is not in the pixels");
                foreach (var button in buttons)
                    if (Lightest(button.Words) < 150) failures.Add(button.Words.text + ": no words in the pixels");
                Debug.Log("Halcyonic: parts: " + name + ": the recap's card " + (view.Card == null ? "missing" : view.Card.Size.y.ToString("0.0", CultureInfo.InvariantCulture) + " units tall")
                    + ", " + buttons.Count + " actions: " + string.Join(", ", buttons.Select(button => button.Words.text)) + ".");
            };
            return shot;
        }
    }
}
