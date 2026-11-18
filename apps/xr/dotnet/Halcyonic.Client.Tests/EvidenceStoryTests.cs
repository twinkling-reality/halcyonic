using System.Linq;
using Halcyonic.Contracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Halcyonic.Client.Tests;

/// <summary>
/// The evidence view as a story anyone can follow (the owner's 2026-11-18 direction), from the demonstration's recorded answers in the
/// reads' own shape: plain heads, the source's own sentences, our plain answer to whether it worked from its check runs, and nothing
/// technical on the face.
/// </summary>
public class EvidenceStoryTests
{
    private const string Run = "01a0ec64-6fac-77a9-827e-bdcf356af403";
    private const string Asked = "Add cursor pagination to GET /orders, 50 orders per page, with tests.";

    private static UnderstandingResponse Recorded(int at) =>
        HalcyonicJson.Deserialize<UnderstandingResponse>(JObject.Parse(Demonstration.Text())["understanding"]![Run]![at]!["response"]!.ToString());

    [Test]
    public void TheStoryTellsWhatWasAskedWrongWhyDoneWhetherItWorkedAndWhatIsLeft()
    {
        var story = EvidenceStory.Of(Asked, Recorded(7));
        Assert.That(story.Places.Select(place => place.Head), Is.EqualTo(new[] { "What You Asked", "What It Did", "Why", "How It Works", "Did the Checks Pass?", "What's Left" }));
        var places = story.Places.ToDictionary(place => place.Kind);
        Assert.That(places[StoryPlaceKind.Wrong].Answer.Text, Is.EqualTo("GET /orders sent every order at once, so long histories loaded slowly."));
        Assert.That(places[StoryPlaceKind.Why].Steps.Select(step => step.Text), Is.EqualTo(new[] { "Every order in one response", "Slow pages for long histories" }));
        Assert.That(places[StoryPlaceKind.Did].Steps.Select(step => step.Text), Is.EqualTo(new[] { "Sort by creation time", "Read 50 after the cursor", "Return the next cursor" }));
        Assert.That(places[StoryPlaceKind.Did].Note, Is.EqualTo("It changed 3 files."));
        Assert.That((places[StoryPlaceKind.Worked].Answer.Text, places[StoryPlaceKind.Worked].Note, places[StoryPlaceKind.Worked].From),
            Is.EqualTo(("Yes", (string?)"31 tests passed.", (string?)"Practice run, not real")),
            "the demonstration is a stand-in, and says so in plain words");
        Assert.That(places[StoryPlaceKind.Left].Answer.Text, Is.EqualTo("Document the cursor parameter in the API reference"));
        Assert.That(story.Places.All(place => place.Teach.Length > 0), "every place teaches its idea in a line");
        var face = story.Places.SelectMany(place => new[] { place.Answer.Text, place.Note ?? "", place.From ?? "" }.Concat(place.Steps.Select(step => step.Text))).ToList();
        Assert.That(face, Has.None.Contain("make test").And.None.Contain("src/").And.None.Contain("Observed").And.None.Contain("Inferred"), "nothing technical on the face");
        var everything = face.Concat(story.Places.Select(place => place.Head)).Concat(story.Places.Select(place => place.Teach)).ToList();
        Assert.That(everything, Has.None.Contain("Salidium").And.None.Contain("Seorak"), "no brand on the face (the film's rule 182)");

        // A real (not stand-in) source says how each answer is known, in plain words.
        var real = Recorded(7);
        ((AvailableUnderstanding)real.Result).Understanding.Source.Synthetic = false;
        var known = EvidenceStory.Of(Asked, real).Places.ToDictionary(place => place.Kind, place => place.From);
        Assert.That((known[StoryPlaceKind.Worked], known[StoryPlaceKind.Why]), Is.EqualTo(((string?)"Seen happen", (string?)"Summary written afterwards")));
    }

    [Test]
    public void DidItWorkIsOnlyWhatTheChecksSaid()
    {
        var response = Recorded(7);
        var run = ((AvailableUnderstanding)response.Result).Understanding.Verification.LatestByMethod.Single();
        run.Outcome = UnderstandingVerificationRunOutcome.Fail;
        run.Counts = new UnderstandingVerificationRunCounts { Passed = 23, Failed = 1 };
        var failed = EvidenceStory.Of(Asked, response).Places.Single(place => place.Kind == StoryPlaceKind.Worked);
        Assert.That((failed.Answer.Text, failed.Note), Is.EqualTo(("No", (string?)"23 tests passed, 1 failed.")));
        run.Stale = true;
        Assert.That(EvidenceStory.Of(Asked, response).Places.Single(place => place.Kind == StoryPlaceKind.Worked).Answer.Text, Is.EqualTo("Not Checked Yet"),
            "a check from before the last change says nothing of the change");
        var early = EvidenceStory.Of(Asked, Recorded(3)).Places.Single(place => place.Kind == StoryPlaceKind.Worked);
        Assert.That((early.Answer.Text, early.From), Is.EqualTo(("Not Checked Yet", (string?)null)));
    }

    [Test]
    public void TheAgentsPlainSentenceAnswersWhereItGaveOneSaidAsTheAgents()
    {
        var closing = new EvidenceStory.ClosingWords("The orders page was slow because it loaded every order at once.", null, "It now loads 50 orders at a time.", null);
        var places = EvidenceStory.Of(Asked, Recorded(7), closing).Places.ToDictionary(place => place.Kind);
        Assert.That((places[StoryPlaceKind.Wrong].Answer.Text, places[StoryPlaceKind.Wrong].From, places[StoryPlaceKind.Wrong].Source?.Text),
            Is.EqualTo(("The orders page was slow because it loaded every order at once.", (string?)"The AI Says", (string?)"GET /orders sent every order at once, so long histories loaded slowly.")),
            "the agent's sentence answers, said as the agent's, the source's own kept one press in");
        Assert.That(places[StoryPlaceKind.Why].From, Is.Not.EqualTo("The AI Says"), "a place the agent left blank stays the source's");
        Assert.That(places[StoryPlaceKind.Did].Steps.Count, Is.EqualTo(3), "the source's steps stay");
        Assert.That((places[StoryPlaceKind.Asked].Answer.Text, places[StoryPlaceKind.Worked].Answer.Text), Is.EqualTo((Asked, "Yes")),
            "never the ask, never the check");
    }

    [Test]
    public void WithoutAnAnswerTheAskStandsAlone()
    {
        var story = EvidenceStory.Of(Asked, null);
        Assert.That(story.Places.Select(place => place.Kind), Is.EqualTo(new[] { StoryPlaceKind.Asked }));
        Assert.That(story.Without?.State, Is.EqualTo("No Conclusions"));
    }
}
