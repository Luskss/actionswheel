using AcoesRapidas.Config;

namespace AcoesRapidas.UI;

/// <summary>Uma tecla ou gesto e o que ele faz.</summary>
public sealed record TutorialKey(string Key, string Meaning);

/// <summary>Um passo do tutorial, com a rodinha que ilustra a explicação.</summary>
public sealed record TutorialStep(
    string Title,
    string Body,
    IReadOnlyList<TutorialKey> Keys,
    IReadOnlyList<ActionDefinition> Wheel,
    int Selected,
    string Caption);

/// <summary>Monta o tutorial com as fatias que o usuário realmente tem.</summary>
public static class TutorialContent
{
    /// <summary>
    /// Rodinha de exemplo usada nas ilustrações. Os tutoriais não mostram as ações do usuário,
    /// assim ficam iguais para todos e não expõem nomes, endereços ou canais de ninguém.
    /// </summary>
    public static IReadOnlyList<ActionDefinition> Sample { get; } =
    [
        new() { Label = "Calculadora", Icon = "E8EF", Type = ActionTypes.Command, Target = "calc.exe" },
        new() { Label = "Bloco de Notas", Icon = "E70B", Type = ActionTypes.Command, Target = "notepad.exe" },
        new()
        {
            Label = "Música",
            Icon = "E8D6",
            Type = ActionTypes.Menu,
            Children =
            [
                new() { Label = "Abrir player", Icon = "E8D6", Type = ActionTypes.Url, Target = "spotify:" },
                new() { Label = "Próxima", Icon = "E893", Type = ActionTypes.Media, Target = "next" },
                new() { Label = "Tocar / pausar", Icon = "E768", Type = ActionTypes.Media, Target = "playpause" },
                new() { Label = "Anterior", Icon = "E892", Type = ActionTypes.Media, Target = "previous" },
            ],
        },
        new()
        {
            Label = "Discord",
            Icon = "E8F2",
            Type = ActionTypes.Menu,
            Children =
            [
                new() { Label = "Abrir Discord", Icon = "E8F2", Type = ActionTypes.Url, Target = "discord://" },
                new() { Label = "Mensagens diretas", Icon = "E716", Type = ActionTypes.Url, Target = "discord://-/channels/@me" },
                new() { Label = "Meu canal", Icon = "E734", Type = ActionTypes.Url, Target = "discord://-/channels/123/456" },
            ],
        },
        new() { Label = "Criar evento", Icon = "E787", Type = ActionTypes.Event, Target = "https://nuvem.exemplo.com" },
        new() { Label = "Servidor", Icon = "E968", Type = ActionTypes.Command, Target = "wt.exe", Args = "ssh usuario@servidor" },
    ];

    public static IReadOnlyList<TutorialStep> Build(IReadOnlyList<ActionDefinition> actions)
    {
        var list = actions.FirstOrDefault(action => action.IsMenu);
        var eventAction = actions.FirstOrDefault(action => action.Type == ActionTypes.Event);

        var steps = new List<TutorialStep> { OpenStep(actions), ChooseStep(actions) };
        if (list is not null)
        {
            steps.Add(ListStep(actions, list));
        }

        steps.Add(BackStep(actions, list));
        if (eventAction is not null)
        {
            steps.Add(EventStep(actions, eventAction));
        }

        steps.Add(CustomizeStep(actions));
        return steps;
    }

    /// <summary>Ensina a pôr um canal ou conversa do Discord na rodinha, usando a lista Discord do usuário.</summary>
    public static IReadOnlyList<TutorialStep> BuildDiscord(IReadOnlyList<ActionDefinition> actions)
    {
        var index = actions.ToList().FindIndex(IsDiscordList);
        var list = index >= 0 ? actions[index] : null;
        var channels = list?.Children ?? actions;
        var last = Math.Max(0, channels.Count - 1);

        return
        [
            new TutorialStep(
                "Copie o link do canal",
                "No Discord, clique com o botão direito no canal ou na conversa que você quer e escolha Copiar link do canal. Se a opção não aparecer, ligue o Modo desenvolvedor em Configurações do Discord > Avançado.",
                [
                    new("Botão direito", "Em cima do nome do canal, na lista da esquerda."),
                    new("Copiar link", "Guarda o endereço para você colar daqui a pouco."),
                ],
                actions,
                Math.Max(0, index),
                "No Discord"),
            new TutorialStep(
                "Abra as configurações",
                list is null
                    ? "Clique no ícone da rodinha, perto do relógio do Windows. Você ainda não tem a lista Discord: clique em +, escolha Lista em O que ela faz e dê o nome Discord."
                    : "Clique no ícone da rodinha, perto do relógio do Windows. Na prévia, clique na fatia Discord e depois em Editar as opções da lista.",
                [
                    new("Ícone da rodinha", "Abre as configurações com um clique."),
                    new("Discord", "Escolha esta fatia na prévia."),
                    new("Editar as opções", "Mostra os canais que já estão na lista."),
                ],
                actions,
                Math.Max(0, index),
                "Ajustes"),
            new TutorialStep(
                "Adicione o canal",
                "Clique em + embaixo da prévia para criar uma opção nova. Dê um nome ao canal, deixe O que ela faz como Site ou app e cole o link no campo Endereço. O app troca o link por um endereço que abre direto no Discord, e não no navegador.",
                [
                    new("+", "Cria uma opção nova na lista."),
                    new("Nome", "O texto que aparece na rodinha, como o nome do canal."),
                    new("Endereço", "Cole o link. Ele vira discord://, e isso é o esperado."),
                ],
                channels,
                last,
                "Novo canal"),
            new TutorialStep(
                "Teste e salve",
                "Clique em Testar ação: o canal deve abrir no Discord. Se estiver certo, clique em Salvar. Cada lista aceita até 8 opções.",
                [
                    new("Testar ação", "Abre o canal no Discord, sem fechar as configurações."),
                    new("Salvar", "Coloca o canal na rodinha."),
                ],
                channels,
                last,
                "Testar"),
            new TutorialStep(
                "Use o canal",
                "Aperte a tecla Copilot, escolha Discord e depois o canal. O Discord abre direto nele, mesmo se estiver escondido perto do relógio.",
                [
                    new("Copilot", "Abre a rodinha."),
                    new("Discord", "Abre a lista de canais."),
                    new("Canal", "Abre a conversa no app."),
                ],
                channels,
                last,
                "Pronto"),
        ];
    }

