namespace WattsUp.Services;

/// <summary>
/// Wraps the browser's native install prompt (the `beforeinstallprompt`/`appinstalled` events),
/// so MainLayout can offer an explicit "Install app" button instead of relying on visitors to
/// notice their browser's own — often well-hidden — install affordance. Always unavailable under
/// the Home Assistant add-on (Server) build, which is never installed as a standalone PWA.
/// </summary>
public interface IPwaInstallService
{
    /// <summary>Raised whenever <see cref="IsInstallAvailable"/> changes, so MainLayout knows to
    /// show/hide its button.</summary>
    event Action? AvailabilityChanged;

    /// <summary>True once the browser has fired `beforeinstallprompt` and the prompt hasn't been
    /// consumed (by a successful install) or superseded yet.</summary>
    bool IsInstallAvailable { get; }

    /// <summary>Wires up the underlying browser event listeners. Safe to call more than once —
    /// only the first call does anything.</summary>
    Task InitializeAsync();

    /// <summary>Shows the browser's native install prompt and waits for the visitor to accept or
    /// dismiss it. No-op if <see cref="IsInstallAvailable"/> is false.</summary>
    Task PromptInstallAsync();
}
