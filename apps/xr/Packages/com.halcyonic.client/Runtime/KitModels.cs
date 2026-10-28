#nullable enable
using System.Collections.Generic;

namespace Halcyonic.Client
{
    /// <summary>What a kit row shows and where it goes. A row takes the person somewhere; it never acts.</summary>
    public sealed class KitRowModel
    {
        public KitRowModel(string key, LineWords words, GlazeIcon? icon = null, GlazeTone iconTone = GlazeTone.Neutral,
            string? detail = null, GlazeTone? detailTone = null, string? end = null, GlazeTone? endTone = null,
            bool opensMore = true, bool selected = false, int wordLines = 2, bool isName = false)
        {
            Key = key;
            IsName = isName;
            Words = words;
            Icon = icon;
            IconTone = iconTone;
            Detail = detail;
            DetailTone = detailTone;
            End = end;
            EndTone = endTone;
            OpensMore = opensMore;
            Selected = selected;
            WordLines = wordLines;
        }

        /// <summary>What the row is, for its owner: a task's or a project's id, or a place.</summary>
        public string Key { get; }

        /// <summary>Its words: a task's title, a project's name, or Halcyonic's name for a place.</summary>
        public LineWords Words { get; }

        public GlazeIcon? Icon { get; }

        /// <summary>The icon's tone: a state's, which its detail also says in words.</summary>
        public GlazeTone IconTone { get; }

        /// <summary>A second line in Halcyonic's words, as a task's state.</summary>
        public string? Detail { get; }

        public GlazeTone? DetailTone { get; }

        /// <summary>Words at its right end saying where pressing it goes, as Review request; else a chevron when it opens more.</summary>
        public string? End { get; }

        public GlazeTone? EndTone { get; }

        public bool OpensMore { get; }

        /// <summary>The row is the chosen one: lit, and already showing.</summary>
        public bool Selected { get; }

        public int WordLines { get; }

        /// <summary>
        /// The words are one name from outside, a task's or a project's: it stands upright through the named-identity
        /// treatment, literal and with its outside range kept. Anything else from outside keeps its lean.
        /// </summary>
        public bool IsName { get; }
    }

    /// <summary>A group of the spine's rows under an optional heading: Halcyonic's name for a place, or a project's own name.</summary>
    public sealed class KitSpineSection
    {
        public KitSpineSection(IReadOnlyList<KitRowModel> rows, string? heading = null, bool headingIsName = false)
        {
            Rows = rows;
            Heading = heading;
            HeadingIsName = headingIsName;
        }

        public IReadOnlyList<KitRowModel> Rows { get; }

        public string? Heading { get; }

        /// <summary>The heading is a project's name from outside, shown upright as a named identity, literal.</summary>
        public bool HeadingIsName { get; }
    }

    /// <summary>One line of the evidence rail: a journal-derived activity entry, the icon its event's kind shows and the tone its recorded outcome takes.</summary>
    public sealed class KitTrailEntry
    {
        public KitTrailEntry(ActivityEntry entry, GlazeIcon icon, GlazeTone tone = GlazeTone.Neutral)
        {
            Entry = entry;
            Icon = icon;
            Tone = tone;
        }

        public ActivityEntry Entry { get; }

        public GlazeIcon Icon { get; }

        /// <summary>The recorded outcome's tone on the entry's icon; its words stay in the text colour.</summary>
        public GlazeTone Tone { get; }
    }

    /// <summary>What a notice says: Halcyonic's words, the icon of the state they are about, and that state's tone.</summary>
    public sealed class KitNoticeModel
    {
        public KitNoticeModel(GlazeIcon icon, GlazeTone tone, LineWords words)
        {
            Icon = icon;
            Tone = tone;
            Words = words;
        }

        public GlazeIcon Icon { get; }

        public GlazeTone Tone { get; }

        /// <summary>Halcyonic's own sentence, as ConnectionText or WorkspaceText words it; an agent's words never stand here.</summary>
        public LineWords Words { get; }
    }

