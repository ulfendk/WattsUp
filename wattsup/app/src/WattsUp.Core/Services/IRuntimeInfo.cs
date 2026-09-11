namespace WattsUp.Services;

/// <summary>
/// Tells shared UI which host it's currently rendered by, so a handful of spots (the Secrets nav
/// link, the Eloverblik "how to configure a token" message) can say the right thing without the
/// shared Razor Class Library needing to know about either host's specifics.
/// </summary>
public interface IRuntimeInfo
{
    /// <summary>True under the public WASM/GitHub Pages build; false under the Home Assistant
    /// add-on (Blazor Server) build.</summary>
    bool IsBrowserHosted { get; }
}
