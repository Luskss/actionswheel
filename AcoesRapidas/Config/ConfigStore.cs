using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AcoesRapidas.Config;

public static class ActionTypes
{
    public const string Toast = "toast";
    public const string Url = "url";
    public const string Command = "command";
    public const string Settings = "settings";

    /// <summary>Abre uma segunda rodinha com as ações em "children".</summary>
    public const string Menu = "menu";

    /// <summary>Envia uma tecla de mídia (tocar, pausar, próxima, anterior).</summary>
    public const string Media = "media";

    /// <summary>
    /// Abre a janela de novo evento, que oferece Google Agenda e Outlook. Se "target" tiver o endereço
    /// de um Nextcloud, ele aparece como terceira opção.
    /// </summary>
    public const string Event = "event";
}

public sealed record ActionDefinition
{
    public string Label { get; init; } = "";

    /// <summary>Código hexadecimal de um ícone da fonte Segoe Fluent Icons, como "E774".</summary>
    public string Icon { get; init; } = "E946";

    /// <summary>Programa ou imagem cujo ícone substitui o ícone da fonte. Pode usar variáveis como %LOCALAPPDATA%.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconPath { get; init; }

    public string Type { get; init; } = "";

    public string Target { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Args { get; init; }

    /// <summary>Ações da segunda rodinha, usadas quando o tipo é "menu".</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ActionDefinition>? Children { get; init; }

    [JsonIgnore]
    public string IconGlyph => char.ConvertFromUtf32(Convert.ToInt32(Icon, 16));

    [JsonIgnore]
    public bool IsMenu =>
        string.Equals(Type, ActionTypes.Menu, StringComparison.OrdinalIgnoreCase) && Children is { Count: > 0 };
}

public sealed record AppConfig
{
    public List<ActionDefinition> Actions { get; init; } = [];
}

public sealed class ConfigException(string message) : Exception(message);

public sealed partial class ConfigStore(string path)
{
    public const int MinActions = 2;
    public const int MaxActions = 12;
    public const int MaxChildren = 8;

    /// <summary>Quantos níveis de rodinha existem: a principal e mais duas listas dentro de listas.</summary>
    private const int MaxDepth = 3;

    private static readonly string[] PrivateHostSuffixes = [".local", ".lan", ".home.arpa", ".internal", ".ts.net"];

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Teclas de mídia aceitas pelo tipo "media", com o código virtual de cada uma.</summary>
    public static IReadOnlyDictionary<string, ushort> MediaKeys { get; } =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["playpause"] = 0xB3,
            ["next"] = 0xB0,
            ["previous"] = 0xB1,
            ["stop"] = 0xB2,
        };

    public static string DefaultDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AcoesRapidas");

    public static string DefaultPath { get; } = Path.Combine(DefaultDirectory, "config.json");

    public string FilePath { get; } = path;

    public AppConfig Current { get; private set; } = CreateDefault();

