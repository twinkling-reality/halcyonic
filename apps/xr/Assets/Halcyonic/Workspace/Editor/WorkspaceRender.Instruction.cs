#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
        /// <summary>Production local instruction editor, explicit sends and full notices at both real-font sizes. No session or microphone starts.</summary>
        public static void InstructionCheck()
        {
            var failures = GlazeChecks.AtEachTextSize(variant =>
            {
                var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "InstructionRenders", variant));
                Directory.CreateDirectory(folder);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                ViewField.Current = FieldChecks.Quest3S;
                var found = RenderInstructions("far-3s", folder).ToList();
                ViewField.Current = null;
                KeepFontAssetsAsCommitted();
                foreach (var failure in found) Debug.LogError("Halcyonic: instruction render: " + failure);
                return found;
            });
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }

        private static IEnumerable<string> RenderInstructions(string name, string folder)
        {
            var failures = new List<string>();
            var root = new GameObject("Instruction editor witness");
            var texture = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            try
            {
                var work = Work.Running();
                var eyes = new Vector3(0f, EyeHeight, 0f);
                var camera = MakeCamera(root.transform, eyes, texture);
                var view = CharacterView.Create(root.transform, work.Workstream.WorkstreamId);
                view.Show(work.Present().Character);
                var radius = CharacterStage.DefaultDistance;
                var (height, scale) = CharacterStage.Stance(view, radius, 0f, CharacterStage.DefaultHeightFromEyes);
                view.transform.SetPositionAndRotation(eyes + Vector3.forward * radius + Vector3.up * height, Quaternion.LookRotation(Vector3.back));
                view.transform.localScale = Vector3.one * scale;
                var target = CharacterTarget.Attach(view, work.Workstream.WorkstreamId);
                var characters = new List<(CharacterView View, CharacterTarget Target)> { (view, target) };
                var client = new ClientInfo { Name = "halcyonic-xr", Version = "render", DeviceLabel = "render" };
                var commands = new CommandFactory(client);
                // An unstarted identity only binds local submissions. The fake submit records an acknowledgement, not runtime completion.
                var session = new RealtimeSession(new RealtimeSessionOptions(new Uri("ws://127.0.0.1:9/realtime"), "render", client));
                var sent = new List<CommandEnvelope>();
                Action<string>? done = null;
                var presets = false;
                FileColumn? file = null;
                var voiceComponent = root.AddComponent<HoldToTalk>();
                var director = MenuDirector.Create(root.transform, new MenuDirector.Setup
                {
                    Commands = commands,
                    Comfort = new Comfort { Text = GlazeText.Scale > 1f ? TextSize.Larger : TextSize.Standard },
                    Overview = () => WorkOverview.Of(work.State, new StageVisibility(), _ => true),
                    State = () => work.State,
                    Session = () => session,
                    Connected = () => true,
                    KeyboardOffered = () => true,
                    Keyboard = (_, _, accept, _) => done = accept,
                    Voice = voiceComponent,
                    Submit = (_, command) =>
                    {
                        sent.Add(command);
                        return Task.FromResult(new CommandAckMessage { CommandId = command.CommandId, Disposition = CommandAckDisposition.Accepted });
                    },
                    File = (host, _) => file = new FileColumn(host, () => work.Present(), commands, () => null,
                        _ => presets ? WorkspaceText.PresetInstructions : null, () => ""),
                    StageNow = () => new MenuDirector.Stage(eyes, target.BodyPosition - eyes, new[] { target }, null, false),
                    CharacterOf = _ => target,
                    Bar = place => TasksColumn.Bar(place, work.State),
                });
                void Draw()
                {
                    for (var draw = 0; draw < 3; draw++) { director.DrawNow(); director.Plane.Advance(MenuPlane.OpeningSeconds); }
                }
                bool Press(string id, string? key = null)
                {
                    Draw();
                    var frame = director.Plane.Showing(MenuColumn.File);
                    var offered = frame != null && (frame.Footer.All.Any(each => each.Prompt.Id == id)
                        || frame.Lines.Any(line => line.Action == id && (key == null || line.Key == key)));
                    if (!offered) { failures.Add(name + ": no visible instruction control " + id); return false; }
                    var taken = director.PressForRender(MenuColumn.File, id, key, frame, null);
                    Draw();
                    if (!taken) failures.Add(name + ": visible instruction control took no press " + id);
                    return taken;
                }
                void Shot(string step)
                {
                    Draw();
                    failures.AddRange(PlaneState(name + " instruction " + step, folder, camera, texture, director.Plane, characters, eyes, null));
                    if (file!.Frame!.Footer.All.Count() > 3) failures.Add(name + ": instruction footer exceeded three prompts at " + step);
                }
                void ReadDraft(string step)
                {
                    Shot(step + " current");
                    for (var part = 0; part < 100 && !file!.Screen.Instruction.Read; part++)
                    {
                        Shot(step + " part " + (file.Screen.Instruction.Part + 1));
                        Thread.Sleep(TimeSpan.FromSeconds(0.45));
                        if (!file.Screen.Instruction.Read) Press(FileScreens.NextInstructionPart);
                    }
                    Draw();
                    if (!file!.Screen.Instruction.Read) failures.Add(name + ": instruction never reached whole reading proof");
                }
                director.OpenFile(work.Workstream.WorkstreamId);
                Draw();
                Press(FileScreens.TellIt);
                Shot("empty keyboard");
                if (!file!.Frame!.Lines.Any(line => line.Control == LineControl.Input && line.InputFocused && !line.Runs.AnyTheirs))
                    failures.Add(name + ": empty keyboard draft lost its focused input role or placeholder provenance");
                done!("Add a test for the rate limit.");
                if (sent.Count != 0 || file.Steering.Armed != null) failures.Add(name + ": keyboard completion sent or armed");
                ReadDraft("keyboard draft");
                Press(FileScreens.SendInstruction);
                if (sent.Count != 1 || ((ExecutionSendInstructionCommand)sent[0]).Payload.Text != "Add a test for the rate limit.")
                    failures.Add(name + ": explicit typed Send did not preserve the exact draft");

                // Use the production director and MenuVoice routing with an in-memory recording state; never call HoldToTalk.Begin.
                var busy = false;
                var voice = new MenuVoice(() => busy, () => busy = true, () => { }, () => busy = false);
                director.VoiceForRender(voice);
                Draw();
                var held = director.Plane.Showing(MenuColumn.File)!.Footer.All.Select(each => each.Prompt).First(prompt => prompt.Id == FileScreens.HoldToTalk);
                director.HoldPromptForRender(MenuColumn.File, held, director.Plane.Showing(MenuColumn.File), null);
                var transcription = voice.CaptureHeard();
                voice.Ended(file, FileScreens.HoldToTalk, true);
                transcription("Keep the audit messages readable."); busy = false;
                if (sent.Count != 1 || file.Steering.Armed != null || !file.Screen.Instruction.Spoken) failures.Add(name + ": spoken completion did not remain a protected draft");
                ReadDraft("spoken draft");
                Press(FileScreens.SendInstruction);
                Shot("spoken review");
                if (file.Steering.Armed != WorkspaceAction.Instruct || sent.Count != 1) failures.Add(name + ": spoken Send lost its reviewed confirmation");
                for (var part = 0; part < 100 && !file.Steering.CanConfirm; part++)
                {
                    Shot("spoken confirmation part " + (file.Screen.RequestPart + 1));
                    Thread.Sleep(TimeSpan.FromSeconds(0.45));
                    if (!file.Steering.CanConfirm) Press(FileScreens.NextPart, FileScreens.RequestKey);
                }
                Press(FileScreens.Yes);
                if (sent.Count != 2) failures.Add(name + ": reviewed spoken instruction was not sent exactly once");

                presets = true;
                Press(FileScreens.TellIt);
                Shot("preset choices");
                var choice = file.Frame!.Lines.First(line => line.Action == FileScreens.Preset);
                Press(FileScreens.Preset, choice.Key);
                Shot("preset chosen");
                Press(FileScreens.TellIt);
                if (sent.Count != 2 || file.Steering.Armed != null) failures.Add(name + ": preset review implicitly sent");
                ReadDraft("preset local draft");
                Press(FileScreens.SendInstruction);
                if (sent.Count != 3) failures.Add(name + ": explicit preset Send was not exactly once");

                presets = false;
                Press(FileScreens.TellIt);
                done!(string.Join(" ", Enumerable.Range(0, 54).Select(index => "Check step " + index + " and retain its exact output.")));
                Shot("long local draft");
                Thread.Sleep(TimeSpan.FromSeconds(0.45));
                Press(FileScreens.NextInstructionPart);
                file.Said(string.Join(" ", Enumerable.Range(0, 30).Select(index => "Nothing was sent because the instruction could not be accepted at step " + index + ".")));
                Shot("full notice");
                if (!file.Screen.Instruction.ExplainingNotice || file.Screen.Instruction.Read) failures.Add(name + ": long notice leaked into instruction reading or had no full route");
                Press(FileScreens.BackInstructionNotice);
                Shot("after notice");
                if (file.Screen.Notice != null || file.Screen.Instruction.Part != 0 || sent.Count != 3) failures.Add(name + ": acknowledging notice lost the draft or sent it");
                director.Compact(); Draw(); director.OpenFile(work.Workstream.WorkstreamId); Draw();
                Shot("resumed local draft");
            }
            finally { UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(root); }
            return failures;
        }
    }
}
