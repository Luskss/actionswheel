using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using AcoesRapidas.Config;
using AcoesRapidas.Input;

namespace AcoesRapidas.Actions;

/// <summary>Executa as ações do menu e informa o resultado pelas notificações do app.</summary>
public sealed class ActionRunner(Action<string, bool> notify, Action openSettings, Action<ActionDefinition> openEvent)
{
    public void Run(ActionDefinition action)
    {
        try
        {
            switch (action.Type.ToLowerInvariant())
            {
                case ActionTypes.Toast:
                    notify(action.Target, false);
                    break;

                case ActionTypes.Url:
                    OpenUrl(action);
                    break;

                case ActionTypes.Command:
                    StartCommand(action);
                    break;

                case ActionTypes.Settings:
                    openSettings();
                    break;

                case ActionTypes.Event:
                    openEvent(action);
                    break;

                case ActionTypes.Media when ConfigStore.MediaKeys.TryGetValue(action.Target, out var key):
                    NativeMethods.SendMediaKey(key);
                    break;
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            notify($"Não foi possível executar \"{action.Label}\": {ex.Message}", true);
        }
    }

    private void OpenUrl(ActionDefinition action)
    {
        if (!ConfigStore.IsAllowedUrl(action.Target))
        {
            notify($"O endereço de \"{action.Label}\" não é permitido.", true);
            return;
        }

        Start(new ProcessStartInfo(action.Target) { UseShellExecute = true });

        if (action.Target.StartsWith("discord:", StringComparison.OrdinalIgnoreCase))
        {
            _ = BringDiscordToFrontAsync(action.Target);
        }
    }

    /// <summary>
    /// O Discord escondido na bandeja não mostra a janela ao receber o endereço e, ao ser restaurado,
    /// volta à tela anterior. Por isso a janela é restaurada e o endereço é enviado de novo.
    /// </summary>
    private static async Task BringDiscordToFrontAsync(string target)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await Task.Delay(attempt == 0 ? 400 : 300);

            var ids = Process.GetProcessesByName("Discord").Select(p => (uint)p.Id).ToHashSet();
            if (ids.Count > 0 && NativeMethods.RestoreWindowOfProcesses(
                    ids, title => title.EndsWith("Discord", StringComparison.Ordinal) && !title.Contains("Overlay")))
            {
                await Task.Delay(700);
                try
                {
                    Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    // O primeiro envio já aconteceu; falhar aqui só deixa o Discord na tela anterior.
                }

                return;
            }
        }
    }

    private static void StartCommand(ActionDefinition action)
    {
        Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(action.Target))
        {
            Arguments = Environment.ExpandEnvironmentVariables(action.Args ?? ""),
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        });
    }

    private static void Start(ProcessStartInfo startInfo)
    {
        NativeMethods.GrantForegroundToNextProcess();
        Process.Start(startInfo)?.Dispose();
    }
}
