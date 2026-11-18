using AcoesRapidas.Input;

namespace AcoesRapidas.Tests;

public class CopilotKeyDetectorTests
{
    private const int VkCtrlRight = 0xA3;
    private const int VkLetterS = 0x53;

    private static DetectorResult PressCopilot(CopilotKeyDetector detector, uint time = 1000)
    {
        detector.Process(CopilotKeyDetector.VkLeftWin, true, time);
        detector.Process(CopilotKeyDetector.VkLeftShift, true, time);
        return detector.Process(CopilotKeyDetector.VkF23, true, time);
    }

    /// <summary>Reproduz a ordem real registrada no notebook com a tecla no modo menu de contexto.</summary>
    private static List<DetectorResult> ReplayRealSequence(CopilotKeyDetector detector, uint time)
    {
        (int Vk, bool IsDown, uint Offset)[] events =
        [
            (CopilotKeyDetector.VkLeftWin, true, 0),
            (CopilotKeyDetector.VkLeftShift, true, 28),
            (CopilotKeyDetector.VkF23, true, 30),
            (CopilotKeyDetector.VkLeftWin, false, 32),
            (CopilotKeyDetector.VkLeftShift, false, 33),
            (CopilotKeyDetector.VkF23, false, 35),
            (CopilotKeyDetector.VkApps, true, 36),
            (CopilotKeyDetector.VkF23, false, 38),
            (CopilotKeyDetector.VkApps, false, 39),
            (CopilotKeyDetector.VkLeftShift, false, 40),
            (CopilotKeyDetector.VkLeftWin, false, 41),
        ];

        return events.Select(e => detector.Process(e.Vk, e.IsDown, time + e.Offset)).ToList();
    }

    [Fact]
    public void RealSequenceTriggersOnceAndBlocksContextMenuKey()
    {
        var results = ReplayRealSequence(new CopilotKeyDetector(), 1000);

        Assert.Equal(1, results.Count(r => r.Triggered));
        Assert.True(results[6].Suppress);
        Assert.True(results[8].Suppress);
    }

    [Fact]
    public void DuplicatedSequenceFromOnePressTriggersOnlyOnce()
    {
        var detector = new CopilotKeyDetector();

        var first = ReplayRealSequence(detector, 1000);
        var duplicate = ReplayRealSequence(detector, 1042);

        Assert.Equal(1, first.Concat(duplicate).Count(r => r.Triggered));
        Assert.True(duplicate[2].MaskWinKey);
        Assert.True(duplicate[6].Suppress);
    }

    [Fact]
    public void FastConsecutivePressesAllTrigger()
    {
        var detector = new CopilotKeyDetector();

        var first = ReplayRealSequence(detector, 1000);
        var second = ReplayRealSequence(detector, 1130);

        Assert.Equal(2, first.Concat(second).Count(r => r.Triggered));
    }

    [Fact]
    public void ContextMenuKeyWorksNormallyOnItsOwn()
    {
        var detector = new CopilotKeyDetector();

        Assert.False(detector.Process(CopilotKeyDetector.VkApps, true, 5000).Suppress);
        Assert.False(detector.Process(CopilotKeyDetector.VkApps, false, 5050).Suppress);
    }

    [Fact]
    public void ContextMenuKeyWorksNormallyLongAfterCopilotKey()
    {
        var detector = new CopilotKeyDetector();
        ReplayRealSequence(detector, 1000);

        Assert.False(detector.Process(CopilotKeyDetector.VkApps, true, 3000).Suppress);
        Assert.False(detector.Process(CopilotKeyDetector.VkApps, false, 3050).Suppress);
    }

    [Fact]
    public void TimestampWraparoundStillDetectsDuplicate()
    {
        var detector = new CopilotKeyDetector();

        var first = PressCopilot(detector, uint.MaxValue - 10);
        detector.Process(CopilotKeyDetector.VkF23, false, uint.MaxValue - 5);
        var duplicate = PressCopilot(detector, 20);

        Assert.True(first.Triggered);
        Assert.False(duplicate.Triggered);
    }

    private static void ReleaseCopilot(CopilotKeyDetector detector)
    {
        detector.Process(CopilotKeyDetector.VkF23, false);
        detector.Process(CopilotKeyDetector.VkLeftShift, false);
        detector.Process(CopilotKeyDetector.VkLeftWin, false);
    }

    [Fact]
    public void FullSequenceTriggersAndSuppressesF23()
    {
        var result = PressCopilot(new CopilotKeyDetector());

        Assert.True(result.Triggered);
        Assert.True(result.Suppress);
    }

    [Fact]
    public void F23ReleaseIsSuppressedAfterTrigger()
    {
        var detector = new CopilotKeyDetector();
        PressCopilot(detector);

        var result = detector.Process(CopilotKeyDetector.VkF23, false);

        Assert.True(result.Suppress);
        Assert.False(result.Triggered);
    }

    [Fact]
    public void AutoRepeatDoesNotTriggerAgain()
    {
        var detector = new CopilotKeyDetector();
        PressCopilot(detector);

        var repeat = detector.Process(CopilotKeyDetector.VkF23, true);

        Assert.False(repeat.Triggered);
        Assert.True(repeat.Suppress);
    }

