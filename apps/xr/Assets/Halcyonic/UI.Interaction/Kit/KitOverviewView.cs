#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.UI
{
    /// <summary>One project on the overview: its own name, a sentence of its work, and its tasks as rows.</summary>
    public sealed class KitProjectModel
    {
        public KitProjectModel(string key, string name, string summary, IReadOnlyList<KitRowModel> tasks)
        {
            Key = key;
            Name = name;
            Summary = summary;
            Tasks = tasks;
        }

        public string Key { get; }

        public string Name { get; }

        /// <summary>The project's work in a sentence a person would say, as "1 task waiting for you, 1 running".</summary>
        public string Summary { get; }

        public IReadOnlyList<KitRowModel> Tasks { get; }
    }

    /// <summary>What the overview shows: every project's work, led by what needs the person.</summary>
    public sealed class KitOverviewModel
    {
        public KitOverviewModel(string title, string lead, IReadOnlyList<KitProjectModel> projects, IReadOnlyList<KitSpineSection> spine, KitRowModel? spineFoot)
        {
            Title = title;
            Lead = lead;
            Projects = projects;
            Spine = spine;
            SpineFoot = spineFoot;
        }

        public string Title { get; }

        /// <summary>The whole workspace's attention in one or two sentences, what waits for the person first.</summary>
        public string Lead { get; }

        public IReadOnlyList<KitProjectModel> Projects { get; }

        public IReadOnlyList<KitSpineSection> Spine { get; }

        public KitRowModel? SpineFoot { get; }
    }

    /// <summary>
    /// The overview: the same instrument with the spine at its left and a wider slate, the board, holding every project
    /// as one band of its own: the project's name, a sentence of its work, then its tasks as rows, each with its state
    /// in its icon and its words, and at its right end where pressing it goes, the next action of the task that has
    /// one. The board leads with what waits for the person across every project. Bands are parts of one face divided by
    /// space and their names, not cards; every next action stands at one right-hand line, so the eyes find them in one
    /// place. The board stands in front and carries the instrument's one light; the spine stands behind it, parallel
    /// to it on one plane facing the eyes, a column's gap away with the room between. Centred on its transform, built
    /// in units of the plane's distance.
    /// </summary>
    public sealed class KitOverviewView : MonoBehaviour
    {
        private KitCarrier spineCarrier = null!;
        private KitCarrier board = null!;
        private KitSpine spine = null!;
        private TextMeshPro title = null!;
        private TextMeshPro lead = null!;
        private readonly List<Band> bands = new List<Band>();
        private Vector2 size;

        public KitCarrier Board => board;
        public KitCarrier SpineCarrier => spineCarrier;
        public KitSpine Spine => spine;
        public TextMeshPro Title => title;
        /// <summary>The instrument's extent on its plane, in units of its distance, the gap included.</summary>
        public Vector2 Size => size;

        public event Action<string>? RowPressed;

        public IEnumerable<KitCarrier> Carriers => new[] { spineCarrier, board };

        public IEnumerable<KitRow> TaskRows => bands.Where(band => band.Root.activeSelf).SelectMany(band => band.Rows.Where(row => row.gameObject.activeSelf));

        public IEnumerable<KitPress> Targets => SpineTargets.Concat(BoardTargets);

        public IEnumerable<KitPress> SpineTargets
        {
            get
            {
                yield return spine.Switch.WorkspaceTarget;
                yield return spine.Switch.CompactTarget;
                foreach (var row in spine.Rows) yield return row.Press;
            }
        }

        public IEnumerable<KitPress> BoardTargets
        {
            get
            {
                foreach (var row in TaskRows) yield return row.Press;
            }
        }

        public static KitOverviewView Create(Transform parent, string name = "Overview")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<KitOverviewView>();
            view.spineCarrier = KitCarrier.Create(go.transform, "Context spine", KitType.Order.Carrier);
            view.board = KitCarrier.Create(go.transform, "Board", KitType.Order.Carrier + 3);
            view.spine = KitSpine.Create(view.spineCarrier.transform);
            view.spine.RowPressed += key => view.RowPressed?.Invoke(key);
            view.title = KitType.Create(view.board.transform, "Place", KitRole.Identity, KitType.Text, KitType.Order.Words);
            view.lead = KitType.Create(view.board.transform, "What waits", KitRole.Body, KitType.Text, KitType.Order.Words, leading: SpatialKit.ReadingLeading);
            return view;
        }

        public void Show(KitOverviewModel model)
        {
            var pad = KitType.Units(SpatialKit.PaddingDegrees);
            var spineWidth = PlaneComposition.Units(SpatialKit.SpineDegrees);
            var boardWidth = PlaneComposition.Units(SpatialKit.BoardDegrees);
            var width = spineWidth + boardWidth;
            var inner = boardWidth - 2f * pad;

            // Measure the board top-down from its own top, then place everything once its height is known.
            KitType.Size(title, KitRole.Identity);
            GlazeRuns.Of(title).Set(LineWords.Ours(model.Title));
            var (_, _, titleHeight) = KitType.Lay(title, inner, 1);
            KitType.Size(lead, KitRole.Body, SpatialKit.ReadingLeading);
            GlazeText.SetLiteral(lead, model.Lead);
            var (_, _, leadHeight) = KitType.Lay(lead, inner, 3);
            var y = pad + titleHeight + KitType.Scaled(0.3f) + leadHeight + KitType.Scaled(1.1f);
            while (bands.Count < model.Projects.Count) bands.Add(Band.Make(board.transform, bands.Count, key => RowPressed?.Invoke(key)));
            var bandTops = new float[model.Projects.Count];
            for (var index = 0; index < model.Projects.Count; index++)
            {
                bandTops[index] = y;
                y += bands[index].Measure(model.Projects[index], inner) + KitType.Scaled(1.6f);
            }
            for (var index = model.Projects.Count; index < bands.Count; index++) bands[index].Root.SetActive(false);
            var rise = KitType.Units(SpatialKit.SlateRiseDegrees);
            var spineNeeds = spine.Show(model.Spine, model.SpineFoot, Vector2.zero, spineWidth - 2f * pad, 0f) + 2f * pad + 2f * rise;
            var height = Mathf.Max(y - KitType.Scaled(1.6f) + pad, spineNeeds);

            var radius = KitType.Units(SpatialKit.InstrumentRadiusDegrees);
            // The board shows no one task's state: its light is the instrument's own, untinted.
            board.Draw(new Vector2(boardWidth, height), radius, SpatialKit.Slate, lit: true);
            // The board stands past the spine above and below: in front of it.
            var sides = height - 2f * rise;
            // Side by side on one plane, a column's gap apart, the whole centred on the view's line.
            var gap = SpatialKit.ColumnGapUnits;
            var recess = SpatialKit.RecessMeters / PlaneComposition.Distance;
            var left = -(width + gap) / 2f;
            spineCarrier.transform.localPosition = new Vector3(left + spineWidth / 2f, 0f, recess);
            board.transform.localPosition = new Vector3(left + spineWidth + gap + boardWidth / 2f, 0f, 0f);
            spineCarrier.Draw(new Vector2(spineWidth, sides), radius, SpatialKit.Side);
            size = new Vector2(width + gap, height);

            var top = height / 2f;
            var boardLeft = -boardWidth / 2f + pad;
            title.transform.localPosition = new Vector3(boardLeft, top - pad, -0.0005f);
            lead.transform.localPosition = new Vector3(boardLeft, top - pad - titleHeight - KitType.Scaled(0.3f), -0.0005f);
            for (var index = 0; index < model.Projects.Count; index++)
                bands[index].Place(model.Projects[index], new Vector2(boardLeft, top - bandTops[index]), inner);
            spine.Show(model.Spine, model.SpineFoot, new Vector2(-spineWidth / 2f + pad, sides / 2f - pad), spineWidth - 2f * pad, sides - 2f * pad);
        }

        public IEnumerable<Vector3> Corners() => Carriers.SelectMany(carrier => carrier.Corners());

        /// <summary>One project's band: a hairline above it, its name and its sentence, then its tasks.</summary>
        private sealed class Band
        {
            public GameObject Root = null!;
            public Surface Divider = null!;
            public TextMeshPro Name = null!;
            public TextMeshPro Summary = null!;
            public readonly List<KitRow> Rows = new List<KitRow>();
            private Action<string> pressed = null!;
            private float nameHeight;
            private float summaryHeight;
            private bool summaryBeside;

            public static Band Make(Transform parent, int index, Action<string> pressed)
            {
                var root = new GameObject("Project " + index);
                root.transform.SetParent(parent, false);
                var band = new Band { Root = root, pressed = pressed };
                band.Divider = Surface.Create(root.transform, "Divider", KitType.Order.Plate);
                band.Name = KitType.Create(root.transform, "Name", KitRole.Heading, KitType.Text, KitType.Order.Words);
                band.Summary = KitType.Create(root.transform, "Its work", KitRole.Body, KitType.Secondary, KitType.Order.Words);
                return band;
            }

            public float Measure(KitProjectModel project, float width)
            {
                Root.SetActive(true);
                KitType.Size(Name, KitRole.Heading);
                GlazeRuns.Of(Name).SetIdentity(project.Name);
                float nameWidth;
                (_, nameWidth, nameHeight) = KitType.Lay(Name, width, 1);
                KitType.Size(Summary, KitRole.Body);
                GlazeText.SetLiteral(Summary, project.Summary);
                var gap = KitType.Scaled(1.5f);
                // The project's sentence beside its name where both fit on one line, else under it.
                summaryBeside = Summary.GetPreferredValues(Summary.text).x <= width - nameWidth - gap;
                Summary.alignment = summaryBeside ? TextAlignmentOptions.TopRight : TextAlignmentOptions.TopLeft;
                Summary.rectTransform.pivot = summaryBeside ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
                (_, _, summaryHeight) = KitType.Lay(Summary, summaryBeside ? width - nameWidth - gap : width, 2);
                var height = KitType.Scaled(0.9f) + (summaryBeside ? Mathf.Max(nameHeight, summaryHeight) : nameHeight + summaryHeight) + KitType.Scaled(0.35f);
                while (Rows.Count < project.Tasks.Count)
                {
                    var row = KitRow.Create(Root.transform, "Task " + Rows.Count);
                    row.Pressed += key => pressed(key);
                    Rows.Add(row);
                }
                var targetGap = KitType.Units(SpatialKit.TargetGapDegrees);
                for (var index = 0; index < project.Tasks.Count; index++)
                    height += Rows[index].Show(project.Tasks[index], Vector2.zero, width) + (index + 1 < project.Tasks.Count ? targetGap : 0f);
                return height;
            }

            public void Place(KitProjectModel project, Vector2 topLeft, float width)
            {
                Root.transform.localPosition = new Vector3(topLeft.x, topLeft.y, 0f);
                // Bands are divided by space and their names, not by rules.
                Divider.gameObject.SetActive(false);
                var y = KitType.Scaled(0.9f);
                Name.transform.localPosition = new Vector3(0f, -y, -0.0005f);
                if (summaryBeside)
                {
                    // On the name's line, its words' baseline level with the name's.
                    var drop = (nameHeight - KitType.LinePitch(Summary)) * 0.62f;
                    Summary.transform.localPosition = new Vector3(width, -y - Mathf.Max(0f, drop), -0.0005f);
                    y += Mathf.Max(nameHeight, summaryHeight);
                }
                else
                {
                    y += nameHeight;
                    Summary.transform.localPosition = new Vector3(0f, -y, -0.0005f);
                    y += summaryHeight;
                }
                y += KitType.Scaled(0.35f);
                var targetGap = KitType.Units(SpatialKit.TargetGapDegrees);
                for (var index = 0; index < Rows.Count; index++)
                {
                    if (index >= project.Tasks.Count) { Rows[index].Hide(); continue; }
                    y += Rows[index].Show(project.Tasks[index], new Vector2(0f, -y), width) + targetGap;
                }
            }
        }
    }
}
