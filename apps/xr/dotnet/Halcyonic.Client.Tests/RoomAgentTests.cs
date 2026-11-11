#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>The agent and its level (the redesign's addendum A1): the page, what a pick does, and the redesign's view of it.</summary>
public sealed class RoomAgentTests
{
    private static readonly string[] Names = { "Glimmer 30B", "Ember 7B", "Tide 12B", "Kite 4B", "Lantern 9B" };

    private sealed class Stand
    {
        public AgentOffer? Offer;
        public readonly List<bool> Levels = new List<bool>();
        public readonly List<string> Picks = new List<string>();
        public int Changed;
        public int Closed;
        public readonly AgentColumn Column;

        public Stand(AgentOffer? offer)
        {
            Offer = offer;
            Column = new AgentColumn(() => Offer, Levels.Add, Picks.Add);
            Column.Changed += () => Changed++;
            Column.Closed += () => Closed++;
        }
    }

    private static AgentOffer Offer(bool offersNeverAsk = true, bool neverAsk = false, int options = 3, int chosen = 0, bool listsModels = true) =>
        new AgentOffer(Names[chosen], listsModels, offersNeverAsk, neverAsk,
            Names.Take(options).Select((name, index) => new AgentOption("key-" + index, name, index == chosen)).ToList());

    private static string[] Words(MenuFrame frame) => Acting(frame).Select(line => line.Words).ToArray();

    /// <summary>The page's lines that act: every line but the first, the fact of what the agent's name is.</summary>
    private static List<PageLine> Acting(MenuFrame frame) => frame.Lines.Where(line => line.Action != null).ToList();

    [Test]
    public void The_page_names_the_agent_and_offers_the_two_levels_then_Change()
    {
        var frame = new Stand(Offer()).Column.Frame!;
        Assert.That(frame.Subject, Is.EqualTo("Glimmer 30B"), "the name alone, never joined to what it is on a dot");
        Assert.That((frame.Lines[0].Words, frame.Lines[0].Action), Is.EqualTo((AgentColumn.ModelWords, (string?)null)), "what the name is, a fact of its own");
        Assert.That(Words(frame), Is.EqualTo(new[] { "Ask First", "Never Ask", "Change" }));
        Assert.That(Acting(frame).Select(line => line.Action), Is.EqualTo(new[] { AgentColumn.Level, AgentColumn.Level, AgentColumn.Change }));
        Assert.That(Acting(frame).Select(line => line.Key), Is.EqualTo(new[] { "ask", "never_ask", null }));
        Assert.That(Acting(frame).Select(line => line.Chosen), Is.EqualTo(new[] { true, false, false }), "Ask First is the level in force");
        Assert.That(Acting(frame).Take(2).All(line => line.AnswerMark == AnswerMark.Single), "the levels are answers, one of two");
        var back = frame.Footer[PromptSlot.Close]!;
        Assert.That((back.Words, back.Id, back.Kind), Is.EqualTo(("Back", Footer.Close, PromptKind.Close)));
        Assert.That(frame.Footer.All.Count(), Is.EqualTo(1), "nothing but the way back stands under the page");
    }

    [Test]
    public void Never_Ask_chosen_is_the_one_marked_and_an_agent_app_is_named_as_one()
    {
        var frame = new Stand(Offer(neverAsk: true, listsModels: false)).Column.Frame!;
        Assert.That((frame.Subject, frame.Lines[0].Words), Is.EqualTo(("Glimmer 30B", AgentColumn.AgentAppWords)));
        Assert.That(Acting(frame).Select(line => line.Chosen), Is.EqualTo(new[] { false, true, false }));
    }

    [Test]
    public void A_runtime_that_does_not_offer_it_draws_no_levels_and_is_never_at_never_ask()
    {
        var offer = Offer(offersNeverAsk: false, neverAsk: true);
        Assert.That(offer.NeverAsk, Is.False, "a level that is not offered is never the one chosen");
        var stand = new Stand(offer);
        Assert.That(Words(stand.Column.Frame!), Is.EqualTo(new[] { "Change" }));
        stand.Column.Act(AgentColumn.Level, AgentColumn.Never);
        Assert.That((stand.Levels.Count, stand.Closed, stand.Changed), Is.EqualTo((0, 0, 0)), "a level that is not offered cannot be chosen");
    }

