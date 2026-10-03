using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RonSijm.Demo.BobsBurgers.Components;
using RonSijm.Demo.WonderWharf.Components;
using RonSijm.Demo.WonderWharf.Contracts;
using RonSijm.Demo.WonderWharf.Services;

namespace RonSijm.Blazyload.Components.Tests;

public sealed class RenderingTests
{
    [Theory]
    [InlineData("wonderwharf.events", "Wonder Wharf events")]
    [InlineData("wonderwharf.rides", "RideSchedule: local rendering inside Wonder Wharf")]
    [InlineData("bobsburgers.burger-of-the-day", "BurgerOfTheDay")]
    public void RendersComponentWithoutParameters(string name, string expectedHeading)
    {
        using var context = CreateContext();
        var component = context.Render<BlazyComponent>(parameters => parameters.Add(value => value.Name, name));
        Assert.Contains(expectedHeading, component.Markup);
    }

    [Fact]
    public void ForwardsPrimitiveObjectNullAndEventCallbackParameters()
    {
        using var context = CreateContext();
        DateOnly? selected = null;
        var callback = EventCallback.Factory.Create<DateOnly>(this, date => selected = date);
        var parameters = new Dictionary<string, object?>
        {
            ["Title"] = "Team outing",
            ["Model"] = new EventCalendarModel("Alice's Diner"),
            ["DateSelected"] = callback
        };
        var component = context.Render<BlazyComponent>(builder => builder.Add(value => value.Name, "wonderwharf.events").Add(value => value.Parameters, parameters));

        Assert.Equal("Team outing", component.Find("[data-testid=event-calendar] h2").TextContent);
        Assert.Equal("Alice's Diner", component.Find("[data-testid=restaurant-name]").TextContent);
        component.Find("[data-testid=select-date]").Click();
        Assert.Equal(new DateOnly(2026, 10, 10), selected);

        parameters["Title"] = "Changed title";
        parameters["Model"] = null;
        component.Render(builder => builder.Add(value => value.Name, "wonderwharf.events").Add(value => value.Parameters, parameters));
        Assert.Equal("Changed title", component.Find("[data-testid=event-calendar] h2").TextContent);
        Assert.Empty(component.Find("[data-testid=restaurant-name]").TextContent);
    }

    [Fact]
    public void RestaurantRendersWonderWharfWithoutImplementationOrServiceReference()
    {
        using var context = CreateContext();
        var references = typeof(RestaurantDashboard).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, assembly => assembly.Name == typeof(EventCalendar).Assembly.GetName().Name);
        Assert.DoesNotContain(references, assembly => assembly.Name == typeof(WharfEventService).Assembly.GetName().Name);
        var component = context.Render<BlazyComponent>(parameters => parameters.Add(value => value.Name, "bobsburgers.dashboard"));
        Assert.Equal("Compose first. Download later.", component.Find("h1").TextContent);
        Assert.Equal("#compose", component.Find("nav a").GetAttribute("href"));
        Assert.Contains("<BurgerOfTheDay />", component.Find("[data-testid=burger-of-the-day]").TextContent);
        Assert.Empty(component.FindAll("details"));
        Assert.Empty(component.FindAll("[data-testid=event-calendar]"));
        Assert.Empty(component.FindAll("[data-testid=ride-schedule]"));
        Assert.Contains("No calendar requested by this dashboard yet", component.Find("[data-testid=demo-status]").TextContent);
        Assert.Contains("<BlazyComponent Name=\"@WonderWharfComponents.Events\" Parameters=\"@_parameters\" />", component.Find("[data-testid=restaurant-consumer-code]").TextContent);
        Assert.Contains("Bob's Burgers only references Components and WonderWharf.Contracts", component.Find("[data-testid=reference-boundary]").TextContent);
        Assert.Contains("blazy-components.json", component.Find("[data-testid=loading-steps]").TextContent);
        Assert.Contains("EventCallback.Factory.Create<DateOnly>", component.Find("[data-testid=restaurant-parameter-code]").TextContent);
        Assert.True(component.Find("[data-testid=change-title]").HasAttribute("disabled"));

