using WikiViewer.Core.Models;
using Xunit;

namespace WikiViewer.Tests;

public class FavouriteNavigationTests
{
    [Theory]
    [InlineData("Page", "Actual article", "Talk:Page", "Actual article")]
    [InlineData("Page", null, "Talk:Page", "Page")]
    [InlineData("Reader", null, "User talk:Reader", "User:Reader")]
    [InlineData("Reader", null, "user talk:Reader", "User:Reader")]
    public void CardsOpenTheSubjectArticleIncludingTalkOnlyFavourites(string display, string article, string talk, string expected)
    {
        var item = new FavouriteItem(display) { ArticlePageTitle = article, TalkPageTitle = talk };
        Assert.Equal(expected, item.DefaultArticleTitle);
        Assert.Equal(article != null, item.IsArticleAvailable);
        Assert.DoesNotContain("DefaultArticleTitle", Newtonsoft.Json.JsonConvert.SerializeObject(item));
    }
}
