using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace AcoesRapidas.Calendar;

public sealed record NextcloudAccount(string Server, string LoginName, string AppPassword, string? CalendarHref = null);

public sealed record NextcloudCalendar(string Name, string Href);

public sealed record NextcloudLogin(Uri BrowserUrl, Uri PollUrl, string Token);

public sealed class NextcloudException(string message, bool isUnauthorized = false) : Exception(message)
{
    /// <summary>O servidor recusou a senha de aplicativo, então é preciso conectar de novo.</summary>
    public bool IsUnauthorized { get; } = isUnauthorized;
}

/// <summary>Conversa com o Nextcloud: autorização pelo navegador e calendários por CalDAV.</summary>
public sealed class NextcloudClient : IDisposable
{
    private const string UnreachableMessage =
        "Não foi possível falar com o Nextcloud. Confira se o servidor está ligado e se você está na rede dele.";

    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";
    private static readonly HttpMethod Propfind = new("PROPFIND");

    private readonly Uri _server;
    private readonly HttpClient _http;

    public NextcloudClient(string server, HttpMessageHandler? handler = null)
    {
        _server = new Uri(server.TrimEnd('/') + "/");

        // Sem redirecionamento automático, a senha nunca é levada para outro endereço.
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };

        // O Nextcloud usa este nome para identificar o app na lista de dispositivos conectados.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AcoesRapidas/1.0");
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Pede ao servidor o endereço onde o usuário autoriza o app.</summary>
    public async Task<NextcloudLogin> StartLoginAsync(CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_server, "index.php/login/v2"));
        using var response = await SendAsync(request, cancellation);
        EnsureSuccess(response);

        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var root = json.RootElement;
            var poll = root.GetProperty("poll");

            // Os endereços devolvidos podem usar outro nome do servidor; o configurado é o que funciona aqui.
            return new NextcloudLogin(
                OnServer(root.GetProperty("login").GetString()),
                OnServer(poll.GetProperty("endpoint").GetString()),
                poll.GetProperty("token").GetString() ?? throw new FormatException());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or UriFormatException)
        {
            throw new NextcloudException("O servidor respondeu de um jeito inesperado. Confira o endereço do Nextcloud.");
        }
    }

    /// <summary>Devolve a conta quando o usuário já autorizou, ou null enquanto ainda não autorizou.</summary>
    public async Task<NextcloudAccount?> PollLoginAsync(NextcloudLogin login, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, login.PollUrl)
        {
            Content = new FormUrlEncodedContent([new("token", login.Token)]),
        };
        using var response = await SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        EnsureSuccess(response);

        try
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            return new NextcloudAccount(
                _server.ToString().TrimEnd('/'),
                json.RootElement.GetProperty("loginName").GetString() ?? throw new FormatException(),
                json.RootElement.GetProperty("appPassword").GetString() ?? throw new FormatException());
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new NextcloudException("O servidor respondeu de um jeito inesperado durante a autorização.");
        }
    }

    /// <summary>Lista os calendários em que a conta pode criar eventos.</summary>
    public async Task<List<NextcloudCalendar>> ListCalendarsAsync(NextcloudAccount account, CancellationToken cancellation)
    {
        var principal = await FindHrefAsync(
            account, new Uri(_server, "remote.php/dav/"), Dav + "current-user-principal", cancellation);
        var home = await FindHrefAsync(account, principal, CalDav + "calendar-home-set", cancellation);

        var body = new XDocument(new XElement(Dav + "propfind",
            new XAttribute(XNamespace.Xmlns + "d", Dav),
            new XAttribute(XNamespace.Xmlns + "c", CalDav),
            new XElement(Dav + "prop",
                new XElement(Dav + "displayname"),
                new XElement(Dav + "resourcetype"),
                new XElement(Dav + "current-user-privilege-set"),
                new XElement(CalDav + "supported-calendar-component-set"))));

        var document = await PropfindAsync(account, home, depth: 1, body, cancellation);
        return document.Descendants(Dav + "response")
            .Select(ReadCalendar)
            .OfType<NextcloudCalendar>()
            .OrderBy(calendar => calendar.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Grava o evento. Repetir o mesmo identificador não cria uma cópia.</summary>
    public async Task CreateEventAsync(
        NextcloudAccount account, string calendarHref, string uid, string ics, CancellationToken cancellation)
    {
        var address = new Uri(Resolve(calendarHref.TrimEnd('/') + "/"), Uri.EscapeDataString(uid) + ".ics");
        using var request = new HttpRequestMessage(HttpMethod.Put, address)
        {
            Content = new StringContent(ics, new UTF8Encoding(false), "text/calendar"),
        };
        request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Any);
        Authorize(request, account);

        using var response = await SendAsync(request, cancellation);

        // 412 significa que uma tentativa anterior já gravou este mesmo evento.
        if (response.StatusCode != HttpStatusCode.PreconditionFailed)
        {
            EnsureSuccess(response);
        }
    }

    private async Task<Uri> FindHrefAsync(NextcloudAccount account, Uri address, XName property, CancellationToken cancellation)
    {
        var body = new XDocument(new XElement(Dav + "propfind", new XElement(Dav + "prop", new XElement(property))));
        var document = await PropfindAsync(account, address, depth: 0, body, cancellation);
        var href = document.Descendants(property).Elements(Dav + "href").FirstOrDefault()?.Value;

        return string.IsNullOrWhiteSpace(href)
            ? throw new NextcloudException("Este servidor não informou onde ficam os calendários.")
            : Resolve(href);
    }

    private async Task<XDocument> PropfindAsync(
        NextcloudAccount account, Uri address, int depth, XDocument body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(Propfind, address)
        {
            Content = new StringContent(body.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", depth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Authorize(request, account);

        using var response = await SendAsync(request, cancellation);
        EnsureSuccess(response);

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(await response.Content.ReadAsStringAsync(cancellation)), settings);
            return XDocument.Load(reader);
        }
        catch (XmlException)
        {
            throw new NextcloudException("O servidor respondeu de um jeito inesperado ao listar os calendários.");
        }
    }

    private NextcloudCalendar? ReadCalendar(XElement response)
    {
        // Propriedades que o servidor não tem vêm em um bloco separado, com outro status.
        var properties = response.Elements(Dav + "propstat")
            .Where(block => block.Element(Dav + "status")?.Value.Contains(" 200 ", StringComparison.Ordinal) == true)
            .Elements(Dav + "prop")
            .ToList();

        var isCalendar = properties.Elements(Dav + "resourcetype").Elements(CalDav + "calendar").Any();
        var acceptsEvents = properties.Elements(CalDav + "supported-calendar-component-set").Elements(CalDav + "comp")
            .Any(component => (string?)component.Attribute("name") == "VEVENT");
        var canWrite = properties.Elements(Dav + "current-user-privilege-set").Elements(Dav + "privilege").Elements()
            .Any(privilege => privilege.Name.LocalName is "write" or "write-content" or "all");
        var href = response.Element(Dav + "href")?.Value;

        if (!isCalendar || !acceptsEvents || !canWrite || string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var name = properties.Elements(Dav + "displayname").FirstOrDefault()?.Value;
        var fallback = Uri.UnescapeDataString(href.TrimEnd('/').Split('/')[^1]);
        return new NextcloudCalendar(string.IsNullOrWhiteSpace(name) ? fallback : name.Trim(), href);
    }

    /// <summary>Monta o endereço e recusa qualquer um que levaria a senha para fora do servidor.</summary>
    private Uri Resolve(string href)
    {
        var address = new Uri(_server, href);
        return address.Scheme == _server.Scheme && address.Authority == _server.Authority
            ? address
            : throw new NextcloudException("O servidor indicou um endereço de calendário fora dele.");
    }

    private Uri OnServer(string? address) =>
        new(_server, new Uri(address ?? throw new FormatException(), UriKind.Absolute).PathAndQuery);

    private static void Authorize(HttpRequestMessage request, NextcloudAccount account)
    {
        var credentials = Encoding.UTF8.GetBytes($"{account.LoginName}:{account.AppPassword}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(credentials));
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
    {
        try
        {
            return await _http.SendAsync(request, cancellation);
        }
        catch (HttpRequestException)
        {
            throw new NextcloudException(UnreachableMessage);
        }
        catch (TaskCanceledException) when (!cancellation.IsCancellationRequested)
        {
            throw new NextcloudException(UnreachableMessage);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new NextcloudException("O Nextcloud recusou o acesso. Conecte o app de novo.", isUnauthorized: true),
            HttpStatusCode.Forbidden => new NextcloudException("Sua conta não tem permissão para criar eventos neste calendário."),
            _ => new NextcloudException($"O Nextcloud respondeu com o erro {(int)response.StatusCode}."),
        };
    }
}
