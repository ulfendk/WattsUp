using Microsoft.JSInterop;
using WattsUp.Services;

namespace WattsUp.Wasm.Services;

/// <summary>WASM-host <see cref="IThemePreferenceStore"/>, backed by plain <c>localStorage</c> via
/// theme.js's getDarkMode/setDarkMode — nothing to encrypt against here, unlike the Server host's
/// ProtectedLocalStorage (the browser sandbox is already the trust boundary).</summary>
public sealed class BrowserLocalStorageThemePreferenceStore(IJSRuntime js) : IThemePreferenceStore
{
    public async Task<bool?> TryGetDarkModeAsync() => await js.InvokeAsync<bool?>("themeInterop.getDarkMode");

    public async Task SetDarkModeAsync(bool isDarkMode) => await js.InvokeVoidAsync("themeInterop.setDarkMode", isDarkMode);
}
