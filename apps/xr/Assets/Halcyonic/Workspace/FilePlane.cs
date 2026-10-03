#nullable enable
using System.Collections.Generic;
using System.Linq;
using Halcyonic.Client;
using Halcyonic.XR.UI;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>
    /// A task's file on one plane facing the eyes (ADR 0026): the file's column, and beside it the side
    /// panel a line opened, laid as one composition, its top <see cref="MenuPage.TopDegrees"/> below eye
    /// level, turned toward the task's character. Each column settles as the plane has it, which raises
    /// its view's <see cref="MenuFrameView.Drawn"/>: what that view shows now stands whole. The menu is
    /// not drawn here; with a side panel open it would step aside anyway (<see cref="MenuColumns"/>).
    /// </summary>
    public sealed class FilePlane : MonoBehaviour
    {
        /// <summary>The file's own column.</summary>
        public MenuFrameView File { get; private set; } = null!;

        /// <summary>The side panel's column, shown while a line opened one.</summary>
        public MenuFrameView Side { get; private set; } = null!;

        public static FilePlane Create(Transform parent)
        {
            var go = new GameObject("File");
            go.transform.SetParent(parent, false);
            var plane = go.AddComponent<FilePlane>();
            plane.File = MenuFrameView.Create(go.transform, "File column");
            plane.Side = MenuFrameView.Create(go.transform, "Side panel");
            plane.Side.Hide();
            return plane;
        }

        /// <summary>
        /// Shows <paramref name="frame"/>, with its side panel where it has one, on a plane seen from
        /// <paramref name="eyes"/>, its centre <paramref name="yaw"/> degrees round from straight ahead.
        /// </summary>
        public void Show(MenuFrame frame, Vector3 eyes, float yaw)
        {
            gameObject.SetActive(true);
            var side = frame.Side;
            var subject = MenuFrameView.SubjectHeight(frame.Subject, Glaze.Menu.FileColumnDegrees, pillRoom: true);
            if (side != null) subject = Mathf.Max(subject, MenuFrameView.SubjectHeight(side.Subject, Glaze.Menu.SideColumnDegrees, pillRoom: true));
            File.Show(frame, Glaze.Menu.FileColumnDegrees, subject, pillRoom: true);
            var columns = new List<MenuFrameView> { File };
            if (side != null)
            {
                Side.Show(side, Glaze.Menu.SideColumnDegrees, subject, pillRoom: true);
                columns.Add(Side);
            }
            else Side.Hide();

            var composition = new PlaneComposition(columns.Select(view => new PlaneColumn(view.Width, view.Heights.ToArray())).ToList(), GlazeText.Scale);
            var zoom = composition.Zoom;
            var half = Mathf.Atan(composition.Height / 2f) * Mathf.Rad2Deg;
            var direction = new PanelDirection(yaw, -MenuPage.TopDegrees - half, true, false);
            for (var column = 0; column < columns.Count; column++)
            {
                var view = columns[column];
                var placed = composition.Parts.Where(part => part.Column == column).OrderBy(part => part.Index).ToList();
                for (var index = 0; index < placed.Count; index++)
                {
                    PlaneLayout.Lay(view.Parts[index], eyes, direction, placed[index], zoom);
                    // The last part placed settles the column's words as the plane has them: drawn.
                    if (index == placed.Count - 1) view.Settle(placed[index], zoom);
                }
            }
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>The yaw, in degrees round from straight ahead, from <paramref name="eyes"/> toward <paramref name="toward"/>.</summary>
        public static float YawToward(Vector3 eyes, Vector3 toward)
        {
            var flat = toward - eyes;
            return flat.x == 0f && flat.z == 0f ? 0f : Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }
    }
}
