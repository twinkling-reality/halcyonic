package com.halcyonic.glance;

/**
 * Text Halcyonic did not write (task titles, project names), shown as one line of exactly what it
 * says: the client core's LabelText.Plain rule, ported. White space that holds a line break, a tab
 * or anything but spaces collapses to one space; spaces stay as written; none at either end. Every
 * other control character, format character, default ignorable code point, unpaired surrogate and
 * Private Use Area character shows as its code point, such as ‹U+202E›. The ranges are LabelText's,
 * from Unicode 17.0; a test on the Mac holds the two equal on the same cases.
 */
public final class GlanceText {
    private static final int[] WHITE_SPACE = {
        0x0009, 0x000D,
        0x0020, 0x0020,
        0x0085, 0x0085,
        0x00A0, 0x00A0,
        0x1680, 0x1680,
        0x2000, 0x200A,
        0x2028, 0x2029,
        0x202F, 0x202F,
        0x205F, 0x205F,
        0x3000, 0x3000,
    };

    private static final int[] SHOWN_BY_CODE = {
        0x0000, 0x0008,
        0x000E, 0x001F,
        0x007F, 0x0084,
        0x0086, 0x009F,
        0x00AD, 0x00AD,
        0x034F, 0x034F,
        0x0600, 0x0605,
        0x061C, 0x061C,
        0x06DD, 0x06DD,
        0x070F, 0x070F,
        0x0890, 0x0891,
        0x08E2, 0x08E2,
        0x115F, 0x1160,
        0x17B4, 0x17B5,
        0x180B, 0x180F,
        0x200B, 0x200F,
        0x202A, 0x202E,
        0x2060, 0x206F,
        0x3164, 0x3164,
        0xFDD0, 0xFDEF,
        0xFE00, 0xFE0F,
        0xFEFF, 0xFEFF,
        0xFFA0, 0xFFA0,
        0xFFF0, 0xFFFB,
        0xFFFE, 0xFFFF,
        0x110BD, 0x110BD,
        0x110CD, 0x110CD,
        0x13430, 0x1343F,
        0x1BCA0, 0x1BCA3,
        0x1D173, 0x1D17A,
        0x1FFFE, 0x1FFFF,
        0x2FFFE, 0x2FFFF,
        0x3FFFE, 0x3FFFF,
        0x4FFFE, 0x4FFFF,
        0x5FFFE, 0x5FFFF,
        0x6FFFE, 0x6FFFF,
        0x7FFFE, 0x7FFFF,
        0x8FFFE, 0x8FFFF,
        0x9FFFE, 0x9FFFF,
        0xAFFFE, 0xAFFFF,
        0xBFFFE, 0xBFFFF,
        0xCFFFE, 0xCFFFF,
        0xDFFFE, 0xDFFFF,
        0xE0000, 0xE0FFF,
        0xEFFFE, 0xEFFFF,
    };

    private static final int[] PRIVATE_USE = {
        0xE000, 0xF8FF,
        0xF0000, 0x10FFFF,
    };

    private GlanceText() {}

    /** One line of exactly what <code>text</code> says; empty for null. */
    public static String plain(String text) {
        if (text == null || text.isEmpty()) return "";
        StringBuilder result = new StringBuilder(text.length());
        int spaces = 0;
        boolean collapse = false;
        for (int index = 0; index < text.length(); index++) {
            char unit = text.charAt(index);
            boolean paired = Character.isHighSurrogate(unit) && index + 1 < text.length()
                && Character.isLowSurrogate(text.charAt(index + 1));
            int codePoint = paired ? Character.toCodePoint(unit, text.charAt(index + 1)) : unit;
            if (in(WHITE_SPACE, codePoint)) {
                if (result.length() == 0) continue;
                spaces++;
                collapse |= unit != ' ';
                continue;
            }
            if (spaces > 0) {
                for (int each = 0; each < (collapse ? 1 : spaces); each++) result.append(' ');
            }
            spaces = 0;
            collapse = false;
            if (in(SHOWN_BY_CODE, codePoint) || in(PRIVATE_USE, codePoint) || (!paired && Character.isSurrogate(unit))) {
                result.append("‹U+").append(String.format(java.util.Locale.ROOT, "%04X", codePoint)).append('›');
            } else {
                result.append(text, index, index + (paired ? 2 : 1));
            }
            if (paired) index++;
        }
        return result.toString();
    }

    /**
     * <code>plain</code> text of at most <code>most</code> characters as shown, ending in an
     * ellipsis when cut. It is built a code point at a time, so a character shown by its code is
     * never shown half, and a code that would pass the limit is left out whole.
     */
    public static String cut(String text, int most) {
        String plain = plain(text);
        if (plain.length() <= most) return plain;
        StringBuilder kept = new StringBuilder();
        int end = 0;
        while (end < text.length()) {
            int next = end + Character.charCount(text.codePointAt(end));
            String shown = plain(text.substring(0, next));
            if (shown.length() > most - 1) break;
            kept.setLength(0);
            kept.append(shown);
            end = next;
        }
        return kept + "…";
    }

    private static boolean in(int[] ranges, int codePoint) {
        for (int index = 0; index < ranges.length; index += 2) {
            if (codePoint < ranges[index]) return false;
            if (codePoint <= ranges[index + 1]) return true;
        }
        return false;
    }
}
