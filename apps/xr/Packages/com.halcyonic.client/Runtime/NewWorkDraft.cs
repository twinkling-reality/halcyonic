#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Halcyonic.Contracts;

namespace Halcyonic.Client
{
    /// <summary>
    /// The choices a person makes before starting work from the headset. A runtime's models are kept
    /// with those served on this Mac first, in the runtime's order, then the rest (<see cref="Elsewhere"/>).
    /// For a runtime that lists its models, a model on this Mac is chosen for the person, and said so
    /// (<see cref="Preferred"/>); a model that
    /// runs elsewhere, or where it runs is not known, is never chosen for them and takes a second,
    /// deliberate press (<see cref="ChooseModel"/>), since the person's code and instructions go
    /// there. No start is built without a model for such a runtime.
    /// </summary>
    public sealed class NewWorkDraft
    {
        private readonly CommandFactory commands;
        private readonly List<RuntimeModel> models = new List<RuntimeModel>();

        public NewWorkDraft(CommandFactory commands) => this.commands = commands;

        public string? ProjectId { get; set; }

        public RuntimeDescriptor? Runtime { get; private set; }

        public string Objective { get; set; } = "";

        public IReadOnlyList<RuntimeModel> Models => models;

        public RuntimeModel? Model { get; private set; }

        /// <summary>
        /// Whether this start is to be never ask (ADR 0043): false, Ask Me First, unless the flow that shows the level before
        /// the start sets it (the review's Model row naming the level; AUDIT, 2026-11-11). The choice alone is not the level
        /// the start uses (<see cref="StartsNeverAsk"/>).
        /// </summary>
        public bool NeverAsk { get; set; }

        /// <summary>The level this start will use is never ask: the person chose it and the chosen runtime's descriptor offers it. Otherwise it asks.</summary>
        public bool StartsNeverAsk => NeverAsk && Runtime?.OffersNeverAsk == true && !StartsPlan;

        /// <summary>Whether this start is to be a plan (SCREENS's <see cref="PlanStart"/>): false unless the flow that shows the level sets it.</summary>
        public bool Plan { get; set; }

        /// <summary>The start will be a plan: the person chose Show Plan First and the chosen runtime offers a plan. A plan always asks.</summary>
        public bool StartsPlan => Plan && PlanStart.Offered(Runtime);

        public string? ModelProblem { get; private set; }

        /// <summary>The model was chosen for the person, because it runs on this Mac; they have not chosen it themselves.</summary>
        public bool ModelPreselected { get; private set; }

        /// <summary>A model that runs elsewhere, pressed once: pressing it again chooses it.</summary>
        public RuntimeModel? PendingModel { get; private set; }

        /// <summary>Whether a model runs on this Mac, the only kind chosen without a deliberate second press.</summary>
        public static bool RunsHere(RuntimeModel model) => model.Served == ModelServed.ThisMac;

        /// <summary>Where the models that do not run on this Mac begin in <see cref="Models"/>, or its count when there are none.</summary>
        public int Elsewhere
        {
            get
            {
                var index = models.FindIndex(model => !RunsHere(model));
                return index < 0 ? models.Count : index;
            }
        }

        /// <summary>
        /// The model on this Mac to choose for the person, or null when none is listed: one that declares
        /// tool calling before one that does not, then the one with the larger context, then the
        /// runtime's own order. The list says neither which model the runtime uses by default nor how
        /// large a model is, so neither can decide.
        /// </summary>
        public static RuntimeModel? Preferred(IReadOnlyList<RuntimeModel> listed)
        {
            RuntimeModel? best = null;
            foreach (var model in listed)
            {
                if (!RunsHere(model)) continue;
                if (best == null || Better(model, best)) best = model;
            }
            return best;
        }

        private static bool Better(RuntimeModel model, RuntimeModel than)
        {
            var tools = (model.ToolCalling == ModelToolCalling.Declared).CompareTo(than.ToolCalling == ModelToolCalling.Declared);
            if (tools != 0) return tools > 0;
            return (model.ContextTokens ?? 0) > (than.ContextTokens ?? 0);
        }

        public void ChooseRuntime(RuntimeDescriptor runtime)
        {
            Runtime = runtime;
            models.Clear();
            Model = null;
            ModelPreselected = false;
            PendingModel = null;
            ModelProblem = runtime.ModelChoice == ModelChoice.Listed ? UsageColumn.ReadingModels : null;
        }

        public void SetModels(RuntimeModelsResponse response)
        {
            if (Runtime == null || Runtime.RuntimeId != response.RuntimeId) return;
            models.Clear();
            Model = null;
            ModelPreselected = false;
            PendingModel = null;
            if (response.Result is AvailableModels available)
            {
                // The Mac's models first: OpenCode lists hosted models before the local ones.
                foreach (var model in available.Models)
                {
                    if (RunsHere(model)) models.Add(model);
                }
                foreach (var model in available.Models)
                {
                    if (!RunsHere(model)) models.Add(model);
                }
                ModelProblem = models.Count == 0 ? EntryText.NoModels : null;
                Model = Preferred(models);
                ModelPreselected = Model != null;
            }
            else if (response.Result is UnavailableModels unavailable)
            {
                ModelProblem = EntryText.ModelsUnread(unavailable.Reason.Code);
            }
        }