    [Test]
    public void One_agent_alone_draws_no_Change_and_nothing_to_choose_draws_no_page()
    {
        var alone = new Stand(Offer(options: 1));
        Assert.That(Words(alone.Column.Frame!), Is.EqualTo(new[] { "Ask First", "Never Ask" }));
        alone.Column.Act(AgentColumn.Change, null);
        Assert.That(alone.Changed, Is.EqualTo(0));
        Assert.That(Offer(offersNeverAsk: false, options: 1).Chooses, Is.False);
        Assert.That(new Stand(Offer(offersNeverAsk: false, options: 1)).Column.Frame, Is.Null, "one runtime, no models, no level: no page");
        Assert.That(new Stand(null).Column.Frame, Is.Null, "no agent to name: no page");
    }

    [TestCase(AgentColumn.Never, true)]
    [TestCase(AgentColumn.Ask, false)]
    public void A_level_is_handed_over_at_once_and_the_page_closes(string key, bool never)
    {
        var stand = new Stand(Offer(neverAsk: !never));
        stand.Column.Act(AgentColumn.Level, key);
        Assert.That(stand.Levels, Is.EqualTo(new[] { never }));
        Assert.That((stand.Closed, stand.Changed, stand.Picks.Count), Is.EqualTo((1, 0, 0)), "no confirmation: the level is kept and the page leaves");
    }

    [Test]
    public void A_level_with_a_key_that_is_not_one_does_nothing()
    {
        var stand = new Stand(Offer());
        stand.Column.Act(AgentColumn.Level, "always");
        stand.Column.Act(AgentColumn.Level, null);
        Assert.That((stand.Levels.Count, stand.Closed, stand.Changed), Is.EqualTo((0, 0, 0)));
    }

    [Test]
    public void Change_turns_the_page_to_the_agents_and_models_as_answers()
    {
        var stand = new Stand(Offer(chosen: 1));
        stand.Column.Act(AgentColumn.Change, null);
        Assert.That((stand.Changed, stand.Closed), Is.EqualTo((1, 0)));
        var frame = stand.Column.Frame!;
        Assert.That((frame.Subject, frame.Lines[0].Words), Is.EqualTo(("Ember 7B", AgentColumn.ModelWords)), "the name and its fact stay the page's");
        Assert.That(Words(frame), Is.EqualTo(new[] { "Glimmer 30B", "Ember 7B", "Tide 12B" }));
        Assert.That(Acting(frame).Select(line => (line.Action, line.Key, line.Chosen)),
            Is.EqualTo(new[] { (AgentColumn.Pick, "key-0", false), (AgentColumn.Pick, "key-1", true), (AgentColumn.Pick, "key-2", false) }));
        Assert.That(Acting(frame).All(line => line.AnswerMark == AnswerMark.Single));
    }

    [Test]
    public void More_than_three_page_by_Next_Choices_starting_on_the_chosen_ones_page()
    {
        var stand = new Stand(Offer(options: 5, chosen: 4));
        Assert.That(AgentColumn.Pages(5), Is.EqualTo(2));
        stand.Column.Act(AgentColumn.Change, null);
        Assert.That(Words(stand.Column.Frame!), Is.EqualTo(new[] { "Kite 4B", "Lantern 9B", "Next Choices · 2 of 2" }), "it opens on the page that holds the one chosen");
        stand.Column.Act(Footer.NextPage, null);
        Assert.That(Words(stand.Column.Frame!), Is.EqualTo(new[] { "Glimmer 30B", "Ember 7B", "Tide 12B", "Next Choices · 1 of 2" }));
        Assert.That(stand.Column.Frame!.Lines.Last().Action, Is.EqualTo(Footer.NextPage));
        stand.Column.Act(Footer.NextPage, null);
        Assert.That(Words(stand.Column.Frame!).First(), Is.EqualTo("Kite 4B"));
        Assert.That(stand.Changed, Is.EqualTo(3));
    }

