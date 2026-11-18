using AcoesRapidas.Config;
using AcoesRapidas.Input;
using AcoesRapidas.UI;

namespace AcoesRapidas.Tests;

public sealed class MenuUsabilityTests
{
    private const int VkBackspace = 0x08;
    private const int VkEnter = 0x0D;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;

    public static TheoryData<int, MenuCommand> NavigationKeys => new()
    {
        { VkRight, MenuCommand.Next },
        { VkDown, MenuCommand.Next },
        { VkLeft, MenuCommand.Previous },
        { VkUp, MenuCommand.Previous },
        { VkEnter, MenuCommand.Accept },
        { VkBackspace, MenuCommand.Close },
    };

    [Theory]
    [MemberData(nameof(NavigationKeys))]
    public void NavigationKeysWorkWhileTheMenuIsOpen(int vk, MenuCommand expected)
    {
        var result = new MenuKeyFilter().Process(vk, true, menuOpen: true);

        Assert.True(result.Suppress);
        Assert.Equal(expected, result.Command);
    }

    [Theory]
    [MemberData(nameof(NavigationKeys))]
    public void NavigationKeysStayNormalWhileTheMenuIsClosed(int vk, MenuCommand _)
    {
        Assert.Equal(default, new MenuKeyFilter().Process(vk, true, menuOpen: false));
    }

    [Fact]
    public void HoldingAnArrowKeepsMoving()
    {
        var filter = new MenuKeyFilter();
        filter.Process(VkRight, true, menuOpen: true);

        Assert.Equal(MenuCommand.Next, filter.Process(VkRight, true, menuOpen: true).Command);
    }

    [Fact]
    public void HoldingEnterDoesNotChooseTwice()
    {
        var filter = new MenuKeyFilter();
        filter.Process(VkEnter, true, menuOpen: true);

        var repeat = filter.Process(VkEnter, true, menuOpen: true);

        Assert.True(repeat.Suppress);
        Assert.Null(repeat.Command);
    }

    [Fact]
    public void ArrowHeldWhenTheMenuClosesStopsMovingAndStaysSwallowed()
    {
        var filter = new MenuKeyFilter();
        filter.Process(VkRight, true, menuOpen: true);

        var repeat = filter.Process(VkRight, true, menuOpen: false);

        Assert.True(repeat.Suppress);
        Assert.Null(repeat.Command);
        Assert.True(filter.Process(VkRight, false, menuOpen: false).Suppress);
    }

    [Theory]
    [InlineData(null, 1, 12, 0)]
    [InlineData(null, -1, 12, 11)]
    [InlineData(0, 1, 12, 1)]
    [InlineData(11, 1, 12, 0)]
    [InlineData(0, -1, 12, 11)]
    public void StepsAroundTheWheel(int? current, int step, int count, int expected)
    {
        Assert.Equal(expected, RadialMenuGeometry.StepSlice(current, step, count));
    }

    [Fact]
    public void StepOnAnEmptyWheelSelectsNothing()
    {
        Assert.Null(RadialMenuGeometry.StepSlice(null, 1, 0));
    }

    [Theory]
    [InlineData("https://discord.com/channels/108/109", "discord://-/channels/108/109")]
    [InlineData("https://ptb.discord.com/channels/108/109/110", "discord://-/channels/108/109/110")]
    [InlineData("https://discord.com/channels/@me", "discord://-/channels/@me")]
    [InlineData("  https://discord.com/channels/108/109/  ", "discord://-/channels/108/109")]
    [InlineData("https://open.spotify.com/track/abc123?si=xyz", "spotify:track:abc123")]
    [InlineData("https://open.spotify.com/intl-pt/playlist/abc123", "spotify:playlist:abc123")]
    public void PastedLinksOpenInTheApp(string link, string expected)
    {
        var address = AppLinks.ToAppAddress(link);

        Assert.Equal(expected, address);
        Assert.True(ConfigStore.IsAllowedUrl(address));
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://192.168.0.10:8080/channels/1/2")]
    [InlineData("https://discord.com/invite/abc")]
    [InlineData("https://discord.com.exemplo.net/channels/1/2")]
    [InlineData("https://open.spotify.com/user/abc")]
    [InlineData("steam://rungameid/365670")]
    public void OtherLinksStayAsTheyAre(string link)
    {
        Assert.Equal(link, AppLinks.ToAppAddress(link));
    }
}