    /// <summary>
    /// An approval the work waits on, in production's words (<see cref="WorkspaceText.NeedFromYou"/>): what it asks, the
    /// request whole as the runtime reported it, where an approved command runs as its agent app declares it, and what
    /// each answer does; and once an answer is pressed, its confirmation.
    /// </summary>
    public sealed class KitNeed
    {
        public KitNeed(LineWords asks, string? request, string? scopeNote, IReadOnlyList<string> notes, string denyWords, string approveWords,
            bool denyOffered, bool approveOffered, KitConfirmation? confirming = null)
        {
            Asks = asks;
            Request = request;
            ScopeNote = scopeNote;
            Notes = notes;
            DenyWords = denyWords;
            ApproveWords = approveWords;
            DenyOffered = denyOffered;
            ApproveOffered = approveOffered;
            Confirming = confirming;
        }

        /// <summary>Halcyonic's sentence of what it asks, a tool's name leaning inside it.</summary>
        public LineWords Asks { get; }

        /// <summary>The request, literal and whole, or null where it named no tool.</summary>
        public string? Request { get; }

        /// <summary>Where an approved command runs, ADR 0033's words, or null where its agent app declares nothing.</summary>
        public string? ScopeNote { get; }

        /// <summary>What each answer does and when it counts, in Halcyonic's words, the scope note not among them.</summary>
        public IReadOnlyList<string> Notes { get; }

        public string DenyWords { get; }

        public string ApproveWords { get; }

        /// <summary>Production offers Deny now (<see cref="WorkspacePresentation.Actions"/>); where it doesn't, none is drawn.</summary>
        public bool DenyOffered { get; }

        /// <summary>Production offers Approve now; not for a request it can't show whole, which can only be denied (<see cref="WorkspaceText.OnlyDeny"/>).</summary>
        public bool ApproveOffered { get; }

        /// <summary>The confirmation of the answer pressed, or null while both answers are offered.</summary>
        public KitConfirmation? Confirming { get; }
    }

    /// <summary>The confirmation of an approval's answer: the question about the request above, Yes's words, and whether all of it has been read.</summary>
    public sealed class KitConfirmation
    {
        public KitConfirmation(bool approving, string ask, string confirm, bool read, string readFirst)
        {
            Approving = approving;
            Ask = ask;
            Confirm = confirm;
            Read = read;
            ReadFirst = readFirst;
        }

        public bool Approving { get; }

        public string Ask { get; }

        public string Confirm { get; }

        public bool Read { get; }

        public string ReadFirst { get; }
    }

    /// <summary>The question the work waits on the person to answer: production's heading over the agent's own words.</summary>
    public sealed class KitAsk
    {
        public KitAsk(LineWords heading, string question)
        {
            Heading = heading;
            Question = question;
        }

        /// <summary>Halcyonic's heading, such as It asks you, or the agent's own header leaning.</summary>
        public LineWords Heading { get; }

        /// <summary>The question as the agent wrote it: outside text.</summary>
        public string Question { get; }
    }

    /// <summary>The task's place: its character, or why none stands there and the one real way back.</summary>
    public sealed class KitAvatar
    {
        private KitAvatar(CharacterPresentation? present, string? why, string? wayBack, GlazeIcon? wayBackIcon)
        {
            Present = present;
            Why = why;
            WayBack = wayBack;
            WayBackIcon = wayBackIcon;
        }

        public static KitAvatar Standing(CharacterPresentation presentation) => new KitAvatar(presentation, null, null, null);

        /// <summary>No character: <paramref name="why"/> in Halcyonic's words, with the existing action that brings it back, if any.</summary>
        public static KitAvatar Absent(string why, string? wayBack = null, GlazeIcon? icon = null) => new KitAvatar(null, why, wayBack, icon);

        public CharacterPresentation? Present { get; }

        public string? Why { get; }

        public string? WayBack { get; }

        public GlazeIcon? WayBackIcon { get; }
    }

    /// <summary>What the focused work shows: the selected task, what its agent last said, what was recorded, and the person's draft.</summary>
    public sealed class KitWorkModel
    {
        public KitWorkModel(string projectName, string taskTitle, StateBadge badge, IReadOnlyList<WorkMark> marks,
            ActivityEntry? report, IReadOnlyList<KitTrailEntry> trail, string draft, bool voiceOffered, bool sendable,
            string? whyNotSend, IReadOnlyList<KitSpineSection> spine, KitRowModel? spineFoot,
            KitNeed? need = null, KitAsk? ask = null, KitNoticeModel? notice = null, KitAvatar? avatar = null, IReadOnlyList<PageLine>? said = null)
        {
            Said = said ?? new PageLine[0];
            Avatar = avatar;
            Need = need;
            Ask = ask;
            Notice = notice;
            ProjectName = projectName;
            TaskTitle = taskTitle;
            Badge = badge;
            Marks = marks;
            Report = report;
            Trail = trail;
            Draft = draft;
            VoiceOffered = voiceOffered;
            Sendable = sendable;
            WhyNotSend = whyNotSend;
            Spine = spine;
            SpineFoot = spineFoot;
        }

