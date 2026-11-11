#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>
        /// The result at rest over what the loop made of the frame (DESIGN's BUILD-SPEC-v4 2.5 to 2.8). It stands only with
        /// the row or its place (at rest, talking, or nothing to send), never over a draft, a confirmation or Sent.
        ///
        /// The result: how many files the computer saw change in the panel, where it observed the turn; the computer's
        /// own check in the sentence bar, where it ran one; the agent's newest message on the band. Its actions: Make VR
        /// Version, the main one, for a project that is not VR yet, else the file's own way to an instruction as
        /// Request Change; Try Build where the product lets it stand; Changes and Checks where the computer has either.
        /// Changes: a row a file, eight a page, Next Page the main action while more follow, and Back. Checks: a row a
        /// step the check reached, with its mark, then Try Build where it may stand, or the file's own way to an
        /// instruction where the check failed (Write Instruction), and Back. With nothing known of the task the loop's own view stands unchanged.
        /// Where a chain of tries at a failed check ended (FIELD's A7), its line is the panel's first, over the result's own
        /// ("Fixed · Try 2 of 2", "Stopped After 2 Tries"), and where the tries ran out the main action is the file's own way to
        /// an instruction, as on a failed check's page. "Fixed" stands only while the result has the computer's check.
        /// </summary>
        private static RoomBar Resulting(KitFileBinding binding, RoomResult result, RoomBar loop)
        {
            var fixedLine = FixedWords(binding.Model.AutoFix);
            // "Fixed" is the computer's check speaking for these files: it stands only while the result has that check, so
            // only while the folder is as it was checked. That the tries ran out stays true whatever the folder did (AUDIT, 2026-11-11).
            if (binding.Model.AutoFix?.Reason == Halcyonic.Contracts.AutoFixEndReason.Passed && result.Passed != true) fixedLine = null;
            var ranOut = fixedLine != null && binding.Model.AutoFix!.Reason == Halcyonic.Contracts.AutoFixEndReason.CapReached;
            if (loop.Pair || loop.State is RoomBarState.Sent or RoomBarState.Words || !result.Any && fixedLine == null && binding.Model.Standing == null) return loop;
            var frame = binding.Frame;
            // The view draws each action's glyph by its words (RoomGlyphs); the icon is the nearest of the product's own.
            RoomAction Own(string action, string words, bool main, GlazeIcon icon) =>
                new RoomAction(new KitControl(KitPlace.Step, frame, action, null, words, icon, true, null, false), main, icon: icon);
            var instruct = binding[KitPlace.Field] is { Available: true } field ? field : null;
            var column = new List<RoomAction>();
            LineWords body = LineWords.None;
            string? sentence = null;
            string? band = null;
            IReadOnlyList<RoomRow>? facts = null;
            RoomCard? card = null;
            switch (result.Page)
            {
                case RoomResultPage.Changes:
                    // FIELD's Changes card (A17): each file its kind's mark, its path, a quiet note; five a page (RoomCard.ListAPage).
                    var changes = RoomPanelCards.Changes(result, frame.SubjectRuns, result.ReadsFiles ? change => FileRow(frame, change) : null);
                    card = changes.List.Count <= RoomCard.ListAPage ? changes
                        : new RoomCard(changes.Title, changes.Description, changes.Head, changes.Status, changes.Slots, changes.Steps, changes.TrackAt, changes.TrackOf,
                            changes.List.Skip(result.Part * RoomCard.ListAPage).Take(RoomCard.ListAPage).ToList(), changes.Gauges, changes.More);
                    if (result.Part + 1 < result.Parts) column.Add(Own(RoomResult.NextPage, Footer.NextPageWords(result.Part, result.Parts), main: true, GlazeIcon.Next));
                    // DEVICE's History, Changes' second action (lead, 2026-11-11): the versions the computer kept, and Restore.
                    column.Add(Own(RoomResult.ShowHistory, HistoryText.Title, main: false, GlazeIcon.Next));
                    // The result's other page one press away, as tabs are at a desk: every page of a result is reached by a drawn control.
                    if (result.Steps.Count > 0) column.Add(Own(RoomResult.ShowChecks, RoomResult.ChecksWords, main: false, GlazeIcon.Next));
                    column.Add(Own(RoomResult.Back, EntryText.Back, main: false, GlazeIcon.Back));
                    break;
                case RoomResultPage.Add:
                    // What adding brings, as FIELD's review card (the task as its title, where its work goes, the files as its
                    // list); its confirming action names the act, Add to Project, the one press (the lead's R1, 2026-11-12: no
                    // forced paging, no lock; the add's digest still guards that what is added is the reading these files came
                    // from); and Back. Nothing to confirm where adding no longer stands, or where the host did not list every file.
                    // Nothing on the headset can undo an add yet (Restore is not offered), so the review says nothing of undoing.
                    // Five files a page; Next Page reads on, never in the way of the add. Undo comes back with History.
                    if (result.Add is { State: RoomAddState.Ready } adding)
                    {
                        var project = binding.Model.ProjectName.Length > 0 ? LineWords.Ours("Into ") + LineWords.TheirName(binding.Model.ProjectName) + LineWords.Ours(".") : (LineWords?)null;
                        card = new RoomCard(frame.SubjectRuns, project,
                            list: adding.Paths.Skip(result.Part * RoomCard.ListAPage).Take(RoomCard.ListAPage)
                                .Select(path => new RoomListRow(RoomKind.ToAdd, LineWords.Theirs(path), mono: true)).ToList());
                        // The add carries the digest of the reading these files came from, taken with them (TASKS's call note):
                        // without one there is no add to offer (nothing left, no reading yet, or a reading from before).
                        if (adding.Paths.Count == adding.Count && result.AddReview is string review)
                            column.Add(new RoomAction(new KitControl(KitPlace.Step, frame, RoomResult.AddConfirm, review, adding.ConfirmWords, GlazeIcon.Next, true, null, false),
                                main: true, icon: GlazeIcon.Next));
                        if (result.Part + 1 < result.Parts) column.Add(Own(RoomResult.NextPage, Footer.NextPageWords(result.Part, result.Parts), main: column.Count == 0, GlazeIcon.Next));
                    }
                    column.Add(Own(RoomResult.Back, EntryText.Back, main: false, GlazeIcon.Back));
                    break;
                case RoomResultPage.Conflict:
                    // Where the project changed the same files: FIELD's card naming them, then Let Agent Fix, which writes both
                    // sides marked into the task's copy and asks its agent to fit them, and Back.
                    if (result.Add?.ConflictCard is RoomCard both)
                    {
                        card = both;
                        column.Add(Own(RoomResult.Fit, RoomAdd.LetAgentFix, main: true, GlazeIcon.Next));
                    }
                    column.Add(Own(RoomResult.Back, EntryText.Back, main: false, GlazeIcon.Back));
                    break;
                case RoomResultPage.Share when result.Share is ShareColumn share:
                    // DEVICE's Share (ShareColumn, FIELD's share parts in A17's cards): the card, each way a row picked from by
                    // its own control (key: the way), the column as the column gives it, Share behind its lock. The code to scan
                    // is FIELD's RoomCard.Code once it lands (TODO); the host can draw ShareCodeView from share.Cells till then.
                    var shareCard = share.Card();
                    card = shareCard == null || shareCard.List.Count == 0 ? shareCard
                        : new RoomCard(shareCard.Title, shareCard.Description, shareCard.Head, shareCard.Status, shareCard.Slots, shareCard.Steps, shareCard.TrackAt,
                            shareCard.TrackOf, shareCard.List.Zip(share.Ways(), (row, way) => new RoomListRow(row.Kind, row.Name, row.Note, row.Tone, row.Mono,
                                new KitControl(KitPlace.Step, frame, RoomResult.ShareWayAction, way.ToString(), row.Name.Text, GlazeIcon.Next, true, null, false))).ToList(),
                            shareCard.Gauges, shareCard.More);
                    foreach (var action in share.Actions())
                    {
                        var (id, icon) = action.Kind switch
                        {
                            ShareActionKind.Share => (RoomResult.ShareStart, GlazeIcon.Next),
                            ShareActionKind.Cancel => (RoomResult.ShareCancel, GlazeIcon.Back),
                            ShareActionKind.Stop => (RoomResult.ShareStop, GlazeIcon.Next),
                            ShareActionKind.TryAgain or ShareActionKind.ShareAgain => (RoomResult.ShareAgain, GlazeIcon.Next),
                            ShareActionKind.CheckGame => (RoomResult.ShareCheckGame, GlazeIcon.Next),
                            ShareActionKind.StartPreview => (RoomResult.ShareStartPreview, GlazeIcon.Next),
                            ShareActionKind.Back => (RoomResult.ShareBack, GlazeIcon.Back),
                            ShareActionKind.Close => (RoomResult.ShareClose, GlazeIcon.Back),
                            _ => ((string?)null, GlazeIcon.Next),
                        };
                        // A way is its row's press, not a column action.
                        if (id == null) continue;
                        var control = new KitControl(KitPlace.Step, frame, id, null, action.Words, icon, true, null, false);
                        column.Add(new RoomAction(control, action.Main, locks: action.Kind == ShareActionKind.Share, icon: icon));
                    }
                    break;
                case RoomResultPage.History when result.History is HistoryColumn history:
                    // DEVICE's History (HistoryColumn, FIELD's A14 in A17's cards): the card where there is one, else the
                    // sentence that sweeps while it reads or restores; each version row picked from by its own control; the
                    // column's actions as the column gives them, Restore behind its lock.
                    card = history.Card(version => new KitControl(KitPlace.Step, frame, RoomResult.HistoryVersion, version.View.CheckpointId,
                        version.Name.Text, GlazeIcon.Next, true, null, false));
                    sentence = history.Sentence;
                    foreach (var action in history.Actions())
                    {
                        var (id, icon) = action.Kind switch
                        {
                            HistoryActionKind.Older => (RoomResult.HistoryOlder, GlazeIcon.Next),
                            HistoryActionKind.Restore => (RoomResult.HistoryRestore, GlazeIcon.Next),
                            HistoryActionKind.Cancel => (RoomResult.HistoryCancel, GlazeIcon.Back),
                            HistoryActionKind.UndoRestore => (RoomResult.HistoryUndo, GlazeIcon.Next),
                            HistoryActionKind.ReviewAgain => (RoomResult.HistoryAgain, GlazeIcon.Next),
                            HistoryActionKind.TryAgain => (RoomResult.HistoryTryAgain, GlazeIcon.Next),
                            HistoryActionKind.Changes => (RoomResult.HistoryChanges, GlazeIcon.Next),
                            HistoryActionKind.Back => (RoomResult.HistoryBack, GlazeIcon.Back),
                            HistoryActionKind.Close => (RoomResult.HistoryClose, GlazeIcon.Back),
                            _ => ((string?)null, GlazeIcon.Next),
                        };
                        // A version is its row's press, not a column action.
                        if (id == null) continue;
                        var control = new KitControl(KitPlace.Step, frame, id, action.CheckpointId, action.Words, icon, true, null, false);
                        column.Add(new RoomAction(control, action.Main, locks: action.Kind == HistoryActionKind.Restore, icon: icon));
                    }
                    break;
                case RoomResultPage.File when result.File is FileChangeColumn reading:
                    // SCREENS's file of Changes read: its change, then the whole file, paged, with a way back at each step.
                    (card, sentence) = FileReading(reading, frame, column);
                    break;
                case RoomResultPage.Checks:
                    // FIELD's Checks card: the outcome as its status, the steps as nodes, the tries' pips on the failed one.
                    card = RoomPanelCards.Checks(result, binding.Model.AutoFix);
                    if (result.CanTry) column.Add(Own(RoomResult.TryBuild, KitScreenModel.Words(KitScreenAction.TryIt), main: true, GlazeIcon.OpenNow));
                    else if (result.Passed == false && instruct != null) column.Add(new RoomAction(instruct, main: true, WorkspaceText.Label(WorkspaceAction.Instruct)));
                    column.Add(Own(RoomResult.ShowChanges, RoomResult.ChangesWords, main: false, GlazeIcon.Next));
                    column.Add(Own(RoomResult.Back, EntryText.Back, main: false, GlazeIcon.Back));
                    break;
                default:
                    // FIELD's result card (A17): the task's name as its title, the check and the files changed (or waiting to be
                    // added) as slots, an ended chain of tries as its status first. No sentence: the check is a slot now.
                    // Where the result says nothing, how the run stands (B).
                    card = RoomPanelCards.Result(result, frame.SubjectRuns, binding.Model.AutoFix, result.Add?.Slot) ?? binding.Model.Standing;
                    // Kept, said (the lead's R1): once the host recorded the add and nothing newer waits, the card's status says
                    // so, with no line, since nothing on the headset can undo it yet; a chain of tries that ended keeps its status.
                    if (result.Add?.State == RoomAddState.Added) card = Said(card, frame.SubjectRuns, new RoomCardStatus(RoomAdd.AddedWords, null, RoomTone.Good));
                    if (ranOut && instruct != null) column.Add(new RoomAction(instruct, main: true, WorkspaceText.Label(WorkspaceAction.Instruct)));
                    // SCREENS's Carry Out Plan leads a plan at rest (FIELD's words; the glyph FIELD's).
                    if (result.Start is { CanCarryOut: true } && column.Count == 0) column.Add(Own(RoomResult.CarryOutPlan, RunStart.CarryOutPlan, main: true, GlazeIcon.Next));
                    // A13: the task's work to add to its project leads where there is some; where the project changed the same
                    // files, Let Agent Fix leads to FIELD's card naming them.
                    if (result.Add?.State == RoomAddState.Ready) column.Add(Own(RoomResult.ShowAdd, RoomAdd.AddToProject, main: column.Count == 0, GlazeIcon.Next));
                    else if (result.Add?.State == RoomAddState.Conflict) column.Add(Own(RoomResult.ShowConflict, RoomAdd.LetAgentFix, main: column.Count == 0, GlazeIcon.Next));
                    if (result.CanMakeVr) column.Add(Own(RoomResult.MakeVrVersion, KitScreenModel.Words(KitScreenAction.MakeAVrVersion), main: column.Count == 0, GlazeIcon.Next));
                    else if (result.IsVr && instruct != null && !ranOut) column.Add(new RoomAction(instruct, main: column.Count == 0, KitScreenModel.Words(KitScreenAction.ChangeIt)));
                    if (result.CanTry) column.Add(Own(RoomResult.TryBuild, KitScreenModel.Words(KitScreenAction.TryIt), main: column.Count == 0, GlazeIcon.OpenNow));
                    // DEVICE's Play in Headset beside Try Build, where it stands. Where it does not, it is not drawn: an action that
                    // cannot act has no disabled look (BUILD-SPEC-v4 1.4); where its refusal's words stand is FIELD's to say.
                    // The project's places from its one list (LINK's ProjectPlaces): Play in Headset where the list has it, by its kind, never its label.
                    foreach (var place in result.Places)
                    {
                        if (place.Kind == ProjectPlaceKind.PlayInHeadset)
                            column.Add(Own(RoomResult.PlayInHeadset, place.Label, main: column.Count == 0, GlazeIcon.OpenNow));
                    }
                    // DEVICE's Share, where the host would admit it or a share stands (FIELD: an action of a passed result at rest).
                    if (result.CanShare) column.Add(Own(RoomResult.ShowShare, ShareText.Share, main: column.Count == 0, GlazeIcon.Next));
                    // The one Changes (the lead, 11:15Z): always offered at rest, its page saying Nothing Changed where none did and the plain
                    // not-seen words where the computer could not say, as a desk always has its Changes (JUDGE's live-17: unreached otherwise).
                    column.Add(Own(RoomResult.ShowChanges, RoomResult.ChangesWords, main: false, GlazeIcon.Next));
                    if (result.Steps.Count > 0) column.Add(Own(RoomResult.ShowChecks, RoomResult.ChecksWords, main: false, GlazeIcon.Next));
                    // The agent's words stand on the band beside its result, never beside how a run stands (B): nothing extra.
                    // Beside how a run stands, only that turn's own words, on the band, read by parts and never cut (the card stays whole).
                    band = card != null && ReferenceEquals(card, binding.Model.Standing)
                        ? KitPresenter.TurnReportOf(binding.Model.Report, binding.Model.Trail)?.Text
                        : binding.Model.Report?.Text;
                    break;
            }
            if (facts != null)
            {
                // The rows' words, joined, are the body's: a view that draws no rows still shows every word, each on its own line.
                foreach (var row in facts) body = body.IsEmpty ? row.Words : body + LineWords.Ours(KitMomentWords.Dot) + row.Words;
            }
            return new RoomBar(loop.State, row: loop.Row, body: body, sentence: sentence, column: column, band: band, wide: body.IsEmpty && card == null,
                lines: facts != null && facts.Count > 1 ? facts.Select(row => row.Words).ToList() : null)
            {
                Result = true,
                Facts = facts,
                Known = result,
                Card = card,
                Ended = fixedLine,
            };
        }

        /// <summary><paramref name="card"/> with <paramref name="status"/> where it has none, or a card of the task's name and that status alone.</summary>
        private static RoomCard Said(RoomCard? card, LineWords task, RoomCardStatus status) => card == null
            ? new RoomCard(task, head: RoomCardHead.Picture, status: status)
            : card.Status != null ? card
            : new RoomCard(card.Title, card.Description, card.Head, status, card.Slots, card.Steps, card.TrackAt, card.TrackOf, card.List, card.Gauges, card.More);
    }
}