        /// <summary>
        /// The models couldn't be asked for, <paramref name="connected"/> saying whether the computer was
        /// connected to ask, and <paramref name="answered"/> whether the request got any reply in time. Takes
        /// no words: a failed request's are the control plane's own.
        /// </summary>
        public void ModelReadFailed(bool connected, bool answered = true)
        {
            models.Clear();
            Model = null;
            ModelPreselected = false;
            PendingModel = null;
            ModelProblem = !connected ? EntryText.ModelsNotConnected : answered ? EntryText.ModelsUnreadable : EntryText.ModelsComputerSilent;
        }

        /// <summary>Focus went to another window: a first press on a model that runs elsewhere no longer counts.</summary>
        public void FocusLeft() => PendingModel = null;

        /// <summary>
        /// Chooses a model from the runtime's current list. A model on this Mac is chosen at once; one
        /// that runs elsewhere, or where it runs is not known, is chosen only when pressed a second time
        /// in a row, and the first press leaves the current choice as it was. Returns whether it is chosen.
        /// </summary>
        public bool ChooseModel(RuntimeModel model)
        {
            if (!models.Contains(model)) throw new ArgumentException("Choose a model from the selected runtime's current list.", nameof(model));
            if (!RunsHere(model) && PendingModel != model)
            {
                PendingModel = model;
                return false;
            }
            PendingModel = null;
            Model = model;
            ModelPreselected = false;
            return true;
        }

        public string? Problem
        {
            get
            {
                if (ProjectId == null) return "Choose Project";
                if (Runtime == null || !Runtime.Capabilities.StartExecution) return "Choose Agent App";
                if (Runtime.ModelChoice == ModelChoice.Listed && Model == null) return ModelProblem ?? "Choose Model";
                if (string.IsNullOrWhiteSpace(Objective)) return "Type Goal";
                if (Objective.Length > 4000) return "A goal can be up to 4,000 characters.";
                return null;
            }
        }

        /// <summary>The most characters a title made from the person's words holds.</summary>
        public const int MadeTitleLength = 60;

        /// <summary>The title of a task whose words hold nothing to name it by.</summary>
        public const string UnnamedTask = "New Task";

        /// <summary>
        /// The workstream's title, made from the objective by the one rule the plane holds as well
        /// (apps/control-plane/src/core/task-title.ts, which says the rule step by step), pinned by one
        /// table of cases both suites read (fixtures/titles/cases.json), so the review shows the name the
        /// task gets. Halcyonic adds no ellipsis; a label spells what cannot be seen (<see cref="LabelText"/>).
        /// </summary>
        public string Title => TitleOf(Objective);

        private static readonly char[] LineBreaks = { '\n', '\r', '\v', '\f', (char)0x85, (char)0x2028, (char)0x2029 };
        private static readonly string[] NotAnEnd = { "e.g.", "i.e.", "vs.", "cf." };
        private static readonly char[] LeftAtACut = { ' ', ',', ';', ':', '-', (char)0x2013, (char)0x2014 };

        /// <summary>The title Halcyonic gives a task made from <paramref name="text"/>, the person's own words.</summary>
        public static string TitleOf(string? text) => TitleOf(text, out _);

        /// <summary>
        /// Whether <paramref name="text"/> holds nothing to name its task by, so its title is Halcyonic's
        /// own <see cref="UnnamedTask"/>, never words the person typed.
        /// </summary>
        public static bool NamesNothing(string? text)
        {
            TitleOf(text, out var unnamed);
            return unnamed;
        }

        private static string TitleOf(string? text, out bool unnamed)
        {
            unnamed = false;
            var line = FirstLine(text ?? "");
            unnamed = line == null;
            if (line == null) return UnnamedTask;
            var sentence = FirstSentence(string.Join(" ", Array.ConvertAll(line.Split(' '), WithoutSecrets)));
            if (CharactersOf(sentence).Count <= MadeTitleLength) return sentence;
            var words = sentence.Split(' ');
            if (CharactersOf(words[0]).Count > MadeTitleLength) return HostOf(words[0]) ?? EndOf(words[0], '/');
            var title = words[0];
            for (var index = 1; index < words.Length; index++)
            {
                var longer = title + " " + words[index];
                if (CharactersOf(longer).Count > MadeTitleLength) break;
                title = longer;
            }
            var kept = title.TrimEnd(LeftAtACut);
            unnamed = kept.Length == 0;
            return unnamed ? UnnamedTask : kept;
        }

