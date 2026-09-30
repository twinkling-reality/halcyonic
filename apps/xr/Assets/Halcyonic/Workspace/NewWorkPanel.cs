#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Halcyonic.Client;
using Halcyonic.Contracts;
using TMPro;
using UnityEngine;

namespace Halcyonic.XR.Workspace
{
    /// <summary>A hand operated panel for starting live work from the headset.</summary>
    [RequireComponent(typeof(ControlPlaneConnection))]
    public sealed class NewWorkPanel : MonoBehaviour
    {
        private const float Width = 0.78f;
        private const float Height = 0.98f;
        private const float TextWidth = 0.70f;
        private ControlPlaneConnection connection = null!;
        private CommandFactory commands = null!;
        private NewWorkDraft draft = null!;
        private Transform root = null!;
        private GameObject details = null!;
        private PanelButton toggle = null!;
        private PanelButton project = null!;
        private PanelButton projectNameButton = null!;
        private PanelButton runtime = null!;
        private PanelButton model = null!;
        private PanelButton objective = null!;
        private PanelButton start = null!;
        private TextMeshPro title = null!;
        private TextMeshPro summary = null!;
        private TextMeshPro note = null!;
        private bool expanded;
        private bool newProject;
        private bool confirming;
        private bool finished;
        private bool uncertain;
        private string newProjectName = "";
        private string? notice;
        private TouchScreenKeyboard? keyboard;
        private bool typingProject;
        private CancellationTokenSource? modelCancellation;
        private Task<RuntimeModelsResponse>? modelRead;
        private string? modelRuntimeId;
        private CommandEnvelope? pending;
        private Task<CommandAckMessage>? pendingAck;
        private CommandView? acknowledged;
        private string? createdWorkstreamId;
        private float nextRefresh;
        private bool placed;

        private void Awake()
        {
            connection = GetComponent<ControlPlaneConnection>();
            commands = new CommandFactory(new ClientInfo
            {
                Name = "halcyonic-xr",
                Version = Application.version,
                DeviceLabel = SystemInfo.deviceModel,
            });
            draft = new NewWorkDraft(commands);
            root = new GameObject("New work controls").transform;
            root.SetParent(transform, false);
            toggle = PanelButton.Create(root, "New work");
            toggle.Pressed += Toggle;
            details = new GameObject("New work panel");
            details.transform.SetParent(root, false);
            WorkspaceVisuals.Plate(details.transform, "Background", new Vector2(Width, Height),
                WorkspaceVisuals.PanelColor, WorkspaceVisuals.PanelPlateOrder);
            PointerTarget.Rectangle(details, new Vector2(Width, Height), ray: true, poke: false);
            title = Label("Title", 0.26f, 0.43f, 0.06f);
            summary = Label("Selection", 0.19f, 0.35f, 0.08f);
            note = Label("Message", 0.18f, -0.32f, 0.12f);
            project = Button("Project", 0.22f, ChooseProject);
            projectNameButton = Button("Project name", 0.13f, TypeProjectName);
            runtime = Button("Runtime", 0.04f, ChooseRuntime);
            model = Button("Model", -0.05f, ChooseModel);
            objective = Button("Objective", -0.14f, TypeObjective);
            start = Button("Start", -0.23f, Start);
            Layout();
        }

        private TextMeshPro Label(string name, float size, float y, float height)
        {
            var label = WorkspaceVisuals.Text(details.transform, name, size, WorkspaceVisuals.TextColor,
                new Vector2(TextWidth, height), TextAlignmentOptions.TopLeft, wrap: true,
                order: WorkspaceVisuals.PanelTextOrder);
            label.rectTransform.localPosition = new Vector3(-TextWidth / 2f, y, -0.003f);
            return label;
        }

        private PanelButton Button(string name, float y, Action pressed)
        {
            var button = PanelButton.Create(details.transform, name, height: 0.058f, textSize: WorkspaceVisuals.DetailSize);
            button.Pressed += pressed;
            button.transform.localPosition = new Vector3(0f, y, -0.004f);
            return button;
        }

        private void OnDestroy()
        {
            modelCancellation?.Cancel();
            modelCancellation?.Dispose();
            if (root != null) Destroy(root.gameObject);
        }

        private void Update()
        {
            PollKeyboard();
            PollModels();
            PollCommand();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 0.5f;
                Layout();
            }
            if (!placed) Place();
        }

