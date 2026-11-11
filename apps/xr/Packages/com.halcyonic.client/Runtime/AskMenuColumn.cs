#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// Ask beside the menu (gap 7; ADR 0030): the <see cref="AskColumn"/> as a column the director stands beside the menu,
    /// from a task's page, a project's or the gallery (XR's lines). Its frame is the Ask's card (<see cref="AskColumn.Card"/>)
    /// with the prompts the person presses: Send while there are words to send; Send Answer while the assistant asks the
    /// person something; Stop while the assistant works or waits; Hold to Talk and Type for the words; and Close. The
    /// assistant's words stand on the band only, as the frame's one claim line (<see cref="AskColumn.Band"/>), never in the
    /// card. It sends only on the person's press, through the host, and reads the request it sent, every two seconds while it
    /// is under way. It never draws.
    /// </summary>
    public sealed class AskMenuColumn : IMenuColumn
    {
        /// <summary>The column's subject, which its reader in the redesign knows it by.</summary>
        public const string Subject = "Ask";

        public const string Send = "ask-send";
        public const string Answer = "ask-answer";
        public const string Stop = "ask-stop";
        public const string Type = "ask-type";
        public const string Talk = "ask-talk";

        /// <summary>The column's own words, for FIELD to settle with the card's (<see cref="AskColumn.Words"/>).</summary>
        public static class Words
        {
            public const string Send = "Send";
            public const string Answer = "Send Answer";
            public const string Stop = "Stop";
            public const string TypeQuestion = "Type a Question";
            public const string TypeAnswer = "Type an Answer";
            public const string Close = "Close";
            public const string QuestionPrompt = "Ask the assistant";
            public const string AnswerPrompt = "Answer the assistant";
        }

        /// <summary>How often the request is read while it is under way.</summary>
        public const double ReadEverySeconds = 2;

        private readonly IMenuHost host;
        private readonly CommandFactory commands;
        private Task<CommandAckMessage>? acknowledgement;
        private Task<WorkspaceAgentStatus>? offer;
        private Task<WorkspaceRequestResponse>? reading;
        private bool offerAsked, talking;
        private double nextRead = double.NegativeInfinity;
        private string answer = "";

        public AskMenuColumn(IMenuHost host, CommandFactory commands, AskScope scope)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Ask = new AskColumn(commands, scope);
            Ask.Changed += () => Changed?.Invoke();
        }

        /// <summary>The Ask this column shows.</summary>
        public AskColumn Ask { get; }

        /// <summary>The person's answer to the assistant's question, written or heard, while it asks; empty otherwise.</summary>
        public string AnswerDraft => Ask.Stage == AskStage.NeedsYou ? answer : "";

        public event Action? Changed;
        public event Action? Closed;

        private bool Stoppable =>
            Ask.RequestId != null && Ask.Stage is AskStage.Thinking or AskStage.Waiting or AskStage.NeedsYou;

        private bool WordsOpen => Ask.Stage is AskStage.Writing or AskStage.NeedsYou;

        public MenuFrame? Frame
        {
            get
            {
                var close = new Prompt(Footer.Close, Words.Close, GlazeIcon.Close, PromptKind.Close);
                var stop = Stoppable ? new Prompt(Stop, Words.Stop, GlazeIcon.Stop) : null;
                var talk = WordsOpen && host.VoiceOffered ? new Prompt(Talk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true) : null;
                Prompt? main = null;
                // Send and Send Answer stand only once there are words to send: an action that cannot act is not drawn.
                if (Ask.CanSend)
                    main = new Prompt(Send, Words.Send, GlazeIcon.TellIt, main: true);
                else if (Ask.Stage == AskStage.NeedsYou && answer.Trim().Length > 0)
                    main = new Prompt(Answer, Words.Answer, GlazeIcon.SendAnswer, main: true);
                var lines = new List<PageLine>();
                // The assistant's words, its question or its reply: the band's, as a claim, never the panel's.
                if (Ask.Band is LineWords said) lines.Add(new PageLine(LabelText.Plain(said.Text), wordsAreData: true, claim: true));
                if (Ask.Stage == AskStage.NeedsYou && answer.Trim().Length > 0) lines.Add(new PageLine(LineWords.Theirs(answer.Trim())));
                if (WordsOpen && host.KeyboardOffered)
                    lines.Add(new PageLine(Ask.Stage == AskStage.NeedsYou ? Words.TypeAnswer : Words.TypeQuestion, icon: GlazeIcon.Type, action: Type));
                return new MenuFrame(Subject, new Footer(close, rare: stop, secondary: talk, farRight: main), lines: lines).WithCard(Ask.Card);
            }
        }

        public void Act(string id, string? key)
        {
            switch (id)
            {
                case Send when Ask.CanSend:
                {
                    var command = Ask.Submit();
                    if (command != null) acknowledgement = host.Submit(command);
                    return;
                }
                case Answer when Ask.Stage == AskStage.NeedsYou && Ask.RequestId != null && answer.Trim().Length > 0:
                    host.Submit(commands.ResumeRequest(Ask.RequestId, answer.Trim()));
                    answer = "";
                    nextRead = double.NegativeInfinity;
                    Changed?.Invoke();
                    return;
                case Stop when Stoppable:
                    host.Submit(commands.CancelRequest(Ask.RequestId!));
                    nextRead = double.NegativeInfinity;
                    return;
                case Type when WordsOpen && host.KeyboardOffered:
                {
                    var answering = Ask.Stage == AskStage.NeedsYou;
                    host.OpenKeyboard(LabelText.Plain(answering ? answer : Ask.Draft),
                        answering ? Words.AnswerPrompt : Words.QuestionPrompt, text => Take(text, answering));
                    return;
                }
                case Footer.Close:
                    Closed?.Invoke();
                    return;
            }
        }

        /// <summary>Words typed or heard: the question while writing, the answer while the assistant asks; dropped once the Ask moved on.</summary>
        private void Take(string text, bool answering)
        {
            if (answering != (Ask.Stage == AskStage.NeedsYou)) return;
            if (answering)
            {
                answer = text;
                Changed?.Invoke();
            }
            else Ask.Write(text);
        }

        public void HoldStarted(string id) => talking = id == Talk && WordsOpen;

        public void HoldEnded(string id, bool letGo)
        {
            if (id == Talk && !letGo) talking = false;
        }

        public void Heard(string text)
        {
            if (!talking) return;
            talking = false;
            Take(text, Ask.Stage == AskStage.NeedsYou);
        }

        public void Said(string words) { }

        public void Tick()
        {
            var api = host.Api;
            if (!offerAsked && api != null)
            {
                offerAsked = true;
                offer = api.GetWorkspaceAgentAsync();
            }
            if (offer is { IsCompleted: true } offered)
            {
                offer = null;
                if (offered.Status == TaskStatus.RanToCompletion) Ask.Offer(offered.Result);
            }
            if (acknowledgement is { IsCompleted: true } acked)
            {
                acknowledgement = null;
                Ask.Acknowledge(acked.Status == TaskStatus.RanToCompletion ? acked.Result : null);
            }
            if (reading is { IsCompleted: true } read)
            {
                reading = null;
                if (read.Status == TaskStatus.RanToCompletion) Ask.Read(read.Result.Request);
            }
            if (reading == null && api != null && Ask.RequestId is string requestId && Stoppable && host.Now >= nextRead)
            {
                nextRead = host.Now + ReadEverySeconds;
                reading = api.ReadWorkspaceRequestAsync(requestId);
            }
        }

        public void Drawn(MenuFrame drawn, Footer? sidePanel) { }
        public void FocusLeft() => talking = false;
    }
}
