#nullable enable
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    /// <summary>
    /// One file's panel in Watch It Work (<see cref="RoomWritingPanel"/>): a file at its first read, settled; the same file read
    /// again with three rows changed, caught mid-pour and lit; a file arriving, caught mid-rise; and a file whose text is not
    /// shown. Each is measured (the panel the panel's width, the name whole in the mono face, every row whole at its own alpha
    /// once settled, rows before the change shown while the new ones pour in by their turn, the changed rows lit with the one
    /// accent, the arrival rising from 0.92 with its glass and words together, a held file saying why with no rows, nothing
    /// that takes a press) and read in the pixels (the name and the rows in them, the lit rule in the accent).
    /// </summary>
    public static partial class KitRender
    {
        private static readonly string[] BuildStates = { "47-build-one", "47-build-edited", "47-build-arriving", "47-build-held" };

        /// <summary>The arc's states: three files standing, and a fourth that folds the oldest into the files so far, caught mid-move.</summary>
        private static readonly string[] BuildArcStates = { "47-build-three", "47-build-most" };

        /// <summary>The kite game's files, in the order the agent writes them.</summary>
        private static readonly (string Name, string Folder, string[] Lines)[] BuildFiles =
        {
            ("kite.js", "src/game", new[] { "export class Kite {", "  constructor({ tail, lift }) {", "    this.tail = tail;", "  }", "}" }),
            ("follow.js", "src/game", new[] { "import { Kite } from './kite.js';", "", "export function follow(hand) {", "  kite.pullTowards(hand.position);", "}" }),
            ("score.js", "src/game", new[] { "let highest = 0;", "", "export function climb(height) {", "  highest = Math.max(highest, height);", "}" }),
            ("index.html", "public", new[] { "<canvas id=\"sky\"></canvas>", "<script type=\"module\" src=\"/src/main.js\"></script>" }),
        };

        private const string BuildListWords = "Files So Far";

        private static WritingPanel BuildFilePanel(int index)
        {
            var (name, folder, lines) = BuildFiles[index];
            return new WritingPanel(folder + "/" + name, LineWords.Theirs(name), LineWords.Theirs(folder), WritingState.Changing,
                lines.SelectMany(RoomWritingPanel.Wrap).ToList(), new int[0], 120, isNew: true);
        }

        /// <summary>The arc after <paramref name="files"/> of the kite's files: the last three standing, every one named so far.</summary>
        private static WritingArc BuildArcOf(int files)
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
                if (state == "47-build-most")
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
                    clock += RoomWritingArc.MoveSeconds;
                    arc.Show(BuildArcOf(4));
                    arc.Settle();
                    Require(!oldest.gameObject.activeSelf || arc.Panels.Contains(oldest), "the folded panel still stands once its row is shown");
                }
                // The arc's middle at the eyes' level, as XR stands it: its origin is its top.
                arc.transform.localPosition = new Vector3(0f, arc.Size.y / 2f, 0f);
                Place(plane, eyes, 0f, shot.Elevation);

                var final = BuildArcOf(state == "47-build-most" ? 4 : 3);
                var panels = arc.Panels;
                Require(panels.Count == final.Panels.Count, "the arc stands " + panels.Count + " panels, not " + final.Panels.Count);
                for (var index = 0; index < panels.Count; index++)
                {
                    Require(panels[index].Path == final.Panels[index].Path, "panel " + index + " shows " + panels[index].Path + ", not " + final.Panels[index].Path);
                    Require(Mathf.Abs(panels[index].transform.localPosition.y + panels[index].Size.y / 2f) < unit * 0.05f, "panel " + index + "'s top is not the arc's");
                    if (index == 0) continue;
                    var apart = arc.FlatOf(panels[index]).x - arc.FlatOf(panels[index - 1]).x;
                    Require(Mathf.Abs(apart - panelWidth - gap) < unit * 0.05f, "panel " + index + " stands " + ((apart - panelWidth) / unit).ToString("0.0") + " dp after the one before, not " + RoomWritingArc.GapDp.ToString("0"));
                }
                var rows = RoomWritingArc.ListRows(final);
                Require(arc.ListGlass.gameObject.activeSelf == rows.Count > 0, rows.Count > 0 ? "the files so far are not shown" : "an empty list stands");
                if (rows.Count > 0)
                {
                    Require(arc.List.Count == rows.Count, "the list shows " + arc.List.Count + " rows, not " + rows.Count);
                    var listRight = arc.ListFlat.x + RoomType.Units(RoomWritingArc.ListDp) / 2f;
                    var firstLeft = arc.FlatOf(panels[0]).x - panelWidth / 2f;
                    Require(Mathf.Abs(firstLeft - listRight - gap) < unit * 0.05f, "the list stands " + ((firstLeft - listRight) / unit).ToString("0.0") + " dp before the first panel");
                }
                // On the bend every panel's middle stands as far from the eyes as the arc's, and faces them.
                var eyeAt = new Vector3(0f, 0f, -arc.Radius);
                foreach (var panel in panels)
                {
                    var away = panel.transform.localPosition - new Vector3(0f, panel.transform.localPosition.y, 0f) - eyeAt;
                    Require(Mathf.Abs(away.magnitude - arc.Radius) < 1e-4f, panel.Path + " stands " + away.magnitude.ToString("0.000") + " from the eyes, not " + arc.Radius.ToString("0.000"));
                    Require(Vector3.Angle(panel.transform.localRotation * Vector3.back, -away) < 0.5f, panel.Path + " does not face the eyes");
                }
                Require(Mathf.Abs(arc.Size.x - (panels.Count * panelWidth + (panels.Count - 1) * gap + (rows.Count > 0 ? RoomType.Units(RoomWritingArc.ListDp) + gap : 0f))) < unit * 0.05f,
                    "the arc says it stands " + (arc.Size.x / unit).ToString("0") + " dp wide");
                shot.Read = (camera, eye, white, dark, bright, failures) =>
                {
                    Debug.Log("Halcyonic: parts: " + name + ": " + (arc.Size.x / unit).ToString("0", CultureInfo.InvariantCulture) + "x" + (arc.Size.y / unit).ToString("0", CultureInfo.InvariantCulture)
                        + " dp, " + panels.Count + " panels, " + rows.Count + " rows so far.");
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

        private const string BuildHeldWords = "Kept Private";

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
            RoomWritingPanel.NotShownWords = panel => panel.State == WritingState.KeptPrivate ? BuildHeldWords : null;
            try
            {
                WritingPanel shown;
                IReadOnlyList<int> lit = new int[0];
                if (state == "47-build-edited")
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
                    clock += RoomWritingPanel.RevealMostSeconds + RoomWritingPanel.RowFadeSeconds;
                    view.Show(shown);
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
                        var title = view.Card.Shown.FirstOrDefault();
                        Require(title != null && title.alpha < 0.99f && title.alpha > 0.01f, "the name is not fading in with its glass: " + (title?.alpha.ToString("0.00") ?? "none"));
                    }
                    else view.Settle();
                }
                Place(plane, eyes, 0f, shot.Elevation);

                // The panel's width, its name whole in the mono face, its folder under it.
                Require(Mathf.Abs(view.Size.x - RoomType.Units(RoomGeometry.PanelDp)) < unit * 0.05f, "the panel stands " + (view.Size.x / unit).ToString("0.0") + " dp wide");
                var labels = view.Card.Shown;
                var nameLabel = labels.FirstOrDefault(each => each.GetParsedText().Trim() == shown.Name.Text);
                Require(nameLabel != null && !nameLabel.isTextOverflowing, "the file's name is not shown whole");
                Require(labels.Any(each => each.GetParsedText().Trim() == shown.Folder!.Text), "the file's folder is not shown");
                Require(view.GetComponentsInChildren<Collider>(true).Length == 0 && view.GetComponentsInChildren<KitPress>(true).Length == 0, "the panel holds a part that takes a press");
                var rows = view.Card.ReadingShown;
                if (state == "47-build-held")
                {
                    Require(rows.Count == 0, "a held file shows " + rows.Count + " rows of its text");
                    Require(labels.Any(each => each.GetParsedText().Trim() == BuildHeldWords), "a held file does not say why it shows no text");
                }
                else
                {
                    var texts = shown.Lines.Select(line => line.Trim(' ')).Where(line => line.Length > 0).ToList();
                    Require(rows.Count == texts.Count, "the panel shows " + rows.Count + " rows, the file has " + texts.Count);
                    for (var index = 0; index < rows.Count && index < texts.Count; index++)
                    {
                        rows[index].ForceMeshUpdate(true);
                        Require(!rows[index].isTextOverflowing && !rows[index].isTextTruncated && rows[index].GetParsedText().TrimEnd(' ') == texts[index], "row " + index + " is not whole: \"" + rows[index].GetParsedText() + "\"");
                        if (state != "47-build-arriving") Require(rows[index].alpha > 0.79f, "settled, row " + index + " stands at " + rows[index].alpha.ToString("0.00"));
                    }
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
                if (state == "47-build-edited") Require(bands.Count > 0 && rules.Count == bands.Count, "the changed rows are not lit: " + bands.Count + " bands, " + rules.Count + " rules");
                else Require(bands.Count == 0, "rows no read changed are lit");

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
                    if (state != "47-build-arriving" && rowLight.Any(value => value < 140)) failures.Add("a row is not in the pixels: " + string.Join(", ", rowLight));
                    var ruleNote = "";
                    if (rules.Count > 0)
                    {
                        var pixel = At(dark, rules[0].Plate.transform.TransformPoint(Vector3.zero));
                        ruleNote = ", the lit rule " + pixel;
                        if (!(pixel.b > pixel.r + 60 && pixel.g > pixel.r + 40)) failures.Add("the lit rule is not in the accent: " + pixel);
                    }
                    Debug.Log("Halcyonic: parts: " + name + ": " + (view.Size.x / unit).ToString("0", CultureInfo.InvariantCulture) + "x" + (view.Size.y / unit).ToString("0", CultureInfo.InvariantCulture)
                        + " dp, " + view.Card.ReadingShown.Count + " rows, " + bands.Count + " lit runs, the name's lightest " + nameLight + ", the rows' " + string.Join(", ", rowLight) + ruleNote + ".");
                };
            }
            finally { RoomWritingPanel.NotShownWords = notShownWas; }
            return shot;
        }
    }
}
