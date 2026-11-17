package com.halcyonic.panel;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.LayerDrawable;
import android.view.Gravity;
import android.view.View;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * The compact panel beside the person's show (take 10), held against Titanfall 2's pilot HUD speaker card for feel only:
 * a small card at the edge of what they watch, its state alone in spaced capitals over it with a hairline running out,
 * the task's own mark where the card shows a face, and four corner ticks. While it needs them: one orange on the state,
 * the hairline, the ticks and the one action, and one soft glow, never a pulse. Drawn with the panel's own views: a solid
 * fill (a Horizon OS panel has no backdrop blur), the equipped Ask glass's colour.
 */
final class PanelHud {
    static final int FILL = Color.rgb(12, 14, 22);
    static final int ACCENT = Color.rgb(237, 120, 51);
    static final int QUIET = Color.argb(184, 255, 255, 255);
    static final int TEXT = Color.WHITE;
    static final int SECOND = Color.argb(219, 255, 255, 255);
    private PanelHud() {}

    /**
     * The state over the card: a dot, the word in spaced capitals, and a hairline running out to the card's width. At rest,
     * with no card, the task's name follows the word in its own case and weight, set apart by space, never a dot.
     */
    static LinearLayout state(Context context, String word, boolean needs, float density, String task) {
        int colour = needs ? ACCENT : QUIET;
        LinearLayout row = new LinearLayout(context); row.setOrientation(LinearLayout.HORIZONTAL); row.setGravity(Gravity.CENTER_VERTICAL);
        View dot = new View(context);
        GradientDrawable round = new GradientDrawable(); round.setShape(GradientDrawable.OVAL); round.setColor(colour); dot.setBackground(round);
        row.addView(dot, new LinearLayout.LayoutParams(px(9, density), px(9, density)));
        TextView label = new TextView(context); label.setText(word); label.setAllCaps(true); label.setLetterSpacing(0.16f); label.setTextSize(15);
        label.setTextColor(colour); label.setPadding(px(8, density), 0, px(8, density), 0); label.setContentDescription(word);
        row.addView(label, new LinearLayout.LayoutParams(-2, -2));
        if (task != null) {
            TextView name = new TextView(context); name.setText(task); name.setTextSize(17); name.setTextColor(SECOND); name.setSingleLine(true);
            name.setEllipsize(android.text.TextUtils.TruncateAt.END); name.setPadding(px(2, density), 0, px(8, density), 0);
            row.addView(name, new LinearLayout.LayoutParams(-2, -2));
        }
        View rule = new View(context);
        GradientDrawable fade = new GradientDrawable(GradientDrawable.Orientation.LEFT_RIGHT,
            new int[] {needs ? Color.argb(179, 237, 120, 51) : Color.argb(89, 255, 255, 255), Color.TRANSPARENT});
        rule.setBackground(fade);
        row.addView(rule, new LinearLayout.LayoutParams(0, Math.max(1, px(1, density)), 1));
        row.setPadding(px(2, density), 0, 0, px(8, density));
        return row;
    }

