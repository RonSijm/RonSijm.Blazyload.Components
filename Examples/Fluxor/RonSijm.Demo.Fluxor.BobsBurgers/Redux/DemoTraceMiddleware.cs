using System.Reflection;
using Fluxor;
using Microsoft.AspNetCore.Components;
using RonSijm.Blazyload;
using RonSijm.Syringe.Models;

namespace RonSijm.Demo.Fluxor.BobsBurgers.Redux;

public sealed class DemoTraceMiddleware : Middleware
{
    [Inject] public IDispatcher Dispatcher { get; set; } = null!;

    public override void AfterDispatch(object action)
    {
        if (action is ActionObserved || action.GetType().IsDefined(typeof(FeatureStateAttribute), inherit: false))
        {
            return;
        }

        string description;
        if (action is PageEvent lifecycle)
        {
            description = $"PageEvent: {lifecycle.Source.Name} / {lifecycle.Type}";
        }
        else if (action is AssemblyLoaded loaded)
        {
            description = $"AssemblyLoaded: {loaded.LoadedAssembly.GetName().Name}";
        }
        else if (action is List<Assembly>)
        {
            description = "Loaded assembly list: Blazyload.Fluxor result";
        }
        else if (action is LoadAssembly or LoadAssemblyForPath || action.GetType().Namespace?.StartsWith("RonSijm.Demo.Fluxor.", StringComparison.Ordinal) == true)
        {
            description = action.GetType().Name;
        }
        else
        {
            return;
        }

        Dispatcher.Dispatch(new ActionObserved(description));
    }
}