    private static bool IsDiscordList(ActionDefinition action) =>
        action.IsMenu
        && (action.Label.Contains("discord", StringComparison.OrdinalIgnoreCase)
            || action.Children!.Any(child => child.Target.StartsWith("discord:", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Descreve as teclas de número disponíveis. A décima fatia usa a tecla 0.</summary>
    public static string NumberKeys(int count) => count >= 10 ? "1 a 9 e 0" : count == 1 ? "1" : $"1 a {count}";

    private static TutorialStep OpenStep(IReadOnlyList<ActionDefinition> actions) => new(
        "Abra a rodinha",
        "Aperte a tecla Copilot do teclado. A rodinha aparece onde o mouse estiver, por cima de qualquer programa.",
        [new("Copilot", "Abre a rodinha. Apertar de novo fecha.")],
        actions,
        0,
        "Tecla Copilot");

    private static TutorialStep ChooseStep(IReadOnlyList<ActionDefinition> actions)
    {
        var index = Math.Max(0, actions.ToList().FindIndex(action => !action.IsMenu));
        return new TutorialStep(
            "Escolha uma ação",
            $"Aponte o mouse na direção da fatia e clique. Não precisa mirar em cima do ícone: a direção já basta. Por exemplo, a fatia {actions[index].Label} fica destacada assim.",
            [
                new("Clique", "Executa a fatia destacada."),
                new(NumberKeys(actions.Count), "Executa a fatia com esse número."),
                new("← → Enter", "Anda pelas fatias e confirma."),
            ],
            actions,
            index,
            actions[index].Label);
    }

    private static TutorialStep ListStep(IReadOnlyList<ActionDefinition> actions, ActionDefinition list)
    {
        var count = list.Children!.Count;
        return new TutorialStep(
            "Abra uma lista",
            $"Fatias com uma aba saltando da borda guardam mais opções. {list.Label}, por exemplo, abre uma segunda rodinha com {count} {(count == 1 ? "opção" : "opções")}.",
            [new("Clique", "Abre a lista, e a rodinha troca pelas opções dela.")],
            actions,
            actions.ToList().IndexOf(list),
            $"{count} {(count == 1 ? "opção" : "opções")}");
    }

    private static TutorialStep BackStep(IReadOnlyList<ActionDefinition> actions, ActionDefinition? list)
    {
        var keys = new TutorialKey[]
        {
            new("Esc", list is null ? "Fecha a rodinha." : "Volta um nível. Na rodinha principal, fecha."),
            new("Botão direito", list is null ? "Fecha a rodinha." : "Faz o mesmo que o Esc."),
            new("Centro", list is null ? "Clicar no meio fecha a rodinha." : "Clicar no meio também volta ou fecha."),
        };

        return list is null
            ? new TutorialStep("Feche sem escolher", "Mudou de ideia? Saia da rodinha sem executar nada.", keys, actions, 0, "Esc fecha")
            : new TutorialStep(
                "Volte ou feche",
                $"Dentro de {list.Label}, você pode voltar para a rodinha principal ou sair sem executar nada.",
                keys,
                list.Children!,
                0,
                "Esc volta");
    }

    private static TutorialStep EventStep(IReadOnlyList<ActionDefinition> actions, ActionDefinition eventAction) => new(
        "Crie um evento",
        $"Escolha {eventAction.Label}, depois onde salvar: Google Agenda, Outlook ou Nextcloud. Preencha o título, a data e o horário. No Google e no Outlook, o navegador abre com tudo preenchido e você só confirma lá. O Nextcloud grava direto, com uma autorização no primeiro uso.",
        [
            new("Google / Outlook", "Abrem o navegador com o evento pronto para confirmar."),
            new("Nextcloud", "Cria o evento sem sair do app."),
            new("Enter", "Confirma. Esc cancela."),
        ],
        actions,
        actions.ToList().IndexOf(eventAction),
        "Nextcloud");

    private static TutorialStep CustomizeStep(IReadOnlyList<ActionDefinition> actions) => new(
        "Deixe do seu jeito",
        "Clique no ícone da rodinha, perto do relógio do Windows, para abrir as configurações. Se ele não aparecer, procure na setinha ^ da barra de tarefas. Para rever este tutorial, clique nele com o botão direito e escolha Como usar.",
        [
            new("+", "Adiciona uma fatia, até o limite de 12."),
            new("Testar ação", "Confere se a fatia funciona."),
            new("Salvar", "Aplica as mudanças na rodinha."),
        ],
        actions,
        0,
        "Ajustes");
}
