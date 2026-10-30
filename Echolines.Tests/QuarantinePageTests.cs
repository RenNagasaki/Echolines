using AngleSharp.Dom;
using Bunit;
using Echolines.Server.Components.Pages;
using Echolines.Server.Data.Entities;
using Echolines.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Echolines.Tests;

/// <summary>
/// The quarantine page driven the way an operator drives it: tick a box, press a button.
/// </summary>
/// <remarks>
/// Written after the same blind spot bit twice. The endpoints were covered, the review service was
/// covered, and the one thing nobody exercised — the actual interaction — was where the defect sat.
/// </remarks>
public class QuarantinePageTests : TestContext
{
    private sealed class FakeReview : ILineReviewService
    {
        public List<PendingLine> Entries { get; } =
        [
            new() { Id = 1, Lang = "en", NpcName = "Alphinaud", Text = "One.", TextHash = "one" },
            new() { Id = 2, Lang = "de", NpcName = "Alisaie", Text = "Two.", TextHash = "two" },
        ];

        public List<int> Approved { get; } = [];
        public List<int> Rejected { get; } = [];
        public List<PendingFilter> ApprovedAll { get; } = [];

        public Task<Page<PendingLine>> QueryPendingAsync(PendingFilter filter, CancellationToken ct = default) =>
            Task.FromResult(new Page<PendingLine>(Entries, Entries.Count, 0, 50));

        public Task<Page<Line>> QueryLinesAsync(LineFilter filter, CancellationToken ct = default) =>
            Task.FromResult(new Page<Line>([], 0, 0, 50));

        public Task<InsightCounts> GetCountsAsync(CancellationToken ct = default) =>
            Task.FromResult(new InsightCounts(0, 0, 0, 0, 0, 0, 0));

        public Task<int> ApproveAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
        {
            Approved.AddRange(ids);
            return Task.FromResult(ids.Count);
        }

        public Task<int> ApproveAllAsync(PendingFilter filter, Action<int>? progress = null, CancellationToken ct = default)
        {
            ApprovedAll.Add(filter);
            progress?.Invoke(Entries.Count);
            return Task.FromResult(Entries.Count);
        }

        public Task<int> RejectAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
        {
            Rejected.AddRange(ids);
            return Task.FromResult(ids.Count);
        }
    }

    private FakeReview Arrange()
    {
        var review = new FakeReview();
        Services.AddSingleton<ILineReviewService>(review);
        return review;
    }

    private static IElement[] RowBoxes(IRenderedComponent<Quarantine> page) =>
        [.. page.FindAll("tbody input[type=checkbox]")];

    private static IElement HeaderBox(IRenderedComponent<Quarantine> page) =>
        page.Find("thead input[type=checkbox]");

    private static IElement Button(IRenderedComponent<Quarantine> page, string label) =>
        page.FindAll("button").First(b => b.TextContent.Trim() == label);

    private static bool HasButton(IRenderedComponent<Quarantine> page, string label) =>
        page.FindAll("button").Any(b => b.TextContent.Trim() == label);

    [Fact]
    public void Ticking_one_row_enables_the_action_buttons()
    {
        Arrange();
        var page = Render<Quarantine>();

        Assert.True(Button(page, "Release").HasAttribute("disabled"));

        RowBoxes(page)[0].Change(true);

        Assert.False(Button(page, "Release").HasAttribute("disabled"));
        Assert.False(Button(page, "Turn down").HasAttribute("disabled"));
        Assert.Contains("1 selected", page.Markup);
    }

    [Fact]
    public void Unticking_the_row_again_disables_them()
    {
        Arrange();
        var page = Render<Quarantine>();

        RowBoxes(page)[0].Change(true);
        RowBoxes(page)[0].Change(false);

        Assert.True(Button(page, "Release").HasAttribute("disabled"));
        Assert.Contains("0 selected", page.Markup);
    }

    [Fact]
    public void The_header_box_selects_and_clears_every_row_on_the_page()
    {
        Arrange();
        var page = Render<Quarantine>();

        HeaderBox(page).Change(true);
        Assert.Contains("2 selected", page.Markup);

        HeaderBox(page).Change(false);
        Assert.Contains("0 selected", page.Markup);
    }

    [Fact]
    public void Release_passes_exactly_the_ticked_entries_to_the_service()
    {
        var review = Arrange();
        var page = Render<Quarantine>();

        RowBoxes(page)[1].Change(true);
        Button(page, "Release").Click();

        Assert.Equal([2], review.Approved);
        Assert.Empty(review.Rejected);
    }

    [Fact]
    public void Turn_down_passes_exactly_the_ticked_entries_and_clears_the_selection()
    {
        var review = Arrange();
        var page = Render<Quarantine>();

        HeaderBox(page).Change(true);
        Button(page, "Turn down").Click();

        Assert.Equal([1, 2], review.Rejected);
        Assert.Contains("0 selected", page.Markup);
        Assert.True(Button(page, "Release").HasAttribute("disabled"));
    }

    [Fact]
    public void A_ticked_box_stays_ticked_in_the_markup_after_the_re_render()
    {
        // The visible state has to survive the round trip, or the operator cannot tell what they
        // are about to release.
        Arrange();
        var page = Render<Quarantine>();

        RowBoxes(page)[0].Change(true);

        Assert.True(RowBoxes(page)[0].HasAttribute("checked"));
        Assert.False(RowBoxes(page)[1].HasAttribute("checked"));
    }

    [Fact]
    public void Release_all_asks_first_and_does_nothing_until_confirmed()
    {
        var review = Arrange();
        var page = Render<Quarantine>();

        Button(page, "Release all 2").Click();

        Assert.Empty(review.ApprovedAll);
        Assert.Contains("Release all 2 entries in this filter?", page.Markup);

        Button(page, "Yes, release all").Click();

        var filter = Assert.Single(review.ApprovedAll);
        Assert.Equal(PendingState.Pending, filter.State);
        Assert.Contains("2 released.", page.Markup);
        Assert.False(HasButton(page, "Yes, release all"));
    }

    [Fact]
    public void Cancelling_release_all_releases_nothing()
    {
        var review = Arrange();
        var page = Render<Quarantine>();

        Button(page, "Release all 2").Click();
        Button(page, "Cancel").Click();

        Assert.Empty(review.ApprovedAll);
        Assert.True(HasButton(page, "Release all 2"));
    }

    [Fact]
    public void A_filter_change_withdraws_a_pending_release_all_confirmation()
    {
        // The count in the question belongs to the old filter; confirming it under a new one would
        // release a set the operator never saw the size of.
        var review = Arrange();
        var page = Render<Quarantine>();

        Button(page, "Release all 2").Click();
        page.Find("input[placeholder=NPC]").Input("Alisaie");

        Assert.False(HasButton(page, "Yes, release all"));
        Assert.Empty(review.ApprovedAll);
    }

    [Fact]
    public void Release_all_passes_the_current_filter_to_the_service()
    {
        var review = Arrange();
        var page = Render<Quarantine>();

        page.Find("input[placeholder=NPC]").Input("Alisaie");
        Button(page, "Release all 2").Click();
        Button(page, "Yes, release all").Click();

        Assert.Equal("Alisaie", Assert.Single(review.ApprovedAll).Npc);
    }
}
