using AcoesRapidas.Config;
using AcoesRapidas.UI;

namespace AcoesRapidas.Tests;

public sealed class TutorialContentTests
{
    private static readonly ActionDefinition Notes = new() { Label = "Notas", Type = ActionTypes.Command, Target = "notepad.exe" };
    private static readonly ActionDefinition Calc = new() { Label = "Calculadora", Type = ActionTypes.Command, Target = "calc.exe" };
    private static readonly ActionDefinition Event = new() { Label = "Criar evento", Type = ActionTypes.Event, Target = "http://192.168.0.10:8080" };
    private static readonly ActionDefinition Music = new()
    {
        Label = "Spotify",
        Type = ActionTypes.Menu,
        Children = [Notes, Calc, Notes, Calc],
    };

    [Fact]
    public void FullWheelTeachesEveryFeature()
    {
        var steps = TutorialContent.Build([Music, Notes, Event]);

        Assert.Equal(
            ["Abra a rodinha", "Escolha uma ação", "Abra uma lista", "Volte ou feche", "Crie um evento", "Deixe do seu jeito"],
            steps.Select(step => step.Title));
    }

    [Fact]
    public void SimpleWheelSkipsWhatTheUserDoesNotHave()
    {
        var steps = TutorialContent.Build([Notes, Calc]);

        Assert.Equal(
            ["Abra a rodinha", "Escolha uma ação", "Feche sem escolher", "Deixe do seu jeito"],
            steps.Select(step => step.Title));
    }

    [Fact]
    public void ExamplesUseTheRealSlices()
    {
        var steps = TutorialContent.Build([Music, Notes, Event]);

        var choose = steps.Single(step => step.Title == "Escolha uma ação");
        Assert.Equal(1, choose.Selected);
        Assert.Contains("Notas", choose.Body);

        var list = steps.Single(step => step.Title == "Abra uma lista");
        Assert.Equal(0, list.Selected);
        Assert.Contains("Spotify", list.Body);
        Assert.Contains("4 opções", list.Body);

        Assert.Equal(2, steps.Single(step => step.Title == "Crie um evento").Selected);
        Assert.Same(Music.Children, steps.Single(step => step.Title == "Volte ou feche").Wheel);
    }

    [Fact]
    public void WheelMadeOnlyOfListsStillHasAValidExample()
    {
        var steps = TutorialContent.Build([Music, Music]);

        Assert.All(steps, step => Assert.InRange(step.Selected, 0, step.Wheel.Count - 1));
    }

    private static readonly ActionDefinition Chat = new()
    {
        Label = "Discord",
        Type = ActionTypes.Menu,
        Children =
        [
            new() { Label = "Abrir", Type = ActionTypes.Url, Target = "discord://" },
            new() { Label = "Canal", Type = ActionTypes.Url, Target = "discord://-/channels/1/2" },
        ],
    };

    [Fact]
    public void SampleWheelIsValidAndFeedsBothTutorials()
    {
        for (var i = 0; i < TutorialContent.Sample.Count; i++)
        {
            ConfigStore.ValidateAction(TutorialContent.Sample[i], i + 1);
        }

        Assert.Equal(6, TutorialContent.Build(TutorialContent.Sample).Count);
        var discord = TutorialContent.BuildDiscord(TutorialContent.Sample);
        Assert.Equal(5, discord.Count);
        Assert.Equal("Meu canal", discord[2].Wheel[discord[2].Selected].Label);
    }

    [Fact]
    public void DiscordTutorialUsesTheDiscordList()
    {
        var steps = TutorialContent.BuildDiscord([Notes, Chat, Event]);

        Assert.Equal(5, steps.Count);
        Assert.Equal(1, steps[0].Selected);
        Assert.Same(Chat.Children, steps[2].Wheel);
        Assert.Equal(1, steps[2].Selected);
        Assert.All(steps, step => Assert.InRange(step.Selected, 0, step.Wheel.Count - 1));
    }

    [Fact]
    public void DiscordListIsFoundByItsLinksEvenWithAnotherName()
    {
        var renamed = Chat with { Label = "Conversas" };

        var steps = TutorialContent.BuildDiscord([Notes, renamed]);

        Assert.Same(renamed.Children, steps[2].Wheel);
    }

    [Fact]
    public void DiscordTutorialWorksBeforeTheListExists()
    {
        var steps = TutorialContent.BuildDiscord([Notes, Calc]);

        Assert.All(steps, step => Assert.InRange(step.Selected, 0, step.Wheel.Count - 1));
        Assert.Contains("Você ainda não tem a lista Discord", steps[1].Body);
    }

    [Theory]
    [InlineData(2, "1 a 2")]
    [InlineData(9, "1 a 9")]
    [InlineData(10, "1 a 9 e 0")]
    [InlineData(12, "1 a 9 e 0")]
    public void DescribesTheNumberKeys(int count, string expected)
    {
        Assert.Equal(expected, TutorialContent.NumberKeys(count));
    }
}