        private void Toggle()
        {
            if (FocusGuard.InputSuspended) return;
            if (!expanded && (finished || uncertain))
            {
                var previousProject = draft.ProjectId;
                draft = new NewWorkDraft(commands) { ProjectId = previousProject };
                createdWorkstreamId = null;
                finished = false;
                uncertain = false;
                newProject = false;
                newProjectName = "";
            }
            expanded = !expanded;
            Place();
            confirming = false;
            notice = null;
            Layout();
        }

        private void ChooseProject()
        {
            if (Busy || createdWorkstreamId != null) return;
            var projects = connection.Session?.State.Projects.Values.OrderBy(value => value.Name, StringComparer.Ordinal).ToArray()
                ?? Array.Empty<ProjectView>();
            if (newProject)
            {
                newProject = false;
                draft.ProjectId = projects.FirstOrDefault()?.ProjectId;
            }
            else
            {
                var index = Array.FindIndex(projects, value => value.ProjectId == draft.ProjectId);
                if (index < 0 && projects.Length > 0) draft.ProjectId = projects[0].ProjectId;
                else if (index + 1 == projects.Length)
                {
                    newProject = true;
                    draft.ProjectId = null;
                }
                else draft.ProjectId = projects[index + 1].ProjectId;
            }
            confirming = false;
            Layout();
        }

        private void TypeProjectName()
        {
            if (Busy || !newProject || createdWorkstreamId != null) return;
            OpenKeyboard(newProjectName, "Name the project", true);
        }

        private void ChooseRuntime()
        {
            if (Busy) return;
            var runtimes = connection.Session?.State.Runtimes.Where(value => value.Capabilities.StartExecution)
                .OrderBy(value => value.DisplayName, StringComparer.Ordinal).ToArray() ?? Array.Empty<RuntimeDescriptor>();
            if (runtimes.Length == 0) return;
            var index = Array.FindIndex(runtimes, value => value.RuntimeId == draft.Runtime?.RuntimeId);
            var chosen = runtimes[(index + 1) % runtimes.Length];
            modelCancellation?.Cancel();
            modelCancellation?.Dispose();
            modelRead = null;
            draft.ChooseRuntime(chosen);
            confirming = false;
            if (chosen.ModelChoice == ModelChoice.Listed)
            {
                var api = ControlPlaneSettings.Api();
                if (api == null) draft.ModelReadFailed("No control plane is configured.");
                else
                {
                    modelRuntimeId = chosen.RuntimeId;
                    modelCancellation = new CancellationTokenSource();
                    modelRead = api.GetRuntimeModelsAsync(chosen.RuntimeId, modelCancellation.Token);
                }
            }
            Layout();
        }

        private void PollModels()
        {
            var read = modelRead;
            if (read == null || !read.IsCompleted) return;
            modelRead = null;
            if (draft.Runtime?.RuntimeId != modelRuntimeId) return;
            if (read.IsCanceled) return;
            if (read.IsFaulted) draft.ModelReadFailed(read.Exception?.GetBaseException().Message ?? "The request failed.");
            else draft.SetModels(read.Result);
            Layout();
        }

        private void ChooseModel()
        {
            if (Busy || draft.Models.Count == 0) return;
            var index = draft.Model == null ? -1 : draft.Models.ToList().FindIndex(value => value.ModelRef == draft.Model.ModelRef);
            draft.ChooseModel(draft.Models[(index + 1) % draft.Models.Count]);
            confirming = false;
            Layout();
        }

        private void TypeObjective()
        {
            if (Busy || createdWorkstreamId != null) return;
            OpenKeyboard(draft.Objective, "What should this work accomplish?", false);
        }

        private void OpenKeyboard(string initial, string prompt, bool projectName)
        {
            if (!TouchScreenKeyboard.isSupported)
            {
                notice = "Typing needs the headset's system keyboard.";
                Layout();
                return;
            }
            typingProject = projectName;
            keyboard = TouchScreenKeyboard.Open(initial, TouchScreenKeyboardType.Default, true, false, false, false, prompt);
        }

        private void PollKeyboard()
        {
            var open = keyboard;
            if (open == null || open.status == TouchScreenKeyboard.Status.Visible) return;
            keyboard = null;
            if (open.status != TouchScreenKeyboard.Status.Done) return;
            if (typingProject) newProjectName = (open.text ?? "").Trim();
            else draft.Objective = (open.text ?? "").Trim();
            confirming = false;
            notice = null;
            Layout();
        }

