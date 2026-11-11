#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>Where pairing stands, as its column shows it.</summary>
    public enum PairingColumnStep
    {
        /// <summary>The headset asks the Wi-Fi for computers whose pairing is open.</summary>
        Looking,

        /// <summary>What the search found, none included: the person picks one, or types its address.</summary>
        Found,

        /// <summary>The system number pad asks for the code, and then the exchange runs.</summary>
        Code,

        Paired,

        Refused,
    }

    /// <summary>
    /// Pairing beside the menu (ADR 0017, amendment of 2026-11-11): what the search found as rows to pick from, the code,
    /// paired, or refused, each as its card (<see cref="PairingCards"/>) on the frame. The pairing, in the Unity layer, says
    /// where it stands through <see cref="Looking"/>, <see cref="Found"/>, <see cref="Code"/>, <see cref="Paired"/> and
    /// <see cref="Refused"/>, and hears the person's choice through the column's two callbacks: a computer picked, or typing
    /// its address (null), and trying again. Nothing found is trusted: the code and the pinned certificate decide.
    /// </summary>
    public sealed class PairingColumn : IMenuColumn
    {
        /// <summary>The column's subject, which its reader in the redesign knows it by.</summary>
        public const string Subject = "Pairing";

        public const string Choose = "pairing-choose";
        public const string TypeAddress = "pairing-type-address";
        public const string TryAgain = "pairing-try-again";

        public const string TypeAddressWords = "Type Address";
        public const string TryAgainWords = "Try Again";

        private readonly Action<PairingChoice?> chosen;
        private readonly Action again;
        private IReadOnlyList<PairingChoice> found = Array.Empty<PairingChoice>();
        private string? name;
        private string address = "";
        private string? refusal;
        private long? attemptsLeft;

        /// <param name="chosen">A found computer picked, or null to type its address.</param>
        /// <param name="again">Try Again after a refusal.</param>
        public PairingColumn(Action<PairingChoice?> chosen, Action again)
        {
            this.chosen = chosen;
            this.again = again;
        }

        public event Action? Changed;
        public event Action? Closed;

        public PairingColumnStep Step { get; private set; } = PairingColumnStep.Looking;

        public void Looking() => Set(PairingColumnStep.Looking);

        public void Found(IReadOnlyList<PairingChoice> computers)
        {
            found = computers.Take(RoomCard.ListAPage).ToList();
            Set(PairingColumnStep.Found);
        }

        /// <param name="computer">The found computer's announced name, or null where the person typed <paramref name="at"/>.</param>
        public void Code(string? computer, string at)
        {
            name = computer;
            address = at;
            Set(PairingColumnStep.Code);
        }

        public void Paired() => Set(PairingColumnStep.Paired);

        /// <summary>Refused or broken, by the refusal's code, never its message (<see cref="PairingException"/>).</summary>
        public void Refused(string? code, long? left)
        {
            refusal = code;
            attemptsLeft = left;
            Set(PairingColumnStep.Refused);
        }

        public MenuFrame? Frame
        {
            get
            {
                var close = new Prompt(Footer.Close, "Close", GlazeIcon.Close, PromptKind.Close);
                var type = new Prompt(TypeAddress, TypeAddressWords, GlazeIcon.Type, main: Step == PairingColumnStep.Found && found.Count == 0);
                switch (Step)
                {
                    case PairingColumnStep.Found when found.Count > 0:
                    {
                        // One line per found computer, in the card's order: the redesign's reader gives each row its line's press.
                        var lines = found.Select(choice => new PageLine(LineWords.TheirName(choice.Name), action: Choose, key: choice.Address, opens: true)).ToList();
                        return new MenuFrame(Subject, new Footer(close, secondary: type), lines: lines).WithCard(PairingCards.Found(found));
                    }
                    case PairingColumnStep.Found:
                        return Carded(PairingCards.Found(found), new Footer(close, farRight: type), PairingCards.NoneFoundLine);
                    case PairingColumnStep.Code:
                        return Carded(PairingCards.Code(name, address), new Footer(close), PairingCards.CodeLine);
                    case PairingColumnStep.Paired:
                        return Carded(PairingCards.Paired(name, address), new Footer(close), PairingCards.PairedLine);
                    case PairingColumnStep.Refused:
                    {
                        var (_, line) = PairingCards.Why(refusal);
                        return Carded(PairingCards.Refused(refusal, attemptsLeft),
                            new Footer(close, secondary: type, farRight: new Prompt(TryAgain, TryAgainWords, GlazeIcon.Refresh, main: true)), line);
                    }
                    default:
                        return Carded(PairingCards.Looking(), new Footer(close, secondary: type), PairingCards.LookingLine);
                }
            }
        }

        /// <summary>A frame whose card says it, with its one line for a view that draws no card.</summary>
        private static MenuFrame Carded(RoomCard card, Footer footer, string line) =>
            new MenuFrame(Subject, footer, lines: new[] { new PageLine(line) }).WithCard(card);

        public void Act(string id, string? key)
        {
            switch (id)
            {
                case Choose when Step == PairingColumnStep.Found && key != null:
                    // Only an address the search offered, the first under it: a press names nothing else.
                    foreach (var choice in found)
                    {
                        if (!string.Equals(choice.Address, key, StringComparison.Ordinal)) continue;
                        chosen(choice);
                        return;
                    }
                    return;
                case TypeAddress when Step is PairingColumnStep.Looking or PairingColumnStep.Found or PairingColumnStep.Refused:
                    chosen(null);
                    return;
                case TryAgain when Step == PairingColumnStep.Refused:
                    again();
                    return;
                case Footer.Close:
                    Closed?.Invoke();
                    return;
            }
        }

        private void Set(PairingColumnStep step)
        {
            Step = step;
            Changed?.Invoke();
        }

        public void Drawn(MenuFrame drawn, Footer? sidePanel) { }
        public void HoldStarted(string id) { }
        public void HoldEnded(string id, bool letGo) { }
        public void Heard(string text) { }
        public void Said(string words) { }
        public void Tick() { }
        public void FocusLeft() { }
    }
}
