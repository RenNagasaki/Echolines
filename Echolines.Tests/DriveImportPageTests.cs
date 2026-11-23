using AngleSharp.Dom;
using Bunit;
using Echolines.Server.Components.Pages;
using Echolines.Server.Services;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace Echolines.Tests;

/// <summary>
/// The Drive import page, driven the way an operator drives it: pick files, then decide.
/// </summary>
public class DriveImportPageTests : TestContext
{
    private sealed class FakeImporter : IDriveImportService
    {
        public int Calls { get; private set; }
        public List<string> LastFileNames { get; } = [];

        public Task<DriveImportReport> ImportAsync(
            IReadOnlyList<DriveImportItem> files,
            CancellationToken ct = default)
        {
            Calls++;
            LastFileNames.Clear();
            LastFileNames.AddRange(files.Select(f => f.FileName));

            return Task.FromResult(new DriveImportReport { Imported = files.Count });
        }
    }

    private FakeImporter Arrange()
    {
        var importer = new FakeImporter();
        Services.AddSingleton<IDriveImportService>(importer);
        return importer;
    }

    private static IElement ImportButton(IRenderedComponent<DriveImport> page) =>
        page.FindAll("button").First(b => b.TextContent.Contains("Import"));

    private static void Pick(IRenderedComponent<DriveImport> page, params string[] names) =>
        page.FindComponent<InputFile>().UploadFiles(
            [.. names.Select(n => InputFileContent.CreateFromText("{\"Text\":\"hi\"}", n))]);

    [Fact]
    public void There_is_an_import_button_and_it_starts_disabled()
    {
        Arrange();
        var page = Render<DriveImport>();

        Assert.True(ImportButton(page).HasAttribute("disabled"));
    }

    [Fact]
    public void Picking_files_does_not_import_anything_yet()
    {
        // The whole point of the button: this writes into the public table, bypassing quarantine, so
        // there has to be a moment to look at the selection and back out.
        var importer = Arrange();
        var page = Render<DriveImport>();

        Pick(page, "a.json", "b.json");

        Assert.Equal(0, importer.Calls);
        Assert.False(ImportButton(page).HasAttribute("disabled"));
        Assert.Contains("Import 2 file(s)", page.Markup);
        Assert.Contains("a.json", page.Markup);
    }

    [Fact]
    public void Pressing_the_button_imports_exactly_the_picked_files()
    {
        var importer = Arrange();
        var page = Render<DriveImport>();

        Pick(page, "a.json", "b.json");
        ImportButton(page).Click();

        Assert.Equal(1, importer.Calls);
        Assert.Equal(["a.json", "b.json"], importer.LastFileNames);
        Assert.Contains("imported", page.Markup);
    }

    [Fact]
    public void The_selection_can_be_dropped_again_without_importing()
    {
        var importer = Arrange();
        var page = Render<DriveImport>();

        Pick(page, "a.json");
        page.FindAll("button").First(b => b.TextContent.Trim() == "Clear").Click();

        Assert.Equal(0, importer.Calls);
        Assert.True(ImportButton(page).HasAttribute("disabled"));
    }

    [Fact]
    public void After_a_successful_import_the_button_goes_quiet_again()
    {
        // Otherwise a second, accidental click imports the same stock twice.
        Arrange();
        var page = Render<DriveImport>();

        Pick(page, "a.json");
        ImportButton(page).Click();

        Assert.True(ImportButton(page).HasAttribute("disabled"));
    }
}
