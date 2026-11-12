using System.Globalization;

namespace NOVORA.AndroidVideo;

/// <summary>Pure UTF-16 selection rules shared by accessibility navigation and its platform-independent checks.</summary>
internal static class NLAndroidKeyboardEditing
{
    public static (int Start, int End) Move(string text, int start, int end, uint keycode, uint metaState)
    {
        start = Math.Clamp(start < 0 ? text.Length : start, 0, text.Length);
        end = Math.Clamp(end < 0 ? text.Length : end, 0, text.Length);
        bool shift = (metaState & 1) != 0, control = (metaState & 0x1000) != 0;
        int caret = end;
        if (!shift && start != end && keycode is 21 or 22)
            return keycode == 21 ? (Math.Min(start, end), Math.Min(start, end)) : (Math.Max(start, end), Math.Max(start, end));
        int lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;
        int lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0) lineEnd = text.Length;
        int target = keycode switch
        {
            21 => control ? PreviousWord(text, caret) : Previous(text, caret),
            22 => control ? NextWord(text, caret) : Next(text, caret),
            19 => PreviousLine(text, caret, lineStart),
            20 => NextLine(text, caret, lineStart, lineEnd),
            122 => control ? 0 : lineStart,
            123 => control ? text.Length : lineEnd,
            92 => 0,
            93 => text.Length,
            _ => caret
        };
        return shift ? (start, target) : (target, target);
    }

    public static (string Text, int Caret) Insert(string text, int start, int end, string input)
    {
        if (start < 0 || end < 0) start = end = text.Length;
        int first = Math.Clamp(Math.Min(start, end), 0, text.Length), last = Math.Clamp(Math.Max(start, end), 0, text.Length);
        if (input == "\b")
        {
            if (first == last) first = Previous(text, first);
            return (text.Remove(first, last - first), first);
        }
        if (input == "\u007f")
        {
            if (first == last) last = Next(text, last);
            return (text.Remove(first, last - first), first);
        }
        return (text[..first] + input + text[last..], first + input.Length);
    }

    private static int Previous(string text, int caret)
    {
        int previous = 0;
        foreach (int boundary in StringInfo.ParseCombiningCharacters(text))
        {
            if (boundary >= caret) break;
            previous = boundary;
        }
        return previous;
    }
    private static int Next(string text, int caret)
    {
        foreach (int boundary in StringInfo.ParseCombiningCharacters(text)) if (boundary > caret) return boundary;
        return text.Length;
    }
    private static int PreviousWord(string text, int caret)
    {
        while (caret > 0 && char.IsWhiteSpace(text[Previous(text, caret)])) caret = Previous(text, caret);
        while (caret > 0 && !char.IsWhiteSpace(text[Previous(text, caret)])) caret = Previous(text, caret);
        return caret;
    }
    private static int NextWord(string text, int caret)
    {
        while (caret < text.Length && !char.IsWhiteSpace(text[caret])) caret = Next(text, caret);
        while (caret < text.Length && char.IsWhiteSpace(text[caret])) caret = Next(text, caret);
        return caret;
    }
    private static int PreviousLine(string text, int caret, int lineStart)
    {
        if (lineStart == 0) return 0;
        int end = lineStart - 1;
        int start = end == 0 ? 0 : text.LastIndexOf('\n', end - 1) + 1;
        return Snap(text, Math.Min(end, start + caret - lineStart));
    }
    private static int NextLine(string text, int caret, int lineStart, int lineEnd)
    {
        if (lineEnd == text.Length) return text.Length;
        int nextStart = lineEnd + 1, end = text.IndexOf('\n', nextStart);
        if (end < 0) end = text.Length;
        return Snap(text, Math.Min(end, nextStart + caret - lineStart));
    }
    private static int Snap(string text, int caret)
    {
        if (caret == text.Length) return caret;
        return Previous(text, caret + 1);
    }
}
