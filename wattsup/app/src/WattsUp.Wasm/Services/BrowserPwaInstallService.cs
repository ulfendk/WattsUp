using Microsoft.JSInterop;
using WattsUp.Services;

namespace WattsUp.Wasm.Services;

/// <summary>Real implementation, backed by wwwroot/pwaInstallInterop.js — see IPwaInstallService
/// for what this is for.</summary>
public sealed class BrowserPwaInstallService(IJSRuntime js) : IPwaInstallService, IAsyncDisposable
{
    private DotNetObjectReference<BrowserPwaInstallService>? _selfReference;
    private bool _initialized;

    public event Action? AvailabilityChanged;

    public bool IsInstallAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _selfReference = DotNetObjectReference.Create(this);
        IsInstallAvailable = await js.InvokeAsync<bool>("wattsUpPwaInstall.init", _selfReference);
    }

    public async Task PromptInstallAsync()
    {
        if (!IsInstallAvailable)
        {
            return;
        }

        await js.InvokeVoidAsync("wattsUpPwaInstall.prompt");
    }

    [JSInvokable]
    public void OnAvailabilityChanged(bool available)
    {
        IsInstallAvailable = available;
        AvailabilityChanged?.Invoke();
    }

    public ValueTask DisposeAsync()
    {
        _selfReference?.Dispose();
        return ValueTask.CompletedTask;
    }
}
