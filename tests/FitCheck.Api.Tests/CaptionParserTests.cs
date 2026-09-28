using FitCheck.Api.Services;

namespace FitCheck.Api.Tests;

public class CaptionParserTests
{
    [Fact]
    public void Tags_are_lower_cased_distinct_and_kept_in_order_of_appearance()
    {
        var tags = CaptionParser.Tags("Sun out #Summer, #beach #SUMMER #Beach_Day2 #beach.");
        Assert.Equal(new[] { "summer", "beach", "beach_day2" }, tags);
    }

    [Fact]
    public void Only_the_first_five_distinct_tags_count()
    {
        var tags = CaptionParser.Tags("#t1 #t2 #t1 #t3 #t4 #t5 #t6 #t7");
        Assert.Equal(new[] { "t1", "t2", "t3", "t4", "t5" }, tags);
    }

    [Fact]
    public void Tags_come_in_any_script()
    {
        Assert.Equal(new[] { "קיץ", "חוף_ים" }, CaptionParser.Tags("לוק של #קיץ בתל אביב #חוף_ים"));
        Assert.Equal(new[] { "été", "日本", "2024" }, CaptionParser.Tags("#Été #日本 #ÉTÉ #2024"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void A_tag_is_two_to_thirty_characters_or_it_is_plain_text(int length, bool isTag)
    {
        var word = new string('x', length);
        var tags = CaptionParser.Tags($"look #{word} done");
        if (isTag)
        {
            Assert.Equal(new[] { word }, tags);
        }
        else
        {
            Assert.Empty(tags);
        }
    }

    [Fact]
    public void A_marker_glued_to_the_previous_word_is_not_a_marker()
    {
        const string caption = "write to someone@example.com, see item#42, a#b or c@d.e and #summer@brand";
        Assert.Equal(new[] { "summer" }, CaptionParser.Tags(caption));
        Assert.Empty(CaptionParser.Mentions(caption));
    }

    [Fact]
    public void Punctuation_and_the_start_of_the_text_separate_markers()
    {
        Assert.Equal(new[] { "party", "date", "office" }, CaptionParser.Tags("(#party) #date,#office! # #"));
        Assert.Equal(new[] { "brand", "shop", "x_y" }, CaptionParser.Mentions("@brand, (@shop) and @x_y? no: @, @ nothing"));
    }

    [Fact]
    public void Mentions_are_distinct_case_insensitively_keep_the_first_spelling_and_drop_trailing_dots()
    {
        var handles = CaptionParser.Mentions("@Nexor @nexor. @NEXOR... @other_1.2 @Other_1.2");
        Assert.Equal(new[] { "Nexor", "other_1.2" }, handles);
    }

    [Fact]
    public void Only_the_first_five_mentions_count()
    {
        Assert.Equal(new[] { "u1", "u2", "u3", "u4", "u5" }, CaptionParser.Mentions("@u1 @u2 @u3 @u4 @u5 @u6 @u1"));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(40, true)]
    [InlineData(41, false)]
    public void A_handle_is_two_to_forty_characters_or_it_is_plain_text(int length, bool isHandle)
    {
        var handle = new string('h', length);
        var handles = CaptionParser.Mentions($"thanks @{handle}!");
        if (isHandle)
        {
            Assert.Equal(new[] { handle }, handles);
        }
        else
        {
            Assert.Empty(handles);
        }
    }

    [Fact]
    public void Dots_alone_or_one_character_before_a_dot_are_not_handles()
    {
        Assert.Empty(CaptionParser.Mentions("@.. @a. @_."));
    }

    [Fact]
    public void Handles_come_in_any_script()
    {
        Assert.Equal(new[] { "מותג_ישראלי" }, CaptionParser.Mentions("תודה @מותג_ישראלי!"));
    }

    [Fact]
    public void Tags_and_mentions_do_not_swallow_each_other()
    {
        Assert.Equal(new[] { "summer" }, CaptionParser.Tags("#summer @brand"));
        Assert.Equal(new[] { "brand" }, CaptionParser.Mentions("#summer @brand"));
        Assert.Empty(CaptionParser.Tags("@brand #"));
    }

    [Fact]
    public void Empty_captions_yield_nothing()
    {
        Assert.Empty(CaptionParser.Tags(null));
        Assert.Empty(CaptionParser.Tags(""));
        Assert.Empty(CaptionParser.Mentions(null));
        Assert.Empty(CaptionParser.Mentions("no markers here"));
    }
}
