#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// What a new project starts from (the addendum's A16; the Mac's starters, ADR 0037, read at <c>GET /api/starters</c>):
    /// one moment of New Project's page of changes. Start From stands only for a new project in a new folder, and only where
    /// a starter is ready (an action that cannot act is not drawn); its page holds the ready starters as answers with
    /// single marks in the host's order, the one chosen marked, three a page with Next Choices for more; a pick is kept at
    /// once and the page leaves for the facts, whose card shows the starter as a slot under the cube, and the review names
    /// it before its Confirm. Where the person chooses nothing, the default stands as today. The titles are the host's
    /// own starters' (Halcyonic's words, upright).
    /// </summary>
    public static class StarterChoice
    {
        /// <summary>The action on the page of changes, and the page's own line (the lead's yes in A16).</summary>
        public const string StartFrom = "Start From";

        /// <summary>The review's label (its "{label} · {value}" form, as every review item).</summary>
        public const string ReviewLabel = "Start From · ";

        /// <summary>The page of changes' action that opens the choice.</summary>
        public const string Open = "start-from";

        /// <summary>A starter picked, its key the starter's id.</summary>
        public const string Choose = "start-from-choose";

        /// <summary>The choice's next page of starters.</summary>
        public const string NextChoices = "start-from-next";

        /// <summary>Back to the page of changes, nothing chosen.</summary>
        public const string Back = "start-from-back";

        /// <summary>The starters a project can begin from now, in the host's order: installed ones only.</summary>
        public static IReadOnlyList<StarterView> Ready(StartersResponse? starters) =>
            starters?.Starters.Where(starter => starter.Installed).ToList() ?? (IReadOnlyList<StarterView>)Array.Empty<StarterView>();

        /// <summary>Whether Start From stands for <paramref name="idea"/>: a new project in a new folder, with a starter ready.</summary>
        public static bool Offered(ProjectIdea idea, IReadOnlyList<StarterView> ready) =>
            idea.ExistingProjectId == null && idea.Folder is { IsNew: true, PlaceGone: false } && ready.Count > 0;

        /// <summary>How many pages the ready starters fill: the agent's choice's own pager, so every choice of New Project pages alike.</summary>
        public static int Pages(IReadOnlyList<StarterView> ready) => AgentColumn.Pages(ready.Count);

        /// <summary>A starter's title as the page shows it: the host's own words, upright.</summary>
        public static LineWords Title(StarterView starter) => LineWords.Written(LabelText.Plain(starter.Title));

        /// <summary>The chosen folder's starter, by the host's listing; null where none is chosen or the host does not list it.</summary>
        public static StarterView? Chosen(ProjectIdea idea, StartersResponse? starters) =>
            idea.Folder?.StarterId is string id ? starters?.Starters.FirstOrDefault(starter => starter.StarterId == id) : null;

        /// <summary>
        /// The choice (A16's <c>start-from</c>): its one line, Start From, in the panel; the ready starters of page
        /// <paramref name="page"/> as answers, the chosen one marked; Next Choices where more follow; Back to the page of changes.
        /// </summary>
        public static MenuFrame Page(ProjectIdea idea, IReadOnlyList<StarterView> ready, int page, bool startReached)
        {
            var pages = Pages(ready);
            var at = ((page % pages) + pages) % pages;
            var lines = new List<PageLine> { new PageLine(StartFrom) };
            foreach (var starter in ready.Skip(at * AgentColumn.OptionsAPage).Take(AgentColumn.OptionsAPage))
                lines.Add(new PageLine(Title(starter), action: Choose, key: starter.StarterId, choice: true, chosen: idea.Folder?.StarterId == starter.StarterId,
                    answerMark: AnswerMark.Single));
            if (pages > 1)
                lines.Add(new PageLine("Next Choices · " + (at + 1).ToString(CultureInfo.InvariantCulture) + " of " + pages.ToString(CultureInfo.InvariantCulture),
                    icon: GlazeIcon.Next, action: NextChoices));
            var footer = new Footer(new Prompt(Footer.Close, EntryText.Close, GlazeIcon.Close, PromptKind.Close), farRight: new Prompt(NewProjectScreens.Done, EntryText.Back, GlazeIcon.Back));
            return new MenuFrame(NewProjectScreens.Subject(idea), footer, sections: NewProjectScreens.Sections(NewProjectStep.Recap, idea, startReached), lines: lines);
        }

        /// <summary>
        /// The folder with <paramref name="starterId"/> as what it starts from, where the host lists it ready; null where it
        /// cannot be (not a new folder, or a starter not ready).
        /// </summary>
        public static ProjectFolder? Pick(ProjectIdea idea, IReadOnlyList<StarterView> ready, string starterId) =>
            Offered(idea, ready) && ready.Any(starter => starter.StarterId == starterId) ? idea.Folder!.WithStarter(starterId) : null;

        /// <summary>
        /// Whether <paramref name="card"/> shows what the start would send of the starter: the starter the person picked is on
        /// it as a slot. The default, which the person did not pick, needs none (it was never named before Start From). Where a
        /// picked starter has no room on a full card, the one press must not send it unseen: the review names it instead.
        /// </summary>
        public static bool Shown(RoomCard? card, ProjectIdea idea) =>
            idea.Folder?.StarterId is not string id || id == ProjectFolder.DefaultStarterId
            || card?.Slots.Any(slot => slot.Kind == RoomKind.Starter) == true;

        /// <summary>
        /// The recap's card with the starter as a slot after the folder (its title as the value), where the card has room for
        /// one more slot (<see cref="RoomCard.MostSlots"/>): a fact past it would push the level off the card, so a full card
        /// leaves the starter to the review alone. A recap card carries no reading and no code.
        /// </summary>
        public static RoomCard WithStarter(RoomCard card, StarterView? starter)
        {
            if (starter == null || card.Slots.Count >= RoomCard.MostSlots) return card;
            var slots = card.Slots.ToList();
            // After the folder, which it fills: the folder's slot first where there is one.
            var at = slots.FindIndex(slot => slot.Kind == RoomKind.Folder);
            slots.Insert(at < 0 ? 0 : at + 1, new RoomSlot(RoomKind.Starter, Title(starter)));
            return new RoomCard(card.Title, card.Description, card.Head, card.Status, slots, card.Steps, card.TrackAt, card.TrackOf, card.List, card.Gauges, card.More);
        }
    }
}
