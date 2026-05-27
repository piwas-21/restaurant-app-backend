using FluentAssertions;
using RestaurantSystem.Api.Common.Utilities;

namespace RestaurantSystem.IntegrationTests.Common.Utilities;

/// <summary>
/// Unit tests for <see cref="UrlJoin"/>. These pin the contract used by
/// every S3 image-URL call site so a regression to the double-slash bug
/// (https://github.com/piwas-21/restaurant-app-backend/issues/47) fails
/// CI immediately.
/// </summary>
public class UrlJoinTests
{
    [Theory]
    // base trailing slash + path leading slash → exactly one slash
    [InlineData("https://s3.example.com/", "/foo.jpg", "https://s3.example.com/foo.jpg")]
    // base trailing slash only
    [InlineData("https://s3.example.com/", "foo.jpg", "https://s3.example.com/foo.jpg")]
    // path leading slash only
    [InlineData("https://s3.example.com", "/foo.jpg", "https://s3.example.com/foo.jpg")]
    // neither side has a slash
    [InlineData("https://s3.example.com", "foo.jpg", "https://s3.example.com/foo.jpg")]
    // multiple trailing/leading slashes still collapse
    [InlineData("https://s3.example.com///", "///foo.jpg", "https://s3.example.com/foo.jpg")]
    // nested path is preserved internally — only edge slashes are trimmed
    [InlineData("https://s3.example.com/", "products/123/main.jpg", "https://s3.example.com/products/123/main.jpg")]
    // query string: no extra slash inserted before '?'
    [InlineData("https://s3.example.com", "foo.jpg?version=2", "https://s3.example.com/foo.jpg?version=2")]
    // query string with leading slash on path
    [InlineData("https://s3.example.com/", "/foo.jpg?v=1&w=200", "https://s3.example.com/foo.jpg?v=1&w=200")]
    public void Join_NormalisesSeparator(string baseUrl, string path, string expected)
    {
        UrlJoin.Join(baseUrl, path).Should().Be(expected);
    }

    [Fact]
    public void Join_EmptyPath_ReturnsTrimmedBase()
    {
        UrlJoin.Join("https://s3.example.com/", "").Should().Be("https://s3.example.com");
    }

    [Fact]
    public void Join_NullPath_ReturnsTrimmedBase()
    {
        UrlJoin.Join("https://s3.example.com/", null).Should().Be("https://s3.example.com");
    }

    [Fact]
    public void Join_EmptyBase_ReturnsTrimmedPath()
    {
        UrlJoin.Join("", "/foo.jpg").Should().Be("foo.jpg");
    }

    [Fact]
    public void Join_NullBase_ReturnsTrimmedPath()
    {
        UrlJoin.Join(null, "/foo.jpg").Should().Be("foo.jpg");
    }

    [Fact]
    public void Join_BothNull_ReturnsEmpty()
    {
        UrlJoin.Join(null, null).Should().Be(string.Empty);
    }

    [Fact]
    public void Join_BothEmpty_ReturnsEmpty()
    {
        UrlJoin.Join("", "").Should().Be(string.Empty);
    }
}