    [Test]
    public void A_pick_is_handed_over_by_its_key_and_the_page_closes()
    {
        var stand = new Stand(Offer());
        stand.Column.Act(AgentColumn.Pick, "key-2");
        Assert.That((stand.Picks.Count, stand.Closed), Is.EqualTo((0, 0)), "nothing is picked from the page of levels");
        stand.Column.Act(AgentColumn.Change, null);
        stand.Column.Act(AgentColumn.Pick, "key-9");
        Assert.That((stand.Picks.Count, stand.Closed), Is.EqualTo((0, 0)), "a key that is none of the options' picks nothing");
        stand.Column.Act(AgentColumn.Pick, "key-2");
        Assert.That(stand.Picks, Is.EqualTo(new[] { "key-2" }));
        Assert.That((stand.Closed, stand.Levels.Count), Is.EqualTo((1, 0)));
        Assert.That(Words(stand.Column.Frame!), Does.Contain("Change"), "opened again it is the page of levels");
    }

    [Test]
    public void Back_goes_from_the_agents_to_the_levels_and_from_the_levels_away()
    {
        var stand = new Stand(Offer());
        stand.Column.Act(AgentColumn.Change, null);
        stand.Column.Act(Footer.Close, null);
        Assert.That((stand.Changed, stand.Closed), Is.EqualTo((2, 0)));
        Assert.That(Words(stand.Column.Frame!), Is.EqualTo(new[] { "Ask First", "Never Ask", "Change" }));
        stand.Column.Act(Footer.Close, null);
        Assert.That(stand.Closed, Is.EqualTo(1));
    }

    [Test]
    public void Focus_leaving_turns_the_page_back_to_the_levels()
    {
        var stand = new Stand(Offer());
        stand.Column.FocusLeft();
        Assert.That(stand.Changed, Is.EqualTo(0), "nothing to turn back on the page of levels");
        stand.Column.Act(AgentColumn.Change, null);
        stand.Column.FocusLeft();
        Assert.That(Words(stand.Column.Frame!), Does.Contain("Change"));
    }

    [Test]
    public void The_redesign_draws_the_page_as_the_fact_in_the_panel_and_the_choices_in_the_column()
    {
        var frame = new Stand(Offer(neverAsk: true)).Column.Frame!;
        var bar = RoomBar.Agent(frame, VoiceStage.Idle)!;
        Assert.That(bar.State, Is.EqualTo(RoomBarState.Choose));
        Assert.That((bar.Card!.Title!.Text, bar.Card.Description!.Text, bar.Card.Head), Is.EqualTo(("Glimmer 30B", AgentColumn.ModelWords, RoomCardHead.Agent)), "the panel is the agent's card");
        Assert.That(bar.Card.AnyDot, Is.False);
        Assert.That(bar.Wide, Is.False, "the fact keeps the panel; the choices stand in the column");
        Assert.That(bar.Sentence, Is.Null);
        Assert.That(bar.Column.Select(action => action.Words), Is.EqualTo(new[] { "Ask First", "Never Ask", "Change", "Back" }));
        Assert.That(bar.Column.Select(action => action.Mark), Is.EqualTo(new[] { AnswerMark.Single, AnswerMark.Single, AnswerMark.None, AnswerMark.None }));
        Assert.That(bar.Column.Select(action => action.Chosen), Is.EqualTo(new[] { false, true, false, false }));
        Assert.That(bar.Column.Any(action => action.Main), Is.False, "no action here is the one thing to do");
        Assert.That(bar.Column.All(action => ReferenceEquals(action.Control.Frame, frame)), "every press raises the page's own action");
        Assert.That(bar.Column.Select(action => (action.Control.Action, action.Control.Key)),
            Is.EqualTo(new[] { (AgentColumn.Level, "ask"), (AgentColumn.Level, "never_ask"), (AgentColumn.Change, null), (Footer.Close, (string?)null) }));
        Assert.That(bar.Column.Select(action => action.Place), Is.EqualTo(new[] { KitPlace.Choice, KitPlace.Choice, KitPlace.Route, KitPlace.Back }));
        Assert.That(bar.Column[2].Icon, Is.EqualTo(GlazeIcon.Change));
    }

