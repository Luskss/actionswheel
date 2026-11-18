using System.Text.RegularExpressions;

namespace AcoesRapidas.Config;

/// <summary>Troca links copiados do navegador pelo endereço que abre o mesmo conteúdo direto no app.</summary>
public static partial class AppLinks
{
    public static string ToAppAddress(string link)
    {
        var value = link.Trim();

        var discord = DiscordChannel().Match(value);
        if (discord.Success)
        {
            return $"discord://-/channels/{discord.Groups[1].Value}";
        }

        var spotify = SpotifyItem().Match(value);
        return spotify.Success ? $"spotify:{spotify.Groups[1].Value}:{spotify.Groups[2].Value}" : value;
    }

    [GeneratedRegex(@"^https?://(?:www\.|ptb\.|canary\.)?discord(?:app)?\.com/channels/((?:@me|\d+)(?:/\d+){0,2})/?(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex DiscordChannel();

    [GeneratedRegex(@"^https?://open\.spotify\.com/(?:intl-[a-z-]+/)?(track|album|playlist|artist|show|episode)/([A-Za-z0-9]+)/?(?:[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex SpotifyItem();
}
