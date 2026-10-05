using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Bunit;
using Fluxor;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using RonSijm.Demo.Fluxor.BobsBurgers.Components;
using RonSijm.Demo.Fluxor.BobsBurgers.Redux;
using RonSijm.Demo.Fluxor.Diagnostics;
using RonSijm.Demo.Fluxor.WonderWharf.Components;
using RonSijm.Demo.Fluxor.WonderWharf.Contracts;
using RonSijm.Demo.Fluxor.WonderWharf.Models;
using RonSijm.Demo.Fluxor.WonderWharf.Publications.Components;
using RonSijm.Demo.Fluxor.WonderWharf.Publications.Redux;
using RonSijm.Demo.Fluxor.WonderWharf.Publications.Services;
using RonSijm.Demo.Fluxor.WonderWharf.Redux;
using RonSijm.Demo.Fluxor.WonderWharf.Services;
using RonSijm.Syringe;
using RonSijm.Syringe.Models;
using WharfBootstrap = RonSijm.Demo.Fluxor.WonderWharf.Properties.BlazyBootstrap;
using TrackingBootstrap = RonSijm.Demo.Fluxor.WonderWharf.Publications.Properties.BlazyBootstrap;
using BobBootstrap = RonSijm.Demo.Fluxor.BobsBurgers.Properties.BlazyBootstrap;
using RestaurantDashboard = RonSijm.Demo.Fluxor.BobsBurgers.Components.RestaurantDashboard;
using TestContext = Xunit.TestContext;

namespace RonSijm.Blazyload.Components.Tests;

