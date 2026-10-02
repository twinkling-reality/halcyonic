#nullable enable

namespace Halcyonic.Client
{
    /// <summary>
    /// The Usage left glance as a panel model (ADR 0023): its title, where the readings come from
    /// under it, and Close in the header; each limit window's name with a meter at its right and, under
    /// it, what was seen, in words; that some limits couldn't be read and that no account is
    /// identified, under the list, so every page says it; and Refresh on the bar. A meter is a picture
    /// of the words under it, drawn from the same rounded-up share, its end open as "at most" is.
    /// While a read is in flight the rows read before stay, their meters show only their tracks, and
    /// Refresh waits.
    /// </summary>
    public static class UsageLeftScreens
    {
        /// <summary>What Refresh raises: read the limits again.</summary>
        public const string Refresh = "refresh";

        /// <param name="presentation">What was read, or why nothing shows.</param>
        /// <param name="reading">A read is in flight.</param>
        /// <param name="canRead">There is somewhere to read from: not while the recorded demonstration plays, so no Refresh then.</param>
        public static PanelModel Screen(UsageLeftPresentation presentation, bool reading, bool canRead)
        {
            var model = new PanelModel(UsageLeftPresenter.Title) { Movable = false, Context = presentation.Source };
            if (presentation.Rows.Count == 0)
            {
                model.Rows.Add(new PanelRow
                {
                    Line = true,
                    Title = presentation.Note,
                    TitleLines = 2,
                    Tone = presentation.Failed ? GlazeTone.Failure : (GlazeTone?)null,
                });
            }
            else
            {
                foreach (var row in presentation.Rows)
                {
                    model.Rows.Add(new PanelRow
                    {
                        Line = true,
                        Title = row.Title,
                        TitleIsData = true,
                        Size = PanelTextSize.Caption,
                        Meter = row.Left / 100f,
                        MeterWaiting = reading,
                    });
                    model.Rows.Add(new PanelRow { Line = true, Title = row.Text, TitleLines = 2, Continues = true });
                }
                model.PartsNote = presentation.Note;
                // With no rows the list's own line says it; with rows, the bar says they are being read again.
                if (reading) model.BarNote = UsageLeftPresenter.Reading;
            }
            if (canRead) model.Actions = new ActionSet(new PanelAction(Refresh, WorkspaceText.Refresh, PanelActionRole.Secondary, available: !reading, icon: GlazeIcon.Refresh));
            return model;
        }
    }
}
