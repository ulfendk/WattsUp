namespace WattsUp.Services;

/// <summary>
/// Persists the user's explicit dark-mode choice (the app-bar toggle) across visits. The Server
/// host backs this with <c>ProtectedLocalStorage</c> (needs the app's Data Protection keys); the
/// WASM host backs it with plain browser <c>localStorage</c> (nothing to encrypt against — the
/// browser sandbox is the trust boundary already).
/// </summary>
public interface IThemePreferenceStore
{
    /// <summary>The stored choice, or null if none has been made yet (caller should fall back to
    /// the browser's <c>prefers-color-scheme</c>).</summary>
    Task<bool?> TryGetDarkModeAsync();

    Task SetDarkModeAsync(bool isDarkMode);
}
