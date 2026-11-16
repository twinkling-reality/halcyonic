#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>
    /// Watch It Work's arc (FIELD's SHAPE, "Several files"; XR places it on the plane), bent round the eyes so every panel
    /// faces them and no word is read at a slant (a flat 1652 dp row read its outer code under 14 dp): at most
    /// three file panels (<see cref="RoomWritingPanel"/>) side by side, oldest left, the newest arriving at the right end while
    /// the others move left by one place over the standard curve's 600 ms; and at the left, once any file has left the
    /// panels, the turn's files so far as the rows draw them (<see cref="RoomRowsView"/>: each file's name with its check),
    /// in the result's Changes order, so nothing is ever stacked over text. A panel that leaves the arc folds toward its row:
    /// it moves to the list over 600 ms, shrinking and fading as its row stands. Under Still nothing moves: panels and rows
    /// cross-fade in place. Its origin is the arc's top middle.
    /// </summary>
    public sealed class RoomWritingArc : MonoBehaviour
    {
        /// <summary>The gap between the panels, and between the list and the first panel.</summary>
        public const float GapDp = 24f;

        /// <summary>How wide the files-so-far list stands.</summary>
        public const float ListDp = 260f;

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

        /// <summary>The rows of the files so far that stand in no panel, in the result's order, each with its check.</summary>
        public static IReadOnlyList<RoomRow> ListRows(WritingArc arc)
        {
            var paths = new HashSet<string>(arc.Panels.Select(panel => panel.Path));
            return arc.SoFar.Where(file => !paths.Contains(file.Path)).Select(file => new RoomRow(file.Name, RoomRowMark.Check)).ToList();
        }

        /// <summary>Lays <paramref name="arc"/>: new files arrive at the right, the rest move left, a file gone from the panels folds into the list.</summary>
        public Vector2 Show(WritingArc arc)
        {
            gameObject.SetActive(true);
            var now = Clock();
            var pad = RoomType.Scaled(RoomGeometry.PanelPadDp);
            var gap = RoomType.Units(GapDp);
            var panelWidth = RoomType.Units(RoomGeometry.PanelDp);

            // The list at the left, where any file has left the panels.
            var rows = ListRows(arc);
            var listWidth = rows.Count > 0 ? RoomType.Units(ListDp) : 0f;
            var listTall = 0f;
            if (rows.Count > 0)
            {
                listGlass.gameObject.SetActive(true);
                var inner = listWidth - 2f * pad;
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
                listGlass.Draw(new Vector2(listWidth, listTall));
                listTitle.transform.localPosition = new Vector3(-listWidth / 2f + pad, listTall / 2f - pad, -RoomType.Units(0.5f));
                list.transform.localPosition = new Vector3(-listWidth / 2f + pad, listTall / 2f - pad - down, -RoomType.Units(0.5f));
            }
            else listGlass.gameObject.SetActive(false);

            var count = arc.Panels.Count;
            var width = listWidth + (listWidth > 0f && count > 0 ? gap : 0f) + count * panelWidth + Math.Max(0, count - 1) * gap;
            var left = -width / 2f;
            var listAt = new Vector3(left + listWidth / 2f, -listTall / 2f, 0f);
            ListFlat = listAt;
            Put(listGlass.transform, listAt);

            // Panels gone from the arc fold toward the list; panels that stay move to their new place; new ones arrive in theirs.
            var keep = new HashSet<string>(arc.Panels.Select(panel => panel.Path));
            foreach (var path in standing.Keys.Where(path => !keep.Contains(path)).ToList())
            {
                var gone = standing[path];
                standing.Remove(path);
                gone.From = gone.Now;
                gone.To = listAt;
                gone.MovedAt = now;
                folding.Add(gone);
            }
            var tallest = listTall;
            var placed = new List<RoomWritingPanel>();
            for (var index = 0; index < count; index++)
            {
                var panel = arc.Panels[index];
                if (!standing.TryGetValue(panel.Path, out var at))
                {
                    var view = spare.Count > 0 ? spare[spare.Count - 1] : RoomWritingPanel.Create(transform);
                    if (spare.Count > 0) spare.RemoveAt(spare.Count - 1);
                    view.Clock = () => Clock();
                    at = new Placed { Panel = view, MovedAt = now - MoveSeconds };
                    standing[panel.Path] = at;
                }
                var size = at.Panel.Show(panel);
                tallest = Mathf.Max(tallest, size.y);
                var x = left + listWidth + (listWidth > 0f ? gap : 0f) + index * (panelWidth + gap) + panelWidth / 2f;
                var to = new Vector3(x, -size.y / 2f, 0f);
                if (at.MovedAt <= now - MoveSeconds && at.To == default) at.From = to;
                else if ((at.To - to).sqrMagnitude > 1e-10f) { at.From = at.Now; at.MovedAt = now; }
                at.To = to;
                placed.Add(at.Panel);
            }
            Panels = placed;
            Size = new Vector2(width, tallest);
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
            foreach (var at in standing.Values)
            {
                at.MovedAt = Clock() - MoveSeconds;
                at.Panel.Settle();
            }
            foreach (var gone in folding) gone.MovedAt = Clock() - MoveSeconds;
            Follow();
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
