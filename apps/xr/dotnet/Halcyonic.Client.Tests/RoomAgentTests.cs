#nullable enable
using System.Collections.Generic;
using System.Linq;
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

    private static string[] Words(MenuFrame frame) => frame.Lines.Select(line => line.Words).ToArray();

    [Test]
    public void The_page_names_the_agent_and_offers_the_two_levels_then_Change()
    {
        var frame = new Stand(Offer()).Column.Frame!;
        Assert.That(frame.Subject, Is.EqualTo("Model · Glimmer 30B"));
        Assert.That(Words(frame), Is.EqualTo(new[] { "Ask First", "Never Ask", "Change" }));
        Assert.That(frame.Lines.Select(line => line.Action), Is.EqualTo(new[] { AgentColumn.Level, AgentColumn.Level, AgentColumn.Change }));
        Assert.That(frame.Lines.Select(line => line.Key), Is.EqualTo(new[] { "ask", "never_ask", null }));
        Assert.That(frame.Lines.Select(line => line.Chosen), Is.EqualTo(new[] { true, false, false }), "Ask First is the level in force");
        Assert.That(frame.Lines.Take(2).All(line => line.AnswerMark == AnswerMark.Single), "the levels are answers, one of two");
        var back = frame.Footer[PromptSlot.Close]!;
        Assert.That((back.Words, back.Id, back.Kind), Is.EqualTo(("Back", Footer.Close, PromptKind.Close)));
        Assert.That(frame.Footer.All.Count(), Is.EqualTo(1), "nothing but the way back stands under the page");
    }

    [Test]
    public void Never_Ask_chosen_is_the_one_marked_and_an_agent_app_is_named_as_one()
    {
        var frame = new Stand(Offer(neverAsk: true, listsModels: false)).Column.Frame!;
        Assert.That(frame.Subject, Is.EqualTo("Agent App · Glimmer 30B"));
        Assert.That(frame.Lines.Select(line => line.Chosen), Is.EqualTo(new[] { false, true, false }));
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
        Assert.That(frame.Subject, Is.EqualTo("Model · Ember 7B"), "the fact stays the page's subject");
        Assert.That(Words(frame), Is.EqualTo(new[] { "Glimmer 30B", "Ember 7B", "Tide 12B" }));
        Assert.That(frame.Lines.Select(line => (line.Action, line.Key, line.Chosen)),
            Is.EqualTo(new[] { (AgentColumn.Pick, "key-0", false), (AgentColumn.Pick, "key-1", true), (AgentColumn.Pick, "key-2", false) }));
        Assert.That(frame.Lines.All(line => line.AnswerMark == AnswerMark.Single));
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
        Assert.That(bar.Body.Text, Is.EqualTo("Model · Glimmer 30B"));
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
    public void The_action_at_rest_and_the_plate_at_work_name_the_agent_then_its_level()
    {
        Assert.That(RoomBar.AgentWords("Glimmer 30B", false), Is.EqualTo("Glimmer 30B · Ask First"));
        Assert.That(RoomBar.AgentWords("Glimmer 30B", true), Is.EqualTo("Glimmer 30B · Never Ask"));
        Assert.That(RoomBar.AgentWords("Glimmer 30B", null), Is.EqualTo("Glimmer 30B"), "a run whose record carries no level names the agent alone");
        Assert.That(RoomBar.AgentWords(null, true), Is.Null);
        Assert.That(RoomBar.AgentWords("  ", false), Is.Null);
    }
}
