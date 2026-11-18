using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using AcoesRapidas.Calendar;
using AcoesRapidas.Config;

namespace AcoesRapidas.Tests;

public sealed class CalendarTests
{
    private const string Server = "http://192.168.0.10:8080";
    private static readonly DateOnly Today = new(2026, 11, 18);
    private static readonly NextcloudAccount Account = new(Server, "usuario", "senha-de-app");
    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");

    [Theory]
    [InlineData("hoje", 2026, 11, 18)]
    [InlineData("amanhã", 2026, 11, 19)]
    [InlineData("amanha", 2026, 11, 19)]
    [InlineData("25/12", 2026, 12, 25)]
    [InlineData("10/01", 2027, 1, 10)]
    [InlineData("25/12/27", 2027, 12, 25)]
    [InlineData("25-12-2026", 2026, 12, 25)]
    [InlineData("29/02", 2028, 2, 29)]
    public void UnderstandsDates(string text, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), EventInput.ParseDate(text, Today));
    }

    [Theory]
    [InlineData("")]
    [InlineData("31/02/2026")]
    [InlineData("13/13")]
    [InlineData("amanhã cedo")]
    [InlineData("1/2/3/4")]
    public void RejectsInvalidDates(string text)
    {
        Assert.Null(EventInput.ParseDate(text, Today));
    }

    [Theory]
    [InlineData("14", 14, 0)]
    [InlineData("14h", 14, 0)]
    [InlineData("14h30", 14, 30)]
    [InlineData("9:05", 9, 5)]
    public void UnderstandsTimes(string text, int hour, int minute)
    {
        Assert.Equal(new TimeOnly(hour, minute), EventInput.ParseTime(text));
    }

    [Theory]
    [InlineData("24:00")]
    [InlineData("12:60")]
    [InlineData("meio dia")]
    public void RejectsInvalidTimes(string text)
    {
        Assert.Null(EventInput.ParseTime(text));
    }

    [Theory]
    [InlineData("", "25/12", "14", "15", false)]
    [InlineData("Reunião", "ontem", "14", "15", false)]
    [InlineData("Reunião", "25/12", "x", "15", false)]
    [InlineData("Reunião", "25/12", "14", "x", false)]
    [InlineData("Reunião", "25/12", "15", "15", false)]
    [InlineData("Reunião", "25/12", "16", "15", false)]
    public void ExplainsWhatToFix(string title, string date, string start, string end, bool allDay)
    {
        var (draft, error) = EventInput.Read(title, date, start, end, allDay, "", Today);

        Assert.Null(draft);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void AllDayIgnoresTimeFields()
    {
        var (draft, error) = EventInput.Read(" Feriado ", "25/12", "x", "y", true, "  ", Today);

        Assert.Null(error);
        Assert.Equal(new EventDraft("Feriado", new DateOnly(2026, 12, 25), null, null, null), draft);
    }

    [Theory]
    [InlineData(10, 0, 10, 30)]
    [InlineData(10, 29, 10, 30)]
    [InlineData(10, 30, 11, 0)]
    [InlineData(23, 45, 0, 0)]
    public void SuggestsTheNextHalfHour(int hour, int minute, int expectedHour, int expectedMinute)
    {
        var start = EventInput.SuggestStart(new DateTime(2026, 11, 18, hour, minute, 12));

        Assert.Equal((expectedHour, expectedMinute, 0), (start.Hour, start.Minute, start.Second));
    }

    [Fact]
    public void BuildsTimedEventInUtc()
    {
        var draft = new EventDraft("Reunião", new DateOnly(2026, 11, 20), new TimeOnly(14, 30), new TimeOnly(15, 30), "Sala 1");

        var ics = IcsBuilder.Build(draft, "abc@teste", new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc), SaoPaulo);

        Assert.Contains("UID:abc@teste\r\n", ics);
        Assert.Contains("DTSTAMP:20261118T120000Z\r\n", ics);
        Assert.Contains("DTSTART:20261120T173000Z\r\n", ics);
        Assert.Contains("DTEND:20261120T183000Z\r\n", ics);
        Assert.Contains("SUMMARY:Reunião\r\n", ics);
        Assert.Contains("LOCATION:Sala 1\r\n", ics);
        Assert.EndsWith("END:VEVENT\r\nEND:VCALENDAR\r\n", ics);
    }

    [Fact]
    public void AllDayEventEndsOnTheNextDay()
    {
        var draft = new EventDraft("Virada", new DateOnly(2026, 12, 31), null, null, null);

        var ics = IcsBuilder.Build(draft, "x", DateTime.UtcNow, SaoPaulo);

        Assert.Contains("DTSTART;VALUE=DATE:20261231\r\n", ics);
        Assert.Contains("DTEND;VALUE=DATE:20270101\r\n", ics);
        Assert.DoesNotContain("LOCATION", ics);
    }

    [Fact]
    public void EscapesTextSoItCannotAddProperties()
    {
        var draft = new EventDraft("A;B,C\\D\r\nATTENDEE:x", new DateOnly(2026, 12, 1), null, null, null);

        var ics = IcsBuilder.Build(draft, "x", DateTime.UtcNow, SaoPaulo);

        Assert.Contains(@"SUMMARY:A\;B\,C\\D\nATTENDEE:x", ics);
        Assert.DoesNotContain("\r\nATTENDEE", ics);
    }

    [Fact]
    public void FoldsLongLinesWithoutBreakingAccents()
    {
        var draft = new EventDraft(new string('ç', 120), new DateOnly(2026, 12, 1), null, null, null);

        var ics = IcsBuilder.Build(draft, "x", DateTime.UtcNow, SaoPaulo);

        var lines = ics.Split("\r\n");
        Assert.All(lines, line => Assert.True(Encoding.UTF8.GetByteCount(line) <= 75));
        Assert.Contains("SUMMARY:" + new string('ç', 120), ics.Replace("\r\n ", ""));
    }

    [Theory]
    [InlineData("http://100.64.0.1:8090", true)]
    [InlineData("http://192.168.0.10", true)]
    [InlineData("http://10.0.0.5:8080", true)]
    [InlineData("http://172.20.1.1", true)]
    [InlineData("http://localhost:8080", true)]
    [InlineData("http://servidor", true)]
    [InlineData("http://nuvem.tail1234.ts.net", true)]
    [InlineData("https://nuvem.exemplo.com", true)]
    [InlineData("http://nuvem.exemplo.com", false)]
    [InlineData("http://8.8.8.8", false)]
    [InlineData("http://100.128.0.1", false)]
    [InlineData("http://172.32.0.1", false)]
    [InlineData("https://usuario:senha@nuvem.exemplo.com", false)]
    [InlineData("ftp://192.168.0.10", false)]
    [InlineData("nuvem", false)]
    [InlineData("", false)]
    public void PasswordOnlyTravelsOverSafeAddresses(string server, bool expected)
    {
        Assert.Equal(expected, ConfigStore.IsAllowedNextcloudServer(server));
    }

    private static readonly EventDraft Timed = new("Reunião & café", new DateOnly(2026, 11, 20), new TimeOnly(14, 30), new TimeOnly(15, 30), "Sala 1");
    private static readonly EventDraft AllDay = new("Feriado", new DateOnly(2026, 12, 31), null, null, null);

    [Fact]
    public void GoogleLinkCarriesTheEventInUtc()
    {
        var link = CalendarLinks.Google(Timed, SaoPaulo);

        Assert.Equal("calendar.google.com", link.Host);
        Assert.Equal(
            "https://calendar.google.com/calendar/render?action=TEMPLATE&text=Reuni%C3%A3o%20%26%20caf%C3%A9&dates=20261120T173000Z%2F20261120T183000Z&location=Sala%201",
            link.AbsoluteUri);
    }

    [Fact]
    public void GoogleAllDayEventEndsOnTheNextDay()
    {
        var link = CalendarLinks.Google(AllDay, SaoPaulo);

        Assert.Contains("dates=20261231%2F20270101", link.AbsoluteUri);
        Assert.DoesNotContain("location", link.AbsoluteUri);
    }

    [Fact]
    public void OutlookLinkCarriesTheEventInUtc()
    {
        var link = CalendarLinks.Outlook(Timed, SaoPaulo);

        Assert.Equal("outlook.live.com", link.Host);
        Assert.Contains("rru=addevent", link.AbsoluteUri);
        Assert.Contains("subject=Reuni%C3%A3o%20%26%20caf%C3%A9", link.AbsoluteUri);
        Assert.Contains("startdt=2026-11-20T17%3A30%3A00Z", link.AbsoluteUri);
        Assert.Contains("enddt=2026-11-20T18%3A30%3A00Z", link.AbsoluteUri);
        Assert.DoesNotContain("allday", link.AbsoluteUri);
    }

    [Fact]
    public void OutlookAllDayEventIsMarkedAsAllDay()
    {
        var link = CalendarLinks.Outlook(AllDay, SaoPaulo);

        Assert.Contains("startdt=2026-12-31", link.AbsoluteUri);
        Assert.Contains("enddt=2027-01-01", link.AbsoluteUri);
        Assert.Contains("allday=true", link.AbsoluteUri);
    }

    [Fact]
    public void TextTypedByTheUserCannotAddLinkParameters()
    {
        var draft = new EventDraft("a&action=outro#x", new DateOnly(2026, 11, 20), null, null, "b&c=d");

        var query = CalendarLinks.Google(draft, SaoPaulo).Query.TrimStart('?');

        Assert.Equal(1, query.Split('&').Count(part => part.StartsWith("action=", StringComparison.Ordinal)));
        Assert.Contains("text=a%26action%3Doutro%23x", query);
    }

    [Fact]
    public void RemembersTheLastDestination()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AcoesRapidasTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "destino");
        try
        {
            Assert.Equal(EventDestination.Google, EventDestinationStore.Load(path));

            EventDestinationStore.Save(path, EventDestination.Nextcloud);
            Assert.Equal(EventDestination.Nextcloud, EventDestinationStore.Load(path));

            File.WriteAllText(path, "99");
            Assert.Equal(EventDestination.Google, EventDestinationStore.Load(path));
            Assert.Equal(EventDestination.Nextcloud, EventDestinationStore.Load(path, EventDestination.Nextcloud));

            // Uma escolha já feita vale mais que o padrão.
            EventDestinationStore.Save(path, EventDestination.Outlook);
            Assert.Equal(EventDestination.Outlook, EventDestinationStore.Load(path, EventDestination.Nextcloud));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void EventActionWorksWithoutANextcloudServer()
    {
        const string Other = """{ "label": "B", "type": "toast", "target": "x" }""";

        Assert.Equal(2, ConfigStore.Parse($$"""{ "actions": [ { "label": "Evento", "type": "event" }, {{Other}} ] }""").Actions.Count);
        Assert.Equal(2, ConfigStore.Parse($$"""{ "actions": [ { "label": "Evento", "type": "event", "target": "  " }, {{Other}} ] }""").Actions.Count);
    }

    [Fact]
    public void EventActionNeedsAValidServer()
    {
        const string Other = """{ "label": "B", "type": "toast", "target": "x" }""";

        var config = ConfigStore.Parse($$"""{ "actions": [ { "label": "Evento", "type": "event", "target": "{{Server}}" }, {{Other}} ] }""");
        Assert.Equal(ActionTypes.Event, config.Actions[0].Type);

        Assert.Throws<ConfigException>(() => ConfigStore.Parse(
            $$"""{ "actions": [ { "label": "Evento", "type": "event", "target": "http://nuvem.exemplo.com" }, {{Other}} ] }"""));
    }

    [Fact]
    public async Task LoginFlowUsesTheConfiguredServerAddress()
    {
        var handler = new FakeServer(request => request.RequestUri!.AbsolutePath switch
        {
            "/index.php/login/v2" => Json("""{ "poll": { "token": "t1", "endpoint": "http://localhost/login/v2/poll" }, "login": "http://localhost/login/v2/flow/abc" }"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var client = new NextcloudClient(Server, handler);

        var login = await client.StartLoginAsync(CancellationToken.None);

        Assert.Equal($"{Server}/login/v2/flow/abc", login.BrowserUrl.AbsoluteUri);
        Assert.Equal($"{Server}/login/v2/poll", login.PollUrl.AbsoluteUri);
        Assert.Null(await client.PollLoginAsync(login, CancellationToken.None));
    }

    [Fact]
    public async Task FinishedLoginReturnsTheAppPassword()
    {
        var handler = new FakeServer(_ => Json("""{ "server": "http://localhost", "loginName": "usuario", "appPassword": "abc" }"""));
        using var client = new NextcloudClient(Server, handler);

        var account = await client.PollLoginAsync(new NextcloudLogin(new Uri(Server), new Uri($"{Server}/login/v2/poll"), "t"), CancellationToken.None);

        Assert.Equal(new NextcloudAccount(Server, "usuario", "abc"), account);
        Assert.Contains("token=t", handler.Bodies[0]);
    }

    [Fact]
    public async Task ListsOnlyCalendarsThatAcceptNewEvents()
    {
        using var client = new NextcloudClient(Server, CalDavServer());

        var calendars = await client.ListCalendarsAsync(Account, CancellationToken.None);

        var calendar = Assert.Single(calendars);
        Assert.Equal(new NextcloudCalendar("Pessoal", "/remote.php/dav/calendars/usuario/personal/"), calendar);
    }

    [Fact]
    public async Task RefusesCalendarAddressOutsideTheServer()
    {
        var handler = new FakeServer(_ => MultiStatus("""
            <d:response><d:propstat><d:prop><d:current-user-principal><d:href>http://outro.exemplo.com/roubo/</d:href></d:current-user-principal></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
            """));
        using var client = new NextcloudClient(Server, handler);

        await Assert.ThrowsAsync<NextcloudException>(() => client.ListCalendarsAsync(Account, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task CreatesEventWithoutOverwritingAnything()
    {
        var handler = new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.Created));
        using var client = new NextcloudClient(Server, handler);

        await client.CreateEventAsync(Account, "/remote.php/dav/calendars/usuario/personal/", "abc@acoesrapidas", "BEGIN:VCALENDAR", CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal($"{Server}/remote.php/dav/calendars/usuario/personal/abc%40acoesrapidas.ics", request.RequestUri!.AbsoluteUri);
        Assert.Equal("*", request.Headers.IfNoneMatch.Single().Tag);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("text/calendar", request.Content!.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task RepeatedEventCountsAsCreated()
    {
        using var client = new NextcloudClient(Server, new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.PreconditionFailed)));

        await client.CreateEventAsync(Account, "/cal/", "abc", "x", CancellationToken.None);
    }

    [Fact]
    public async Task RejectedPasswordAsksToConnectAgain()
    {
        using var client = new NextcloudClient(Server, new FakeServer(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        var error = await Assert.ThrowsAsync<NextcloudException>(
            () => client.CreateEventAsync(Account, "/cal/", "abc", "x", CancellationToken.None));

        Assert.True(error.IsUnauthorized);
    }

    [Fact]
    public async Task OfflineServerGivesAFriendlyMessage()
    {
        using var client = new NextcloudClient(Server, new FakeServer(_ => throw new HttpRequestException("falhou")));

        var error = await Assert.ThrowsAsync<NextcloudException>(
            () => client.CreateEventAsync(Account, "/cal/", "abc", "x", CancellationToken.None));

        Assert.Contains("Não foi possível falar", error.Message);
    }

    [Fact]
    public void AccountIsStoredEncryptedAndTiedToTheServer()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AcoesRapidasTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "nextcloud.dat");
        try
        {
            var store = new NextcloudAccountStore(path);
            Assert.Null(store.Load(Server));

            store.Save(Account);

            Assert.DoesNotContain("senha-de-app", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
            Assert.Equal(Account, store.Load(Server + "/"));
            Assert.Null(store.Load("http://192.168.0.2"));

            File.WriteAllText(path, "corrompido");
            Assert.Null(store.Load(Server));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static FakeServer CalDavServer() => new(request => request.RequestUri!.AbsolutePath switch
    {
        "/remote.php/dav/" => MultiStatus("""
            <d:response><d:propstat><d:prop><d:current-user-principal><d:href>/remote.php/dav/principals/users/usuario/</d:href></d:current-user-principal></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
            """),
        "/remote.php/dav/principals/users/usuario/" => MultiStatus("""
            <d:response><d:propstat><d:prop><c:calendar-home-set><d:href>/remote.php/dav/calendars/usuario/</d:href></c:calendar-home-set></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
            """),
        _ => MultiStatus($"""
            {Calendar("personal", "Pessoal", "VEVENT", "<d:write/>")}
            {Calendar("tarefas", "Tarefas", "VTODO", "<d:write/>")}
            {Calendar("compartilhado", "Só leitura", "VEVENT", "<d:read/>")}
            <d:response><d:href>/remote.php/dav/calendars/usuario/</d:href><d:propstat><d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
            """),
    });

    private static string Calendar(string id, string name, string component, string privilege) => $"""
        <d:response>
          <d:href>/remote.php/dav/calendars/usuario/{id}/</d:href>
          <d:propstat>
            <d:prop>
              <d:displayname>{name}</d:displayname>
              <d:resourcetype><d:collection/><c:calendar/></d:resourcetype>
              <d:current-user-privilege-set><d:privilege>{privilege}</d:privilege></d:current-user-privilege-set>
              <c:supported-calendar-component-set><c:comp name="{component}"/></c:supported-calendar-component-set>
            </d:prop>
            <d:status>HTTP/1.1 200 OK</d:status>
          </d:propstat>
        </d:response>
        """;

    private static HttpResponseMessage MultiStatus(string responses) => new(HttpStatusCode.MultiStatus)
    {
        Content = new StringContent(
            $"""<?xml version="1.0"?><d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">{responses}</d:multistatus>""",
            Encoding.UTF8,
            "application/xml"),
    };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class FakeServer(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }
}
