namespace Echolines.Server.Data;

/// <summary>
/// Shape limits that need no stock to check against. The server holds no reference corpus, so these
/// plus the quarantine are the only things standing between an open write endpoint and arbitrary text.
/// </summary>
public static class LineLimits
{
    /// <summary>
    /// 120 is not a round number by accident: the plugin truncates its file-name slug at 120 characters
    /// (<c>TalkTextHelper.VoiceMessageToFileName</c>), and that slug is the key both sides share.
    /// </summary>
    public const int MaxHashLength = 120;

    /// <summary>Longest dialogue text accepted. Real FFXIV lines are far below this.</summary>
    public const int MaxTextLength = 4000;

    public const int MaxNpcNameLength = 128;

    /// <summary>The four game languages. Anything else is a client bug or a probe.</summary>
    public static readonly string[] Languages = ["en", "de", "fr", "ja"];

    public static bool IsKnownLanguage(string? lang) =>
        lang is not null && Array.IndexOf(Languages, lang) >= 0;
}
