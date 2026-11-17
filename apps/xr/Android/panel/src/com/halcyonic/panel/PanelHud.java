package com.halcyonic.panel;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.ColorFilter;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.PixelFormat;
import android.graphics.RectF;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.LayerDrawable;
import android.graphics.drawable.StateListDrawable;
import android.view.Gravity;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * The compact panel beside the person's show, in the ONE design language every surface shares (FIELD's sheet, tile f7; the
 * lead, 2026-11-17 01:05Z): the owner's dark Ask glass with visionOS's corners and sheen, one face in three roles (Mirror's
 * Edge's wide spaced capitals for labels, a medium title, a plain sentence), the agent's command in mono, and the one accent
 * only as light, a lit rim and glow on the one main action, never a fill. The window is 640 x 720 dp; the tile is drawn
 * 340 x 520, so every size here is the tile's times 720 / 520. A Horizon OS panel cannot blur what is behind it, so the glass
 * is its tint alone over a clear window.
 */
final class PanelHud {
    /** The tile's size to the window's: 720 dp tall for the tile's 520. */
    static final float SCALE = 720f / 520f;
    /**
     * The glass's tint. The sheet's regular glass is 72% over a heavy blur; a Horizon OS panel cannot blur, and 72% alone lets
     * the room through sharp enough to fight the words, so the panel takes the sheet's thick glass, a little thicker (90%).
     */
    static final int GLASS = Color.argb(230, 12, 14, 22);
    /** The kit's thin-glass capsule every action stands on (FIELD's 929c6eecc, RoomButton): at rest, pointed, held. */
    static final int CAPSULE = Color.rgb(0x25, 0x27, 0x2E);
    static final int CAPSULE_POINTED = Color.rgb(0x3A, 0x3C, 0x43);
    static final int CAPSULE_HELD = Color.rgb(0x30, 0x32, 0x39);
    /** The lit main action's own plate under its rim (RoomButton, Lit). */
    static final int MAIN_PLATE = Color.rgb(0x2A, 0x2C, 0x33);
    /** The glass's hairline, lit along its top and easing to its foot (RoomSurface.LitEdgeTop, LitEdgeFoot). */
    static final int EDGE_TOP = Color.rgb(0x5A, 0x5D, 0x66);
    static final int EDGE_FOOT = Color.rgb(0x2B, 0x2C, 0x33);
    static final int W1 = Color.WHITE;
    static final int W2 = Color.argb(189, 255, 255, 255);
    static final int W3 = Color.argb(133, 255, 255, 255);
    /** The one accent, as light only (#F39552). */
    static final int LIGHT = Color.rgb(243, 149, 82);
    private PanelHud() {}

    static int px(float tile, float density) { return Math.round(tile * SCALE * density); }
    /** The room the main action's glow takes outside its rim, half the kit's 12 dp, on every press so all line up. */
    static int glow(float density) { return Math.round(12 * density / 2f); }
    static float sp(float tile) { return tile * SCALE; }

    /** The panel's glass: the tint, the sheen falling from its top edge, and its one hairline, round corners. */
    static Drawable glass(float density) {
        float radius = px(32, density);
        GradientDrawable tint = new GradientDrawable(); tint.setColor(GLASS); tint.setCornerRadius(radius);
        GradientDrawable sheen = new GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM,
            new int[] {Color.argb(23, 255, 255, 255), Color.argb(0, 255, 255, 255), Color.argb(0, 255, 255, 255)});
        sheen.setCornerRadius(radius);
        return new LayerDrawable(new Drawable[] {tint, sheen, new Edge(radius, Math.max(1f, density))});
    }

    /** A label in wide spaced capitals (the type's first role). */
    static TextView cap(Context context, String words, int colour) {
        TextView view = new TextView(context); view.setText(words); view.setAllCaps(true); view.setLetterSpacing(0.24f);
        view.setTextSize(sp(15)); view.setTextColor(colour); view.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        view.setIncludeFontPadding(false);
        return view;
    }

    /** The state over the card: a small white dot and the word in spaced capitals. */
    static LinearLayout state(Context context, String word, float density) {
        LinearLayout row = new LinearLayout(context); row.setOrientation(LinearLayout.HORIZONTAL); row.setGravity(Gravity.CENTER_VERTICAL);
        android.view.View dot = new android.view.View(context);
        GradientDrawable round = new GradientDrawable(); round.setShape(GradientDrawable.OVAL); round.setColor(W1); dot.setBackground(round);
        LinearLayout.LayoutParams at = new LinearLayout.LayoutParams(px(8, density), px(8, density)); at.setMargins(0, 0, px(10, density), 0);
        row.addView(dot, at);
        row.addView(cap(context, word, W1), new LinearLayout.LayoutParams(-2, -2));
        return row;
    }

    /** The task's name, theirs, in its own case (the type's second role). */
    static TextView title(Context context, String words) {
        TextView view = new TextView(context); view.setText(words); view.setTextSize(sp(26)); view.setTextColor(W1);
        view.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL)); view.setLineSpacing(0, 1.15f); view.setMaxLines(3);
        view.setEllipsize(android.text.TextUtils.TruncateAt.END);
        return view;
    }

    /** The agent's command, whole as far as three lines go, in mono. */
    static TextView mono(Context context, String words) {
        TextView view = new TextView(context); view.setText(words); view.setTextSize(sp(16)); view.setTextColor(W2);
        view.setTypeface(Typeface.MONOSPACE); view.setLineSpacing(0, 1.3f); view.setMaxLines(3); view.setEllipsize(android.text.TextUtils.TruncateAt.END);
        return view;
    }

    /** A plain sentence (the type's third role): the agent's question, in its own words. */
    static TextView say(Context context, String words) {
        TextView view = new TextView(context); view.setText(words); view.setTextSize(sp(17)); view.setTextColor(W2); view.setLineSpacing(0, 1.3f);
        view.setMaxLines(3); view.setEllipsize(android.text.TextUtils.TruncateAt.END);
        return view;
    }

    /**
     * A press as the language draws it: a glyph and the words. The main action is a thin plate with the one accent as a lit
     * rim and a soft glow, its glyph in the light; a quiet press has no plate and white-dim words.
     */
    static void press(Button button, String label, boolean main, boolean workspaceGlyph, float density) {
        button.setText(label); button.setAllCaps(false); button.setTextSize(sp(19)); button.setSingleLine(true); button.setContentDescription(label);
        button.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL)); button.setTextColor(W1);
        button.setGravity(Gravity.CENTER_VERTICAL); button.setStateListAnimator(null);
        button.setMinHeight(px(56, density)); button.setMinimumHeight(px(56, density)); button.setMinWidth(0); button.setMinimumWidth(0);
        int glow = glow(density);
        button.setPadding(px(22, density) + glow, glow, px(26, density) + glow, glow);
        Glyph glyph = new Glyph(workspaceGlyph, main ? LIGHT : W1, density);
        button.setCompoundDrawablesRelative(glyph, null, null, null); button.setCompoundDrawablePadding(px(12, density));
        float radius = px(28, density);
        StateListDrawable states = new StateListDrawable();
        if (main) {
            states.addState(new int[] {android.R.attr.state_pressed}, lit(CAPSULE_HELD, radius, glow, density));
            states.addState(new int[] {android.R.attr.state_hovered}, lit(CAPSULE_POINTED, radius, glow, density));
            states.addState(new int[0], lit(MAIN_PLATE, radius, glow, density));
        } else {
            states.addState(new int[] {android.R.attr.state_pressed}, capsule(CAPSULE_HELD, radius, glow));
            states.addState(new int[] {android.R.attr.state_hovered}, capsule(CAPSULE_POINTED, radius, glow));
            states.addState(new int[0], capsule(CAPSULE, radius, glow));
        }
        button.setBackground(states);
    }

    /** A quiet action's crisp thin-glass capsule, inset where the main action's glow would stand so the two line up. */
    private static Drawable capsule(int plate, float radius, int glow) {
        GradientDrawable shape = new GradientDrawable(); shape.setCornerRadius(radius); shape.setColor(plate);
        LayerDrawable layers = new LayerDrawable(new Drawable[] {shape});
        layers.setLayerInset(0, glow, glow, glow, glow);
        return layers;
    }

    /** The main action's plate: the thin fill, its lit rim, and two faint static rings of glow outside it, never a pulse. */
    private static Drawable lit(int plate, float radius, int glow, float density) {
        GradientDrawable far = new GradientDrawable(); far.setCornerRadius(radius + glow); far.setColor(Color.TRANSPARENT);
        far.setStroke(Math.max(1, Math.round(glow / 2f)), Color.argb(30, 243, 149, 82));
        GradientDrawable near = new GradientDrawable(); near.setCornerRadius(radius + glow / 2f); near.setColor(Color.TRANSPARENT);
        near.setStroke(Math.max(1, Math.round(glow / 2f)), Color.argb(56, 243, 149, 82));
        GradientDrawable rim = new GradientDrawable(); rim.setCornerRadius(radius); rim.setColor(plate);
        rim.setStroke(Math.max(1, Math.round(1.5f * SCALE * density)), LIGHT);
        LayerDrawable layers = new LayerDrawable(new Drawable[] {far, near, rim});
        layers.setLayerInset(1, glow / 2, glow / 2, glow / 2, glow / 2);
        layers.setLayerInset(2, glow, glow, glow, glow);
        return layers;
    }

    /** The glass's one hairline, its colour easing from the lit top (#5A5D66) to the foot (#2B2C33). */
    static final class Edge extends Drawable {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final float radius, width;

        Edge(float radius, float width) {
            this.radius = radius; this.width = width;
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(width);
        }

        @Override public void draw(Canvas canvas) {
            android.graphics.Rect b = getBounds();
            paint.setShader(new android.graphics.LinearGradient(0, b.top, 0, b.bottom, EDGE_TOP, EDGE_FOOT, android.graphics.Shader.TileMode.CLAMP));
            float half = width / 2f;
            canvas.drawRoundRect(new RectF(b.left + half, b.top + half, b.right - half, b.bottom - half), radius, radius, paint);
        }

        @Override public void setAlpha(int alpha) { paint.setAlpha(alpha); }
        @Override public void setColorFilter(ColorFilter filter) { paint.setColorFilter(filter); }
        @Override public int getOpacity() { return PixelFormat.TRANSLUCENT; }
    }

    /** The two glyphs the tile draws, stroked as its 24-unit icons: open-out for Open Task, a window for Open Workspace. */
    static final class Glyph extends Drawable {
        private final Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final boolean window;
        private final int size;

        Glyph(boolean window, int colour, float density) {
            this.window = window; size = px(22, density);
            paint.setStyle(Paint.Style.STROKE); paint.setStrokeWidth(1.8f * size / 24f); paint.setStrokeCap(Paint.Cap.ROUND);
            paint.setStrokeJoin(Paint.Join.ROUND); paint.setColor(colour);
            setBounds(0, 0, size, size);
        }

        @Override public void draw(Canvas canvas) {
            float u = getBounds().width() / 24f;
            canvas.save(); canvas.translate(getBounds().left, getBounds().top);
            if (window) {
                canvas.drawRoundRect(new RectF(4 * u, 5 * u, 20 * u, 19 * u), 3 * u, 3 * u, paint);
                canvas.drawLine(4 * u, 9 * u, 20 * u, 9 * u, paint);
            } else {
                Path path = new Path();
                path.moveTo(14 * u, 4 * u); path.lineTo(20 * u, 4 * u); path.lineTo(20 * u, 10 * u);
                path.moveTo(20 * u, 4 * u); path.lineTo(11 * u, 13 * u);
                path.moveTo(18 * u, 14 * u); path.lineTo(18 * u, 20 * u); path.lineTo(4 * u, 20 * u); path.lineTo(4 * u, 6 * u); path.lineTo(10 * u, 6 * u);
                canvas.drawPath(path, paint);
            }
            canvas.restore();
        }

        @Override public int getIntrinsicWidth() { return size; }
        @Override public int getIntrinsicHeight() { return size; }
        @Override public void setAlpha(int alpha) { paint.setAlpha(alpha); }
        @Override public void setColorFilter(ColorFilter filter) { paint.setColorFilter(filter); }
        @Override public int getOpacity() { return PixelFormat.TRANSLUCENT; }
    }
}
