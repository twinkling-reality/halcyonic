package com.halcyonic.panel;

import java.math.BigDecimal;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** Strict JSON for the actual Android consumer and JVM parity tests: no coercion or duplicate keys. */
final class PanelJson {
    private final String source;
    private int cursor;
    private int values;

    private PanelJson(String source) { this.source = source; }

    static Object parse(String source) {
        if (source == null || source.length() > 8 * 1024 * 1024) throw invalid();
        PanelJson parser = new PanelJson(source);
        Object value = parser.value(0);
        parser.space();
        if (parser.cursor != source.length()) throw invalid();
        return value;
    }

    private Object value(int depth) {
        if (depth > 32 || ++values > 200000) throw invalid();
        space();
        if (cursor >= source.length()) throw invalid();
        char c = source.charAt(cursor);
        if (c == '"') return string();
        if (c == '{') {
            cursor++;
            Map<String, Object> object = new LinkedHashMap<>();
            space();
            if (take('}')) return object;
            do {
                space();
                if (cursor >= source.length() || source.charAt(cursor) != '"') throw invalid();
                String key = string();
                space();
                if (!take(':') || object.containsKey(key)) throw invalid();
                object.put(key, value(depth + 1));
                space();
            } while (take(','));
            if (!take('}')) throw invalid();
            return object;
        }
        if (c == '[') {
            cursor++;
            List<Object> array = new ArrayList<>();
            space();
            if (take(']')) return array;
            do { array.add(value(depth + 1)); space(); } while (take(','));
            if (!take(']')) throw invalid();
            return array;
        }
        for (String literal : new String[] {"null", "true", "false"}) {
            if (source.startsWith(literal, cursor)) {
                cursor += literal.length();
                return literal.equals("null") ? null : Boolean.valueOf(literal);
            }
        }
        int start = cursor;
        take('-');
        if (!take('0')) {
            if (cursor >= source.length() || source.charAt(cursor) < '1' || source.charAt(cursor) > '9') throw invalid();
            while (digit()) cursor++;
        }
        boolean integer = true;
        if (take('.')) {
            integer = false;
            if (!digit()) throw invalid();
            while (digit()) cursor++;
        }
        if (take('e') || take('E')) {
            integer = false;
            if (!take('+')) take('-');
            if (!digit()) throw invalid();
            while (digit()) cursor++;
        }
        try {
            String number = source.substring(start, cursor);
            if (integer) return Long.valueOf(number);
            return new BigDecimal(number);
        } catch (NumberFormatException error) { throw invalid(); }
    }

    private String string() {
        cursor++;
        StringBuilder result = new StringBuilder();
        while (cursor < source.length()) {
            char c = source.charAt(cursor++);
            if (c == '"') return result.toString();
            if (c < 32) throw invalid();
            if (c == '\\') {
                if (cursor >= source.length()) throw invalid();
                char escaped = source.charAt(cursor++);
                switch (escaped) {
                    case '"': case '\\': case '/': c = escaped; break;
                    case 'b': c = '\b'; break;
                    case 'f': c = '\f'; break;
                    case 'n': c = '\n'; break;
                    case 'r': c = '\r'; break;
                    case 't': c = '\t'; break;
                    case 'u':
                        if (cursor + 4 > source.length()) throw invalid();
                        String hex = source.substring(cursor, cursor + 4);
                        if (!hex.matches("[0-9a-fA-F]{4}")) throw invalid();
                        c = (char) Integer.parseInt(hex, 16); cursor += 4; break;
                    default: throw invalid();
                }
            }
            result.append(c);
        }
        throw invalid();
    }

    private boolean digit() { return cursor < source.length() && source.charAt(cursor) >= '0' && source.charAt(cursor) <= '9'; }
    private boolean take(char c) { if (cursor < source.length() && source.charAt(cursor) == c) { cursor++; return true; } return false; }
    private void space() { while (cursor < source.length() && " \r\n\t".indexOf(source.charAt(cursor)) >= 0) cursor++; }
    static IllegalArgumentException invalid() { return new IllegalArgumentException("Unsupported data."); }

    @SuppressWarnings("unchecked") static Map<String, Object> object(Object value) {
        if (!(value instanceof Map)) throw invalid();
        return (Map<String, Object>) value;
    }
    @SuppressWarnings("unchecked") static List<Object> array(Object value) {
        if (!(value instanceof List)) throw invalid();
        return (List<Object>) value;
    }
    static String text(Object value) { if (!(value instanceof String)) throw invalid(); return (String) value; }
    static String nullableText(Object value) { return value == null ? null : text(value); }
    static boolean bool(Object value) { if (!(value instanceof Boolean)) throw invalid(); return (Boolean) value; }
    static long integer(Object value, long min, long max) {
        if (!(value instanceof Long) || (Long) value < min || (Long) value > max) throw invalid();
        return (Long) value;
    }
    static Map<String, Object> map(Object... pairs) {
        Map<String, Object> value = new LinkedHashMap<>();
        for (int i = 0; i < pairs.length; i += 2) value.put((String) pairs[i], pairs[i + 1]);
        return value;
    }
    static void keys(Map<String, Object> value, String... names) {
        if (value.size() != names.length) throw invalid();
        for (String name : names) if (!value.containsKey(name)) throw invalid();
    }
    static String encode(Object value) {
        if (value == null) return "null";
        if (value instanceof String) {
            StringBuilder out = new StringBuilder("\"");
            for (char c : ((String) value).toCharArray()) {
                switch (c) {
                    case '"': out.append("\\\""); break;
                    case '\\': out.append("\\\\"); break;
                    case '\b': out.append("\\b"); break;
                    case '\f': out.append("\\f"); break;
                    case '\n': out.append("\\n"); break;
                    case '\r': out.append("\\r"); break;
                    case '\t': out.append("\\t"); break;
                    default:
                        if (c < 32 || Character.isSurrogate(c)) out.append(String.format(java.util.Locale.ROOT, "\\u%04x", (int) c));
                        else out.append(c);
                }
            }
            return out.append('"').toString();
        }
        if (value instanceof Boolean || value instanceof Number) return value.toString();
        StringBuilder out = new StringBuilder();
        if (value instanceof Map) {
            out.append('{');
            for (Map.Entry<String, Object> entry : object(value).entrySet()) {
                if (out.length() > 1) out.append(',');
                out.append(encode(entry.getKey())).append(':').append(encode(entry.getValue()));
            }
            return out.append('}').toString();
        }
        out.append('[');
        for (Object item : array(value)) { if (out.length() > 1) out.append(','); out.append(encode(item)); }
        return out.append(']').toString();
    }
}
