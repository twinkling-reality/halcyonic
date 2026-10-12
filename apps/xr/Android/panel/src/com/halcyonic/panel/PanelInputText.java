package com.halcyonic.panel;

/** Canonical codepoint limit shared by the actual Android editors and plain JVM regression. */
final class PanelInputText {
    private PanelInputText() {}
    static int acceptedEnd(CharSequence source, int start, int end, CharSequence destination, int replaceStart, int replaceEnd, int maximum) {
        int used = Character.codePointCount(destination, 0, replaceStart) + Character.codePointCount(destination, replaceEnd, destination.length());
        int remaining = Math.max(0, maximum - used), cursor = start;
        while (cursor < end && remaining > 0) {
            char first = source.charAt(cursor++);
            if (Character.isHighSurrogate(first) && cursor < end && Character.isLowSurrogate(source.charAt(cursor))) cursor++;
            remaining--;
        }
        return cursor;
    }
}
