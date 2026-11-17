package com.halcyonic.panel;

import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.ColorFilter;
import android.graphics.Paint;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.Drawable;
import android.graphics.drawable.GradientDrawable;
import android.graphics.drawable.StateListDrawable;
import android.util.TypedValue;
import android.view.Gravity;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;

/**
 * The native card and strip as the SAME family as the headset's tiles (the lead, 2026-11-17 19:36Z; FIELD's 19:37Z spec): a
 * scaled copy of the headset's approval dialog (the one-language sheet's tile f4, RoomBarView.LayDialog), every size and gap
 * the kit's own (RoomGeometry, RoomBarView, RoomButton, RoomType), at one scale, 1 kit dp to 1 panel dp. One glass with a
 * thin control lighter than it (visionos-window-example.png), white words (visionos-label-vibrant-primary.png), no hue.
 * Every block starts on one shared left edge (the lead's 19:38Z kit pass), the well's ticks stay inside the pad.
 */
final class PanelKit {
    /** The kit's sizes, in dp. */
    static final float PANEL = 440, PAD = 28, CORNER = 32;
    static final float LABEL = 18, LABEL_TRACKING = 0.24f, TITLE = 24, TYPE = 20, MONO = 19, LINE = 28;
    static final float LABEL_GAP = 8, CONTENT_GAP = 14, PRESSES_GAP = 28, ROW_GAP = 32;
    static final float CORE = 48, CORE_CORNER = 24, CHIP = 32, GLYPH = 20, CHIP_PAD = 8, WORDS_GAP = 10, RIGHT_PAD = 20;
    static final float WELL_PAD = 16, WELL_CORNER = 16, TICK_ARM = 18, TICK_LINE = 1.5f, TICK_OUT = 10;
    static final float STRIP_CORNER = 28, STRIP_PAD_V = 12, STRIP_PAD_H = 24, STRIP_GAP = 16;

    /** The Ask glass, opaque; the well's thick glass over it; the ticks; the label role's white. */
    static final int GLASS = Color.rgb(0x0E, 0x12, 0x1E);
    static final int WELL = Color.argb(230, 7, 8, 13);
    static final int TICK = Color.argb(189, 255, 255, 255);
    static final int LABEL_INK = Color.argb(135, 255, 255, 255);

    private PanelKit() {}

    static int px(float dp, float density) { return Math.round(dp * density); }

    /** The card's or the strip's one glass: the Ask glass at its corner, no border, no shadow. */
    static Drawable glass(float corner, float density) {
        GradientDrawable shape = new GradientDrawable(); shape.setColor(GLASS); shape.setCornerRadius(px(corner, density));
        return shape;
    }

    private static TextView text(Context context, String words, float size, float density) {
        TextView view = new TextView(context);
        view.setText(words); view.setTextColor(Color.WHITE); view.setIncludeFontPadding(false);
        view.setTextSize(TypedValue.COMPLEX_UNIT_PX, px(size, density));
        view.setLineSpacing(0, 1f);
        return view;
    }

