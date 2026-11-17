package com.halcyonic.panel;

import android.app.Activity;
import android.app.Dialog;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.Typeface;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.text.InputFilter;
import android.view.View;
import android.view.MotionEvent;
import android.os.SystemClock;
import android.text.TextWatcher;
import android.text.Editable;
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
public class AgentPanelActivity extends Activity {
    private final Handler ui = new Handler(Looper.getMainLooper());
    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final PanelUiState authority = new PanelUiState();
    private final PanelRestore restore = new PanelRestore();
    private long reviewOwner;
    private final List<Runnable> reviewReads = new ArrayList<>();
    private PanelContinuation incomingRecord;
    private String incomingId;
    private String storageWarning;
    private PanelFiles files;
    private PanelTransport transport;
    private PanelIdentity identity;
    private PanelModel snapshot;
    private PanelContinuation continuation;
    private PanelDrafts drafts = new PanelDrafts();
    private boolean draftsHealthy = true;
    private LinearLayout content;
    private PanelLoadingText connection;
    private TextView requestAvailability;
    private Button presentationButton;
    private TextView brand;
    private LinearLayout navRow, shellRoot;
    private ScrollView scroll;
    private LinearLayout developmentControls;
    private boolean compactPanel;
    private long checkedAt;
    private LinearLayout requestActions;
    private EditText instruction, requestEditor;
    private String requestId, requestPending, requestText="", surface="task";
    private boolean requestSpoken;
    private long requestPart;
    private Map<String,Object> agentOffer, requestView;
    private Dialog review;
    private PanelProjectOutput.Lease outputLease;
    private PanelToolResults.Lease resultLease;
    private String projectId, workId, executionId, draft = "", commandId;
    private boolean spoken, busy, preview, developmentAllowed;
    /** Decided once, at creation: whether this panel may stay capturable (PanelCapture). Every FLAG_SECURE site reads it. */
    private boolean capturable;
    private volatile boolean visible;
    private volatile int generation;
    private final ExecutorService speechWorker = Executors.newSingleThreadExecutor();
    private final PanelVoice.Gesture voiceGesture = new PanelVoice.Gesture();
    private PanelVoice.Lease voiceLease;
    private PanelVoiceCapture voiceCapture;
    private PanelTransport voiceTransport;
    private PanelVoice.Audio voiceAudio;
    private EditText voiceEditor;
    private String voiceRoute, voiceFingerprint;
    private TextView voiceStatus;
    private Button voiceButton;
    private boolean voicePermissionAtPress;
    private final Runnable refresh = () -> poll(false);

