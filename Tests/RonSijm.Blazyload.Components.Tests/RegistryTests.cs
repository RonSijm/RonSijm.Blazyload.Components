using System.Reflection;
using System.Reflection.Emit;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using TestContext = Xunit.TestContext;

namespace RonSijm.Blazyload.Components.Tests;

public sealed class RegistryTests
{
    [Fact]
    public async Task ResolvesAlreadyLoadedComponentAndCachesManifest()
    {
        var descriptor = Describe("test.simple", typeof(SimpleComponent));
        var provider = Substitute.For<IBlazyComponentManifestProvider>();
        provider.GetComponentsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<BlazyComponentDescriptor>>([descriptor]));
        var loader = CreateLoader();
        using var context = CreateContext(provider, loader);
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();

        var first = await registry.ResolveAsync("test.simple", TestContext.Current.CancellationToken);
        var second = await registry.ResolveAsync("test.simple", TestContext.Current.CancellationToken);

        Assert.Same(typeof(SimpleComponent), first.ComponentType);
        Assert.Same(first, second);
        await provider.Received(1).GetComponentsAsync(Arg.Any<CancellationToken>());
        await loader.Received(1).LoadAssemblyAsync($"{descriptor.AssemblyName}.wasm");
    }

    [Fact]
    public async Task UnknownNamesAreCaseSensitive()
    {
        using var context = CreateContext([Describe("test.simple", typeof(SimpleComponent))], CreateLoader());
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();

        var exception = await Assert.ThrowsAsync<BlazyComponentNotFoundException>(async () => await registry.ResolveAsync("TEST.SIMPLE", TestContext.Current.CancellationToken));
        Assert.Contains("TEST.SIMPLE", exception.Message);
    }

    [Fact]
    public async Task DuplicateNamesReportBothImplementations()
    {
        using var context = CreateContext([new("duplicate", "First", "First.Calendar"), new("duplicate", "Second", "Second.Calendar")], CreateLoader());
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();

        var exception = await Assert.ThrowsAsync<BlazyComponentRegistrationException>(async () => await registry.ResolveAsync("duplicate", TestContext.Current.CancellationToken));
        Assert.Contains("First:First.Calendar", exception.Message);
        Assert.Contains("Second:Second.Calendar", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task RejectsEmptyNames(string name)
    {
        using var context = CreateContext([], CreateLoader());
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        await Assert.ThrowsAsync<ArgumentException>(async () => await registry.ResolveAsync(name, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InvalidManifestEntryIsRejected()
    {
        using var context = CreateContext([new("broken", "", "Type")], CreateLoader());
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        await Assert.ThrowsAsync<BlazyComponentRegistrationException>(async () => await registry.ResolveAsync("broken", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CoreDownloadFailureBecomesContextualFailure()
    {
        var loader = CreateLoader();
        var failure = new BlazyAssemblyLoadException("Missing.Component.Assembly.wasm", new FileNotFoundException("missing assembly"));
        loader.LoadAssemblyAsync("Missing.Component.Assembly.wasm").Returns(Task.FromException<List<Assembly>>(failure));
        using var context = CreateContext([new("missing.calendar", "Missing.Component.Assembly", "Missing.Calendar")], loader);

        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        var exception = await Assert.ThrowsAsync<BlazyComponentLoadException>(async () => await registry.ResolveAsync("missing.calendar", TestContext.Current.CancellationToken));
        Assert.Contains("missing.calendar", exception.Message);
        Assert.Contains("Missing.Component.Assembly", exception.Message);
        Assert.Same(failure, exception.InnerException);
    }

    [Fact]
    public async Task LoaderFailurePreservesInnerException()
    {
        var original = new InvalidOperationException("bootstrap failed");
        var failure = new BlazyAssemblyLoadException("Broken.Component.Assembly.wasm", original);
        var loader = CreateLoader();
        loader.LoadAssemblyAsync("Broken.Component.Assembly.wasm").Returns(Task.FromException<List<Assembly>>(failure));
        using var context = CreateContext([new("broken.calendar", "Broken.Component.Assembly", "Broken.Calendar")], loader);
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();

        var exception = await Assert.ThrowsAsync<BlazyComponentLoadException>(async () => await registry.ResolveAsync("broken.calendar", TestContext.Current.CancellationToken));
        Assert.Same(failure, exception.InnerException);
        Assert.Same(original, exception.InnerException!.InnerException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoaderResultMissingTheRequestedAssemblyIsContextual(bool includeAnotherAssembly)
    {
        var descriptor = new BlazyComponentDescriptor("test.missing", "Missing.Component.Assembly", "Missing.Component");
        var loader = CreateLoader();
        var assemblies = new List<Assembly>();
        if (includeAnotherAssembly)
        {
            assemblies.Add(typeof(object).Assembly);
        }

        loader.LoadAssemblyAsync($"{descriptor.AssemblyName}.wasm").Returns(Task.FromResult(assemblies));
        using var context = CreateContext([descriptor], loader);
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        var exception = await Assert.ThrowsAsync<BlazyComponentLoadException>(async () => await registry.ResolveAsync(descriptor.Name, TestContext.Current.CancellationToken));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains(descriptor.AssemblyName, exception.Message);
    }

    [Fact]
    public async Task ResolvesTheRequestedAssemblyRatherThanTheFirstResult()
    {
        var descriptor = Describe("test.simple", typeof(SimpleComponent));
        var loader = CreateLoader();
        loader.LoadAssemblyAsync($"{descriptor.AssemblyName}.wasm").Returns(Task.FromResult<List<Assembly>>([typeof(object).Assembly, typeof(SimpleComponent).Assembly]));
        using var context = CreateContext([descriptor], loader);
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        var resolved = await registry.ResolveAsync(descriptor.Name, TestContext.Current.CancellationToken);
        Assert.Same(typeof(SimpleComponent), resolved.ComponentType);
    }

    [Fact]
    public async Task ResolvesAnExistingAssemblyWhenNoNewAssembliesAreReturned()
    {
        var descriptor = Describe("test.simple", typeof(SimpleComponent));
        var loader = CreateLoader();
        loader.LoadAssemblyAsync($"{descriptor.AssemblyName}.wasm").Returns(Task.FromResult(new List<Assembly>()));
        using var context = CreateContext([descriptor], loader);
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        var resolved = await registry.ResolveAsync(descriptor.Name, TestContext.Current.CancellationToken);
        Assert.Same(typeof(SimpleComponent), resolved.ComponentType);
    }

    [Fact]
    public async Task MissingTypeIsContextual()
    {
        using var context = CreateContext([new("missing.type", typeof(SimpleComponent).Assembly.GetName().Name!, "No.Such.Type")], CreateLoader());
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();

        var exception = await Assert.ThrowsAsync<BlazyComponentTypeException>(async () => await registry.ResolveAsync("missing.type", TestContext.Current.CancellationToken));
        Assert.Contains("No.Such.Type", exception.Message);
    }

    [Theory]
    [InlineData(typeof(string))]
    [InlineData(typeof(AbstractComponent))]
    [InlineData(typeof(GenericComponent<>))]
    [InlineData(typeof(InternalComponent))]
    public async Task RejectsUnrenderableTypes(Type type)
    {
        using var context = CreateContext([Describe("invalid.type", type)], CreateLoader());
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        await Assert.ThrowsAsync<BlazyComponentTypeException>(async () => await registry.ResolveAsync("invalid.type", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsStaleAttributeName()
    {
        using var context = CreateContext([Describe("old.name", typeof(AnnotatedComponent))], CreateLoader());
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        var exception = await Assert.ThrowsAsync<BlazyComponentTypeException>(async () => await registry.ResolveAsync("old.name", TestContext.Current.CancellationToken));
        Assert.Contains("new.name", exception.Message);
    }

    [Fact]
    public async Task ConcurrentResolutionsShareEachComponentTask()
    {
        var assemblyName = $"LazyAssembly{Guid.NewGuid():N}";
        var loader = CreateLoader();
        var completion = new TaskCompletionSource<List<Assembly>>(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.LoadAssemblyAsync($"{assemblyName}.wasm").Returns(completion.Task);
        using var context = CreateContext([new("first", assemblyName, "First"), new("second", assemblyName, "Second")], loader);
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();

        var requests = Enumerable.Range(0, 20).Select(index => registry.ResolveAsync(index % 2 == 0 ? "first" : "second", TestContext.Current.CancellationToken).AsTask()).ToArray();
        var assembly = CreateAssembly(assemblyName, "First", "Second");
        completion.SetResult([assembly]);
        var results = await Task.WhenAll(requests);

        Assert.All(results, descriptor => Assert.NotNull(descriptor.ComponentType));
        await loader.Received(2).LoadAssemblyAsync($"{assemblyName}.wasm");
    }

    [Fact]
    public async Task CancelledWaiterDoesNotCancelSharedLoad()
    {
        var assemblyName = $"CancelAssembly{Guid.NewGuid():N}";
        var loader = CreateLoader();
        var completion = new TaskCompletionSource<List<Assembly>>(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.LoadAssemblyAsync($"{assemblyName}.wasm").Returns(completion.Task);
        using var context = CreateContext([new("shared", assemblyName, "Shared")], loader);
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        using var cancellation = new CancellationTokenSource();
        var cancelled = registry.ResolveAsync("shared", cancellation.Token).AsTask();
        var successful = registry.ResolveAsync("shared", TestContext.Current.CancellationToken).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        completion.SetResult([CreateAssembly(assemblyName, "Shared")]);
        Assert.NotNull((await successful).ComponentType);
        await loader.Received(1).LoadAssemblyAsync($"{assemblyName}.wasm");
    }

    [Fact]
    public async Task RegistriesUseTheConfiguredCoreLoader()
    {
        var assemblyName = $"ScopeAssembly{Guid.NewGuid():N}";
        var loader = CreateLoader();
        var completion = new TaskCompletionSource<List<Assembly>>(TaskCreationOptions.RunContinuationsAsynchronously);
        loader.LoadAssemblyAsync($"{assemblyName}.wasm").Returns(completion.Task);
        using var firstContext = CreateContext([new("shared", assemblyName, "Shared")], loader);
        using var secondContext = CreateContext([new("shared", assemblyName, "Shared")], loader);
        var firstRegistry = firstContext.Services.GetRequiredService<IBlazyComponentRegistry>();
        var secondRegistry = secondContext.Services.GetRequiredService<IBlazyComponentRegistry>();

        var first = firstRegistry.ResolveAsync("shared", TestContext.Current.CancellationToken).AsTask();
        var second = secondRegistry.ResolveAsync("shared", TestContext.Current.CancellationToken).AsTask();
        completion.SetResult([CreateAssembly(assemblyName, "Shared")]);
        await Task.WhenAll(first, second);

        await loader.Received(2).LoadAssemblyAsync($"{assemblyName}.wasm");
    }

    private static Assembly CreateAssembly(string name, params string[] types)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule(name);
        foreach (var type in types)
        {
            module.DefineType(type, TypeAttributes.Public, typeof(ComponentBase)).CreateType();
        }

        return assembly;
    }

    internal static BlazyComponentDescriptor Describe(string name, Type type) => new(name, type.Assembly.GetName().Name!, type.FullName!);

    internal static IBlazyAssemblyLoader CreateLoader()
    {
        var loader = Substitute.For<IBlazyAssemblyLoader>();
        loader.AdditionalAssemblies.Returns(new List<Assembly>());
        loader.LoadAssemblyAsync(Arg.Any<string>()).Returns(call =>
        {
            var name = Path.GetFileNameWithoutExtension(call.ArgAt<string>(0));
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate => candidate.GetName().Name == name);
            if (assembly is not null)
            {
                return Task.FromResult<List<Assembly>>([assembly]);
            }

            var failure = new BlazyAssemblyLoadException(name, new FileNotFoundException(name));
            return Task.FromException<List<Assembly>>(failure);
        });
        return loader;
    }

    internal static BunitContext CreateContext(IReadOnlyList<BlazyComponentDescriptor> entries, IBlazyAssemblyLoader loader)
    {
        var provider = Substitute.For<IBlazyComponentManifestProvider>();
        provider.GetComponentsAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(entries));
        return CreateContext(provider, loader);
    }

    private static BunitContext CreateContext(IBlazyComponentManifestProvider provider, IBlazyAssemblyLoader loader)
    {
        var context = new BunitContext();
        context.Services.AddSingleton(provider);
        context.Services.AddSingleton(loader);
        context.Services.AddBlazyloadComponents();
        return context;
    }
}

public class SimpleComponent : ComponentBase;
public abstract class AbstractComponent : ComponentBase;
public class GenericComponent<T> : ComponentBase;
internal class InternalComponent : ComponentBase;

[BlazyComponentAttribute("new.name")]
public class AnnotatedComponent : ComponentBase;