    [Fact]
    public void SecondPressTriggersAgain()
    {
        var detector = new CopilotKeyDetector();
        PressCopilot(detector);
        ReleaseCopilot(detector);

        Assert.True(PressCopilot(detector, 2000).Triggered);
    }

    [Fact]
    public void LostF23ReleaseRecoversOnNextSequence()
    {
        var detector = new CopilotKeyDetector();
        PressCopilot(detector);

        Assert.True(PressCopilot(detector, 2000).Triggered);
    }

    [Fact]
    public void F23WithoutModifiersPassesThrough()
    {
        var result = new CopilotKeyDetector().Process(CopilotKeyDetector.VkF23, true);

        Assert.False(result.Triggered);
        Assert.False(result.Suppress);
    }

    [Fact]
    public void F23WithOnlyWinPassesThrough()
    {
        var detector = new CopilotKeyDetector();
        detector.Process(CopilotKeyDetector.VkLeftWin, true);

        Assert.False(detector.Process(CopilotKeyDetector.VkF23, true).Triggered);
    }

    [Theory]
    [InlineData(VkCtrlRight)]
    [InlineData(VkLetterS)]
    public void OtherKeysAreNeverSuppressed(int vk)
    {
        var detector = new CopilotKeyDetector();
        detector.Process(CopilotKeyDetector.VkLeftWin, true);
        detector.Process(CopilotKeyDetector.VkLeftShift, true);

        Assert.Equal(default, detector.Process(vk, true));
        Assert.Equal(default, detector.Process(vk, false));
    }
}

public class MenuKeyFilterTests
{
    private const int VkEscape = 0x1B;
    private const int VkDigit1 = 0x31;
    private const int VkDigit8 = 0x38;
    private const int VkDigit9 = 0x39;
    private const int VkNumpad3 = 0x63;

    [Theory]
    [InlineData(VkDigit1, 0)]
    [InlineData(VkDigit8, 7)]
    [InlineData(VkNumpad3, 2)]
    public void NumberKeysSelectActionWhileOpen(int vk, int expectedIndex)
    {
        var result = new MenuKeyFilter().Process(vk, true, menuOpen: true);

        Assert.True(result.Suppress);
        Assert.Equal(new MenuCommand(expectedIndex), result.Command);
    }

    [Fact]
    public void EscapeClosesWhileOpen()
    {
        var result = new MenuKeyFilter().Process(VkEscape, true, menuOpen: true);

        Assert.True(result.Suppress);
        Assert.Equal(MenuCommand.Close, result.Command);
    }

    [Theory]
    [InlineData(VkEscape)]
    [InlineData(VkDigit1)]
    public void KeysPassThroughWhenMenuIsClosed(int vk)
    {
        Assert.Equal(default, new MenuKeyFilter().Process(vk, true, menuOpen: false));
    }

    [Theory]
    [InlineData(VkDigit9, 8)]
    [InlineData(0x30, 9)]
    [InlineData(0x60, 9)]
    [InlineData(0x69, 8)]
    public void NinthAndTenthActionsHaveKeys(int vk, int expectedIndex)
    {
        Assert.Equal(new MenuCommand(expectedIndex), new MenuKeyFilter().Process(vk, true, menuOpen: true).Command);
    }

    [Fact]
    public void UnmappedKeyPassesThroughWhileOpen()
    {
        const int VkLetterA = 0x41;

        Assert.Equal(default, new MenuKeyFilter().Process(VkLetterA, true, menuOpen: true));
    }

    [Fact]
    public void ReleaseIsSuppressedEvenAfterMenuCloses()
    {
        var filter = new MenuKeyFilter();
        filter.Process(VkDigit1, true, menuOpen: true);

        Assert.True(filter.Process(VkDigit1, false, menuOpen: false).Suppress);
    }

    [Fact]
    public void AutoRepeatDoesNotRepeatCommand()
    {
        var filter = new MenuKeyFilter();
        filter.Process(VkDigit1, true, menuOpen: true);

        var repeat = filter.Process(VkDigit1, true, menuOpen: false);

        Assert.True(repeat.Suppress);
        Assert.Null(repeat.Command);
    }
}

public class KeyLogFormatterTests
{
    [Theory]
    [InlineData(0x41)]
    [InlineData(0x5A)]
    [InlineData(0x30)]
    [InlineData(0x20)]
    [InlineData(0x65)]
    [InlineData(0xBE)]
    public void TypedKeysAreRedacted(int vk)
    {
        Assert.Equal(KeyLogFormatter.RedactedKey, KeyLogFormatter.Describe(vk));
    }

    [Theory]
    [InlineData(0x5B, "Win esquerdo")]
    [InlineData(0xA0, "Shift esquerdo")]
    [InlineData(0xA3, "Ctrl direito")]
    [InlineData(0x86, "F23")]
    [InlineData(0x70, "F1")]
    public void SignalKeysAreNamed(int vk, string expected)
    {
        Assert.Equal(expected, KeyLogFormatter.Describe(vk));
    }
}
