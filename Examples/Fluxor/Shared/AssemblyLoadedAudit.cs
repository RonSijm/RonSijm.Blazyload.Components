using Fluxor;
using Microsoft.Extensions.Logging;
using RonSijm.Blazyload;

namespace RonSijm.Demo.Fluxor.Diagnostics;

public sealed class AssemblyLoadedAudit(ILogger<AssemblyLoadedAudit> logger)
{
    [EffectMethod]
    public Task RecordLoadedAssembly(AssemblyLoaded action, IDispatcher dispatcher)
    {
        logger.LogInformation("Lazy assembly {AssemblyName} finished registration in the running application", action.LoadedAssembly.GetName().Name);
        return Task.CompletedTask;
    }
}
