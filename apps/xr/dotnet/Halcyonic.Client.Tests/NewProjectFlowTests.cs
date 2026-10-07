using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

public class NewProjectFlowTests
{
    private static readonly CommandFactory Commands = new(Samples.Client);

    /// <summary>The director's side, as a test plays it: what it reads, what it sends, and the keyboard's answers.</summary>
    private sealed class Host : IMenuHost
    {
        public ClientProjection? State { get; set; } = With();

        public bool Connected { get; set; } = true;

        public bool Demonstration { get; set; }

        public double Now { get; set; }

        public bool VoiceOffered { get; set; } = true;

        public ControlPlaneApi? Api { get; set; }

        public List<CommandEnvelope> Sent { get; } = new();

        /// <summary>How each send is acknowledged; by default never, so the projection decides.</summary>
        public Func<CommandEnvelope, Task<CommandAckMessage>> Acknowledge { get; set; } = _ => new TaskCompletionSource<CommandAckMessage>().Task;

        public bool KeyboardOffered { get; set; } = true;

        public Queue<string> Typed { get; } = new();

        public List<string> Prompts { get; } = new();

        public Task<CommandAckMessage>? Submit(CommandEnvelope command)
        {
            if (!Connected) return null;
            Sent.Add(command);
            return Acknowledge(command);
        }

        public void OpenKeyboard(string text, string prompt, Action<string> done)
        {
            Prompts.Add(prompt);
            if (Typed.Count > 0) done(Typed.Dequeue());
        }

        public DateTimeOffset Clock => DateTimeOffset.Parse(Samples.Time).AddSeconds(Now);

        public TimeZoneInfo Zone => TimeZoneInfo.Utc;

        public TextSize TextSize { get; set; } = TextSize.Standard;

        /// <summary>The characters a row holds at each text size; a test narrows it to wrap lines as a narrow column does.</summary>
        public Func<TextSize, int> RowWidth { get; set; } = _ => 60;

        public int RowsOf(string words, float columnDegrees) => Math.Max(1, (words.Length + RowWidth(TextSize) - 1) / RowWidth(TextSize));

        public int RowsOf(PageLine line, float columnDegrees) => RowsOf(line.Words, columnDegrees);

        public bool FitsHalf(PageLine answer, float columnDegrees) => answer.Words.Length <= 24;

        public int TitleRows(string subject, float columnDegrees) => subject.Length <= 36 ? 1 : 2;

        public int PageRows(bool sourceLine) => MenuFrame.RowsAPage(TextSize, sourceLine);

        /// <summary>The page's room where a test sets it, as the head moves on the stage; else a Quest 3S's, strictly.</summary>
        public Func<int, bool, float>? Height { get; set; }

        /// <summary>How often the page's room was read.</summary>
        public int HeightReads { get; private set; }

        public float PageHeight(int subjectRows, bool besideMenu)
        {
            HeightReads++;
            return Height?.Invoke(subjectRows, besideMenu) ?? MenuPage.Height(TextSize, subjectRows, besideMenu: besideMenu);
        }

        public void OpenFile(string workstreamId)
        {
        }

        public void OpenNewProject(string? projectId, string? projectName)
        {
        }
    }

    private sealed class Kept : IKeptCommand
    {
        public string? Id { get; set; }
    }

    private sealed class Memory : ICreationDraftStore
    {
        public List<CreationDraft> Drafts { get; private set; } = new();

        public IReadOnlyList<CreationDraft> Load() => Drafts;

        public void Save(IReadOnlyList<CreationDraft> drafts) => Drafts = drafts.ToList();
    }

    /// <summary>The control plane's HTTP API, answering each path with what a test gave it, and counting what was asked.</summary>
    private sealed class Routes : HttpMessageHandler
    {
        public Dictionary<string, Func<string>> Answers { get; } = new();

        public List<string> Asked { get; } = new();

        /// <summary>Paths refused with a status and the control plane's error body.</summary>
        public Dictionary<string, (HttpStatusCode Status, string Body)> Refusals { get; } = new();

        /// <summary>Paths whose request never gets a reply, failing with this socket error.</summary>
        public Dictionary<string, string> Unreachable { get; } = new();

        /// <summary>Paths answered only after this long, unless the request gives up first.</summary>
        public Dictionary<string, TimeSpan> Delays { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Method.Method + " " + request.RequestUri!.AbsolutePath;
            Asked.Add(key);
            if (Delays.TryGetValue(key, out var delay)) await Task.Delay(delay, cancellationToken);
            return await Answer(key);
        }

        private Task<HttpResponseMessage> Answer(string key)
        {
            if (Unreachable.TryGetValue(key, out var socket)) return Task.FromException<HttpResponseMessage>(new HttpRequestException(socket));
            if (Refusals.TryGetValue(key, out var refusal))
            {
                return Task.FromResult(new HttpResponseMessage(refusal.Status) { Content = new StringContent(refusal.Body, Encoding.UTF8, "application/json") });
            }
            var found = Answers.TryGetValue(key, out var answer);
            return Task.FromResult(new HttpResponseMessage(found ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(found ? answer!() : "{}", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static ClientProjection With(params CommandView[] commands)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Commands = commands.ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    private static CommandView Completed(CommandEnvelope command, CommandResult result) => new()
    {
        CommandId = command.CommandId, Status = CommandStatus.Completed, Result = result, IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
    };

    private static NewProjectFlow Flow(Host host, Kept? kept = null, ICreationDraftStore? store = null) =>
        new(host, Commands, kept ?? new Kept(), store);

    /// <summary>
    /// A press as the person makes one: on the frame the director drew for them, turning the page first,
    /// as they would, while the keyed line they press stands on another page of it. A prompt stands on
    /// every page, so none is looked for elsewhere.
    /// </summary>
    private static void Press(NewProjectFlow flow, string id, string? key)
    {
        for (var turned = 0; turned < 12 && key != null && flow.Frame is MenuFrame frame && !Shows(frame, id, key) && Turn(frame) is { } turn; turned++)
        {
            Draw(flow, frame);
            flow.Act(turn.Id, turn.Key);
        }
        if (flow.Frame is MenuFrame drawn) Draw(flow, drawn);
        flow.Act(id, key);
        if (flow.Frame is MenuFrame after) NeverFourPrompts(after);
    }

    /// <summary>The director draws the frame, as it would after checking it holds never four prompts.</summary>
    private static void Draw(NewProjectFlow flow, MenuFrame frame)
    {
        NeverFourPrompts(frame);
        flow.Drawn(frame, null);
    }

    /// <summary>Never four prompts in a footer (ADR 0026): every frame a test reaches through a press or a turn is checked.</summary>
    private static void NeverFourPrompts(MenuFrame frame) =>
        Assert.That(frame.Footer.All.Count(), Is.LessThanOrEqualTo(3), frame.Subject + ": " + string.Join(", ", frame.Footer.All.Select(each => each.Prompt.Words)));

    /// <summary>
    /// What turns a frame's page, as the person finds it: the footer's Next page, where a list pages as
    /// the menu's lists do; else the row at the page's end, more answers, the next part or the next
    /// page; null where it doesn't page.
    /// </summary>
    private static (string Id, string? Key, string Words)? Turn(MenuFrame frame)
    {
        if (frame.Footer.All.Select(each => each.Prompt).FirstOrDefault(prompt => prompt.Kind == PromptKind.NextPage) is Prompt pager) return (pager.Id, null, pager.Words);
        return frame.Lines.LastOrDefault(line => line.Action is NewProjectScreens.NextPage or NewProjectScreens.MoreAnswers or NewProjectScreens.NextPart) is { } row
            ? (row.Action!, row.Key, row.Words)
            : null;
    }

    /// <summary>Whether a line only turns the page.</summary>
    private static bool Turns(PageLine line) => line.Action is NewProjectScreens.NextPage or NewProjectScreens.MoreAnswers or NewProjectScreens.NextPart;

    /// <summary>
    /// Every page of what shows, as the person turns them, from the one showing on round to one already
    /// seen; one page when everything fits.
    /// </summary>
    private static List<MenuFrame> Pages(NewProjectFlow flow)
    {
        static string Seen(MenuFrame frame) => string.Join("|", frame.Lines.Select(line => line.Words + "#" + line.Key));
        var pages = new List<MenuFrame> { flow.Frame! };
        var seen = new HashSet<string> { Seen(pages[0]) };
        while (pages.Count < 12 && Turn(pages[^1]) is { } turn)
        {
            Draw(flow, pages[^1]);
            flow.Act(turn.Id, turn.Key);
            if (!seen.Add(Seen(flow.Frame!))) break;
            pages.Add(flow.Frame!);
        }
        return pages;
    }

    /// <summary>Whether a frame shows a line or prompt raising <paramref name="id"/> with <paramref name="key"/>, whatever it allows now.</summary>
    private static bool Shows(MenuFrame frame, string id, string? key) =>
        frame.Footer.All.Any(each => each.Prompt.Id == id) || frame.Lines.Any(line => line.Action == id && line.Key == key) || id == MenuFrame.ChooseSection
        || id == Footer.Close;

    /// <summary>A suggestion's key, on the companion's question showing now.</summary>
    private static string Suggestion(NewProjectFlow flow, string words) => NewProjectScreens.AnswerKey(flow.Idea!.Companion!.Generation, words);

    /// <summary>A fixed answer's key, on the fixed question showing now.</summary>
    private static string Fixed(NewProjectFlow flow, string words) => NewProjectScreens.AnswerKey(flow.Idea!.Question, words);

    /// <summary>Waits for what the flow asked the control plane, as its director's frames would.</summary>
    private static async Task Until(NewProjectFlow flow, Func<bool> done)
    {
        for (var tries = 0; tries < 200 && !done(); tries++)
        {
            await Task.Delay(10);
            flow.Tick();
        }
        Assert.That(done(), Is.True, "the flow got its answer");
    }

    /// <summary>A new project's idea typed, an agent app chosen, and the recap showing.</summary>
    private static NewProjectFlow Recapped(Host host, Kept? kept = null, ICreationDraftStore? store = null)
    {
        var flow = Flow(host, kept, store);
        flow.Open(null, null);
        host.Typed.Enqueue("A page of race times for my running club");
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Press(flow, NewProjectScreens.ChooseRuntime, "mock");
        Press(flow, NewProjectScreens.Done, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(flow.Idea!.HasRecap, Is.True);
        return flow;
    }

    /// <summary>The review read part by part, as the director draws each and the person presses Next part a second later.</summary>
    private static void ReadToTheEnd(NewProjectFlow flow, Host host)
    {
        Draw(flow, flow.Frame!);
        for (var part = 0; part < 50 && flow.Review?.CanConfirm != true; part++)
        {
            host.Now += 1;
            Press(flow, NewProjectScreens.NextPart, null);
            Draw(flow, flow.Frame!);
        }
    }

    private static IEnumerable<string> Ids(MenuFrame frame) =>
        frame.Footer.All.Select(each => each.Prompt.Id).Concat(frame.Lines.Where(line => line.Action != null).Select(line => line.Action!));

    [Test]
    public void NothingIsSentUntilTheReviewIsReadToItsEndAndItsYesPressed()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build));
        Assert.That(Ids(flow.Frame!), Does.Not.Contain(NewProjectScreens.ConfirmStart));
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty, "a Yes pressed before the review was read sends nothing");

        ReadToTheEnd(flow, host);
        Assert.That(flow.Review!.CanConfirm, Is.True);
        Assert.That(Ids(flow.Frame!), Does.Contain(NewProjectScreens.ConfirmStart));
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent.Single(), Is.InstanceOf<ProjectCreateCommand>());
        Assert.That(((ProjectCreateCommand)host.Sent[0]).Payload.Name, Is.EqualTo(flow.Idea!.Name));
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "a review confirms one send");
    }

    /// <summary>The footer <paramref name="frame"/>'s side panel shows standing in the page's place, as the navigator tells the flow it drew.</summary>
    private static Footer CarriedInPlace(MenuFrame frame) => frame.Side is SidePanel side ? frame.Footer.InPlace(side) : SidePanel.Footer;

    [Test]
    public void ASidePanelDrawnBesideItsPageTakesOnlyItsOwnClose()
    {
        // Beside the page the panel shows Close details alone: what it would carry in the page's place does not act from it.
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        var frame = flow.Frame!;
        var change = frame.Footer[PromptSlot.Rare];
        Assert.That(change, Is.Not.Null, "the chosen fact's change in the frame's footer");
        Assert.That(CarriedInPlace(frame).All.Select(each => each.Prompt.Id), Does.Contain(change!.Id), "in the page's place the panel would carry it");
        flow.Drawn(frame, SidePanel.Footer);
        flow.Act(change.Id, null);
        Assert.That((flow.Frame!.Side?.Subject, flow.Frame!.Lines.Any(line => line.Key == NewProjectScreens.FactKey(RecapFact.HowItRuns))),
            Is.EqualTo((EntryText.HowItRuns, true)), "the change, never shown on the panel beside the page, took nothing");
        flow.Act(SidePanel.Close, null);
        Assert.That(flow.Frame!.Side, Is.Null, "Close details took");
    }

