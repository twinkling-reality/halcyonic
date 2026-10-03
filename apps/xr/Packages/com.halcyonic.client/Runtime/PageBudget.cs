#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>
    /// What a page holds and what each kind of line on it takes, so a screen packs a page before the
    /// view lays it (ADR 0026): lines of words, rows and answers to press, and the gaps between them,
    /// against the room a page has for its lines, its source line already taken. On the headset a page
    /// packs by height (<see cref="HeightBudget"/>, from <see cref="MenuPage"/>); counted in rows
    /// (<see cref="RowBudget"/>), every line takes its rows and no gap anything.
    /// </summary>
    public abstract class PageBudget
    {
        /// <summary>The room a page has for its lines, its reason included, its source line already taken.</summary>
        public abstract float Room { get; }

        /// <summary>A line of words <paramref name="rows"/> rows tall.</summary>
        public abstract float Words(int rows);

        /// <summary>A row or an answer to press, its words in <paramref name="rows"/> rows.</summary>
        public abstract float Target(int rows = 1);

        /// <summary>Between two targets.</summary>
        public abstract float TargetGap { get; }

        /// <summary>
        /// Within a group, from a line to the next where both are not targets, as from a question to its
        /// first answer (8 dp): the view draws this, and the page counts it.
        /// </summary>
        public abstract float LineGap { get; }

        /// <summary>Between groups, as before a page's reason.</summary>
        public abstract float GroupGap { get; }

        /// <summary>The reason a prompt waits, a line of words after a group's gap.</summary>
        public float Reason => GroupGap + Words(1);

        /// <summary>
        /// Targets one after another: each one's own height and the gaps between them, none before the
        /// first and none after the last.
        /// </summary>
        public float Targets(params int[] rows)
        {
            var total = 0f;
            for (var index = 0; index < rows.Length; index++) total += Target(rows[index]) + (index > 0 ? TargetGap : 0f);
            return total;
        }

        /// <summary>The most rows of words that fit in <paramref name="room"/>, at least one.</summary>
        public int WordsIn(float room)
        {
            var rows = 1;
            while (Words(rows + 1) <= room + 1e-6f) rows++;
            return rows;
        }
    }

    /// <summary>A page counted in rows: every line takes its rows, a target too, and no gap anything.</summary>
    public sealed class RowBudget : PageBudget
    {
        /// <param name="rows">The rows a page holds for its lines, its source line already taken.</param>
        public RowBudget(int rows) => Rows = Math.Max(1, rows);

        public int Rows { get; }

        public override float Room => Rows;

        public override float Words(int rows) => Math.Max(1, rows);

        public override float Target(int rows = 1) => Math.Max(1, rows);

        public override float TargetGap => 0f;

        public override float LineGap => 0f;

        public override float GroupGap => 0f;
    }

    /// <summary>
    /// A page packed by height (<see cref="MenuPage"/>): a line of words a line's height, a target
    /// 48 dp or taller, 12 mm between targets, 8 dp between other lines of a group and a group's gap
    /// before the reason, against the most a
    /// lone file's page holds inside a Quest 3S's field.
    /// </summary>
    public sealed class HeightBudget : PageBudget
    {
        /// <param name="page">The page's room for its lines, as <see cref="MenuPage.Height"/> gives it, its source line taken here.</param>
        public HeightBudget(float page) => Room = Math.Max(0f, page - MenuPage.SourceLine);

        /// <summary>A lone file's page at <paramref name="text"/>'s size, its subject in <paramref name="subjectRows"/> rows.</summary>
        public static HeightBudget Of(TextSize text, int subjectRows) => new HeightBudget(MenuPage.Height(text, subjectRows));

        public override float Room { get; }

        public override float Words(int rows) => MenuPage.Words(Math.Max(1, rows));

        public override float Target(int rows = 1) => MenuPage.Target(Math.Max(1, rows));

        public override float TargetGap => MenuPage.TargetGap;

        public override float LineGap => MenuPage.Grid;

        public override float GroupGap => MenuPage.GroupGap;
    }
}