        component.Find("[data-testid=show-events]").Click();
        Assert.Contains("has been requested", component.Find("[data-testid=demo-status]").TextContent);
        Assert.Contains("This component comes from the Wonder Wharf assembly", component.Find("[data-testid=wharf-explanation]").TextContent);
        Assert.Contains("@attribute [BlazyComponent(\"wonderwharf.events\")]", component.Find("[data-testid=wharf-producer-code]").TextContent);
        Assert.Contains("[BlazyContract]", component.Markup);
        Assert.Empty(component.FindAll("details"));
        Assert.False(component.Find("[data-testid=change-title]").HasAttribute("disabled"));
        Assert.Equal("Upcoming Wonder Wharf events", component.Find("[data-testid=event-calendar] h2").TextContent);
        Assert.Equal("Bob's Burgers", component.Find("[data-testid=restaurant-name]").TextContent);
        Assert.Contains("Fall festival", component.Find("[data-testid=wharf-events]").TextContent);
        Assert.Contains("<RideSchedule />", component.Find("[data-testid=ride-schedule]").TextContent);
        component.Find("[data-testid=select-date]").Click();
        Assert.Equal("2026-10-10", component.Find("[data-testid=selected-date]").TextContent);
        component.Find("[data-testid=change-title]").Click();
        Assert.Equal("Wonder Wharf events for the restaurant team", component.Find("[data-testid=event-calendar] h2").TextContent);
    }

    [Fact]
    public void ExtensiveExplainsOptionalToolingBeforeRequestingProducer()
    {
        using var context = CreateContext();
        var component = context.Render<RestaurantDashboard>();
        Assert.Equal("#tooling", component.Find("nav a:last-child").GetAttribute("href"));
        Assert.Contains("No source-generator package", component.Find("[data-testid=demo-comparison]").TextContent);
        Assert.Contains("Pizza.Contracts", component.Find("[data-testid=demo-comparison]").TextContent);
        Assert.Contains("BlazyComponentContractReference", component.Find("[data-testid=demo-comparison]").TextContent);
        Assert.Equal("<BlazyComponent Name=\"pizza.calendar\" Parameters=\"@_parameters\" />", component.Find("[data-testid=simple-composition-code]").TextContent);
        Assert.Equal("<BlazyComponent Name=\"@WonderWharfComponents.Events\" Parameters=\"@_parameters\" />", component.Find("[data-testid=extensive-composition-code]").TextContent);
        Assert.Contains("[BlazyContract]", component.Find("[data-testid=contract-source-code]").TextContent);
        Assert.Contains($"public const string Events = \"{WonderWharfComponents.Events}\";", component.Find("[data-testid=generated-contract-code]").TextContent);
        Assert.Contains("public sealed record EventCalendarModel(string RestaurantName);", component.Find("[data-testid=generated-contract-code]").TextContent);
        Assert.Equal(4, component.FindAll("[data-testid=contracts-pipeline] li").Count);
        Assert.Contains("compiled definition lives only in contracts", component.Find("[data-testid=contracts-pipeline]").TextContent);
        Assert.Contains("RonSijm.Blazyload.Components.Build", component.Find("[data-testid=shared-demo-runtime]").TextContent);
        Assert.Contains("Simple also demonstrates DI services, images, JavaScript and scoped CSS", component.Find("[data-testid=shared-demo-runtime]").TextContent);
        Assert.Contains("Build-time only", component.Find("[data-testid=demo-tooling]").TextContent);
        Assert.Contains("generated contracts DLL is a runtime dependency", component.Find("[data-testid=demo-tooling]").TextContent);
        Assert.Empty(component.FindAll("details"));
        Assert.Empty(component.FindAll("[data-testid=event-calendar]"));
    }

    [Fact]
    public void GeneratedContractsOwnModelsAndProducerForwardsThem()
    {
        var contracts = typeof(EventCalendarModel).Assembly;
        Assert.Equal("RonSijm.Demo.WonderWharf.Contracts", contracts.GetName().Name);
        Assert.Equal("wonderwharf.events", WonderWharfComponents.Events);
        Assert.Equal("wonderwharf.rides", WonderWharfComponents.Rides);
        Assert.Same(contracts, typeof(WonderWharfComponents).Assembly);
        Assert.Contains(typeof(EventCalendarModel), typeof(EventCalendar).Assembly.GetForwardedTypes());
        Assert.Same(typeof(EventCalendarModel), typeof(EventCalendar).GetProperty("Model")!.PropertyType);
        Assert.DoesNotContain(contracts.GetReferencedAssemblies(), assembly => assembly.Name == typeof(EventCalendar).Assembly.GetName().Name || assembly.Name == typeof(WharfEventService).Assembly.GetName().Name || assembly.Name == typeof(BlazyComponent).Assembly.GetName().Name);
    }

    [Fact]
    public void ChangingLogicalNameReplacesRenderedComponent()
    {
        using var context = CreateContext();
        var component = context.Render<BlazyComponent>(parameters => parameters.Add(value => value.Name, "wonderwharf.events"));
        component.Render(parameters => parameters.Add(value => value.Name, "bobsburgers.dashboard"));
        Assert.Equal("Compose first. Download later.", component.Find("h1").TextContent);
        Assert.Empty(component.FindAll("[data-testid=event-calendar]"));
        Assert.Empty(component.FindAll("[data-testid=ride-schedule]"));
    }

    [Fact]
    public void NewerNameSupersedesPendingResolution()
    {
        using var context = CreateContext();
        var registry = Substitute.For<IBlazyComponentRegistry>();
        var completion = new TaskCompletionSource<BlazyComponentDescriptor>(TaskCreationOptions.RunContinuationsAsynchronously);
        registry.ResolveAsync("first", Arg.Any<CancellationToken>()).Returns(call => new ValueTask<BlazyComponentDescriptor>(completion.Task.WaitAsync(call.ArgAt<CancellationToken>(1))));
        registry.ResolveAsync("second", Arg.Any<CancellationToken>()).Returns(new ValueTask<BlazyComponentDescriptor>(RegistryTests.Describe("second", typeof(RestaurantDashboard)) with { ComponentType = typeof(RestaurantDashboard) }));
        context.Services.AddSingleton(registry);

        var component = context.Render<BlazyComponent>(parameters => parameters.Add(value => value.Name, "first"));
        Assert.Empty(component.Markup);
        component.Render(parameters => parameters.Add(value => value.Name, "second"));
        completion.SetResult(RegistryTests.Describe("first", typeof(EventCalendar)) with { ComponentType = typeof(EventCalendar) });
        component.WaitForAssertion(() =>
        {
            Assert.Equal("Compose first. Download later.", component.Find("h1").TextContent);
            Assert.Empty(component.FindAll("[data-testid=event-calendar]"));
        });
    }

    private static Bunit.BunitContext CreateContext()
    {
        var entries = new[]
        {
            RegistryTests.Describe("wonderwharf.events", typeof(EventCalendar)),
            RegistryTests.Describe("wonderwharf.rides", typeof(RideSchedule)),
            RegistryTests.Describe("bobsburgers.dashboard", typeof(RestaurantDashboard)),
            RegistryTests.Describe("bobsburgers.burger-of-the-day", typeof(BurgerOfTheDay))
        };
        var context = RegistryTests.CreateContext(entries, RegistryTests.CreateLoader());
        context.Services.AddSingleton<WharfEventService>();
        return context;
    }
}