    [Test]
    public void The_redesign_draws_the_agents_page_with_its_way_to_more_and_Back_last()
    {
        var stand = new Stand(Offer(options: 5));
        stand.Column.Act(AgentColumn.Change, null);
        var bar = RoomBar.Agent(stand.Column.Frame!, VoiceStage.Idle)!;
        Assert.That(bar.Column.Select(action => action.Words), Is.EqualTo(new[] { "Glimmer 30B", "Ember 7B", "Tide 12B", "Next Choices · 1 of 2", "Back" }));
        Assert.That(bar.Column.Select(action => action.Place), Is.EqualTo(new[] { KitPlace.Choice, KitPlace.Choice, KitPlace.Choice, KitPlace.Step, KitPlace.Back }));
        Assert.That(bar.Column.Count(action => action.Chosen), Is.EqualTo(1));
        Assert.That(bar.Column[0].Chosen);
    }

    [Test]
    public void Any_other_page_is_not_the_agents()
    {
        var other = new MenuFrame("Appearance", new Footer(new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close)),
            lines: new[] { new PageLine("Restore Default", action: "appearance-read-restore", opens: true) });
        Assert.That(RoomBar.Agent(other, VoiceStage.Idle), Is.Null);
    }

    [Test]
    public void The_action_at_rest_and_the_plate_at_work_name_the_agent_and_carry_its_level_apart()
    {
        Assert.That(RoomBar.AgentWords("Glimmer 30B", false), Is.EqualTo(((string?)"Glimmer 30B", (string?)"Ask First")));
        Assert.That(RoomBar.AgentWords("Glimmer 30B", true), Is.EqualTo(((string?)"Glimmer 30B", (string?)"Never Ask")));
        Assert.That(RoomBar.AgentWords("Glimmer 30B", null), Is.EqualTo(((string?)"Glimmer 30B", (string?)null)), "a run whose record carries no level names the agent alone");
        Assert.That(RoomBar.AgentWords(null, true), Is.EqualTo(((string?)null, (string?)null)));
        Assert.That(RoomBar.AgentWords("  ", false).Name, Is.Null);
        Assert.That(RoomBar.AgentWords("Glimmer 30B", true).Name, Does.Not.Contain(KitMomentWords.Dot.Trim()), "never joined on a dot");
    }

    // The offer built from the computer's runtimes and the person's choice, and the third action at rest.

    private static RuntimeDescriptor Runtime(string id, bool? offersNeverAsk, bool listsModels = false) => new()
    {
        RuntimeId = id, Kind = id, DisplayName = id, Synthetic = false, OffersNeverAsk = offersNeverAsk, ModelChoice = listsModels ? ModelChoice.Listed : default,
        Capabilities = new RuntimeCapabilities { StartExecution = true, InstructAtRest = true, InstructWhileRunning = true, Interrupt = true },
    };

    private static readonly RuntimeDescriptor Plain = Runtime("plain", offersNeverAsk: null);
    private static readonly RuntimeDescriptor Lister = Runtime("lister", offersNeverAsk: true, listsModels: true);
    private static readonly RuntimeModel[] Listed = { new() { ModelRef = "tide-12b", DisplayName = "Tide 12B" }, new() { ModelRef = "ember-7b", DisplayName = "Ember 7B" } };

    private static IReadOnlyList<RuntimeModel>? ModelsOf(string runtimeId) => runtimeId == "lister" ? Listed : null;

    [Test]
    public void The_offer_lists_every_runtime_then_its_models_in_the_order_the_choice_turns_and_marks_the_chosen()
    {
        var offer = AgentOffer.Of("Tide 12B", new[] { Plain, Lister }, ModelsOf, "lister", "tide-12b", neverAskChosen: true)!;
        Assert.That(offer.Options.Select(option => option.Key), Is.EqualTo(new[]
        {
            AgentOffer.KeyOf("plain", null), AgentOffer.KeyOf("lister", null), AgentOffer.KeyOf("lister", "tide-12b"), AgentOffer.KeyOf("lister", "ember-7b"),
        }));
        Assert.That(offer.Options.Select(option => option.Name), Is.EqualTo(new[]
        {
            KitPresenter.AgentOf(Plain, null, null), KitPresenter.AgentOf(Lister, null, null), KitPresenter.AgentOf(Lister, null, CaptureNames.ModelName("Tide 12B")),
            KitPresenter.AgentOf(Lister, null, CaptureNames.ModelName("Ember 7B")),
        }));
        Assert.That(offer.Options.Select(option => option.Chosen), Is.EqualTo(new[] { false, false, true, false }));
        Assert.That((offer.Agent, offer.ListsModels, offer.OffersNeverAsk, offer.NeverAsk, offer.Chooses), Is.EqualTo(("Tide 12B", true, true, true, true)));
        Assert.That(AgentOffer.Picked(offer.Options[2].Key), Is.EqualTo(("lister", (string?)"tide-12b")));
        Assert.That(AgentOffer.Picked(offer.Options[0].Key), Is.EqualTo(("plain", (string?)null)));
    }

    [Test]
    public void The_offers_level_is_never_ask_only_where_it_was_chosen_and_the_chosen_runtime_offers_it()
    {
        var onPlain = AgentOffer.Of("plain", new[] { Plain, Lister }, ModelsOf, "plain", null, neverAskChosen: true)!;
        Assert.That((onPlain.OffersNeverAsk, onPlain.NeverAsk, onPlain.ListsModels), Is.EqualTo((false, false, false)), "chosen for the project, but this runtime does not offer it: the next start asks");
        Assert.That(onPlain.Options.Single(option => option.Chosen).Key, Is.EqualTo("plain"));
        var notChosen = AgentOffer.Of("lister", new[] { Plain, Lister }, ModelsOf, "lister", null, neverAskChosen: false)!;
        Assert.That((notChosen.OffersNeverAsk, notChosen.NeverAsk, notChosen.ListsModels), Is.EqualTo((true, false, false)));
        Assert.That(AgentOffer.Of("lister", new[] { Plain, Lister }, ModelsOf, "gone", null, false), Is.Null, "a chosen runtime the computer no longer has: no page");
        Assert.That(AgentOffer.Of(null, new[] { Plain }, ModelsOf, "plain", null, false), Is.Null);
        Assert.That(AgentOffer.Of("plain", null, ModelsOf, "plain", null, false), Is.Null);
        var alone = AgentOffer.Of("plain", new[] { Plain }, ModelsOf, "plain", null, false)!;
        Assert.That(alone.Chooses, Is.False, "one runtime, no models, no level: nothing to choose");
    }

    private static KitFileBinding AtRest(KitSelectors? selectors, ApprovalLevel? recorded = null, params WorkspaceAction[] actions)
    {
        var work = new WaitingWork();
        work.Change(execution => { execution.PendingApprovals.Clear(); execution.Status = ExecutionStatus.Running; execution.ApprovalLevel = recorded; }, WorkstreamStatus.Running);
        var running = work.Present();
        var offered = actions.Length > 0 ? actions : new[] { WorkspaceAction.Instruct, WorkspaceAction.Interrupt };
        var workspace = new WorkspacePresentation(running.Character, running.Objective, running.Execution, running.Runtime, offered,
            offered.Where(running.RequiresConfirmation).ToList(), running.Commands, new ActivityEntry[0]);
        var steering = new WorkspaceSteering(new CommandFactory(Samples.Client));
        var frame = FileScreens.Screen(workspace, steering, new FileScreen { Section = FileSection.Activity, Speak = true }, new AnswerRoom(4));
        return KitFileBinding.Of(frame, steering.Armed, workspace, "", new ConnectionStatus(ConnectionPhase.Live), "Storefront",
            new[] { new KitSpineSection(new KitRowModel[0]) }, roomDesign: true, selectors: selectors)!;
    }

    [TestCase(false, "Ask First")]
    [TestCase(true, "Never Ask")]
    public void At_rest_the_row_gains_the_action_that_opens_the_agents_page_reading_the_level_the_next_start_carries(bool neverAsk, string level)
    {
        var plain = RoomBar.Of(AtRest(null), VoiceStage.Idle, sentAwaitingStart: false);
        Assume.That(plain.State, Is.EqualTo(RoomBarState.Rest));
        Assert.That((plain.Row.Count, plain.FromMoment), Is.EqualTo((2, false)), "nothing to choose: the task's own row");
        var binding = AtRest(new KitSelectors(null, false, "Glimmer 30B", agentChangeable: true, offersNeverAsk: true, neverAsk: neverAsk));
        var rest = RoomBar.Of(binding, VoiceStage.Idle, sentAwaitingStart: false);
        Assert.That((rest.State, rest.Pair, rest.FromMoment), Is.EqualTo((RoomBarState.Rest, false, false)), "it decorates the task's own bar");
        Assert.That(rest.Row.Take(2).Select(action => (action.Place, action.Words, action.Main)), Is.EqualTo(plain.Row.Select(action => (action.Place, action.Words, action.Main))),
            "Hold to Talk and Type stand as they did");
        var open = rest.Row[2];
        Assert.That((open.Words, open.Chip, open.Main, open.Place, open.Control.Action, open.Control.Key),
            Is.EqualTo(("Glimmer 30B", (string?)level, false, KitPlace.Step, AgentColumn.Open, (string?)null)), "the level is the action's chip, apart from its words");
        Assert.That(ReferenceEquals(open.Control.Frame, binding.Frame), "its press belongs to the file's own frame");
    }

    [Test]
    public void At_rest_a_runtime_with_no_level_names_the_agent_alone_and_nothing_to_choose_adds_no_action()
    {
        var changeable = RoomBar.Of(AtRest(new KitSelectors(null, false, "Glimmer 30B", agentChangeable: true)), VoiceStage.Idle, sentAwaitingStart: false);
        Assert.That(changeable.Row.Count, Is.EqualTo(3));
        Assert.That((changeable.Row[2].Words, changeable.Row[2].Icon), Is.EqualTo(("Glimmer 30B", (GlazeIcon?)GlazeIcon.CodeFile)));
        var offersOnly = RoomBar.Of(AtRest(new KitSelectors(null, false, "Glimmer 30B", agentChangeable: false, offersNeverAsk: true, neverAsk: false)), VoiceStage.Idle, sentAwaitingStart: false);
        Assert.That((offersOnly.Row[2].Words, offersOnly.Row[2].Chip), Is.EqualTo(("Glimmer 30B", (string?)"Ask First")), "one agent, a level to choose");
        var nothing = RoomBar.Of(AtRest(new KitSelectors(null, false, "Glimmer 30B")), VoiceStage.Idle, sentAwaitingStart: false);
        Assert.That((nothing.Row.Count, nothing.FromMoment), Is.EqualTo((2, false)));
        var unnamed = RoomBar.Of(AtRest(new KitSelectors(null, false, null, agentChangeable: true, offersNeverAsk: true)), VoiceStage.Idle, sentAwaitingStart: false);
        Assert.That(unnamed.Row.Count, Is.EqualTo(2), "no agent to name: no action");
    }

    [TestCase(ApprovalLevel.NeverAsk, "Never Ask")]
    [TestCase(ApprovalLevel.Ask, "Ask First")]
    public void While_the_agent_works_with_no_row_the_plate_reads_the_runs_own_level(ApprovalLevel recorded, string level)
    {
        // The choice for the next start says the other level: the plate reads the run's record.
        var selectors = new KitSelectors(null, false, "Glimmer 30B", agentChangeable: true, offersNeverAsk: true, neverAsk: recorded != ApprovalLevel.NeverAsk);
        var working = RoomBar.Of(AtRest(selectors, recorded, WorkspaceAction.Interrupt), VoiceStage.Idle, sentAwaitingStart: false);
        Assume.That((working.Working, working.Row.Count), Is.EqualTo((true, 0)), "at work, its runtime taking no instruction");
        Assert.That((working.Plate?.Text, working.PlateLevel), Is.EqualTo(("Glimmer 30B", (string?)level)));
        Assert.That(working.FromMoment, Is.False, "the frame stays the task's own");
        var rowed = RoomBar.Of(AtRest(selectors, recorded), VoiceStage.Idle, sentAwaitingStart: false);
        Assert.That(rowed.Plate, Is.Null, "where the row stands there is no plate");
    }
}
