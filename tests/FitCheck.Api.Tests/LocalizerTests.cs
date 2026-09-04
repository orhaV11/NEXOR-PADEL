using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

public class LocalizerTests
{
    [Theory]
    [InlineData("he-IL", "he")]
    [InlineData("he", "he")]
    [InlineData("HE_il", "he")]
    [InlineData("fr-FR", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    [InlineData("   ", "en")]
    public void Match_uses_the_language_subtag_and_falls_back_to_english(string? tag, string expected)
    {
        Assert.Equal(expected, Localizer.Match(tag));
    }

    [Theory]
    [InlineData("he-IL,he;q=0.9,en-US;q=0.8", "he")]
    [InlineData("fr-FR,fr;q=0.9", "en")]
    [InlineData("fr-FR, he;q=0.5", "he")]
    [InlineData("en;q=0.3, he;q=0.9", "he")]
    [InlineData("he;q=0", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    [InlineData("*", "en")]
    public void Accept_language_matching_honours_quality_order(string? header, string expected)
    {
        Assert.Equal(expected, Localizer.MatchAcceptLanguage(header));
    }

    [Fact]
    public void Messages_exist_in_every_locale_and_fall_back_to_english()
    {
        var localizer = new Localizer();
        Assert.Equal("Use a JPEG, PNG or WebP photo.", localizer.Get("en", "error.image_format"));
        Assert.NotEqual(localizer.Get("en", "error.image_format"), localizer.Get("he", "error.image_format"));
        Assert.Equal(localizer.Get("en", "error.image_format"), localizer.Get("fr", "error.image_format"));
        Assert.Equal("missing.key", localizer.Get("en", "missing.key"));
    }

    [Fact]
    public void Messages_format_arguments()
    {
        var localizer = new Localizer();
        Assert.Contains("20 checks", localizer.Get("en", "error.rate_limited", 20));
        Assert.Contains("20", localizer.Get("he", "error.rate_limited", 20));
    }

    [Fact]
    public void Language_names_are_english_for_the_prompt()
    {
        Assert.Equal("Hebrew", Localizer.LanguageName("he"));
        Assert.Equal("English", Localizer.LanguageName("en"));
        Assert.Equal("English", Localizer.LanguageName("xx"));
    }
}