        private bool Busy => pending != null;

        private string? Problem()
        {
            if (uncertain) return "A request's outcome is unknown. Check the work shown before starting more.";
            if (connection.DemonstrationLine != null) return "Connect to a Mac to start live work.";
            var session = connection.Session;
            if (session == null || !session.Status.IsLive) return "Waiting for the control plane.";
            if (!newProject && (draft.ProjectId == null || !session.State.Projects.ContainsKey(draft.ProjectId))) return "Choose a project.";
            if (draft.Runtime != null && !session.State.Runtimes.Any(value => value.RuntimeId == draft.Runtime.RuntimeId))
                return "Choose an available runtime.";
            if (newProject)
            {
                if (newProjectName.Length == 0 || newProjectName.Length > 200) return "Type a project name of at most 200 characters.";
                // Project creation provides the id before the draft makes its workstream command.
                return draft.Runtime == null || !draft.Runtime.Capabilities.StartExecution
                    ? "Choose a runtime that can start work."
                    : draft.Runtime.ModelChoice == ModelChoice.Listed && draft.Model == null
                        ? draft.ModelProblem ?? "Choose a model."
                        : string.IsNullOrWhiteSpace(draft.Objective) || draft.Objective.Length > 4000
                            ? "Type an objective of at most 4,000 characters."
                            : null;
            }
            return draft.Problem;
        }

        private void Start()
        {
            if (Busy || finished) return;
            var problem = Problem();
            if (problem != null)
            {
                notice = problem;
                Layout();
                return;
            }
            if (!confirming)
            {
                confirming = true;
                notice = "Start this work with the selected runtime and model?";
                Layout();
                return;
            }
            confirming = false;
            notice = null;
            if (createdWorkstreamId != null) Send(draft.StartExecution(createdWorkstreamId));
            else if (newProject) Send(commands.CreateProject(newProjectName));
            else Send(draft.CreateWorkstream());
        }

        private void Send(CommandEnvelope command)
        {
            var session = connection.Session;
            if (session == null || !session.Status.IsLive)
            {
                notice = "Not sent: the control plane is not connected.";
                Layout();
                return;
            }
            pending = command;
            acknowledged = null;
            pendingAck = session.SubmitAsync(command);
            notice = "Sending request. Waiting for the control plane's result.";
            Layout();
        }

        private void PollCommand()
        {
            if (pending == null) return;
            if (pendingAck != null)
            {
                if (!pendingAck.IsCompleted) return;
                var task = pendingAck;
                pendingAck = null;
                if (task.IsFaulted || task.IsCanceled)
                {
                    uncertain = task.Exception?.GetBaseException() is not SessionUnavailableException;
                    notice = task.Exception?.GetBaseException() is SessionUnavailableException
                        ? "Not sent: the control plane is not connected."
                        : "The request's outcome is unknown. Check the work shown before trying again.";
                    pending = null;
                    Layout();
                    return;
                }
                acknowledged = task.Result.Command;
                if (task.Result.Disposition == CommandAckDisposition.Conflict || acknowledged == null)
                {
                    notice = "The control plane could not accept the request.";
                    pending = null;
                    Layout();
                    return;
                }
            }
            var state = connection.Session?.State;
            var record = state != null && state.Commands.TryGetValue(pending.CommandId, out var current) ? current : acknowledged;
            if (record == null || record.Status == CommandStatus.Accepted) return;
            var sent = pending;
            pending = null;
            if (record.Status != CommandStatus.Completed)
            {
                uncertain = record.Failure?.Effect == FailureEffect.Unknown;
                notice = record.Rejection?.Message ?? record.Failure?.Message ?? "The request failed.";
                Layout();
                return;
            }
            if (sent is ProjectCreateCommand && record.Result is ProjectCreatedResult projectResult)
            {
                draft.ProjectId = projectResult.ProjectId;
                newProject = false;
                Send(draft.CreateWorkstream());
            }
            else if (sent is WorkstreamCreateCommand && record.Result is WorkstreamCreatedResult workstreamResult)
            {
                createdWorkstreamId = workstreamResult.WorkstreamId;
                Send(draft.StartExecution(createdWorkstreamId));
            }
            else if (sent is ExecutionStartCommand && record.Result is ExecutionCreatedResult)
            {
                finished = true;
                notice = "Work started. Its character will appear on the stage.";
                Layout();
            }
            else
            {
                notice = "The control plane returned an unexpected result. Check the work shown.";
                Layout();
            }
        }

