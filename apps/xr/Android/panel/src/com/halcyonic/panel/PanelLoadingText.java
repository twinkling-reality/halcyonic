package com.halcyonic.panel;

import android.animation.ValueAnimator;
import android.content.Context;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.LinearGradient;
import android.graphics.Matrix;
import android.graphics.Shader;
import android.os.SystemClock;
import android.widget.TextView;

/** A real in-flight check lifts its words, never changes the words or invents progress. */
final class PanelLoadingText extends TextView {
    private static final int NORMAL = Color.rgb(233, 237, 242), OWN = Color.rgb(184, 197, 213), ACTIVE = Color.rgb(171, 222, 243);
    private final PanelWait wait = new PanelWait();
    private final Matrix offset = new Matrix();
    private LinearGradient gradient;
    private float gradientWidth;
    private long began;
    private boolean still = true;

    PanelLoadingText(Context context) { super(context); setTextColor(NORMAL); }
    void loading(String words) {
        setText(words); still = PanelBridge.motionStill(getContext());
        setTextColor(OWN); wait.start(); began = SystemClock.uptimeMillis(); invalidate();
    }
    void stop() {
        if (wait != null) wait.stop();
        getPaint().setShader(null); setTextColor(NORMAL); invalidate();
    }
    @Override public void setText(CharSequence words, BufferType type) { if (wait != null) stop(); super.setText(words, type); }
    @Override protected void onDetachedFromWindow() { stop(); super.onDetachedFromWindow(); }
    @Override protected void onDraw(Canvas canvas) {
        boolean moving = wait.moves(still || !PanelBridge.motionPermitted(), ValueAnimator.areAnimatorsEnabled(), isShown() && getWindowVisibility() == VISIBLE);
        if (moving && getLayout() != null) {
            float width = 0;
            for (int i = 0; i < getLayout().getLineCount(); i++) width = Math.max(width, getLayout().getLineWidth(i));
            if (width > 0) {
                if (gradient == null || width != gradientWidth) {
                    int[] colours = new int[17];
                    for (int i = 0; i < colours.length; i++) {
                        double lift = PanelWait.lift(0.5 - PanelWait.WIDTH + 2 * PanelWait.WIDTH * i / (colours.length - 1), 0.5);
                        colours[i] = Color.rgb(lift(Color.red(OWN), lift), lift(Color.green(OWN), lift), lift(Color.blue(OWN), lift));
                    }
                    gradient = new LinearGradient(0, 0, (float)(2 * PanelWait.WIDTH * width), 0, colours, null, Shader.TileMode.CLAMP);
                    gradientWidth = width;
                }
                double phase = ((SystemClock.uptimeMillis() - began) / (PanelWait.SECONDS * 1000)) % 1;
                offset.setTranslate((float)(PanelWait.start(phase) * width), 0);
                gradient.setLocalMatrix(offset); getPaint().setShader(gradient);
            }
        } else {
            getPaint().setShader(null);
            int colour = wait.waiting() ? ACTIVE : NORMAL;
            if (getCurrentTextColor() != colour) setTextColor(colour);
        }
        super.onDraw(canvas);
        if (moving) postInvalidateOnAnimation();
    }
    private static int lift(int channel, double lift) { return (int)(channel + (255 - channel) * lift); }
}
