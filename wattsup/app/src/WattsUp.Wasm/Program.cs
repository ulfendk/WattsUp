using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.JSInterop;
using MudBlazor.Services;
using WattsUp.BackgroundServices;
using WattsUp.Data.Repositories;
using WattsUp.Services;
using WattsUp.Services.Consumption;
using WattsUp.Services.Diagnostics;
using WattsUp.Services.Eloverblik;
using WattsUp.Services.EnergiDataService;
using WattsUp.Services.Homeassistant;
using WattsUp.Services.Mqtt;
using WattsUp.Services.Pricing;
using WattsUp.Services.Settings;
using WattsUp.Services.Tariffs;
using WattsUp.Wasm.Components;
using WattsUp.Wasm.Data.Repositories;
using WattsUp.Wasm.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// --- Secrets, entered on Secrets.razor and persisted to localStorage — see BrowserAddonOptionsStore.
// A mutable singleton (not replaced, only mutated in place) so a save takes effect for every
// already-injected consumer (e.g. EloverblikClient) immediately, no reload needed. There is no
// /data/options.json here — this build has no server at all. ---
var addonOptions = new AddonOptions();
builder.Services.AddSingleton(addonOptions);

// --- Host identity + browser-backed cross-cutting concerns ---
builder.Services.AddSingleton<IRuntimeInfo, BrowserRuntimeInfo>();
builder.Services.AddSingleton<IThemePreferenceStore, BrowserLocalStorageThemePreferenceStore>();
builder.Services.AddSingleton<IPwaInstallService, BrowserPwaInstallService>();

// --- Data layer: IndexedDB instead of SQLite (Microsoft.Data.Sqlite is native, WASM can't load it) ---
builder.Services.AddSingleton<ISpotPriceRepository, IndexedDbSpotPriceRepository>();
builder.Services.AddSingleton<ITariffRepository, IndexedDbTariffRepository>();
builder.Services.AddSingleton<INationwideChargeSeedRepository, IndexedDbNationwideChargeSeedRepository>();
builder.Services.AddSingleton<IMeteringPointRepository, IndexedDbMeteringPointRepository>();
builder.Services.AddSingleton<IConsumptionRepository, IndexedDbConsumptionRepository>();
builder.Services.AddSingleton<IElafgiftAllowanceRepository, IndexedDbElafgiftAllowanceRepository>();
builder.Services.AddSingleton<ISettingsRepository, IndexedDbSettingsRepository>();

// --- No Home Assistant integration in this build (no Supervisor, no broker reachable from a
// browser tab) — no-op stubs so the shared UI/domain services don't need host-specific branches. ---
builder.Services.AddSingleton<IConsumptionDeviceRepository, NullConsumptionDeviceRepository>();
builder.Services.AddSingleton<IDeviceHourlyConsumptionRepository, NullDeviceHourlyConsumptionRepository>();
builder.Services.AddSingleton<IHomeAssistantApiClient, NullHomeAssistantApiClient>();
builder.Services.AddSingleton<IMqttPublisherService, NullMqttPublisherService>();
builder.Services.AddSingleton<IMqttBrokerResolver, NullMqttBrokerResolver>();

// --- Diagnostics ---
builder.Services.AddSingleton<DiagnosticsStatusService>();

// --- Domain services (identical to the Server host) ---
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<ITariffResolutionService, TariffResolutionService>();
builder.Services.AddSingleton<IPriceCalculationService, PriceCalculationService>();
builder.Services.AddSingleton<IDeviceCostService, DeviceCostService>();
builder.Services.AddSingleton<IPriceTrendService, PriceTrendService>();

// --- api.energidataservice.dk does not send CORS headers (confirmed against the live API, not
// assumed) — a browser can never call it directly. StaticSnapshotEnergiDataServiceClient reads a
// same-origin JSON snapshot instead (see its own doc comment + .github/scripts/fetch_price_snapshot.py). ---
builder.Services.AddHttpClient<IEnergiDataServiceClient, StaticSnapshotEnergiDataServiceClient>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

// --- Eloverblik's CORS support for browser-originated calls is unverified (unlike EnergiDataService
// above, which is confirmed unsupported) — Settings.razor's load handler catches and surfaces a
// failure gracefully either way, so this still attempts the real API directly. ---
builder.Services.AddHttpClient<IEloverblikClient, EloverblikClient>(client =>
{
    client.BaseAddress = new Uri("https://api.eloverblik.dk/CustomerApi/");
    client.Timeout = TimeSpan.FromSeconds(30);
}).AddStandardResilienceHandler();

// --- Background pollers (no MQTT republish, no device polling — see the Null* stubs above).
// Registered as their own singleton (not just AddHostedService<T>, which only exposes them via
// the IHostedService collection) so IDataRefreshCoordinator below can call PollOnceAsync() on the
// very same instances the loop runs on, for the manual "Refresh" button. ---
builder.Services.AddSingleton<SpotPricePollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SpotPricePollingService>());
builder.Services.AddSingleton<TariffPollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TariffPollingService>());
builder.Services.AddSingleton<EloverblikConsumptionPollingService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<EloverblikConsumptionPollingService>());
builder.Services.AddSingleton<IDataRefreshCoordinator, DataRefreshCoordinator>();

// --- Web UI ---
builder.Services.AddMudServices();

var host = builder.Build();

// Open/upgrade IndexedDB and load any saved secrets before the first render, so the singleton
// AddonOptions and repositories are already populated when Dashboard/Settings mount.
var js = host.Services.GetRequiredService<IJSRuntime>();
await js.InvokeVoidAsync("wattsUpDb.open");
await BrowserAddonOptionsStore.LoadIntoAsync(js, addonOptions);

// Unlike the generic .NET Host, WebAssemblyHost.RunAsync() does NOT start registered
// IHostedService/BackgroundService instances on its own (it only starts the renderer) — every
// AddHostedService<T>() above needs an explicit StartAsync here, or the three pollers above never
// run at all (confirmed: without this, every Diagnostics poller badge stays "Last update: never"
// forever, with no exception/warning anywhere, since nothing ever called them in the first place).
foreach (var hostedService in host.Services.GetServices<IHostedService>())
{
    await hostedService.StartAsync(CancellationToken.None);
}

await host.RunAsync();
