using MudBlazor.Services;
using WattsUp.BackgroundServices;
using WattsUp.Components;
using WattsUp.Data;
using WattsUp.Data.Repositories;
using WattsUp.Middleware;
using WattsUp.Services.Diagnostics;
using WattsUp.Services.Eloverblik;
using WattsUp.Services.EnergiDataService;
using WattsUp.Services.Mqtt;
using WattsUp.Services.Pricing;
using WattsUp.Services.Settings;
using WattsUp.Services.Tariffs;

var builder = WebApplication.CreateBuilder(args);

// --- Add-on options (secrets), read directly from /data/options.json — no bashio needed. ---
using var bootstrapLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
var addonOptions = AddonOptionsLoader.Load(bootstrapLoggerFactory.CreateLogger("Startup"));
builder.Services.AddSingleton(addonOptions);

// --- Data layer ---
builder.Services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
builder.Services.AddSingleton<DatabaseMigrator>();
builder.Services.AddSingleton<ISettingsRepository, SettingsRepository>();
builder.Services.AddSingleton<ISpotPriceRepository, SpotPriceRepository>();
builder.Services.AddSingleton<ITariffRepository, TariffRepository>();
builder.Services.AddSingleton<INationwideChargeSeedRepository, NationwideChargeSeedRepository>();
builder.Services.AddSingleton<IMeteringPointRepository, MeteringPointRepository>();
builder.Services.AddSingleton<IConsumptionRepository, ConsumptionRepository>();

// --- Diagnostics ---
builder.Services.AddSingleton<DiagnosticsStatusService>();

// --- Domain services ---
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<ITariffResolutionService, TariffResolutionService>();
builder.Services.AddSingleton<IPriceCalculationService, PriceCalculationService>();

// --- External HTTP clients, all resilient (retry + circuit breaker) via Polly v8. ---
builder.Services.AddHttpClient<IEnergiDataServiceClient, EnergiDataServiceClient>(client =>
{
    client.BaseAddress = new Uri("https://api.energidataservice.dk/");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddStandardResilienceHandler();

builder.Services.AddHttpClient<IEloverblikClient, EloverblikClient>(client =>
{
    client.BaseAddress = new Uri("https://api.eloverblik.dk/CustomerApi/");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddStandardResilienceHandler();

builder.Services.AddHttpClient<ISupervisorApiClient, SupervisorApiClient>(client =>
{
    client.BaseAddress = new Uri("http://supervisor/");
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddStandardResilienceHandler();

// --- MQTT ---
builder.Services.AddSingleton<IMqttBrokerResolver, SupervisorMqttDiscoveryService>();
builder.Services.AddSingleton<MqttPublisherService>();
builder.Services.AddSingleton<IMqttPublisherService>(sp => sp.GetRequiredService<MqttPublisherService>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<MqttPublisherService>());

// --- Background pollers ---
builder.Services.AddHostedService<SpotPricePollingService>();
builder.Services.AddHostedService<TariffPollingService>();
builder.Services.AddHostedService<EloverblikConsumptionPollingService>();

// --- Web UI ---
builder.Services.AddMudServices();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Apply SQLite migrations before anything reads the database.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().Migrate();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// Ingress handling must run first, before routing: reject non-ingress callers, then rewrite
// PathBase from HA's per-request X-Ingress-Path header.
app.UseMiddleware<IngressRemoteIpFilterMiddleware>();
app.UseMiddleware<IngressPathBaseMiddleware>();

// Explicit UseRouting() is required here: without it, ASP.NET Core's minimal hosting model
// inserts endpoint matching implicitly at the START of the pipeline (before any app.Use*()
// middleware, including the two above), so it would always match against the raw, un-rewritten
// request path — silently 404ing every real ingress request (which arrives with the full
// "/api/hassio_ingress/<token>/..." prefix still in the path) while only ever matching against
// the PathBase-stripped path in incomplete local tests that don't send a genuinely prefixed URL.
app.UseRouting();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// Exposes the top-level Program for WebApplicationFactory<Program>-based integration tests.
public partial class Program;