public sealed class FluxorTests
{
    [Fact]
    public async Task DynamicBootstrapAddsReducersEffectsAndMiddlewareToTheOriginalStore()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HttpClient(new EventHandler()) { BaseAddress = new Uri("https://localhost/") });
        await using var provider = new SyringeServiceProvider(services, options => options.UseFluxor(fluxor => fluxor.ScanTypes(typeof(StartupState))));
        var store = provider.GetRequiredService<IStore>();
        await store.InitializeAsync();
        Assert.False(store.Features.ContainsKey("Fluxor.WonderWharf"));
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var descriptors = await new WharfBootstrap().Bootstrap();
        IServiceCollection featureServices = new ServiceCollection();
        foreach (var descriptor in descriptors)
        {
            featureServices.Add(descriptor);
        }
        await provider.LoadServiceDescriptors(featureServices);
        provider.Build();

        Assert.Same(store, provider.GetRequiredService<IStore>());
        Assert.Same(dispatcher, provider.GetRequiredService<IDispatcher>());
        Assert.True(store.Features.ContainsKey("Fluxor.WonderWharf"));
        var state = provider.GetRequiredService<IState<WonderWharfViewModel>>();
        var effect = provider.GetRequiredService<LoadWharfEventsEffect>();
        Assert.Same(provider.GetRequiredService<WharfEventService>(), effect.Service);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChanged += (_, _) =>
        {
            if (state.Value.HasLoaded)
            {
                completed.TrySetResult();
            }
        };
        dispatcher.Dispatch(new PageEvent(PageEventType.OnInitialized, typeof(EventCalendar)));
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, state.Value.LoadCount);
        Assert.Equal("Fall festival", Assert.Single(state.Value.Events).Name);
        Assert.Equal(1, provider.GetRequiredService<WharfTraceMiddleware>().LoadActionsSeen);
        var eventId = state.Value.Events[0].Id;
        dispatcher.Dispatch(new SelectWharfEvent(eventId));
        Assert.Equal(eventId, state.Value.SelectedEventId);

        provider.Build();
        Assert.Same(store, provider.GetRequiredService<IStore>());
        Assert.Same(state, provider.GetRequiredService<IState<WonderWharfViewModel>>());
        dispatcher.Dispatch(new PageEvent(PageEventType.OnInitialized, typeof(EventCalendar)));
        Assert.Equal(1, state.Value.LoadCount);
        Assert.Equal(1, provider.GetRequiredService<WharfEventService>().RequestCount);

        await provider.LoadServiceDescriptors(featureServices);
        provider.Build();
        Assert.Same(store, provider.GetRequiredService<IStore>());
        Assert.Same(state, provider.GetRequiredService<IState<WonderWharfViewModel>>());
        Assert.Same(effect, provider.GetRequiredService<LoadWharfEventsEffect>());
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        state.StateChanged += (_, _) =>
        {
            if (state.Value.LoadCount >= 2)
            {
                refreshed.TrySetResult();
            }
        };
        dispatcher.Dispatch(new LoadWharfEvents());
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, state.Value.LoadCount);
        Assert.Equal(2, provider.GetRequiredService<WharfEventService>().RequestCount);
        Assert.Equal(2, provider.GetRequiredService<WharfTraceMiddleware>().LoadActionsSeen);
    }

    [Fact]
    public async Task EffectReportsFailureInsteadOfReturningSuccessfulEmptyEvents()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        using var httpClient = new HttpClient(new EventHandler());
        var effect = new LoadWharfEventsEffect
        {
            Service = new WharfEventService(httpClient),
            Logger = NullLogger<LoadWharfEventsEffect>.Instance
        };
        await effect.HandleAsync(new LoadWharfEvents(true), dispatcher);
        dispatcher.Received().Dispatch(Arg.Is<WharfEventsLoadFailed>(action => action.Message.Contains("Simulated event-service failure")));
        dispatcher.DidNotReceive().Dispatch(Arg.Any<WharfEventsLoaded>());
        Assert.Equal(1, effect.Service.RequestCount);
    }

    [Fact]
    public void FailureRetainsPriorEventsAndSelectionAndRefreshClearsTheError()
    {
        var item = new WharfEvent(Guid.NewGuid(), "Fall festival", new DateOnly(2026, 10, 10));
        var initial = new WonderWharfViewModel { HasLoaded = true, Events = [item], SelectedEventId = item.Id, LoadCount = 1 };
        var loading = WharfReducers.Loading(initial, new LoadWharfEvents(true));
        Assert.True(loading.IsLoading);
        var failed = WharfReducers.Failed(loading, new WharfEventsLoadFailed("Service unavailable"));
        Assert.False(failed.IsLoading);
        Assert.Equal("Service unavailable", failed.Error);
        Assert.Same(initial.Events, failed.Events);
        Assert.Equal(item.Id, failed.SelectedEventId);
        Assert.Equal(1, failed.LoadCount);
        var retry = WharfReducers.Loading(failed, new LoadWharfEvents());
        Assert.Null(retry.Error);
        Assert.Equal(2, retry.AttemptCount);
    }

    [Fact]
    public void ConsumerReferencesOnlyPublicContractsAndDoesNotImportWharfState()
    {
        var references = typeof(RestaurantDashboard).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name == typeof(EventCalendar).Assembly.GetName().Name);
        Assert.Contains(references, reference => reference.Name == typeof(CalendarRequest).Assembly.GetName().Name);
        Assert.Equal("RonSijm.Demo.Fluxor.WonderWharf.Contracts", typeof(CalendarRequest).Assembly.GetName().Name);
        Assert.Same(typeof(CalendarRequest).Assembly, typeof(WharfEventSelection).Assembly);
        Assert.Same(typeof(CalendarRequest).Assembly, typeof(WharfEventSelected).Assembly);
        Assert.Same(typeof(CalendarRequest).Assembly, typeof(WonderWharfFeature).Assembly);
        Assert.Equal("fluxor.wonderwharf.events", WonderWharfComponents.Events);
        Assert.Equal("fluxor.wonderwharf.summary", WonderWharfComponents.Summary);
        Assert.DoesNotContain(typeof(CalendarRequest).Assembly.GetReferencedAssemblies(), reference => reference.Name is "Fluxor" or "RonSijm.Blazyload.Components");
        Assert.Contains(typeof(CalendarRequest), typeof(EventCalendar).Assembly.GetForwardedTypes());
        Assert.DoesNotContain(typeof(RestaurantDashboard).GetProperties(), property => property.PropertyType == typeof(IState<WonderWharfViewModel>));
    }

    [Fact]
    public async Task DynamicCompositionAndPublicEffectsUseTheRealProvider()
    {
        await using var context = CreateFoundationContext();
        var store = context.Services.GetRequiredService<IStore>();
        await store.InitializeAsync();
        await AddFeatureAsync(context, await new BobBootstrap().Bootstrap());
        var dispatcher = context.Services.GetRequiredService<IDispatcher>();
        var restaurant = context.Services.GetRequiredService<IState<RestaurantViewModel>>();
        var preferences = context.Services.GetRequiredService<IState<PreferencesViewModel>>();
        var shell = context.Services.GetRequiredService<IState<RestaurantShellViewModel>>();
        var trace = context.Services.GetRequiredService<IState<TraceViewModel>>();
        var selection = new WharfEventSelection(Guid.NewGuid(), "Fall festival", new DateOnly(2026, 10, 10));
        var sequence = trace.Value.Sequence;
        dispatcher.Dispatch(new RecordWharfSelection(selection));
        Assert.Equal(sequence + 1, trace.Value.Sequence);
        Assert.Same(restaurant.Value, shell.Value.Restaurant);
        Assert.Same(selection, shell.Value.Restaurant!.SelectedEvent);
        Assert.Null(restaurant.Value.BroadcastEvent);

        dispatcher.Dispatch(new DemoThemeApplied(true));
        Assert.Same(preferences.Value, shell.Value.Preferences);
        Assert.True(shell.Value.Preferences!.IsDarkMode);
        await AddFeatureAsync(context, await new WharfBootstrap().Bootstrap());
        Assert.Same(store, context.Services.GetRequiredService<IStore>());
        var wharf = context.Services.GetRequiredService<IState<WonderWharfViewModel>>();
        var selectionEffect = context.Services.GetRequiredService<WharfSelectionEffect>();
        Assert.False(typeof(IEffect).IsAssignableFrom(selectionEffect.GetType()));
        Assert.Same(wharf, selectionEffect.State);
        dispatcher.Dispatch(new WharfEventsLoaded([new WharfEvent(selection.EventId, selection.Name, selection.Date)]));
        dispatcher.Dispatch(new SelectWharfEvent(selection.EventId));
        var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        restaurant.StateChanged += (_, _) =>
        {
            if (restaurant.Value.PublicEventCount == 1)
            {
                published.TrySetResult();
            }
        };
        dispatcher.Dispatch(new PublishSelectedWharfEvent());
        await published.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(selection, restaurant.Value.BroadcastEvent);
        Assert.Same(restaurant.Value, shell.Value.Restaurant);
        Assert.Equal(selection, shell.Value.Restaurant!.BroadcastEvent);
        Assert.Contains(trace.Value.Entries, entry => entry.Description == nameof(WharfEventSelected));
        Assert.Contains("Fall festival", JsonSerializer.Serialize(shell.Value));
    }

    [Fact]
    public async Task FeatureCacheMissesDoNotHideALaterLazyFeature()
    {
        await using var context = CreateFoundationContext();
        var store = context.Services.GetRequiredService<IStore>();
        await store.InitializeAsync();
        var cache = context.Services.GetRequiredService<FeatureCache>();
        Assert.Null(cache.GetFeature(new WonderWharfViewModel()));
        Assert.Null(cache.GetFeature(new WonderWharfViewModel()));

        await AddFeatureAsync(context, await new WharfBootstrap().Bootstrap());
        Assert.Same(store.Features["Fluxor.WonderWharf"], cache.GetFeature(new WonderWharfViewModel()));
        context.Services.GetRequiredService<IDispatcher>().Dispatch(new WonderWharfViewModel { HasLoaded = true, LoadCount = 4 });
        Assert.Equal(4, context.Services.GetRequiredService<IState<WonderWharfViewModel>>().Value.LoadCount);
    }

    [Fact]
    public async Task FailedLateRegistrationRollsBackAndRetryAttachesReducersAndBothMethodStylesOnce()
    {
        await using var context = CreateFoundationContext();
        var store = context.Services.GetRequiredService<IStore>();
        await store.InitializeAsync();
        await AddFeatureAsync(context, await new BobBootstrap().Bootstrap());
        await AddFeatureAsync(context, await new WharfBootstrap().Bootstrap());
        await AddFeatureAsync(context, await new TrackingBootstrap().Bootstrap());
        var provider = context.Services.GetRequiredService<SyringeServiceProvider>();
        var dispatcher = provider.GetRequiredService<IDispatcher>();
        var state = provider.GetRequiredService<IState<WonderWharfViewModel>>();
        var restaurant = provider.GetRequiredService<IState<RestaurantViewModel>>();
        var item = new WharfEvent(Guid.NewGuid(), "Fall festival", new DateOnly(2026, 10, 10));
        dispatcher.Dispatch(new WharfEventsLoaded([item]));
        dispatcher.Dispatch(new SelectWharfEvent(item.Id));
        var feature = store.Features["Fluxor.WonderWharf"];
        var initialState = state.Value;
        var descriptors = provider.ServiceDescriptors.ToArray();
        var extension = provider.GetRequiredService<WharfRuntimeExtensions>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => extension.RegisterPublicationTrackingAsync(true));
        Assert.Contains(nameof(IWharfPublicationCounter), error.Message);
        Assert.Equal(descriptors, provider.ServiceDescriptors.ToArray());
        Assert.Same(initialState, state.Value);
        Assert.Null(provider.GetService<IWharfPublicationCounter>());
        Assert.DoesNotContain(provider.ServiceDescriptors, descriptor => descriptor.ServiceType == typeof(WharfPublicationTrackingEffects));
        dispatcher.Dispatch(new PublishSelectedWharfEvent());
        Assert.Equal(1, restaurant.Value.PublicEventCount);
        Assert.Equal(0, state.Value.PublicationCount);
        Assert.Equal(0, state.Value.PublicationAuditCount);

        await extension.RegisterPublicationTrackingAsync();
        Assert.True(state.Value.IsPublicationTrackingEnabled);
        Assert.Same(feature, store.Features["Fluxor.WonderWharf"]);
        Assert.Same(store, provider.GetRequiredService<IStore>());
        Assert.Same(state, provider.GetRequiredService<IState<WonderWharfViewModel>>());
        var counter = provider.GetRequiredService<IWharfPublicationCounter>();
        Assert.Same(counter, provider.GetRequiredService<WharfPublicationTrackingEffects>().Counter);
        dispatcher.Dispatch(new PublishSelectedWharfEvent());
        Assert.Equal(1, state.Value.PublicationCount);
        Assert.Equal(1, state.Value.PublicationAuditCount);
        Assert.Equal(1, counter.Calls);
        Assert.Equal(2, restaurant.Value.PublicEventCount);

        await extension.RegisterPublicationTrackingAsync();
        dispatcher.Dispatch(new PublishSelectedWharfEvent());
        Assert.Equal(2, state.Value.PublicationCount);
        Assert.Equal(2, state.Value.PublicationAuditCount);
        Assert.Equal(2, counter.Calls);
        Assert.Equal(3, restaurant.Value.PublicEventCount);
        Assert.Same(counter, provider.GetRequiredService<IWharfPublicationCounter>());
        Assert.Equal(item.Id, state.Value.SelectedEventId);
        Assert.Equal(1, state.Value.LoadCount);
    }

    [Fact]
    public async Task OptionalRegistrationFailureIsVisibleAndTheComponentCanRetry()
    {
        await using var context = CreateFoundationContext();
        await context.Services.GetRequiredService<IStore>().InitializeAsync();
        await AddFeatureAsync(context, await new WharfBootstrap().Bootstrap());
        await AddFeatureAsync(context, await new TrackingBootstrap().Bootstrap());
        var panel = context.Render<PublicationTracking>();
        panel.Find("[data-testid=fluxor-tracking-fail]").Click();
        panel.WaitForAssertion(() => Assert.Contains(nameof(IWharfPublicationCounter), panel.Find("[data-testid=fluxor-registration-error]").TextContent));
        Assert.Equal("Not attached", panel.Find("[data-testid=fluxor-tracking-status]").TextContent);
        panel.Find("[data-testid=fluxor-tracking-enable]").Click();
        panel.WaitForAssertion(() => Assert.Equal("Attached", panel.Find("[data-testid=fluxor-tracking-status]").TextContent));
        Assert.Empty(panel.FindAll("[data-testid=fluxor-registration-error]"));
        Assert.True(panel.Find("[data-testid=fluxor-tracking-fail]").HasAttribute("disabled"));
        panel.Find("[data-testid=fluxor-tracking-repeat]").Click();
        Assert.Equal("0", panel.Find("[data-testid=fluxor-publication-count]").TextContent);
    }

    [Theory]
    [InlineData(StoreLifetime.Singleton)]
    [InlineData(StoreLifetime.Scoped)]
    public async Task TypedNativeConfigurationHonorsLifetimesAndUpdatesExistingAndFutureStores(StoreLifetime lifetime)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new HttpClient(new EventHandler()) { BaseAddress = new Uri("https://localhost/") });
        SyringeFluxorOptions.NativeExtensionConfiguration configureStore = native =>
        {
            native.WithLifetime(lifetime);
            native.ScanTypes(typeof(StartupState));
        };
        await using var provider = new SyringeServiceProvider(services, options =>
        {
            options.ValidateScopes = true;
            options.UseFluxor(fluxor => fluxor.AddNativeExtension(configureStore));
        });
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var firstStore = first.ServiceProvider.GetRequiredService<IStore>();
        var secondStore = second.ServiceProvider.GetRequiredService<IStore>();
        await firstStore.InitializeAsync();
        await secondStore.InitializeAsync();
        if (lifetime == StoreLifetime.Scoped)
        {
            Assert.NotSame(firstStore, secondStore);
        }
        else
        {
            Assert.Same(firstStore, secondStore);
        }

        await AddFeatureAsync(provider, await new WharfBootstrap().Bootstrap());
        await AddFeatureAsync(provider, await new TrackingBootstrap().Bootstrap());
        await first.ServiceProvider.GetRequiredService<WharfRuntimeExtensions>().RegisterPublicationTrackingAsync();
        var firstState = first.ServiceProvider.GetRequiredService<IState<WonderWharfViewModel>>();
        var secondState = second.ServiceProvider.GetRequiredService<IState<WonderWharfViewModel>>();
        Assert.Same(firstState, first.ServiceProvider.GetRequiredService<WharfSelectionEffect>().State);
        Assert.Same(secondState, second.ServiceProvider.GetRequiredService<WharfSelectionEffect>().State);
        var item = new WharfEvent(Guid.NewGuid(), "Fall festival", new DateOnly(2026, 10, 10));
        var firstDispatcher = first.ServiceProvider.GetRequiredService<IDispatcher>();
        firstDispatcher.Dispatch(new WharfEventsLoaded([item]));
        firstDispatcher.Dispatch(new SelectWharfEvent(item.Id));
        firstDispatcher.Dispatch(new PublishSelectedWharfEvent());
        Assert.Equal(1, firstState.Value.PublicationCount);
        Assert.Equal(1, firstState.Value.PublicationAuditCount);
        Assert.Equal(lifetime == StoreLifetime.Scoped ? 0 : 1, secondState.Value.PublicationCount);
        Assert.Equal(lifetime == StoreLifetime.Scoped ? 0 : 1, secondState.Value.PublicationAuditCount);
        var counter = first.ServiceProvider.GetRequiredService<IWharfPublicationCounter>();
        Assert.Same(counter, second.ServiceProvider.GetRequiredService<IWharfPublicationCounter>());
        Assert.Equal(1, counter.Calls);

        await using var future = provider.CreateAsyncScope();
        var futureStore = future.ServiceProvider.GetRequiredService<IStore>();
        await futureStore.InitializeAsync();
        var futureState = future.ServiceProvider.GetRequiredService<IState<WonderWharfViewModel>>();
        Assert.Same(futureState, future.ServiceProvider.GetRequiredService<WharfSelectionEffect>().State);
        var futureDispatcher = future.ServiceProvider.GetRequiredService<IDispatcher>();
        futureDispatcher.Dispatch(new WharfEventsLoaded([item]));
        futureDispatcher.Dispatch(new SelectWharfEvent(item.Id));
        futureDispatcher.Dispatch(new PublishSelectedWharfEvent());
        Assert.Equal(lifetime == StoreLifetime.Scoped ? 1 : 2, futureState.Value.PublicationCount);
        Assert.Equal(2, futureState.Value.PublicationAuditCount);
        Assert.Equal(lifetime == StoreLifetime.Scoped ? 1 : 2, firstState.Value.PublicationCount);
        Assert.Equal(lifetime == StoreLifetime.Scoped ? 1 : 2, firstState.Value.PublicationAuditCount);
        Assert.Same(counter, future.ServiceProvider.GetRequiredService<IWharfPublicationCounter>());
        Assert.Equal(2, counter.Calls);
        Assert.Same(firstStore, first.ServiceProvider.GetRequiredService<IStore>());
        Assert.Same(secondStore, second.ServiceProvider.GetRequiredService<IStore>());
    }

    [Fact]
    public async Task StartupMethodEffectUsesConstructorInjectionAndAuditsLoadedAssemblies()
    {
        await using var context = CreateFoundationContext();
        var logger = Substitute.For<ILogger<AssemblyLoadedAudit>>();
        var logged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        logger.When(logger => logger.Log(Arg.Any<LogLevel>(), Arg.Any<EventId>(), Arg.Any<Arg.AnyType>(), Arg.Any<Exception?>(), Arg.Any<Func<Arg.AnyType, Exception?, string>>())).Do(_ => logged.TrySetResult());
        context.Services.AddSingleton(logger);
        await context.Services.GetRequiredService<IStore>().InitializeAsync();
        context.Services.GetRequiredService<IDispatcher>().Dispatch(new AssemblyLoaded(typeof(EventCalendar).Assembly));
        await logged.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains(logger.ReceivedCalls(), call => call.GetMethodInfo().Name == nameof(ILogger.Log) && call.GetArguments()[2]!.ToString()!.Contains("RonSijm.Demo.Fluxor.WonderWharf"));
    }

    [Fact]
    public async Task StateAwareLayoutDispatcherControlsAndLifecycleEffectsShareRuntimeInjection()
    {
        await using var context = CreateFoundationContext();
        var module = context.JSInterop.SetupModule("./_content/RonSijm.Demo.Fluxor.BobsBurgers/demo.js");
        module.Setup<string>("initializeShell", false).SetResult("Browser module ready");
        module.Setup<string>("initializeShell", true).SetResult("Browser module ready");
        module.SetupVoid("applyTheme", true).SetVoidResult();
        module.SetupVoid("applyTheme", false).SetException(new JSException("Theme API unavailable"));
        await context.Services.GetRequiredService<IStore>().InitializeAsync();
        await AddFeatureAsync(context, await new BobBootstrap().Bootstrap());
        var dispatcher = context.Services.GetRequiredService<IDispatcher>();
        var preferences = context.Services.GetRequiredService<IState<PreferencesViewModel>>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("Fluxor/");
        var layout = context.Render<RestaurantLayout>();
        layout.WaitForAssertion(() => Assert.Equal("1", layout.Find("[data-testid=fluxor-first-render-count]").TextContent));
        dispatcher.Dispatch(new PageEvent(PageEventType.OnInitialized, typeof(BrowserStatus)));
        dispatcher.Dispatch(new PageEvent(PageEventType.FirstTimeOnAfterRender, typeof(EventCalendar)));
        Assert.Equal(1, preferences.Value.FirstRenderCount);
        Assert.Single(module.Invocations["initializeShell"]);

        layout.Find("[data-testid=fluxor-toggle-theme]").Click();
        layout.WaitForAssertion(() => Assert.Equal("Dark", layout.Find("[data-testid=fluxor-shell-theme]").TextContent));
        Assert.True(preferences.Value.IsDarkMode);
        layout.Find("[data-testid=fluxor-navigate-guide]").Click();
        Assert.Equal("http://localhost/Fluxor/#fluxor-benefits", navigation.Uri);
        layout.WaitForAssertion(() => Assert.Equal("fluxor-benefits", layout.Find("[data-testid=fluxor-last-navigation]").TextContent));
        layout.Find("[data-testid=fluxor-toggle-theme]").Click();
        layout.WaitForAssertion(() => Assert.Contains("Theme API unavailable", layout.Find("[data-testid=fluxor-browser-error]").TextContent));
        Assert.True(preferences.Value.IsDarkMode);
        Assert.False(preferences.Value.IsApplyingTheme);

        layout.Dispose();
        var reopened = context.Render<RestaurantLayout>();
        reopened.WaitForAssertion(() => Assert.Equal("2", reopened.Find("[data-testid=fluxor-first-render-count]").TextContent));
        Assert.Equal("Dark", reopened.Find("[data-testid=fluxor-shell-theme]").TextContent);
        Assert.Single(context.JSInterop.Invocations["import"]);
    }

    [Fact]
    public async Task FailedModuleImportIsVisibleAndDoesNotDisposeANonexistentModule()
    {
        await using var context = CreateFoundationContext();
        var jsRuntime = Substitute.For<IJSRuntime>();
        jsRuntime.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(_ => ValueTask.FromException<IJSObjectReference>(new JSException("Module import failed")));
        context.Services.AddSingleton(jsRuntime);
        await context.Services.GetRequiredService<IStore>().InitializeAsync();
        await AddFeatureAsync(context, await new BobBootstrap().Bootstrap());
        var layout = context.Render<RestaurantLayout>();
        layout.WaitForAssertion(() => Assert.Contains("Module import failed", layout.Find("[data-testid=fluxor-browser-error]").TextContent));
        var preferences = context.Services.GetRequiredService<IState<PreferencesViewModel>>();
        Assert.Equal(0, preferences.Value.FirstRenderCount);
        Assert.False(preferences.Value.IsApplyingTheme);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreloadingDispatchesTheNativeIntegrationActionWithoutMountingUi(bool usePath)
    {
        var state = Substitute.For<IState<RestaurantViewModel>>();
        state.Value.Returns(new RestaurantViewModel());
        var configuration = new AssemblyLoadConfiguration();
        configuration.Add(WonderWharfFeature.LoadingPath, $"{WonderWharfFeature.AssemblyName}.wasm");
        var dispatcher = Substitute.For<IDispatcher>();
        var effect = new PreloadWharfEffect { State = state, Configuration = configuration };
        await effect.HandleAsync(new PreloadWharf(usePath), dispatcher);
        if (usePath)
        {
            dispatcher.Received(1).Dispatch(new LoadAssemblyForPath(WonderWharfFeature.LoadingPath));
            dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadAssembly>());
        }
        else
        {
            dispatcher.Received(1).Dispatch(new LoadAssembly($"{WonderWharfFeature.AssemblyName}.wasm"));
            dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadAssemblyForPath>());
        }
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadWharfEvents>());
    }

    [Fact]
    public async Task PreloadingRejectsMissingMappingsAndCompletesAlreadyLoadedRequests()
    {
        var state = Substitute.For<IState<RestaurantViewModel>>();
        state.Value.Returns(new RestaurantViewModel());
        var dispatcher = Substitute.For<IDispatcher>();
        var effect = new PreloadWharfEffect { State = state, Configuration = new AssemblyLoadConfiguration() };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => effect.HandleAsync(new PreloadWharf(true), dispatcher));
        Assert.Contains(WonderWharfFeature.LoadingPath, error.Message);
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadAssemblyForPath>());
        state.Value.Returns(new RestaurantViewModel { LoadedAssemblies = [WonderWharfFeature.AssemblyName] });
        await effect.HandleAsync(new PreloadWharf(true), dispatcher);
        dispatcher.Received(1).Dispatch(new WharfPreloadCompleted());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<LoadAssembly>());
    }

    [Fact]
    public void ActionTraceKeepsExactlyFifteenEntriesWithoutObservingItsOwnActions()
    {
        var state = new TraceViewModel();
        for (var index = 1; index <= 20; index++)
        {
            state = TraceReducers.Observed(state, new ActionObserved($"Action {index}"));
        }
        Assert.Equal(20, state.Sequence);
        Assert.Equal(15, state.Entries.Count);
        Assert.Equal(6, state.Entries[0].Sequence);
        Assert.Equal(20, state.Entries[^1].Sequence);
        var dispatcher = Substitute.For<IDispatcher>();
        var middleware = new DemoTraceMiddleware { Dispatcher = dispatcher };
        middleware.AfterDispatch(new ActionObserved("Do not recurse"));
        middleware.AfterDispatch(new PreferencesViewModel());
        dispatcher.DidNotReceive().Dispatch(Arg.Any<object>());
        middleware.AfterDispatch(new PageEvent(PageEventType.FirstTimeOnAfterRender, typeof(BrowserStatus)));
        dispatcher.Received().Dispatch(Arg.Is<ActionObserved>(action => action.Description.Contains(nameof(BrowserStatus))));
    }

    [Fact]
    public void DevToolsAssemblyConverterRestoresOnlyAlreadyLoadedIdentities()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new LoadedAssemblyJsonConverter());
        var assembly = typeof(EventCalendar).Assembly;
        var json = JsonSerializer.Serialize(new AssembliesLoadedState { LoadedAssemblies = [assembly] }, options);
        Assert.Contains(assembly.FullName!, json);
        Assert.Same(assembly, Assert.Single(JsonSerializer.Deserialize<AssembliesLoadedState>(json, options)!.LoadedAssemblies));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Assembly>("\"An.Unknown.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null\"", options));
    }

    private static BunitContext CreateFoundationContext()
    {
        var context = new BunitContext();
        context.Services.AddLogging();
        context.Services.AddSingleton(new HttpClient(new EventHandler()) { BaseAddress = new Uri("http://localhost/") });
        var options = new BlazyloadProviderOptions();
        options.LoadOnNavigation(WonderWharfFeature.LoadingPath, $"{WonderWharfFeature.AssemblyName}.wasm");
        options.UseFluxor(fluxor => fluxor.ScanTypes(typeof(StartupState), typeof(AssemblyLoadedAudit)));
        context.Services.UseServiceProviderFactory(new BlazyServiceProviderFactory(options));
        return context;
    }

    private static Task AddFeatureAsync(BunitContext context, IEnumerable<ServiceDescriptor> descriptors)
    {
        return AddFeatureAsync(context.Services.GetRequiredService<SyringeServiceProvider>(), descriptors);
    }

    private static async Task AddFeatureAsync(SyringeServiceProvider provider, IEnumerable<ServiceDescriptor> descriptors)
    {
        IServiceCollection services = new ServiceCollection();
        foreach (var descriptor in descriptors)
        {
            services.Add(descriptor);
        }
        await provider.LoadServiceDescriptors(services);
        provider.Build();
    }

    [FeatureState(Name = "Tests.Startup")]
    public sealed record StartupState;

    private sealed class EventHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var events = new[] { new WharfEvent(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Fall festival", new DateOnly(2026, 10, 10)) };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(events) });
        }
    }
}
