namespace NOVORA.Control;

public static class NLControlAndroidInput
{
    public static string GetEditableText(
        string? current,
        string? hint,
        bool isShowingHint)
    {
        current ??= string.Empty;

        if (isShowingHint ||
            (!string.IsNullOrEmpty(hint) &&
             string.Equals(current, hint, StringComparison.Ordinal)))
        {
            return string.Empty;
        }

        return current;
    }

    public static string EditText(string? current, int selectionStart, int selectionEnd, string input)
    {
        current ??= string.Empty;
        ArgumentNullException.ThrowIfNull(input);
        if (selectionStart < 0 || selectionEnd < 0)
            selectionStart = selectionEnd = current.Length;
        int start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, current.Length);
        int end = Math.Clamp(Math.Max(selectionStart, selectionEnd), start, current.Length);
        if (input == "\b")
        {
            if (start != end) return current.Remove(start, end - start);
            return start == 0 ? current : current.Remove(start - 1, 1);
        }
        if (input == "\u007f")
        {
            if (start != end) return current.Remove(start, end - start);
            return start >= current.Length ? current : current.Remove(start, 1);
        }
        return current[..start] + input + current[end..];
    }
}