    /// <summary>
    /// Lê o arquivo, criando-o na primeira execução. Em caso de erro mantém a configuração
    /// atual e devolve a mensagem para o usuário.
    /// </summary>
    public string? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, WriteOptions));
                return null;
            }

            Current = Parse(File.ReadAllText(FilePath));
            return null;
        }
        catch (ConfigException ex)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Não foi possível acessar a configuração: {ex.Message}";
        }
    }

    /// <summary>Valida e grava a configuração. Devolve a mensagem de erro, se houver.</summary>
    public string? Save(AppConfig config)
    {
        try
        {
            Validate(config);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Grava em um arquivo temporário para uma falha não corromper a configuração atual.
            var temporaryPath = FilePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(config, WriteOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);

            Current = config;
            return null;
        }
        catch (ConfigException ex)
        {
            return ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Não foi possível salvar a configuração: {ex.Message}";
        }
    }

    public static AppConfig Parse(string json)
    {
        AppConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<AppConfig>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            var line = ex.LineNumber is { } number ? $" na linha {number + 1}" : "";
            throw new ConfigException($"O arquivo de configuração tem um erro de JSON{line}.");
        }

        if (config?.Actions is null)
        {
            throw new ConfigException("A configuração precisa ter a lista \"actions\".");
        }

        Validate(config);
        return config;
    }

    /// <summary>Aceita sites e os endereços dos aplicativos que o app sabe abrir (Steam, Discord e Spotify).</summary>
    public static bool IsAllowedUrl(string value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return true;
        }

        return AppSchemePattern().IsMatch(value);
    }

    /// <summary>
    /// A senha do Nextcloud vai em cada pedido, então http só é aceito em redes privadas,
    /// incluindo os endereços do Tailscale. Fora delas o endereço precisa ser https.
    /// </summary>
    public static bool IsAllowedNextcloudServer(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps)
        {
            return true;
        }

        if (uri.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        if (!IPAddress.TryParse(uri.IdnHost, out var address))
        {
            return uri.IsLoopback
                || !uri.IdnHost.Contains('.')
                || PrivateHostSuffixes.Any(suffix => uri.IdnHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6UniqueLocal;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168)
            || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
    }

    public static AppConfig CreateDefault() => new()
    {
        Actions =
        [
            new() { Label = "Notificação", Icon = "E946", Type = ActionTypes.Toast, Target = "O menu de ações rápidas funcionou!" },
            new() { Label = "Site de teste", Icon = "E774", Type = ActionTypes.Url, Target = "https://example.com" },
            new() { Label = "Bloco de Notas", Icon = "E70B", Type = ActionTypes.Command, Target = "notepad.exe" },
            new() { Label = "Calculadora", Icon = "E8EF", Type = ActionTypes.Command, Target = "calc.exe" },
            new() { Label = "Comando", Icon = "E756", Type = ActionTypes.Command, Target = "cmd.exe", Args = "/k echo Comando de teste executado." },
            new() { Label = "Ajustes", Icon = "E713", Type = ActionTypes.Settings },
        ],
    };

    public static void ValidateAction(ActionDefinition? action, int position) => ValidateAction(action, position, 1);

    private static void Validate(AppConfig config) =>
        ValidateList(config.Actions, MinActions, MaxActions, "O menu", 1);

    private static void ValidateList(List<ActionDefinition> actions, int minimum, int maximum, string owner, int depth)
    {
        if (actions.Count < minimum || actions.Count > maximum)
        {
            throw new ConfigException($"{owner} precisa ter de {minimum} a {maximum} ações.");
        }

        for (var i = 0; i < actions.Count; i++)
        {
            ValidateAction(actions[i], i + 1, depth);
        }
    }

    private static void ValidateAction(ActionDefinition? action, int position, int depth)
    {
        if (action is null)
        {
            throw new ConfigException($"A ação {position} está vazia.");
        }

        if (string.IsNullOrWhiteSpace(action.Label))
        {
            throw new ConfigException($"A ação {position} precisa de um nome.");
        }

        var type = action.Type?.ToLowerInvariant();
        var needsTarget = type is not (ActionTypes.Settings or ActionTypes.Menu or ActionTypes.Event);
        if (needsTarget && string.IsNullOrWhiteSpace(action.Target))
        {
            throw new ConfigException($"A ação \"{action.Label}\" precisa de um destino.");
        }

        if (!IconPattern().IsMatch(action.Icon ?? ""))
        {
            throw new ConfigException($"O ícone da ação \"{action.Label}\" precisa ser um código como E774.");
        }

        switch (type)
        {
            case ActionTypes.Toast:
            case ActionTypes.Command:
            case ActionTypes.Settings:
                break;

            case ActionTypes.Url when IsAllowedUrl(action.Target):
                break;

            case ActionTypes.Url:
                throw new ConfigException(
                    $"A ação \"{action.Label}\" precisa de um endereço http, https, steam, discord ou spotify.");

            case ActionTypes.Media when MediaKeys.ContainsKey(action.Target):
                break;

            case ActionTypes.Media:
                throw new ConfigException(
                    $"A ação \"{action.Label}\" precisa de uma tecla de mídia: playpause, next, previous ou stop.");

            case ActionTypes.Menu:
                ValidateMenu(action, depth);
                break;

            case ActionTypes.Event when string.IsNullOrWhiteSpace(action.Target) || IsAllowedNextcloudServer(action.Target):
                break;

            case ActionTypes.Event:
                throw new ConfigException(
                    $"O endereço do Nextcloud da ação \"{action.Label}\" precisa ser https, ou http em uma rede privada. Deixe em branco para usar só o Google Agenda e o Outlook.");

            default:
                throw new ConfigException(
                    $"A ação \"{action.Label}\" tem um tipo desconhecido. Use toast, url, command, settings, menu, media ou event.");
        }
    }

    private static void ValidateMenu(ActionDefinition action, int depth)
    {
        if (depth >= MaxDepth)
        {
            throw new ConfigException($"A lista \"{action.Label}\" está dentro de listas demais.");
        }

        if (action.Children is null)
        {
            throw new ConfigException($"A lista \"{action.Label}\" precisa de \"children\".");
        }

        ValidateList(action.Children, 1, MaxChildren, $"A lista \"{action.Label}\"", depth + 1);
    }

    [GeneratedRegex("^[EeFf][0-9A-Fa-f]{3}$")]
    private static partial Regex IconPattern();

    [GeneratedRegex("""^(?:steam|discord|spotify):[^\s"<>|^]*$""", RegexOptions.IgnoreCase)]
    private static partial Regex AppSchemePattern();
}
