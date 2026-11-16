#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Watch It Work's arc (FIELD's SHAPE, "Several files", amended 2026-11-16T0730Z; XR places it on the plane), bent round
    /// the eyes so every panel faces them and no word is read at a slant: two columns at the panel's width, inside the
    /// comfortable field. The newest file stands at the right; the one before it at the top of the left column, and under it
    /// the turn's files so far as the rows draw them (<see cref="RoomRowsView"/>: each file's name with its check), in the
    /// result's Changes order, so nothing is ever stacked over text. A new file arrives at the right while the one there
    /// moves to the left column over the standard curve's 600 ms, and the one that stood there folds into its row: it moves
    /// to the list, its code gone first, shrinking and fading as its row stands. Under Still panels take their places at
    /// once and fade. A single file stands alone, centred. Its origin is the arc's top middle.
    /// </summary>
    public sealed class RoomWritingArc : MonoBehaviour
    {
        /// <summary>The gap between the panels, and between the list and the first panel.</summary>
        public const float GapDp = 24f;

        /// <summary>
        /// The most panels it draws at once: two columns, 904 dp, 56.5 degrees, inside the comfortable field (three side by
        /// side came to 103 degrees). A third file the turn hands it stands as its row.
        /// </summary>
        public const int MostShown = 2;

        /// <summary>How long a panel takes to move one place, or to fold into its row.</summary>
        public const float MoveSeconds = 0.6f;

        /// <summary>
        /// The radius the arc bends on, in its own units: 1 where its parent stands at the plane's distance scaled by it (as the
        /// plane does), so each panel's middle is as far from the eyes as the arc's middle; 0 lays it flat.
        /// </summary>
        public float Radius { get; set; } = 1f;

        private readonly Dictionary<string, Placed> standing = new Dictionary<string, Placed>();
        private readonly List<RoomWritingPanel> spare = new List<RoomWritingPanel>();
        private readonly List<Placed> folding = new List<Placed>();
        private RoomSurface listGlass = null!;
        private RoomRowsView list = null!;
        private TMPro.TextMeshPro listTitle = null!;

        private sealed class Placed
        {
            public RoomWritingPanel Panel = null!;
            public Vector3 From;
            public Vector3 To;
            public float MovedAt;
            public Vector3 Now;
            public bool Fresh;
        }

        /// <summary>The clock the arc moves by, its panels' too; renders fix it.</summary>
        public Func<float> Clock { get; set; } = () => Time.unscaledTime;

        /// <summary>The list's title, from the product's words (AUDIT2's); null shows the list with no title.</summary>
        public static Func<WritingArc, string?> ListTitle { get; set; } = _ => null;

        /// <summary>How wide and tall the arc stands as last laid.</summary>
        public Vector2 Size { get; private set; }

        /// <summary>The panels standing now, left to right; and the list, for the checks.</summary>
        public IReadOnlyList<RoomWritingPanel> Panels { get; private set; } = Array.Empty<RoomWritingPanel>();

        public RoomRowsView List => list;

        public RoomSurface ListGlass => listGlass;

        /// <summary>Where <paramref name="panel"/> stands along the arc, as laid flat before the bend: for the checks.</summary>
        public Vector3 FlatOf(RoomWritingPanel panel) => standing.Values.Concat(folding).FirstOrDefault(at => at.Panel == panel)?.Now ?? Vector3.zero;

        /// <summary>Where the list stands along the arc, as laid flat.</summary>
        public Vector3 ListFlat { get; private set; }

        /// <summary>Stands <paramref name="part"/> at <paramref name="flat"/> along the arc: on the bend, turned to face the eyes.</summary>
        private void Put(Transform part, Vector3 flat)
        {
            if (Radius <= 0f)
            {
                part.localPosition = flat;
                part.localRotation = Quaternion.identity;
                return;
            }
            var angle = flat.x / Radius;
            part.localPosition = new Vector3(Mathf.Sin(angle) * Radius, flat.y, (Mathf.Cos(angle) - 1f) * Radius + flat.z);
            part.localRotation = Quaternion.Euler(0f, angle * Mathf.Rad2Deg, 0f);
        }

        public static RoomWritingArc Create(Transform parent)
        {
            var go = new GameObject("Writing arc");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<RoomWritingArc>();
            view.listGlass = RoomSurface.Create(go.transform, "Files so far", RoomSurfaceKind.Information, KitType.Order.Plate);
            view.list = RoomRowsView.Create(view.listGlass.transform);
            view.listTitle = RoomType.Create(view.listGlass.transform, "Files so far title", Color.white, KitType.Order.Words);
            view.listGlass.gameObject.SetActive(false);
            go.SetActive(false);
            return view;
        }

        /// <summary>The panels the arc draws: the last two the turn hands it, the older left, the newest right.</summary>
        public static IReadOnlyList<WritingPanel> Drawn(WritingArc arc) => arc.Panels.Skip(Math.Max(0, arc.Panels.Count - MostShown)).ToList();

        /// <summary>The rows of the files so far that stand in no panel drawn, in the result's order, each with its check.</summary>
        public static IReadOnlyList<RoomRow> ListRows(WritingArc arc)
        {
            var paths = new HashSet<string>(Drawn(arc).Select(panel => panel.Path));
            return arc.SoFar.Where(file => !paths.Contains(file.Path)).Select(file => new RoomRow(file.Name, RoomRowMark.Check)).ToList();
        }

        /// <summary>How wide and tall the list stands as last laid; zero where none stands.</summary>
        public Vector2 ListSize { get; private set; }

        /// <summary>Lays <paramref name="arc"/>: a new file arrives at the right, the one before moves to the left column, a file gone from the panels folds into the list under it.</summary>
        public Vector2 Show(WritingArc arc)
        {
            gameObject.SetActive(true);
            var now = Clock();
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var gap = RoomType.Units(GapDp);
            var panelWidth = RoomType.Units(RoomGeometry.PanelDp);
            var drawn = Drawn(arc);
            var rows = ListRows(arc);
            var columns = drawn.Count == 2 || rows.Count > 0 ? 2 : 1;
            var width = columns * panelWidth + (columns - 1) * gap;
            var rightX = width / 2f - panelWidth / 2f;
            var leftX = -width / 2f + panelWidth / 2f;

            // Panels gone from the arc fold toward the list; the rest are laid first, so the list knows where it starts.
            var keep = new HashSet<string>(drawn.Select(panel => panel.Path));
            var leaving = standing.Keys.Where(path => !keep.Contains(path)).ToList();
            var placed = new List<RoomWritingPanel>();
            var sizes = new List<Vector2>();
            foreach (var panel in drawn)
            {
                if (!standing.TryGetValue(panel.Path, out var at))
                {
                    var view = spare.Count > 0 ? spare[spare.Count - 1] : RoomWritingPanel.Create(transform);
                    if (spare.Count > 0) spare.RemoveAt(spare.Count - 1);
                    view.Clock = () => Clock();
                    at = new Placed { Panel = view, MovedAt = now - MoveSeconds, Fresh = true };
                    standing[panel.Path] = at;
                }
                sizes.Add(at.Panel.Show(panel));
                placed.Add(at.Panel);
            }
            var olderTall = drawn.Count == 2 ? sizes[0].y : 0f;

            // The list in the left column, under the older panel where one stands.
            var listTop = drawn.Count == 2 ? -(olderTall + gap) : 0f;
            var listTall = 0f;
            if (rows.Count > 0)
            {
                listGlass.gameObject.SetActive(true);
                var inner = panelWidth - 2f * pad;
                var down = 0f;
                var title = ListTitle(arc);
                if (title != null)
                {
                    listTitle.gameObject.SetActive(true);
                    RoomType.Size(listTitle);
                    GlazeRuns.Of(listTitle).Set(LineWords.Ours(title));
                    down += Mathf.Max(RoomType.Scaled(RoomGeometry.LineDp), KitType.Lay(listTitle, inner, 2).Height) + RoomType.Scaled(RoomRows.GapDp);
                }
                else listTitle.gameObject.SetActive(false);
                var rowsTall = list.Show(rows, inner);
                listTall = down + rowsTall + 2f * pad;
                listGlass.InnerBar = null;
                listGlass.Draw(new Vector2(panelWidth, listTall));
                listTitle.transform.localPosition = new Vector3(-panelWidth / 2f + pad, listTall / 2f - pad, -RoomType.Units(0.5f));
                list.transform.localPosition = new Vector3(-panelWidth / 2f + pad, listTall / 2f - pad - down, -RoomType.Units(0.5f));
            }
            else listGlass.gameObject.SetActive(false);
            ListSize = rows.Count > 0 ? new Vector2(panelWidth, listTall) : Vector2.zero;
            var listAt = new Vector3(leftX, listTop - listTall / 2f, 0f);
            ListFlat = listAt;
            Put(listGlass.transform, listAt);

            foreach (var path in leaving)
            {
                var gone = standing[path];
                standing.Remove(path);
                gone.From = gone.Now;
                gone.To = listAt;
                gone.MovedAt = now;
                folding.Add(gone);
            }
            for (var index = 0; index < drawn.Count; index++)
            {
                var at = standing[drawn[index].Path];
                var x = index == drawn.Count - 1 ? (columns == 1 ? 0f : rightX) : leftX;
                var to = new Vector3(x, -sizes[index].y / 2f, 0f);
                if (at.Fresh) { at.From = to; at.Fresh = false; }
                else if ((at.To - to).sqrMagnitude > 1e-10f) { at.From = at.Now; at.MovedAt = now; }
                at.To = to;
            }
            Panels = placed;
            var leftTall = olderTall + (drawn.Count == 2 && rows.Count > 0 ? gap : 0f) + listTall;
            Size = new Vector2(width, Mathf.Max(leftTall, drawn.Count > 0 ? sizes[drawn.Count - 1].y : 0f));
            Follow();
            return Size;
        }

        private void Update() => Follow();

        /// <summary>Each panel where its move stands now; each folding panel shrunk and faded by its fold, put by once it is in its row.</summary>
        private void Follow()
        {
            var still = GlazeMotion.Still;
            var now = Clock();
            foreach (var at in standing.Values)
            {
                var moved = still ? 1f : RoomMotion.Standard((now - at.MovedAt) / MoveSeconds);
                at.Now = Vector3.Lerp(at.From, at.To, moved);
                Put(at.Panel.transform, at.Now);
            }
            for (var index = folding.Count - 1; index >= 0; index--)
            {
                var gone = folding[index];
                var t = Mathf.Clamp01((now - gone.MovedAt) / MoveSeconds);
                var folded = still ? t : RoomMotion.Standard(t);
                if (t >= 1f)
                {
                    gone.Panel.Hide();
                    spare.Add(gone.Panel);
                    folding.RemoveAt(index);
                    continue;
                }
                gone.Now = still ? gone.From : Vector3.Lerp(gone.From, gone.To, folded);
                Put(gone.Panel.transform, gone.Now);
                gone.Panel.Fold(1f - folded, still);
            }
        }

        /// <summary>For renders: every move and fold finished, every panel arrived.</summary>
        public void Settle()
        {
            // A second past each move's end, so no float lands a fold a hair short of done.
            foreach (var at in standing.Values)
            {
                at.MovedAt = Clock() - MoveSeconds - 1f;
                at.Panel.Settle();
            }
            foreach (var gone in folding) gone.MovedAt = Clock() - MoveSeconds - 1f;
            Follow();
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