        public string ProjectName { get; }

        public string TaskTitle { get; }

        public StateBadge Badge { get; }

        /// <summary>Practice, Demo or Recorded, said beside the state where it applies.</summary>
        public IReadOnlyList<WorkMark> Marks { get; }

        /// <summary>The agent's latest message, a reported claim; none where it has said nothing yet.</summary>
        public ActivityEntry? Report { get; }

        /// <summary>The recorded events, oldest first; agent messages are read in the slate instead.</summary>
        public IReadOnlyList<KitTrailEntry> Trail { get; }

        public string Draft { get; }

        public bool VoiceOffered { get; }

        public bool Sendable { get; }

        public string? WhyNotSend { get; }

        public IReadOnlyList<KitSpineSection> Spine { get; }

        public KitRowModel? SpineFoot { get; }

        /// <summary>An approval it waits on: the slate reads the request and the decision bar takes the composer's place.</summary>
        public KitNeed? Need { get; }

        /// <summary>A question it waits on the person to answer: the composer asks for the answer.</summary>
        public KitAsk? Ask { get; }

        /// <summary>What its state alone can't say about what happened, as an outcome that can't be told: shown over the activity it points to.</summary>
        public KitNoticeModel? Notice { get; }

        /// <summary>The task's place on the slate's top edge, or null where the kit shows none.</summary>
        public KitAvatar? Avatar { get; }

        /// <summary>
        /// What the file says of the work beside its state, in its own lines and tones: an attention note, that an approval
        /// waits on Waiting, why nothing can be sent. Shown on the slate under the state.
        /// </summary>
        public IReadOnlyList<PageLine> Said { get; }
    }

    /// <summary>One project on the overview: its own name, a sentence of its work, and its tasks as rows.</summary>
    public sealed class KitProjectModel
    {
        public KitProjectModel(string key, string name, string summary, IReadOnlyList<KitRowModel> tasks)
        {
            Key = key;
            Name = name;
            Summary = summary;
            Tasks = tasks;
        }

        public string Key { get; }

        public string Name { get; }

        /// <summary>The project's work in a sentence a person would say, as "1 task waiting for you, 1 running".</summary>
        public string Summary { get; }

        public IReadOnlyList<KitRowModel> Tasks { get; }
    }

    /// <summary>What the overview shows: every project's work, led by what needs the person.</summary>
    public sealed class KitOverviewModel
    {
        public KitOverviewModel(string title, string lead, IReadOnlyList<KitProjectModel> projects, IReadOnlyList<KitSpineSection> spine, KitRowModel? spineFoot)
        {
            Title = title;
            Lead = lead;
            Projects = projects;
            Spine = spine;
            SpineFoot = spineFoot;
        }

        public string Title { get; }

        /// <summary>The whole workspace's attention in one or two sentences, what waits for the person first.</summary>
        public string Lead { get; }

        public IReadOnlyList<KitProjectModel> Projects { get; }

        public IReadOnlyList<KitSpineSection> Spine { get; }

        public KitRowModel? SpineFoot { get; }
    }

    /// <summary>What the compact presence keeps in view: the retained task, its state, and what waits anywhere in the workspace.</summary>
    public sealed class KitPresenceModel
    {
        public KitPresenceModel(string taskTitle, StateBadge badge, int waiting)
        {
            TaskTitle = taskTitle;
            Badge = badge;
            Waiting = waiting;
        }

        public string TaskTitle { get; }

        public StateBadge Badge { get; }

        /// <summary>How many tasks wait for the person across the workspace, as known now.</summary>
        public int Waiting { get; }

        /// <summary>The count said in full, as there is room for it (WORDS.md): a count keeps its noun and its verb.</summary>
        public string WaitingWords => Waiting == 0 ? "Nothing is waiting for you."
            : Waiting == 1 ? "1 task is waiting for you." : Waiting + " tasks are waiting for you.";
    }
}
