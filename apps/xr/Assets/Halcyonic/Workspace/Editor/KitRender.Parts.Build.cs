#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// One file's panel in Watch It Work (<see cref="RoomWritingPanel"/>): a file at its first read, settled; the same file read
    /// again with three rows changed, caught mid-pour and lit; a file arriving, caught mid-rise; and a file whose text is not
    /// shown. Each is measured (the panel the panel's width, the name whole in the mono face, every row whole at its own alpha
    /// once settled, rows before the change shown while the new ones pour in by their turn, the changed rows marked by a
    /// white hairline, the other rows quiet, no well under the code, the arrival rising from 0.92 with its glass and words
    /// together, a held file saying why with no rows, nothing that takes a press) and read in the pixels (the name and the rows
    /// in them, the lit rule white).
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] BuildStates = { "47-build-one", "47-build-edited", "47-build-arriving", "47-build-held", "47-build-light", "47-build-light-after" };

        /// <summary>The arc's states: three files (two panels, the first a row), and a fourth that folds the older panel into the files so far, settled and caught mid-fold.</summary>
        private static readonly string[] BuildArcStates = { "47-build-three", "47-build-most", "47-build-folding", "47-build-settle", "47-build-after" };

        /// <summary>The kite game's files, in the order the agent writes them.</summary>
        private static readonly (string Name, string Folder, string[] Lines)[] BuildFiles =
        {
            ("kite.js", "src/game", new[] { "export class Kite {", "  constructor({ tail, lift }) {", "    this.tail = tail;", "  }", "}" }),
            ("follow.js", "src/game", new[] { "import { Kite } from './kite.js';", "", "export function follow(hand) {", "  kite.pullTowards(hand.position);", "}" }),
            ("score.js", "src/game", new[] { "let highest = 0;", "", "export function climb(height) {", "  highest = Math.max(highest, height);", "}" }),
            ("index.html", "public", new[] { "<canvas id=\"sky\"></canvas>", "<script type=\"module\" src=\"/src/main.js\"></script>" }),
        };

        private const string BuildListWords = WatchBuild.ListWords;

        private static WritingPanel BuildFilePanel(int index)
        {
            var (name, folder, lines) = BuildFiles[index];
            return new WritingPanel(folder + "/" + name, LineWords.Theirs(name), LineWords.Theirs(folder), WritingState.Changing,
                lines.SelectMany(RoomWritingPanel.Wrap).ToList(), new int[0], 120, isNew: true);
        }

        /// <summary>The arc after <paramref name="files"/> of the kite's files: the last three standing, every one named so far.</summary>
        internal static WritingArc BuildArcOf(int files)
        {
            var all = Enumerable.Range(0, files).Select(BuildFilePanel).ToList();
            return new WritingArc(all.Skip(System.Math.Max(0, files - WritingArc.MostPanels)).ToList(), all, files);
        }

        private static PartShot BuildArcShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var unit = RoomType.Units(1f);
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var arc = RoomWritingArc.Create(plane);
            var clock = 100f;
            arc.Clock = () => clock;
            var titleWas = RoomWritingArc.ListTitle;
            RoomWritingArc.ListTitle = _ => BuildListWords;
            try
            {
                // The files arrive one a second, as a turn writes them.
                for (var files = 1; files <= 3; files++)
                {
                    arc.Show(BuildArcOf(files));
                    clock += 1f;
                    arc.Settle();
                }
                var panelWidth = RoomType.Units(RoomGeometry.PanelDp);
                var gap = RoomType.Units(RoomWritingArc.GapDp);
                var folding = state == "47-build-folding";
                if (state == "47-build-most" || folding)
                {
                    var before = arc.Panels.Select(panel => arc.FlatOf(panel).x).ToList();
                    var oldest = arc.Panels[0];
                    arc.Show(BuildArcOf(4));
                    // 0.3 s in: the oldest folds toward the list, smaller and fainter; the others stand between their places.
                    clock += 0.3f;
                    arc.Show(BuildArcOf(4));
                    var moved = RoomMotion.Standard(0.3f / RoomWritingArc.MoveSeconds);
                    Require(oldest.gameObject.activeSelf && oldest.transform.localScale.x < 0.99f && oldest.transform.localScale.x > RoomWritingPanel.FoldTo,
                        "the oldest panel is not folding mid-way: its scale " + oldest.transform.localScale.x.ToString("0.00"));
                    var next = arc.Panels[0];
                    Require(arc.FlatOf(next).x < before[1] - unit && arc.FlatOf(next).x > before[0] - panelWidth,
                        "the second panel is not moving left: it stands at " + (arc.FlatOf(next).x / unit).ToString("0") + " dp, was " + (before[1] / unit).ToString("0"));
                    Require(moved > 0.05f && moved < 0.95f, "0.3 s is not mid-move on the standard curve: " + moved.ToString("0.00"));
                    if (!folding)
                    {
                        clock += RoomWritingArc.MoveSeconds;
                        arc.Show(BuildArcOf(4));
                        arc.Settle();
                        Require(!oldest.gameObject.activeSelf || arc.Panels.Contains(oldest), "the folded panel still stands once its row is shown");
                    }
                }
                // The arc's middle at the eyes' level, as XR stands it: its origin is its top.
                arc.transform.localPosition = new Vector3(0f, arc.Size.y / 2f, 0f);
                Place(plane, eyes, 0f, shot.Elevation);

                var settling = state is "47-build-settle" or "47-build-after";
                if (settling)
                {
                    // The turn ends with three files: the panels fold into the list right to left, the newest first, one after the other.
                    var newest = arc.Panels[arc.Panels.Count - 1];
                    var older = arc.Panels[0];
                    var takes = arc.FoldAll();
                    Require(Mathf.Abs(takes - (RoomWritingArc.MoveSeconds + RoomWritingArc.FoldStaggerSeconds)) < 1e-4f, "the settle says it takes " + takes.ToString("0.00") + " s");
                    clock += 0.2f;
                    arc.Show(new WritingArc(new WritingPanel[0], BuildArcOf(3).SoFar, 3));
                    Require(arc.Folding.Contains(newest) && newest.transform.localScale.x < 0.99f, "0.2 s into the settle, the newest panel is not folding first");
                    Require(arc.Folding.Contains(older) && older.transform.localScale.x > 0.999f, "0.2 s into the settle, the older panel already folds: they fold one after the other");
                    if (state == "47-build-after")
                    {
                        clock += takes + 0.1f;
                        arc.Show(new WritingArc(new WritingPanel[0], BuildArcOf(3).SoFar, 3));
                        Require(arc.Folding.Count == 0 && !newest.gameObject.activeSelf && !older.gameObject.activeSelf, "once settled, a panel still stands");
                        Require(arc.List.Count == 3, "once settled, the list holds " + arc.List.Count + " files, not the turn's 3");
                    }
                    else clock += 0.15f;
                    arc.Show(new WritingArc(new WritingPanel[0], BuildArcOf(3).SoFar, 3));
                }
                var final = settling ? new WritingArc(new WritingPanel[0], BuildArcOf(3).SoFar, 3) : BuildArcOf(state == "47-build-three" ? 3 : 4);
                var panels = arc.Panels;
                var drawn = RoomWritingArc.Drawn(final);
                Require(panels.Count == drawn.Count, "the arc stands " + panels.Count + " panels, not " + drawn.Count);
                // The list's title in the label role, as the panels' own label (the one language's f5).
                var listTitle = arc.ListTitleShown;
                if (arc.List.Count > 0)
                {
                    listTitle.ForceMeshUpdate(true);
                    Require(listTitle.gameObject.activeInHierarchy && listTitle.GetParsedText().Trim() == BuildListWords.ToUpperInvariant() && listTitle.characterSpacing > 20f && listTitle.alpha < 0.7f,
                        "the list's title is not the quiet spaced label " + BuildListWords.ToUpperInvariant() + ": " + listTitle.GetParsedText());
                }
                for (var index = 0; index < panels.Count && index < drawn.Count && !folding; index++)
                {
                    Require(panels[index].Path == drawn[index].Path, "panel " + index + " shows " + panels[index].Path + ", not " + drawn[index].Path);
                    Require(Mathf.Abs(panels[index].transform.localPosition.y + panels[index].Size.y / 2f) < unit * 0.05f, "panel " + index + "'s top is not the arc's");
                }
                // Two columns at the panel's width: the newest at the right, the one before at the left, the gap between.
                var width = 2f * panelWidth + gap;
                Require(Mathf.Abs(arc.Size.x - width) < unit * 0.05f, "the arc stands " + (arc.Size.x / unit).ToString("0") + " dp wide, not " + (width / unit).ToString("0"));
                Require(SpatialKit.Degrees(arc.Size.x / unit) <= 60f, "the arc spans " + SpatialKit.Degrees(arc.Size.x / unit).ToString("0.0") + " degrees, past the comfortable 60");
                if (panels.Count == 2 && !folding)
                {
                    Require(Mathf.Abs(arc.FlatOf(panels[1]).x - (width / 2f - panelWidth / 2f)) < unit * 0.05f, "the newest panel is not the right column");
                    Require(Mathf.Abs(arc.FlatOf(panels[0]).x + (width / 2f - panelWidth / 2f)) < unit * 0.05f, "the panel before is not the left column");
                }
                var rows = RoomWritingArc.ListRows(final);
                Require(rows.Count == final.SoFar.Count - drawn.Count, "the list holds " + rows.Count + " rows, not every file the panels do not");
                Require(arc.ListGlass.gameObject.activeSelf == rows.Count > 0, rows.Count > 0 ? "the files so far are not shown" : "an empty list stands");
                if (rows.Count > 0 && !folding)
                {
                    Require(arc.List.Count == rows.Count, "the list shows " + arc.List.Count + " rows, not " + rows.Count);
                    // Under the panel before, the gap between, in its column; with no panel standing (the turn settled), at the arc's top.
                    var listTop = arc.ListFlat.y + arc.ListSize.y / 2f;
                    if (panels.Count > 1)
                    {
                        var olderBottom = -panels[0].Size.y;
                        Require(Mathf.Abs(olderBottom - listTop - gap) < unit * 0.05f, "the list stands " + ((olderBottom - listTop) / unit).ToString("0.0") + " dp under the panel before");
                        Require(Mathf.Abs(arc.ListFlat.x - arc.FlatOf(panels[0]).x) < unit * 0.05f, "the list is not in the left column");
                    }
                    else if (panels.Count == 0) Require(Mathf.Abs(listTop) < unit * 0.05f, "with no panel standing, the list does not stand at the arc's top");
                }
                // On the bend every panel's middle stands as far from the eyes as the arc's, and faces them.
                var eyeAt = new Vector3(0f, 0f, -arc.Radius);
                foreach (var panel in panels)
                {
                    var away = panel.transform.localPosition - new Vector3(0f, panel.transform.localPosition.y, 0f) - eyeAt;
                    Require(Mathf.Abs(away.magnitude - arc.Radius) < 1e-4f, panel.Path + " stands " + away.magnitude.ToString("0.000") + " from the eyes, not " + arc.Radius.ToString("0.000"));
                    Require(Vector3.Angle(panel.transform.localRotation * Vector3.back, -away) < 0.5f, panel.Path + " does not face the eyes");
                }
                // Inside a Quest 3S's field less its margin with the head forward (AUDIT, 07:26Z): every corner of every glass.
                IEnumerable<Vector3> CornersOf(Transform glass, Vector2 size)
                {
                    foreach (var x in new[] { -0.5f, 0.5f })
                    foreach (var y in new[] { -0.5f, 0.5f }) yield return glass.TransformPoint(new Vector3(x * size.x, y * size.y, 0f));
                }
                var corners = panels.SelectMany(panel => CornersOf(panel.Glass.transform, panel.Size)).ToList();
                if (rows.Count > 0) corners.AddRange(CornersOf(arc.ListGlass.transform, arc.ListSize));
                var forward = (plane.position - eyes).normalized;
                shot.Failures.AddRange(FieldChecks.Inside("the arc", corners, eyes, forward, 0f, FieldChecks.Quest3S));
                var yaws = corners.Select(corner => Vector3.SignedAngle(new Vector3(forward.x, 0f, forward.z), new Vector3(corner.x - eyes.x, 0f, corner.z - eyes.z), Vector3.up)).ToList();
                // Every word on its own plate: the bend turns each panel whole, so no word stands off its glass (86e3d194f's rule).
                var mostOff = 0f;
                foreach (var panel in panels)
                foreach (var label in panel.GetComponentsInChildren<TMPro.TMP_Text>(false))
                {
                    var depth = -panel.Glass.transform.InverseTransformPoint(label.transform.position).z;
                    mostOff = Mathf.Max(mostOff, Mathf.Abs(depth));
                    Require(depth >= -unit * 0.05f && depth <= unit * 2f, panel.Path + "'s \"" + label.text + "\" stands " + (depth / unit).ToString("0.00") + " dp off its glass");
                }
                shot.Read = (camera, eye, white, dark, bright, failures) =>
                {
                    Debug.Log("Halcyonic: parts: " + name + ": " + (arc.Size.x / unit).ToString("0", CultureInfo.InvariantCulture) + "x" + (arc.Size.y / unit).ToString("0", CultureInfo.InvariantCulture)
                        + " dp, " + panels.Count + " panels, " + rows.Count + " rows so far, across " + yaws.Min().ToString("0.0", CultureInfo.InvariantCulture) + " to "
                        + yaws.Max().ToString("0.0", CultureInfo.InvariantCulture) + " degrees (the field " + (FieldChecks.Quest3S.Left - ViewField.EdgeMarginDegrees).ToString("0.0", CultureInfo.InvariantCulture)
                        + " either side), every word within " + (mostOff / unit).ToString("0.00", CultureInfo.InvariantCulture) + " dp of its glass.");
                };
            }
            finally { RoomWritingArc.ListTitle = titleWas; }
            return shot;
        }

        /// <summary>The file a person's kite game is written into, as the agent writes it.</summary>
        private static readonly string[] BuildFile =
        {
            "import { Kite } from './kite.js';",
            "",
            "const kite = new Kite({ tail: 12, lift: 0.8 });",
            "let highest = 0;",
            "",
            "export function follow(hand) {",
            "  kite.pullTowards(hand.position);",
            "  highest = Math.max(highest, kite.height);",
            "}",
        };

        /// <summary>The same file a read later: the follow body rewritten, three rows changed.</summary>
        private static readonly string[] BuildFileEdited =
        {
            "import { Kite } from './kite.js';",
            "",
            "const kite = new Kite({ tail: 12, lift: 0.8 });",
            "let highest = 0, fell = false;",
            "",
            "export function follow(hand) {",
            "  kite.pullTowards(hand.position, { ease: 0.2 });",
            "  if (kite.height < 0.1) fell = true;",
            "  highest = fell ? kite.height : Math.max(highest, kite.height);",
            "}",
        };


        private static WritingPanel BuildPanel(IReadOnlyList<string> file, WritingState state = WritingState.Changing, IReadOnlyList<int>? lit = null)
        {
            var rows = file.SelectMany(RoomWritingPanel.Wrap).ToList();
            return new WritingPanel("src/game/follow.js", LineWords.Theirs("follow.js"), LineWords.Theirs("src/game"), state,
                state == WritingState.Changing || state == WritingState.Settled ? rows : new string[0], lit ?? new int[0], 812, isNew: true);
        }

        private static PartShot BuildShot(string state, Transform root, Vector3 eyes, string name)
        {
            var shot = new PartShot { Elevation = 0f };
            void Require(bool condition, string why) { if (!condition) shot.Failures.Add(why); }
            var unit = RoomType.Units(1f);
            var plane = new GameObject("Plane").transform;
            plane.SetParent(root, false);
            var view = RoomWritingPanel.Create(plane);
            var clock = 100f;
            view.Clock = () => clock;
            var notShownWas = RoomWritingPanel.NotShownWords;
            RoomWritingPanel.NotShownWords = RoomWritingPanel.NotShownOf;
            try
            {
                WritingPanel shown;
                IReadOnlyList<int> lit = new int[0];
                if (state == "47-build-edited" || state.StartsWith("47-build-light", System.StringComparison.Ordinal))
                {
                    view.Show(BuildPanel(BuildFile));
                    view.Settle();
                    clock += 2f;
                    var before = BuildPanel(BuildFile).Lines;
                    var after = BuildPanel(BuildFileEdited).Lines;
                    var first = 0;
                    while (first < before.Count && first < after.Count && before[first] == after[first]) first++;
                    lit = Enumerable.Range(first, after.Count - first).Where(row => row >= before.Count || before[row] != after[row]).ToList();
                    shown = BuildPanel(BuildFileEdited, lit: lit);
                    view.Show(shown);
                    // 0.1 s into the pour: rows before the change stand whole, the first new row most of the way in, a later one not yet.
                    clock += 0.1f;
                    Require(Mathf.Approximately(view.RowShown(first - 1), 1f), "a row before the change is not whole mid-pour: " + view.RowShown(first - 1).ToString("0.00"));
                    var firstIn = Mathf.Clamp01(0.1f / RoomWritingPanel.RowFadeSeconds);
                    Require(Mathf.Abs(view.RowShown(first) - firstIn) < 0.01f, "the first changed row stands " + view.RowShown(first).ToString("0.00") + " in, not " + firstIn.ToString("0.00"));
                    Require(view.RowShown(first + 2) < 0.01f, "a row two after the change is already in: " + view.RowShown(first + 2).ToString("0.00"));
                    Require(lit.Count >= 3, "the edit lit " + lit.Count + " rows, not three or more");
                    if (state.StartsWith("47-build-light", System.StringComparison.Ordinal))
                    {
                        // The light, frame by frame (AUDIT's raised bar): 0.1 s in, the first new row's light has run most of
                        // the way under its words at full strength and its words stand between the accent and their own white; a
                        // row whose turn has not come shows none. Its light gone and its words their own by 0.6 s.
                        var own = Color.white;
                        view.Show(shown);
                        var (across, strength) = view.LightAt(first);
                        Require(Mathf.Abs(across - RoomMotion.Standard(0.1f / RoomWritingPanel.LightAcrossSeconds)) < 0.01f && strength > 0.99f,
                            "0.1 s in, the first new row's light stands " + across.ToString("0.00") + " across at " + strength.ToString("0.00"));
                        var light = view.LightOf(first);
                        // Only the travelling segment, never a full line under the words (AUDIT's f5 verdict), and in the gap under the row.
                        Require(light.HasValue && Mathf.Abs(light.Value.Light.Size.x - Mathf.Min(light.Value.Width * across, RoomType.Scaled(RoomWritingPanel.LightSegmentDp))) < unit * 0.5f && light.Value.Light.Fill.a > RoomWritingPanel.LightPeak * 0.75f,
                            "0.1 s in, the first new row shows no light under " + (across * 100f).ToString("0") + " percent of its words");
                        var firstLabel = view.Card.ReadingShown[shown.Lines.Take(first).Count(line => line.Trim(' ').Length > 0)];
                        firstLabel.ForceMeshUpdate(true);
                        var lightY = view.Card.transform.InverseTransformPoint(light!.Value.Light.transform.position).y;
                        var wordsFoot = view.Card.transform.InverseTransformPoint(firstLabel.transform.TransformPoint(new Vector3(0f, firstLabel.textInfo.lineInfo[0].descender, 0f))).y;
                        Require(lightY < wordsFoot - RoomType.Scaled(RoomWritingPanel.LightLineDp), "the light runs " + ((wordsFoot - lightY) / unit).ToString("0.0") + " dp under its words, not in the gap below them");
                        // Its words arrive lightly tinted, light enough to hold their contrast over the tint (AUDIT's 07:54Z point 2).
                        // White now, never the accent (AUDIT's Watch It Work pair, 2026-11-17): the one orange stays on the main action.
                        Require(firstLabel.color.r > 0.7f && Mathf.Abs(firstLabel.color.b - firstLabel.color.r) < 0.08f, "0.1 s in, the first new row's words are not near white: " + firstLabel.color);
                        // A changed row's tint comes with its words (point 1): no run's tint stands taller than its rows shown so far.
                        var pitch = RoomType.Scaled(RoomReading.LineDp);
                        var runsLit = lit.OrderBy(row => row).Aggregate(new List<(int First, int Count)>(), (all, row) =>
                        {
                            if (all.Count > 0 && all[all.Count - 1].First + all[all.Count - 1].Count == row) all[all.Count - 1] = (all[all.Count - 1].First, all[all.Count - 1].Count + 1);
                            else all.Add((row, 1));
                            return all;
                        });
                        var bandsNow = view.Card.Plates.Where(each => each.What == "reading lit band").Select(each => each.Plate).ToList();
                        for (var run = 0; run < runsLit.Count && run < bandsNow.Count; run++)
                        {
                            var rowsIn = Enumerable.Range(runsLit[run].First, runsLit[run].Count).Sum(row => view.RowShown(row));
                            var standing = bandsNow[run].gameObject.activeSelf ? bandsNow[run].Size.y : 0f;
                            Require(standing <= rowsIn * pitch + unit * 0.05f, "0.1 s in, changed run " + run + "'s tint stands " + (standing / unit).ToString("0.0") + " dp, its rows shown " + (rowsIn * pitch / unit).ToString("0.0"));
                        }
                        // Nothing before its turn: a row three after the first has no light yet.
                        Require(view.LightAt(first + 3).Strength == 0f && view.LightOf(first + 3) == null, "0.1 s in, a row whose turn has not come shows light");
                        Require(view.LightAt(first - 1).Strength == 0f && view.LightOf(first - 1) == null, "a row shown before this read carries light");
                        clock += 0.5f + 0.2f;
                        view.Show(shown);
                        Require(view.LightAt(first).Strength < 0.001f && view.LightOf(first) == null, "0.7 s in, the first new row's light still stands");
                        Require(Mathf.Abs(firstLabel.color.r - own.r) < 0.01f && Mathf.Abs(firstLabel.color.g - own.g) < 0.01f, "0.7 s in, the first new row's words are not their own colour: " + firstLabel.color);
                        // The frame drawn: back to 0.12 s in, the light running; or, after, 0.8 s in, the light gone, the pour still landing.
                        if (state == "47-build-light")
                        {
                            clock -= 0.5f + 0.2f - 0.02f;
                            view.Show(shown);
                        }
                    }
                    else
                    {
                        clock += RoomWritingPanel.RevealMostSeconds + RoomWritingPanel.RowFadeSeconds;
                        view.Show(shown);
                    }
                }
                else
                {
                    shown = BuildPanel(BuildFile, state == "47-build-held" ? WritingState.KeptPrivate : WritingState.Changing);
                    view.Show(shown);
                    if (state == "47-build-arriving")
                    {
                        // 0.2 s in: the panel stands between 0.92 and its size, its glass and its words faded in together.
                        clock += 0.2f;
                        view.Show(shown);
                        var arrived = RoomMotion.Standard(0.2f / RoomWritingPanel.ArriveSeconds);
                        var scale = view.transform.localScale.x;
                        Require(Mathf.Abs(scale - Mathf.Lerp(RoomWritingPanel.ArriveFrom, 1f, arrived)) < 0.001f && scale > RoomWritingPanel.ArriveFrom && scale < 1f,
                            "the arriving panel stands at " + scale.ToString("0.000") + " of its size");
                        Require(Mathf.Abs(view.Glass.Shown - arrived) < 0.001f, "the glass stands " + view.Glass.Shown.ToString("0.00") + " in, the words " + arrived.ToString("0.00"));
                        var title = view.Head.Title;
                        Require(title != null && title.alpha < 0.99f && title.alpha > 0.01f, "the name is not fading in with its glass: " + (title?.alpha.ToString("0.00") ?? "none"));
                    }
                    else view.Settle();
                }
                Place(plane, eyes, 0f, shot.Elevation);

                // The panel's width and its head as the one language's sheet sets it (f5): the step's verb as a quiet label in
                // spaced capitals, then the file's path whole as the title, in its own case in the sans face; the code under them.
                Require(Mathf.Abs(view.Size.x - RoomType.Units(RoomGeometry.PanelDp)) < unit * 0.05f, "the panel stands " + (view.Size.x / unit).ToString("0.0") + " dp wide");
                var labels = view.Card.Shown;
                var (headLabel, nameLabel) = view.Head;
                headLabel.ForceMeshUpdate(true);
                nameLabel.ForceMeshUpdate(true);
                Require(headLabel.gameObject.activeInHierarchy && headLabel.GetParsedText().Trim() == RoomWritingPanel.LabelWords.ToUpperInvariant(), "the label does not read " + RoomWritingPanel.LabelWords + " in capitals: " + headLabel.GetParsedText());
                Require(headLabel.characterSpacing > 20f && headLabel.color.a < 0.7f, "the label is not quiet and spaced wide");
                Require(nameLabel.gameObject.activeInHierarchy && nameLabel.GetParsedText().Replace("\n", "").Trim() == shown.Path && !nameLabel.isTextOverflowing, "the file's path is not the title whole: " + nameLabel.GetParsedText());
                Require(nameLabel.font != null && !nameLabel.font.name.Contains("Mono") && (nameLabel.fontStyle & FontStyles.UpperCase) == 0, "the title is not in the sans face in its own case: " + nameLabel.font?.name);
                Require(!labels.Any(each => each.GetParsedText().Trim() == shown.Folder!.Text || each.GetParsedText().Trim() == shown.Name.Text), "the file's name or folder is shown again in the card");
                var headRight = Mathf.Max(headLabel.textBounds.max.x, nameLabel.textBounds.max.x);
                Require(view.transform.InverseTransformPoint(nameLabel.transform.TransformPoint(new Vector3(headRight, 0f, 0f))).x < view.Size.x / 2f - RoomType.Scaled(RoomGeometry.PanelPadDp) * 0.5f, "the head runs past the glass's padding");
                Require(view.GetComponentsInChildren<Collider>(true).Length == 0 && view.GetComponentsInChildren<KitPress>(true).Length == 0, "the panel holds a part that takes a press");
                var rows = view.Card.ReadingShown;
                if (state == "47-build-held")
                {
                    Require(rows.Count == 0, "a held file shows " + rows.Count + " rows of its text");
                    // The product's own words, the Changes reading's, title and line.
                    Require(labels.Any(each => each.GetParsedText().Trim() == FileChangeText.KeptPrivate), "a held file does not say why it shows no text");
                    Require(labels.Any(each => string.Join(" ", each.GetParsedText().Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries)) == FileChangeText.KeptPrivateLine), "a held file does not say its line");
                }
                else
                {
                    var texts = shown.Lines.Select(line => line.Trim(' ')).Where(line => line.Length > 0).ToList();
                    Require(rows.Count == texts.Count, "the panel shows " + rows.Count + " rows, the file has " + texts.Count);
                    for (var index = 0; index < rows.Count && index < texts.Count; index++)
                    {
                        rows[index].ForceMeshUpdate(true);
                        Require(!rows[index].isTextOverflowing && !rows[index].isTextTruncated && rows[index].GetParsedText().TrimEnd(' ') == texts[index], "row " + index + " is not whole: \"" + rows[index].GetParsedText() + "\"");
                        // A read that changed some lines leaves the others quiet (RoomWritingPanel.QuietRow); none falls below that.
                        var floor = (shown.Lit?.Count ?? 0) > 0 ? 0.79f * RoomWritingPanel.QuietRow : 0.79f;
                        if (state != "47-build-arriving" && !state.StartsWith("47-build-light", System.StringComparison.Ordinal)) Require(rows[index].alpha > floor, "settled, row " + index + " stands at " + rows[index].alpha.ToString("0.00"));
                    }
                    // And the changed lines stand whole, brighter than the quiet ones, so what changed reads at a glance.
                    if ((shown.Lit?.Count ?? 0) > 0 && state != "47-build-arriving" && !state.StartsWith("47-build-light", System.StringComparison.Ordinal))
                        Require(rows.Any(each => each.alpha > 0.79f), "no changed line stands whole over the quiet ones");
                    // Each row keeps its line's indent: a row the file indents stands right of one it does not, a continued row further.
                    var lefts = shown.Lines.Where(line => line.Trim(' ').Length > 0).Select(line => line.Length - line.TrimStart(' ').Length).ToList();
                    for (var index = 1; index < rows.Count && index < lefts.Count; index++)
                    {
                        var moved = rows[index].transform.localPosition.x - rows[0].transform.localPosition.x;
                        Require(lefts[index] > lefts[0] ? moved > unit : Mathf.Abs(moved) < unit * 0.05f, "row " + index + " stands " + (moved / unit).ToString("0.0") + " dp from the first, its indent " + lefts[index]);
                    }
                }
                var bands = view.Card.Plates.Where(each => each.What == "reading lit band").ToList();
                var rules = view.Card.Plates.Where(each => each.What == "reading lit rule").ToList();
                if (state == "47-build-edited" || state.StartsWith("47-build-light", System.StringComparison.Ordinal)) Require(bands.Count > 0 && rules.Count == bands.Count, "the changed rows are not lit: " + bands.Count + " bands, " + rules.Count + " rules");
                else Require(bands.Count == 0, "rows no read changed are lit");
                // Each rule stands clear of the words, left of the first letter, and inside the glass.
                if (rows.Count > 0 && rules.Count > 0)
                {
                    var wordsLeft = rows.Min(row => { row.ForceMeshUpdate(true); return view.transform.InverseTransformPoint(row.transform.TransformPoint(new Vector3(row.textBounds.min.x, 0f, 0f))).x; });
                    var glassLeft = -view.Size.x / 2f;
                    foreach (var rule in rules)
                    {
                        var ruleRight = view.transform.InverseTransformPoint(rule.Plate.transform.TransformPoint(new Vector3(rule.Plate.Size.x / 2f, 0f, 0f))).x;
                        var ruleLeft = view.transform.InverseTransformPoint(rule.Plate.transform.TransformPoint(new Vector3(-rule.Plate.Size.x / 2f, 0f, 0f))).x;
                        Require(ruleRight < wordsLeft - unit * 4f, "a lit rule stands " + ((wordsLeft - ruleRight) / unit).ToString("0.0") + " dp from the words, on or against them");
                        Require(ruleLeft > glassLeft + unit * 8f, "a lit rule stands outside the glass's padding");
                    }
                }

                shot.Read = (camera, eye, white, dark, bright, failures) =>
                {
                    Color32 At(Color32[] pixels, Vector3 world)
                    {
                        var screen = camera.WorldToScreenPoint(world);
                        return pixels[Mathf.Clamp(Mathf.FloorToInt(screen.y), 0, Height - 1) * Width + Mathf.Clamp(Mathf.FloorToInt(screen.x), 0, Width - 1)];
                    }
                    int Light(Color32 pixel) => (pixel.r * 2126 + pixel.g * 7152 + pixel.b * 722) / 10000;
                    int Lightest(TMPro.TextMeshPro label)
                    {
                        var bounds = label.textBounds;
                        var most = 0;
                        for (var step = 0; step <= 60; step++)
                            most = Mathf.Max(most, Light(At(dark, label.transform.TransformPoint(new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, step / 60f), bounds.max.y - KitType.LinePitch(label) * 0.5f, 0f)))));
                        return most;
                    }
                    var nameLight = nameLabel != null ? Lightest(nameLabel) : 0;
                    if (state != "47-build-arriving" && nameLight < 160) failures.Add("the file's name is not in the pixels, the lightest reads " + nameLight + " of 255");
                    var rowLight = view.Card.ReadingShown.Select(Lightest).ToList();
                    // The levels AUDIT set on f5 (2026-11-17T01:59Z), in the pixels on the glass: the label's cores 135 to 150, the
                    // unchanged code about 140 to 150, the changed code bright; each held with a little room for the frame's grain.
                    var labelLight = Lightest(view.Head.Label);
                    var settledRead = state != "47-build-arriving" && !Moving(state);
                    if (settledRead && (labelLight < 125 || labelLight > 165)) failures.Add("the label's cores read " + labelLight + ", not 135 to 150");
                    var quietLight = view.Card.ReadingShown.Where(row => row.alpha < 0.79f).Select(Lightest).ToList();
                    var litLight = view.Card.ReadingShown.Where(row => row.alpha >= 0.79f).Select(Lightest).ToList();
                    if (settledRead && (shown.Lit?.Count ?? 0) > 0)
                    {
                        if (quietLight.Count == 0 || quietLight.Any(value => value < 125 || value > 165)) failures.Add("the unchanged code reads " + string.Join(", ", quietLight) + ", not about 140 to 150");
                        if (litLight.Count == 0 || litLight.Any(value => value < 215)) failures.Add("the changed code reads " + string.Join(", ", litLight) + ", not bright");
                    }
                    // A read that changed some lines leaves the others quiet (RoomWritingPanel.QuietRow), still plainly there.
                    var rowFloor = bands.Count > 0 ? 100 : 140;
                    if (state != "47-build-arriving" && !state.StartsWith("47-build-light", System.StringComparison.Ordinal) && rowLight.Any(value => value < rowFloor)) failures.Add("a row is not in the pixels: " + string.Join(", ", rowLight));
                    // The code stands on the glass: no well under it.
                    if (view.Card.Plates.Any(each => each.What == "reading well" && each.Plate.gameObject.activeInHierarchy && each.Plate.Fill.a > 0.01f)) failures.Add("a well stands under the code");
                    var ruleNote = "";
                    if (rules.Count > 0)
                    {
                        var pixel = At(dark, rules[0].Plate.transform.TransformPoint(Vector3.zero));
                        ruleNote = ", the lit rule " + pixel;
                        if (!(pixel.r > 190 && pixel.g > 190 && pixel.b > 190)) failures.Add("the lit rule is not the white hairline: " + pixel);
                    }
                    Debug.Log("Halcyonic: parts: " + name + ": " + (view.Size.x / unit).ToString("0", CultureInfo.InvariantCulture) + "x" + (view.Size.y / unit).ToString("0", CultureInfo.InvariantCulture)
                        + " dp, " + view.Card.ReadingShown.Count + " rows, " + bands.Count + " lit runs, the name's lightest " + nameLight + ", the label's " + labelLight + ", the rows' " + string.Join(", ", rowLight) + ruleNote + ".");
                };
            }
            finally { RoomWritingPanel.NotShownWords = notShownWas; }
            return shot;
        }
    }
}
