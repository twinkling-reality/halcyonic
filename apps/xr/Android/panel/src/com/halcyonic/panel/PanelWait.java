package com.halcyonic.panel;

/** Consumed wait lifecycle and Glaze text-lift math; no command or completion semantics. */
final class PanelWait {
    static final double SECONDS = 1.6, WIDTH = 0.35, DEPTH = 0.55;
    private boolean waiting;
    void start() { waiting = true; }
    void stop() { waiting = false; }
    boolean waiting() { return waiting; }
    boolean moves(boolean still, boolean systemAnimations, boolean visible) { return waiting && !still && systemAnimations && visible; }
    static double start(double phase) { return -2 * WIDTH + phase * (1 + 2 * WIDTH); }
    static double lift(double at, double phase) {
        double middle = start(phase) + WIDTH;
        double off = Math.abs(at - middle) / WIDTH;
        return off >= 1 ? 0 : DEPTH * (0.5 + 0.5 * Math.cos(Math.PI * off));
    }
    static String motion(boolean still) { return still ? "{\"version\":1,\"still\":true}" : "{\"version\":1,\"still\":false}"; }
    static boolean still(String value) {
        if (value == null) return true;
        if (value.equals(motion(false))) return false;
        if (value.equals(motion(true))) return true;
        return true; // Missing, corrupt, incompatible or unknown preferences keep things still.
    }
    static void validateMotion(String value) {
        if (!motion(false).equals(value) && !motion(true).equals(value)) throw PanelJson.invalid();
    }
}
