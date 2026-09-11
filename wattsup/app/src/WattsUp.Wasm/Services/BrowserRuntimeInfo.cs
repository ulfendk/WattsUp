using WattsUp.Services;

namespace WattsUp.Wasm.Services;

public sealed class BrowserRuntimeInfo : IRuntimeInfo
{
    public bool IsBrowserHosted => true;
}