    /** Only the internal development subclass has a never-private preview window. */
    protected boolean syntheticSurface() { return false; }
    /**
     * Only a development preview variant opens straight onto one synthetic task, compact, so an unworn headset with no
     * input focus can be captured in that state (DEVICE, 23:54Z); never an extra on the real panel.
     */
    protected String previewTask() { return null; }
    @Override public void onCreate(Bundle state) {
        super.onCreate(state);
        // The panel opens compact: the card or the strip, the film's path (the lead's 19:36Z). The full panel is one press away
        // through More, for the actions the card does not hold.
        compactPanel = state == null || state.getBoolean("halcyonic.panel.compact", true);
        authority.compact(compactPanel);
        developmentAllowed = (getApplicationInfo().flags & android.content.pm.ApplicationInfo.FLAG_DEBUGGABLE) != 0;
        capturable = PanelCapture.allowed(this);
        if (syntheticSurface()) {
            preview = true;
            if (!developmentAllowed) { finish(); return; }
            // This branch initializes no app-private store, connection or saved editor context.
            try (java.io.InputStream source = getAssets().open("halcyonic-native-panel-preview.json")) {
                java.io.ByteArrayOutputStream bytes = new java.io.ByteArrayOutputStream(); byte[] buffer = new byte[4096]; int count;
                while ((count = source.read(buffer)) >= 0) { if (bytes.size() + count > 256 * 1024) throw new IOException(); bytes.write(buffer, 0, count); }
                snapshot = new PanelModel(new String(bytes.toByteArray(), java.nio.charset.StandardCharsets.UTF_8));
                for (Map<String, Object> run : snapshot.executions.values()) if (!PanelJson.bool(PanelJson.object(run.get("runtime")).get("synthetic"))) throw new IOException();
                String opens = previewTask();
                if (opens != null) {
                    for (Map<String, Object> work : snapshot.workstreams.values()) if (opens.equals(PanelJson.text(work.get("title")))) {
                        projectId = PanelJson.text(work.get("project_id")); workId = PanelJson.text(work.get("workstream_id"));
                        Map<String, Object> run = snapshot.execution(workId); executionId = run == null ? null : PanelJson.text(run.get("execution_id"));
                    }
                    if (workId == null) throw new IOException();
                    compactPanel = true; authority.compact(true);
                }
                shell(); connection.setText("Synthetic preview: no live work, and nothing is sent."); show();
            } catch (Exception error) { finish(); }
            return;
        }
        // The real panel's window remains secure for its entire lifetime, including while the
        // separate preview is in front. No formerly private compositor buffer becomes capturable.
        if (!capturable) getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        files = new PanelFiles(this); shell();
    }
    @Override protected void onSaveInstanceState(Bundle state) {
        state.putBoolean("halcyonic.panel.compact", compactPanel);
        super.onSaveInstanceState(state);
    }
    @Override public void onResume() {
        cancelVoice(); super.onResume(); visible = true; authority.resumed(true); authority.top(false); authority.awaitingProjection(); generation++; busy = false;
        if (preview) { if (snapshot != null && content != null) { authority.projected(); show(); } return; }
        loadSaved(); poll(true);
    }
    @Override public void onPause() {
        if (connection != null) connection.stop();
        cancelVoice(); PanelContinuation current = editorRecord(); boolean persisted = saveState(); restore.paused(current, persisted);
        authority.resumed(false); visible = false; generation++; busy = false;
        if (connection != null && compactPanel) connection.setText(withStorageWarning(monitorFreshness(true)));
        ui.removeCallbacks(refresh);
        if (transport != null) transport.cancel();
        closeReview(); instruction = null; requestEditor=null; agentOffer=requestView=null;
        super.onPause();
    }
    @Override protected void onStop() {
        cancelNetwork();
        if (connection != null && compactPanel) connection.setText(withStorageWarning(monitorFreshness(true)));
        super.onStop();
    }
    @Override protected void onNewIntent(Intent intent) {
        cancelVoice(); super.onNewIntent(intent);
        if (syntheticSurface()) { setIntent(new Intent()); closeReview(); return; }
        // Exported OS launcher entry never accepts selection, credential or command extras.
        if (!capturable) getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE);
        if (!preview && !saveState()) { restore.paused(editorRecord(), false); connection.setText("Your unsaved draft must be preserved before changing this view."); return; }
        preview = false; setIntent(new Intent());
        closeReview(); loadSaved();
        if (visible) { generation++; busy = false; if (transport != null) transport.cancel(); poll(true); }
    }
    @Override public void onTopResumedActivityChanged(boolean top) {
        super.onTopResumedActivityChanged(top); authority.top(top);
        if (!top) {
            if (connection != null) connection.stop();
            cancelVoice(); rememberDraft(); closeReview(); generation++; busy = false; ui.removeCallbacks(refresh); if (transport != null) transport.cancel();
            if (visible && authority.monitor() && !preview) poll(false);
        } else if (visible && !preview) poll(true);
    }
    @Override public void onWindowFocusChanged(boolean focused) {
        super.onWindowFocusChanged(focused); if (!focused) cancelVoice(); authority.activityFocus(focused);
        if (!focused && review == null) {
            if (connection != null) connection.stop();
            rememberDraft(); generation++; busy = false; if (transport != null) transport.cancel(); ui.removeCallbacks(refresh);
            if (visible && authority.monitor() && !preview) poll(false);
        }
        else if (focused && visible && review == null && !preview && !busy) poll(true);
    }
    private boolean ownKeyboard() {
        View focused = review == null ? getCurrentFocus() : review.getCurrentFocus();
        android.view.inputmethod.InputMethodManager keyboard = (android.view.inputmethod.InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
        return focused instanceof EditText && keyboard != null && keyboard.isActive(focused) && keyboard.isAcceptingText();
    }
    private final class ReviewDialog extends Dialog {
        final long owner; boolean hadFocus;
        ReviewDialog(long owner) { super(AgentPanelActivity.this); this.owner = owner; }
        @Override public void onWindowFocusChanged(boolean focused) {
            super.onWindowFocusChanged(focused); authority.dialogFocus(owner, focused);
            if (review == this) for (Runnable update : new ArrayList<>(reviewReads)) getWindow().getDecorView().post(update);
            if (review == this && hadFocus && !focused && !ownKeyboard()) closeReview();
            hadFocus |= focused;
        }
    }
    @Override public void onDestroy() { cancelVoice(); speechWorker.shutdownNow(); worker.shutdownNow(); super.onDestroy(); }
    @Override public void onBackPressed() { cancelVoice(); if (review != null) closeReview(); else if (workId != null) { if (!saveState()) return;cancelNetwork(); workId = null; executionId = null; draft = "";loadRequest(null); show(); } else if (projectId != null) { if (!saveState()) return;cancelNetwork(); projectId = null;loadRequest(null); show(); } else super.onBackPressed(); }

    private static InputFilter ordinaryTextLimit() {
        return (source, start, end, destination, replaceStart, replaceEnd) -> {
            int accepted = PanelInputText.acceptedEnd(source, start, end, destination, replaceStart, replaceEnd, 32000);
            return accepted == end ? null : source.subSequence(start, accepted);
        };
    }
    private int dp(int value) { return Math.round(value * getResources().getDisplayMetrics().density); }
    private LinearLayout column() { LinearLayout v = new LinearLayout(this); v.setOrientation(LinearLayout.VERTICAL); v.setPadding(dp(20), dp(16), dp(20), dp(16)); return v; }
    private TextView text(LinearLayout parent, String value, int size) {
        // The kit's roles (the sheet's f1): a heading in the title's style, everything else in body type, white.
        float density = getResources().getDisplayMetrics().density;
        TextView view = size >= 23 ? PanelKit.title(this, value, density) : PanelKit.body(this, value, density);
        view.setPadding(0, PanelKit.px(size >= 23 ? 14 : 6, density), 0, PanelKit.px(6, density));
        parent.addView(view, new LinearLayout.LayoutParams(-1, -2)); return view;
    }
    private Button button(LinearLayout parent, String label, Runnable action) { return button(parent, label, action, false); }
    private Button button(LinearLayout parent, String label, Runnable action, boolean escape) { return button(parent, label, action, escape, false); }
    private Button connectionButton(LinearLayout parent, String label, Runnable action) { return button(parent, label, action, false, true); }
    private Button button(LinearLayout parent, String label, Runnable action, boolean escape, boolean localConnection) {
        Button button = new Button(this); button.setText(label); button.setAllCaps(false); button.setTextSize(18); button.setMinHeight(dp(52));
        parent.addView(button, new LinearLayout.LayoutParams(-1, -2)); styleButton(button, label); button.setContentDescription(label); final Dialog owner = review; final long id = owner == null ? 0 : reviewOwner; final int offeredGeneration = generation; button.setOnClickListener(view -> { if (visible && generation == offeredGeneration && review == owner && (localConnection ? !busy && authority.connectionInteract(id) : authority.interact(id, busy, escape))) { cancelVoice(); action.run(); } }); return button;
    }
    private void styleButton(Button button, String label) {
        // The kit's row form (the sheet's f1 lists; AUDIT's 19:40Z (a)): the words on the card's left edge, no plate at rest, the
        // plain glass where pointed, a main send on the lighter thin plate (visionos-window-example.png), white words, no hue.
        float density = getResources().getDisplayMetrics().density;
        boolean primary = label.startsWith("Send") || label.equals("Workspace") || label.startsWith("Review instruction");
        button.setBackground(PanelKit.rowPlates(primary, density)); button.setTextColor(PanelHud.W1); button.setAllCaps(false);
        button.setTextSize(android.util.TypedValue.COMPLEX_UNIT_PX, PanelKit.px(PanelKit.TYPE, density));
        button.setGravity(android.view.Gravity.CENTER_VERTICAL | android.view.Gravity.START); button.setStateListAnimator(null);
        int inset = PanelKit.px(16, density);
        button.setPadding(inset, 0, inset, 0); button.setMinHeight(PanelKit.px(PanelKit.CORE + 8, density));
        LinearLayout.LayoutParams layout = new LinearLayout.LayoutParams(-1, -2); layout.setMargins(-inset, 0, -inset, 0); button.setLayoutParams(layout);
    }
    private static String stateName(String value) { return PanelModel.stateWord(value); }
    private void shell() {
        LinearLayout root = column(); root.setBackgroundColor(PanelHud.GROUND); shellRoot = root;
        // HALORA in the label role over the page, as the sheet's f1 heads its card.
        brand = PanelKit.label(this, "Halora", getResources().getDisplayMetrics().density); root.addView(brand, new LinearLayout.LayoutParams(-1, -2));
        connection = new PanelLoadingText(this); connection.setTextSize(16); connection.setPadding(0, dp(6), 0, dp(8));
        root.addView(connection, new LinearLayout.LayoutParams(-1, -2));
        connection.setText("Connecting to your computer…");
        LinearLayout nav = new LinearLayout(this); nav.setOrientation(LinearLayout.HORIZONTAL); root.addView(nav); navRow = nav;
        for (String name : new String[] {"Projects", "Workspace", "Other Apps"}) {
            // The places as one row of plain presses of one width (the lead's 19:38Z), never a tab list.
            float density = getResources().getDisplayMetrics().density;
            Button b = PanelKit.press(this, name, false, PanelKit.Glyph.NONE, 0, density); LinearLayout.LayoutParams navLayout = new LinearLayout.LayoutParams(0, PanelKit.px(PanelKit.CORE, density), 1);
            navLayout.setMargins(0, PanelKit.px(14, density), name.equals("Other Apps") ? 0 : PanelKit.px(PanelKit.ROW_GAP / 2f, density), PanelKit.px(8, density)); nav.addView(b, navLayout);
            b.setOnClickListener(v -> {
                if (!authority.navigate() || (busy && name.equals("Projects"))) return;
                cancelVoice();
                if (name.equals("Projects")) { if (!saveState()) return; projectId = workId = executionId = null; draft = "";loadRequest(null); selectionChanged(); }
                else if (name.equals("Workspace")) { cancelNetwork(); returnToWorkspace(); }
                else { if (!saveState() && !saveSelectedContext()) return; cancelNetwork(); try { startActivity(new Intent(Intent.ACTION_MAIN).addCategory(Intent.CATEGORY_HOME).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); } catch (Exception error) { connection.setText("Other Apps could not open."); } }
            });
        }
        presentationButton = new Button(this); presentationButton.setAllCaps(false); presentationButton.setMinHeight(dp(52));
        styleButton(presentationButton, "Compact"); root.addView(presentationButton);
        presentationButton.setOnClickListener(view -> { if (visible && authority.navigate()) changePresentation(!compactPanel); });
        presentationButton.setVisibility(View.GONE);
        developmentControls = new LinearLayout(this); developmentControls.setOrientation(LinearLayout.VERTICAL); root.addView(developmentControls);
        if (developmentAllowed && !preview) {
            Button mode = new Button(this); mode.setText("Connection (development)"); mode.setAllCaps(false); styleButton(mode, "Connection (development)"); developmentControls.addView(mode);
            mode.setOnClickListener(v -> { if (!visible || busy || !authority.connectionInteract(0) || !saveState()) return; LinearLayout choices = dialog("Development connection");
                text(choices, "Use the paired computer, or the existing USB development connection. Changing connection never changes its saved credential.", 18);
                connectionButton(choices, "Use paired computer", () -> switchConnection(false));
                connectionButton(choices, "Use USB connection (development)", () -> switchConnection(true));
                connectionButton(choices, "Preview synthetic panel (screenshots allowed)", this::previewSynthetic);
            });
        }
        scroll = new ScrollView(this); content = column(); scroll.addView(content, new android.widget.FrameLayout.LayoutParams(-1, -1)); root.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        setContentView(root);
    }
    private void selectionChanged() {
        cancelNetwork(); closeReview(); authority.awaitingProjection();
        show(); saveState();
        if (preview) { authority.projected(); show(); }
        else authority.navigated(() -> poll(true));
    }
    private void changePresentation(boolean compact) {
        if (compactPanel == compact || workId == null || !authority.navigate()) return;
        // Preserve both independent drafts and their original pending identity before hiding editors.
        if (!authority.changePresentation(compact, saveState())) return;
        cancelNetwork(); closeReview();
        compactPanel = compact;
        connection.setText(withStorageWarning(compact ? monitorFreshness(false) : "Checking this task before enabling controls…"));
        show();
        if (!preview) poll(true); else { authority.projected(); show(); }
    }
    private String monitorFreshness(boolean paused) {
        if (preview) return "Synthetic preview: no live work, and nothing is sent.";
        String when = checkedAt == 0 ? "Not checked yet" : "Last checked " + new java.text.SimpleDateFormat("HH:mm:ss", java.util.Locale.ROOT).format(new java.util.Date(checkedAt));
        return (paused ? "Updates paused" : "Read-only task monitor") + ". " + when + ".";
    }
    /**
     * The compact panel beside the person's show (take 10), in the one design language (FIELD's sheet, tile f7; the lead,
     * 01:05Z): the window is the glass. Its state in spaced capitals with a dot, the task's name, and, while it needs the
     * person, the need as a label over the agent's own words for it (the command, or the question); the presses stand at the
     * foot. Open Task carries the one accent as a lit rim only while something waits; nothing is lit at rest (the lead,
     * 01:05Z, AUDIT's (1)). No monitor line in compact (AUDIT's (2)).
     */
    private void monitorTask(Map<String, Object> summary) {
        float density = getResources().getDisplayMetrics().density;
        boolean needs = PanelJson.bool(summary.get("needs_you"));
        // The window is clear; the card or the strip floats in it, about its middle (FIELD's 19:37Z), never at its top edge.
        shellRoot.setBackground(null); shellRoot.setPadding(0, 0, 0, 0); content.setPadding(0, 0, 0, 0);
        content.setGravity(android.view.Gravity.CENTER);
        String state = PanelJson.text(summary.get("state_word"));
        String task = PanelJson.text(summary.get("task"));
        // One press back into Halora at this task: the act's one word (AUDIT2, 19:36Z), the take-10 beat.
        Runnable openTask = () -> { cancelNetwork(); returnToWorkspace(); };
        String pending = requestPending == null ? null : "The last workspace send is unconfirmed. Its original reference is retained.";
        if (!needs) {
            content.addView(strip(state, task, pending, null, null, "Open Task", PanelKit.Glyph.OPEN, false, openTask), new LinearLayout.LayoutParams(-1, -2));
            return;
        }
        content.addView(needsYouCard(summary, state, task, pending, openTask, density), new LinearLayout.LayoutParams(-2, -2));
    }

    /**
     * The Needs You card, a scaled copy of the headset's approval dialog (f4, LayDialog; FIELD's 19:37Z): the label role, the
     * task's name, the need in the title's style over the command in the well (or the agent's question in body type), and one
     * row of presses, every block on the card's one left edge.
     */
    private LinearLayout needsYouCard(Map<String, Object> summary, String state, String task, String pending, Runnable openTask, float density) {
        LinearLayout card = column();
        card.setBackground(PanelKit.glass(PanelKit.CORNER, density));
        int pad = PanelKit.px(PanelKit.PAD, density);
        card.setPadding(pad, pad, pad, pad);
        // The well and its ticks reach back into the pad (LayDialog's well), so the card never clips its children to its pad.
        card.setClipToPadding(false); card.setClipChildren(false);
        Typeface medium = Typeface.create("sans-serif-medium", Typeface.NORMAL);
        int widest = PanelKit.widthOf(state.toUpperCase(java.util.Locale.ROOT), PanelKit.LABEL, medium, PanelKit.LABEL_TRACKING, density);
        card.addView(PanelKit.label(this, state, density), new LinearLayout.LayoutParams(-1, -2));
        TextView title = PanelKit.title(this, task, density);
        card.addView(title, below(PanelKit.LABEL_GAP, density));
        widest = Math.max(widest, PanelKit.widthOf(task, PanelKit.TITLE, medium, 0, density));
        Object need = summary.get("need");
        Object detail = summary.get("detail");
        if (need != null) {
            String needWords = PanelJson.text(need);
            card.addView(PanelKit.title(this, needWords, density), below(PanelKit.CONTENT_GAP, density));
            widest = Math.max(widest, PanelKit.widthOf(needWords, PanelKit.TITLE, medium, 0, density));
            if (detail != null) {
                String words = PanelJson.text(detail);
                if (!"Agent's Question".equals(needWords)) {
                    // The command in the well, the well's glass on the card's edge (f4), its ticks reaching back into the pad.
                    int reach = PanelKit.wellReach(density);
                    LinearLayout.LayoutParams at = new LinearLayout.LayoutParams(-1, -2);
                    at.setMargins(-reach, PanelKit.px(PanelKit.LABEL_GAP, density), -reach, 0);
                    card.addView(PanelKit.well(this, words, density), at);
                    widest = Math.max(widest, PanelKit.widthOf(words, PanelKit.MONO, Typeface.MONOSPACE, 0, density) + 2 * PanelKit.px(PanelKit.WELL_PAD, density));
                } else {
                    card.addView(PanelKit.body(this, words, density), below(PanelKit.LABEL_GAP, density));
                    widest = Math.max(widest, PanelKit.widthOf(words, PanelKit.TYPE, Typeface.DEFAULT, 0, density));
                }
            }
        }
        if (pending != null) card.addView(PanelKit.body(this, pending, density), below(PanelKit.CONTENT_GAP, density));
        // One row of presses on the same left edge (the lead's 19:38Z), each the row's widest width.
        int width = PanelKit.pressWidth(this, "Open Task", PanelKit.Glyph.OPEN, density);
        LinearLayout row = new LinearLayout(this); row.setOrientation(LinearLayout.HORIZONTAL);
        Button press = PanelKit.press(this, "Open Task", true, PanelKit.Glyph.OPEN, width, density);
        hold(press, openTask);
        row.addView(press);
        card.addView(row, below(PanelKit.PRESSES_GAP, density));
        LinearLayout holder = column(); holder.setGravity(android.view.Gravity.CENTER);
        holder.addView(card, new LinearLayout.LayoutParams(PanelKit.cardWidth(widest, width, density), -2));
        return holder;
    }

    /**
     * The strip at rest, one row on the same glass (FIELD's 19:37Z): the label role, 16 after it the words in body type, then the
     * presses on the right, the main one last, each the widest one's width. A note, when there is one, stands under the words.
     */
    private LinearLayout strip(String labelWord, String words, String note, String secondWord, Runnable second, String mainWord,
        PanelKit.Glyph mainGlyph, boolean wraps, Runnable main) {
        float density = getResources().getDisplayMetrics().density;
        LinearLayout strip = new LinearLayout(this); strip.setOrientation(LinearLayout.HORIZONTAL);
        strip.setGravity(android.view.Gravity.CENTER_VERTICAL);
        strip.setBackground(PanelKit.glass(PanelKit.STRIP_CORNER, density));
        int h = PanelKit.px(PanelKit.STRIP_PAD_H, density), v = PanelKit.px(PanelKit.STRIP_PAD_V, density);
        strip.setPadding(h, v, h, v);
        int width = PanelKit.pressWidth(this, mainWord, mainGlyph, density);
        if (secondWord != null) width = Math.max(width, PanelKit.pressWidth(this, secondWord, PanelKit.Glyph.NONE, density));
        int presses = secondWord != null ? 2 * width + PanelKit.px(PanelKit.ROW_GAP, density) : width;
        LinearLayout said = column(); said.setPadding(0, 0, 0, 0);
        TextView name = PanelKit.body(this, words, density);
        // The strip stands inside the window with a margin of 24 at each side; the words take what the label and presses leave.
        int margin = PanelKit.px(24, density);
        int budget = getResources().getDisplayMetrics().widthPixels - 2 * margin - 2 * PanelKit.px(PanelKit.STRIP_PAD_H, density);
        // The label stands over the words (as the card's label stands over its title), so a long label never cuts the task's name.
        int room = budget - presses - PanelKit.px(PanelKit.STRIP_GAP, density);
        name.setMaxWidth(Math.max(PanelKit.px(120, density), room));
        said.addView(PanelKit.label(this, labelWord, density), new LinearLayout.LayoutParams(-2, -2));
        if (!wraps) { name.setSingleLine(true); name.setEllipsize(android.text.TextUtils.TruncateAt.END); }
        said.addView(name, below(PanelKit.LABEL_GAP / 2f, density));
        name.getLayoutParams().width = -2;
        if (note != null) {
            TextView noted = PanelKit.body(this, note, density); noted.setMaxWidth(name.getMaxWidth());
            said.addView(noted, below(PanelKit.LABEL_GAP, density));
        }
        LinearLayout.LayoutParams saidAt = new LinearLayout.LayoutParams(-2, -2);
        saidAt.setMargins(0, 0, PanelKit.px(PanelKit.STRIP_GAP, density), 0);
        strip.addView(said, saidAt);
        if (secondWord != null) {
            Button other = PanelKit.press(this, secondWord, false, PanelKit.Glyph.NONE, width, density);
            hold(other, second);
            LinearLayout.LayoutParams otherAt = new LinearLayout.LayoutParams(width, PanelKit.px(PanelKit.CORE, density));
            otherAt.setMargins(0, 0, PanelKit.px(PanelKit.ROW_GAP, density), 0);
            strip.addView(other, otherAt);
        }
        Button press = PanelKit.press(this, mainWord, true, mainGlyph, width, density);
        hold(press, main);
        strip.addView(press, new LinearLayout.LayoutParams(width, PanelKit.px(PanelKit.CORE, density)));
        LinearLayout holder = new LinearLayout(this); holder.setGravity(android.view.Gravity.CENTER);
        holder.setPadding(margin, 0, margin, 0);
        holder.addView(strip, new LinearLayout.LayoutParams(-2, -2));
        return holder;
    }

    /**
     * With no task chosen, the panel stands as one strip (AUDIT2's 19:40Z words): HALORA where the person is, the way to choose a
     * task, Open Halora, and More to the full panel, which keeps the actions the card does not (LINK's 19:40Z inventory).
     */
    private void noTaskStrip() {
        float density = getResources().getDisplayMetrics().density;
        shellRoot.setBackground(null); shellRoot.setPadding(0, 0, 0, 0); content.setPadding(0, 0, 0, 0);
        content.setGravity(android.view.Gravity.CENTER);
        // A sentence and two presses do not fit one row, so the no-task state stands as the card's own form (f4): HALORA in the
        // label role, the sentence, then More and Open Halora on one row of one width, the main action last.
        LinearLayout card = column();
        card.setBackground(PanelKit.glass(PanelKit.CORNER, density));
        int pad = PanelKit.px(PanelKit.PAD, density);
        card.setPadding(pad, pad, pad, pad);
        String sentence = "Choose a task in Halora to follow it here.";
        card.addView(PanelKit.label(this, "Halora", density), new LinearLayout.LayoutParams(-1, -2));
        card.addView(PanelKit.body(this, sentence, density), below(PanelKit.LABEL_GAP, density));
        int width = Math.max(PanelKit.pressWidth(this, "More", PanelKit.Glyph.NONE, density), PanelKit.pressWidth(this, "Open Halora", PanelKit.Glyph.WINDOW, density));
        LinearLayout row = new LinearLayout(this); row.setOrientation(LinearLayout.HORIZONTAL);
        Button more = PanelKit.press(this, "More", false, PanelKit.Glyph.NONE, width, density);
        hold(more, () -> { compactPanel = false; authority.compact(false); show(); });
        LinearLayout.LayoutParams moreAt = new LinearLayout.LayoutParams(width, PanelKit.px(PanelKit.CORE, density));
        moreAt.setMargins(0, 0, PanelKit.px(PanelKit.ROW_GAP, density), 0);
        row.addView(more, moreAt);
        Button open = PanelKit.press(this, "Open Halora", true, PanelKit.Glyph.WINDOW, width, density);
        hold(open, () -> { cancelNetwork(); returnToWorkspace(); });
        row.addView(open, new LinearLayout.LayoutParams(width, PanelKit.px(PanelKit.CORE, density)));
        card.addView(row, below(PanelKit.PRESSES_GAP, density));
        int pressesWidth = 2 * width + PanelKit.px(PanelKit.ROW_GAP, density);
        int widest = PanelKit.widthOf(sentence, PanelKit.TYPE, Typeface.DEFAULT, 0, density);
        LinearLayout holder = column(); holder.setGravity(android.view.Gravity.CENTER);
        holder.addView(card, new LinearLayout.LayoutParams(PanelKit.cardWidth(Math.min(widest, pressesWidth), pressesWidth, density), -2));
        content.addView(holder, new LinearLayout.LayoutParams(-1, -2));
    }

    /** A press on the card or a strip, held to the same navigation authority as the panel's own navigation. */
    private void hold(Button press, Runnable action) {
        press.setOnClickListener(view -> { if (!visible || !authority.navigate()) return; cancelVoice(); action.run(); });
    }

    private LinearLayout.LayoutParams below(float gap, float density) {
        LinearLayout.LayoutParams at = new LinearLayout.LayoutParams(-1, -2);
        at.setMargins(0, PanelKit.px(gap, density), 0, 0);
        return at;
    }
    private void cancelNetwork() { if (connection != null) connection.stop(); cancelVoice(); generation++; busy = false; ui.removeCallbacks(refresh); if (transport != null) transport.cancel(); }
    private void switchConnection(boolean usb) {
        try { if (!capturable) getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE); preview = false; files.useDevelopmentUsb(usb); closeReview(); identity = null; snapshot = null; projectId = workId = executionId = null; instruction = null; draft = ""; continuation = null; generation++; busy = false; if (transport != null) transport.cancel(); loadSaved(); poll(true); }
        catch (Exception error) { connection.setText("That connection could not be selected."); }
    }
    private void previewSynthetic() {
        if (!developmentAllowed || !saveState()) return;
        cancelNetwork(); closeReview();
        try { startActivity(new Intent().setClassName(getPackageName(), "com.halcyonic.panel.PanelPreviewActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); }
        catch (Exception error) { connection.setText("The synthetic preview could not open."); }
    }
    private void loadSaved() {
        instruction = null;requestEditor=null; closeReview();
        try { drafts = files.exists(PanelFiles.DRAFTS) ? PanelDrafts.parse(files.read(PanelFiles.DRAFTS, PanelDrafts.MAXIMUM_BYTES)) : new PanelDrafts(); draftsHealthy = true; }
        catch (Exception error) { draftsHealthy = false; }
        PanelContinuation disk = null; incomingId = null; incomingRecord = null;
        try {
            boolean incoming = files.exists(PanelFiles.INCOMING);
            disk = PanelContinuation.parse(files.read(incoming ? PanelFiles.INCOMING : PanelFiles.STATE, PanelContinuation.MAXIMUM_BYTES));
            if (incoming) { incomingId = disk.string("handoff_id"); incomingRecord = disk; }
        } catch (Exception error) { /* No context is authorized by a malformed or unreadable record. */ }
        restore.stage(disk);
        // Loaded context is staged, never compared or written against an old cached projection.
        identity = null; snapshot = null; continuation = null;
        projectId = workId = executionId = null; draft = ""; spoken = false;loadRequest(null);
        content.removeAllViews(); connection.setText("Checking your computer before restoring this view…");
    }
    private PanelContinuation editorRecord() {
        rememberDraft();
        if (preview) return null;
        if (identity == null || snapshot == null) return restore.pending();
        try { return PanelContinuation.parse(PanelJson.encode(PanelJson.map("version", 1L, "handoff_id", java.util.UUID.randomUUID().toString(), "connection_key", identity.key,
            "journal_id", snapshot.journal, "project_id", projectId, "workstream_id", workId, "execution_id", executionId,
            "section", continuation == null ? "activity" : continuation.value.get("section"), "page", continuation == null ? 0L : continuation.value.get("page"),
            "surface", surface,
            "request", requestRecord(),
            "instruction", executionId == null ? null : PanelJson.map("text", draft, "spoken", spoken, "part", continuation != null && executionId.equals(continuation.string("execution_id")) && draft.equals(continuation.draft()) && continuation.value.get("instruction") != null ? PanelJson.object(continuation.value.get("instruction")).get("part") : 0L)))); }
        catch (Exception error) { return null; }
    }
    private PanelUiState.Context actionContext() {
        return identity == null || snapshot == null || executionId == null ? null : new PanelUiState.Context(identity.key, snapshot.journal, projectId, workId, executionId);
    }
    private boolean applyContext(PanelContinuation pending) {
        if (pending == null) return true;
        if (pending.matches(identity, snapshot)) {
            continuation = pending; projectId = pending.string("project_id"); workId = pending.string("workstream_id"); executionId = pending.string("execution_id");
            draft = pending.draft(); spoken = pending.spoken(); loadRequest(pending);
        } else {
            if (pending.hasContent() && pending.string("journal_id") != null && pending.string("workstream_id") != null && pending.string("execution_id") != null) {
                try { PanelDrafts candidate = PanelDrafts.parse(drafts.json()); candidate.put(pending); files.write(PanelFiles.DRAFTS, candidate.json()); drafts = candidate; }
                catch (Exception error) { restore.paused(pending, false); storageWarning = "Your earlier draft is retained in memory. Saved draft storage needs attention before changing this view."; connection.setText(storageWarning); return false; }
            }
            closeReview(); projectId = workId = executionId = null; draft = ""; spoken = false; continuation = null;loadRequest(null);
            // Keep the task visible when only its execution changed, but never retarget its text.
            Map<String, Object> work = snapshot.workstreams.get(pending.string("workstream_id"));
            if (identity.key.equals(pending.string("connection_key")) && snapshot.journal.equals(pending.string("journal_id")) && work != null && pending.string("project_id").equals(work.get("project_id"))) {
                projectId = pending.string("project_id"); workId = pending.string("workstream_id"); Map<String, Object> run = snapshot.execution(workId); executionId = run == null ? null : PanelJson.text(run.get("execution_id")); restoreDraft();
            }
        }
        restore.applied();
        if (saveSelectedContext()) {
            restore.persisted();
            if (incomingRecord != null && sameSelection(incomingRecord, pending)) { files.acknowledge(PanelFiles.INCOMING, incomingId); incomingId = null; incomingRecord = null; }
        } else restore.paused(editorRecord(), false);
        return true;
    }
    private static boolean sameSelection(PanelContinuation a, PanelContinuation b) {
        for (String field : new String[] {"connection_key", "journal_id", "project_id", "workstream_id", "execution_id"}) if (!java.util.Objects.equals(a.value.get(field), b.value.get(field))) return false;
        return true;
    }
    private void rememberDraft() {
        if (instruction != null) draft = instruction.getText().toString();
        if (requestEditor != null) {String words=requestEditor.getText().toString();if(!words.equals(requestText))requestPart=0;requestText=words;}
        if(continuation!=null && identity!=null && snapshot!=null && continuation.matches(identity,snapshot) && java.util.Objects.equals(executionId,continuation.string("execution_id")) && java.util.Objects.equals(workId,continuation.string("workstream_id")) && java.util.Objects.equals(projectId,continuation.string("project_id"))){continuation.value.put("surface",surface);continuation.value.put("request",requestRecord());}
    }
    private Object requestRecord() { return executionId==null || (requestId==null && requestPending==null && requestText.isEmpty() && !surface.equals("request")) ? null : PanelWorkspaceRequest.draft(requestId,requestPending,requestText,requestSpoken,requestPart); }
    private void loadRequest(PanelContinuation record) {
        requestId=requestPending=null;requestText="";requestSpoken=false;requestPart=0;surface="task";requestEditor=null;agentOffer=requestView=null;
        if(record==null)return;surface=record.string("surface");
        if(record.value.get("request")!=null){Map<String,Object> value=PanelJson.object(record.value.get("request"));requestId=PanelJson.nullableText(value.get("request_id"));requestPending=PanelJson.nullableText(value.get("pending_command_id"));requestText=PanelJson.text(value.get("text"));requestSpoken=PanelJson.bool(value.get("spoken"));requestPart=PanelJson.integer(value.get("part"),0,100000);}
    }
    private boolean saveCurrentDraft() {
        rememberDraft();
        if (preview) return true;
        if (identity == null || snapshot == null || executionId == null) return true;
        if (!draftsHealthy) { storageWarning = "Your saved drafts could not be read. Keep this draft open or return to Workspace."; connection.setText(storageWarning); return false; }
        try {
            PanelContinuation record = PanelContinuation.selection(identity, snapshot, projectId, workId, executionId, draft, spoken, continuation);record.value.put("surface",surface);record.value.put("request",requestRecord());
            PanelDrafts candidate = PanelDrafts.parse(drafts.json()); candidate.put(record);
            files.write(PanelFiles.DRAFTS, candidate.json()); drafts = candidate;
            return true;
        } catch (Exception error) { storageWarning = "This draft could not be saved. Keep it open, or clear an earlier saved draft first."; connection.setText(storageWarning); return false; }
    }
    private boolean saveState() {
        if (preview) return true;
        if (identity == null || snapshot == null) return restore.pending() == null;
        if (!saveCurrentDraft()) return false;
        boolean saved = saveSelectedContext(); if (saved) { restore.persisted(); storageWarning = null; } return saved;
    }
    private boolean saveSelectedContext() {
        if (preview) return true;
        if (identity == null || snapshot == null) return true;
        try {
            continuation = PanelContinuation.selection(identity, snapshot, projectId, workId, executionId, draft, spoken, continuation);continuation.value.put("surface",surface);continuation.value.put("request",requestRecord());
            files.write(PanelFiles.STATE, continuation.json());
            return true;
        } catch (Exception error) { storageWarning = "This view could not be saved. Keep it open or return to Workspace."; connection.setText(storageWarning); return false; }
    }
    private void restoreDraft() {
        draft = ""; spoken = false;loadRequest(null);
        if (identity == null || snapshot == null || workId == null || executionId == null) return;
        PanelContinuation saved = drafts.find(identity.key, snapshot.journal, workId, executionId);
        if (saved != null && saved.matches(identity, snapshot)) { draft = saved.draft(); spoken = saved.spoken(); continuation = saved;loadRequest(saved); }
    }
    private void closeReview() { cancelVoice(); if (resultLease != null) { resultLease.revoke(); resultLease = null; } if (outputLease != null) { outputLease.revoke(); outputLease = null; } if (review != null) { Dialog old = review; review = null; reviewReads.clear(); authority.close(reviewOwner); old.dismiss(); } }

    private PanelUiState.Context outputContext() {
        PanelUiState.Context scope = identity == null || snapshot == null || projectId == null ? null
            : new PanelUiState.Context(identity.key, snapshot.journal, projectId, workId, executionId);
        return PanelProjectOutput.selected(scope, identity, snapshot) ? scope : null;
    }
    private PanelProjectOutput.Scope outputScope(PanelUiState.Context scope) {
        return new PanelProjectOutput.Scope(scope.connection, scope.journal, scope.project);
    }
    private Object outputEditor() { return instruction != null ? instruction : requestEditor != null ? requestEditor : content; }
    private String outputWords() {
        rememberDraft();
        long part = continuation != null && java.util.Objects.equals(executionId, continuation.string("execution_id"))
            && draft.equals(continuation.draft()) && continuation.value.get("instruction") != null
            ? PanelJson.integer(PanelJson.object(continuation.value.get("instruction")).get("part"), 0, 100000) : 0;
        return PanelJson.encode(PanelJson.map("surface", surface, "instruction", draft, "spoken", spoken,
            "instruction_part", part,
            "request", requestText, "request_spoken", requestSpoken, "request_id", requestId,
            "pending", requestPending, "request_part", requestPart));
    }
    private boolean outputCurrent(PanelProjectOutput.Lease lease) {
        try {
            PanelUiState.Context current = outputContext();
            PanelProjectOutput.Link link = files.readProjectOutputs().find(lease.scope);
            return outputLease == lease && lease.current(authority, current, current == null ? null : outputScope(current),
                link == null ? null : link.component, generation, outputEditor(), outputWords(),
                !preview && visible && authority.commandsAllowed() && identity.same(files.connection()));
        } catch (Exception error) { lease.revoke(); return false; }
    }
    private void projectOutputControls() {
        if (preview || outputContext() == null || !authority.controlsReady()) return;
        button(content, "Linked app", this::projectOutput);
    }
    private LinearLayout outputDialog(String title, PanelUiState.Context selection, PanelProjectOutput.Component previous) {
        LinearLayout body = dialog(title);
        outputLease = new PanelProjectOutput.Lease(authority, reviewOwner, selection, outputScope(selection), previous,
            generation, outputEditor(), outputWords());
        return body;
    }
    private void outputRefused(String message) {
        closeReview(); connection.setText(message); show(); ui.postDelayed(refresh, 1000);
    }
    /** Each decisive callback checks the original tuple with a fresh authenticated host read. */
    private void freshOutput(PanelProjectOutput.Lease lease, PanelUiState.Context selection, Runnable next) {
        busy = true; ui.removeCallbacks(refresh);
        final int requestGeneration = generation;
        final PanelIdentity original = identity;
        PanelTransport check = new PanelTransport(files::connection); transport = check;
        worker.execute(() -> {
            boolean confirmed = false;
            try {
                PanelIdentity target = files.connection();
                if (original.same(target)) {
                    PanelTransport.Result result = check.request(target, "GET", "/api/snapshot", null);
                    confirmed = result.status == 200 && PanelProjectOutput.selected(selection, target, new PanelModel(result.body));
                }
            } catch (Exception error) { /* Refusal grants no local mutation or launch authority. */ }
            final boolean fresh = confirmed;
            ui.post(() -> {
                if (!visible || generation != requestGeneration) return;
                busy = false;
                if (outputLease != lease) { ui.postDelayed(refresh, 1000); return; }
                if (!fresh || !outputCurrent(lease)) { outputRefused("This link action expired or the computer could not confirm the same selection. Choose it again after reconnecting."); return; }
                next.run();
            });
        });
    }
    private void projectOutput() {
        if (preview || busy || !authority.commandsAllowed()) return;
        final PanelUiState.Context selection = outputContext(); if (selection == null) return;
        try {
            PanelProjectOutput.Link link = files.readProjectOutputs().find(outputScope(selection));
            final PanelProjectOutput.Component previous = link == null ? null : link.component;
            LinearLayout body = outputDialog("Linked app", selection, previous);
            text(body, outputProjectName(selection), 20);
            text(body, "Installed build may differ from current source.", 18);
            if (previous == null) text(body, "No app linked on this headset.", 18);
            else {
                PanelProjectOutput.App installed = PanelAppLauncher.current(this, previous);
                text(body, installed == null ? "Linked app unavailable\n" + PanelText.plain(previous.packageName) + "\n" + PanelText.plain(previous.activity) : installed.label, 20);
                button(body, "Open linked app", () -> outputAction(outputLease, selection, previous, previous, true));
                button(body, "Remove link", () -> outputAction(outputLease, selection, previous, null, false));
            }
            button(body, previous == null ? "Choose installed app" : "Change linked app", () -> outputChooser(selection, previous));
            text(body, "Other Apps opens Home if an app cannot be found or opened here.", 18);
        } catch (Exception error) { outputRefused("Saved links could not be read. Use Other Apps to open Home."); }
    }
    private void outputChooser(PanelUiState.Context selection, PanelProjectOutput.Component previous) {
        final PanelProjectOutput.Lease old = outputLease;
        if (old == null || !outputCurrent(old)) { outputRefused("This app choice expired. Choose it again."); return; }
        LinearLayout body = outputDialog("Choose installed app", selection, previous);
        final PanelProjectOutput.Lease lease = outputLease;
        text(body, "Checking this project…", 18);
        freshOutput(lease, selection, () -> {
            try {
                // Enumeration happens only in this explicit chooser; labels are never saved.
                PanelAppLauncher.Catalog catalog = PanelAppLauncher.list(this);
                if (!outputCurrent(lease)) { outputRefused("This app choice expired. Choose it again."); return; }
                body.removeAllViews();
                text(body, "Choose an app for " + outputProjectName(selection) + ". It will not open yet.", 20);
                if (catalog.apps.isEmpty()) text(body, "No apps were found. Close this view and use Other Apps to open Home.", 18);
                if (catalog.truncated) text(body, "Only the first 256 apps are shown. Use Other Apps for more.", 18);
                for (PanelProjectOutput.App app : catalog.apps) button(body, outputAppName(catalog, app), () -> {
                    if (!outputCurrent(lease) || catalog.find(app.component) != app) { outputRefused("This app choice expired. Choose it again."); return; }
                    outputConfirmation(selection, previous, app);
                });
            } catch (Exception error) { outputRefused("Apps could not be listed. Use Other Apps to open Home."); }
        });
    }
    private String outputProjectName(PanelUiState.Context selection) {
        Map<String,Object> project = snapshot.projects.get(selection.project);
        return project == null ? "this project" : PanelText.plain(PanelJson.text(project.get("name")));
    }
    private String outputAppName(PanelAppLauncher.Catalog catalog, PanelProjectOutput.App app) {
        long sameName = catalog.apps.stream().filter(other -> other.label.equals(app.label)).count();
        if (sameName <= 1) return app.label;
        String identity = app.label + "\n" + PanelText.plain(app.component.packageName);
        return catalog.apps.stream().filter(other -> other.label.equals(app.label) && other.component.packageName.equals(app.component.packageName)).count() <= 1
            ? identity : identity + "\n" + PanelText.plain(app.component.activity);
    }
    private void outputConfirmation(PanelUiState.Context selection, PanelProjectOutput.Component previous, PanelProjectOutput.App chosen) {
        if (outputLease == null || !outputCurrent(outputLease)) { outputRefused("This app choice expired. Choose it again."); return; }
        LinearLayout body = outputDialog("Link app?", selection, previous);
        final PanelProjectOutput.Lease lease = outputLease;
        text(body, outputProjectName(selection) + "\n" + chosen.label, 22);
        text(body, "Installed build may differ from current source. Saving this link does not open the app.", 18);
        button(body, "Save link", () -> outputAction(lease, selection, previous, chosen.component, false));
    }
    private void outputAction(PanelProjectOutput.Lease lease, PanelUiState.Context selection, PanelProjectOutput.Component previous,
                              PanelProjectOutput.Component chosen, boolean open) {
        if (lease == null || !outputCurrent(lease)) { outputRefused("This app action expired. Choose it again."); return; }
        freshOutput(lease, selection, () -> {
            if (open) {
                PanelProjectOutput.OpenResult result = PanelProjectOutput.open(new PanelProjectOutput.OpenOperations() {
                    public boolean current() { return outputCurrent(lease); }
                    public boolean available() { return chosen != null && PanelAppLauncher.current(AgentPanelActivity.this, chosen) != null; }
                    public boolean preserveDrafts() { return saveState(); }
                    public void start() { PanelAppLauncher.open(AgentPanelActivity.this, chosen); }
                });
                outputRefused(result == PanelProjectOutput.OpenResult.REQUESTED ? "App open requested."
                    : result == PanelProjectOutput.OpenResult.UNSAVED ? "Your drafts could not be saved, so the app was not opened."
                    : result == PanelProjectOutput.OpenResult.UNAVAILABLE ? "Linked app unavailable. Change the link or use Other Apps to open Home."
                    : "The app could not open. Choose it again or use Other Apps to open Home.");
            } else {
                try {
                    if (chosen != null && PanelAppLauncher.current(this, chosen) == null) { outputRefused("This app is unavailable. Choose an installed app again."); return; }
                    if (!saveState() || !outputCurrent(lease)) { outputRefused("Your drafts or selection could not be preserved. The link was kept."); return; }
                    files.changeProjectOutput(lease.scope, previous, chosen);
                    outputRefused(chosen == null ? "Link removed." : "Link saved. The app was not opened.");
                } catch (Exception error) { outputRefused("The link save could not be confirmed. Reopen Linked app to check. No app was opened."); }
            }
        });
    }

    private void cancelVoice() {
        voiceGesture.cancel();
        if (voiceLease != null) voiceLease.revoke();
        voiceLease = null;
        if (voiceCapture != null) voiceCapture.stop(false);
        voiceCapture = null;
        if (voiceTransport != null) voiceTransport.cancel();
        voiceTransport = null;
        if (voiceAudio != null) voiceAudio.discard();
        voiceAudio = null;
        if (voiceStatus != null) voiceStatus.setText("Hold to talk adds words locally. Review before sending.");
    }
    private boolean voiceEligible(PanelModel model, String route) {
        if (!developmentAllowed || preview || !visible || review != null || !authority.interact(0) || model == null || identity == null) return false;
        Map<String,Object> run = model.execution(workId);
        if (run == null || !java.util.Objects.equals(executionId, run.get("execution_id")) || !java.util.Objects.equals(projectId, run.get("project_id"))) return false;
        if (route.equals("instruction")) return surface.equals("task") && model.instruct(run) && instruction != null;
        return surface.equals("request") && requestEditor != null && requestPending == null && agentOffer != null
            && "available".equals(agentOffer.get("availability")) && PanelJson.array(run.get("pending_approvals")).isEmpty()
            && PanelJson.array(run.get("pending_questions")).isEmpty();
    }
    private boolean voiceCurrent(PanelModel model, PanelIdentity target) {
        PanelVoice.Lease lease = voiceLease;
        if (lease == null) return false;
        if (voicePermissionAtPress && checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) != android.content.pm.PackageManager.PERMISSION_GRANTED) { lease.revoke(); return false; }
        try { if (files == null || target == null || !target.same(files.connection())) { lease.revoke(); return false; } }
        catch (Exception error) { lease.revoke(); return false; }
        if (model == null || !java.util.Objects.equals(voiceFingerprint, model.context(workId))) { lease.revoke(); return false; }
        EditText editor = "instruction".equals(voiceRoute) ? instruction : requestEditor;
        PanelUiState.Context context = model == null || target == null ? null : new PanelUiState.Context(target.key, model.journal, projectId, workId, executionId);
        return lease.current(authority, context, target, voiceRoute, editor, editor == null ? "" : editor.getText().toString(),
            requestId, requestPending, generation, voiceEligible(model, voiceRoute));
    }
    private void voiceEditor(LinearLayout parent, EditText editor, String route) {
        if (!developmentAllowed || preview) return;
        voiceEditor = editor; voiceRoute = route;
        voiceStatus = text(parent, "Hold to talk adds words locally. Review before sending.", 16);
        Button held = new Button(this); voiceButton = held; held.setAllCaps(false); held.setMinHeight(dp(52)); styleButton(held, "Hold to talk");
        Runnable label = () -> { String words = editor.getText().toString(); String value = words.isEmpty() ? "Hold to talk" : "Hold to add words"; held.setText(value); held.setContentDescription(value); };
        label.run(); parent.addView(held);
        editor.addTextChangedListener(new TextWatcher() {
            public void beforeTextChanged(CharSequence text, int start, int count, int after) { cancelVoice(); }
            public void onTextChanged(CharSequence text, int start, int before, int count) { label.run(); }
            public void afterTextChanged(Editable value) {}
        });
        held.setOnTouchListener((view, event) -> {
            int action = event.getActionMasked(), pointer = event.getPointerId(event.getActionIndex());
            if (action == MotionEvent.ACTION_DOWN) {
                if (voiceLease != null || voiceCapture != null) { cancelVoice(); return true; }
                if (busy || editor != voiceEditor || held != voiceButton || !voiceEligible(snapshot, route)) return true;
                if (!voiceGesture.down(pointer, SystemClock.elapsedRealtime())) return true;
                voicePermissionAtPress = checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) == android.content.pm.PackageManager.PERMISSION_GRANTED;
                voiceFingerprint = snapshot.context(workId);
                voiceLease = new PanelVoice.Lease(authority, actionContext(), identity, route, editor, editor.getText().toString(), requestId, requestPending, generation);
                final PanelVoice.Lease lease = voiceLease;
                ui.postDelayed(() -> { if (voiceLease == lease && voiceCurrent(snapshot, identity) && voiceGesture.mature(pointer, SystemClock.elapsedRealtime())) beginVoice(lease); }, PanelVoice.HOLD_MS);
            } else if (action == MotionEvent.ACTION_MOVE) {
                if (event.getPointerCount() != 1 || !voiceGesture.owns(pointer) || event.getX() < 0 || event.getY() < 0 || event.getX() >= held.getWidth() || event.getY() >= held.getHeight()) cancelVoice();
            } else if (action == MotionEvent.ACTION_UP) {
                if (event.getX() >= 0 && event.getY() >= 0 && event.getX() < held.getWidth() && event.getY() < held.getHeight() && voiceCurrent(snapshot, identity) && voiceGesture.release(pointer)) { if (voiceCapture != null) voiceCapture.stop(true); }
                else { cancelVoice(); if (voiceStatus != null) voiceStatus.setText("Hold while speaking; releasing adds a draft for review."); }
            } else if (action == MotionEvent.ACTION_CANCEL || action == MotionEvent.ACTION_POINTER_DOWN || action == MotionEvent.ACTION_POINTER_UP) cancelVoice();
            return true;
        });
        button(parent, "Cancel recording", this::cancelVoice, true);
    }
    private void beginVoice(PanelVoice.Lease lease) {
        if (!voicePermissionAtPress || checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) != android.content.pm.PackageManager.PERMISSION_GRANTED) {
            cancelVoice(); voiceStatus.setText("Allow the microphone to add spoken words. Nothing records until you hold again.");
            requestPermissions(new String[]{android.Manifest.permission.RECORD_AUDIO}, 7301); return;
        }
        try {
            voiceCapture = new PanelVoiceCapture(); voiceStatus.setText("Listening. Release to add words, or cancel.");
            final PanelVoiceCapture capture = voiceCapture;
            ui.postDelayed(() -> { if (voiceLease == lease && voiceCapture == capture) capture.stop(true); }, PanelVoice.MAX_MS);
            capture.read(speechWorker, (wav, cancelled, failed) -> ui.post(() -> {
                if (voiceLease != lease || voiceCapture != capture) { PanelVoice.discard(wav); return; }
                voiceCapture = null;
                if (!PanelVoice.usableClip(wav, voiceCurrent(snapshot, identity), cancelled)) {
                    cancelVoice(); voiceStatus.setText(failed ? "Microphone unavailable. Your words are unchanged." : cancelled ? "Recording cancelled. Your words are unchanged." : "Speak for at least half a second. Your words are unchanged."); return;
                }
                voiceStatus.setText("Writing down. Nothing goes to the agent until you review and send it.");
                voiceAudio = new PanelVoice.Audio(wav);
                transcribeVoice(lease, voiceAudio);
            }));
        } catch (RuntimeException error) { cancelVoice(); voiceStatus.setText("16 kHz microphone capture is unavailable. You can still type."); }
    }
    @Override public void onRequestPermissionsResult(int code, String[] permissions, int[] grants) {
        super.onRequestPermissionsResult(code, permissions, grants);
        if (code != 7301) return;
        cancelVoice();
        if (voiceStatus != null && visible) voiceStatus.setText(checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) == android.content.pm.PackageManager.PERMISSION_GRANTED
            ? "Microphone allowed. Hold again to speak." : "Microphone denied. You can still type.");
    }
    private PanelModel voiceSnapshot(PanelTransport next, PanelVoice.Lease lease) throws IOException {
        PanelTransport.Result response = next.request(lease.identity, "GET", "/api/snapshot", null);
        if (response.status != 200) throw new IOException();
        PanelModel fresh = new PanelModel(response.body); Map<String,Object> run = fresh.execution(lease.ticket.context.work);
        if (!lease.ticket.context.journal.equals(fresh.journal) || run == null || !lease.ticket.context.project.equals(run.get("project_id"))
            || !lease.ticket.context.execution.equals(run.get("execution_id"))) throw new IOException();
        if (lease.route.equals("instruction")) { if (!fresh.instruct(run)) throw new IOException(); }
        else {
            if (!PanelJson.array(run.get("pending_approvals")).isEmpty() || !PanelJson.array(run.get("pending_questions")).isEmpty()) throw new IOException();
            response = next.request(lease.identity, "GET", "/api/workspace/agent", null);
            if (response.status != 200 || !"available".equals(PanelWorkspaceRequest.offer(response.body).get("availability"))) throw new IOException();
        }
        return fresh;
    }
    private void transcribeVoice(PanelVoice.Lease lease, PanelVoice.Audio audio) {
        byte[] wav = audio.bytes;
        PanelTransport next = new PanelTransport(files::connection); voiceTransport = next;
        speechWorker.execute(() -> {
            try {
                PanelModel fresh = voiceSnapshot(next, lease);
                ui.post(() -> {
                    if (voiceLease != lease) { PanelVoice.discard(wav); return; }
                    if (!voiceCurrent(fresh, lease.identity)) { cancelVoice(); PanelVoice.discard(wav); return; }
                    speechWorker.execute(() -> {
                        String words = null; PanelModel latest = null; boolean failed = false;
                        try {
                            PanelTransport.Result response = next.transcribe(lease.identity, wav);
                            if (response.status != 200) throw new IOException();
                            words = PanelVoice.transcript(response.body); latest = voiceSnapshot(next, lease);
                        } catch (Exception error) { failed = true; }
                        finally { PanelVoice.discard(wav); }
                        final String heard = words; final PanelModel received = latest; final boolean refused = failed;
                        ui.post(() -> {
                            if (voiceLease != lease) return;
                            if (refused || !voiceCurrent(received, lease.identity)) { cancelVoice(); voiceStatus.setText("Voice could not be added to this current field. Your words are unchanged."); return; }
                            try {
                                String added = PanelVoice.append(lease.words, heard);
                                EditText editor = voiceEditor; cancelVoice();
                                if (!added.equals(lease.words)) {
                                    if (lease.route.equals("instruction")) { draft = added; spoken = true; } else { requestText = added; requestSpoken = true; }
                                    editor.setText(added); editor.setSelection(added.length()); rememberDraft(); saveState();
                                    voiceStatus.setText("Words added from speech. Review every word before sending.");
                                } else voiceStatus.setText("Nothing heard. Your words are unchanged.");
                            } catch (Exception error) { cancelVoice(); voiceStatus.setText("The words do not fit this draft. Your words are unchanged."); }
                        });
                    });
                });
            } catch (Exception error) {
                PanelVoice.discard(wav);
                ui.post(() -> { if (voiceLease == lease) { cancelVoice(); voiceStatus.setText("This task or connection changed. Your words are unchanged."); } });
            }
        });
    }

    private void poll(boolean rebuild) {
        ui.removeCallbacks(refresh);
        if (!visible || busy || !authority.readProjection()) return;
        if (preview) return;
        busy = true; int requestGeneration = generation;
        if (restore.pending() != null) connection.loading("Checking your computer before restoring this view…");
        PanelTransport next = new PanelTransport(files::connection); transport = next;
        worker.execute(() -> {
            PanelIdentity target = null; PanelModel model = null; String failure = null;
            try {
                target = files.connection();
                PanelTransport.Result response = next.request(target, "GET", "/api/snapshot", null);
                if (response.status == 401 || response.status == 403) failure = "This connection was refused. Pair again in Workspace.";
                else if (response.status != 200) failure = "Your computer is not available.";
                else model = new PanelModel(response.body);
            } catch (Exception error) { failure = "Your computer is not available."; }
            PanelIdentity receivedTarget = target; PanelModel received = model; String message = failure;
            ui.post(() -> {
                if (!visible || generation != requestGeneration) return;
                busy = false;
                connection.stop();
                if (voiceLease != null && !voiceCurrent(received, receivedTarget)) cancelVoice();
                if (identity != null && snapshot != null && !saveState()) { ui.postDelayed(refresh, 3000); return; }
                if (received == null) {
                    snapshot = null; identity = null; authority.awaitingProjection(); closeReview(); instruction = null;requestEditor=null;agentOffer=requestView=null; connection.setVisibility(View.VISIBLE);
                    // Offline in compact: the words stand on the glass, whatever the at-rest line had cleared (AUDIT3's L1).
                    if (compactPanel) shellRoot.setBackground(PanelHud.glass(getResources().getDisplayMetrics().density));
                    connection.setText(compactPanel ? message + "\n" + monitorFreshness(true) : message); content.removeAllViews(); text(content, "Your agents continue on your computer. Open Workspace to check the connection.", 20);
                } else {
                    String priorContext = snapshot == null || workId == null ? null : snapshot.context(workId);
                    boolean connectionChanged = identity != null && !identity.same(receivedTarget);
                    boolean journalChanged = snapshot != null && !snapshot.journal.equals(received.journal);
                    identity = receivedTarget; snapshot = received; checkedAt = System.currentTimeMillis(); authority.projected();
                    PanelContinuation staged = restore.pending();
                    if (staged != null) { if (!applyContext(staged)) { show(); ui.postDelayed(refresh, 3000); return; } }
                    else if (connectionChanged || journalChanged || (continuation != null && !continuation.matches(identity, snapshot))) {
                        PanelContinuation old = continuation; if (!applyContext(old)) { show(); ui.postDelayed(refresh, 3000); return; }
                    }
                    boolean contextChanged = priorContext != null && workId != null && !priorContext.equals(snapshot.context(workId));
                    if (contextChanged) closeReview();
                    connection.setText(withStorageWarning(compactPanel ? monitorFreshness(false) : status()));
                    if (contextChanged || rebuild || (instruction == null && requestEditor == null && review == null)) show();
                    if (!compactPanel && surface.equals("request") && review==null && executionId!=null) { pollRequest(); return; }
                }
                ui.postDelayed(refresh, 3000);
            });
        });
    }
    private String withStorageWarning(String message) { return storageWarning == null ? message : message + "\n" + storageWarning; }
    private String status() {
        if (commandId != null) for (Object item : snapshot.commands) {
            Map<String, Object> command = PanelJson.object(item);
            if (!commandId.equals(command.get("command_id"))) continue;
            return PanelModel.commandStatus(command);
        }
        return identity != null && identity.scheme.equals("ws") ? "USB development connection. Agents work on your computer." : "Connected to your paired computer. Agents work there independently.";
    }

    private void show() {
        cancelVoice(); rememberDraft();instruction = null;requestEditor=null;requestAvailability=null;requestActions=null; content.removeAllViews();
        authority.compact(compactPanel && workId != null);
        // Project/task lists are local navigation, not restoration of hidden task action controls.
        if (workId == null && snapshot != null) authority.projected();
        presentationButton.setVisibility(workId == null ? View.GONE : View.VISIBLE);
        presentationButton.setText(compactPanel ? "Expand" : "Compact");
        presentationButton.setContentDescription(presentationButton.getText());
        developmentControls.setVisibility(compactPanel ? View.GONE : View.VISIBLE);
        // Compact beside the person's show is the card alone (take 10): no name, no navigation, no monitor line, save the
        // offline line, and the synthetic preview's own line, which never leaves the preview (AUDIT2, 23:49Z).
        boolean hud = compactPanel;
        brand.setVisibility(hud ? View.GONE : View.VISIBLE); navRow.setVisibility(hud ? View.GONE : View.VISIBLE);
        if (hud) presentationButton.setVisibility(View.GONE);
        // Compact is the card alone: the window around it clear, so the card stands at the show's edge rather than at the top of
        // a dark slab (unverified on the headset whether Horizon OS composites a panel's clear pixels; if not they read black).
        // The full panel stands as one glass card too (AUDIT's 19:40Z (a)), its window clear round the card's corners.
        getWindow().setFormat(android.graphics.PixelFormat.TRANSLUCENT);
        getWindow().setBackgroundDrawable(new android.graphics.drawable.ColorDrawable(Color.TRANSPARENT));
        // In compact the window stands on glass by default, so every compact screen (offline, waiting for the computer, the
        // preview's line) is read on glass; only the at-rest line clears the rest of the window (AUDIT3's L1).
        if (hud) shellRoot.setBackground(PanelHud.glass(getResources().getDisplayMetrics().density)); else shellRoot.setBackground(PanelKit.glass(PanelKit.CORNER, getResources().getDisplayMetrics().density));
        float d = getResources().getDisplayMetrics().density;
        // The full card's pad is the kit's 28, given as 12 to the card and 16 to each block in it, so a row's plate can reach 16
        // left of its words (f1) inside the scroll, and every block's words still start on the one edge 28 in.
        int kitPad = PanelKit.px(PanelKit.PAD, d), rowReach = PanelKit.px(16, d);
        if (hud) shellRoot.setPadding(PanelHud.px(32, d), PanelHud.px(36, d), PanelHud.px(28, d), PanelHud.px(28, d)); else shellRoot.setPadding(kitPad - rowReach, kitPad, kitPad - rowReach, kitPad);
        int blockInset = hud ? 0 : rowReach;
        for (View block : new View[] {brand, connection, navRow}) {
            if (block.getLayoutParams() instanceof LinearLayout.LayoutParams) ((LinearLayout.LayoutParams) block.getLayoutParams()).setMargins(blockInset, 0, blockInset, 0);
        }
        content.setPadding(blockInset, 0, blockInset, 0); content.setGravity(android.view.Gravity.TOP | android.view.Gravity.START); content.setClipToPadding(false);
        scroll.setFillViewport(hud);
        connection.setVisibility(!hud || snapshot == null || preview ? View.VISIBLE : View.GONE);
        if (snapshot == null) return;
        try {
            if (compactPanel && workId == null) { noTaskStrip(); return; }
            if (projectId == null) {
                text(content, "Projects", 26);
                if (snapshot.projects.isEmpty()) text(content, "No projects yet. Create one in Workspace.", 20);
                for (Map<String, Object> project : snapshot.projects.values()) button(content, PanelText.plain(PanelJson.text(project.get("name"))), () -> { if (!saveState()) return; projectId = PanelJson.text(project.get("project_id")); continuation = null; selectionChanged(); });
                return;
            }
            Map<String, Object> project = snapshot.projects.get(projectId);
            if (project == null) { projectId = null; show(); return; }
            // Compact is the card's four things alone: no project heading over it (SCREENS' brief, AUDIT 22:08Z).
            if (!(compactPanel && workId != null)) text(content, PanelText.plain(PanelJson.text(project.get("name"))), 23);
            if (workId == null) {
                projectOutputControls();
                text(content, "Tasks", 26);
                for (Map<String, Object> work : snapshot.workstreams.values()) if (projectId.equals(work.get("project_id"))) {
                    Map<String, Object> taskRun = snapshot.execution(PanelJson.text(work.get("workstream_id")));
                    boolean simulated = taskRun != null && PanelJson.bool(PanelJson.object(taskRun.get("runtime")).get("synthetic"));
                    String label = PanelText.plain(PanelJson.text(work.get("title"))) + "\n" + stateName(PanelJson.text(work.get("status")));
                    button(content, label, () -> { if (!saveState()) return; workId = PanelJson.text(work.get("workstream_id")); Map<String, Object> run = snapshot.execution(workId); executionId = run == null ? null : PanelJson.text(run.get("execution_id")); continuation = null; restoreDraft(); selectionChanged(); });
                }
                return;
            }
            Map<String, Object> work = snapshot.workstreams.get(workId);
            if (work == null || !projectId.equals(work.get("project_id"))) { workId = executionId = null; draft = "";loadRequest(null); show(); return; }
            if (compactPanel) { monitorTask(snapshot.monitor(projectId, workId)); return; }
            if (!preview && !authority.controlsReady()) {
                text(content, "Checking this task before enabling controls…", 20);
                return;
            }
            text(content, PanelText.plain(PanelJson.text(work.get("title"))), 25);
            if (work.get("objective") != null) text(content, "Objective\n" + PanelText.plain(PanelJson.text(work.get("objective"))), 18);
            Map<String, Object> run = snapshot.execution(workId);
            if (run == null) { executionId = null; projectOutputControls(); text(content, "This task has no current agent. Start work in Workspace.", 20); return; }
            executionId = PanelJson.text(run.get("execution_id"));
            projectOutputControls();
            Map<String, Object> runtime = PanelJson.object(run.get("runtime"));
            text(content, stateName(PanelJson.text(run.get("status"))) + "\n" + (capturable ? "Coding Agent" : PanelText.plain(PanelJson.text(runtime.get("display_name")))), 19);
            final PanelUiState.Context displayed = actionContext();
            button(content,surface.equals("request")?"Task Controls":"Ask",()->{if(!saveState())return;surface=surface.equals("request")?"task":"request";agentOffer=requestView=null;show();saveSelectedContext();if(!preview)poll(true);});
            button(content, "Activity", this::activity);
            if (!preview) button(content, "What It Did", this::results);
            for (Object item : PanelJson.array(run.get("pending_approvals"))) {
                Map<String, Object> request = PanelJson.object(item);
                button(content, PanelModel.approvalLabel(request), () -> approval(request, displayed));
            }
            for (Object item : PanelJson.array(run.get("pending_questions"))) {
                Map<String, Object> question = PanelJson.object(item);
                button(content, "Agent's Question", () -> question(question, displayed));
            }
            for (PanelContinuation earlier : preview ? new ArrayList<PanelContinuation>() : drafts.earlier(identity.key, snapshot.journal, workId, executionId)) {
                final Map<String,Object> earlierRequest=earlier.value.get("request")==null?null:PanelJson.object(earlier.value.get("request"));
                String earlierWords="Instruction\n"+PanelText.plain(earlier.draft())+(earlierRequest==null?"":"\n\nWorkspace request\n"+PanelText.plain(PanelJson.text(earlierRequest.get("text")))+(earlierRequest.get("pending_command_id")==null?"":"\n\nAn original command reference is retained for this previous run. Its effect must be checked in that run; copying words never retries it."));
                button(content, "Read saved draft from an earlier run", () -> reviewed("Draft for an earlier run", earlierWords + "\n\nThis draft belongs to a previous run. Copying it does not send it or transfer its request reference.", "Choose what to do", () -> {
                    LinearLayout actions = dialog("Earlier draft");
                    button(actions, "Copy into this run’s instruction", () -> { if (!saveCurrentDraft()) return; draft = earlier.draft(); spoken = earlier.spoken(); instruction = null; closeReview(); show(); saveState(); });
                    if(earlierRequest!=null)button(actions,"Copy request words into this run",()->{if(requestPending!=null){connection.setText("Check the last send before replacing this request. Its reference must be preserved.");return;}if(!saveCurrentDraft())return;Map<String,Object> copied=PanelWorkspaceRequest.copyWords(requestRecord(),earlierRequest);requestText=PanelJson.text(copied.get("text"));requestSpoken=PanelJson.bool(copied.get("spoken"));requestPart=0;requestId=null;surface="request";requestEditor=null;agentOffer=requestView=null;closeReview();show();saveState();poll(true);});
                    button(actions, "Delete this saved draft", () -> { try { PanelDrafts candidate = PanelDrafts.parse(drafts.json()); candidate.remove(candidate.find(earlier.string("connection_key"), earlier.string("journal_id"), earlier.string("workstream_id"), earlier.string("execution_id"))); files.write(PanelFiles.DRAFTS, candidate.json()); drafts = candidate; closeReview(); show(); } catch (Exception error) { connection.setText("The draft could not be removed."); } });
                }));
            }
            if(surface.equals("request")){showRequest(displayed);if(snapshot.interrupt(run))button(content,"Stop this agent",()->reviewed("Stop this agent?","The agent must confirm before its runtime turn is stopped.","Send stop request",displayed,()->send("execution.interrupt",PanelJson.map("execution_id",displayed.execution),null,null,displayed)));return;}
            if (snapshot.instruct(run)) {
                text(content, "Your instruction", 20); if (spoken) text(content, "From speech. Read every word.", 17);
                instruction = new EditText(this); instruction.setText(draft); instruction.setTextColor(Color.WHITE); instruction.setContentDescription("Your instruction"); instruction.setTextSize(20); instruction.setMinLines(3); instruction.setMaxLines(8);
                instruction.setFilters(new InputFilter[] {ordinaryTextLimit()}); instruction.setInputType(android.text.InputType.TYPE_CLASS_TEXT | android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE | android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);
                content.addView(instruction, new LinearLayout.LayoutParams(-1, -2));
                voiceEditor(content, instruction, "instruction");
                button(content, "Review instruction", () -> { rememberDraft(); if (!draft.trim().isEmpty()) reviewInstruction(displayed); });
            } else text(content, "Instructions are unavailable while this agent needs a decision or cannot receive one.", 18);
            if (snapshot.interrupt(run)) button(content, "Stop this agent", () -> reviewed("Stop this agent?", "This asks the agent to stop its current turn. The agent must confirm before this task is stopped.", "Send stop request", displayed, () -> send("execution.interrupt", PanelJson.map("execution_id", preview ? executionId : displayed.execution), null, null, displayed)));
        } catch (Exception error) { content.removeAllViews(); text(content, "This task could not be read safely. Open Workspace.", 20); }
    }

    private boolean resultCurrent(PanelToolResults.Lease lease) {
        try { return resultLease == lease && lease.current(authority, actionContext(), files.connection(), generation,
            outputEditor(), outputWords(), !preview && visible && review != null && authority.commandsAllowed()); }
        catch (Exception error) { lease.revoke(); return false; }
    }
    private void results() {
        if (preview || busy || !authority.commandsAllowed() || actionContext() == null || executionId == null || !saveState()) return;
        final PanelUiState.Context selection = actionContext();
        LinearLayout body = dialog("Recorded results");
        resultLease = new PanelToolResults.Lease(authority, reviewOwner, selection, identity, generation, outputEditor(), outputWords());
        readResults(resultLease, body, null);
    }
    private void readResults(PanelToolResults.Lease lease, LinearLayout body, Map<String,Object> selected) {
        if (resultLease != lease) return;
        busy = true; ui.removeCallbacks(refresh); body.removeAllViews(); text(body, "Reading recorded results…", 20);
        final int requestedGeneration = generation;
        final String resultRuntime = PanelJson.text(PanelJson.object(snapshot.execution(lease.context.work).get("runtime")).get("runtime_id"));
        PanelTransport next = new PanelTransport(files::connection); transport = next;
        worker.execute(() -> {
            Object answer = null; String failure = "Recorded results could not be read. Try again after checking the connection.";
            try {
                PanelIdentity target = files.connection();
                if (lease.identity.same(target)) {
                    PanelTransport.Result fresh = next.request(target, "GET", "/api/snapshot", null);
                    if (fresh.status == 200 && PanelProjectOutput.selected(lease.context, target, new PanelModel(fresh.body))) {
                        PanelTransport.Result response = next.request(target, "GET", PanelToolResults.path(lease.context, selected == null ? null : PanelJson.text(selected.get("event_id"))), null);
                        if (response.status == 200) answer = selected == null ? PanelToolResults.list(response.body, lease.context, resultRuntime) : PanelToolResults.detail(response.body, lease.context, selected);
                        else if (response.status == 404) failure = "These recorded results are unavailable for this task or this computer version.";
                    } else failure = "This task changed. Close Results and select it again.";
                }
            } catch (Exception error) { /* Outside text and transport errors are never logged. */ }
            final Object received = answer; final String message = failure;
            ui.post(() -> {
                if (!visible || generation != requestedGeneration) return;
                busy = false;
                if (!resultCurrent(lease)) { if (resultLease == lease) closeReview(); ui.postDelayed(refresh, 1000); return; }
                body.removeAllViews();
                text(body, PanelText.plain(PanelJson.text(snapshot.projects.get(lease.context.project).get("name"))) + "\n" + PanelText.plain(PanelJson.text(snapshot.workstreams.get(lease.context.work).get("title"))), 20);
                text(body, "Recorded by " + PanelText.plain(PanelJson.text(PanelJson.object(snapshot.execution(lease.context.work).get("runtime")).get("display_name"))), 17);
                if (received == null) text(body, message, 20);
                else if (selected == null) {
                    PanelToolResults.ListView list = (PanelToolResults.ListView) received;
                    List<Map<String,Object>> summaries = list.summaries;
                    text(body, "Current files and software correctness are not verified.", 18);
                    text(body, "Showing " + summaries.size() + " of " + list.total + " recorded results" + "." + (list.earlier > 0 ? " Earlier ones are not in this list." : ""), 17);
                    if (!list.availability.equals("available")) text(body, list.availability.equals("unsupported") ? "This agent app does not support new result records. Earlier records remain readable." : list.availability.equals("unknown") ? "Support for new result records is unknown. Earlier records remain readable." : "Result recording is unavailable here. Earlier records remain readable.", 18);
                    if (summaries.isEmpty()) text(body, "No results have been recorded for this task.", 20);
                    for (Object item : summaries) {
                        Map<String,Object> summary = PanelJson.object(item);
                        button(body, PanelToolResults.label(summary), () -> { if (resultCurrent(lease)) readResults(lease, body, summary); });
                    }
                } else {
                    Map<String,Object> result = PanelJson.object(received);
                    text(body, PanelToolResults.label(selected), 20);
                    if (selected.get("title") != null) text(body, PanelText.plain(PanelJson.text(selected.get("title"))), 18);
                    if (PanelJson.integer(PanelJson.object(result.get("redaction")).get("replacements"),0,9007199254740991L)>0) text(body,"Some private text was removed.",16);
                    if ("command".equals(result.get("kind"))) resultBody(body, PanelToolResults.body(result.get("output")), lease);
                    else {
                        List<Object> files = PanelJson.array(result.get("files"));
                        text(body, files.size() + " of " + result.get("files_total") + " changed-file entries recorded.", 18);
                        LinearLayout selectedFile=column();
                        for (Object item : files) {
                            Map<String,Object> file = PanelJson.object(item);
                            button(body,file.get("path")==null?"File path unavailable":PanelText.plain(PanelJson.text(file.get("path")))+(PanelJson.bool(file.get("path_complete"))?"":"\nShortened"),()->{if(resultCurrent(lease))resultFile(selectedFile,file,lease);});
                        }
                        body.addView(selectedFile);
                        if(!files.isEmpty())text(selectedFile,"Choose a file to read its recorded patch.",18);
                    }
                    button(body, "Recent results", () -> { if (resultCurrent(lease)) readResults(lease, body, null); });
                }
                ui.postDelayed(refresh, 3000);
            });
        });
    }
    private void resultBody(LinearLayout parent, Map<String,Object> body, PanelToolResults.Lease lease) {
        text(parent, PanelToolResults.note(body), 17);
        if (body.get("text") == null) return;
        List<String> pages = PanelToolResults.pages(PanelJson.text(body.get("text")));
        LinearLayout page = column(); parent.addView(page);
        resultPage(page, pages, 0, lease);
    }
    private void resultFile(LinearLayout parent,Map<String,Object> file,PanelToolResults.Lease lease){
        parent.removeAllViews();text(parent,PanelJson.text(file.get("kind"))+"\n"+(file.get("path")==null?"File path unavailable":PanelText.plain(PanelJson.text(file.get("path")))),20);
        if(file.get("move_path_scope")!=null)text(parent,file.get("move_path")==null?"Destination path unavailable":"To\n"+PanelText.plain(PanelJson.text(file.get("move_path")))+(PanelJson.bool(file.get("move_path_complete"))?"":"\nShortened"),18);
        resultBody(parent,PanelToolResults.body(file.get("patch")),lease);
    }
    private void resultPage(LinearLayout page, List<String> pages, int index, PanelToolResults.Lease lease) {
        page.removeAllViews(); text(page, "Content " + (index + 1) + " of " + pages.size(), 16);
        TextView outside=text(page,pages.get(index),19);outside.setTypeface(android.graphics.Typeface.create(android.graphics.Typeface.MONOSPACE,android.graphics.Typeface.ITALIC));outside.setTextIsSelectable(true);
        if (index > 0) button(page, "Previous content", () -> { if (resultCurrent(lease)) resultPage(page, pages, index - 1, lease); });
        if (index + 1 < pages.size()) button(page, "Next content", () -> { if (resultCurrent(lease)) resultPage(page, pages, index + 1, lease); });
    }

    private void reviewInstruction(PanelUiState.Context scope) {
        if (!authority.commandsAllowed() || !preview && (scope == null || !scope.same(actionContext()))) return;
        String text = draft;
        reviewed("Review your instruction", PanelText.plain(text), "Send instruction", scope, () -> send("execution.send_instruction", PanelJson.map("execution_id", preview ? executionId : scope.execution, "text", text), null, null, scope));
    }
    private void showRequest(PanelUiState.Context scope) {
        text(content,"Ask",25);
        text(content,"Direct this task in your own words. The workspace can read context and continue authorized work. Agent permissions and questions still need your decision.",18);
        if(preview){text(content,"Synthetic preview: workspace requests are not available here. No model or agent action is sent.",18);return;}
        requestAvailability=text(content,"Checking workspace availability…",17);
        text(content,"Your request",20); if(requestSpoken) text(content,"From speech. Read every word.",17);
        requestEditor=new EditText(this);requestEditor.setText(requestText);requestEditor.setTextColor(Color.WHITE);requestEditor.setTextSize(20);requestEditor.setMinLines(3);requestEditor.setMaxLines(8);requestEditor.setContentDescription("Your workspace request");requestEditor.setFilters(new InputFilter[]{ordinaryTextLimit()});requestEditor.setInputType(android.text.InputType.TYPE_CLASS_TEXT|android.text.InputType.TYPE_TEXT_FLAG_MULTI_LINE|android.text.InputType.TYPE_TEXT_FLAG_NO_SUGGESTIONS);content.addView(requestEditor,new LinearLayout.LayoutParams(-1,-2));
        voiceEditor(content, requestEditor, "request");
        requestActions=new LinearLayout(this);requestActions.setOrientation(LinearLayout.VERTICAL);content.addView(requestActions,new LinearLayout.LayoutParams(-1,-2));showRequestActions(scope);
    }
    private void showRequestActions(PanelUiState.Context scope){
        if(requestActions==null||requestAvailability==null)return;requestActions.removeAllViews();
        boolean offered=agentOffer!=null&&"available".equals(agentOffer.get("availability"));
        requestAvailability.setText(offered?"Model\n"+PanelText.plain(PanelJson.text(agentOffer.get("model")))+"\nOn Your Computer":agentOffer==null?"Checking workspace availability…":"Workspace unavailable\n"+PanelText.plain(PanelJson.text(PanelJson.object(agentOffer.get("reason")).get("message"))));
        if(requestPending!=null){text(requestActions,"Waiting to confirm the last send. Its original reference is saved. It will not be sent again.",18);final String pending=requestPending;if(knownUnsent(scope,pending,requestId))button(requestActions,"Clear unsent request",()->reviewed("Clear this unsent request?","This request was saved before sending and was never attempted. Your words stay here. Clearing it does not send anything.","Clear unsent request",scope,()->clearUnsent(scope,pending)));return;}
        final String selectedRequest=requestId;
        if(selectedRequest==null){if(offered)button(requestActions,"Review workspace request",()->{rememberDraft();final String words=requestText;if(!words.trim().isEmpty())reviewed("Review your workspace request",PanelText.plain(words),"Send workspace request",scope,()->sendWorkspace("workspace.request.submit",PanelJson.map("project_id",scope.project,"workstream_id",scope.work,"execution_id",scope.execution,"text",words),scope,null));});return;}
        if(requestView==null){text(requestActions,"This saved request is being checked against the current task. Its reference and your words are retained.",18);return;}
        String state=PanelJson.text(requestView.get("status"));
        text(requestActions,requestState(state)+"\nA response is model-reported; it does not certify software completion.",18);
        if(requestView.get("reply")!=null){Map<String,Object> reply=PanelJson.object(requestView.get("reply"));button(requestActions,"Workspace reply",()->reviewed("Reported workspace reply",PanelText.plain(PanelJson.text(reply.get("text")))+"\n\nReported by "+PanelText.plain(PanelJson.text(reply.get("model")))+" on this Mac. Actions and software output remain separate evidence.","Done",scope,this::closeReview));}
        if(!PanelJson.array(requestView.get("steps")).isEmpty())button(requestActions,"Actions",()->{
            StringBuilder whole=new StringBuilder("Actions\nSending an instruction and finishing the agent’s turn are separate outcomes. Earlier actions remain in task activity.\n");
            whole.append("Earlier actions\n").append(requestView.get("earlier_steps")).append('\n');
            for(Object item:PanelJson.array(requestView.get("steps"))){Map<String,Object> step=PanelJson.object(item);whole.append('\n').append(toolLabel(PanelJson.text(step.get("tool_name")))).append('\n').append(PanelWorkspaceRequest.receiptWords(step)).append('\n').append(PanelText.plain(PanelJson.text(step.get("summary")))).append('\n');if(step.get("command_status")!=null)whole.append("Send\n").append(PanelText.plain(PanelJson.text(step.get("command_status")))).append('\n');if(step.get("runtime_status")!=null)whole.append("Agent turn\n").append(stateName(PanelJson.text(step.get("runtime_status")))).append('\n');}
            reviewed("Actions",whole.toString(),"Done",scope,this::closeReview);
        });
        if(!state.equals("stopped")){
            if(offered)button(requestActions,"Review continuation",()->{rememberDraft();final String words=requestText.trim().isEmpty()?null:requestText;reviewed("Review request continuation",words==null?"Continue this same recorded request with its existing direction.":PanelText.plain(words),"Send continuation",scope,()->sendWorkspace("workspace.request.resume",PanelJson.map("request_id",selectedRequest,"text",words),scope,selectedRequest));});
            button(requestActions,"Stop request planning",()->reviewed("Stop request planning?","This stops the workspace request’s planning. The agent’s existing work continues until you separately stop that agent.","Stop planning",scope,()->sendWorkspace("workspace.request.cancel",PanelJson.map("request_id",selectedRequest),scope,selectedRequest)));
        }else button(requestActions,"Start another direction",()->{if(!saveState())return;requestId=null;requestView=null;show();saveState();});
    }
    private static String requestState(String state){switch(state){case "responded":return "Reported response";case "waiting_for_runtime":return "Waiting for the agent’s outcome";case "waiting_for_human":return "Needs your direction or decision";case "running":return "Planning";case "stopped":return "Planning stopped";case "interrupted":return "Planning paused";case "failed":return "Planning failed";default:return PanelText.plain(state);}}
    private static String toolLabel(String name){switch(name){case "read_workspace":return "Read task context";case "read_events":return "Read task activity";case "read_understanding":return "Read project understanding";case "read_evaluation":return "Read checks";case "continue_task":return "Continue agent work";case "observe_task":return "Check agent outcome";case "ask_person":return "Ask for your direction";case "person_response":return "Your additional direction";default:return "Planning step";}}
    private static Map<String,Object> unavailableAgent(String message){return PanelJson.map("availability","unavailable","reason",PanelJson.map("code","unavailable","message",message));}
    private void pollRequest(){
        final PanelUiState.Context scope=actionContext();if(compactPanel||!authority.controlsReady()||scope==null||identity==null||busy||!visible||review!=null)return;
        final PanelIdentity target=identity;final String savedId=requestId,pending=requestPending;final int epoch=generation;
        busy=true;PanelTransport next=new PanelTransport(files::connection);transport=next;
        worker.execute(()->{
            Map<String,Object> offer=unavailableAgent("The workspace could not be checked. Your words are retained."),view=null;PanelWorkspaceRequest.Recovery recovery=null;String message=null;
            try{
                PanelTransport.Result response=next.request(target,"GET","/api/workspace/agent",null);
                if(response.status==200)offer=PanelWorkspaceRequest.offer(response.body);else if(response.status==404)offer=unavailableAgent("This host does not offer workspace requests. Your words are retained.");
                String id=savedId;
                if(pending!=null){response=next.request(target,"GET","/api/workspace/commands/"+pending,null);if(response.status==200){String expected=readSends().type(pending,scope,savedId);recovery=PanelWorkspaceRequest.command(response.body,pending,savedId,scope,expected);message=recovery.message;if(recovery.settled)id=recovery.requestId;}else message="The last send cannot be confirmed yet. Its reference is saved; it will not be sent again.";}
                if(id!=null){response=next.request(target,"GET","/api/workspace/requests/"+id,null);if(response.status==200)view=PanelWorkspaceRequest.read(response.body,id,scope);else message="This recorded request could not be read for the current task. Your reference and words are retained.";}
            }catch(Exception error){message="Workspace request state could not be checked. Your reference and words are retained.";}
            final Map<String,Object> receivedOffer=offer,receivedView=view;final PanelWorkspaceRequest.Recovery resolved=recovery;final String status=message;
            ui.post(()->{
                if(!visible||generation!=epoch)return;
                if(!scope.same(actionContext())||!java.util.Objects.equals(savedId,requestId)||!java.util.Objects.equals(pending,requestPending)){busy=false;ui.postDelayed(refresh,1000);return;}
                busy=false;agentOffer=receivedOffer;requestView=null;
                if (voiceLease != null && !voiceCurrent(snapshot, identity)) cancelVoice();
                boolean stored=resolved==null||!resolved.settled||resolvePending(resolved,scope,pending);
                if(stored&&requestPending==null)requestView=receivedView;
                if(status!=null)connection.setText(withStorageWarning(status));
                if(requestEditor==null||requestActions==null)show();else showRequestActions(scope);
                ui.postDelayed(refresh,3000);
            });
        });
    }
    private boolean resolvePending(PanelWorkspaceRequest.Recovery result,PanelUiState.Context scope,String pending){
        if(!scope.same(actionContext())||!java.util.Objects.equals(pending,requestPending))return false;
        cancelVoice(); String oldId=requestId;requestId=result.requestId;requestPending=null;
        if(saveState()){try{synchronized(this){PanelRequestSends ledger=readSends();ledger.forgetSettled(pending,scope,oldId);files.write(PanelFiles.REQUEST_SENDS,ledger.json());}}catch(Exception error){/* A stale phase record grants no send or cleanup authority. */}return true;}
        requestId=oldId;requestPending=pending;rememberDraft();return false;
    }
    private PanelRequestSends readSends()throws IOException{return files.readRequestSends();}
    private synchronized boolean knownUnsent(PanelUiState.Context scope,String pending,String id){try{return readSends().unsent(pending,scope,id);}catch(Exception error){return false;}}
    private synchronized void clearUnsent(PanelUiState.Context scope,String pending){
        if(busy||!scope.same(actionContext())||!java.util.Objects.equals(pending,requestPending))return;
        try{PanelRequestSends ledger=readSends();if(!ledger.unsent(pending,scope,requestId))throw new IOException();requestPending=null;if(!saveState()){requestPending=pending;rememberDraft();return;}ledger.removeUnsent(pending,scope,requestId);files.write(PanelFiles.REQUEST_SENDS,ledger.json());closeReview();connection.setText("The unsent request was cleared. Your words are retained; nothing was sent.");show();}
        catch(Exception error){connection.setText(withStorageWarning("The unsent request could not be cleared safely. Keep this view open."));}
    }
    private void sendWorkspace(String type,Map<String,Object> payload,PanelUiState.Context scope,String expectedRequest){
        if(preview||!authority.commandsAllowed()||identity==null||snapshot==null||busy||!visible||scope==null||!scope.same(actionContext())||requestPending!=null||!java.util.Objects.equals(expectedRequest,requestId))return;
        rememberDraft();final Map<String,Object> envelope=PanelModel.command(type,payload);final String pending=PanelJson.text(envelope.get("command_id"));
        // Both ordinary stores are durable before POST. Failure blocks sending and preserves the ID.
        final PanelIdentity target=identity;final int epoch=generation;
        final PanelRequestDispatch.Prepared prepared=PanelRequestDispatch.prepare(envelope,new PanelRequestDispatch.Store(){
            public boolean persist(String id){synchronized(AgentPanelActivity.this){try{PanelRequestSends ledger=readSends();ledger.stage(id,scope,expectedRequest,type);files.write(PanelFiles.REQUEST_SENDS,ledger.json());requestPending=id;return saveState();}catch(Exception error){storageWarning="This request could not be saved safely. Nothing was sent.";return false;}}}
            public boolean attempted(String id){synchronized(AgentPanelActivity.this){try{if(!visible||generation!=epoch||!target.same(files.connection()))return false;PanelRequestSends ledger=readSends();ledger.attempted(id,scope,expectedRequest,type);files.write(PanelFiles.REQUEST_SENDS,ledger.json());return true;}catch(Exception error){return false;}}}
        });
        if(prepared==null){connection.setText(withStorageWarning("Nothing was sent. Save needs attention; your words are retained."));return;}
        closeReview();busy=true;agentOffer=requestView=null;
        PanelTransport next=new PanelTransport(files::connection);transport=next;connection.setText("Checking this task before sending the workspace command…");
        worker.execute(()->{
            PanelWorkspaceRequest.Recovery outcome=null;String message="The original command outcome is unknown. Its reference is retained; no retry is sent.";boolean posted=false;PanelModel fresh=null;
            try{
                PanelTransport.Result response=next.request(target,"GET","/api/snapshot",null);if(response.status!=200)throw new IOException();fresh=new PanelModel(response.body);Map<String,Object> run=fresh.execution(scope.work);
                if(!scope.journal.equals(fresh.journal)||run==null||!scope.project.equals(run.get("project_id"))||!scope.execution.equals(run.get("execution_id")))throw new IOException();
                if(expectedRequest!=null){response=next.request(target,"GET","/api/workspace/requests/"+expectedRequest,null);if(response.status!=200)throw new IOException();PanelWorkspaceRequest.read(response.body,expectedRequest,scope);}
                if(!type.equals("workspace.request.cancel")){response=next.request(target,"GET","/api/workspace/agent",null);if(response.status!=200||!"available".equals(PanelWorkspaceRequest.offer(response.body).get("availability")))throw new IOException();}
                if(!visible||generation!=epoch)throw new IOException();
                posted=true;response=prepared.post(next,target);
                if(response.status==200||response.status==202||response.status==422){PanelWorkspaceRequest.submission(response.body,pending,expectedRequest,scope,type);outcome=prepared.recover(next,target,expectedRequest,scope);if(outcome!=null)message=outcome.message;}
            }catch(Exception error){if(!posted||knownUnsent(scope,pending,expectedRequest))message="Nothing was sent. Clear the saved unsent request when storage is available; your words are retained.";}
            final PanelWorkspaceRequest.Recovery result=outcome;final String status=message;final PanelModel latest=fresh;
            ui.post(()->{
                if(!visible||generation!=epoch)return;
                if(!scope.same(actionContext())||!pending.equals(requestPending)){busy=false;ui.postDelayed(refresh,1000);return;}
                busy=false;if(latest!=null)snapshot=latest;
                if(result!=null&&result.settled)resolvePending(result,scope,pending);
                connection.setText(withStorageWarning(status));show();ui.postDelayed(refresh,1000);
            });
        });
    }
    private void approval(Map<String, Object> request, PanelUiState.Context scope) {
        if (!authority.commandsAllowed() || !preview && (scope == null || !scope.same(actionContext()))) return;
        try {
            Map<String, Object> subject = PanelJson.object(request.get("subject"));
            if (!"tool_use".equals(subject.get("kind"))) throw PanelJson.invalid();
            String whole = PanelModel.approvalLabel(request) + "\n" + PanelText.plain(PanelJson.text(subject.get("tool_name"))) + "\n\n" + PanelText.plain(PanelJson.text(subject.get("summary")));
            boolean canRespond = snapshot.capability(snapshot.execution(workId), "respond_to_approval") && PanelModel.running(snapshot.execution(workId));
            boolean canApprove = canRespond && PanelJson.bool(request.get("approvable"));
            if (!canRespond) { reviewed("Read the agent’s request", whole + "\n\nThis runtime cannot receive a permission response here.", "Done", this::closeReview); return; }
            LinearLayout body = dialog("Read the agent’s request"); text(body, whole + (canApprove ? "" : "\n\nThis request was shortened and cannot be approved."), 20);
            ScrollView scroll = (ScrollView) body.getParent(); LinearLayout root = (LinearLayout) scroll.getParent();
            if (canApprove) gatedButton(root, body, scroll, "Approve", scope, () -> send("execution.respond_to_approval", PanelJson.map("execution_id", preview ? executionId : scope.execution, "approval_id", request.get("approval_id"), "decision", "approve", "message", null), request, null, scope));
            gatedButton(root, body, scroll, "Decline", scope, () -> send("execution.respond_to_approval", PanelJson.map("execution_id", preview ? executionId : scope.execution, "approval_id", request.get("approval_id"), "decision", "deny", "message", null), request, null, scope));
        } catch (Exception error) { connection.setText("This request cannot be read safely. Open Workspace."); }
    }
    private void question(Map<String, Object> request, PanelUiState.Context scope) {
        if (!authority.commandsAllowed() || !preview && (scope == null || !scope.same(actionContext()))) return;
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
            reviewed("Read the whole question", whole.toString(), "Write answers", scope, () -> editAnswers(request, scope));
        } catch (Exception error) { connection.setText("This question cannot be read safely. Open Workspace."); }
    }
    private void editAnswers(Map<String, Object> request, PanelUiState.Context scope) {
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
                summary.append('\n').append(PanelText.plain(PanelJson.nullableText(p.get("header")))).append('\n').append(PanelText.plain(PanelJson.text(p.get("text")))).append('\n');
                for (Object option : PanelJson.array(p.get("options"))) { Map<String, Object> o = PanelJson.object(option); summary.append(PanelText.plain(PanelJson.text(o.get("label")))).append('\n').append(PanelText.plain(PanelJson.nullableText(o.get("description")))).append('\n'); }
                summary.append("Your answer\n");
                for (Object label : selected) summary.append("• ").append(PanelText.plain(PanelJson.text(label))).append('\n');
                if (text != null) summary.append(PanelText.plain(text)).append('\n');
            }
            if (!snapshot.answer(snapshot.execution(workId), request, answers)) { connection.setText("Answer every question using the offered choices or allowed text."); return; }
            reviewed("Review every answer", summary.toString(), "Send answers", scope, () -> send("execution.answer_question", PanelJson.map("execution_id", preview ? executionId : scope.execution, "question_id", request.get("question_id"), "answers", answers), request, answers, scope));
        });
    }

    private LinearLayout dialog(String title) {
        closeReview(); reviewOwner = authority.open(); Dialog next = new ReviewDialog(reviewOwner); review = next;
        final long id = reviewOwner; next.setOnDismissListener(ignored -> { if (review == next) { review = null; authority.close(id); } });
        LinearLayout root = column(); root.setBackground(PanelKit.glass(PanelKit.CORNER, getResources().getDisplayMetrics().density));
        int dialogPad = PanelKit.px(PanelKit.PAD, getResources().getDisplayMetrics().density); root.setPadding(dialogPad, dialogPad, dialogPad, dialogPad); root.setClipToPadding(false);
        text(root, title, 25);
        button(root, "Back", this::closeReview, true);
        ScrollView scroll = new ScrollView(this); LinearLayout body = column(); scroll.addView(body); root.addView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));
        next.setContentView(root); if (!preview && !capturable) next.getWindow().addFlags(WindowManager.LayoutParams.FLAG_SECURE); next.show(); next.getWindow().setLayout(-1, -1);
        return body;
    }
    /** Entire body, exact scope, current modal and focus lifetime precede deliberate confirmation. */
    private void reviewed(String title, String whole, String label, Runnable next) { reviewed(title, whole, label, actionContext(), next); }
    private void reviewed(String title, String whole, String label, PanelUiState.Context scope, Runnable next) {
        LinearLayout body = dialog(title); text(body, whole, 20);
        ScrollView scroll = (ScrollView) body.getParent(); LinearLayout root = (LinearLayout) scroll.getParent();
        gatedButton(root, body, scroll, label, scope, next);
    }
    private void gatedButton(LinearLayout root, LinearLayout body, ScrollView scroll, String label, PanelUiState.Context scope, Runnable next) {
        PanelUiState.Review ticket = authority.review(reviewOwner, scope); Dialog shown = review;
        Button continueButton = button(root, label, () -> { if (review == shown && authority.authorize(ticket, actionContext())) next.run(); }); continueButton.setEnabled(false);
        Runnable read = () -> { if (review == shown && body.getHeight() > 0 && scroll.getHeight() > 0 && scroll.getScrollY() + scroll.getHeight() >= body.getHeight()) continueButton.setEnabled(authority.read(ticket)); };
        reviewReads.add(read); scroll.getViewTreeObserver().addOnScrollChangedListener(read::run); scroll.getViewTreeObserver().addOnGlobalLayoutListener(read::run); scroll.post(read);
    }

    private void send(String type, Map<String, Object> payload, Map<String, Object> request, List<Object> answers, PanelUiState.Context scope) {
        if (preview) { closeReview(); connection.setText("Synthetic preview: no request was sent."); return; }
        if (!authority.commandsAllowed() || snapshot == null || identity == null || busy || !visible || scope == null || !scope.same(actionContext())) return;
        rememberDraft();
        final PanelIdentity target = identity; final String journal = scope.journal, selectedWork = scope.work, selectedProject = scope.project, selectedExecution = scope.execution;
        final int requestGeneration = generation;
        if (!selectedExecution.equals(payload.get("execution_id"))) return;
        final Map<String, Object> envelope = PanelModel.command(type, payload);
        commandId = PanelJson.text(envelope.get("command_id")); closeReview(); busy = true; connection.setText("Checking this task before sending…");
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
                        result = PanelModel.commandStatus(command);
                    } else result = "The host did not confirm this request. Check the task before trying again.";
                }
            } catch (PanelTransport.Refused error) { result = error.unknownEffect ? "The connection ended after sending. The effect is unknown; check this task before trying again." : "Your request was not sent. Check the connection."; }
            catch (Exception error) { result = "The host did not confirm this request. Check the task before trying again."; }
            String message = result; boolean recorded = submitted, acceptedInstruction = accepted; PanelModel latest = fresh;
            ui.post(() -> {
                if (!visible || generation != requestGeneration) return;
                busy = false; if (latest != null) snapshot = latest;
                if (recorded) { commandId = PanelJson.text(envelope.get("command_id")); if (acceptedInstruction && type.equals("execution.send_instruction")) { draft = ""; spoken = false; instruction = null; } }
                connection.setText(withStorageWarning(message)); show(); saveState(); ui.postDelayed(refresh, 1000);
            });
        });
    }

    private void activity() {
        if (preview) { reviewed("Synthetic activity", "The Agent Says\nThis is the public synthetic preview. No work ran from this panel and no live activity was read.", "Done", this::closeReview); return; }
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
                    for (String field : new String[] {"text", "message", "summary"}) if (payload.get(field) instanceof String) text.append("The Agent Says\n").append(PanelText.plain((String) payload.get(field))).append('\n');
                }
                whole = text.toString();
            } catch (Exception error) { whole = "Activity could not be read. Check your connection in Workspace."; }
            String shown = whole;
            ui.post(() -> { if (!visible || generation != requestGeneration) return; busy = false; reviewed("Recorded activity", shown, "Done", this::closeReview); ui.postDelayed(refresh, 3000); });
        });
    }
    private void returnToWorkspace() {
        if (preview) {
            try { startActivity(new Intent().setClassName(getPackageName(), "com.unity3d.player.UnityPlayerGameActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)); finishAndRemoveTask(); }
            catch (Exception error) { connection.setText("Workspace could not open."); }
            return;
        }
        rememberDraft();
        // The selected record is an additional durable recovery path when the aggregate editor
        // store is full. It carries ordinary context only; Unity must validate fresh state.
        PanelContinuation selected = editorRecord();
        String staged = null;
        try {
            if (identity != null && !identity.same(files.connection())) throw new IOException();
            boolean previousReturn = files.exists(PanelFiles.RETURN);
            if (selected != null) {
                files.write(PanelFiles.STATE, selected.json());
                if (!previousReturn) { files.write(PanelFiles.RETURN, selected.json()); staged = selected.string("handoff_id"); }
            }
            startActivity(new Intent().setClassName(getPackageName(), "com.unity3d.player.UnityPlayerGameActivity").addFlags(Intent.FLAG_ACTIVITY_NEW_TASK));
            finishAndRemoveTask();
        } catch (Exception error) { if (staged != null) files.acknowledgeReturn(staged); connection.setText("Workspace could not open. Your draft remains here."); }
    }
}
