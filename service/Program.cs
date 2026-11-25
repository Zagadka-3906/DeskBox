using System.Net;
using System.Reflection;
using DeskBox.Models;
using DeskBox.Services;
using DormElectricityBridge;

const int port = 46382;
const string origin = "http://127.0.0.1:46382";
string? Arg(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, port);
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 8192;
});
var dataRoot = Arg("--data") ?? Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskBoxDormElectricity", "data");
var store = new BridgeStore(dataRoot, Arg("--import"));
var credentials = new PasswordVaultCredentialStore();
var electricity = new DormElectricityService();
var queries = new BridgeQueries(electricity);
var sender = new ServerChanService();
var edits = new SemaphoreSlim(1, 1);
bool preview = args.Contains("--no-push", StringComparer.Ordinal);
if (!preview)
{
    builder.Services.AddHostedService(_ => new PushWorker(store, credentials, queries, sender));
}
var app = builder.Build();
app.Use(async (request, next) =>
{
    bool write = !HttpMethods.IsGet(request.Request.Method) && !HttpMethods.IsHead(request.Request.Method);
    string suppliedOrigin = request.Request.Headers.Origin.ToString();
    string fetchSite = request.Request.Headers["Sec-Fetch-Site"].ToString();
    if (request.Request.Host.Value != $"127.0.0.1:{port}" ||
        (suppliedOrigin.Length > 0 && suppliedOrigin != origin) ||
        (fetchSite == "cross-site" && request.Request.Path != "/") ||
        (write && (suppliedOrigin != origin || request.Request.Headers["X-Dorm-Settings"] != "1" ||
            !request.Request.HasJsonContentType())))
    {
        request.Response.StatusCode = 403;
        return;
    }
    request.Response.Headers.CacheControl = "no-store";
    request.Response.Headers["X-Content-Type-Options"] = "nosniff";
    request.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    await next();
});

static IResult Resource(string name, string contentType)
{
    var assembly = Assembly.GetExecutingAssembly();
    return Results.Stream(assembly.GetManifestResourceStream($"DormElectricityBridge.{name}")!, contentType);
}

app.MapGet("/", () => Resource("Settings.html", "text/html; charset=utf-8"));
app.MapGet("/settings.js", () => Resource("Settings.js", "text/javascript; charset=utf-8"));
app.MapGet("/settings.css", () => Resource("Settings.css", "text/css; charset=utf-8"));
app.MapGet("/api/health", () => new { service = "宿舍用电", version = 1, preview });
app.MapGet("/api/snapshot", async (string? tab, int? page, CancellationToken token) =>
    await queries.GetViewAsync(store.Read().Settings, tab == "payments", page ?? 1, token));
app.MapGet("/api/config", async (CancellationToken token) =>
    new
    {
        settings = store.Read().Settings,
        keySaved = ServerChanService.IsValidSendKey(await credentials.GetSecretAsync(PushWorker.CredentialKey, token)),
        campuses = DormElectricityService.Campuses
    });

app.MapGet("/api/buildings", (string campus, CancellationToken token) =>
    CampusResult(() => electricity.GetBuildingsAsync(campus, token)));
app.MapGet("/api/floors", (string building, CancellationToken token) =>
    CampusResult(() => electricity.GetFloorsAsync(DormElectricityService.LihuPhaseTwoClient, building, token)));
app.MapGet("/api/rooms", (string building, string floor, CancellationToken token) =>
    CampusResult(() => electricity.GetRoomsAsync(DormElectricityService.LihuPhaseTwoClient, building, floor, token)));

app.MapPost("/api/config", async (SettingsDraft draft, CancellationToken token) =>
{
    if (draft.Settings is not { Location: { } location } settings ||
        !DormElectricityService.Campuses.Any(campus => campus.Client == location.Client) ||
        string.IsNullOrWhiteSpace(location.BuildingId) || string.IsNullOrWhiteSpace(location.RoomName) ||
        location.RoomName.Length > 80 || location.RoomName.Any(char.IsControl) ||
        settings.LowThreshold is < 0 or > 10000 ||
        !DormElectricityPeriods.Options.Contains(settings.UsagePeriod) ||
        !DormElectricityPeriods.Options.Contains(settings.PaymentPeriod))
        return Results.BadRequest(new { error = "请检查宿舍、时间范围与提醒阈值。" });
    string? key = draft.SendKey?.Trim();
    if (!string.IsNullOrEmpty(key) && !ServerChanService.IsValidSendKey(key))
        return Results.BadRequest(new { error = "请填写以 SCT 开头的 SendKey。" });

    await edits.WaitAsync(token);
    try
    {
        if (store.Read().Settings.Location != location)
        {
            await electricity.ValidateLocationAsync(location, token);
        }
        if (!preview)
        {
            if (draft.ClearKey) await credentials.RemoveSecretAsync(PushWorker.CredentialKey, token);
            else if (!string.IsNullOrEmpty(key))
                await credentials.SetSecretAsync(PushWorker.CredentialKey, key, token);
        }
        store.UpdateSettings(settings);
        return Results.Ok(new { message = "已保存。格子将在下次刷新时更新。" });
    }
    catch (Exception ex) when (!token.IsCancellationRequested &&
        ex is HttpRequestException or InvalidOperationException or ArgumentException or OperationCanceledException)
    {
        return Results.BadRequest(new { error = "无法验证宿舍，请检查校园网络和房间选择。" });
    }
    finally { edits.Release(); }
});

app.MapPost("/api/test-push", async (CancellationToken token) =>
{
    if (preview) return Results.BadRequest(new { error = "验证模式已暂停真实推送。" });
    if (!edits.Wait(0)) return Results.Conflict(new { error = "请稍后再试。" });
    try
    {
        string? key = await credentials.GetSecretAsync(PushWorker.CredentialKey, token);
        if (!ServerChanService.IsValidSendKey(key))
            return Results.BadRequest(new { error = "请先保存 SendKey。" });
        await sender.SendAsync(key!, "宿舍用电推送测试", "本机宿舍用电服务已连接 Server 酱。", token);
        return Results.Ok(new { message = "已提交测试消息，请在微信中查看。" });
    }
    catch (Exception) when (!token.IsCancellationRequested)
    {
        return Results.BadRequest(new { error = "发送失败，请检查 SendKey、微信通道和今日额度。" });
    }
    finally { edits.Release(); }
});

await app.RunAsync();

static async Task<IResult> CampusResult<T>(Func<Task<T>> fetch)
{
    try { return Results.Ok(await fetch()); }
    catch (ArgumentException) { return Results.BadRequest(new { error = "选择无效。" }); }
    catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
    {
        return Results.Json(new { error = "无法加载选项，请检查校园网络。" }, statusCode: 502);
    }
}

internal sealed record SettingsDraft(BridgeSettings? Settings, string? SendKey, bool ClearKey);
