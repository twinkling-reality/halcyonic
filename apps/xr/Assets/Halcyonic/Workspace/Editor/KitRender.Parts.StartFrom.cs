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
    /// What a new project starts from (SCREENS, the addendum's A16, <see cref="StarterChoice"/>), as the redesign draws it:
    /// the page of changes with Start From after First Task (<c>48-start-from-change</c>), the choice itself with the ready
    /// starters as answers, the chosen one marked, and Back (<c>48-start-from</c>), and the recap's card naming the starter
    /// under the cube after the folder (<c>48-start-from-recap</c>). Each frame is the flow's own screens' through
    /// <see cref="RoomBar.NewProject"/>, drawn by the room's own view. Measured (no word cut, no fact joined on a dot, every
    /// action drawn, one main action at most, one answer chosen) and read in the pixels (every action's words). Part of
    /// <c>KitRender.PartsCheck</c>. Synthetic names only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] StartFromStates = { "48-start-from-change", "48-start-from", "48-start-from-recap" };

        private static PartShot StartFromShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var was = RoomDesign.Enabled;
            RoomBar? bar;
            var idea = new ProjectIdea();
            try
            {
                RoomDesign.Enabled = true;
                var commands = new CommandFactory(new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" });
                idea.UseIdea("Make a kite that follows my hand, and climbs when I raise it");
                idea.Rename("Kite Run");
                var place = new LocationRoot { Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false, Folders = new List<LocationFolder>() };
                idea.ChooseFolder(ProjectFolder.NewStarter(place, "kite-run", "world")!);
                var starters = new StartersResponse
                {
                    Starters = new List<StarterView>
                    {
                        new() { StarterId = "iwsdk-vr", Title = "VR Game", Installed = true },
                        new() { StarterId = "world", Title = "A World of Your Own", Installed = true },
                    },
                };
                var ready = StarterChoice.Ready(starters);
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
                MenuFrame frame = state switch
                {
                    "48-start-from-change" => NewProjectScreens.RecapChange(idea, false, false, null, true, draft, startFrom: StarterChoice.Offered(idea, ready)),
                    "48-start-from" => StarterChoice.Page(idea, ready, 0, false),
                    _ => NewProjectScreens.Recap(idea, draft, null, true, null, null),
                };
                if (state == "48-start-from-recap" && frame.Card is RoomCard recap) frame = frame.WithCard(StarterChoice.WithStarter(recap, StarterChoice.Chosen(idea, starters)));
                bar = RoomBar.NewProject(frame, VoiceStage.Idle);
            }
            finally { RoomDesign.Enabled = was; }
            Require(bar != null, "New Project's page gives the redesign no bar");
            if (bar == null) return shot;
            Require(bar.Column.Count(action => action.Main) <= 1, "more than one main action");
            switch (state)
            {
                case "48-start-from-change":
                    // The page of changes is a list card (FIELD's): its rows, then its column.
                    var words = (bar.Card?.List.Select(row => row.Name.Text) ?? Enumerable.Empty<string>()).Concat(bar.Column.Select(action => action.Words)).ToList();
                    Require(words.IndexOf(StarterChoice.StartFrom) == words.IndexOf(EntryText.FirstTask) + 1, "Start From does not stand after First Task: " + string.Join(", ", words));
                    break;
                case "48-start-from":
                    Require(bar.Body.Text == StarterChoice.StartFrom, "the panel does not read Start From: " + bar.Body.Text);
                    Require(bar.Column.Count(action => action.Control.Place == KitPlace.Choice) == 2 && bar.Column.Count(action => action.Chosen) == 1, "the starters are not two answers, one chosen");
                    Require(bar.Column.Last().Words == EntryText.Back, "Back is not last");
                    break;
                default:
                    // A card of four facts (folder, the agent app, its model, the level) has no room: the review names the starter, and
                    // the one press does not send it unseen (StarterChoice.Shown). One that has room names it.
                    var hasRoom = bar.Card == null || bar.Card.Slots.Count < RoomCard.MostSlots || bar.Card.Slots.Any(slot => slot.Kind == RoomKind.Starter);
                    if (bar.Card != null && hasRoom) Require(bar.Card.Slots.Any(slot => slot.Kind == RoomKind.Starter && slot.Value.Text == "A World of Your Own"), "the recap's card does not name the starter");
                    if (bar.Card != null && !hasRoom) Require(!StarterChoice.Shown(bar.Card, idea), "a picked starter off a full card would be sent unseen");
                    Require(bar.Card != null && !bar.Card.AnyDot, "the recap's card joins facts on a dot");
                    break;
            }

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
            Require(buttons.Count == bar.Column.Count, "the column draws " + buttons.Count + " of " + bar.Column.Count + " actions");
            foreach (var button in buttons)
            {
                button.Words.ForceMeshUpdate(true);
                var said = button.Words.GetParsedText();
                Require(!button.Words.isTextOverflowing && !said.Contains("…"), "\"" + said + "\" is cut");
            }
            if (view.Card is RoomCardView card)
            {
                foreach (var label in card.Shown)
                {
                    label.ForceMeshUpdate(true);
                    var said = label.GetParsedText();
                    Require(label.textInfo.characterCount > 0 && !label.isTextOverflowing && !said.Contains("…"), "\"" + said + "\" is cut");
                    Require(!said.Contains(KitMomentWords.Dot.Trim()), "\"" + said + "\" joins facts on a dot");
                }
                Require(card.Plates.Count(each => each.What.StartsWith("slot", StringComparison.Ordinal)) == bar.Card!.Slots.Count, "the card does not lay each fact as a slot");
            }
            if (state == "48-start-from")
            {
                view.Body.ForceMeshUpdate(true);
                Require(view.Panel.gameObject.activeSelf && view.Body.gameObject.activeSelf && !view.Body.isTextOverflowing, "the panel does not show Start From whole");
            }

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
                foreach (var button in buttons)
                    if (Lightest(button.Words) < 150) failures.Add(button.Words.text + ": no words in the pixels");
                if (state == "48-start-from" && Lightest(view.Body) < 150) failures.Add("Start From is not in the pixels");
                Debug.Log("Halcyonic: parts: " + name + ": " + buttons.Count + " actions: " + string.Join(", ", buttons.Select(button => button.Words.text))
                    + (view.Card == null ? "" : ", the card " + view.Card.Size.y.ToString("0.0", CultureInfo.InvariantCulture) + " units tall") + ".");
            };
            return shot;
        }
    }
}
