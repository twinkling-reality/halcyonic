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
        public const string TryAgain = "ask-try-again";
        public const string SendAgain = "ask-send-again";

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
            /// <summary>A refused Send Answer, the answer kept (AUDIT SHOULD 1; FIELD to settle).</summary>
            public const string AnswerDidNotGo = "Your answer did not go";
            /// <summary>A refused Stop (AUDIT SHOULD 1; FIELD to settle).</summary>
            public const string StopDidNotGo = "It did not stop";
            /// <summary>A Send with no session to go through: the question kept (AUDIT MUST; FIELD to settle).</summary>
            public const string SendDidNotGo = "Your question did not go";
            public const string TryAgain = "Try Again";
            public const string SendAgain = "Send Again";
        }

        /// <summary>How often the request is read while it is under way.</summary>
        public const double ReadEverySeconds = 2;

        /// <summary>How often the assistant's availability is read again while there is none (AUDIT SHOULD 2).</summary>
        public const double OfferEverySeconds = 30;

        private readonly IMenuHost host;
        private readonly CommandFactory commands;
        private Task<CommandAckMessage>? acknowledgement, answered, stopped;
        private string? sentAnswer, notice;

        /// <summary>The acknowledgement awaited is a resend's: the first send's outcome is unknown, whatever this one's is.</summary>
        private bool resending;
        private double nextOffer = double.NegativeInfinity;
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

        /// <summary>Words can be written or spoken: while writing or answering, and only while there is an assistant to send them to.</summary>
        private bool WordsOpen => (Ask.Stage is AskStage.Writing or AskStage.NeedsYou) && !Ask.Unavailable;

        public MenuFrame? Frame
        {
            get
            {
                var close = new Prompt(Footer.Close, Words.Close, GlazeIcon.Close, PromptKind.Close);
                var stop = Stoppable ? new Prompt(Stop, Words.Stop, GlazeIcon.Stop) : null;
                var talk = WordsOpen && host.VoiceOffered ? new Prompt(Talk, VoiceText.HoldToTalk, GlazeIcon.HoldToTalk, holds: true) : null;
                Prompt? main = null;
                // Send and Send Answer stand only once there are words to send: an action that cannot act is not drawn.
                if (Ask.CanTryAgain)
                    main = new Prompt(TryAgain, Words.TryAgain, GlazeIcon.Refresh, main: true);
                else if (Ask.OutcomeUnknown && acknowledgement == null)
                    main = new Prompt(SendAgain, Words.SendAgain, GlazeIcon.Refresh, main: true);
                else if (Ask.CanSend)
                    main = new Prompt(Send, Words.Send, GlazeIcon.TellIt, main: true);
                else if (Ask.Stage == AskStage.NeedsYou && answer.Trim().Length > 0)
                    main = new Prompt(Answer, Words.Answer, GlazeIcon.SendAnswer, main: true);
                var lines = new List<PageLine>();
                // The assistant's words, its question or its reply: the band's, as a claim, never the panel's.
                if (Ask.Band is LineWords said) lines.Add(new PageLine(LabelText.Plain(said.Text), wordsAreData: true, claim: true));
                if (Ask.Stage == AskStage.NeedsYou && answer.Trim().Length > 0) lines.Add(new PageLine(LineWords.Theirs(answer.Trim())));
                if (notice != null) lines.Add(new PageLine(notice));
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
                    if (command == null) return;
                    acknowledgement = host.Submit(command);
                    notice = null;
                    // No session to send through: nothing went, so the question stands written again (AUDIT MUST).
                    if (acknowledgement == null)
                    {
                        Ask.Unsent();
                        notice = Words.SendDidNotGo;
                        Changed?.Invoke();
                    }
                    return;
                }
                case SendAgain when Ask.OutcomeUnknown && acknowledgement == null:
                {
                    // The same envelope under its own id: a reused id is answered with the original command (AUDIT).
                    var again = Ask.SendAgain();
                    if (again == null) return;
                    acknowledgement = host.Submit(again);
                    resending = acknowledgement != null;
                    if (acknowledgement == null) Ask.Unknown();
                    Changed?.Invoke();
                    return;
                }
                case TryAgain when Ask.CanTryAgain:
                    notice = null;
                    Ask.TryAgain();
                    return;
                case Answer when Ask.Stage == AskStage.NeedsYou && Ask.RequestId != null && answer.Trim().Length > 0 && answered == null:
                    // The answer stays until the computer takes it: a refusal keeps it and says so (AUDIT SHOULD 1).
                    sentAnswer = answer;
                    answered = host.Submit(commands.ResumeRequest(Ask.RequestId, answer.Trim()));
                    answer = "";
                    notice = null;
                    nextRead = double.NegativeInfinity;
                    Changed?.Invoke();
                    return;
                case Stop when Stoppable && stopped == null:
                    stopped = host.Submit(commands.CancelRequest(Ask.RequestId!));
                    notice = null;
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
            // Once on opening, and again on a slow beat while there is no assistant, so the column learns it came back.
            if (offer == null && api != null && (!offerAsked || (Ask.Unavailable && Ask.Stage == AskStage.Writing && host.Now >= nextOffer)))
            {
                offerAsked = true;
                nextOffer = host.Now + OfferEverySeconds;
                offer = api.GetWorkspaceAgentAsync();
            }
            if (answered is { IsCompleted: true } resumed)
            {
                answered = null;
                if (Refused(resumed))
                {
                    if (answer.Trim().Length == 0) answer = sentAnswer ?? "";
                    notice = Words.AnswerDidNotGo;
                    Changed?.Invoke();
                }
                sentAnswer = null;
            }
            if (stopped is { IsCompleted: true } stopping)
            {
                stopped = null;
                if (Refused(stopping))
                {
                    notice = Words.StopDidNotGo;
                    Changed?.Invoke();
                }
            }
            if (offer is { IsCompleted: true } offered)
            {
                offer = null;
                if (offered.Status == TaskStatus.RanToCompletion) Ask.Offer(offered.Result);
            }
            if (acknowledgement is { IsCompleted: true } acked)
            {
                acknowledgement = null;
                var wasResend = resending;
                resending = false;
                // Only a session that was not there means nothing was sent; any other fault, or no answer, leaves the
                // outcome unknown, as CommandSubmissions has it (AUDIT, 2026-11-11). A resend that was not sent leaves
                // the first send's outcome as unknown as it was, its id and envelope kept (AUDIT's reread).
                if (acked.Status == TaskStatus.RanToCompletion && acked.Result != null) Ask.Acknowledge(acked.Result);
                else if (!wasResend && acked.Exception?.GetBaseException() is SessionUnavailableException)
                {
                    Ask.Unsent();
                    notice = Words.SendDidNotGo;
                    Changed?.Invoke();
                }
                else Ask.Unknown();
            }
            // A send whose outcome is unknown moves on once the computer's record of its command shows (ReadCommand).
            if (Ask.OutcomeUnknown && Ask.CommandId is string commandId && host.State?.Commands.TryGetValue(commandId, out var recorded) == true)
                Ask.ReadCommand(recorded);
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

        /// <summary>A press the computer refused, or that never reached it.</summary>
        private static bool Refused(Task<CommandAckMessage> ack) =>
            ack.Status != TaskStatus.RanToCompletion || ack.Result == null
            || ack.Result.Disposition is CommandAckDisposition.Rejected or CommandAckDisposition.Conflict;

        public void Drawn(MenuFrame drawn, Footer? sidePanel) { }
        public void FocusLeft() => talking = false;
    }
}
