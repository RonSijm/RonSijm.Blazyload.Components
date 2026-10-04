using Microsoft.JSInterop;

namespace RonSijm.Demo.Fluxor.BobsBurgers.Services;

public sealed class DemoBrowserInterop(IJSRuntime jsRuntime) : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _module = new(() => jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/RonSijm.Demo.Fluxor.BobsBurgers/demo.js").AsTask());

    public async Task<string> InitializeShellAsync(bool isDarkMode)
    {
        var module = await _module.Value;
        return await module.InvokeAsync<string>("initializeShell", isDarkMode);
    }

    public async Task ApplyThemeAsync(bool isDarkMode)
    {
        var module = await _module.Value;
        await module.InvokeVoidAsync("applyTheme", isDarkMode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module.IsValueCreated && !_module.Value.IsFaulted && !_module.Value.IsCanceled)
        {
            var module = await _module.Value;
            await module.DisposeAsync();
        }
    }
}
