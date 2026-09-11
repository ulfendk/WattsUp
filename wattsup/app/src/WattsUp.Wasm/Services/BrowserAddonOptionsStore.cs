using Microsoft.JSInterop;
using WattsUp.Services.Settings;

namespace WattsUp.Wasm.Services;

/// <summary>
/// Loads/saves the secrets a visitor enters on Secrets.razor (Eloverblik refresh token, Carnot API
/// key) to/from plain browser <c>localStorage</c> — see wwwroot/secretsInterop.js. There is no
/// server-side <c>options.json</c> in this build (see <c>AddonOptionsLoader</c>, which stays
/// Server-only); this is its WASM equivalent.
///
/// <see cref="AddonOptions"/> is a mutable class registered as a single DI singleton in both
/// hosts, so mutating its properties in place (rather than replacing the instance) means every
/// already-injected consumer (e.g. <c>EloverblikClient</c>, which reads them live on every call)
/// sees a saved change immediately — no reload needed.
/// </summary>
public static class BrowserAddonOptionsStore
{
    public static async Task LoadIntoAsync(IJSRuntime js, AddonOptions options)
    {
        var stored = await js.InvokeAsync<Dto?>("secretsInterop.get");
        if (stored is null)
        {
            return;
        }

        options.EloverblikRefreshToken = stored.EloverblikRefreshToken;
        options.CarnotApiKey = stored.CarnotApiKey;
    }

    public static async Task SaveAsync(IJSRuntime js, AddonOptions options) =>
        await js.InvokeVoidAsync("secretsInterop.save", new Dto(options.EloverblikRefreshToken, options.CarnotApiKey));

    private sealed record Dto(string EloverblikRefreshToken, string CarnotApiKey);
}