    /** The task's own mark in a quiet field, where the reference card shows the speaker's face. */
    static TextView mark(Context context, String letters, float density) {
        TextView face = new TextView(context); face.setText(letters); face.setTextSize(24); face.setTextColor(TEXT); face.setGravity(Gravity.CENTER);
        face.setLetterSpacing(0.04f);
        GradientDrawable field = new GradientDrawable(GradientDrawable.Orientation.TL_BR, new int[] {Color.rgb(63, 143, 138), Color.rgb(44, 100, 96)});
        field.setCornerRadius(px(12, density)); field.setStroke(Math.max(1, px(1, density)), Color.argb(36, 255, 255, 255));
        face.setBackground(field); face.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);
        return face;
    }

    /** A press on the card: the one orange where it is the main action, otherwise a quiet outline. */
    static void action(TextView button, String label, boolean main, float density) {
        // Title Case, the headset's actions (AUDIT's mend 1, 23:48Z): spaced capitals are the state word's alone.
        button.setText(label); button.setAllCaps(false); button.setTextSize(17); button.setGravity(Gravity.CENTER);
        button.setTypeface(android.graphics.Typeface.create("sans-serif-medium", android.graphics.Typeface.NORMAL));
        button.setTextColor(main ? TEXT : SECOND); button.setSingleLine(true); button.setContentDescription(label);
        button.setMinHeight(px(44, density)); button.setMinimumHeight(px(44, density));
        button.setPadding(px(16, density), 0, px(16, density), 0);
        GradientDrawable surface = new GradientDrawable(); surface.setCornerRadius(px(8, density));
        if (main) surface.setColor(ACCENT); else { surface.setColor(Color.TRANSPARENT); surface.setStroke(Math.max(1, px(1, density)), Color.argb(72, 255, 255, 255)); }
        button.setBackground(surface);
    }

    /** The card's frame: its fill and hairline, the soft glow while it needs them, and the four corner ticks outside it. */
    static final class Frame extends FrameLayout {
        private final Paint ticks = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final int inset, tick;

        Frame(Context context, boolean needs, float density) {
            super(context);
            inset = px(8, density); tick = px(12, density);
            setWillNotDraw(false); setPadding(inset, inset, inset, inset);
            ticks.setStyle(Paint.Style.STROKE); ticks.setStrokeWidth(Math.max(1f, 1.5f * density));
            ticks.setColor(needs ? Color.argb(242, 237, 120, 51) : Color.argb(140, 255, 255, 255));
            GradientDrawable card = new GradientDrawable(); card.setColor(FILL); card.setCornerRadius(px(14, density));
            if (needs) card.setStroke(Math.max(1, Math.round(1.5f * density)), Color.argb(230, 237, 120, 51));
            else card.setStroke(Math.max(1, px(1, density)), Color.argb(20, 255, 255, 255));
            Drawable background = card;
            if (needs) {
                // One faint glow, as two rings no wider than the ticks (AUDIT's mend 2): never animated.
                GradientDrawable far = new GradientDrawable(); far.setCornerRadius(px(20, density)); far.setColor(Color.TRANSPARENT);
                far.setStroke(px(4, density), Color.argb(14, 237, 120, 51));
                GradientDrawable near = new GradientDrawable(); near.setCornerRadius(px(17, density)); near.setColor(Color.TRANSPARENT);
                near.setStroke(px(3, density), Color.argb(28, 237, 120, 51));
                LayerDrawable layers = new LayerDrawable(new Drawable[] {far, near, card});
                layers.setLayerInset(0, inset - px(6, density), inset - px(6, density), inset - px(6, density), inset - px(6, density));
                layers.setLayerInset(1, inset - px(3, density), inset - px(3, density), inset - px(3, density), inset - px(3, density));
                layers.setLayerInset(2, inset, inset, inset, inset);
                background = layers;
            } else {
                LayerDrawable layers = new LayerDrawable(new Drawable[] {card});
                layers.setLayerInset(0, inset, inset, inset, inset);
                background = layers;
            }
            setBackground(background);
        }

        @Override protected void dispatchDraw(Canvas canvas) {
            super.dispatchDraw(canvas);
            float left = inset - tick / 2f, top = inset - tick / 2f, right = getWidth() - inset + tick / 2f, bottom = getHeight() - inset + tick / 2f;
            canvas.drawLine(left, top, left + tick, top, ticks); canvas.drawLine(left, top, left, top + tick, ticks);
            canvas.drawLine(right, top, right - tick, top, ticks); canvas.drawLine(right, top, right, top + tick, ticks);
            canvas.drawLine(left, bottom, left + tick, bottom, ticks); canvas.drawLine(left, bottom, left, bottom - tick, ticks);
            canvas.drawLine(right, bottom, right - tick, bottom, ticks); canvas.drawLine(right, bottom, right, bottom - tick, ticks);
        }
    }

    static int px(int dp, float density) { return Math.round(dp * density); }
}
