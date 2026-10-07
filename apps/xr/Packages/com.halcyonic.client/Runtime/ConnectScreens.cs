#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The screens of Connect a folder, reached from Connect projects, as <see cref="PanelModel"/>s
    /// (ADR 0023): the folders on the person's computer that no project uses, each with what the
    /// computer saw of it, then one folder with what connecting does, Connect, and how it went. Nothing
    /// here sends anything; the entry panel sends the one <c>project.create</c> on Connect's press.
    /// </summary>
    public static class ConnectScreens
    {
        // What each action raises, for the entry panel to act on.
        public const string OpenFolders = "connect-folder";
        public const string ChooseFolder = "connect-choose-folder";
        public const string ReadAgain = "connect-read-again";
        public const string Connect = "connect-confirm";
        public const string TryAgain = "connect-try-again";
        public const string ChooseAnother = "connect-choose-another";
        public const string AddTask = "connect-add-task";
        public const string Done = "connect-done";
        public const string Back = "connect-back";

        /// <summary>The folders no project uses, the latest changed first, or why there are none to show.</summary>
        /// <param name="listing">The host's listing, or null while it is read.</param>
        /// <param name="problem">Why it could not be read, in Halcyonic's own words (<see cref="EntryText.WhyFoldersUnread"/>), shown under <see cref="EntryText.FoldersUnread"/>, or null while reading.</param>
        /// <param name="live">Connected to a control plane, outside the demonstration: only then can anything be connected.</param>
        public static PanelModel Folders(LocationsResponse? listing, string? problem, bool live, DateTimeOffset now, TimeZoneInfo zone)
        {
            var model = new PanelModel(ConnectText.ConnectFolder) { Lead = ConnectText.FoldersLine, Columns = 2 };
            var back = new PanelAction(Back, EntryText.Back, PanelActionRole.Back, icon: GlazeIcon.Back);
            var readAgain = new PanelAction(ReadAgain, EntryText.TryAgain, PanelActionRole.Primary, icon: GlazeIcon.Refresh);
            if (!live)
            {
                model.Rows.Add(Line(ConnectText.NotConnectedYet));
                model.Actions = new ActionSet(back);
                return model;
            }
            if (listing == null)
            {
                if (problem != null) model.Rows.Add(Line(EntryText.FoldersUnread));
                model.Rows.Add(Line(problem ?? EntryText.ReadingFolders));
                model.Actions = new ActionSet(back, problem == null ? null : readAgain);
                return model;
            }
            if (listing.Roots.Count == 0)
            {
                model.Rows.Add(Line(EntryText.NoFolders));
                model.Actions = new ActionSet(back, readAgain);
                return model;
            }
            var offers = FolderConnect.Offers(listing);
            if (FolderConnect.AnyCut(listing)) model.Lead += " " + EntryText.FoldersCut;
            if (offers.Count == 0)
            {
                model.Rows.Add(Line(ConnectText.NoFreeFolders));
                model.Actions = new ActionSet(back);
                return model;
            }
            var manyPlaces = offers.Select(offer => offer.Root.Path).Distinct(StringComparer.Ordinal).Count() > 1;
            foreach (var offer in offers)
            {
                model.Rows.Add(new PanelRow
                {
                    Title = offer.Name,
                    TitleIsData = true,
                    Detail = ConnectText.Detail(offer, now, zone, manyPlaces),
                    DetailLines = 2,
                    Action = ChooseFolder,
                    Key = offer.Key,
                });
            }
            model.Actions = new ActionSet(back);
            return model;
        }

        /// <summary>
        /// One folder: its name as the title, what the computer saw of it and what connecting does, and
        /// Connect; once sent, how it went, with Add a task once it is a project.
        /// </summary>
        /// <param name="connection">The connection sent for this folder, or null before Connect is pressed.</param>
        /// <param name="waitingOn">Another folder whose connection may still be on its way, which holds Connect back.</param>
        public static PanelModel Folder(ConnectableFolder folder, FolderConnection? connection, bool live, DateTimeOffset now, TimeZoneInfo zone,
            ConnectableFolder? waitingOn = null)
        {
            var model = new PanelModel(folder.Name) { TitleIsData = true, Lead = ConnectText.Facts(folder, now, zone) };
            // What connecting will do, until it was sent; then how it went takes its place.
            if (connection == null) model.Rows.Add(Line(ConnectText.WhatConnectingDoes(folder)));
            if (folder.LooksLikeAnother) model.Rows.Add(Line(ConnectText.CheckWhichOne));
            var back = new PanelAction(Back, EntryText.Back, PanelActionRole.Back, icon: GlazeIcon.Back);
            if (connection == null)
            {
                var reason = !live ? ConnectText.NotConnectedYet : waitingOn != null ? ConnectText.WaitingOn(waitingOn) : null;
                model.Actions = new ActionSet(back, new PanelAction(Connect, ConnectText.Connect, PanelActionRole.Primary, reason == null,
                    reason, icon: GlazeIcon.ConnectProjects));
                return model;
            }
            var outcome = ConnectText.Outcome(connection);
            if (connection.Connected)
            {
                model.Rows.Add(Line(outcome, tone: GlazeTone.Success));
                model.Actions = new ActionSet(
                    new PanelAction(Done, EntryText.Done, PanelActionRole.Secondary),
                    new PanelAction(AddTask, EntryText.AddTask, PanelActionRole.Primary, live, live ? null : ConnectText.NotConnectedYet, icon: GlazeIcon.AddTask));
                return model;
            }
            var stopped = connection.Stopped && connection.Unresolved == null;
            model.Rows.Add(Line(outcome, tone: stopped ? GlazeTone.Failure : (GlazeTone?)null));
            if (connection.CanRetry)
            {
                // A refusal about the folder leads to another folder; one that never left, to trying again.
                var aboutFolder = connection.Step.Status != BuildStepStatus.NotSent;
                model.Actions = new ActionSet(back, aboutFolder
                    ? new PanelAction(ChooseAnother, EntryText.ChooseAnotherFolder, PanelActionRole.Primary)
                    : new PanelAction(TryAgain, EntryText.TryAgain, PanelActionRole.Primary, live, live ? null : ConnectText.NotConnectedYet, icon: GlazeIcon.Refresh));
                return model;
            }
            // Sent and not settled, or settled in a way that may have run: nothing to press but leave.
            model.Actions = new ActionSet(new PanelAction(Done, EntryText.Done, PanelActionRole.Primary));
            return model;
        }

        private static PanelRow Line(string text, int lines = 3, GlazeTone? tone = null) =>
            new PanelRow { Line = true, Title = text, TitleLines = lines, Tone = tone };
    }
}
