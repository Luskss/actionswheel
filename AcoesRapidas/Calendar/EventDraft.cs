using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AcoesRapidas.Calendar;

/// <summary>Evento preenchido pelo usuário. Sem horário de início, ele ocupa o dia inteiro.</summary>
public sealed record EventDraft(string Title, DateOnly Date, TimeOnly? Start, TimeOnly? End, string? Location)
{
    public bool IsAllDay => Start is null;
}

/// <summary>Interpreta o que foi digitado na janela de evento.</summary>
public static partial class EventInput
{
    /// <summary>Aceita "hoje", "amanhã", 25/12, 25/12/26 e 25/12/2026.</summary>
    public static DateOnly? ParseDate(string text, DateOnly today)
    {
        var value = text.Trim().ToLowerInvariant();
        if (value == "hoje")
        {
            return today;
        }

        if (value is "amanhã" or "amanha")
        {
            return today.AddDays(1);
        }

        var parts = value.Split('/', '-', '.');
        if (parts.Length is < 2 or > 3 || !parts.All(part => part.Length > 0 && part.All(char.IsAsciiDigit) && part.Length <= 4))
        {
            return null;
        }

        var day = int.Parse(parts[0], CultureInfo.InvariantCulture);
        var month = int.Parse(parts[1], CultureInfo.InvariantCulture);

        if (parts.Length == 3)
        {
            var year = int.Parse(parts[2], CultureInfo.InvariantCulture);
            return Create(year < 100 ? 2000 + year : year, month, day);
        }

        // Sem o ano, vale a próxima vez em que essa data acontece.
        for (var year = today.Year; year <= today.Year + 4; year++)
        {
            if (Create(year, month, day) is { } date && date >= today)
            {
                return date;
            }
        }

        return null;
    }

    /// <summary>Aceita 14, 14h, 14h30 e 14:30.</summary>
    public static TimeOnly? ParseTime(string text)
    {
        var match = TimePattern().Match(text.Trim());
        if (!match.Success)
        {
            return null;
        }

        var hour = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups[2].Success ? int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
        return hour < 24 && minute < 60 ? new TimeOnly(hour, minute) : null;
    }

    /// <summary>Monta o evento ou devolve a mensagem que explica o que corrigir.</summary>
    public static (EventDraft? Draft, string? Error) Read(
        string title, string date, string start, string end, bool allDay, string location, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (null, "Dê um título ao evento.");
        }

        if (ParseDate(date, today) is not { } day)
        {
            return (null, "Use uma data como 25/12/2026.");
        }

        var place = string.IsNullOrWhiteSpace(location) ? null : location.Trim();
        if (allDay)
        {
            return (new EventDraft(title.Trim(), day, null, null, place), null);
        }

        if (ParseTime(start) is not { } startTime)
        {
            return (null, "Use um horário de início como 14:30.");
        }

        if (ParseTime(end) is not { } endTime)
        {
            return (null, "Use um horário de fim como 15:30.");
        }

        return endTime <= startTime
            ? (null, "O fim precisa ser depois do início.")
            : (new EventDraft(title.Trim(), day, startTime, endTime, place), null);
    }

    /// <summary>Sugere a próxima meia hora cheia como início do evento.</summary>
    public static DateTime SuggestStart(DateTime now)
    {
        var start = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, now.Kind);
        return start.AddMinutes(now.Minute < 30 ? 30 : 60);
    }

    /// <summary>Converte o horário digitado, no fuso do usuário, para UTC.</summary>
    public static DateTime ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);

        // Um horário pulado pelo horário de verão não existe; vale o primeiro instante depois do salto.
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return TimeZoneInfo.ConvertTimeToUtc(local, timeZone);
    }

    /// <summary>Sugere uma hora de duração, sem passar para o dia seguinte.</summary>
    public static TimeOnly SuggestEnd(TimeOnly start)
    {
        var end = start.AddHours(1);
        return end > start ? end : new TimeOnly(23, 59);
    }

    private static DateOnly? Create(int year, int month, int day) =>
        year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;

    [GeneratedRegex(@"^(\d{1,2})(?:[:hH](\d{2})?)?$")]
    private static partial Regex TimePattern();
}

/// <summary>Gera o arquivo iCalendar (RFC 5545) que o Nextcloud guarda.</summary>
public static class IcsBuilder
{
    private const int MaxLineOctets = 75;
    private const string UtcFormat = "yyyyMMdd'T'HHmmss'Z'";
    private const string DateFormat = "yyyyMMdd";

    public static string Build(EventDraft draft, string uid, DateTime nowUtc, TimeZoneInfo timeZone)
    {
        var ics = new StringBuilder();
        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//AcoesRapidas//Evento rapido//PT");
        Line("BEGIN:VEVENT");
        Line($"UID:{uid}");
        Line($"DTSTAMP:{nowUtc.ToString(UtcFormat, CultureInfo.InvariantCulture)}");

        if (draft is { Start: { } start, End: { } end })
        {
            Line($"DTSTART:{ToUtc(draft.Date, start, timeZone)}");
            Line($"DTEND:{ToUtc(draft.Date, end, timeZone)}");
        }
        else
        {
            // No iCalendar o fim de um evento de dia inteiro é o dia seguinte.
            Line($"DTSTART;VALUE=DATE:{draft.Date.ToString(DateFormat, CultureInfo.InvariantCulture)}");
            Line($"DTEND;VALUE=DATE:{draft.Date.AddDays(1).ToString(DateFormat, CultureInfo.InvariantCulture)}");
        }

        Line($"SUMMARY:{Escape(draft.Title)}");
        if (draft.Location is { } location)
        {
            Line($"LOCATION:{Escape(location)}");
        }

        Line("END:VEVENT");
        Line("END:VCALENDAR");
        return ics.ToString();

        void Line(string text) => AppendFolded(ics, text);
    }

    private static string ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo timeZone) =>
        EventInput.ToUtc(date, time, timeZone).ToString(UtcFormat, CultureInfo.InvariantCulture);

    private static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var character in text.ReplaceLineEndings("\n"))
        {
            switch (character)
            {
                case '\\' or ';' or ',':
                    escaped.Append('\\').Append(character);
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                default:
                    if (!char.IsControl(character))
                    {
                        escaped.Append(character);
                    }

                    break;
            }
        }

        return escaped.ToString();
    }

    /// <summary>Quebra linhas longas sem cortar um caractere acentuado ao meio.</summary>
    private static void AppendFolded(StringBuilder ics, string line)
    {
        var octets = 0;
        foreach (var rune in line.EnumerateRunes())
        {
            if (octets + rune.Utf8SequenceLength > MaxLineOctets)
            {
                ics.Append("\r\n ");
                octets = 1;
            }

            ics.Append(rune.ToString());
            octets += rune.Utf8SequenceLength;
        }

        ics.Append("\r\n");
    }
}
