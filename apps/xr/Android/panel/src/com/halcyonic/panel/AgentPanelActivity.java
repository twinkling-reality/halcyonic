package com.halcyonic.panel;

import android.app.Activity;
import android.app.Dialog;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.drawable.GradientDrawable;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputFilter;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/** Production 2D hybrid client. It remains usable when Unity pauses; the Mac owns agent execution. */
public final class AgentPanelActivity extends Activity {
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private PanelFiles files;
    private PanelTransport transport;
    private PanelIdentity identity;
    private PanelModel snapshot;
    private PanelContinuation continuation;
    private PanelDrafts drafts = new PanelDrafts();
    private boolean draftsHealthy = true;
    private LinearLayout content;
    private TextView connection;
    private EditText instruction;
    private Dialog review;
    private String projectId, workId, executionId, draft = "", commandId;
    private boolean spoken, visible, busy;
    private int generation;
    private final Runnable refresh = () -> poll(false);

    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        // Prevent task/agent text in screenshots and recent thumbnails. Capture is a separate,
        // deliberate consent flow, not an implicit permission granted by opening this panel.
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        files = new PanelFiles(this);
        shell();
    }
    @Override public void onResume() {
        super.onResume(); visible = true; generation++; busy = false;
        loadSaved(); poll(true);
    }
    @Override public void onPause() {
        saveState(); visible = false; generation++; busy = false;
        ui.removeCallbacks(refresh);
        if (transport != null) transport.cancel();
        closeReview(); instruction = null;
        super.onPause();
    }
    @Override protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        // Exported OS launcher entry never accepts selection, credential or command extras.
        setIntent(new Intent());
        closeReview(); loadSaved();
        if (visible) { generation++; busy = false; if (transport != null) transport.cancel(); poll(true); }
    }
    @Override public void onDestroy() { worker.shutdownNow(); super.onDestroy(); }
    @Override public void onBackPressed() { if (review != null) closeReview(); else if (workId != null) { if (!saveState()) return; workId = null; executionId = null; draft = ""; show(); } else if (projectId != null) { if (!saveState()) return; projectId = null; show(); } else super.onBackPressed(); }

    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private LinearLayout column() { LinearLayout v = new LinearLayout(this); v.setOrientation(LinearLayout.VERTICAL); v.setPadding(dp(20), dp(16), dp(20), dp(16)); return v; }
    private TextView text(LinearLayout parent, String value, int size) {
        TextView view = new TextView(this); view.setText(value); view.setTextSize(size); view.setTextColor(Color.rgb(233, 237, 242)); view.setPadding(0, dp(6), 0, dp(8));
        parent.addView(view, new LinearLayout.LayoutParams(-1, -2)); return view;
    }
    private Button button(LinearLayout parent, String label, Runnable action) {
        Button button = new Button(this); button.setText(label); button.setAllCaps(false); button.setTextSize(18); button.setMinHeight(dp(52));
        parent.addView(button, new LinearLayout.LayoutParams(-1, -2)); styleButton(button, label); button.setContentDescription(label); button.setOnClickListener(view -> { if (visible && !busy) action.run(); }); return button;
    }
    private void styleButton(Button button, String label) {
        GradientDrawable surface = new GradientDrawable();
        boolean primary = label.startsWith("Send") || label.equals("Workspace") || label.startsWith("Review instruction");
        surface.setColor(primary ? Color.rgb(35, 73, 126) : Color.rgb(26, 37, 54));
        surface.setCornerRadius(dp(10)); surface.setStroke(dp(1), Color.rgb(75, 105, 145));
        button.setBackground(surface); button.setTextColor(Color.rgb(238, 244, 255)); button.setPadding(dp(16), dp(10), dp(16), dp(10));
        LinearLayout.LayoutParams layout = new LinearLayout.LayoutParams(-1, -2); layout.setMargins(0, dp(6), 0, dp(6)); button.setLayoutParams(layout);
    }
    private static String stateName(String value) {
        switch (value) {
            case "waiting_for_human": return "Needs your decision";
            case "running": return "Working";
            case "verifying": return "Checking work";
            case "completed": return "Completed";
            case "interrupted": return "Stopped";
            case "failed": return "Failed";
            case "starting": return "Starting";
            default: return PanelText.plain(value);
        }
    }
    private void shell() {
        LinearLayout root = column(); root.setBackgroundColor(Color.rgb(18, 24, 34));
        text(root, "Halcyonic", 28);
        connection = text(root, "Connecting to your computer…", 16);
        LinearLayout nav = new LinearLayout(this); nav.setOrientation(LinearLayout.HORIZONTAL); root.addView(nav);
        for (String name : new String[] {"Projects", "Workspace", "Other apps"}) {
            Button b = new Button(this); b.setAllCaps(false); b.setText(name); b.setMinHeight(dp(52)); styleButton(b, name); b.setContentDescription(name); LinearLayout.LayoutParams navLayout = new LinearLayout.LayoutParams(0, -2, 1); navLayout.setMargins(dp(3), dp(4), dp(3), dp(4)); nav.addView(b, navLayout);
            b.setOnClickListener(v -> {
                if (busy) return;
                if (name.equals("Projects")) { if (!saveState()) return; projectId = workId = executionId = null; draft = ""; closeReview(); show(); }
                else if (name.equals("Workspace")) returnToWorkspace();
                else { if (!saveState()) return; try { startActivity(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); } catch (Exception error) { connection.setText("Other apps could not open."); } }
            });
        }
        if (files.developmentAllowed) {
            Button mode = new Button(this); mode.setText("Connection (development)"); mode.setAllCaps(false); styleButton(mode, "Connection (development)"); root.addView(mode);
            mode.setOnClickListener(v -> { if (busy || !saveState()) return; LinearLayout choices = dialog("Development connection");
                text(choices, "Use the paired computer, or the existing USB development connection. Changing connection never changes its saved credential.", 18);
                button(choices, "Use paired computer", () -> switchConnection(false));
                button(choices, "Use USB connection (development)", () -> switchConnection(true));
            });
        }
        ScrollView scroll = new ScrollView(this); content = column(); scroll.addView(content); root.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        setContentView(root);
    }
    private void switchConnection(boolean usb) {
        try { files.useDevelopmentUsb(usb); closeReview(); identity = null; snapshot = null; projectId = workId = executionId = null; instruction = null; draft = ""; continuation = null; generation++; busy = false; if (transport != null) transport.cancel(); poll(true); }
        catch (Exception error) { connection.setText("That connection could not be selected."); }
    }
    private void loadSaved() {
        instruction = null;
        try { drafts = files.exists(PanelFiles.DRAFTS) ? PanelDrafts.parse(files.read(PanelFiles.DRAFTS, PanelDrafts.MAXIMUM_BYTES)) : new PanelDrafts(); draftsHealthy = true; }
        catch (Exception error) { draftsHealthy = false; }
        try { continuation = PanelContinuation.parse(files.read(PanelFiles.STATE, PanelContinuation.MAXIMUM_BYTES)); }
        catch (Exception error) { continuation = null; }
        projectId = workId = executionId = null; draft = ""; spoken = false;
        if (continuation != null) {
            projectId = continuation.string("project_id"); workId = continuation.string("workstream_id"); executionId = continuation.string("execution_id");
            draft = continuation.draft(); spoken = continuation.spoken();
        }
    }
    private void rememberDraft() { if (instruction != null) draft = instruction.getText().toString(); }
    private boolean saveCurrentDraft() {
        rememberDraft();
        if (identity == null || snapshot == null || executionId == null) return true;
        if (!draftsHealthy) { connection.setText("Your saved drafts could not be read. Keep this draft open or return to Workspace."); return false; }
        try {
            PanelContinuation record = PanelContinuation.selection(identity, snapshot, projectId, workId, executionId, draft, spoken, continuation);
            PanelDrafts candidate = PanelDrafts.parse(drafts.json()); candidate.put(record);
            files.write(PanelFiles.DRAFTS, candidate.json()); drafts = candidate;
            return true;
        } catch (Exception error) { connection.setText("This draft could not be saved. Keep it open, or clear an earlier saved draft first."); return false; }
    }
    private boolean saveState() {
        if (!saveCurrentDraft()) return false;
        if (identity == null || snapshot == null) return true;
        try {
            continuation = PanelContinuation.selection(identity, snapshot, projectId, workId, executionId, draft, spoken, continuation);
            files.write(PanelFiles.STATE, continuation.json());
            return true;
        } catch (Exception error) { connection.setText("This view could not be saved. Keep it open or return to Workspace."); return false; }
    }
    private void restoreDraft() {
        draft = ""; spoken = false;
        if (identity == null || snapshot == null || workId == null || executionId == null) return;
        PanelContinuation saved = drafts.find(identity.key, snapshot.journal, workId, executionId);
        if (saved != null) { draft = saved.draft(); spoken = saved.spoken(); }
    }
    private void closeReview() { if (review != null) { review.dismiss(); review = null; } }

    private void poll(boolean rebuild) {
        ui.removeCallbacks(refresh);
        if (!visible || busy) return;
        busy = true; int requestGeneration = generation;
        PanelTransport next = new PanelTransport(files::connection); transport = next;
        worker.execute(() -> {
            PanelIdentity target = null; PanelModel model = null; String failure = null;
            try {
                target = files.connection();
                PanelTransport.Result response = next.request(target, "GET", "/api/snapshot", null);
                if (response.status == 401 || response.status == 403) failure = "This connection was refused. Pair again in Workspace.";
                else if (response.status != 200) failure = "Your computer is not available.";
                else model = new PanelModel(response.body);
            } catch (Exception error) { failure = "Your computer is not available. Pair in Workspace to use this panel."; }
            PanelIdentity receivedTarget = target; PanelModel received = model; String message = failure;
            ui.post(() -> {
                if (!visible || generation != requestGeneration) return;
                busy = false;
                if (!saveState()) { ui.postDelayed(refresh, 3000); return; }
                if (received == null) {
                    snapshot = null; identity = null; closeReview(); instruction = null;
                    connection.setText(message); content.removeAllViews(); text(content, "Your agents continue on your computer. Open Workspace to check the connection.", 20);
                } else {
                    String priorContext = snapshot == null || workId == null ? null : snapshot.context(workId);
                    boolean connectionChanged = identity != null && !identity.same(receivedTarget);
                    boolean journalChanged = snapshot != null && !snapshot.journal.equals(received.journal);
                    identity = receivedTarget; snapshot = received;
                    if (continuation != null && !continuation.matches(identity, snapshot) && continuation.value.get("instruction") != null
                        && identity.key.equals(continuation.string("connection_key")) && snapshot.journal.equals(continuation.string("journal_id"))) {
                        Map<String, Object> oldWork = snapshot.workstreams.get(continuation.string("workstream_id"));
                        if (oldWork != null && continuation.string("project_id").equals(oldWork.get("project_id"))) {
                            try { PanelDrafts candidate = PanelDrafts.parse(drafts.json()); candidate.put(continuation); files.write(PanelFiles.DRAFTS, candidate.json()); drafts = candidate; }
                            catch (Exception error) { connection.setText("The previous run’s draft could not be saved. Return to Workspace to recover it."); return; }
                        }
                    }
                    if (connectionChanged || journalChanged || (continuation != null && !continuation.matches(identity, snapshot))) {
                        closeReview(); projectId = workId = executionId = null; draft = ""; spoken = false; instruction = null; continuation = null;
                        files.remove(PanelFiles.STATE); rebuildAfterReset();
                    } else if (workId != null) {
                        Map<String, Object> run = snapshot.execution(workId);
                        String current = run == null ? null : PanelJson.text(run.get("execution_id"));
                        if (executionId != null && !executionId.equals(current)) { closeReview(); executionId = current; instruction = null; continuation = null; restoreDraft(); show(); }
                    }
                    boolean contextChanged = priorContext != null && workId != null && !priorContext.equals(snapshot.context(workId));
                    if (contextChanged) closeReview();
                    connection.setText(status());
                    if (contextChanged || rebuild || (instruction == null && review == null)) show();
                }
                ui.postDelayed(refresh, 3000);
            });
        });
    }
    private void rebuildAfterReset() { show(); }
    private String status() {
        if (commandId != null) for (Object item : snapshot.commands) {
            Map<String, Object> command = PanelJson.object(item);
            if (!commandId.equals(command.get("command_id"))) continue;
            String state = PanelJson.text(command.get("status"));
            if (state.equals("accepted")) return "Your request was accepted. Waiting for the agent.";
            if (state.equals("completed")) return "The agent confirmed your request.";
            if (state.equals("failed")) {
                Map<String, Object> failure = PanelJson.object(command.get("failure"));
                return "The request failed." + ("unknown".equals(failure.get("effect")) ? " Its effect is unknown; check the task before trying again." : "");
            }
            if (state.equals("rejected")) return "Your request was refused. Check this task before trying again.";
        }
        return identity != null && identity.scheme.equals("ws") ? "USB development connection · local owner. Agents work on your computer." : "Connected to your paired computer. Agents work there independently.";
    }

    private void show() {
        instruction = null; content.removeAllViews();
        if (snapshot == null) return;
        try {
            if (projectId == null) {
                text(content, "Projects", 26);
                if (snapshot.projects.isEmpty()) text(content, "No projects yet. Create one in Workspace.", 20);
                for (Map<String, Object> project : snapshot.projects.values()) button(content, PanelText.plain(PanelJson.text(project.get("name"))), () -> { if (!saveState()) return; projectId = PanelJson.text(project.get("project_id")); continuation = null; show(); saveState(); });
                return;
            }
            Map<String, Object> project = snapshot.projects.get(projectId);
            if (project == null) { projectId = null; show(); return; }
            text(content, "Project · " + PanelText.plain(PanelJson.text(project.get("name"))), 23);
            if (workId == null) {
                text(content, "Tasks", 26);
                for (Map<String, Object> work : snapshot.workstreams.values()) if (projectId.equals(work.get("project_id"))) {
                    Map<String, Object> taskRun = snapshot.execution(PanelJson.text(work.get("workstream_id")));
                    boolean simulated = taskRun != null && PanelJson.bool(PanelJson.object(taskRun.get("runtime")).get("synthetic"));
                    String label = PanelText.plain(PanelJson.text(work.get("title"))) + "\n" + stateName(PanelJson.text(work.get("status"))) + (simulated ? " · simulated" : "");
                    button(content, label, () -> { if (!saveState()) return; workId = PanelJson.text(work.get("workstream_id")); Map<String, Object> run = snapshot.execution(workId); executionId = run == null ? null : PanelJson.text(run.get("execution_id")); continuation = null; restoreDraft(); show(); saveState(); });
                }
                return;
            }
            Map<String, Object> work = snapshot.workstreams.get(workId);
            if (work == null || !projectId.equals(work.get("project_id"))) { workId = executionId = null; draft = ""; show(); return; }
            text(content, "Task · " + PanelText.plain(PanelJson.text(work.get("title"))), 25);
            if (work.get("objective") != null) text(content, "Objective\n" + PanelText.plain(PanelJson.text(work.get("objective"))), 18);
            Map<String, Object> run = snapshot.execution(workId);
            if (run == null) { text(content, "This task has no current agent. Start work in Workspace.", 20); return; }
            executionId = PanelJson.text(run.get("execution_id"));
            Map<String, Object> runtime = PanelJson.object(run.get("runtime"));
            text(content, "Agent · " + PanelText.plain(PanelJson.text(runtime.get("display_name"))) + (PanelJson.bool(runtime.get("synthetic")) ? " · simulated" : "") + "\n" + stateName(PanelJson.text(run.get("status"))), 19);
            button(content, "Read recorded activity", this::activity);
            for (Object item : PanelJson.array(run.get("pending_approvals"))) {
                Map<String, Object> request = PanelJson.object(item);
                button(content, "Review agent permission request", () -> approval(request));
            }
            for (Object item : PanelJson.array(run.get("pending_questions"))) {
                Map<String, Object> question = PanelJson.object(item);
                button(content, "Read agent question", () -> question(question));
            }
            for (PanelContinuation earlier : drafts.earlier(identity.key, snapshot.journal, workId, executionId)) {
                button(content, "Read saved draft from an earlier run", () -> reviewed("Draft for an earlier run", PanelText.plain(earlier.draft()) + "\n\nThis draft belongs to a previous run. Copying it does not send it.", "Choose what to do", () -> {
                    LinearLayout actions = dialog("Earlier draft");
                    button(actions, "Copy into this run’s instruction", () -> { if (!saveCurrentDraft()) return; draft = earlier.draft(); spoken = earlier.spoken(); instruction = null; closeReview(); show(); saveState(); });
                    button(actions, "Delete this saved draft", () -> { try { PanelDrafts candidate = PanelDrafts.parse(drafts.json()); candidate.remove(candidate.find(earlier.string("connection_key"), earlier.string("journal_id"), earlier.string("workstream_id"), earlier.string("execution_id"))); files.write(PanelFiles.DRAFTS, candidate.json()); drafts = candidate; closeReview(); show(); } catch (Exception error) { connection.setText("The draft could not be removed."); } });
                }));
            }
            if (snapshot.instruct(run)) {
                text(content, spoken ? "Your instruction · from speech, review every word" : "Your instruction", 20);
                instruction = new EditText(this); instruction.setText(draft); instruction.setTextColor(Color.WHITE); instruction.setContentDescription("Your instruction"); instruction.setTextSize(20); instruction.setMinLines(3); instruction.setMaxLines(8);
                instruction.setFilters(new InputFilter[] {new InputFilter.LengthFilter(32000)}); instruction.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE | android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
                content.addView(instruction, new LinearLayout.LayoutParams(-1, -2));
                button(content, "Review instruction", () -> { rememberDraft(); if (!draft.trim().isEmpty()) reviewInstruction(); });
            } else text(content, "Instructions are unavailable while this agent needs a decision or cannot receive one.", 18);
            if (snapshot.interrupt(run)) button(content, "Stop this agent", () -> reviewed("Stop this agent?", "This asks the agent to stop its current turn. The agent must confirm before this task is stopped.", "Send stop request", () -> send("execution.interrupt", PanelJson.map("execution_id", executionId), null, null)));
        } catch (Exception error) { content.removeAllViews(); text(content, "This task could not be read safely. Open Workspace.", 20); }
    }

    private void reviewInstruction() {
        String text = draft;
        reviewed("Review your instruction", PanelText.plain(text), "Send instruction", () -> send("execution.send_instruction", PanelJson.map("execution_id", executionId, "text", text), null, null));
    }
    private void approval(Map<String, Object> request) {
        try {
            Map<String, Object> subject = PanelJson.object(request.get("subject"));
            if (!"tool_use".equals(subject.get("kind"))) throw PanelJson.invalid();
            String whole = "Agent request\nTool · " + PanelText.plain(PanelJson.text(subject.get("tool_name"))) + "\n\n" + PanelText.plain(PanelJson.text(subject.get("summary")));
            boolean canRespond = snapshot.capability(snapshot.execution(workId), "respond_to_approval") && PanelModel.running(snapshot.execution(workId));
            boolean canApprove = canRespond && PanelJson.bool(request.get("approvable"));
            if (!canRespond) { reviewed("Read the agent’s request", whole + "\n\nThis runtime cannot receive a permission response here.", "Done", this::closeReview); return; }
            reviewed("Read the agent’s request", whole + (canApprove ? "" : "\n\nThis request was shortened and cannot be approved."), "Choose a response", () -> {
                LinearLayout choices = dialog("Choose a response");
                text(choices, "This response applies only to the request you just read. A changed request requires new review.", 20);
                if (canApprove) button(choices, "Approve this request", () -> send("execution.respond_to_approval", PanelJson.map("execution_id", executionId, "approval_id", request.get("approval_id"), "decision", "approve", "message", null), request, null));
                button(choices, "Deny this request", () -> send("execution.respond_to_approval", PanelJson.map("execution_id", executionId, "approval_id", request.get("approval_id"), "decision", "deny", "message", null), request, null));
            });
        } catch (Exception error) { connection.setText("This request cannot be read safely. Open Workspace."); }
    }
    private void question(Map<String, Object> request) {
        try {
            StringBuilder whole = new StringBuilder("Agent questions\n");
            for (Object item : PanelJson.array(request.get("prompts"))) {
                Map<String, Object> p = PanelJson.object(item);
                whole.append('\n').append(PanelText.plain(PanelJson.nullableText(p.get("header")))).append('\n').append(PanelText.plain(PanelJson.text(p.get("text")))).append('\n');
                for (Object option : PanelJson.array(p.get("options"))) {
                    Map<String, Object> o = PanelJson.object(option);
                    whole.append("• ").append(PanelText.plain(PanelJson.text(o.get("label")))).append('\n').append(PanelText.plain(PanelJson.nullableText(o.get("description")))).append('\n');
                }
            }
            if (!snapshot.capability(snapshot.execution(workId), "answer_question") || !PanelModel.running(snapshot.execution(workId)) || !PanelModel.questionSupported(request)) { reviewed("Read the agent’s question", whole + "\nThis question cannot be answered here. Secret answers are never sent or saved. You can stop the agent or open Workspace.", "Done", this::closeReview); return; }
            reviewed("Read the whole question", whole.toString(), "Write answers", () -> editAnswers(request));
        } catch (Exception error) { connection.setText("This question cannot be read safely. Open Workspace."); }
    }
    private void editAnswers(Map<String, Object> request) {
        LinearLayout body = dialog("Your answers");
        List<List<CheckBox>> choices = new ArrayList<>(); List<EditText> written = new ArrayList<>();
        List<Object> prompts = PanelJson.array(request.get("prompts"));
        for (Object item : prompts) {
            Map<String, Object> p = PanelJson.object(item);
            text(body, PanelText.plain(PanelJson.text(p.get("text"))), 20);
            List<CheckBox> options = new ArrayList<>();
            for (Object option : PanelJson.array(p.get("options"))) {
                Map<String, Object> o = PanelJson.object(option); CheckBox check = new CheckBox(this); check.setText(PanelText.plain(PanelJson.text(o.get("label")))); check.setTextColor(Color.WHITE); check.setTextSize(19); check.setMinHeight(dp(52)); body.addView(check); options.add(check);
                if (!PanelJson.bool(p.get("multiple"))) check.setOnCheckedChangeListener((v, checked) -> { if (checked) for (CheckBox other : options) if (other != check) other.setChecked(false); });
            }
            choices.add(options);
            EditText entry = null;
            if (PanelJson.bool(p.get("free_text"))) { entry = new EditText(this); entry.setHint("Your answer"); entry.setTextColor(Color.WHITE); entry.setContentDescription("Your answer to " + PanelText.plain(PanelJson.text(p.get("text")))); entry.setMinLines(2); entry.setTextSize(20); entry.setFilters(new InputFilter[] {new InputFilter.LengthFilter(4000)}); body.addView(entry, new LinearLayout.LayoutParams(-1, -2)); }
            written.add(entry);
        }
        button(body, "Review all answers", () -> {
            List<Object> answers = new ArrayList<>(); StringBuilder summary = new StringBuilder("Your answers to the agent\n");
            for (int i = 0; i < prompts.size(); i++) {
                Map<String, Object> p = PanelJson.object(prompts.get(i)); List<Object> selected = new ArrayList<>(); List<Object> options = PanelJson.array(p.get("options"));
                for (int j = 0; j < choices.get(i).size(); j++) if (choices.get(i).get(j).isChecked()) selected.add(PanelJson.object(options.get(j)).get("label"));
                String text = written.get(i) == null ? null : written.get(i).getText().toString(); if (text != null && text.trim().isEmpty()) text = null;
                answers.add(PanelJson.map("key", p.get("key"), "selected", selected, "text", text));
                summary.append('\n').append(PanelText.plain(PanelJson.text(p.get("text")))).append('\n');
                for (Object label : selected) summary.append("• ").append(PanelText.plain(PanelJson.text(label))).append('\n');
                if (text != null) summary.append(PanelText.plain(text)).append('\n');
            }
            if (!snapshot.answer(snapshot.execution(workId), request, answers)) { connection.setText("Answer every question using the offered choices or allowed text."); return; }
            reviewed("Review every answer", summary.toString(), "Send answers", () -> send("execution.answer_question", PanelJson.map("execution_id", executionId, "question_id", request.get("question_id"), "answers", answers), request, answers));
        });
    }

    private LinearLayout dialog(String title) {
        closeReview(); Dialog next = new Dialog(this); review = next;
        LinearLayout root = column(); root.setBackgroundColor(Color.rgb(18, 24, 34)); text(root, title, 25);
        button(root, "Back", this::closeReview);
        ScrollView scroll = new ScrollView(this); LinearLayout body = column(); scroll.addView(body); root.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        next.setContentView(root); next.show(); next.getWindow().setLayout(-1, -1); next.getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        return body;
    }
    /** Entire untruncated body must be offered and scrolled before the deliberate next action. */
    private void reviewed(String title, String whole, String label, Runnable next) {
        LinearLayout body = dialog(title); text(body, whole, 20);
        ScrollView scroll = (ScrollView) body.getParent(); LinearLayout root = (LinearLayout) scroll.getParent();
        Button continueButton = button(root, label, next); continueButton.setEnabled(false);
        Dialog shown = review;
        Runnable read = () -> { if (review == shown && visible && body.getHeight() > 0 && scroll.getHeight() > 0 && scroll.getScrollY() + scroll.getHeight() >= body.getHeight()) continueButton.setEnabled(true); };
        scroll.setOnScrollChangeListener((v, x, y, oldX, oldY) -> read.run()); scroll.post(read);
    }

    private void send(String type, Map<String, Object> payload, Map<String, Object> request, List<Object> answers) {
        if (snapshot == null || identity == null || busy || !visible) return;
        rememberDraft();
        final PanelIdentity target = identity; final String journal = snapshot.journal, selectedWork = workId, selectedProject = projectId, selectedExecution = executionId;
        final int requestGeneration = generation;
        if (!selectedExecution.equals(payload.get("execution_id"))) return;
        final Map<String, Object> envelope = PanelModel.command(type, payload);
        closeReview(); busy = true; connection.setText("Checking this task before sending…");
        PanelTransport next = new PanelTransport(files::connection); transport = next;
        worker.execute(() -> {
            String result; boolean submitted = false, accepted = false; PanelModel fresh = null;
            try {
                PanelTransport.Result response = next.request(target, "GET", "/api/snapshot", null);
                if (response.status != 200) throw new IOException();
                fresh = new PanelModel(response.body); Map<String, Object> run = fresh.execution(selectedWork);
                boolean same = fresh.journal.equals(journal) && run != null && selectedExecution.equals(run.get("execution_id")) && selectedProject.equals(run.get("project_id"));
                boolean allowed = same && (type.equals("execution.send_instruction") ? fresh.instruct(run)
                    : type.equals("execution.interrupt") ? fresh.interrupt(run)
                    : type.equals("execution.respond_to_approval") ? fresh.decision(run, request, PanelJson.text(payload.get("decision")))
                    : type.equals("execution.answer_question") && fresh.answer(run, request, answers));
                if (!allowed) result = "This task or request changed. Read it again before sending.";
                else {
                    response = next.request(target, "POST", "/api/commands", PanelJson.encode(envelope));
                    boolean replied = response.status == 200 || response.status == 202 || response.status == 422;
                    if (response.status == 401 || response.status == 403) result = "This connection was refused. Pair again in Workspace.";
                    else if (replied) {
                        Map<String, Object> reply = PanelJson.object(PanelJson.parse(response.body)); Map<String, Object> command = PanelJson.object(reply.get("command"));
                        if (!envelope.get("command_id").equals(command.get("command_id"))) throw new IOException();
                        String state = PanelJson.text(command.get("status"));
                        submitted = true; accepted = state.equals("accepted") || state.equals("completed");
                        result = state.equals("rejected") ? "Your request was refused." : state.equals("completed") ? "The agent confirmed your request." : "Your request was accepted. Waiting for the agent.";
                    } else result = "The host did not confirm this request. Check the task before trying again.";
                }
            } catch (PanelTransport.Refused error) { result = error.unknownEffect ? "The connection ended after sending. The effect is unknown; check this task before trying again." : "Your request was not sent. Check the connection."; }
            catch (Exception error) { result = "The host did not confirm this request. Check the task before trying again."; }
            String message = result; boolean recorded = submitted, acceptedInstruction = accepted; PanelModel latest = fresh;
            ui.post(() -> {
                if (!visible || generation != requestGeneration) return;
                busy = false; if (latest != null) snapshot = latest;
                if (recorded) { commandId = PanelJson.text(envelope.get("command_id")); if (acceptedInstruction && type.equals("execution.send_instruction")) { draft = ""; spoken = false; instruction = null; } }
                connection.setText(message); show(); saveState(); ui.postDelayed(refresh, 1000);
            });
        });
    }

    private void activity() {
        if (snapshot == null || identity == null || busy) return;
        final PanelIdentity target = identity; final String work = workId, journal = snapshot.journal; final int requestGeneration = generation;
        busy = true; PanelTransport next = new PanelTransport(files::connection); transport = next;
        worker.execute(() -> {
            String whole;
            try {
                PanelTransport.Result response = next.request(target, "GET", "/api/events?after=0&limit=50&workstream_id=" + work, null);
                if (response.status != 200) throw new IOException();
                Map<String, Object> value = PanelJson.object(PanelJson.parse(response.body));
                if (!journal.equals(PanelJson.object(value.get("journal")).get("journal_id"))) throw new IOException();
                StringBuilder text = new StringBuilder("First 50 recorded events for this task. Open Workspace for its full activity, changes and checks. Agent words are reported; this panel does not infer completion.\n");
                for (Object item : PanelJson.array(value.get("events"))) {
                    Map<String, Object> event = PanelJson.object(PanelJson.object(item).get("event"));
                    text.append('\n').append(PanelText.plain(PanelJson.text(event.get("event_type")))).append('\n');
                    Map<String, Object> payload = PanelJson.object(event.get("payload"));
                    for (String field : new String[] {"text", "message", "summary"}) if (payload.get(field) instanceof String) text.append("Reported · ").append(PanelText.plain((String) payload.get(field))).append('\n');
                }
                whole = text.toString();
            } catch (Exception error) { whole = "Activity could not be read. Check your connection in Workspace."; }
            String shown = whole;
            ui.post(() -> { if (!visible || generation != requestGeneration) return; busy = false; reviewed("Recorded activity", shown, "Done", this::closeReview); ui.postDelayed(refresh, 3000); });
        });
    }
    private void returnToWorkspace() {
        rememberDraft();
        if (!saveState()) return;
        try {
            // Saving ordinary state is independent of the one-shot return mark. Failed launches
            // remove that mark; resuming Unity through another route cannot consume its outbound.
            files.remove(PanelFiles.RETURN);
            if (identity != null && snapshot != null) {
                if (!identity.same(files.connection())) throw new IOException();
                continuation = PanelContinuation.selection(identity, snapshot, projectId, workId, executionId, draft, spoken, continuation);
                files.write(PanelFiles.STATE, continuation.json()); files.write(PanelFiles.RETURN, continuation.json());
            }
            startActivity(new Intent().setClassName(getPackageName(), "com.unity3d.player.UnityPlayerGameActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            finishAndRemoveTask();
        } catch (Exception error) { files.remove(PanelFiles.RETURN); connection.setText("Workspace could not open. Your draft remains here."); }
    }
}
