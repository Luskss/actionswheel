using System.Globalization;
using System.IO;
using AcoesRapidas.Config;

namespace AcoesRapidas.Calendar;

/// <summary>Onde o evento será criado. A ordem é a mesma mostrada na janela.</summary>
public enum EventDestination
{
    Google,
    Outlook,
    Nextcloud,
}

/// <summary>
/// Monta links que abrem o Google Agenda ou o Outlook com o evento já preenchido.
/// O usuário confirma no navegador, então o app não guarda login nem senha desses serviços.
/// </summary>
public static class CalendarLinks
{
    private const string UtcFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const string OutlookUtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static Uri Google(EventDraft draft, TimeZoneInfo timeZone)
    {
        var dates = draft is { Start: { } start, End: { } end }
            ? $"{Format(EventInput.ToUtc(draft.Date, start, timeZone), UtcFormat)}/{Format(EventInput.ToUtc(draft.Date, end, timeZone), UtcFormat)}"
            : $"{Format(draft.Date, "yyyyMMdd")}/{Format(draft.Date.AddDays(1), "yyyyMMdd")}";

        return Build(
            "https://calendar.google.com/calendar/render",
            ("action", "TEMPLATE"),
            ("text", draft.Title),
            ("dates", dates),
            ("location", draft.Location));
    }

    public static Uri Outlook(EventDraft draft, TimeZoneInfo timeZone)
    {
        var (startText, endText, allDay) = draft is { Start: { } start, End: { } end }
            ? (Format(EventInput.ToUtc(draft.Date, start, timeZone), OutlookUtcFormat),
                Format(EventInput.ToUtc(draft.Date, end, timeZone), OutlookUtcFormat),
                null)
            : (Format(draft.Date, "yyyy-MM-dd"), Format(draft.Date.AddDays(1), "yyyy-MM-dd"), "true");

        return Build(
            "https://outlook.live.com/calendar/0/deeplink/compose",
            ("path", "/calendar/action/compose"),
            ("rru", "addevent"),
            ("subject", draft.Title),
            ("startdt", startText),
            ("enddt", endText),
            ("allday", allDay),
            ("location", draft.Location));
    }

    private static Uri Build(string address, params (string Name, string? Value)[] parameters)
    {
        var query = string.Join(
            "&",
            parameters
                .Where(parameter => !string.IsNullOrEmpty(parameter.Value))
                .Select(parameter => $"{parameter.Name}={Uri.EscapeDataString(parameter.Value!)}"));
        return new Uri($"{address}?{query}");
    }

    private static string Format(DateTime value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private static string Format(DateOnly value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}

/// <summary>Lembra o último destino escolhido para a próxima janela já abrir nele.</summary>
public static class EventDestinationStore
{
    public static string DefaultPath { get; } = Path.Combine(ConfigStore.DefaultDirectory, "event-destination");

    /// <summary>Devolve o destino guardado, ou o padrão quando ainda não há um válido.</summary>
    public static EventDestination Load(string path, EventDestination fallback = EventDestination.Google)
    {
        try
        {
            return Enum.TryParse<EventDestination>(File.ReadAllText(path).Trim(), ignoreCase: true, out var destination)
                && Enum.IsDefined(destination)
                    ? destination
                    : fallback;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    public static void Save(string path, EventDestination destination)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, destination.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Não lembrar o destino só faz a janela abrir no Google Agenda da próxima vez.
        }
    }
}
