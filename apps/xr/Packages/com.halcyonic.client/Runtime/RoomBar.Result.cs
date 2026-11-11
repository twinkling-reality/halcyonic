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
            if (loop.Pair || loop.State is RoomBarState.Sent or RoomBarState.Words || !result.Any && fixedLine == null) return loop;
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
            IReadOnlyList<LineWords>? lines = null;
            switch (result.Page)
            {
                case RoomResultPage.Changes:
                    facts = result.Shown;
                    if (result.Part + 1 < result.Parts) column.Add(Own(RoomResult.NextPage, Footer.NextPageWords(result.Part, result.Parts), main: true, GlazeIcon.Next));
                    column.Add(Own(RoomResult.Back, EntryText.Back, main: false, GlazeIcon.Back));
                    break;
                case RoomResultPage.Checks:
                    facts = result.Shown;
                    if (result.CanTry) column.Add(Own(RoomResult.TryBuild, KitScreenModel.Words(KitScreenAction.TryIt), main: true, GlazeIcon.OpenNow));
                    else if (result.Passed == false && instruct != null) column.Add(new RoomAction(instruct, main: true, WorkspaceText.Label(WorkspaceAction.Instruct)));
                    column.Add(Own(RoomResult.Back, EntryText.Back, main: false, GlazeIcon.Back));
                    break;
                default:
                    if (result.CountLine is string count) body = LineWords.Ours(count);
                    sentence = result.CheckLine;
                    if (fixedLine != null)
                    {
                        // The chain's line first, each on a line of its own; the check stays in the sentence bar.
                        var counted = body;
                        body = counted.IsEmpty ? LineWords.Ours(fixedLine) : LineWords.Ours(fixedLine + KitMomentWords.Dot) + counted;
                        if (!counted.IsEmpty) lines = new[] { LineWords.Ours(fixedLine), counted };
                    }
                    // With a check and no count, the check is the panel's one line.
                    else if (body.IsEmpty && sentence != null) (body, sentence) = (LineWords.Ours(sentence), null);
                    if (ranOut && instruct != null) column.Add(new RoomAction(instruct, main: true, WorkspaceText.Label(WorkspaceAction.Instruct)));
                    if (result.CanMakeVr) column.Add(Own(RoomResult.MakeVrVersion, KitScreenModel.Words(KitScreenAction.MakeAVrVersion), main: column.Count == 0, GlazeIcon.Next));
                    else if (result.IsVr && instruct != null && !ranOut) column.Add(new RoomAction(instruct, main: true, KitScreenModel.Words(KitScreenAction.ChangeIt)));
                    if (result.CanTry) column.Add(Own(RoomResult.TryBuild, KitScreenModel.Words(KitScreenAction.TryIt), main: column.Count == 0, GlazeIcon.OpenNow));
                    // DEVICE's Play in Headset beside Try Build, where it stands. Where it does not, it is not drawn: an action that
                    // cannot act has no disabled look (BUILD-SPEC-v4 1.4); where its refusal's words stand is FIELD's to say.
                    if (result.Play is PlayInHeadset play && HeadsetPlay.Stands(play))
                        column.Add(Own(RoomResult.PlayInHeadset, HeadsetPlay.ActionWords, main: column.Count == 0, GlazeIcon.OpenNow));
                    if (result.Observed != null) column.Add(Own(RoomResult.ShowChanges, RoomResult.ChangesWords, main: false, GlazeIcon.Next));
                    if (result.Steps.Count > 0) column.Add(Own(RoomResult.ShowChecks, RoomResult.ChecksWords, main: false, GlazeIcon.Next));
                    band = binding.Model.Report?.Text;
                    break;
            }
            if (facts != null)
            {
                // The rows' words, joined, are the body's: a view that draws no rows still shows every word, each on its own line.
                foreach (var row in facts) body = body.IsEmpty ? row.Words : body + LineWords.Ours(KitMomentWords.Dot) + row.Words;
            }
            return new RoomBar(loop.State, row: loop.Row, body: body, sentence: sentence, column: column, band: band, wide: body.IsEmpty,
                lines: facts != null && facts.Count > 1 ? facts.Select(row => row.Words).ToList() : lines)
            {
                Result = true,
                Facts = facts,
            };
        }
    }
}
