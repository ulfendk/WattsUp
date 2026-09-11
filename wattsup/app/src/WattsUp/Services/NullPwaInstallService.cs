namespace WattsUp.Services;

/// <summary>Server/add-on build stub — never installable as a PWA, so MainLayout's install button
/// simply never appears (<see cref="IsInstallAvailable"/> is always false).</summary>
public sealed class NullPwaInstallService : IPwaInstallService
{
    public event Action? AvailabilityChanged { add { } remove { } }

    public bool IsInstallAvailable => false;

    public Task InitializeAsync() => Task.CompletedTask;

    public Task PromptInstallAsync() => Task.CompletedTask;
}
