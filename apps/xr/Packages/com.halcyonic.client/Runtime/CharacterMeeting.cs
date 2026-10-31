#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>
    /// One task as a meeting sees it: its project, the files whose change the computer observed and attributed to it, its
    /// state, whether its figure stands (not folded or put away) and its home on the floor, in metres (x to the person's
    /// right, z away from them).
    /// </summary>
    public sealed class CharacterMeetingTask
    {
        public CharacterMeetingTask(string id, string projectId, IReadOnlyCollection<string> observedFiles, bool filesAttributed, WorkState state,
            bool present, double homeXM, double homeZM)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            ProjectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
            ObservedFiles = observedFiles ?? throw new ArgumentNullException(nameof(observedFiles));
            FilesAttributed = filesAttributed;
            State = state;
            Present = present;
            HomeXM = homeXM;
            HomeZM = homeZM;
        }

        public string Id { get; }

        public string ProjectId { get; }

        /// <summary>Paths whose change the computer observed for this task; never the agent's reported edits.</summary>
        public IReadOnlyCollection<string> ObservedFiles { get; }

        /// <summary>Whether the computer can tell this task's changes from another's in the same folder; if not, its files link nothing.</summary>
        public bool FilesAttributed { get; }

        public WorkState State { get; }

        public bool Present { get; }

        public double HomeXM { get; }

        public double HomeZM { get; }
    }

    /// <summary>A character's part in a meeting at one moment, for <see cref="CharacterLife.Step"/> and its tag.</summary>
    public readonly struct CharacterMeetingPart
    {
        public CharacterMeetingPart(double xm, double zm, bool facing, double turnDegrees, double leanDegrees, int turn, bool speaking, string? line)
        {
            XM = xm;
            ZM = zm;
            Facing = facing;
            TurnDegrees = turnDegrees;
            LeanDegrees = leanDegrees;
            Turn = turn;
            Speaking = speaking;
            Line = line;
        }

        /// <summary>Its place, from its home, in metres.</summary>
        public double XM { get; }

        public double ZM { get; }

        /// <summary>Both have arrived: turned halfway to each other, leaning in.</summary>
        public bool Facing { get; }

        /// <summary>Its body's turn, toward its partner, positive to the person's right.</summary>
        public double TurnDegrees { get; }

        /// <summary>Leaning in is negative.</summary>
        public double LeanDegrees { get; }

        /// <summary>The turn under way, 0 to 2, or -1 between them.</summary>
        public int Turn { get; }

        public bool Speaking { get; }

        /// <summary>The same true line on both tags while they meet, or null as they part.</summary>
        public string? Line { get; }
    }

    /// <summary>A meeting under way between two characters.</summary>
    public sealed class CharacterMeeting
    {
        internal CharacterMeeting(CharacterMeetingTask first, CharacterMeetingTask second, string line, double startedAt)
        {
            First = first.Id;
            Second = second.Id;
            Line = line;
            StartedAt = startedAt;
            var midX = (first.HomeXM + second.HomeXM) / 2;
            var midZ = (first.HomeZM + second.HomeZM) / 2;
            var dx = second.HomeXM - first.HomeXM;
            var dz = second.HomeZM - first.HomeZM;
            var length = Math.Sqrt(dx * dx + dz * dz);
            if (length < 1e-9) (dx, dz, length) = (1, 0, 1);
            dx /= length;
            dz /= length;
            FirstOffset = (midX - dx * CharacterMeetings.HalfGapM - first.HomeXM, midZ - dz * CharacterMeetings.HalfGapM - first.HomeZM);
            SecondOffset = (midX + dx * CharacterMeetings.HalfGapM - second.HomeXM, midZ + dz * CharacterMeetings.HalfGapM - second.HomeZM);
            FirstMove = CharacterLife.MoveSeconds(Length(FirstOffset));
            SecondMove = CharacterLife.MoveSeconds(Length(SecondOffset));
            ArrivedAt = startedAt + Math.Max(FirstMove, SecondMove);
            PartsAt = ArrivedAt + CharacterMeetings.FirstTurnAfterSeconds + 3 * CharacterMeetings.TurnSeconds + CharacterMeetings.PartAfterSeconds;
            HomeAt = PartsAt + Math.Max(FirstMove, SecondMove);
            FirstSide = dx >= 0 ? 1 : -1;
        }

        public string First { get; }

        public string Second { get; }

        /// <summary>"Same Project" or "Shared · {path}": the link the journal holds, never a dialogue.</summary>
        public string Line { get; }

        public double StartedAt { get; }

        public double ArrivedAt { get; }

        public double PartsAt { get; private set; }

        public double HomeAt { get; private set; }

        internal (double X, double Z) FirstOffset { get; }

        internal (double X, double Z) SecondOffset { get; }

        internal double FirstMove { get; }

        internal double SecondMove { get; }

        internal int FirstSide { get; }

        internal bool Cut { get; private set; }

        internal (double X, double Z) FirstFrom { get; private set; }

        internal (double X, double Z) SecondFrom { get; private set; }

        /// <summary>Breaks the meeting off now: both go home from where they stand, the line goes.</summary>
        internal void CutAt(double now, (double X, double Z) first, (double X, double Z) second)
        {
            Cut = true;
            FirstFrom = first;
            SecondFrom = second;
            PartsAt = now;
            HomeAt = now + Math.Max(CharacterLife.MoveSeconds(Length(first)), CharacterLife.MoveSeconds(Length(second)));
        }

        internal static double Length((double X, double Z) v) => Math.Sqrt(v.X * v.X + v.Z * v.Z);
    }

    /// <summary>
    /// The room's meetings (the art direction's character handoff, 2026-10-30, section 4): two characters meet only over a
    /// link the journal holds as observed for each task, the same project or an observed change of the same file attributed
    /// to each; never while either needs the person, while the computer is unreachable, or under Keep Still; one at a time in
    /// the room; a pair at most once every <see cref="PairEverySeconds"/>. Both drift to points one body width apart, turn
    /// halfway to each other and lean in, take three turns, then go home. Nothing joins them but the same line on both tags.
    /// </summary>
    public sealed class CharacterMeetings
    {
        public const double PairEverySeconds = 120, HalfGapM = 0.14, TurnSeconds = 1.5, FirstTurnAfterSeconds = 0.35, PartAfterSeconds = 0.5;
        public const double FaceDegrees = 45, LeanInDegrees = 4;

        public const string SameProject = "Same Project";
        public const string SharedPrefix = "Shared · ";

        private readonly Dictionary<(string, string), double> lastMet = new Dictionary<(string, string), double>();

        public CharacterMeeting? Current { get; private set; }

        /// <summary>The link between two tasks the journal holds as observed, as both tags say it, or null.</summary>
        public static string? LinkOf(CharacterMeetingTask a, CharacterMeetingTask b)
        {
            if (a.FilesAttributed && b.FilesAttributed)
            {
                var shared = a.ObservedFiles.Where(b.ObservedFiles.Contains).OrderBy(path => path, StringComparer.Ordinal).FirstOrDefault();
                if (shared != null) return SharedPrefix + shared;
            }
            return a.ProjectId == b.ProjectId ? SameProject : null;
        }

        /// <summary>Ends a meeting whose time is up or whose pair may no longer meet, and starts one where a pair may.</summary>
        public void Update(IReadOnlyList<CharacterMeetingTask> tasks, bool controlPlaneReachable, double now, bool keepStill = false)
        {
            if (Current is CharacterMeeting meeting)
            {
                if (now >= meeting.HomeAt) Current = null;
                else if (!meeting.Cut)
                {
                    var first = tasks.FirstOrDefault(t => t.Id == meeting.First);
                    var second = tasks.FirstOrDefault(t => t.Id == meeting.Second);
                    if (!controlPlaneReachable || keepStill || first == null || second == null || !MayMeet(first) || !MayMeet(second))
                        meeting.CutAt(now, Offset(meeting, true, now), Offset(meeting, false, now));
                }
                if (Current != null) return;
            }
            if (!controlPlaneReachable || keepStill) return;
            (CharacterMeetingTask A, CharacterMeetingTask B, string Line, double Last)? best = null;
            for (var i = 0; i < tasks.Count; i++)
            {
                if (!MayMeet(tasks[i])) continue;
                for (var j = i + 1; j < tasks.Count; j++)
                {
                    if (!MayMeet(tasks[j]) || LinkOf(tasks[i], tasks[j]) is not string line) continue;
                    var last = lastMet.TryGetValue(Pair(tasks[i].Id, tasks[j].Id), out var at) ? at : double.NegativeInfinity;
                    if (now - last < PairEverySeconds) continue;
                    if (best == null || last < best.Value.Last) best = (tasks[i], tasks[j], line, last);
                }
            }
            if (best is { } chosen)
            {
                Current = new CharacterMeeting(chosen.A, chosen.B, chosen.Line, now);
                lastMet[Pair(chosen.A.Id, chosen.B.Id)] = now;
            }
        }

        /// <summary>The part of the character <paramref name="id"/> in the meeting at <paramref name="now"/>, or null if it is in none.</summary>
        public CharacterMeetingPart? PartOf(string id, double now)
        {
            if (Current is not CharacterMeeting m || now >= m.HomeAt) return null;
            var first = id == m.First;
            if (!first && id != m.Second) return null;
            var (x, z) = Offset(m, first, now);
            var facing = !m.Cut && now >= m.ArrivedAt && now < m.PartsAt;
            var turn = -1;
            if (facing && now >= m.ArrivedAt + FirstTurnAfterSeconds)
            {
                var index = (int)Math.Floor((now - m.ArrivedAt - FirstTurnAfterSeconds) / TurnSeconds);
                if (index < 3) turn = index;
            }
            var side = first ? m.FirstSide : -m.FirstSide;
            return new CharacterMeetingPart(x, z, facing, facing ? FaceDegrees * side : 0, facing ? -LeanInDegrees : 0, turn,
                turn >= 0 && (turn % 2 == 0) == first, !m.Cut && now < m.PartsAt ? m.Line : null);
        }

        private static (double X, double Z) Offset(CharacterMeeting m, bool first, double now)
        {
            var target = first ? m.FirstOffset : m.SecondOffset;
            var move = first ? m.FirstMove : m.SecondMove;
            if (m.Cut)
            {
                var from = first ? m.FirstFrom : m.SecondFrom;
                return Toward(from, (0, 0), (now - m.PartsAt) / CharacterLife.MoveSeconds(CharacterMeeting.Length(from)));
            }
            if (now < m.PartsAt) return Toward((0, 0), target, (now - m.StartedAt) / move);
            return Toward(target, (0, 0), (now - m.PartsAt) / move);
        }

        private static (double X, double Z) Toward((double X, double Z) from, (double X, double Z) to, double u)
        {
            var e = CharacterLife.EaseInOut(u < 0 ? 0 : u > 1 ? 1 : u);
            return (from.X + (to.X - from.X) * e, from.Z + (to.Z - from.Z) * e);
        }

        private static bool MayMeet(CharacterMeetingTask task) => task.Present && task.State != WorkState.WaitingForYou;

        private static (string, string) Pair(string a, string b) => string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
    }
}
