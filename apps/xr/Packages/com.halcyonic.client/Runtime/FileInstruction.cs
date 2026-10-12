#nullable enable
using System;

namespace Halcyonic.Client
{
    /// <summary>A local instruction draft and its readable editor. No completion or navigation sends it.</summary>
    public sealed class FileInstruction
    {
        public bool Open { get; private set; }
        public string? ExecutionId { get; private set; }
        public string Words { get; private set; } = "";
        public bool Spoken { get; private set; }
        public bool PairedPager { get; private set; }
        public bool InputFocused { get; private set; }
        public int Epoch { get; private set; }
        public int Part { get; private set; }
        public int Parts { get; private set; } = 1;
        public int PartRows { get; private set; } = 1;
        private int readThrough = -1;
        private DateTimeOffset? partDrawnAt;
        private DateTimeOffset? noticeDrawnAt;
        public string? Notice { get; private set; }
        public bool InlineNotice { get; private set; } = true;
        public bool ExplainingNotice { get; private set; }
        public int NoticeRows { get; private set; }
        public int NoticePart { get; private set; }
        public int NoticeParts { get; private set; } = 1;
        public int NoticePartRows { get; private set; } = 1;
        private (string Words, int Rows, float Room, TextSize Size, bool Paired, string? Notice, int NoticeRows)? measured;
        public bool Read => Words.Length > 0 && readThrough >= Parts - 1 && !InputFocused;

        public void Begin(string execution)
        {
            if (ExecutionId != execution) { Words = ""; Spoken = false; measured = null; Part = 0; readThrough = -1; }
            ExecutionId = execution;
            Open = true;
            Suspend();
        }
        public void Edit()
        {
            Epoch++;
            InputFocused = true;
            readThrough = -1;
            partDrawnAt = null;
            Part = 0;
        }
        public void Changed(string words, bool spoken)
        {
            Words = words.Trim();
            // Once this draft contains a transcription, keyboard edits cannot remove its confirmation protection.
            Spoken |= spoken;
            InputFocused = false;
            Epoch++;
            measured = null;
            Part = 0;
            readThrough = -1;
            partDrawnAt = null;
        }
        public void Measure(int rows, PageBudget page, TextSize size, bool pairedPager, string? notice = null, int noticeRows = 0)
        {
            if (ExplainingNotice && notice == null) { notice = Notice; noticeRows = NoticeRows; }
            var key = (Words, rows, page.Room, size, pairedPager, notice, noticeRows);
            if (measured == key) return;
            var noticeReflowed = measured == null || measured.Value.Notice != notice || measured.Value.NoticeRows != noticeRows || measured.Value.Room != page.Room || measured.Value.Size != size;
            measured = key;
            PairedPager = pairedPager;
            var fullRoom = page.Room - page.Words(1) - page.LineGap - page.Reason;
            var pager = pairedPager ? page.Target() : page.Targets(1, 1);
            var noticeHeight = notice == null ? 0f : page.LineGap + page.Words(Math.Max(1, noticeRows));
            var fieldRoom = fullRoom - noticeHeight;
            var pagedRoom = fieldRoom - pager - page.TargetGap;
            InlineNotice = notice == null || (page.Target(rows) <= fieldRoom + 1e-6f || pagedRoom >= page.Target() - 1e-6f);
            if (!InlineNotice)
            {
                fieldRoom = fullRoom;
                ExplainingNotice = true;
                if (noticeReflowed) { NoticePart = 0; noticeDrawnAt = null; }
            }
            if (InlineNotice) ExplainingNotice = false;
            Notice = notice;
            NoticeRows = noticeRows;
            NoticePartRows = page.WordsIn(page.Room - page.Words(1) - page.LineGap);
            NoticeParts = Math.Max(1, (Math.Max(1, noticeRows) + NoticePartRows - 1) / NoticePartRows);
            NoticePart = Math.Min(NoticePart, NoticeParts - 1);
            PartRows = Math.Max(1, rows);
            if (page.Target(PartRows) > fieldRoom + 1e-6f)
            {
                var room = fieldRoom - pager - page.TargetGap;
                PartRows = 1;
                while (page.Target(PartRows + 1) <= room + 1e-6f) PartRows++;
            }
            Parts = Math.Max(1, (Math.Max(1, rows) + PartRows - 1) / PartRows);
            // New wrapping invalidates the ordered read prefix, so start where that proof can be rebuilt.
            Part = 0;
            readThrough = -1;
            partDrawnAt = null;
        }
        public void Drawn(DateTimeOffset now)
        {
            partDrawnAt ??= now;
            if (!InputFocused && Part <= readThrough + 1) readThrough = Math.Max(readThrough, Part);
        }
        private bool MayTurn(DateTimeOffset now) => partDrawnAt != null && now - partDrawnAt >= FileScreen.TurnGuard;
        public void Next(DateTimeOffset now) { if (MayTurn(now) && Part <= readThrough && Part + 1 < Parts) { Part++; partDrawnAt = null; } }
        public void Previous(DateTimeOffset now) { if (MayTurn(now) && Part > 0) { Part--; partDrawnAt = null; } }
        public void NoticeDrawn(DateTimeOffset now) => noticeDrawnAt ??= now;
        public void NextNotice(DateTimeOffset now)
        {
            if (noticeDrawnAt == null || now - noticeDrawnAt < FileScreen.TurnGuard) return;
            NoticePart = (NoticePart + 1) % NoticeParts;
            noticeDrawnAt = null;
        }
        public void BackFromNotice() { ExplainingNotice = false; Notice = null; noticeDrawnAt = null; }
        public void Suspend() { Epoch++; InputFocused = false; partDrawnAt = null; noticeDrawnAt = null; }
        public void Close()
        {
            Suspend();
            Open = false;
            Notice = null;
            ExplainingNotice = false;
            InlineNotice = true;
            NoticePart = 0;
            ExecutionId = null;
            Words = "";
            Spoken = false;
            Part = 0;
            Parts = 1;
            measured = null;
            readThrough = -1;
            partDrawnAt = null;
        }
    }
}