    /// <summary>What the plane draws of <paramref name="frame"/> with text a step larger, its side panel in the page's place (MenuPlane, MenuFrameView): the prompts that panel carries.</summary>
    private static IEnumerable<string> DrawnInPlace(MenuFrame frame)
    {
        Assert.That(MenuColumns.Arrange(menuOpen: false, fileOpen: true, sidePanel: true, fitsBeside: true, sideInPlace: true), Is.EqualTo(new[] { MenuColumn.Side }));
        return frame.Footer.InPlace(frame.Side!).All.Select(each => each.Prompt.Id);
    }

    [Test]
    public void AtLargerTextAChosenFactsChangeIsOfferedInWhatIsDrawn()
    {
        foreach (var fact in new[] { RecapFact.Name, RecapFact.FirstTask, RecapFact.Folder, RecapFact.HowItRuns, RecapFact.StartOver })
        {
            var host = new Host();
            var flow = Recapped(host);
            host.TextSize = TextSize.Larger;
            flow.Tick();
            Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(fact));
            var frame = flow.Frame!;
            var change = frame.Footer[PromptSlot.Rare];
            Assert.That((frame.Side != null, change != null), Is.EqualTo((true, true)), fact + ": its side panel open and its change in the frame's footer");
            // Only the side panel drawn, in the page's place: its change and Start building stand there, and its change acts.
            flow.Drawn(frame, CarriedInPlace(frame));
            Assert.That(DrawnInPlace(frame), Is.SupersetOf(new[] { SidePanel.Close, change!.Id, NewProjectScreens.StartBuilding }), fact + "'s change is drawn");
            flow.Act(change.Id, null);
            if (fact == RecapFact.StartOver) Assert.That(flow.Frame!.Footer.Confirming, Is.True, "Start over asks");
            else Assert.That(flow.Frame!.Lines.Any(line => line.Key == NewProjectScreens.FactKey(fact)), Is.False, fact + "'s change opened its page");
        }
    }

    [Test]
    public void AfterASidePanelDrawnInThePagesPlaceOnlyItAndWhatItCarriesTakeAPress()
    {
        // The director tells of a side panel drawn alone the same way at any size; here its page holds another row.
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        var frame = flow.Frame!;
        flow.Drawn(frame, CarriedInPlace(frame));
        Assert.That(frame.Lines.Any(line => line.Key == NewProjectScreens.FactKey(RecapFact.StartOver)), Is.True, "Start over's row on this page, undrawn");

        // The page it hides: its rows, its steps and the frame's own Close are not drawn, so none acts.
        flow.Act(NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.StartOver));
        flow.Act(MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.YourIdea));
        flow.Act(Footer.Close, null);
        Assert.That((flow.IsOpen, flow.Step, flow.Frame!.Side?.Subject), Is.EqualTo((true, NewProjectStep.Recap, EntryText.HowItRuns)), "nothing on the undrawn page took a press");

        // What it carried does, and so does its Close.
        flow.Act(SidePanel.Close, null);
        Assert.That(flow.Frame!.Side, Is.Null, "Close details took");
    }

    [Test]
    public void AtLargerTextStartOverAsksWithCancelAndYesAndItsYesActsOnlyOnItsOwnFrame()
    {
        var host = new Host();
        var flow = Recapped(host);
        host.TextSize = TextSize.Larger;
        flow.Tick();
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.StartOver));
        var chosen = flow.Frame!;
        flow.Drawn(chosen, CarriedInPlace(chosen));
        flow.Act(NewProjectScreens.StartOver, null);
        var asking = flow.Frame!;
        Assert.That(asking.Footer.Confirming, Is.True);

        // Not yet drawn, its Yes is not what the person sees: the panel drawn carried Start over, not Yes.
        flow.Act(NewProjectScreens.ConfirmStartOver, null);
        Assert.That(flow.Idea!.HasRecap, Is.True, "a Yes no one saw takes nothing");

        // Drawn in the page's place, both Cancel and Yes stand there, and the panel says what Yes clears.
        flow.Drawn(asking, CarriedInPlace(asking));
        Assert.That(DrawnInPlace(asking), Is.SupersetOf(new[] { NewProjectScreens.Cancel, NewProjectScreens.ConfirmStartOver }));
        Assert.That(asking.Side!.Lines.Single().Words, Is.EqualTo(EntryText.StartOverClears(false)));
        flow.Act(NewProjectScreens.ConfirmStartOver, null);
        Assert.That((flow.Step, flow.Idea!.HasRecap), Is.EqualTo((NewProjectStep.YourIdea, false)), "Yes, start over took, from its own frame");
    }

    [Test]
    public void CloseDetailsLetsGoOfStartOversQuestionSoAYesFromTheSameDrawTakesNothing()
    {
        var host = new Host();
        var flow = Recapped(host);
        host.TextSize = TextSize.Larger;
        flow.Tick();
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.StartOver));
        flow.Drawn(flow.Frame!, CarriedInPlace(flow.Frame!));
        flow.Act(NewProjectScreens.StartOver, null);
        flow.Drawn(flow.Frame!, CarriedInPlace(flow.Frame!));
        Assert.That(flow.Frame!.Footer.Confirming, Is.True);
        // Close details, then Yes before anything new is drawn: the question closed with the details.
        flow.Act(SidePanel.Close, null);
        flow.Act(NewProjectScreens.ConfirmStartOver, null);
        Assert.That(flow.Idea!.HasRecap, Is.True, "nothing cleared");
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.StartOver));
        Assert.That(flow.Frame!.Footer.Confirming, Is.False, "chosen again, Start over asks only once pressed again");
    }

    [Test]
    public void AStaleStartOverPressIsRefusedNotArmedOutOfSight()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.StartOver));
        Draw(flow, flow.Frame!);
        flow.Act(NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        var changes = 0;
        flow.Changed += () => changes++;
        // Start over from the frame still showing, its row no longer chosen: refused, nothing redrawn.
        flow.Act(NewProjectScreens.StartOver, null);
        Assert.That(changes, Is.EqualTo(0), "the press took nothing");
    }

    [Test]
    public void StartOverClearsTheIdeaAndKeepsHowItRunsAsItsSidePanelSays()
    {
        var host = new Host();
        var flow = Recapped(host);
        string RunsWith() => Pages(flow).SelectMany(page => page.Lines).Single(line => line.Key == NewProjectScreens.FactKey(RecapFact.HowItRuns)).Words;
        var runsWith = RunsWith();
        Press(flow, NewProjectScreens.StartOver, null);
        Assert.That(flow.Frame!.Footer.Confirming, Is.False, "Start over arms nothing until its row is chosen");
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.StartOver));
        Assert.That(flow.Frame!.Side!.Lines.Single().Words, Is.EqualTo(EntryText.StartOverClears(false)));
        Press(flow, NewProjectScreens.StartOver, null);
        Press(flow, NewProjectScreens.ConfirmStartOver, null);
        Assert.That((flow.Step, flow.Idea!.HasRecap), Is.EqualTo((NewProjectStep.YourIdea, false)));

        host.Typed.Enqueue("A page of race times for my running club");
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Assert.That((flow.Step, flow.Frame!.Side), Is.EqualTo((NewProjectStep.Recap, (SidePanel?)null)), "no fact chosen on the new recap");
        Assert.That(RunsWith(), Is.EqualTo(runsWith), "how it runs stays");
    }

    [Test]
    public void StartOverForATaskKeepsTheProjectAndItsNameAsItsSidePanelSays()
    {
        var host = new Host();
        var flow = Flow(host);
        flow.Open(Samples.ProjectId, "Sample");
        host.Typed.Enqueue("Add a page of results");
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Press(flow, NewProjectScreens.ChooseRuntime, "mock");
        Press(flow, NewProjectScreens.Done, null);
        Assert.That((flow.Step, flow.Idea!.HasRecap), Is.EqualTo((NewProjectStep.Recap, true)));
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.StartOver));
        Assert.That(flow.Frame!.Side!.Lines.Single().Words, Is.EqualTo(EntryText.StartOverClears(true)));
        Press(flow, NewProjectScreens.StartOver, null);
        Press(flow, NewProjectScreens.ConfirmStartOver, null);
        Assert.That((flow.Step, flow.Idea!.HasRecap, flow.Idea.ExistingProjectId, flow.Idea.Name),
            Is.EqualTo((NewProjectStep.YourIdea, false, Samples.ProjectId, "Sample")), "the task's idea cleared, the project and its name kept");
    }

    [Test]
    public void StartOverActsOnlyWhileItsRowIsChosenEvenFromAFrameStillShowing()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.StartOver));
        var drawn = flow.Frame!;
        Draw(flow, drawn);
        // Two presses on the frame still showing: another row on its page first, then Start over, which no longer stands
        // (choosing a row disarms it, and the recap asks only while Start over's row is chosen).
        Assert.That(drawn.Lines.Any(line => line.Key == NewProjectScreens.FactKey(RecapFact.HowItRuns)), Is.True);
        flow.Act(NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Assert.That(flow.Frame!.Side, Is.Not.Null, "How it runs chosen");
        flow.Act(NewProjectScreens.StartOver, null);
        Assert.That(flow.Frame!.Footer.Confirming, Is.False, "Start over arms nothing once its row isn't chosen");
        flow.Act(NewProjectScreens.ConfirmStartOver, null);
        Assert.That(flow.Idea!.HasRecap, Is.True);
    }

    [Test]
    public void OnlyWhatTheFrameShowingOffersActs()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ConfirmStartOver, null);
        Assert.That(flow.Idea!.HasRecap, Is.True, "Yes, start over stands nowhere until Start over is pressed");
        Press(flow, NewProjectScreens.Rename, null);
        Assert.That(flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.TypeWords), Is.False, "Change stands only beside a chosen fact");
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Questions));
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap), "a step not reached takes no press");
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Press(flow, NewProjectScreens.ChooseSuggestion, "Each runner");
        Assert.That(host.Sent, Is.Empty);
        flow.Close();
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "closed, nothing acts");
    }

    [Test]
    public void ChoosingAStepThrowsTheReviewAwaySoTheRequestIsReadAgain()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Assert.That(flow.Review!.CanConfirm, Is.True);

        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Recap));
        Assert.That(flow.Review, Is.Null);
        host.Typed.Enqueue("Race Times, renamed on the way back");
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Name));
        Press(flow, NewProjectScreens.Rename, null);
        Press(flow, NewProjectScreens.TypeWords, null);
        Press(flow, NewProjectScreens.Done, null);
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Build));
        Assert.That(flow.Review!.CanConfirm, Is.False, "a fresh review, read again");
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(((ProjectCreateCommand)host.Sent.Single()).Payload.Name, Is.EqualTo("Race Times, renamed on the way back"));
    }

    [Test]
    public void WhileABuildIsOnItsWayNothingMoreStartsAndStartBuildingSaysWhy()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(flow.Sequence!.InFlight, Is.True);

        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Recap));
        var recap = flow.Frame!;
        Assert.That((recap.Footer[PromptSlot.FarRight]!.Available, recap.Reason), Is.EqualTo((false, EntryText.AlreadyStarting)));
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "no review while the build is on its way");
        Assert.That(host.Sent, Has.Count.EqualTo(1));
    }

    [Test]
    public void TryAgainAfterARefusalReadsTheRequestAgainBeforeAnythingIsSent()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        var create = host.Sent.Single();
        host.State = With(new CommandView
        {
            CommandId = create.CommandId, Status = CommandStatus.Rejected, IssuedAt = Samples.Time, UpdatedAt = Samples.Time,
            Rejection = new CommandRejection { Code = RejectionCode.InvalidRuntimeOptions, Message = "Not now." },
        });
        flow.Tick();
        Assert.That(flow.Sequence!.CanRetry, Is.True);
        Assert.That(Ids(flow.Frame!), Does.Contain(NewProjectScreens.TryAgainStart));

        Press(flow, NewProjectScreens.TryAgainStart, null);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "Try again opens the review; it sends nothing");
        Assert.That(flow.Review!.CanConfirm, Is.False);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Has.Count.EqualTo(2));
        Assert.That(host.Sent[1], Is.InstanceOf<ProjectCreateCommand>());
    }

    [Test]
    public void AnUnknownOutcomeIsKeptOnTheDeviceAndComesFirstUntilCleared()
    {
        var host = new Host
        {
            Acknowledge = command => Task.FromException<CommandAckMessage>(new CommandOutcomeUnknownException(command.CommandId, "The socket closed.")),
        };
        var kept = new Kept();
        var flow = Recapped(host, kept);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        host.State = With();
        flow.Tick();
        Assert.That(kept.Id, Is.EqualTo(host.Sent.Single().CommandId), "kept, so a restart still blocks a blind retry");

        var restarted = Flow(new Host(), kept);
        restarted.Open(null, null);
        Assert.That(restarted.Step, Is.EqualTo(NewProjectStep.Build));
        var unresolved = restarted.Frame!;
        Assert.That(unresolved.Lines.Any(line => line.Words == "Reference: " + kept.Id), Is.True);
        Press(restarted, NewProjectScreens.ConfirmClear, null);
        Assert.That(kept.Id, Is.Not.Null, "one press never clears it");
        Press(restarted, NewProjectScreens.Clear, null);
        Press(restarted, NewProjectScreens.ConfirmClear, null);
        Assert.That(kept.Id, Is.Null);
        Assert.That(restarted.Step, Is.EqualTo(NewProjectStep.YourIdea));
    }

    [Test]
    public async Task TheCompanionIsAskedOnlyBySendAnswerAndItsProposalFillsTheRecap()
    {
        var routes = new Routes();
        routes.Answers["GET /api/companion"] = () => HalcyonicJson.Serialize(new AvailableCompanion
        {
            Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" }, MaxQuestions = 4,
        });
        var replies = new Queue<CompanionReply>(new CompanionReply[] { Companions.Ask(), Companions.Propose() });
        routes.Answers["POST /api/companion/replies"] = () => HalcyonicJson.Serialize(Companions.Response(replies.Dequeue()));
        var host = new Host { Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Flow(host);
        flow.Open(null, null);
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseCompanion));

        Press(flow, NewProjectScreens.BeginCompanion, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Questions));
        await Until(flow, () => flow.Idea!.Companion!.Latest is AskReply);
        Assert.That(routes.Asked.Count(asked => asked == "POST /api/companion/replies"), Is.EqualTo(1));

        Press(flow, NewProjectScreens.ChooseSuggestion, Suggestion(flow, "One organiser"));
        Assert.That(routes.Asked.Count(asked => asked == "POST /api/companion/replies"), Is.EqualTo(1), "choosing sends nothing");
        Press(flow, NewProjectScreens.SendAnswer, null);
        await Until(flow, () => flow.Step == NewProjectStep.Recap);
        Assert.That(routes.Asked.Count(asked => asked == "POST /api/companion/replies"), Is.EqualTo(2));
        Assert.That(flow.Idea!.TaskSuggested, Is.True, "the proposal fills the recap, marked as the companion's");
        Assert.That(flow.Frame!.Source, Is.EqualTo(CompanionText.Note));
    }

    [Test]
    public void WithoutTheCompanionTheFixedQuestionsMakeTheRecap()
    {
        var host = new Host();
        var flow = Flow(host);
        flow.Open(null, null);
        var start = flow.Frame!;
        Assert.That(start.Lines.Any(line => line.Action == NewProjectScreens.ChooseQuestions), Is.True, "no companion was offered");
        Press(flow, NewProjectScreens.BeginQuestions, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Questions));
        Press(flow, NewProjectScreens.NextQuestion, null);
        Assert.That(flow.Idea!.Question, Is.EqualTo(0), "nothing chosen, nothing given");
        Press(flow, NewProjectScreens.ChooseFixedAnswer, Fixed(flow, "An app"));
        Press(flow, NewProjectScreens.NextQuestion, null);
        Press(flow, NewProjectScreens.ChooseFixedAnswer, Fixed(flow, "Just me"));
        Press(flow, NewProjectScreens.NextQuestion, null);
        Press(flow, NewProjectScreens.ChooseFixedAnswer, Fixed(flow, "Do its main job on one screen"));
        Press(flow, NewProjectScreens.NextQuestion, null);
        Press(flow, NewProjectScreens.SkipFixedQuestion, null);
        Press(flow, NewProjectScreens.NextQuestion, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(flow.Idea.FirstTask, Is.EqualTo("Make an app for me. First, do its main job on one screen."));
    }

    [Test]
    public void LeavingForAnotherWindowSendsTheReviewBackToTheRecap()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        flow.FocusLeft();
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(flow.Review, Is.Null);
        Assert.That(flow.Frame!.Lines[0].Words, Is.EqualTo(EntryText.ReviewAfresh));
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void HeardWordsLandWhereWordsAreGivenAndAreNeverSentUnchecked()
    {
        var host = new Host();
        var flow = Flow(host);
        flow.Open(null, null);
        flow.Heard("A page of race times");
        Assert.That(flow.Idea!.OwnWords, Is.EqualTo("A page of race times"));
        Assert.That(flow.Frame!.Lines.Any(line => line.Words == VoiceText.HeardNote), Is.True);
        Press(flow, NewProjectScreens.UseIdea, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Name));
        Press(flow, NewProjectScreens.Rename, null);
        flow.Heard("Race Times");
        Assert.That(flow.Idea.Name, Is.Not.EqualTo("Race Times"), "heard words wait for Done");
        Press(flow, NewProjectScreens.Done, null);
        Assert.That(flow.Idea.Name, Is.EqualTo("Race Times"));
        Assert.That(host.Sent, Is.Empty);
    }

    [Test]
    public void ADraftComesBackAfterARestartForTheSameComputer()
    {
        var store = new Memory();
        var host = new Host();
        var flow = Recapped(host, store: store);
        Assert.That(store.Drafts, Is.Not.Empty, "kept after the change");
        flow.Close();

        var restarted = Flow(new Host(), store: store);
        restarted.Open(null, null);
        Assert.That(restarted.Idea!.OwnWords, Is.EqualTo("A page of race times for my running club"));
        Assert.That(restarted.Step, Is.EqualTo(NewProjectStep.Recap));
        var elsewhere = new Host { State = new ClientProjection() };
        var other = Flow(elsewhere, store: store);
        other.Open(null, null);
        Assert.That(other.Idea!.HasRecap, Is.False, "nothing for a computer whose journal isn't live yet");
    }

    [Test]
    public async Task AKeptFolderShowsItsPlaceAsTheComputerListsItNowAndAGonePlaceSendsNothing()
    {
        static LocationsResponse Listing(params LocationRoot[] roots) => new() { Roots = roots.ToList() };
        static LocationRoot Place(string path, string label, LocationRootStatus status = LocationRootStatus.Available) => new()
        {
            Path = path, Name = "Projects", Label = label, Status = status, FoldersTruncated = false,
            Folders = new List<LocationFolder> { new() { Name = "race-times", Path = path + "/race-times" } },
        };
        var routes = new Routes();
        var listing = Listing(Place("/Users/person/Projects", "Projects"));
        routes.Answers["GET /api/locations"] = () => HalcyonicJson.Serialize(listing);
        var host = new Host { Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
        Press(flow, NewProjectScreens.ChooseWhere, null);
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseFolder));
        var index = flow.Frame!.Lines.Where(line => line.Action == NewProjectScreens.ChooseFolder).First(line => line.Words == "race-times").Key;
        Press(flow, NewProjectScreens.ChooseFolder, index);
        Press(flow, NewProjectScreens.Done, null);
        Assert.That(flow.Idea!.Folder!.Describe(), Is.EqualTo("race-times in Projects"));

        // Another place now takes the label, and this one is gone: read again when the draft opens.
        listing = Listing(Place("/Projects", "Projects"), Place("/Users/person/Projects", "Projects (person)", LocationRootStatus.Missing));
        flow.Close();
        flow.Open(null, null);
        await Until(flow, () => flow.Idea!.Folder!.PlaceGone);
        Assert.That(flow.Idea!.Folder!.Describe(), Does.Contain("no longer lists"), "never the label now another place's");
        var recap = flow.Frame!;
        Assert.That((recap.Footer[PromptSlot.FarRight]!.Available, recap.Reason), Is.EqualTo((false, EntryText.ChooseWhereFilesLive)));
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null);

        // A read that fails changes nothing; the place back unblocks Start building by itself.
        routes.Answers.Remove("GET /api/locations");
        flow.Close();
        flow.Open(null, null);
        await Until(flow, () => routes.Asked.Count(asked => asked == "GET /api/locations") == 3);
        for (var tick = 0; tick < 5; tick++)
        {
            await Task.Delay(10);
            flow.Tick();
        }
        Assert.That(flow.Idea!.Folder!.PlaceGone, Is.True, "only a listing read marks a place, either way");
        listing = Listing(Place("/Projects", "Projects"), Place("/Users/person/Projects", "Projects (person)"));
        routes.Answers["GET /api/locations"] = () => HalcyonicJson.Serialize(listing);
        flow.Close();
        flow.Open(null, null);
        await Until(flow, () => !flow.Idea!.Folder!.PlaceGone);
        Assert.That(flow.Idea!.Folder!.Describe(), Is.EqualTo("race-times in Projects (person)"));
        Assert.That(flow.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.True);
    }

    /// <summary>
    /// Folders that couldn't be read are said by the refusal's code, a refused credential as this
    /// headset's connection says it, or as a computer that didn't answer, never by the error's message,
    /// the control plane's own words, which can hold an address.
    /// </summary>
    [Test]
    public async Task FoldersThatCouldNotBeReadAreNeverSaidByTheirMessage()
    {
        const string Leak = "connect ECONNREFUSED 192.168.1.20:47801 (/Users/someone)";
        static string Refusal(string code) => HalcyonicJson.Serialize(new ErrorResponse { Error = new ErrorBody { Code = code, Message = Leak } });
        var cases = new (Action<Routes> Fail, string Shown)[]
        {
            (routes => routes.Unreachable["GET /api/locations"] = Leak, "It didn't answer. Check that this app is running there, then press Try again."),
            (routes => routes.Refusals["GET /api/locations"] = (HttpStatusCode.Unauthorized, Refusal("device_revoked")), ConnectionText.PairingRefused),
            (routes => routes.Refusals["GET /api/locations"] = (HttpStatusCode.Unauthorized, Refusal("unauthorized")), ConnectionText.AccessTokenRefused),
            (routes => routes.Refusals["GET /api/locations"] = (HttpStatusCode.TooManyRequests, Refusal("too_many_requests")),
                "Your computer is turning this headset away for a minute after too many tries. Press Try again after a minute."),
            (routes => routes.Refusals["GET /api/locations"] = (HttpStatusCode.InternalServerError, Refusal("internal_error")), "Press Try again."),
            (routes => routes.Refusals["GET /api/locations"] = (HttpStatusCode.BadGateway, Leak), "Press Try again."),
            (routes => routes.Refusals["GET /api/locations"] = (HttpStatusCode.InternalServerError, Refusal("device_revoked")), "Press Try again."),
        };
        foreach (var (fail, shown) in cases)
        {
            var routes = new Routes();
            fail(routes);
            using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) { AccessRefused = ConnectionText.AccessTokenRefused };
            var host = new Host { Api = api };
            var flow = Recapped(host);
            Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
            Press(flow, NewProjectScreens.ChooseWhere, null);
            await Until(flow, () => flow.Frame!.Lines.All(line => line.Words != EntryText.ReadingFolders));
            var words = flow.Frame!.Lines.Select(line => line.Words).ToList();
            Assert.That(words, Has.None.Contains("ECONNREFUSED").And.None.Contains("192.168").And.None.Contains("control plane"), shown);
            Assert.That(words, Does.Contain("Couldn't read your computer's folders.").And.Contain(shown), "the heading, then why");
        }
    }

    /// <summary>
    /// A read of the folders or of the models that gets no reply before the headset gives up says it
    /// didn't answer, never staying on Reading…; giving up is not the page's own cancel.
    /// </summary>
    [Test]
    public async Task ARequestThatTimesOutSaysItDidntAnswer()
    {
        var local = Runtime("local", ModelChoice.Listed);
        var host = new Host { State = WithRuntimes(Runtime("mock"), local) };
        var routes = new Routes();
        routes.Delays["GET /api/locations"] = TimeSpan.FromSeconds(30);
        routes.Delays["GET /api/runtimes/local/models"] = TimeSpan.FromSeconds(30);
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes,
            requestTimeout: TimeSpan.FromMilliseconds(100), modelsTimeout: TimeSpan.FromMilliseconds(200));
        host.Api = api;
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
        Press(flow, NewProjectScreens.ChooseWhere, null);
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Words == EntryText.FoldersUnanswered));
        Assert.That(flow.Frame!.Lines.Select(line => line.Words), Does.Contain(EntryText.FoldersUnread).And.Not.Contain(EntryText.ReadingFolders));
        var models = Recapped(host);
        Press(models, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(models, NewProjectScreens.MoreOptions, null);
        Press(models, NewProjectScreens.ChooseRuntime, "local");
        await Until(models, () => models.Frame!.Lines.Any(line => line.Words == EntryText.ModelsComputerSilent));
    }

    /// <summary>
    /// The headset waits past the 30 s your computer gives an agent app to list its models, so your
    /// computer's own reason reaches the step rather than the headset giving up first.
    /// </summary>
    [Test]
    public async Task YourComputersOwnReasonForTheModelsArrivesBeforeTheHeadsetGivesUp()
    {
        Assert.That((ControlPlaneApi.RequestTimeout, ControlPlaneApi.ModelsTimeout > TimeSpan.FromSeconds(30)), Is.EqualTo((TimeSpan.FromSeconds(15), true)));
        var local = Runtime("local", ModelChoice.Listed);
        var host = new Host { State = WithRuntimes(Runtime("mock"), local) };
        var routes = new Routes();
        routes.Answers["GET /api/runtimes/local/models"] = () => HalcyonicJson.Serialize(new RuntimeModelsResponse
        {
            RuntimeId = "local", Result = new UnavailableModels { Reason = new ErrorInfo { Code = "runtime_version_unsupported", Message = "OpenCode 1.0 is too old." } },
        });
        // As 31 s against 15 and 40, scaled down: longer than any other request may take, within a models read's own deadline.
        routes.Delays["GET /api/runtimes/local/models"] = TimeSpan.FromMilliseconds(310);
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes,
            requestTimeout: TimeSpan.FromMilliseconds(150), modelsTimeout: TimeSpan.FromMilliseconds(1500));
        host.Api = api;
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Press(flow, NewProjectScreens.ChooseRuntime, "local");
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Words == EntryText.ModelsUnsupported));
    }

    /// <summary>A read the page itself gives up, as when it reads again, is never said as one that didn't answer.</summary>
    [Test]
    public async Task AReadThePageCancelsNeverSaysItDidntAnswer()
    {
        var routes = new Routes();
        var listing = new LocationsResponse
        {
            Roots = new List<LocationRoot>
            {
                new()
                {
                    Path = "/Users/person/Projects", Name = "Projects", Label = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
                    Folders = new List<LocationFolder> { new() { Name = "race-times", Path = "/Users/person/Projects/race-times" } },
                },
            },
        };
        routes.Answers["GET /api/locations"] = () => HalcyonicJson.Serialize(listing);
        routes.Delays["GET /api/locations"] = TimeSpan.FromSeconds(30);
        using var api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes);
        var host = new Host { Api = api };
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
        Press(flow, NewProjectScreens.ChooseWhere, null);
        flow.Tick();
        routes.Delays.Remove("GET /api/locations");
        Press(flow, NewProjectScreens.Done, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
        Press(flow, NewProjectScreens.ChooseWhere, null);
        var said = new List<string>();
        for (var tries = 0; tries < 200 && !said.Contains("race-times"); tries++)
        {
            await Task.Delay(10);
            flow.Tick();
            said.AddRange(flow.Frame!.Lines.Select(line => line.Words));
        }
        Assert.That(routes.Asked.Count(asked => asked == "GET /api/locations"), Is.GreaterThanOrEqualTo(2), "read again");
        Assert.That(said, Has.None.EqualTo(EntryText.FoldersUnanswered).And.None.EqualTo(EntryText.FoldersUnread));
    }

    /// <summary>A build sent whose acknowledgement was lost: its outcome unknown, its id kept.</summary>
    private static NewProjectFlow Unknown(Host host, Kept kept)
    {
        host.Acknowledge = command => Task.FromException<CommandAckMessage>(new CommandOutcomeUnknownException(command.CommandId, "The socket closed."));
        var flow = Recapped(host, kept);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        host.State = With();
        flow.Tick();
        Assert.That(kept.Id, Is.EqualTo(host.Sent.Single().CommandId));
        return flow;
    }

    [Test]
    public void OpenedAgainWhileItStandsItShowsWhereItOpensAndSaysSo()
    {
        // A start kept unknown: the person goes to Your idea by the steps, then presses New project again,
        // which the navigator, New project already standing beside the menu, doesn't redraw itself.
        var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-0000000000c1" };
        var host = new Host();
        var flow = Flow(host, kept);
        flow.Open(null, null);
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.YourIdea));
        var idea = flow.Frame!;
        Draw(flow, idea);
        var changes = 0;
        flow.Changed += () => changes++;
        flow.Open(null, null);
        Assert.That(changes, Is.EqualTo(1), "said to have changed");
        Assert.That((flow.Step, flow.Frame!.Lines.Any(line => line.Words == EntryText.Reference(kept.Id!))), Is.EqualTo((NewProjectStep.Build, true)), "the unknown start shows");
        flow.Act(MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.YourIdea));
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build), "nothing on the page drawn before takes a press");

        // The same with nothing kept, opened again on the page it stood on.
        var plain = Recapped(new Host());
        Draw(plain, plain.Frame!);
        var told = 0;
        plain.Changed += () => told++;
        plain.Open(null, null);
        Assert.That(told, Is.EqualTo(1), "said to have changed");
    }

    [Test]
    public void AfterARestartAKeptUnknownStartHoldsEveryNewStartAndIsNeverReplaced()
    {
        var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-0000000000c1" };
        var host = new Host();
        var flow = Flow(host, kept);
        flow.Open(null, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build), "the unknown start comes first");

        // The steps still lead to Your idea, and a new idea reaches the recap.
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.YourIdea));
        host.Typed.Enqueue("Another page");
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Press(flow, NewProjectScreens.ChooseRuntime, "mock");
        Press(flow, NewProjectScreens.Done, null);
        var recap = flow.Frame!;
        Assert.That((recap.Footer[PromptSlot.FarRight]!.Available, recap.Reason), Is.EqualTo((false, EntryText.PreviousRequestLine)));
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "nothing starts while an earlier start may have run");
        Assert.That(recap.Sections.Single(step => step.Key == NewProjectScreens.Key(NewProjectStep.Build)).Reached, Is.True);
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Build));
        Assert.That(flow.Frame!.Lines.Any(line => line.Words == "Reference: " + kept.Id), Is.True, "Build shows the start to check");
        Assert.That(host.Sent, Is.Empty);
        Assert.That(kept.Id, Is.EqualTo("01a0dcf1-5a80-7000-8000-0000000000c1"), "never replaced");
    }

    [Test]
    public void InASessionAnUnknownStartCannotBeStartedOverOrStartedAgain()
    {
        var host = new Host();
        var kept = new Kept();
        var flow = Unknown(host, kept);
        var first = kept.Id;
        Press(flow, MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Recap));
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.StartOver));
        var recap = flow.Frame!;
        Assert.That((recap.Footer[PromptSlot.Rare]!.Id, recap.Footer[PromptSlot.Rare]!.Available), Is.EqualTo((NewProjectScreens.StartOver, false)),
            "Start over waits too");
        Press(flow, NewProjectScreens.StartOver, null);
        Press(flow, NewProjectScreens.ConfirmStartOver, null);
        Assert.That(flow.Sequence, Is.Not.Null, "the build is still held");
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null);
        Assert.That(host.Sent, Has.Count.EqualTo(1), "no second project.create");
        Assert.That(kept.Id, Is.EqualTo(first));
    }

    [Test]
    public void ABuildConfirmedGoesOnAfterCloseAndOpeningAgainShowsWhereItStands()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        var create = host.Sent.Single();
        Press(flow, Footer.Close, null);
        Assert.That(flow.IsOpen, Is.False);

        host.State = With(Completed(create, new ProjectCreatedResult { ProjectId = "p1" }));
        flow.Tick();
        Assert.That(host.Sent.Last(), Is.InstanceOf<WorkstreamCreateCommand>(), "the Yes confirmed the whole build");
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(host.Sent, Has.Count.EqualTo(2), "closed, no new press acts");
        flow.Open(null, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build));
        Assert.That(flow.Frame!.Lines.Any(line => line.Words == EntryText.StepName(BuildStepKind.CreateWorkstream, true)), Is.True);
    }

    private static ClientProjection OnJournal(string id, JournalOrigin origin = JournalOrigin.Live, IEnumerable<CommandView>? commands = null)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1, journal: Samples.Journal(id, origin));
        snapshot.Commands = (commands ?? Array.Empty<CommandView>()).ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    [Test]
    public void ABuildBegunOnOneJournalSendsNothingMoreOnceTheSessionIsOnAnother()
    {
        var host = new Host { State = OnJournal("journal-1") };
        var flow = Flow(host);
        flow.Open(null, null);
        host.Typed.Enqueue("A page of race times for my running club");
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Press(flow, NewProjectScreens.ChooseRuntime, "mock");
        Press(flow, NewProjectScreens.Done, null);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        var create = host.Sent.Single();
        Press(flow, Footer.Close, null);

        // The session is now on another journal, and the first one's record of the create comes late.
        host.State = OnJournal("journal-2", commands: new[] { Completed(create, new ProjectCreatedResult { ProjectId = "p1" }) });
        flow.Tick();
        Assert.That(host.Sent, Has.Count.EqualTo(1), "no workstream.create for the first journal's project goes to the second");
        Assert.That(flow.ForAnotherSession, Is.True);
        flow.Open(null, null);
        Assert.That(flow.IsOpen, Is.False);
    }

    [Test]
    public void NewProjectMadeInTheDemonstrationNeverReadsOrSendsOnceARealSessionTakesItsPlace()
    {
        var routes = new Routes();
        var host = new Host { Demonstration = true, State = OnJournal("demonstration", JournalOrigin.Fixture) };
        var flow = Flow(host);
        flow.Open(null, null);
        host.Typed.Enqueue("A page of race times for my running club");
        Press(flow, NewProjectScreens.TypeIdea, null);
        Press(flow, NewProjectScreens.UseIdea, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));

        // The computer answers, and its session takes the demonstration's place.
        host.Demonstration = false;
        host.State = OnJournal("journal-1");
        host.Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes);
        Assert.That(flow.ForAnotherSession, Is.True);
        flow.Tick();
        Assert.That(flow.IsOpen, Is.False);
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That(flow.Review, Is.Null, "a draft made in the demonstration never starts on the computer");
        flow.Open(null, null);
        Assert.That(flow.IsOpen, Is.False, "the director makes New project afresh for this session");
        Assert.That(host.Sent, Is.Empty);
        Assert.That(routes.Asked, Is.Empty, "nothing was read from the computer either");
    }

    [Test]
    public void TheDemonstrationPlaysTheRecordedExchangeToARecapThatCanNotStartAndSendsNothing()
    {
        var recording = CompanionRecording.Parse(System.IO.File.ReadAllText(Repository.PathTo(
            "apps/xr/Assets/Halcyonic/Resources/" + CompanionRecording.ResourceName + ".json")));
        var host = new Host { Demonstration = true, KeyboardOffered = false, State = OnJournal("demonstration", JournalOrigin.Fixture) };
        var flow = new NewProjectFlow(host, Commands, new Kept(), null, recording);
        flow.Open(null, null);

        // Your idea: talking it through is the main action, and the recording brings its own idea.
        var yourIdea = flow.Frame!;
        Assert.That(yourIdea.Lines.Any(line => line.Action == NewProjectScreens.TypeIdea), Is.False, "no keyboard opens here");
        Assert.That(yourIdea.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.BeginCompanion));
        Assert.That(yourIdea.Footer[PromptSlot.Secondary], Is.Null, "no Hold to talk in the demonstration");
        Press(flow, NewProjectScreens.BeginCompanion, null);
        Assert.That((flow.Step, flow.Idea!.OwnWords), Is.EqualTo((NewProjectStep.Questions, recording.Idea)));

        // Questions: the companion's words quoted as its own, said to be recorded, and only the recorded answer offered.
        var questions = flow.Frame!;
        Assert.That(questions.Source, Is.EqualTo(CompanionText.Recorded));
        Assert.That(questions.Lines.Any(line => line.Claim && line.Words.StartsWith("The companion says: “", StringComparison.Ordinal)), Is.True);
        // Where the question takes a page of its own first, on to its answers.
        for (var turned = 0; turned < 4 && !questions.Lines.Any(line => line.Action == NewProjectScreens.ChooseSuggestion) && Turn(questions) is { } next; turned++)
        {
            Press(flow, next.Id, next.Key);
            questions = flow.Frame!;
        }
        var exchange = flow.Idea.Companion!;
        var answer = NewProjectScreens.AnswerKey(exchange.Generation, recording.RecordedAnswer(exchange)!);
        Assert.That(questions.Lines.Where(line => line.Action == NewProjectScreens.ChooseSuggestion && line.Pressable).Select(line => line.Key),
            Is.EqualTo(new[] { answer }));
        Press(flow, NewProjectScreens.ChooseSuggestion, answer);
        Press(flow, NewProjectScreens.SendAnswer, null);

        // The recording's proposal fills the recap, marked as the companion's, and the demonstration can't start it.
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        var recap = flow.Frame!;
        var pages = Pages(flow);
        Assert.That(pages.All(page => page.Source == CompanionText.Note), Is.True, "the note that it is an AI that can be wrong, on every page");
        Assert.That(pages.All(page => page.Reason == EntryText.DemoCannotStart), Is.True);
        Assert.That(pages.SelectMany(page => page.Lines).Any(line => line.Fact == CompanionText.SuggestedShort), Is.True);
        var start = recap.Footer[PromptSlot.FarRight]!;
        Assert.That((start.Id, start.Available, recap.Reason), Is.EqualTo((NewProjectScreens.StartBuilding, false, EntryText.DemoCannotStart)));
        Assert.That(recap.Sections.Single(step => step.Key == NewProjectScreens.Key(NewProjectStep.Build)).Reached, Is.False);
        Press(flow, NewProjectScreens.StartBuilding, null);
        Assert.That((flow.Step, flow.Review), Is.EqualTo((NewProjectStep.Recap, (NewWorkReview?)null)));
        Assert.That(host.Sent, Is.Empty, "nothing is sent");
    }

    [Test]
    public void ALiveNewProjectActsOnlyForTheJournalItWasMadeFor()
    {
        var host = new Host { State = OnJournal("journal-1") };
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Assert.That(flow.Review!.CanConfirm, Is.True);

        host.State = OnJournal("journal-2");
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(host.Sent, Is.Empty, "a review read for one computer is never sent to another");
        flow.Tick();
        Assert.That((flow.IsOpen, flow.ForAnotherSession), Is.EqualTo((false, true)));
        host.State = OnJournal("journal-1");
        Assert.That(flow.ForAnotherSession, Is.True, "for good: the director makes it afresh");
    }

    [Test]
    public void WhereTheKeyboardCantOpenNoRowOpensItAndHeardWordsStayAChoice()
    {
        var host = new Host { KeyboardOffered = false };
        var flow = Flow(host);
        flow.Open(null, null);
        Assert.That(flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.TypeIdea), Is.False);
        flow.Heard("A page of race times for my running club");
        Press(flow, NewProjectScreens.ChooseQuestions, null);
        Press(flow, NewProjectScreens.TypeIdea, null);
        Assert.That(host.Prompts, Is.Empty, "no keyboard was asked to open");
        Assert.That(flow.Frame!.Lines.Single(line => line.Action == NewProjectScreens.TypeIdea).Chosen, Is.True, "the heard idea is chosen again");
        Press(flow, NewProjectScreens.UseIdea, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
    }

    [Test]
    public void WithNeitherTheKeyboardNorHoldToTalkRenamingIsNotOfferedAndItsPressDoesNothing()
    {
        var host = new Host();
        var flow = Recapped(host);
        host.KeyboardOffered = false;
        host.VoiceOffered = false;
        flow.Tick();
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Name));
        Assert.That(Ids(flow.Frame!), Does.Not.Contain(NewProjectScreens.Rename));
        Press(flow, NewProjectScreens.Rename, null);
        Assert.That(flow.Frame!.Lines.Any(line => line.Words == EntryText.ProjectName), Is.False, "no page of words opens");

        host.VoiceOffered = true;
        flow.Tick();
        Assert.That(Ids(flow.Frame!), Does.Contain(NewProjectScreens.Rename), "Hold to talk can give the name");
    }

    // ---------------------------------------------------------------------------------------------
    // Pages that need more than the stage gives (ADR 0026): each packed into a Quest 3S's page.

    private const float Column = Glaze.Menu.FileColumnDegrees;

    /// <summary>What a page's lines take as the view lays them, measured on its own here, to check the flow's packing against.</summary>
    private static float LinesHeight(Host host, IReadOnlyList<PageLine> lines)
    {
        var total = 0f;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            if (index > 0) total += lines[index - 1].Action != null && line.Action != null ? MenuPage.TargetGap : MenuPage.Grid;
            var next = index + 1 < lines.Count ? lines[index + 1] : null;
            if (next != null && ((line.Choice && next.Choice) || line.BesideNext) && host.FitsHalf(line, Column) && host.FitsHalf(next, Column))
            {
                total += MenuPage.Target();
                index++;
                continue;
            }
            var rows = Math.Max(1, Math.Min(line.Rows, host.RowsOf(line, Column)));
            total += line.Action != null ? MenuPage.Target(rows) : MenuPage.Words(rows);
        }
        return total;
    }

    /// <summary>
    /// Every page of what shows, at both text sizes, inside the page a Quest 3S gives it beside its
    /// reason and source line, each ending in the row that turns it where there are several; returns
    /// every line shown, the turning rows left out.
    /// </summary>
    private static List<PageLine> FitsEveryPage(NewProjectFlow flow, Host host, string what)
    {
        var shown = new List<PageLine>();
        foreach (var size in new[] { TextSize.Standard, TextSize.Larger })
        {
            host.TextSize = size;
            flow.Tick();
            var lines = FitsThisSize(flow, host, what);
            if (size == TextSize.Standard) shown.AddRange(lines);
        }
        host.TextSize = TextSize.Standard;
        flow.Tick();
        return shown;
    }

    /// <summary>Every page of what shows at the text size set now, as <see cref="FitsEveryPage"/> checks each; returns every line shown, the turning rows left out.</summary>
    private static List<PageLine> FitsThisSize(NewProjectFlow flow, Host host, string what)
    {
        var shown = new List<PageLine>();
        var pages = Pages(flow);
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var room = host.PageHeight(Math.Max(1, host.TitleRows(page.Subject, Column)), besideMenu: false);
            if (page.Source != null) room -= MenuPage.GroupGap + MenuPage.Words(Math.Max(1, host.RowsOf(page.Source, Column)));
            if (page.Reason != null) room -= MenuPage.GroupGap + MenuPage.Words(Math.Max(1, host.RowsOf(page.Reason, Column)));
            var name = what + " at " + host.TextSize + ", page " + (index + 1) + " of " + pages.Count;
            Assert.That(LinesHeight(host, page.Lines), Is.LessThanOrEqualTo(room + 1e-5f), name + " fits");
            if (pages.Count > 1)
            {
                var turn = Turn(page);
                Assert.That(turn, Is.Not.Null, name + " turns");
                if (turn!.Value.Id == Footer.NextPage) Assert.That(turn.Value.Words, Is.EqualTo(Footer.NextPageWords(index, pages.Count)), name + " says where it turns");
                if (turn.Value.Id == NewProjectScreens.NextPage) Assert.That(turn.Value.Words, Is.EqualTo(EntryText.NextPage(index, pages.Count)), name + " says where it turns");
            }
            shown.AddRange(page.Lines.Where(line => !Turns(line)));
        }
        return shown;
    }

    private static ClientProjection WithRuntimes(params RuntimeDescriptor[] runtimes)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Runtimes = runtimes.ToList();
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    private static RuntimeDescriptor Runtime(string id, ModelChoice choice = ModelChoice.None)
    {
        var runtime = Samples.MockRuntime();
        runtime.RuntimeId = id;
        runtime.DisplayName = "Agent app " + id;
        runtime.ModelChoice = choice;
        return runtime;
    }

    [Test]
    public void TheRecapPagesItsFactsAndKeepsItsFirstLineOnItsFirstPage()
    {
        var host = new Host();
        var flow = Recapped(host);
        var shown = FitsEveryPage(flow, host, "the recap");
        Assert.That(shown.Where(line => line.Action == NewProjectScreens.ChooseFact).Select(line => line.Key), Is.EquivalentTo(new[]
        {
            NewProjectScreens.FactKey(RecapFact.Name), NewProjectScreens.FactKey(RecapFact.FirstTask),
            NewProjectScreens.FactKey(RecapFact.Folder), NewProjectScreens.FactKey(RecapFact.HowItRuns), NewProjectScreens.FactKey(RecapFact.StartOver),
        }), "every fact once, and Start over");
        Assert.That(flow.Frame!.Lines[0].Action, Is.Null, "the recap's line first");

        // A fact chosen on a later page keeps that page, and its side panel slides out from it.
        host.TextSize = TextSize.Larger;
        flow.Tick();
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        var chosen = flow.Frame!;
        Assert.That(chosen.Side, Is.Not.Null);
        Assert.That(chosen.Lines.Single(line => line.Chosen).Key, Is.EqualTo(NewProjectScreens.FactKey(RecapFact.HowItRuns)));

        // The chosen row's action holds the footer's middle, so no Next page: never four prompts.
        Assert.That(chosen.Footer.All.Select(each => each.Prompt.Id), Is.EqualTo(new[] { Footer.Close, NewProjectScreens.MoreOptions, NewProjectScreens.StartBuilding }));
        // Its details closed, the pages turn by the footer's Next page, as the menu's lists turn.
        var chosenKeys = chosen.Lines.Select(line => line.Key).ToList();
        Press(flow, SidePanel.Close, null);
        Assert.That((flow.Frame!.Side, flow.Frame.Footer[PromptSlot.Secondary]?.Id), Is.EqualTo(((SidePanel?)null, Footer.NextPage)));
        Press(flow, Footer.NextPage, null);
        Assert.That(flow.Frame!.Lines.Select(line => line.Key), Is.Not.EqualTo(chosenKeys), "another page");
    }

    [Test]
    public void TheDemonstrationsRecapFitsEveryPageBesideItsReasonAndTheAINote()
    {
        var recording = CompanionRecording.Parse(System.IO.File.ReadAllText(Repository.PathTo(
            "apps/xr/Assets/Halcyonic/Resources/" + CompanionRecording.ResourceName + ".json")));
        var host = new Host { Demonstration = true, KeyboardOffered = false, State = OnJournal("demonstration", JournalOrigin.Fixture) };
        var flow = new NewProjectFlow(host, Commands, new Kept(), null, recording);
        flow.Open(null, null);
        Press(flow, NewProjectScreens.BeginCompanion, null);
        var exchange = flow.Idea!.Companion!;
        Press(flow, NewProjectScreens.ChooseSuggestion, NewProjectScreens.AnswerKey(exchange.Generation, recording.RecordedAnswer(exchange)!));
        Press(flow, NewProjectScreens.SendAnswer, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        var shown = FitsEveryPage(flow, host, "the demonstration's recap");
        Assert.That(shown.Count(line => line.Action == NewProjectScreens.ChooseFact), Is.EqualTo(5), "every fact once, and Start over");
    }

    [Test]
    public async Task TheCompanionsQuestionPagesItsAnswersUnderTheQuestionAndATurnClearsTheChoice()
    {
        var routes = new Routes();
        routes.Answers["GET /api/companion"] = () => HalcyonicJson.Serialize(new AvailableCompanion
        {
            Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" }, MaxQuestions = 4,
        });
        var asked = Companions.Ask("Who will enter the race times after each race, and should they be able to fix a mistake later?");
        asked.Question.Choices = new List<string>
        {
            "Each runner enters their own after each race", "One organiser enters them all for the club",
            "Both, with the organiser fixing any mistakes", "Nobody yet, the club will decide at a meeting",
        };
        routes.Answers["POST /api/companion/replies"] = () => HalcyonicJson.Serialize(Companions.Response(asked));
        var host = new Host { TextSize = TextSize.Larger, Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Flow(host);
        flow.Open(null, null);
        for (var tries = 0; tries < 100 && !flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseCompanion); tries++)
        {
            await Task.Delay(10);
            flow.Tick();
        }
        Press(flow, NewProjectScreens.BeginCompanion, null);
        await Until(flow, () => flow.Idea!.Companion!.Latest is AskReply);

        var pages = Pages(flow);
        Assert.That(pages, Has.Count.GreaterThan(1), "four long answers need more than a page at the larger size");
        Assert.That(pages[0].Lines.Any(line => line.Claim), Is.True, "the question first, whole");
        Assert.That(pages.SelectMany(page => page.Lines).Count(line => line.Action == NewProjectScreens.ChooseSuggestion), Is.EqualTo(4), "no answer left out");
        FitsEveryPage(flow, host, "the companion's question");
        host.TextSize = TextSize.Larger;
        flow.Tick();

        // A choice on one page is cleared by turning, so Send answer never sends what isn't in view.
        var first = flow.Frame!;
        var answer = first.Lines.First(line => line.Action == NewProjectScreens.ChooseSuggestion);
        Press(flow, NewProjectScreens.ChooseSuggestion, answer.Key);
        Assert.That(flow.Frame!.Footer[PromptSlot.FarRight]!.Id, Is.EqualTo(NewProjectScreens.SendAnswer));
        var turn = Turn(flow.Frame!)!.Value;
        Assert.That(turn.Id, Is.EqualTo(NewProjectScreens.MoreAnswers));
        Press(flow, turn.Id, turn.Key);
        Assert.That(flow.Idea!.Companion!.Chosen, Is.EqualTo(CompanionAnswerRow.None), "turning clears the choice");
        Press(flow, NewProjectScreens.SendAnswer, null);
        Assert.That(routes.Asked.Count(request => request == "POST /api/companion/replies"), Is.EqualTo(1), "nothing chosen out of view is sent");
    }

    [Test]
    public void AFixedQuestionPagesItsAnswersAndNextQuestionGivesOnlyWhatIsInView()
    {
        var host = new Host { TextSize = TextSize.Larger, Height = (rows, _) => MenuPage.Height(TextSize.Larger, rows) * 0.8f };
        var flow = Flow(host);
        flow.Open(null, null);
        Press(flow, NewProjectScreens.ChooseQuestions, null);
        Press(flow, NewProjectScreens.BeginQuestions, null);
        var pages = Pages(flow);
        Assert.That(pages, Has.Count.GreaterThan(1));
        Assert.That(pages.SelectMany(page => page.Lines).Count(line => line.Action == NewProjectScreens.ChooseFixedAnswer), Is.EqualTo(flow.Idea!.Choices.Count));

        // Where the question takes a page of its own first, on to its answers.
        if (!flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseFixedAnswer)) Press(flow, Turn(flow.Frame!)!.Value.Id, Turn(flow.Frame!)!.Value.Key);
        var choice = flow.Frame!.Lines.First(line => line.Action == NewProjectScreens.ChooseFixedAnswer);
        Press(flow, NewProjectScreens.ChooseFixedAnswer, choice.Key);
        var turn = Turn(flow.Frame!)!.Value;
        Press(flow, turn.Id, turn.Key);
        Assert.That((flow.Idea.GuideAnswer, flow.Frame!.Footer[PromptSlot.FarRight]!.Available), Is.EqualTo(((string?)null, false)), "turning clears the choice");
        Press(flow, NewProjectScreens.NextQuestion, null);
        Assert.That(flow.Idea.Question, Is.EqualTo(0), "nothing out of view is given");
    }

    [Test]
    public void TheUnknownStartIsReadToItsLastPartBeforeClearCanBePressed()
    {
        var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-00000000dead" };
        var host = new Host { TextSize = TextSize.Larger, Height = (rows, beside) => MenuPage.Height(TextSize.Larger, rows, besideMenu: beside) * 0.6f };
        var flow = Flow(host, kept);
        flow.Open(null, null);
        var first = flow.Frame!;
        var clear = first.Footer[PromptSlot.FarRight]!;
        Assert.That((clear.Id, clear.Available), Is.EqualTo((NewProjectScreens.Clear, false)), "not until every part is read");
        Assert.That(first.Reason, Does.StartWith("Read to part"));
        Press(flow, NewProjectScreens.Clear, null);
        Press(flow, NewProjectScreens.ConfirmClear, null);
        Assert.That(kept.Id, Is.Not.Null, "never cleared unread");

        var lines = new List<PageLine>(first.Lines);
        for (var part = 0; part < 10 && Turn(flow.Frame!) is { } next; part++)
        {
            host.Now += 1;
            Press(flow, next.Id, next.Key);
            lines.AddRange(flow.Frame!.Lines);
        }
        flow.Drawn(flow.Frame!, null);
        Assert.That(lines.Select(line => line.Words), Does.Contain(EntryText.PreviousRequestLine), "the warning shown");
        Assert.That(flow.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.True, "once the last part has shown");
        Press(flow, NewProjectScreens.Clear, null);
        Press(flow, NewProjectScreens.ConfirmClear, null);
        Assert.That(kept.Id, Is.Null);
    }

    [Test]
    public void ASidePanelDrawnCountsNothingAsRead()
    {
        // The review: only its side panel drawn, its parts stay unread however long the person waits.
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        for (var part = 0; part < 20; part++)
        {
            flow.Drawn(flow.Frame!, CarriedInPlace(flow.Frame!));
            host.Now += 1;
            flow.Act(NewProjectScreens.NextPart, null);
        }
        Assert.That(flow.Review!.CanConfirm, Is.False, "no part of the review read from a side panel's draw");
        ReadToTheEnd(flow, host);
        Assert.That(flow.Review!.CanConfirm, Is.True, "its pages drawn, it is read");

        // The unknown start: every part's side panel drawn, Clear still waits.
        var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-00000000dead" };
        var tight = new Host { TextSize = TextSize.Larger, Height = (rows, beside) => MenuPage.Height(TextSize.Larger, rows, besideMenu: beside) * 0.6f };
        var unknown = Flow(tight, kept);
        unknown.Open(null, null);
        for (var part = 0; part < 10 && Turn(unknown.Frame!) is { } next; part++)
        {
            unknown.Drawn(unknown.Frame!, CarriedInPlace(unknown.Frame!));
            tight.Now += 1;
            unknown.Act(next.Id, next.Key);
        }
        unknown.Drawn(unknown.Frame!, CarriedInPlace(unknown.Frame!));
        Assert.That(unknown.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.False, "Clear waits for its parts' pages");
    }

    [Test]
    public void ClearWaitsForEveryLineOfTheUnknownStartEvenWhenItsPartsAreLaidAgain()
    {
        // Parts read at one text size, then the other lays them again. Its lines wrapped to every row they
        // may take, from x0.71 to x0.77 the larger size holds one line a part and the smaller two:
        // [0], [1], [2], [3], [4] against [0, 1], [2, 3], [4]. Three single lines read, then the smaller
        // size: counted by parts, three of three were read on its last part, with the fourth line never
        // shown. Two lines read, then the larger: the part the person was on is now an earlier one.
        foreach (var (first, then) in new[] { (TextSize.Larger, TextSize.Standard), (TextSize.Standard, TextSize.Larger) })
        {
            var cases = 0;
            for (var factor = 0.50f; factor <= 0.90f; factor += 0.005f)
            {
                for (var read = 1; read <= 4; read++)
                {
                    var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-00000000dead" };
                    var host = new Host { TextSize = first, RowWidth = size => size == TextSize.Larger ? 24 : 30 };
                    var size = factor;
                    host.Height = (rows, beside) => MenuPage.Height(host.TextSize, rows, besideMenu: beside) * size;
                    var flow = Flow(host, kept);
                    flow.Open(null, null);
                    var all = NewProjectScreens.Unresolved(new ProjectIdea(), kept.Id, null, armed: false, live: true).Lines.Select(line => line.Words).ToList();
                    var drawn = new HashSet<string>();
                    void DrawAndNote()
                    {
                        foreach (var line in flow.Frame!.Lines) drawn.Add(line.Words);
                        flow.Drawn(flow.Frame!, null);
                    }
                    DrawAndNote();
                    for (var part = 1; part < read && flow.Frame!.Lines.LastOrDefault(line => line.Action == NewProjectScreens.NextPart) is { } row; part++)
                    {
                        host.Now += 1;
                        flow.Act(NewProjectScreens.NextPart, row.Key);
                        DrawAndNote();
                    }
                    var at = first + " to " + then + ", x" + size + ", " + read + " part(s) read first";
                    host.TextSize = then;
                    flow.Tick();
                    // Laid again, no line never drawn is left behind the part showing, where Next part can't reach it.
                    if (all.FirstOrDefault(words => !drawn.Contains(words)) is string unread)
                    {
                        cases++;
                        var showing = flow.Frame!.Lines.Select(line => all.IndexOf(line.Words)).Where(index => index >= 0).Min();
                        Assert.That(all.IndexOf(unread), Is.GreaterThanOrEqualTo(showing), at + ": laid again, the first unread line is behind the part showing: " + unread);
                    }
                    // Read on, part by part, until Clear can be pressed: never before every line has shown.
                    for (var part = 0; part < 10; part++)
                    {
                        DrawAndNote();
                        var undrawn = all.Where(words => !drawn.Contains(words)).ToList();
                        Assert.That(flow.Frame!.Footer[PromptSlot.FarRight]!.Available && undrawn.Count > 0, Is.False,
                            at + ": Clear with a line never drawn: " + string.Join(" / ", undrawn));
                        if (flow.Frame!.Footer[PromptSlot.FarRight]!.Available || flow.Frame!.Lines.LastOrDefault(line => line.Action == NewProjectScreens.NextPart) is not { } row) break;
                        host.Now += 1;
                        flow.Act(NewProjectScreens.NextPart, row.Key);
                    }
                    Assert.That(flow.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.True, at + ": read to its end, Clear can be pressed");
                }
            }
            Assert.That(cases, Is.GreaterThan(0), first + " to " + then + ": some layout leaves a line undrawn");
        }
    }

    /// <summary>The unknown start laid one line a part at the larger size and [0, 1], [2, 3], [4] at the smaller, read to its last part.</summary>
    private static NewProjectFlow ReadUnknownStart(Host host, Kept kept)
    {
        host.TextSize = TextSize.Larger;
        host.RowWidth = size => size == TextSize.Larger ? 24 : 30;
        host.Height = (rows, beside) => MenuPage.Height(host.TextSize, rows, besideMenu: beside) * 0.74f;
        var flow = Flow(host, kept);
        flow.Open(null, null);
        ReadUnknownStartOn(flow, host);
        return flow;
    }

    /// <summary>Draws each part of the unknown start from the one showing to its last, the words of every one.</summary>
    private static List<string> ReadUnknownStartOn(NewProjectFlow flow, Host host)
    {
        var seen = new List<string>();
        for (var part = 0; part < 10; part++)
        {
            seen.AddRange(flow.Frame!.Lines.Select(line => line.Words));
            flow.Drawn(flow.Frame!, null);
            if (flow.Frame!.Lines.LastOrDefault(line => line.Action == NewProjectScreens.NextPart) is not { } row) break;
            host.Now += 1;
            flow.Act(NewProjectScreens.NextPart, row.Key);
        }
        return seen;
    }

    [Test]
    public void YesClearLapsesWhenTheUnknownStartChangesWhileItWaits()
    {
        // Yes pressed on the page drawn before the change, which still offers it, before and after the page is built again.
        foreach (var builtFirst in new[] { false, true })
        {
            var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-00000000dead" };
            var host = new Host();
            var flow = ReadUnknownStart(host, kept);
            Assert.That(flow.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.True, "read to its last part");
            Press(flow, NewProjectScreens.Clear, null);
            Assert.That(flow.Frame!.Footer.All.Any(each => each.Prompt.Id == NewProjectScreens.ConfirmClear && each.Prompt.Available), Is.True, "Yes, clear waits");
            Draw(flow, flow.Frame!);

            // The computer's record arrives while Yes, clear waits, changing a line read on an earlier part.
            host.State = Recorded(kept.Id!, CommandStatus.Accepted);
            flow.Tick();
            if (builtFirst) Assert.That(flow.Frame!.Footer.All.Any(each => each.Prompt.Id == NewProjectScreens.ConfirmClear), Is.False, "the Yes lapses as the page is built");
            flow.Act(NewProjectScreens.ConfirmClear, null);
            Assert.That(kept.Id, Is.Not.Null, (builtFirst ? "built first" : "pressed first") + ": nothing cleared by a Yes that lapsed");
            var changed = flow.Frame!;
            Assert.That(changed.Footer.All.Any(each => each.Prompt.Id == NewProjectScreens.ConfirmClear), Is.False, "the Yes lapsed");
            Assert.That(changed.Lines.Select(line => line.Words), Does.Contain(EntryText.Recorded(CommandStatus.Accepted)), "the part with the line that changed shows");
            Assert.That(changed.Footer[PromptSlot.FarRight]!.Available, Is.False, "Clear waits for it to be read again");

            var seen = ReadUnknownStartOn(flow, host);
            Assert.That(seen, Does.Contain(EntryText.ChangedBeforeClear), "the page says why nothing was cleared");
            Assert.That(flow.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.True, "read again, Clear can be pressed");
            Press(flow, NewProjectScreens.Clear, null);
            Press(flow, NewProjectScreens.ConfirmClear, null);
            Assert.That(kept.Id, Is.Null);
        }
    }

    /// <summary>The control plane's state once the computer has recorded command <paramref name="id"/>.</summary>
    private static ClientProjection Recorded(string id, CommandStatus status)
    {
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(2);
        snapshot.Commands = new List<CommandView>
        {
            new() { CommandId = id, Status = status, IssuedAt = Samples.Time, UpdatedAt = Samples.Time },
        };
        state.ApplySnapshot(snapshot, new StateChanges());
        return state;
    }

    [Test]
    public void ClearPressedOnAPageDrawnBeforeTheUnknownStartChangedLapsesAsItIsBuilt()
    {
        // The record arrives and changes the line on part 3 of 5 as the person, on the last part drawn, presses Clear:
        // before the flow ticks, ticked but not built again, and built again but not drawn.
        foreach (var order in new[] { "pressed before the tick", "ticked, not built", "built, not drawn" })
        {
            var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-00000000dead" };
            var host = new Host();
            var flow = ReadUnknownStart(host, kept);
            Assert.That(flow.Frame!.Footer[PromptSlot.FarRight]!.Available, Is.True, order + ": read to its last part, Clear stands");
            Draw(flow, flow.Frame!);
            host.State = Recorded(kept.Id!, CommandStatus.Accepted);
            if (order != "pressed before the tick") flow.Tick();
            if (order == "built, not drawn") _ = flow.Frame;
            flow.Act(NewProjectScreens.Clear, null);
            var built = flow.Frame!;
            Assert.That(built.Footer.All.Any(each => each.Prompt.Id == NewProjectScreens.ConfirmClear), Is.False, order + ": armed by words the person never saw, the Yes lapses");
            Assert.That(built.Lines.Select(line => line.Words), Does.Contain(EntryText.Recorded(CommandStatus.Accepted)), order + ": the part with the line that changed shows");
            flow.Act(NewProjectScreens.ConfirmClear, null);
            Assert.That(kept.Id, Is.Not.Null, order + ": nothing cleared");
            Assert.That(ReadUnknownStartOn(flow, host), Does.Contain(EntryText.ChangedBeforeClear), order + ": the page says why");
            Press(flow, NewProjectScreens.Clear, null);
            Press(flow, NewProjectScreens.ConfirmClear, null);
            Assert.That(kept.Id, Is.Null, order + ": read again, it clears");
        }
    }

    [Test]
    public void TheUnknownStartChangingKeepsThePartBeingRead()
    {
        // One line a part: the person turns to part 2, the warning, and the record changes part 3 at once.
        var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-00000000dead" };
        var host = new Host { TextSize = TextSize.Larger, RowWidth = size => size == TextSize.Larger ? 24 : 30 };
        host.Height = (rows, beside) => MenuPage.Height(host.TextSize, rows, besideMenu: beside) * 0.74f;
        var flow = Flow(host, kept);
        flow.Open(null, null);
        Draw(flow, flow.Frame!);
        host.Now += 1;
        flow.Act(NewProjectScreens.NextPart, Turn(flow.Frame!)!.Value.Key);
        var warning = flow.Frame!;
        Assert.That(warning.Lines.Select(line => line.Words), Does.Contain(EntryText.PreviousRequestLine), "part 2, the warning");
        Draw(flow, warning);
        host.State = Recorded(kept.Id!, CommandStatus.Accepted);
        flow.Tick();
        Assert.That(flow.Frame!.Lines.Select(line => line.Words), Does.Contain(EntryText.PreviousRequestLine), "laid again, still the part being read, never past it before its pause");
        Draw(flow, flow.Frame!);
        host.Now += 1;
        flow.Act(NewProjectScreens.NextPart, Turn(flow.Frame!)!.Value.Key);
        Assert.That(flow.Frame!.Lines.Select(line => line.Words), Does.Contain(EntryText.Recorded(CommandStatus.Accepted)), "then on to the line that changed");
    }

    [Test]
    public void YesClearWaitsForTheConnection()
    {
        var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-00000000dead" };
        var host = new Host();
        var flow = ReadUnknownStart(host, kept);
        Press(flow, NewProjectScreens.Clear, null);
        Draw(flow, flow.Frame!);
        // The connection drops; Yes, on the page drawn while connected, clears nothing.
        host.Connected = false;
        flow.Act(NewProjectScreens.ConfirmClear, null);
        Assert.That(kept.Id, Is.Not.Null, "nothing cleared while not connected");
    }

    [Test]
    public void YesClearStaysWhenTheUnknownStartIsOnlyLaidAgain()
    {
        var kept = new Kept { Id = "01a0dcf1-5a80-7000-8000-00000000dead" };
        var host = new Host();
        var flow = ReadUnknownStart(host, kept);
        Press(flow, NewProjectScreens.Clear, null);
        // The text a step smaller lays its five parts again as three, every line's words the same and drawn.
        host.TextSize = TextSize.Standard;
        flow.Tick();
        var laid = flow.Frame!;
        Assert.That(laid.Footer.All.Any(each => each.Prompt.Id == NewProjectScreens.ConfirmClear && each.Prompt.Available), Is.True, "Yes, clear still waits");
        Assert.That(laid.Lines.Select(line => line.Words), Does.Not.Contain(EntryText.ChangedBeforeClear));
        Press(flow, NewProjectScreens.ConfirmClear, null);
        Assert.That(kept.Id, Is.Null);
    }

    [Test]
    public void AQuestionsAnswersAreLaidAgainWhenHeardWordsAddALine()
    {
        static string Seen(MenuFrame frame) => string.Join(" | ", frame.Lines.Select(line => line.Words));
        var mattered = 0;
        foreach (var factor in new[] { 0.8f, 0.9f, 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f })
        {
            var host = new Host { TextSize = TextSize.Larger };
            host.Height = (rows, beside) => MenuPage.Height(host.TextSize, rows, besideMenu: beside) * factor;
            var flow = Flow(host);
            flow.Open(null, null);
            Press(flow, NewProjectScreens.ChooseQuestions, null);
            Press(flow, NewProjectScreens.BeginQuestions, null);
            for (var turned = 0; turned < 5 && !flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.TypeFixedAnswer) && Turn(flow.Frame!) is { } turn; turned++)
            {
                Press(flow, turn.Id, turn.Key);
            }
            host.Typed.Enqueue("A page for the club");
            Press(flow, NewProjectScreens.TypeFixedAnswer, null);
            var typed = Seen(flow.Frame!);
            // Heard words replace the typed answer and add the note to check them: the page is laid again for it.
            flow.Heard("A page of race times for the club");
            var heard = flow.Frame!;
            Assert.That(heard.Lines.Any(line => line.Words == VoiceText.HeardNote), Is.True, "x" + factor + ": the note to check heard words");
            // As a layout made afresh for the same lines gives it, the text size changed and back.
            host.TextSize = TextSize.Standard;
            flow.Tick();
            host.TextSize = TextSize.Larger;
            flow.Tick();
            Assert.That(Seen(heard), Is.EqualTo(Seen(flow.Frame!)), "x" + factor + ": laid again with the note, as afresh");
            if (Seen(heard).Replace(" | " + VoiceText.HeardNote, "").Replace("A page of race times for the club", "A page for the club") != typed) mattered++;
        }
        Assert.That(mattered, Is.GreaterThan(0), "the note changes how some page is laid");
    }

    [Test]
    public void ATurnFromWhatShowedBeforeTurnsNothingOnceSomethingElseShows()
    {
        var host = new Host { TextSize = TextSize.Larger, Height = (rows, _) => MenuPage.Height(TextSize.Larger, rows) * 0.8f };
        var flow = Flow(host);
        flow.Open(null, null);
        Press(flow, NewProjectScreens.ChooseQuestions, null);
        Press(flow, NewProjectScreens.BeginQuestions, null);
        var first = flow.Frame!;
        var seen = Turn(first)!.Value;
        Assert.That((first.Lines[0].Words, seen.Id), Is.EqualTo((ProjectIdea.Questions[0].Prompt, NewProjectScreens.MoreAnswers)), "the question first, on a page of its own");
        Draw(flow, first);
        // Quick presses on the frame still showing: Your idea, then the question again, built but not drawn yet.
        flow.Act(MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.YourIdea));
        Assert.That(flow.Frame!.Sections.Single(section => section.Chosen).Key, Is.EqualTo(NewProjectScreens.Key(NewProjectStep.YourIdea)));
        flow.Act(MenuFrame.ChooseSection, NewProjectScreens.Key(NewProjectStep.Questions));
        var again = flow.Frame!;
        Assert.That((again.Lines[0].Words, Turn(again)?.Key), Is.EqualTo((ProjectIdea.Questions[0].Prompt, seen.Key)), "its first page again, turned by a row keyed the same");
        // The turn the person saw before what shows changed turns nothing now.
        flow.Act(seen.Id, seen.Key);
        Assert.That(flow.Frame!.Lines[0].Words, Is.EqualTo(ProjectIdea.Questions[0].Prompt), "still the question's first page");
    }

    [Test]
    public async Task APageReadsItsRoomOnceSoEveryLineShowsOnceAsTheHeadMoves()
    {
        var root = new LocationRoot
        {
            Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
            Folders = Enumerable.Range(1, 12).Select(index => new LocationFolder { Name = "folder-" + index, Path = "/Users/person/Projects/folder-" + index }).ToList(),
        };
        var routes = new Routes();
        routes.Answers["GET /api/locations"] = () => HalcyonicJson.Serialize(new LocationsResponse { Roots = new List<LocationRoot> { root } });
        var host = new Host { Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
        Press(flow, NewProjectScreens.ChooseWhere, null);
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseFolder));

        // The head moves: every reading of the room gives another height.
        var tall = MenuPage.Height(TextSize.Standard, 1);
        var reads = 0;
        host.Height = (_, _) => reads++ % 2 == 0 ? tall * 0.7f : tall * 0.5f;
        flow.Tick();
        var pages = new List<MenuFrame> { flow.Frame! };
        for (var turn = 0; turn < 20 && Turn(pages[^1]) is { } next && next.Words != "First page"; turn++)
        {
            Press(flow, next.Id, next.Key);
            host.State = OnJournal(Samples.JournalId);
            flow.Tick();
            pages.Add(flow.Frame!);
        }
        var folders = pages.SelectMany(page => page.Lines).Where(line => line.Action == NewProjectScreens.ChooseFolder).Select(line => line.Key).ToList();
        Assert.That(folders, Has.Count.EqualTo(14).And.Unique, "every folder once through a whole turn round");
    }

    [Test]
    public void TheReviewsPartsStayPutAsTheHeadMoves()
    {
        var host = new Host();
        var flow = Recapped(host);
        Assert.That(flow.Idea!.Rewrite(string.Join(" ", Enumerable.Repeat("Track recipes, plan the week's dinners and write the shopping list.", 20))), Is.True);
        Press(flow, NewProjectScreens.StartBuilding, null);
        flow.Drawn(flow.Frame!, null);
        host.Now += 1;
        Press(flow, NewProjectScreens.NextPart, null);
        var (parts, part) = (flow.Review!.PageCount, flow.Review.Page);
        Assert.That((parts > 2, part), Is.EqualTo((true, 1)), "a long task, read to its second part");

        // The head moves: every reading of the room gives another height.
        var tall = MenuPage.Height(TextSize.Standard, 1);
        var reads = 0;
        host.Height = (_, _) => reads++ % 2 == 0 ? tall * 1.6f : tall * 0.6f;
        for (var redraw = 0; redraw < 4; redraw++)
        {
            // The journal moves on, so the page is built again, and drawn.
            var state = new ClientProjection();
            state.ApplySnapshot(Samples.Snapshot(10 + redraw), new StateChanges());
            host.State = state;
            flow.Tick();
            flow.Drawn(flow.Frame!, null);
            Assert.That((flow.Review!.PageCount, flow.Review.Page), Is.EqualTo((parts, part)), "laid out once for the review, its reading kept");
        }
    }

    [Test]
    public void ReopeningAndANewQuestionStartAtTheFirstPage()
    {
        var host = new Host { TextSize = TextSize.Larger };
        var flow = Recapped(host);
        Press(flow, Footer.NextPage, null);
        Assert.That(flow.Frame!.Lines[0].Action, Is.Not.Null, "on a later page of the recap");
        flow.Close();
        flow.Open(null, null);
        Assert.That(flow.Frame!.Lines[0].Action, Is.Null, "opened again on the first page, its first line first");

        var guided = new Host { TextSize = TextSize.Larger, Height = (rows, _) => MenuPage.Height(TextSize.Larger, rows) * 0.8f };
        var questions = Flow(guided);
        questions.Open(null, null);
        Press(questions, NewProjectScreens.ChooseQuestions, null);
        Press(questions, NewProjectScreens.BeginQuestions, null);
        var turn = Turn(questions.Frame!)!.Value;
        Press(questions, turn.Id, turn.Key);
        var choice = questions.Frame!.Lines.First(line => line.Action == NewProjectScreens.ChooseFixedAnswer);
        Press(questions, NewProjectScreens.ChooseFixedAnswer, choice.Key);
        Press(questions, NewProjectScreens.NextQuestion, null);
        Assert.That(questions.Idea!.Question, Is.EqualTo(1));
        Assert.That(questions.Frame!.Lines[0].Words, Is.EqualTo(ProjectIdea.Questions[1].Prompt), "the next question opens on its first page, the question heading it");
    }

    [Test]
    public void TheRecapWithStartBuildingsLongestReasonFitsEveryPage()
    {
        var kept = new Kept();
        var host = new Host();
        var flow = Recapped(host, kept);
        kept.Id = "01a0dcf1-5a80-7000-8000-00000000dead";
        flow.Tick();
        Assert.That(flow.Frame!.Reason, Is.EqualTo(EntryText.PreviousRequestLine), "the longest reason Start building gives live");
        FitsEveryPage(flow, host, "the recap with its longest reason");
    }

    [Test]
    public void EveryQuestionPageFitsAtBothSizes()
    {
        var host = new Host();
        var flow = Flow(host);
        flow.Open(null, null);
        Press(flow, NewProjectScreens.ChooseQuestions, null);
        Press(flow, NewProjectScreens.BeginQuestions, null);
        FitsEveryPage(flow, host, "a fixed question");
    }

    [Test]
    public void ALongListOfFoldersPagesAndEveryFolderShowsOnce()
    {
        var root = new LocationRoot
        {
            Path = "/Users/person/Projects", Name = "Projects", Status = LocationRootStatus.Available, FoldersTruncated = false,
            Folders = Enumerable.Range(1, 12).Select(index => new LocationFolder { Name = "folder-" + index, Path = "/Users/person/Projects/folder-" + index }).ToList(),
        };
        var routes = new Routes();
        routes.Answers["GET /api/locations"] = () => HalcyonicJson.Serialize(new LocationsResponse { Roots = new List<LocationRoot> { root } });
        var host = new Host { Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.Folder));
        Press(flow, NewProjectScreens.ChooseWhere, null);
        Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseFolder)).Wait();
        var shown = FitsEveryPage(flow, host, "where its files live");
        Assert.That(shown.Count(line => line.Action == NewProjectScreens.ChooseFolder), Is.EqualTo(14), "a new folder, the place itself and 12 folders, each once");
        Press(flow, NewProjectScreens.ChooseFolder, shown.Single(line => line.Words == "folder-12").Key);
        Press(flow, NewProjectScreens.Done, null);
        Assert.That(flow.Idea!.Folder!.Describe(), Is.EqualTo("folder-12 in Projects"), "a folder on a later page is chosen like any other");
    }

    [Test]
    public void ManyAgentAppsAndModelsPage()
    {
        var local = Runtime("local", ModelChoice.Listed);
        var host = new Host { State = WithRuntimes(new[] { Runtime("mock"), local }.Concat(Enumerable.Range(1, 6).Select(index => Runtime("app-" + index))).ToArray()) };
        var routes = new Routes();
        routes.Answers["GET /api/runtimes/local/models"] = () => HalcyonicJson.Serialize(new RuntimeModelsResponse
        {
            RuntimeId = "local",
            Result = new AvailableModels
            {
                Models = Enumerable.Range(1, 10).Select(index => new RuntimeModel
                {
                    ModelRef = "ollama/model-" + index, DisplayName = "Model " + index, Served = ModelServed.ThisMac, ToolCalling = ModelToolCalling.Declared,
                }).ToList(),
            },
        });
        host.Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes);
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        var apps = FitsEveryPage(flow, host, "how it runs, its agent apps");
        Assert.That(apps.Count(line => line.Action == NewProjectScreens.ChooseRuntime), Is.EqualTo(8), "every agent app once");

        Press(flow, NewProjectScreens.ChooseRuntime, "local");
        Until(flow, () => flow.Frame!.Lines.Count(line => line.Action == NewProjectScreens.ChooseModel) > 0).Wait();
        var models = FitsEveryPage(flow, host, "how it runs, its models");
        Assert.That(models.Count(line => line.Action == NewProjectScreens.ChooseModel), Is.EqualTo(10), "every model once");
    }

    /// <summary>
    /// A request for the agent app's models that fails, refused or never answered, shows the step's own
    /// words, never the failure's message, which names the control plane and can hold an address.
    /// </summary>
    [Test]
    public void AFailedRequestForModelsNeverShowsItsMessage()
    {
        foreach (var unreachable in new[] { false, true })
        {
            var local = Runtime("local", ModelChoice.Listed);
            var host = new Host { State = WithRuntimes(Runtime("mock"), local) };
            var routes = new Routes();
            if (unreachable) routes.Unreachable["GET /api/runtimes/local/models"] = "connect ECONNREFUSED 192.168.1.20:47801";
            host.Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes);
            var flow = Recapped(host);
            Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
            Press(flow, NewProjectScreens.MoreOptions, null);
            Press(flow, NewProjectScreens.ChooseRuntime, "local");
            Until(flow, () => flow.Frame!.Lines.Any(line => line.Words == EntryText.ModelsUnreadable)).Wait();
            Assert.That(flow.Frame!.Lines.Select(line => line.Words), Has.None.Contains("control plane").And.None.Contains("127.0.0.1").And.None.Contains("ECONNREFUSED"));
        }
    }

    [Test]
    public void StartingPagesItsStepsAtTheLargerSize()
    {
        var host = new Host();
        var flow = Recapped(host);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build));
        var shown = FitsEveryPage(flow, host, "starting");
        Assert.That(shown.Count(line => line.Words == EntryText.StepName(BuildStepKind.CreateProject, true)), Is.EqualTo(1), "every step once");
        Assert.That(host.Sent, Has.Count.EqualTo(1), "turning a page sends nothing");
    }

    [Test]
    public void APageTurnsOnlyFromThePageItWasPressedOn()
    {
        var host = new Host { TextSize = TextSize.Larger };
        var flow = Recapped(host);
        var first = flow.Frame!;
        flow.Drawn(first, null);
        Assert.That(first.Footer[PromptSlot.Secondary]!.Id, Is.EqualTo(Footer.NextPage), "the recap's facts page as the menu's lists, by the footer's Next page");
        flow.Act(Footer.NextPage, null);
        var second = flow.Frame!;
        flow.Act(Footer.NextPage, null);
        Assert.That(flow.Frame!.Lines.Select(line => line.Key), Is.EqualTo(second.Lines.Select(line => line.Key)), "a second press before the next page is drawn turns nothing");
    }

    [Test]
    public void AfterAReconnectMidBuildTheSameFlowSettlesTheStartFromItsRecordAndNeverCreatesTwice()
    {
        var drop = new TaskCompletionSource<CommandAckMessage>();
        var host = new Host { Acknowledge = _ => drop.Task };
        var kept = new Kept();
        var flow = Recapped(host, kept);
        Press(flow, NewProjectScreens.StartBuilding, null);
        ReadToTheEnd(flow, host);
        Press(flow, NewProjectScreens.ConfirmStart, null);
        var create = host.Sent.Single();

        // The socket drops with the create on its way, so its outcome is unknown; New project closes meanwhile.
        host.Connected = false;
        drop.SetException(new CommandOutcomeUnknownException(create.CommandId, "The socket closed."));
        Press(flow, Footer.Close, null);
        flow.Tick();
        Assert.That(kept.Id, Is.EqualTo(create.CommandId));

        // Reconnected to the same journal: its record settles the create, and the build goes on.
        host.Connected = true;
        host.State = With(Completed(create, new ProjectCreatedResult { ProjectId = "p1" }));
        flow.Tick();
        Assert.That(host.Sent.Count(each => each is ProjectCreateCommand), Is.EqualTo(1), "never a second project.create");
        Assert.That(host.Sent.Last(), Is.InstanceOf<WorkstreamCreateCommand>());
        Assert.That(kept.Id, Is.EqualTo(host.Sent.Last().CommandId), "the kept id follows the build's own step");
        flow.Open(null, null);
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Build));
    }

    [Test]
    public async Task APressOnAFrameDrawnBeforeANewReplyOrListingNeverTakesAnotherItem()
    {
        var routes = new Routes();
        routes.Answers["GET /api/companion"] = () => HalcyonicJson.Serialize(new AvailableCompanion
        {
            Companion = new CompanionModel { Name = "local-model:tag", Served = "this_mac" }, MaxQuestions = 4,
        });
        var replies = new Queue<CompanionReply>(new CompanionReply[] { Companions.Ask(), Companions.Ask("Who keeps the page up to date?", "One organiser", "Each runner") });
        routes.Answers["POST /api/companion/replies"] = () => HalcyonicJson.Serialize(Companions.Response(replies.Dequeue()));
        var host = new Host { Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Flow(host);
        flow.Open(null, null);
        await Until(flow, () => flow.Frame!.Lines.Any(line => line.Action == NewProjectScreens.ChooseCompanion));
        Press(flow, NewProjectScreens.BeginCompanion, null);
        await Until(flow, () => flow.Idea!.Companion!.Latest is AskReply);
        flow.Act(NewProjectScreens.ChooseSuggestion, Suggestion(flow, "Each runner"));
        Assert.That(flow.Idea!.Companion!.Chosen, Is.EqualTo(CompanionAnswerRow.None), "a frame never drawn takes no press");

        Press(flow, NewProjectScreens.ChooseSuggestion, Suggestion(flow, "Each runner"));
        var earlier = Suggestion(flow, "One organiser");
        Press(flow, NewProjectScreens.SendAnswer, null);
        await Until(flow, () => flow.Idea.Companion.Latest is AskReply asked && asked.Question.Text == "Who keeps the page up to date?");
        // The person's press lands on the earlier frame, the one last drawn: the same words answer another question now.
        flow.Act(NewProjectScreens.ChooseSuggestion, earlier);
        Assert.That(flow.Idea.Companion.Chosen, Is.EqualTo(CompanionAnswerRow.None), "never the same words on the next question");
        Press(flow, NewProjectScreens.ChooseSuggestion, Suggestion(flow, "One organiser"));
        Assert.That(flow.Idea.Companion.Chosen, Is.EqualTo(CompanionAnswerRow.Suggestion), "a press on the question drawn now takes it");
    }

    [Test]
    public void AKeptAgentAppListsItsModelsOnlyWhenThePersonOpensHowItRuns()
    {
        var listing = Samples.MockRuntime();
        listing.RuntimeId = "local";
        listing.ModelChoice = ModelChoice.Listed;
        var state = new ClientProjection();
        var snapshot = Samples.Snapshot(1);
        snapshot.Runtimes = new List<RuntimeDescriptor> { listing };
        state.ApplySnapshot(snapshot, new StateChanges());
        var routes = new Routes();
        routes.Answers["GET /api/runtimes/local/models"] = () => HalcyonicJson.Serialize(new RuntimeModelsResponse
        {
            RuntimeId = "local", Result = new AvailableModels { Models = new List<RuntimeModel>() },
        });
        var store = new Memory();
        var idea = new ProjectIdea();
        idea.UseIdea("A page of race times");
        var draft = new NewWorkDraft(Commands);
        draft.ChooseRuntime(listing);
        store.Drafts.Add(CreationDraft.Of(Samples.JournalId, "", idea, null, draft, DateTimeOffset.UtcNow)!);

        var host = new Host { State = state, Api = new ControlPlaneApi(new Uri("http://127.0.0.1:47800/"), "test-token", routes) };
        var flow = Flow(host, store: store);
        flow.Open(null, null);
        flow.Tick();
        Assert.That(flow.Step, Is.EqualTo(NewProjectStep.Recap));
        Assert.That(routes.Asked.Any(asked => asked.EndsWith("/models", StringComparison.Ordinal)), Is.False, "opening reads no models: listing may start the agent app");
        Press(flow, NewProjectScreens.ChooseFact, NewProjectScreens.FactKey(RecapFact.HowItRuns));
        Press(flow, NewProjectScreens.MoreOptions, null);
        Assert.That(routes.Asked.Count(asked => asked == "GET /api/runtimes/local/models"), Is.EqualTo(1), "on the person's press");
    }
}
