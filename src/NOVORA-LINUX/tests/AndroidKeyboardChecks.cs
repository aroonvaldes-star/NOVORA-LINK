using NOVORA.AndroidVideo;

internal static class AndroidKeyboardChecks
{
    public static void Run()
    {
        Check(NLAndroidKeyboardEditing.Move("a🙂b", 3, 3, 21, 0) == (1, 1), "Left must not split a surrogate pair.");
        Check(NLAndroidKeyboardEditing.Move("a🙂b", 1, 1, 22, 1) == (1, 3), "Shift-right must select a complete Unicode character.");
        Check(NLAndroidKeyboardEditing.Move("one two", 7, 7, 21, 0x1000) == (4, 4), "Ctrl-left must move to the prior word.");
        Check(NLAndroidKeyboardEditing.Move("ab\ncdef", 1, 1, 20, 0) == (4, 4), "Down must keep the text column.");
        Check(NLAndroidKeyboardEditing.Move("ab\ncdef", 5, 5, 122, 0) == (3, 3), "Home must find current line start.");
        Check(NLAndroidKeyboardEditing.Move("ab\ncdef", 5, 5, 123, 0x1000) == (7, 7), "Ctrl-End must find document end.");
        var deleted = NLAndroidKeyboardEditing.Insert("a🙂b", 3, 3, "\b");
        Check(deleted.Text == "ab" && deleted.Caret == 1, "Backspace must remove a complete Unicode character.");
        var inserted = NLAndroidKeyboardEditing.Insert("abcd", 1, 3, "é");
        Check(inserted.Text == "aéd" && inserted.Caret == 2, "Typing must replace selection and preserve the caret.");
        Console.WriteLine("Android keyboard caret, modifiers and Unicode checks passed.");
    }
    private static void Check(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
}