    /** The label role: spaced capitals, white at 53% (RoomType.Label). */
    static TextView label(Context context, String words, float density) {
        TextView view = text(context, words.toUpperCase(java.util.Locale.ROOT), LABEL, density);
        view.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL)); view.setLetterSpacing(LABEL_TRACKING);
        view.setTextColor(LABEL_INK); view.setSingleLine(true);
        return view;
    }

    /** The title: the body face at the card's title size, wrapped at words, never cut. */
    static TextView title(Context context, String words, float density) {
        TextView view = text(context, words, TITLE, density);
        view.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        return view;
    }

    /** A sentence in body type on the kit's line. */
    static TextView body(Context context, String words, float density) {
        TextView view = text(context, words, TYPE, density);
        view.setLineSpacing(px(LINE, density) - view.getPaint().getFontSpacing(), 1f);
        return view;
    }

    /**
     * The command in the well, as LayDialog stands it: mono on the kit's line, literal, on the well's thick glass with its corner
     * ticks round it. The well's glass stands on the card's shared left edge (f4); its ticks reach 10 back into the pad, never past it.
     */
    static FrameLayout well(Context context, String code, float density) {
        TextView words = text(context, code, MONO, density);
        words.setTypeface(Typeface.MONOSPACE); words.setLineSpacing(px(LINE, density) - words.getPaint().getFontSpacing(), 1f);
        words.setHorizontallyScrolling(false);
        Well well = new Well(context, density);
        int inset = px(TICK_OUT + WELL_PAD, density);
        well.setPadding(inset, inset, inset, inset);
        well.addView(words, new FrameLayout.LayoutParams(-1, -2));
        return well;
    }

    /**
     * The well as one view that paints itself (as LayDialog lays it): its thick glass 10 dp in from its bounds at the well's corner,
     * and the four corner ticks at its bounds, 18 dp arms, 1.5 dp lines, white at 74%. Painted here, not as backgrounds, so the
     * headset and the off-headset frames draw the same.
     */
    static final class Well extends FrameLayout {
        private final Paint glass = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint tick = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final float out, corner, arm;

        Well(Context context, float density) {
            super(context);
            setWillNotDraw(false); setClipToPadding(false); setClipChildren(false);
            glass.setColor(WELL); glass.setStyle(Paint.Style.FILL);
            tick.setColor(TICK); tick.setStyle(Paint.Style.STROKE); tick.setStrokeWidth(TICK_LINE * density);
            out = TICK_OUT * density; corner = WELL_CORNER * density; arm = TICK_ARM * density;
        }

        @Override protected void dispatchDraw(Canvas canvas) {
            float w = getWidth(), h = getHeight();
            canvas.drawRoundRect(new android.graphics.RectF(out, out, w - out, h - out), corner, corner, glass);
            float half = tick.getStrokeWidth() / 2f;
            float l = half, t = half, r = w - half, b = h - half;
            canvas.drawLine(l, t, l + arm, t, tick); canvas.drawLine(l, t, l, t + arm, tick);
            canvas.drawLine(r, t, r - arm, t, tick); canvas.drawLine(r, t, r, t + arm, tick);
            canvas.drawLine(l, b, l + arm, b, tick); canvas.drawLine(l, b, l, b - arm, tick);
            canvas.drawLine(r, b, r - arm, b, tick); canvas.drawLine(r, b, r, b - arm, tick);
            super.dispatchDraw(canvas);
        }
    }

    /** How far the ticks reach out of the card's content: the well's glass stands on the shared edge (f4), its ticks 10 out, inside the pad. */
    static int wellReach(float density) { return px(TICK_OUT, density); }

    /** A press's width for its words: the chip and its glyph, the gap, the words, the right pad (RoomButton's core). */
    static int pressWidth(Context context, String words, Glyph glyph, float density) {
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG); paint.setTextSize(px(TYPE, density));
        paint.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        float lead = glyph == Glyph.NONE ? RIGHT_PAD : CHIP_PAD + CHIP + WORDS_GAP;
        return px(lead + RIGHT_PAD, density) + (int) Math.ceil(paint.measureText(words));
    }

    /**
     * A press as RoomButton draws it: the core 48 tall, its corner 24, the glyph 20 in the chip 32 at its left, the words 10
     * after the chip, 20 at the right. The main action on the lighter thin plate; every other on the plain glass. No rim, no hue.
     */
    /** A press's glyph: the open arrow (Open Task), the window (Open Halora), or none (More, as the headset draws More words alone). */
    enum Glyph { OPEN, WINDOW, NONE }

    static Button press(Context context, String words, boolean main, Glyph glyph, int width, float density) {
        Button button = new Button(context);
        button.setText(words); button.setAllCaps(false); button.setSingleLine(true); button.setContentDescription(words);
        button.setTextSize(TypedValue.COMPLEX_UNIT_PX, px(TYPE, density)); button.setTextColor(Color.WHITE);
        button.setTypeface(Typeface.create("sans-serif-medium", Typeface.NORMAL));
        button.setGravity(Gravity.CENTER_VERTICAL | Gravity.START); button.setStateListAnimator(null);
        button.setIncludeFontPadding(false);
        int core = px(CORE, density);
        button.setMinHeight(core); button.setMinimumHeight(core); button.setMinWidth(0); button.setMinimumWidth(0);
        button.setPadding(px(glyph == Glyph.NONE ? RIGHT_PAD : CHIP_PAD + CHIP + WORDS_GAP, density), 0, px(RIGHT_PAD, density), 0);
        StateListDrawable plates = new StateListDrawable();
        plates.addState(new int[] {android.R.attr.state_pressed}, plate(PanelHud.CAPSULE_HELD, density));
        plates.addState(new int[] {android.R.attr.state_hovered}, plate(main ? PanelHud.MAIN_POINTED : PanelHud.CAPSULE_POINTED, density));
        plates.addState(new int[0], plate(main ? PanelHud.MAIN_PLATE : PanelHud.CAPSULE, density));
        button.setBackground(glyph == Glyph.NONE ? plates
            : new android.graphics.drawable.LayerDrawable(new Drawable[] {plates, new ChipGlyph(glyph == Glyph.WINDOW, density)}));
        button.setLayoutParams(new LinearLayout.LayoutParams(width, core));
        return button;
    }

    /**
     * A row of a list, the kit's row form (the sheet's f1 lists; list-death-stranding-cargo-management.png): no plate at rest, the
     * plain glass where pointed, held when pressed; a main action's row on the lighter thin plate. Corner 16.
     */
    static Drawable rowPlates(boolean main, float density) {
        StateListDrawable plates = new StateListDrawable();
        plates.addState(new int[] {android.R.attr.state_pressed}, rowPlate(PanelHud.CAPSULE_HELD, density));
        plates.addState(new int[] {android.R.attr.state_hovered}, rowPlate(main ? PanelHud.MAIN_POINTED : PanelHud.CAPSULE_POINTED, density));
        plates.addState(new int[0], main ? rowPlate(PanelHud.MAIN_PLATE, density) : new android.graphics.drawable.ColorDrawable(Color.TRANSPARENT));
        return plates;
    }

    private static Drawable rowPlate(int colour, float density) {
        GradientDrawable shape = new GradientDrawable(); shape.setColor(colour); shape.setCornerRadius(px(16, density));
        return shape;
    }

    private static Drawable plate(int colour, float density) {
        GradientDrawable shape = new GradientDrawable(); shape.setColor(colour); shape.setCornerRadius(px(CORE_CORNER, density));
        return shape;
    }

    /** The glyph at the chip's centre: 8 in, then half the chip; drawn white, the size the kit draws it. */
    private static final class ChipGlyph extends Drawable {
        private final PanelHud.Glyph glyph;
        private final float density;

        ChipGlyph(boolean workspaceGlyph, float density) {
            this.density = density;
            glyph = new PanelHud.Glyph(workspaceGlyph, Color.WHITE, px(GLYPH, density));
        }

        @Override public void draw(Canvas canvas) {
            int size = px(GLYPH, density);
            int left = getBounds().left + px(CHIP_PAD + (CHIP - GLYPH) / 2f, density);
            int top = getBounds().centerY() - size / 2;
            glyph.setBounds(left, top, left + size, top + size);
            glyph.draw(canvas);
        }

        @Override public void setAlpha(int alpha) { glyph.setAlpha(alpha); }
        @Override public void setColorFilter(ColorFilter filter) { glyph.setColorFilter(filter); }
        @Override public int getOpacity() { return PixelFormat.TRANSLUCENT; }
    }

    /** The card's width for its content: the widest of its blocks plus the pads, at least the presses' row, at most the panel's 440. */
    static int cardWidth(int contentWidth, int pressesWidth, float density) {
        int pads = 2 * px(PAD, density);
        int least = pressesWidth + pads;
        return Math.max(least, Math.min(px(PANEL, density), contentWidth + pads));
    }

    /** The natural single-line width of words at a size and face, for the card's width. */
    static int widthOf(String words, float size, Typeface face, float tracking, float density) {
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG); paint.setTextSize(px(size, density)); paint.setTypeface(face);
        paint.setLetterSpacing(tracking);
        int widest = 0;
        for (String line : (words == null ? "" : words).split("\n", -1)) widest = Math.max(widest, (int) Math.ceil(paint.measureText(line)));
        return widest;
    }
}