        private void Layout()
        {
            if (root == null) return;
            var live = connection.Session?.Status.IsLive == true && connection.DemonstrationLine == null;
            toggle.Show(expanded ? "Close new work" : "New work", Vector2.zero, expanded ? 0.30f : 0.22f);
            details.SetActive(expanded);
            if (!expanded) return;
            WorkspaceVisuals.SetLiteral(title, "Start new work");
            var projectName = ProjectName();
            var runtimeName = draft.Runtime?.DisplayName ?? "none";
            var modelName = draft.Runtime?.ModelChoice == ModelChoice.None ? "runtime choice" : draft.Model?.DisplayName ?? "none";
            WorkspaceVisuals.SetLiteralLines(summary, new[]
            {
                "Project: " + projectName + "   Runtime: " + runtimeName,
                "Model: " + modelName,
            });
            var message = notice ?? (Busy ? "Waiting for the control plane's result." : Problem() ?? "Ready to start.");
            if (draft.Model == null) WorkspaceVisuals.SetLiteral(note, message);
            else WorkspaceVisuals.SetLiteralLines(note, new[]
            {
                "Runs " + Served(draft.Model.Served) + ". " + Tools(draft.Model.ToolCalling) + ".",
                message,
            });
            project.Show("Project: " + projectName, new Vector2(0f, 0.22f), 0.70f);
            if (newProject) projectNameButton.Show(newProjectName.Length == 0 ? "Type project name" : "Project name: " + newProjectName,
                new Vector2(0f, 0.13f), 0.70f);
            else projectNameButton.Hide();
            runtime.Show("Runtime: " + runtimeName + (draft.Runtime?.Synthetic == true ? " (simulated)" : ""),
                new Vector2(0f, 0.04f), 0.70f);
            if (draft.Runtime?.ModelChoice == ModelChoice.Listed)
            {
                var modelLine = draft.Model == null ? draft.ModelProblem ?? "Choose a model" : draft.Model.DisplayName;
                model.Show("Model: " + modelLine, new Vector2(0f, -0.05f), 0.70f);
            }
            else model.Hide();
            objective.Show(draft.Objective.Length == 0 ? "Type objective" : "Objective: " + draft.Objective,
                new Vector2(0f, -0.14f), 0.70f);
            start.Show(finished ? "Work started" : confirming ? "Yes, start work" : Busy ? "Waiting for result" : "Start work",
                new Vector2(0f, -0.23f), 0.70f, confirm: confirming);
            start.Accepting = () => live && !Busy && !finished && !uncertain && !FocusGuard.InputSuspended;
            project.Accepting = objective.Accepting = projectNameButton.Accepting =
                () => live && !Busy && createdWorkstreamId == null && !FocusGuard.InputSuspended;
            runtime.Accepting = model.Accepting = () => live && !Busy && !FocusGuard.InputSuspended;
        }

        private static string Served(ModelServed value) => value switch
        {
            ModelServed.ThisMac => "on this Mac",
            ModelServed.Remote => "remote",
            _ => "location unknown",
        };

        private static string Tools(ModelToolCalling value) => value switch
        {
            ModelToolCalling.Declared => "tools declared",
            ModelToolCalling.NotDeclared => "tools not declared",
            _ => "tools unknown",
        };

        private void Place()
        {
            var head = WorkspaceVisuals.Head;
            if (head == null) return;
            var forward = head.forward;
            var horizontal = new Vector3(forward.x, 0f, forward.z).normalized;
            if (horizontal.sqrMagnitude < 0.01f) horizontal = Vector3.forward;
            var position = head.position + horizontal * (expanded ? 0.68f : 0.43f)
                + Vector3.down * (expanded ? 0.12f : 0.42f);
            root.SetPositionAndRotation(position, Quaternion.LookRotation(position - head.position, Vector3.up));
            root.localScale = Vector3.one * (Vector3.Distance(head.position, position) / WorkspaceVisuals.PanelDistance);
            toggle.transform.localPosition = expanded ? new Vector3(0f, -0.55f, 0f) : Vector3.zero;
            placed = true;
        }

        private string ProjectName()
        {
            if (newProject) return "new project";
            var projects = connection.Session?.State.Projects;
            return projects != null && projects.TryGetValue(draft.ProjectId ?? "", out var selected)
                ? selected.Name : "none";
        }
    }
}
