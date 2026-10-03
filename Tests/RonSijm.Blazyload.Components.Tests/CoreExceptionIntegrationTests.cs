using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using RonSijm.Syringe;
using TestContext = Xunit.TestContext;

namespace RonSijm.Blazyload.Components.Tests;

public class CoreExceptionIntegrationTests()
{
    [Fact]
    public async Task RealCoreBootstrapFailureReachesComponentsAndIsCachedByRegistry()
    {
        var assemblyName = $"ComponentBootstrapFailure{Guid.NewGuid():N}";
        var bytes = CreateFeatureAssembly(assemblyName);
        using var portReservation = new TcpListener(IPAddress.Loopback, 0);
        portReservation.Start();
        var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
        portReservation.Stop();
        var baseUri = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(baseUri);
        listener.Start();
        var downloads = 0;
        var downloading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDownload = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var serve = Task.Run(async () =>
        {
            var request = await listener.GetContextAsync().WaitAsync(TestContext.Current.CancellationToken);
            Interlocked.Increment(ref downloads);
            downloading.SetResult();
            await releaseDownload.Task.WaitAsync(TestContext.Current.CancellationToken);
            var response = request.Response;
            response.ContentType = "application/octet-stream";
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, TestContext.Current.CancellationToken);
            response.Close();
        }, TestContext.Current.CancellationToken);

        var navigation = new FeatureNavigation();
        navigation.Configure(baseUri);
        var services = new ServiceCollection();
        services.AddSingleton<NavigationManager>(navigation);
        services.AddSingleton(Substitute.For<IJSRuntime>());
        var factory = new BlazyServiceProviderFactory(new BlazyloadProviderOptions());
        using var provider = (SyringeServiceProvider)factory.CreateServiceProvider(factory.CreateBuilder(services));
        var loader = provider.GetRequiredService<IBlazyAssemblyLoader>();
        Assert.Same(loader, provider.GetRequiredService<IAssemblyLoader>());
        using var context = new BunitContext();
        var manifest = Substitute.For<IBlazyComponentManifestProvider>();
        manifest.GetComponentsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BlazyComponentDescriptor>>([new("broken.calendar", assemblyName, $"{assemblyName}.Calendar")]));
        context.Services.AddSingleton(manifest);
        context.Services.AddSingleton(loader);
        context.Services.AddBlazyloadComponents();
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();

        var componentRequest = registry.ResolveAsync("broken.calendar", TestContext.Current.CancellationToken).AsTask();
        await downloading.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        releaseDownload.SetResult();
        var componentFailure = await Assert.ThrowsAsync<BlazyComponentLoadException>(() => componentRequest.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        var coreFailure = Assert.IsType<BlazyAssemblyLoadException>(componentFailure.InnerException);
        Assert.Contains(assemblyName, coreFailure.Message);
        Assert.Same(ThrowingComponentBootstrap.Failure, coreFailure.InnerException);
        Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == assemblyName);
        Assert.Empty(loader.AdditionalAssemblies);
        Assert.Same(componentFailure, await Assert.ThrowsAsync<BlazyComponentLoadException>(() => registry.ResolveAsync("broken.calendar", TestContext.Current.CancellationToken).AsTask()));
        provider.Build();
        Assert.Same(loader, provider.GetRequiredService<IBlazyAssemblyLoader>());
        Assert.Same(loader, provider.GetRequiredService<IAssemblyLoader>());
        await serve;
        Assert.Equal(1, downloads);
    }

    private static byte[] CreateFeatureAssembly(string name)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
        var module = assembly.DefineDynamicModule(name);
        var bootstrap = module.DefineType($"{name}.Properties.BlazyBootstrap", TypeAttributes.Public, typeof(ThrowingComponentBootstrap));
        bootstrap.DefineDefaultConstructor(MethodAttributes.Public);
        bootstrap.CreateType();
        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private sealed class FeatureNavigation() : NavigationManager
    {
        public void Configure(string baseUri) => Initialize(baseUri, baseUri);
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }
}

public class ThrowingComponentBootstrap() : IBootstrapper
{
    public static InvalidOperationException Failure { get; } = new("real feature bootstrap failed");
    public Task<IEnumerable<ServiceDescriptor>> Bootstrap() => Task.FromException<IEnumerable<ServiceDescriptor>>(Failure);
}
