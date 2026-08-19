using System.Linq;
using SyncStreamAPI.Helper.SponsorBlock;
using Xunit;

namespace SyncStreamAPI.Tests.Playback;

/// <summary>
///     Which SponsorBlock categories are skipped.
/// </summary>
/// <remarks>
///     This lived in each browser's own settings, and the skip is performed only by the host, whose position the
///     rest of the room then follows. So the choice was made by whoever happened to be host, out of preferences
///     they had set for themselves, and a viewer's own toggles changed nothing about what was skipped.
/// </remarks>
public class SponsorBlockCategoryTests
{
    [Fact]
    public void The_default_skips_sponsorship_only()
    {
        // The client used to default to every category except filler, which skipped intros, outros, greetings,
        // recaps and offtopic music. That is well past what SponsorBlock itself does, and in a room nobody except
        // the host could opt out of it.
        Assert.Equal(new[] { "sponsor", "selfpromo" }, SponsorBlockCategories.Default);

        Assert.DoesNotContain("intro", SponsorBlockCategories.Default);
        Assert.DoesNotContain("outro", SponsorBlockCategories.Default);
        Assert.DoesNotContain("hook", SponsorBlockCategories.Default);
        Assert.DoesNotContain("preview", SponsorBlockCategories.Default);
    }

    [Fact]
    public void Every_default_is_a_category_that_exists()
    {
        Assert.All(SponsorBlockCategories.Default, category => Assert.Contains(category, SponsorBlockCategories.All));
    }

    [Fact]
    public void A_category_that_does_not_exist_is_dropped()
    {
        // So an unknown value cannot be stored and then served to every client as though it meant something.
        var sanitised = SponsorBlockCategories.Sanitise(new[] { "sponsor", "not_a_category", "intro" });

        Assert.Equal(new[] { "sponsor", "intro" }, sanitised);
    }

    [Fact]
    public void Casing_and_padding_are_accepted()
    {
        var sanitised = SponsorBlockCategories.Sanitise(new[] { " Sponsor ", "SELFPROMO" });

        Assert.Equal(new[] { "sponsor", "selfpromo" }, sanitised);
    }

    [Fact]
    public void A_repeated_category_is_stored_once()
    {
        var sanitised = SponsorBlockCategories.Sanitise(new[] { "sponsor", "sponsor" });

        Assert.Single(sanitised);
    }

    [Fact]
    public void An_empty_choice_is_kept_as_skipping_nothing()
    {
        // Distinct from not having chosen: an administrator turning every category off means the room skips
        // nothing, and must not be quietly given the defaults back.
        Assert.Empty(SponsorBlockCategories.Sanitise(new string[0]));
    }

    [Fact]
    public void No_choice_at_all_falls_back_to_the_default()
    {
        Assert.Equal(SponsorBlockCategories.Default, SponsorBlockCategories.Sanitise(null));
    }

    [Fact]
    public void The_category_list_has_no_duplicates()
    {
        Assert.Equal(SponsorBlockCategories.All.Length, SponsorBlockCategories.All.Distinct().Count());
    }
}
