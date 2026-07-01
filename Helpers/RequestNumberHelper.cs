namespace ApproveFlow.MVC.Helpers;

public static class RequestNumberHelper
{
    // Matches current format: REQ-YY-N+ (2-digit year)
    private static readonly System.Text.RegularExpressions.Regex _curFmt =
        new(@"^REQ-(\d{2})-(\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches legacy format: REQ-YYYY-N+ (4-digit year)
    private static readonly System.Text.RegularExpressions.Regex _legacyFmt =
        new(@"^REQ-(\d{4})-(\d+)$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Converts stored request numbers to the canonical display format REQ-YY-NNNNNN (6-digit sequence).
    /// Handles legacy format (REQ-YYYY-N+), current 5-digit format (REQ-YY-NNNNN),
    /// and the new 6-digit format (REQ-YY-NNNNNN) transparently.
    /// </summary>
    public static string Format(string? requestNumber)
    {
        if (string.IsNullOrEmpty(requestNumber)) return "";

        var m = _curFmt.Match(requestNumber);
        if (m.Success
            && int.TryParse(m.Groups[1].Value, out var yy)
            && int.TryParse(m.Groups[2].Value, out var seq))
            return $"REQ-{yy:D2}-{seq:D6}";

        var ml = _legacyFmt.Match(requestNumber);
        if (ml.Success
            && int.TryParse(ml.Groups[1].Value, out var year)
            && int.TryParse(ml.Groups[2].Value, out var seqL))
            return $"REQ-{year % 100:D2}-{seqL:D6}";

        return requestNumber;
    }
}
