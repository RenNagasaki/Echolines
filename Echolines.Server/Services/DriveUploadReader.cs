using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Microsoft.AspNetCore.Components.Forms;

namespace Echolines.Server.Services;

/// <summary>The buffered contents of one upload, valid until disposed.</summary>
public sealed class DriveUploadBundle(IReadOnlyList<DriveImportItem> items, IReadOnlyList<MemoryStream> buffers)
    : IAsyncDisposable
{
    public IReadOnlyList<DriveImportItem> Items { get; } = items;

    public async ValueTask DisposeAsync()
    {
        foreach (var buffer in buffers)
            await buffer.DisposeAsync();
    }
}

/// <summary>
/// Turns what the operator picked in the file dialog — many <c>.json</c> files, or a <c>.zip</c> of
/// them — into something <see cref="IDriveImportService"/> can read. Lives here rather than in the
/// page so the size limits and their justification sit in one reviewable place.
/// </summary>
public static class DriveUploadReader
{
    public const int MaxFiles = 20_000;

    /// <summary>One serialised <c>VoiceLine</c>. Real ones are a few hundred bytes.</summary>
    public const long MaxFileBytes = 256 * 1024;

    /// <summary>
    /// The whole Drive stock as one archive. Hundreds of thousands of small JSON files compress hard,
    /// so this is generous rather than necessary — but a limit that forces the operator to split the
    /// archive by hand would just get worked around.
    /// </summary>
    public const long MaxZipBytes = 64L * 1024 * 1024;

    public static async Task<DriveUploadBundle> ReadAsync(
        IEnumerable<IBrowserFile> files,
        CancellationToken ct = default)
    {
        var items = new List<DriveImportItem>();
        var buffers = new List<MemoryStream>();

        try
        {
            foreach (var file in files)
            {
                if (file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    await ReadArchiveAsync(file, items, buffers, ct);
                }
                else
                {
                    var content = await BufferAsync(file.OpenReadStream(MaxFileBytes, ct), buffers, ct);
                    items.Add(new DriveImportItem(file.Name, content));
                }
            }
        }
        catch
        {
            foreach (var buffer in buffers)
                await buffer.DisposeAsync();
            throw;
        }

        return new DriveUploadBundle(items, buffers);
    }

    [SuppressMessage("Major Code Smell", "S5693:Limit the content length of HTTP requests",
        Justification =
            "MaxZipBytes is that limit and it is enforced by OpenReadStream. The upload sits behind the " +
            "single operator login, is used once to migrate the legacy Google Drive stock, and by its " +
            "nature carries an archive of the whole corpus. The anonymous, internet-facing write path is " +
            "/v1/lines, which caps batches at 200 lines and is separately rate limited.")]
    private static async Task ReadArchiveAsync(
        IBrowserFile file,
        List<DriveImportItem> items,
        List<MemoryStream> buffers,
        CancellationToken ct)
    {
        // Buffered rather than streamed: ZipArchive needs to seek, and the browser stream cannot.
        var archiveBuffer = await BufferAsync(file.OpenReadStream(MaxZipBytes, ct), buffers, ct);

        using var archive = new ZipArchive(archiveBuffer, ZipArchiveMode.Read, leaveOpen: true);

        foreach (var entry in archive.Entries)
        {
            if (entry.Length == 0 || !entry.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;

            await using var entryStream = await entry.OpenAsync(ct);
            items.Add(new DriveImportItem(entry.Name, await BufferAsync(entryStream, buffers, ct)));
        }
    }

    private static async Task<MemoryStream> BufferAsync(Stream source, List<MemoryStream> buffers, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        buffers.Add(buffer);

        await using (source)
            await source.CopyToAsync(buffer, ct);

        buffer.Position = 0;
        return buffer;
    }
}
