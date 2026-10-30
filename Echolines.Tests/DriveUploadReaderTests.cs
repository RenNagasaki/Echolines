using System.IO.Compression;
using System.Text;
using Echolines.Server.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace Echolines.Tests;

/// <summary>
/// Turning the operator's file selection into importable items. Worth its own tests because the zip
/// path is the one the operator will actually use — nobody hand-picks 200 000 files in a dialog.
/// </summary>
public class DriveUploadReaderTests
{
    private sealed class FakeBrowserFile(string name, byte[] content) : IBrowserFile
    {
        public string Name { get; } = name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => content.Length;
        public string ContentType => "application/octet-stream";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            if (content.Length > maxAllowedSize)
                throw new IOException($"file exceeds {maxAllowedSize} bytes");

            return new MemoryStream(content);
        }
    }

    private static FakeBrowserFile Json(string name, string body) =>
        new(name, Encoding.UTF8.GetBytes(body));

    private static FakeBrowserFile Zip(string name, params (string Entry, string Body)[] entries)
    {
        using var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, body) in entries)
            {
                var entry = archive.CreateEntry(entryName);
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(body));
            }
        }

        return new FakeBrowserFile(name, buffer.ToArray());
    }

    private static async Task<string> ReadBack(DriveImportItem item)
    {
        using var reader = new StreamReader(item.Content, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task Loose_json_files_come_through_with_name_and_content()
    {
        await using var bundle = await DriveUploadReader.ReadAsync(
            [Json("a.json", "{\"Text\":\"one\"}"), Json("b.json", "{\"Text\":\"two\"}")]);

        Assert.Equal(2, bundle.Items.Count);
        Assert.Equal("a.json", bundle.Items[0].FileName);
        Assert.Equal("{\"Text\":\"one\"}", await ReadBack(bundle.Items[0]));
        Assert.Equal("{\"Text\":\"two\"}", await ReadBack(bundle.Items[1]));
    }

    [Fact]
    public async Task A_zip_is_unpacked_into_its_json_entries()
    {
        await using var bundle = await DriveUploadReader.ReadAsync(
            [Zip("stock.zip", ("a.json", "{\"Text\":\"one\"}"), ("b.json", "{\"Text\":\"two\"}"))]);

        Assert.Equal(2, bundle.Items.Count);
        Assert.Equal(["a.json", "b.json"], bundle.Items.Select(i => i.FileName).Order());
    }

    [Fact]
    public async Task Non_json_and_empty_entries_inside_a_zip_are_skipped()
    {
        // Drive exports drag along readme files and directory placeholders; they are not conflicts,
        // they are simply not lines, and reporting them would bury the real conflicts.
        await using var bundle = await DriveUploadReader.ReadAsync(
            [Zip("stock.zip", ("notes.txt", "ignore me"), ("empty.json", ""), ("a.json", "{\"Text\":\"one\"}"))]);

        Assert.Equal("a.json", Assert.Single(bundle.Items).FileName);
    }

    [Fact]
    public async Task A_mixed_selection_of_loose_files_and_a_zip_is_read_as_one_set()
    {
        await using var bundle = await DriveUploadReader.ReadAsync(
            [Json("loose.json", "{\"Text\":\"one\"}"), Zip("stock.zip", ("packed.json", "{\"Text\":\"two\"}"))]);

        Assert.Equal(["loose.json", "packed.json"], bundle.Items.Select(i => i.FileName).Order());
    }

    [Fact]
    public async Task An_oversized_file_fails_the_whole_read_rather_than_importing_half_of_it()
    {
        var tooBig = new FakeBrowserFile("huge.json", new byte[DriveUploadReader.MaxFileBytes + 1]);

        await Assert.ThrowsAsync<IOException>(async () =>
            await DriveUploadReader.ReadAsync([Json("a.json", "{}"), tooBig]));
    }

    [Fact]
    public async Task Disposing_the_bundle_releases_the_buffers()
    {
        var bundle = await DriveUploadReader.ReadAsync([Json("a.json", "{\"Text\":\"one\"}")]);
        var content = bundle.Items[0].Content;

        await bundle.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => content.ReadByte());
    }
}
