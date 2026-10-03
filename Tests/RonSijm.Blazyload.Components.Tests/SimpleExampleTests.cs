using Bunit;
using Microsoft.Extensions.DependencyInjection;
using RonSijm.Demo.Blazyload.Components.Burger.Components;
using RonSijm.Demo.Blazyload.Components.Pizza.Components;
using RonSijm.Demo.Blazyload.Components.Pizza.Contracts;
using RonSijm.Demo.Blazyload.Components.Pizza.Services;

namespace RonSijm.Blazyload.Components.Tests;

public sealed class SimpleExampleTests
{
    [Fact]
    public void SimpleCompositionSupportsParametersAndCallbacksWithoutGeneratedContracts()
    {
        var entries = new[]
        {
            RegistryTests.Describe("burger.editor", typeof(BurgerPage)),
            RegistryTests.Describe("pizza.calendar", typeof(PizzaCalendar))
        };
        using var context = RegistryTests.CreateContext(entries, RegistryTests.CreateLoader());
        context.Services.AddSingleton<PizzaGreeting>();
        var component = context.Render<BlazyComponent>(parameters => parameters.Add(value => value.Name, "burger.editor"));
        Assert.Equal("Burger editor", component.Find("h1").TextContent);
        Assert.Contains("minimal changes and dependencies", component.Markup);
        Assert.Empty(component.FindAll("[data-testid=pizza-calendar]"));
        component.Find("[data-testid=show-pizza]").Click();
        Assert.Equal("Pizza for burgers", component.Find("[data-testid=pizza-calendar] h2").TextContent);
        Assert.Equal("Burger customer", component.Find("[data-testid=pizza-customer]").TextContent);
        Assert.Equal("Pizza dependency loaded", component.Find("[data-testid=pizza-dependency]").TextContent);
        component.Find("[data-testid=select-date]").Click();
        Assert.Equal("2026-10-01", component.Find("[data-testid=selected-date]").TextContent);
        component.Find("[data-testid=change-title]").Click();
        Assert.Equal("Updated pizza calendar", component.Find("[data-testid=pizza-calendar] h2").TextContent);
    }

    [Fact]
    public void SimpleConsumerDoesNotReferenceProducerOrSourceGenerator()
    {
        var consumer = typeof(BurgerPage).Assembly;
        var references = consumer.GetReferencedAssemblies();
        Assert.DoesNotContain(references, assembly => assembly.Name == typeof(PizzaCalendar).Assembly.GetName().Name || assembly.Name == typeof(PizzaGreeting).Assembly.GetName().Name || assembly.Name == "RonSijm.Blazyload.Components.SourceGenerator");
        Assert.Equal("RonSijm.Demo.Blazyload.Components.Pizza.Contracts", typeof(PizzaCalendarModel).Assembly.GetName().Name);
        Assert.Same(typeof(PizzaCalendarModel), typeof(PizzaCalendar).GetProperty("Model")!.PropertyType);
    }
}
