using System.Text.Json;
using RetroBatMarqueeManager.Application.Services;
using Xunit;

namespace MarqueeManager.Tests;

public class ApiExposeLinkTests
{
    [Theory]
    [InlineData("ws://127.0.0.1:12345", true)]
    [InlineData("ws://localhost:12345", true)]
    [InlineData("ws://[::1]:12345", true)]
    [InlineData("", true)]
    [InlineData("ws://192.168.1.20:12345", false)]
    [InlineData("ws://borne-salon:12345", false)]
    [InlineData("pas une adresse", false)]
    public void Loopback_is_recognised(string url, bool expected)
        => Assert.Equal(expected, ApiExposeLink.IsLoopback(url));

    [Fact]
    public void No_key_is_presented_on_the_loopback_even_when_one_is_configured()
        => Assert.Equal(string.Empty, ApiExposeLink.ApiKeyFor("ws://127.0.0.1:12345", "0123456789ABCDEF0123", Path.GetTempPath()));

    [Fact]
    public void Configured_key_wins_off_the_loopback()
        => Assert.Equal("0123456789ABCDEF0123", ApiExposeLink.ApiKeyFor("ws://192.168.1.20:12345", " 0123456789ABCDEF0123 ", Path.GetTempPath()));

    [Fact]
    public void Sibling_cabinet_key_is_read_when_nothing_is_configured()
    {
        var root = Path.Combine(Path.GetTempPath(), "mm-link-" + Guid.NewGuid().ToString("N"));
        var plugin = Path.Combine(root, "MarqueeManager");
        var state = Path.Combine(root, "APIExpose", "state");
        Directory.CreateDirectory(plugin);
        Directory.CreateDirectory(state);
        try
        {
            File.WriteAllText(Path.Combine(state, "cabinet-api-key.txt"), "AAAABBBBCCCCDDDDEEEE\r\n");
            Assert.Equal("AAAABBBBCCCCDDDDEEEE", ApiExposeLink.ApiKeyFor("ws://192.168.1.20:12345", "", plugin));

            // too short to be a key APIExpose would have written: ignored
            File.WriteAllText(Path.Combine(state, "cabinet-api-key.txt"), "court");
            Assert.Equal(string.Empty, ApiExposeLink.ApiKeyFor("ws://192.168.1.20:12345", "", plugin));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(6, 30)]
    [InlineData(50, 30)]
    public void Reconnect_delay_grows_then_caps(int attempt, double seconds)
    {
        // jitter 0.5 = the centre of the ±20 % spread
        Assert.Equal(seconds, ApiExposeLink.ReconnectDelay(attempt, 0.5).TotalSeconds, 3);
        Assert.Equal(seconds * 0.8, ApiExposeLink.ReconnectDelay(attempt, 0).TotalSeconds, 3);
        Assert.True(ApiExposeLink.ReconnectDelay(attempt, 0.999).TotalSeconds < seconds * 1.2 + 0.001);
    }

    [Fact]
    public void A_401_off_the_loopback_names_the_setting_and_never_the_key()
    {
        const string message = "The server returned status code '401' when status code '101' was expected.";
        Assert.Contains("ApiExposeApiKey", ApiExposeLink.DescribeFailure(message, loopback: false, keyPresented: false));
        Assert.Contains("refused the cabinet key", ApiExposeLink.DescribeFailure(message, loopback: false, keyPresented: true));
        Assert.DoesNotContain("ApiExposeApiKey", ApiExposeLink.DescribeFailure(message, loopback: true, keyPresented: false));
    }

    [Theory]
    [InlineData("hiscore.updated", true)]
    [InlineData("", true)]
    [InlineData("hiscore.console.score.captured", false)]
    [InlineData("hiscore.console.score.ignored", false)]
    [InlineData("hiscore.console.write.skipped", false)]
    public void Only_tables_are_hiscores(string type, bool expected)
        => Assert.Equal(expected, ApiExposeLink.IsHiscoreTable(type));

    [Theory]
    [InlineData("1234", "1234")]
    [InlineData("2147483648", "2147483648")]
    [InlineData("9999999999999", "9999999999999")]
    [InlineData("125000.0", "125000")]
    [InlineData("\"98 400\"", "98 400")]
    [InlineData("null", "")]
    public void Score_text_survives_large_and_decimal_values(string json, string expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, ApiExposeLink.ScoreText(document.RootElement));
    }

    [Theory]
    [InlineData("12", 12)]
    [InlineData("0", 0)]
    [InlineData("-3", 0)]
    [InlineData("99999999999", 0)]
    [InlineData("\"12\"", 0)]
    public void Count_never_throws(string json, int expected)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(expected, ApiExposeLink.Count(document.RootElement));
    }
}
