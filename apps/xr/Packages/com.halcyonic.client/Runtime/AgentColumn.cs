#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>One agent or model that could run the picked task's next start, as its owner lists them.</summary>
    public sealed class AgentOption
    {
        public AgentOption(string key, string name, bool chosen)
        {
            Key = key;
            Name = name;
            Chosen = chosen;
        }

        /// <summary>What its owner knows it by; opaque here, handed back when it is picked.</summary>
        public string Key { get; }

        /// <summary>The runtime's own name for it, as the product shows such names.</summary>
        public string Name { get; }

        /// <summary>It is the one the next start would run on.</summary>
        public bool Chosen { get; }
    }

    /// <summary>
    /// What the computer offers for the picked task's next start, and what is chosen: the agent by its own name, whether
    /// that name is a model's or an agent app's, whether its runtime offers to never ask, the level chosen, and every
    /// agent and model that could run instead. Read afresh each time the page draws; nothing here is a run's record.
    /// </summary>
    public sealed class AgentOffer
    {
        public AgentOffer(string agent, bool listsModels, bool offersNeverAsk, bool neverAsk, IReadOnlyList<AgentOption>? options = null)
        {
            Agent = agent;
            ListsModels = listsModels;
            OffersNeverAsk = offersNeverAsk;
            NeverAsk = neverAsk && offersNeverAsk;
            Options = options ?? new AgentOption[0];
        }

        public string Agent { get; }

        /// <summary>The name is a model's ("Model · "), not an agent app's ("Agent App · ").</summary>
        public bool ListsModels { get; }

        /// <summary>The runtime's descriptor offers a start that never asks; without it the levels are not drawn.</summary>
        public bool OffersNeverAsk { get; }

        /// <summary>The level chosen for the next start: never ask, or ask first. Never true where it is not offered.</summary>
        public bool NeverAsk { get; }

        /// <summary>Every agent and model that could run the next start, the chosen one among them, in their owner's order.</summary>
        public IReadOnlyList<AgentOption> Options { get; }

        /// <summary>There is something to choose: a level, or another agent or model. Without either the action that opens the page is not drawn.</summary>
        public bool Chooses => OffersNeverAsk || Options.Count > 1;

        /// <summary>
        /// The offer as the computer's runtimes and the person's choice stand: every runtime that can start, then each
        /// model it lists, in the order the agent's choice has always turned (<see cref="WorkspacePresentationState.NextAgent"/>),
        /// each by the name the product shows it by, the chosen one marked. The level is never ask only where it was
        /// chosen AND the chosen runtime offers it: what the page marks is what the next start carries (ADR 0043).
        /// Null where there is no agent to name or the chosen runtime is not among them.
        /// </summary>
        /// <param name="agent">The agent as the file names it at rest (<see cref="KitPresenter.ChipsOf(WorkspacePresentation, string?)"/>'s agent).</param>
        public static AgentOffer? Of(string? agent, IReadOnlyList<RuntimeDescriptor>? runtimes, Func<string, IReadOnlyList<RuntimeModel>?> modelsOf, string? chosenRuntimeId,
            string? chosenModelRef, bool neverAskChosen)
        {
            if (string.IsNullOrWhiteSpace(agent) || runtimes == null) return null;
            var chosen = runtimes.FirstOrDefault(runtime => runtime != null && runtime.RuntimeId == chosenRuntimeId);
            if (chosen == null) return null;
            var model = string.IsNullOrWhiteSpace(chosenModelRef) ? null : chosenModelRef;
            var options = new List<AgentOption>();
            foreach (var runtime in runtimes)
            {
                if (runtime == null || !runtime.Capabilities.StartExecution) continue;
                var here = runtime.RuntimeId == chosen.RuntimeId;
                if (KitPresenter.AgentOf(runtime, null, null) is string app) options.Add(new AgentOption(KeyOf(runtime.RuntimeId, null), app, here && model == null));
                if (runtime.ModelChoice != ModelChoice.Listed) continue;
                foreach (var listed in modelsOf(runtime.RuntimeId) ?? new RuntimeModel[0])
                {
                    if (listed == null) continue;
                    var name = KitPresenter.AgentOf(runtime, null, CaptureNames.ModelName(listed.DisplayName ?? listed.ModelRef));
                    if (name != null) options.Add(new AgentOption(KeyOf(runtime.RuntimeId, listed.ModelRef), name, here && model == listed.ModelRef));
                }
            }
            return new AgentOffer(agent!, chosen.ModelChoice == ModelChoice.Listed && model != null, chosen.OffersNeverAsk == true, neverAskChosen, options);
        }

        /// <summary>An option's key: its runtime, and its model where it is one, parted by a character no id holds.</summary>
        public static string KeyOf(string runtimeId, string? modelRef) => string.IsNullOrWhiteSpace(modelRef) ? runtimeId : runtimeId + KeyPart + modelRef;

        /// <summary>The runtime and the model a picked option's key names.</summary>
        public static (string RuntimeId, string? ModelRef) Picked(string key)
        {
            var at = key.IndexOf(KeyPart);
            return at < 0 ? (key, null) : (key.Substring(0, at), key.Substring(at + 1));
        }

        private const char KeyPart = (char)0x1F;
    }

    /// <summary>
    /// The agent and its level (the redesign's addendum A1, the owner's ask of 2026-11-10): one page whose subject is the
    /// computer's fact, "Model · {model}", and whose lines are the two levels as answers, Ask First and Never Ask, and
    /// Change, which turns the page to every agent and model that could run instead. A pick is handed to its owner at
    /// once and the page closes: the action at rest then reads the new choice, which is the sign it took. The page keeps
    /// nothing itself but which of its two faces shows.
    /// </summary>
    public sealed class AgentColumn : IMenuColumn
    {
        /// <summary>The action at rest that opens this page, beside Hold to Talk and Type; its words are <see cref="RoomBar.AgentWords"/>.</summary>
        public const string Open = "agent-open";

        /// <summary>A level's answer; its key is the start's own value, <see cref="Ask"/> or <see cref="Never"/>.</summary>
        public const string Level = "agent-level";
        public const string Ask = "ask";
        public const string Never = "never_ask";

        /// <summary>The line that turns the page to the agents and models.</summary>
        public const string Change = "agent-change";

        /// <summary>An agent or model's answer; its key is the option's own.</summary>
        public const string Pick = "agent-pick";

        public const string AskFirstWords = "Ask First";
        public const string NeverAskWords = "Never Ask";
        public const string ChangeWords = "Change";
        public const string BackWords = "Back";

        /// <summary>The review's own words for the fact (NewWorkReview).</summary>
        public const string ModelLead = "Model · ";
        public const string AgentAppLead = "Agent App · ";

        /// <summary>The agents and models a page holds as answers; more page by Next Choices.</summary>
        public const int OptionsAPage = 3;

        private readonly Func<AgentOffer?> offer;
        private readonly Action<bool> levelChosen;
        private readonly Action<string> optionChosen;
        private bool changing;
        private int page;

        /// <param name="offer">What the computer offers now; null where there is no agent to name.</param>
        /// <param name="levelChosen">The person chose a level: true for never ask.</param>
        /// <param name="optionChosen">The person chose another agent or model, by its key.</param>
        public AgentColumn(Func<AgentOffer?> offer, Action<bool> levelChosen, Action<string> optionChosen)
        {
            this.offer = offer;
            this.levelChosen = levelChosen;
            this.optionChosen = optionChosen;
        }

        public event Action? Changed;
        public event Action? Closed;

        /// <summary>The words of a level.</summary>
        public static string LevelWords(bool neverAsk) => neverAsk ? NeverAskWords : AskFirstWords;

        /// <summary>How many pages the agents and models take.</summary>
        public static int Pages(int options) => Math.Max(1, (options + OptionsAPage - 1) / OptionsAPage);

        public MenuFrame? Frame
        {
            get
            {
                if (offer() is not AgentOffer now || !now.Chooses) return null;
                var subject = LineWords.Ours(now.ListsModels ? ModelLead : AgentAppLead) + LineWords.TheirName(now.Agent);
                var footer = new Footer(new Prompt(Footer.Close, BackWords, GlazeIcon.Back, PromptKind.Close));
                var lines = new List<PageLine>();
                if (changing && now.Options.Count > 1)
                {
                    var pages = Pages(now.Options.Count);
                    var at = Math.Min(page, pages - 1);
                    foreach (var option in now.Options.Skip(at * OptionsAPage).Take(OptionsAPage))
                        lines.Add(new PageLine(LineWords.TheirName(option.Name), action: Pick, key: option.Key, choice: true, chosen: option.Chosen, answerMark: AnswerMark.Single));
                    if (pages > 1)
                        lines.Add(new PageLine("Next Choices · " + (at + 1).ToString(CultureInfo.InvariantCulture) + " of " + pages.ToString(CultureInfo.InvariantCulture),
                            icon: GlazeIcon.Next, action: Footer.NextPage, opens: true, control: LineControl.Action));
                    return new MenuFrame(subject, footer, lines: lines);
                }
                if (now.OffersNeverAsk)
                {
                    lines.Add(new PageLine(AskFirstWords, action: Level, key: Ask, choice: true, chosen: !now.NeverAsk, answerMark: AnswerMark.Single));
                    lines.Add(new PageLine(NeverAskWords, action: Level, key: Never, choice: true, chosen: now.NeverAsk, answerMark: AnswerMark.Single));
                }
                if (now.Options.Count > 1) lines.Add(new PageLine(ChangeWords, icon: GlazeIcon.Change, action: Change, opens: true, control: LineControl.Action));
                return new MenuFrame(subject, footer, lines: lines);
            }
        }

        public void Act(string id, string? key)
        {
            var now = offer();
            switch (id)
            {
                case Level when !changing && now is { OffersNeverAsk: true } && (key == Ask || key == Never):
                    levelChosen(key == Never);
                    Closed?.Invoke();
                    return;
                case Change when !changing && now is { Options: { Count: > 1 } } several:
                    changing = true;
                    page = Math.Max(0, several.Options.ToList().FindIndex(option => option.Chosen)) / OptionsAPage;
                    break;
                case Pick when changing && key != null && now != null && now.Options.Any(option => option.Key == key):
                    changing = false;
                    optionChosen(key);
                    Closed?.Invoke();
                    return;
                case Footer.NextPage when changing && now != null:
                    page = (Math.Min(page, Pages(now.Options.Count) - 1) + 1) % Pages(now.Options.Count);
                    break;
                case Footer.Close when changing:
                    changing = false;
                    break;
                case Footer.Close:
                    Closed?.Invoke();
                    return;
                default: return;
            }
            Changed?.Invoke();
        }

        public void FocusLeft()
        {
            if (!changing) return;
            changing = false;
            Changed?.Invoke();
        }

        public void Drawn(MenuFrame drawn, Footer? sidePanel) { }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string text) { }
        public void Said(string words) { }
        public void Tick() { }
    }
}
