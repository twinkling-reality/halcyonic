#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using Halcyonic.XR.UI;
using Halcyonic.XR.UI.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Halcyonic.XR.Workspace.Editor
{
    public static partial class WorkspaceRender
    {
        /// <summary>Complete optional shelf and independently placed production console, including real labels and file controls.</summary>
        public static void ShelfWorkspaceCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "ShelfWorkspaceRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var result = new List<string>();
                foreach (var count in new[] { 1, 3, 6 })
                    result.AddRange(RenderShelfWorkspace(Path.Combine(folder, count + "-slots"), count).Select(failure => count + " slots: " + failure));
                ViewField.Current = null;
                KeepFontAssetsAsCommitted();
                foreach (var failure in result) Debug.LogError("Halcyonic: shelf workspace: " + failure);
                if (result.Count == 0) Debug.Log("Halcyonic: shelf workspace: every check passed; " + folder);
                return result;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static List<string> RenderShelfWorkspace(string folder, int count)
        {
            Directory.CreateDirectory(folder);
            var failures = new List<string>();
            var root = new GameObject("Complete optional shelf workspace witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            RealtimeSession? session = null;
            try
            {
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var work = Work.Running();
                var characters = new List<(CharacterView View, CharacterTarget Target)>();
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
                var presentation = new WorkspacePresentationState();
                FileColumn? file = null;
                var sends = 0;
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Presentation = presentation,
                    Commands = commands,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    State = () => work.State,
                    Session = () => session,
                    Connected = () => true,
                    Overview = () => WorkOverview.Of(work.State, new StageVisibility(), _ => true),
                    Bar = place => TasksColumn.Bar(place, work.State),
                    ReadLocations = _ => Task.FromResult(new LocationsResponse { Roots = new List<LocationRoot>() }),
                    Submit = (_, command) => { sends++; return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted }); },
                    File = (host, _) => file = new FileColumn(host, () => work.Present(), commands, () => null, _ => null, () => ""),
                    StageNow = () => new MenuDirector.Stage(eyes, Vector3.forward,
                        presentation.AgentsVisible ? characters.Select(each => each.Target).ToList() : new List<CharacterTarget>(), null, false),
                    CharacterOf = id => presentation.AgentsVisible ? characters.FirstOrDefault(each => each.View.WorkstreamId == id).Target : null,
                });
                void Shot(string name)
                {
                    director.UpdateForRender(); director.DrawNow();
                    failures.AddRange(PlaneState("shelf " + name, folder, camera, texture, director.Plane, characters, eyes, null, lightLine: false));
                    foreach (var (view, _) in characters.Where(each => each.View.gameObject.activeInHierarchy))
                        failures.AddRange(GlazeChecks.TextAsSeen(view.Label.gameObject, eyes, "shelf " + name));
                    failures.AddRange(GlazeChecks.Apart(characters.Where(each => each.View.gameObject.activeInHierarchy)
                        .Select(each => GlazeChecks.Of(each.View.WorkstreamId + " label", eyes, each.View.Label.gameObject)).ToList()));
                }
                Shot("character-free workspace");
                var original = director.Plane.Direction;
                var corners = new Vector3[8];
                var overlap = new Pose(PlaneLayout.PointOf(eyes, original, 0f, 0f), Quaternion.identity);
                CharacterStage.WriteShelfCorners(overlap, AgentShelfDraft.Width(count), eyes, corners);
                if (director.ClearsShelf(corners, eyes)) failures.Add("an overlapping full shelf escaped the actual console veto");
                Pose? accepted = null;
                foreach (var drop in new[] { 0.45f, 0.6f, 0.75f, 0.9f })
                foreach (var distance in new[] { 1.2f, 1.5f, 1.8f, 2f })
                foreach (var turn in new[] { 0f, -20f, 20f, -35f, 35f })
                {
                    var rotation = Quaternion.Euler(0f, turn, 0f);
                    var candidate = new Pose(eyes + rotation * Vector3.forward * distance - Vector3.up * drop, rotation);
                    CharacterStage.WriteShelfCorners(candidate, AgentShelfDraft.Width(count), eyes, corners);
                    var measured = ViewField.Current ?? FieldChecks.Quest3S;
                    var field = new ViewField(measured.Left - ViewField.EdgeMarginDegrees, measured.Right - ViewField.EdgeMarginDegrees,
                        measured.Up - ViewField.EdgeMarginDegrees, measured.Down - ViewField.EdgeMarginDegrees);
                    var inView = corners.All(corner =>
                    {
                        var point = Quaternion.Inverse(camera.transform.rotation) * (corner - eyes);
                        return field.Shows(Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg,
                            Mathf.Atan2(point.y, new Vector2(point.x, point.z).magnitude) * Mathf.Rad2Deg);
                    });
                    if (!accepted.HasValue && inView && director.ClearsShelf(corners, eyes)) accepted = candidate;
                }
                if (!accepted.HasValue)
                {
                    failures.Add("no " + count + "-slot shelf preview in the supported comfortable range clears the actual character-free console and measured field");
                    // Keep an explicitly invalid trial visible so this diagnostic also measures full labels and body collisions.
                    accepted = new Pose(eyes + Vector3.forward * 1.2f - Vector3.up * 0.75f, Quaternion.identity);
                }
                var pose = accepted.Value;
                var holder = new GameObject("Explicitly confirmed shelf").transform;
                holder.SetParent(root.transform, false); holder.SetPositionAndRotation(pose.position, pose.rotation);
                var shelf = Surface.Create(holder, "Placed agent shelf", 0);
                shelf.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                shelf.Draw(new Vector2(AgentShelfDraft.Width(count), AgentShelfDraft.Depth), 0.025f, new Color(0.12f, 0.19f, 0.22f, 1f));
                for (var slot = 0; slot < count; slot++)
                {
                    // One journal task drives the real file; the other explicitly synthetic presentations exercise the whole shelf's geometry.
                    var shown = slot == 0 ? work.Present().Character : Presentation("shelf-render-" + slot, slot);
                    var view = CharacterView.Create(holder, shown.WorkstreamId);
                    view.Grounded = true;
                    view.transform.localPosition = Vector3.right * ((slot - (count - 1) / 2f) * AgentShelfDraft.Spacing * AgentShelfDraft.Scale);
                    view.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    view.transform.localScale = Vector3.one * AgentShelfDraft.Scale;
                    view.Show(shown); view.PlaceShelfLabel(eyes); view.AdvanceForRender(1f / 60f);
                    characters.Add((view, CharacterTarget.Attach(view, shown.WorkstreamId)));
                }
                presentation.SetAgentsEnabled(true);
                Shot("confirmed shelf");
                if (director.Plane.Direction.Yaw != original.Yaw || director.Plane.Direction.Elevation != original.Elevation)
                    failures.Add("enabling the confirmed shelf moved the independent workspace");
                director.OpenFile(work.Workstream.WorkstreamId);
                Shot("selected task");
                work.Workstream.Title = "Add rate limiting and detailed regression coverage to the storefront sign-in endpoint";
                System.Threading.Thread.Sleep(TimeSpan.FromSeconds(FileColumn.RefreshSeconds + 0.05));
                file!.Tick();
                Shot("long task title");
                if (holder.position != pose.position || holder.rotation != pose.rotation || characters.Any(each => !each.View.gameObject.activeInHierarchy))
                    failures.Add("growing a file changed or silently hid the confirmed shelf");
                presentation.Minimize(); holder.gameObject.SetActive(false);
                Shot("global dock");
                presentation.Restore(); holder.gameObject.SetActive(true);
                Shot("restored shelf and task");
                if (sends != 0) failures.Add("placement, navigation or global restore sent a command");
            }
            catch (Exception error) { failures.Add(error.ToString()); }
            finally
            {
                session?.Dispose();
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(root);
            }
            return failures;
        }
    }
}
