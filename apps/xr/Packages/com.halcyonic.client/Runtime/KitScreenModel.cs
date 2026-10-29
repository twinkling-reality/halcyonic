#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Halcyonic.Client
{
    /// <summary>An action on the bar beneath a picked project's screen.</summary>
    public enum KitScreenAction
    {
        ChangeIt,

        /// <summary>The one clear action for a project that is not VR yet: it only asks the agent.</summary>
        MakeAVrVersion,

        TryIt,

        Steps,

        Why,

        HowItWent,
    }

    /// <summary>
    /// A picked project's screen and the bar beneath it (ADR 0040, rule 2). The screen shows a picture only from the computer's
    /// own check screenshot, with its time; until one exists, a neutral two-letter mark. The project's name, the runtime and
    /// the state stand on the screen's own title chip. The bar holds one primary action, Try it only after a passed check of
    /// the computer's own and with a route to try it, and quiet doors to Steps, Why and How it went; it folds to a handle
    /// while nothing is wanted. Where it stands, and the way back to the gallery, are the placement's, never this model's.
    /// </summary>
    public sealed class KitScreenModel
    {
        public KitScreenModel(string project, string runtime, string state, bool isVr, KitMoment? lastCheck, bool tryRoute,
            DateTimeOffset? pictureTakenAt, bool barOpen, bool working, DateTimeOffset? newestChangeAt = null)
        {
            NewestChangeAt = newestChangeAt;
            Project = project;
            Runtime = runtime;
            State = state;
            IsVr = isVr;
            LastCheck = lastCheck;
            TryRoute = tryRoute;
            PictureTakenAt = pictureTakenAt;
            BarOpen = barOpen;
            Working = working;
        }

        /// <summary>The project's name, theirs.</summary>
        public string Project { get; }

        /// <summary>The agent app's name.</summary>
        public string Runtime { get; }

        /// <summary>The task's state in StateLanguage's words.</summary>
        public string State { get; }

        /// <summary>The project is a VR app already.</summary>
        public bool IsVr { get; }

        /// <summary>The newest check, of the computer's own or the agent's; only a passed one of the computer's own opens Try it.</summary>
        public KitMoment? LastCheck { get; }

        /// <summary>A route to try the build exists.</summary>
        public bool TryRoute { get; }

        /// <summary>When the computer's own check took the screenshot the screen shows; null where none exists.</summary>
        public DateTimeOffset? PictureTakenAt { get; }

        /// <summary>The person pointed at or pressed the screen, so the bar is open; it folds again while nothing is wanted.</summary>
        public bool BarOpen { get; }

        /// <summary>The runtime's own state says the work goes on.</summary>
        public bool Working { get; }

        /// <summary>When the computer last saw a file of the project change (ADR 0038); null where it saw none.</summary>
        public DateTimeOffset? NewestChangeAt { get; }

        public bool ShowsPicture => PictureTakenAt != null;

        /// <summary>"Last checked build, today at 10:38", or with its day where that isn't today; null without a picture.</summary>
        public string? PictureLabel(DateTimeOffset now, TimeZoneInfo zone) => PictureTakenAt is DateTimeOffset taken
            ? "Last checked build, " + UsageLeftPresenter.When(taken, now, zone) : null;

        /// <summary>The neutral mark in place of a picture.</summary>
        public string Monogram => MonogramOf(Project);

        public bool Folded => !BarOpen;

        /// <summary>The primary action: Make a VR version for a project that is not VR yet, else Change it.</summary>
        public KitScreenAction Primary => IsVr ? KitScreenAction.ChangeIt : KitScreenAction.MakeAVrVersion;

        /// <summary>
        /// Try it stands only after a passed check of the computer's own, with a route to try it, while no turn runs, and with
        /// no change the computer saw after that check: the build would serve files nobody checked.
        /// </summary>
        public bool CanTry => TryRoute && !Working && LastCheck is KitMoment check && check.Kind == KitMomentKind.HostCheck && check.Passed == true
            && (NewestChangeAt is not DateTimeOffset changed || check.At is DateTimeOffset checkedAt && changed <= checkedAt);

        /// <summary>Every action of the bar in its order: the primary, the other, Try it where it may stand, then the doors.</summary>
        public IReadOnlyList<KitScreenAction> Actions
        {
            get
            {
                var actions = new List<KitScreenAction> { Primary };
                if (!IsVr) actions.Add(KitScreenAction.ChangeIt);
                if (CanTry) actions.Add(KitScreenAction.TryIt);
                actions.AddRange(new[] { KitScreenAction.Steps, KitScreenAction.Why, KitScreenAction.HowItWent });
                return actions;
            }
        }

        /// <summary>The actions the bar shows now: all of them open, none folded.</summary>
        public IReadOnlyList<KitScreenAction> Shown => Folded ? Array.Empty<KitScreenAction>() : Actions;

        /// <summary>The first letters of the name's first two words, or its first two letters; letters only, upper case.</summary>
        public static string MonogramOf(string name)
        {
            var words = (name ?? "").Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => new string(word.Where(char.IsLetter).ToArray())).Where(word => word.Length > 0).ToList();
            var mark = words.Count >= 2 ? words[0].Substring(0, 1) + words[1].Substring(0, 1)
                : words.Count == 1 ? words[0].Substring(0, Math.Min(2, words[0].Length)) : "";
            return mark.ToUpperInvariant();
        }

        public static string Words(KitScreenAction action) => action switch
        {
            KitScreenAction.ChangeIt => "Change it",
            KitScreenAction.MakeAVrVersion => "Make a VR version",
            KitScreenAction.TryIt => "Try it",
            KitScreenAction.Steps => "Steps",
            KitScreenAction.Why => "Why",
            KitScreenAction.HowItWent => "How it went",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
    }
}
