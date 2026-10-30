using System.Text;
using Echolines.Server.Data;

namespace Echolines.Server.Services;

/// <summary>
/// Reproduces Echokraut's <c>TalkTextHelper.VoiceMessageToFileName</c> — the slug that is at once the
/// WAV file name, the local DB key, the <c>*.ekpack</c> entry name and this database's <c>text_hash</c>.
/// Because all four are the same string, central DB, package and local DB line up with no translation
/// layer, which is the single most important decision in the design.
/// </summary>
/// <remarks>
/// <para>
/// The name "hash" from the API is a misnomer inherited from the plugin: this is a deterministic,
/// lossy slug of the text, not a digest. That is a feature here — the server can <b>recompute</b> it
/// from the submitted text instead of trusting the client. A client that sent a wrong or invented
/// hash would otherwise split one line into arbitrarily many keys, which is poisoning by typo.
/// </para>
/// <para>
/// ⚠ The plugin calls <see cref="Path.GetInvalidFileNameChars"/>, which is <b>platform dependent</b>:
/// 41 characters on Windows, exactly two (<c>NUL</c> and <c>/</c>) on Linux. The plugin runs on Windows,
/// this server runs in a Linux container. Calling that API here would silently produce a different slug
/// for any text containing <c>: ? " * | &lt; &gt; \</c> — a colon is common in FFXIV dialogue. The Windows
/// set is therefore hardcoded below and must not be replaced with the framework call.
/// </para>
/// </remarks>
public static class LineKey
{
    /// <summary>The .NET Windows value of <c>Path.GetInvalidFileNameChars()</c>: control chars 0-31 plus these.</summary>
    private const string WindowsInvalidPunctuation = "\"<>|:*?\\/";

    /// <summary>Characters the plugin strips after the invalid-char split.</summary>
    private const string StrippedAfterSplit = " .!,-_";

    /// <summary>Builds the canonical key for an already player-name-tokenised text.</summary>
    public static string FromText(string text)
    {
        var sb = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            // Mirrors Split(invalidChars, RemoveEmptyEntries) + Join("") — both simply drop the char.
            if (c < 32 || WindowsInvalidPunctuation.Contains(c))
                continue;

            // The plugin lowercases the joined string, then removes these characters.
            if (StrippedAfterSplit.Contains(c))
                continue;

            sb.Append(char.ToLowerInvariant(c));

            if (sb.Length == LineLimits.MaxHashLength)
                break; // the plugin truncates at 120, so anything past it never reaches the key
        }

        return sb.ToString();
    }

    /// <summary>
    /// Strips a key down to letters and digits, for comparing a computed key against one that has been
    /// through a file system.
    /// </summary>
    /// <remarks>
    /// The legacy Google Drive names have had characters substituted along the way: an apostrophe the
    /// plugin kept ("Oh, you are Lyse's friend") appears as <c>_</c> in the stored file name. The two
    /// strings describe the same line and must compare equal, or the import throws away good material.
    /// Only punctuation is dropped, so this stays strict about the case it exists to catch — a body
    /// still holding a real character name differs in letters, not in punctuation.
    /// </remarks>
    public static string Normalise(string key)
    {
        var sb = new StringBuilder(key.Length);

        foreach (var c in key)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(c);
        }

        return sb.ToString();
    }
}
