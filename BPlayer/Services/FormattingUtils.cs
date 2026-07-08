using System;

namespace BPlayer.Services;

public static class FormattingUtils
{
    public static string FormatTime(TimeSpan ts) =>
        ts.Hours > 0
            ? $"{ts.Hours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
}
