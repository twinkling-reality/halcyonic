#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// Ask beside the menu as the redesign draws it (gap 7; LOOP's AskMenuColumn): its frames through
    /// <see cref="RoomBar.OfFrame"/>, drawn by the room's own view. Measured: the card stands in the panel's place alone, no
    /// word joined on a dot or cut, the assistant's words on the band and never in the card, every action drawn, Close last,
    /// at most one main action and it first. Read in the pixels: the card's first words and every action's. Synthetic words only.
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] AskColumnStates =
        {
            "50-ask-writing", "50-ask-thinking", "50-ask-needs-you", "50-ask-answered", "50-ask-not-answered",
        };

        private const string AskColumnTime = "2026-11-11T09:00:00.000Z";
        private const string AskColumnRequest = "01a19000-0000-7000-8000-0000000000a4";
        private const string AskColumnQuestion = "Which page do you mean?";
        private const string AskColumnReply = "The sphere is blue now, and the check passed.";

        private static PartShot AskColumnShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var host = new AskColumnRenderHost();
            var commands = new CommandFactory(new ClientInfo { Name = "render", Version = null, DeviceLabel = null },
                () => DateTimeOffset.Parse(AskColumnTime, CultureInfo.InvariantCulture));
            var column = new AskMenuColumn(host, commands, AskScope.Project("01a19000-0000-7000-8000-0000000000a1", "Kite Run"));
            column.Ask.Offer(new AvailableWorkspaceAgent { Model = "muse-glimmer:30b-mlx" });
            column.Ask.Write("Make the sphere blue, then tell me whether it works.");
            if (state != "50-ask-writing")
            {
                var sent = column.Ask.Submit()!;
                column.Ask.Acknowledge(new CommandAckMessage
                {
                    CommandId = sent.CommandId,
                    Disposition = CommandAckDisposition.Accepted,
                    Command = new CommandView { CommandId = sent.CommandId, Status = CommandStatus.Completed, Result = new WorkspaceRequestCreatedResult { RequestId = AskColumnRequest } },
                });
                WorkspaceRequestView View(string status, string? reply) => new()
                {
                    RequestId = AskColumnRequest,
                    JournalId = "render",
                    ProjectId = column.Ask.Scope.ProjectId,
                    OriginalText = column.Ask.Draft,
                    Status = new JValue(status),
                    Reply = reply == null ? null : new WorkspaceRequestReply { Text = reply, Model = "muse-glimmer:30b-mlx" },
                    CreatedAt = AskColumnTime,
                    UpdatedAt = AskColumnTime,
                };
                switch (state)
                {
                    case "50-ask-needs-you": column.Ask.Read(View("waiting_for_human", AskColumnQuestion)); column.Heard("The home page"); break;
                    case "50-ask-answered": column.Ask.Read(View("responded", AskColumnReply)); break;
                    case "50-ask-not-answered": column.Ask.Read(View("interrupted", null)); break;
                    default: column.Ask.Read(View("running", null)); break;
                }
            }
            var frame = column.Frame!;
            var bar = RoomBar.OfFrame(frame, VoiceStage.Idle);
            Require(bar != null && bar.Card != null, "the Ask frame gives the redesign no card");
            if (bar == null) return shot;
            Require(bar.Card != null && !bar.Card.AnyDot, "the Ask card joins facts on a dot: " + (bar.Card == null ? "" : string.Join(" | ", bar.Card.Words())));
            Require(bar.Column.Count > 0 && bar.Column[bar.Column.Count - 1].Place == KitPlace.Close, "Close is not the last action");
            Require(bar.Column.Count(action => action.Main) <= 1, "more than one main action");
            Require(!bar.Column.Any(action => action.Main) || bar.Column[0].Main, "the main action is not first");
            var said = state switch { "50-ask-needs-you" => AskColumnQuestion, "50-ask-answered" => AskColumnReply, _ => null };
            Require(bar.Band == said, "the band holds " + (bar.Band ?? "nothing") + " where it should hold " + (said ?? "nothing"));
            if (said != null && bar.Card != null)
                Require(!bar.Card.Words().Any(words => words.Contains(said)), "the assistant's words stand in the card");

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
            Require(view.Panel.gameObject.activeSelf && card != null, "the panel does not hold the Ask card");
            if (card != null)
            {
                foreach (var label in card.Shown)
                {
                    label.ForceMeshUpdate(true);
                    var words = label.GetParsedText();
                    Require(label.textInfo.characterCount > 0 && !label.isTextOverflowing && !words.Contains("…"), "\"" + words + "\" is cut");
                    Require(!words.Contains(KitMomentWords.Dot.Trim()), "\"" + words + "\" joins facts on a dot");
                }
                Require(card.Plates.Count(each => each.What.StartsWith("slot", StringComparison.Ordinal)) == 0, "an Ask card lays a slot: the model is the foot line");
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
                if (view.Card is RoomCardView shown && shown.Shown.Count > 0 && Lightest(shown.Shown[0]) < 150) failures.Add("the card's first words are not in the pixels");
                foreach (var button in buttons)
                    if (Lightest(button.Words) < 150) failures.Add(button.Words.text + ": no words in the pixels");
                Debug.Log("Halcyonic: parts: " + name + ": the Ask card " + (view.Card == null ? "missing" : view.Card.Size.y.ToString("0.0", CultureInfo.InvariantCulture) + " units tall")
                    + ", " + buttons.Count + " actions: " + string.Join(", ", buttons.Select(button => button.Words.text)) + ".");
            };
            return shot;
        }

        /// <summary>The Ask's host in a render: the production font's measures, voice and the keyboard offered, nothing sent.</summary>
        private sealed class AskColumnRenderHost : IMenuHost
        {
            public ClientProjection State { get; } = new();
            public bool Connected => true;
            public bool Demonstration => false;
            public double Now => 0;
            public DateTimeOffset Clock => DateTimeOffset.Parse(AskColumnTime, CultureInfo.InvariantCulture);
            public TimeZoneInfo Zone => TimeZoneInfo.Utc;
            public TextSize TextSize => GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard;
            public bool VoiceOffered => true;
            public ControlPlaneApi? Api => null;
            public Task<CommandAckMessage>? Submit(CommandEnvelope command) => null;
            public bool KeyboardOffered => true;
            public void OpenKeyboard(string text, string prompt, Action<string> done, Action? dismissed = null) { }
            public int RowsOf(string words, float degrees) => MenuFrameView.RowsOf(words, degrees);
            public int RowsOf(PageLine line, float degrees) => MenuFrameView.RowsOf(line, degrees);
            public bool FitsHalf(PageLine line, float degrees) => MenuFrameView.FitsHalf(line, degrees);
            public int TitleRows(string subject, float degrees) => MenuFrameView.TitleRows(subject, degrees);
            public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);
            public float PageHeight(int rows, bool besideMenu, bool focused = false) => MenuPage.Height(TextSize, rows, besideMenu: besideMenu, focused: focused);
            public void OpenFile(string id) { }
            public void OpenNewProject(string? id, string? name) { }
        }
    }
}