        /// <summary>
        /// The characters of <paramref name="text"/> as the person sees them, each whole: a code point with
        /// the marks and emoji skin tones that follow it, or a pair of regional indicators (a flag).
        /// </summary>
        public static List<string> CharactersOf(string text)
        {
            var characters = new List<string>();
            for (var index = 0; index < text.Length; index++)
            {
                var width = char.IsSurrogatePair(text, index) ? 2 : 1;
                var point = text.Substring(index, width);
                var code = width == 2 ? char.ConvertToUtf32(text, index) : text[index];
                var category = CharUnicodeInfo.GetUnicodeCategory(text, index);
                index += width - 1;
                var last = characters.Count - 1;
                var joins = last >= 0
                    && (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark
                        || category == UnicodeCategory.EnclosingMark
                        || (code >= 0x1F3FB && code <= 0x1F3FF)
                        || (IsRegionalIndicator(code) && characters[last].Length == 2 && char.IsSurrogatePair(characters[last], 0)
                            && IsRegionalIndicator(char.ConvertToUtf32(characters[last], 0))));
                if (joins) characters[last] += point;
                else characters.Add(point);
            }
            return characters;
        }

        private static bool IsRegionalIndicator(int code) => code >= 0x1F1E6 && code <= 0x1F1FF;

        /// <summary>The first line with words: hidden code points dropped, every space one, none at either end.</summary>
        private static string? FirstLine(string text)
        {
            foreach (var raw in text.Split(LineBreaks))
            {
                var kept = new StringBuilder();
                for (var index = 0; index < raw.Length; index++)
                {
                    var width = char.IsSurrogatePair(raw, index) ? 2 : 1;
                    var category = CharUnicodeInfo.GetUnicodeCategory(raw, index);
                    if (raw[index] == '\t' || category == UnicodeCategory.SpaceSeparator)
                    {
                        if (kept.Length > 0 && kept[kept.Length - 1] != ' ') kept.Append(' ');
                    }
                    else if (category != UnicodeCategory.Control && category != UnicodeCategory.Format && category != UnicodeCategory.Surrogate)
                        kept.Append(raw, index, width);
                    index += width - 1;
                }
                var line = kept.ToString().TrimEnd(' ');
                if (line.Length > 0) return line;
            }
            return null;
        }

        private static readonly char[] HostEnds = { '/', '?', '#' };
        private static readonly char[] QueryStarts = { '?', '#' };

        /// <summary>
        /// An address (a word holding "://", or one whose part before its first "/" holds a dot) without
        /// the parts that often hold a token: a user before its host, its query and fragment.
        /// </summary>
        private static string WithoutSecrets(string word)
        {
            if (!(HostStart(word) is int from)) return word;
            var rest = word.Substring(from);
            var hostEnd = rest.IndexOfAny(HostEnds);
            var user = (hostEnd < 0 ? rest : rest.Substring(0, hostEnd)).LastIndexOf('@');
            if (user >= 0) rest = rest.Substring(user + 1);
            var query = rest.IndexOfAny(QueryStarts);
            return word.Substring(0, from) + (query < 0 ? rest : rest.Substring(0, query));
        }

        /// <summary>The first sentence, ending at ". ", "? " or "! ", never at an abbreviation's stop.</summary>
        private static string FirstSentence(string line)
        {
            for (var at = 0; at < line.Length - 1; at++)
            {
                if ((line[at] != '.' && line[at] != '?' && line[at] != '!') || line[at + 1] != ' ') continue;
                var from = line.LastIndexOf(' ', at) + 1;
                if (Array.IndexOf(NotAnEnd, line.Substring(from, at + 1 - from).ToLowerInvariant()) < 0) return line.Substring(0, at + 1);
            }
            return line;
        }

        /// <summary>Where an address's host begins: after its scheme, or at its start; null for a word that is no address.</summary>
        private static int? HostStart(string word)
        {
            var scheme = word.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) return scheme + 3;
            var slash = word.IndexOf('/');
            return slash > 0 && word.IndexOf('.', 0, slash) >= 0 ? 0 : (int?)null;
        }

        /// <summary>
        /// An address's host as typed, its port kept (already without a user), so no path reaches the
        /// title; a host over a title keeps its end after a "."; null for a word that is no address.
        /// </summary>
        private static string? HostOf(string word)
        {
            if (!(HostStart(word) is int from)) return null;
            var rest = word.Substring(from);
            var end = rest.IndexOfAny(HostEnds);
            var host = end < 0 ? rest : rest.Substring(0, end);
            if (host.Length == 0) return null;
            return CharactersOf(host).Count <= MadeTitleLength ? host : EndOf(host, '.');
        }

        /// <summary>
        /// A word longer than a title, by its end: its last characters from a <paramref name="mark"/> among
        /// them where one is, a path from a "/" and a host after a ".", so a host never begins inside a label.
        /// </summary>
        private static string EndOf(string word, char mark)
        {
            var all = CharactersOf(word);
            var end = all.GetRange(Math.Max(0, all.Count - MadeTitleLength), Math.Min(all.Count, MadeTitleLength));
            var at = end.IndexOf(mark.ToString());
            if (at < 0 || at == end.Count - 1) return string.Concat(end);
            var from = mark == '/' ? at : at + 1;
            return string.Concat(end.GetRange(from, end.Count - from));
        }

    }
}
