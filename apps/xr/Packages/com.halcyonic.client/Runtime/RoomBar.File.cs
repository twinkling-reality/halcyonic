#nullable enable
using System.Collections.Generic;

namespace Halcyonic.Client
{
    public sealed partial class RoomBar
    {
        /// <summary>A Changes row's own control: a press reads that file (its key the path, as the folder names it).</summary>
        public static KitControl FileRow(MenuFrame frame, RoomChange change) =>
            new KitControl(KitPlace.Step, frame, RoomResult.ShowFile, change.Path, change.Path, GlazeIcon.Next, true, null, false);

        /// <summary>
        /// A file of Changes read (the result's File page, SCREENS's <see cref="FileChangeColumn"/>): its card with FIELD's page
        /// of reading, or the sweep's sentence while a read is out; and its column, the main action first, Back last, each a
        /// result action the director takes.
        /// </summary>
        private static (RoomCard? Card, string? Sentence) FileReading(FileChangeColumn reading, MenuFrame frame, List<RoomAction> column)
        {
            foreach (var action in reading.Actions())
            {
                var (id, icon) = action.Kind switch
                {
                    FileChangeActionKind.NextPage => (RoomResult.FileNextPage, GlazeIcon.Next),
                    FileChangeActionKind.PreviousPage => (RoomResult.FilePreviousPage, GlazeIcon.Back),
                    FileChangeActionKind.WholeFile => (RoomResult.FileWhole, GlazeIcon.Next),
                    FileChangeActionKind.TryAgain => (RoomResult.FileTryAgain, GlazeIcon.Next),
                    _ => (RoomResult.FileBack, GlazeIcon.Back),
                };
                column.Add(new RoomAction(new KitControl(KitPlace.Step, frame, id, null, action.Words, icon, true, null, false), action.Main, icon: icon));
            }
            return (reading.Card(), reading.Sentence);
        }
    }
}
