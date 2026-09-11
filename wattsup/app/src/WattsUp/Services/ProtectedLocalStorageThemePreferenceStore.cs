using System.Security.Cryptography;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace WattsUp.Services;

/// <summary>
/// Server-host <see cref="IThemePreferenceStore"/>, backed by <c>ProtectedLocalStorage</c> — see
/// <c>Program.cs</c> for the Data Protection key persistence under <c>/data</c> that makes this
/// survive container restarts/upgrades.
/// </summary>
public sealed class ProtectedLocalStorageThemePreferenceStore(ProtectedLocalStorage protectedLocalStorage)
    : IThemePreferenceStore
{
    private const string DarkModeStorageKey = "wattsup.darkMode";

    // ProtectedLocalStorage decrypts with the app's current Data Protection key ring - a value
    // written by an earlier key (e.g. the ring was regenerated, or /data was wiped) throws
    // CryptographicException instead of just "not found", which otherwise crashes the whole
    // circuit here. Falling back to the system preference is a much better failure mode than a
    // dead page.
    public async Task<bool?> TryGetDarkModeAsync()
    {
        try
        {
            var stored = await protectedLocalStorage.GetAsync<bool>(DarkModeStorageKey);
            return stored.Success ? stored.Value : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public async Task SetDarkModeAsync(bool isDarkMode) =>
        await protectedLocalStorage.SetAsync(DarkModeStorageKey, isDarkMode);
}
