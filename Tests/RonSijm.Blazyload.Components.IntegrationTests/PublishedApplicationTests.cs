using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace RonSijm.Blazyload.Components.IntegrationTests;

public sealed class PublishedApplicationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task PublishedDemoLoadsItsComponentsOnDemand()
    {
        var root = Environment.GetEnvironmentVariable("BLAZY_COMPONENTS_PUBLISH_ROOT");
        Assert.True(root is not null && File.Exists(Path.Combine(root, "index.html")), "Set BLAZY_COMPONENTS_PUBLISH_ROOT to the published sample's wwwroot directory. Run Verify-Publish.ps1.");
        var basePath = Environment.GetEnvironmentVariable("BLAZY_COMPONENTS_BASE_PATH") ?? "/";
        var demo = Environment.GetEnvironmentVariable("BLAZY_COMPONENTS_DEMO") ?? "Extensive";
        Assert.True(demo is "Orchestrator" or "Simple" or "Extensive", "BLAZY_COMPONENTS_DEMO must be Orchestrator, Simple or Extensive.");
        await using var server = new PublishedApplicationServer(root, basePath, output.WriteLine);
        using var playwright = await Playwright.CreateAsync();
        var channel = Environment.GetEnvironmentVariable("BLAZY_COMPONENTS_BROWSER_CHANNEL") ?? "msedge";
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = channel, Headless = true });
        var page = await browser.NewPageAsync();
        var requests = new ConcurrentQueue<string>();
        var documents = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        page.Request += (_, request) =>
        {
            requests.Enqueue(request.Url);
            if (request.ResourceType == "document")
            {
                documents.Enqueue(request.Url);
            }
        };
        page.Response += (_, response) =>
        {
            if (!response.Ok)
            {
                output.WriteLine($"HTTP {response.Status}: {response.Url}");
            }
        };
        page.PageError += (_, error) =>
        {
            errors.Enqueue(error);
            output.WriteLine(error);
        };
        page.Console += (_, message) =>
        {
            output.WriteLine($"Browser {message.Type}: {message.Text}");
            if (message.Type == "error")
            {
                errors.Enqueue(message.Text);
            }
        };

        await page.GotoAsync(server.BaseUri, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        if (demo == "Orchestrator")
        {
            await VerifyOrchestratorAsync(page, requests, documents, server.BaseUri);
        }
        else if (demo == "Simple")
        {
            await VerifySimpleAsync(page, requests);
        }
        else
        {
            await VerifyExtensiveAsync(page, requests);
        }

        Assert.All(requests, url => Assert.StartsWith(server.BaseUri, url, StringComparison.Ordinal));
        Assert.Empty(errors);
    }

    private static async Task VerifyOrchestratorAsync(IPage page, ConcurrentQueue<string> requests, ConcurrentQueue<string> documents, string baseUri)
    {
        await Assertions.Expect(page.GetByTestId("open-simple")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("This demo orchestrator is a demo in and of itself. It uses one Blazor runtime.");
        var demoAssemblies = new[]
        {
            "RonSijm.Demo.Blazyload.Components.Burger",
            "RonSijm.Demo.Blazyload.Components.Pizza",
            "RonSijm.Demo.Blazyload.Components.Pizza.Contracts",
            "RonSijm.Demo.Blazyload.Components.Pizza.Services",
            "RonSijm.Demo.BobsBurgers",
            "RonSijm.Demo.WonderWharf",
            "RonSijm.Demo.WonderWharf.Contracts",
            "RonSijm.Demo.WonderWharf.Services"
        };
        foreach (var name in demoAssemblies)
        {
            Assert.DoesNotContain(requests, url => IsAssembly(url, name));
        }

        foreach (var width in new[] { 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"), $"The orchestrator must not overflow horizontally at {width}px.");
        }
        await page.SetViewportSizeAsync(1280, 900);

        string[] GetRuntimeRequests()
        {
            return requests.Where(url => url.Contains("/_framework/", StringComparison.Ordinal) && !demoAssemblies.Any(name => IsAssembly(url, name))).ToArray();
        }

        var runtimeRequests = GetRuntimeRequests();
        Assert.NotEmpty(runtimeRequests);
        var runtimeMarker = Guid.NewGuid().ToString();
        await page.EvaluateAsync("value => window.__blazyRuntimeMarker = value", runtimeMarker);

        await page.GetByTestId("open-simple").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{baseUri}Simple/");
        await VerifySimpleAsync(page, requests);
        Assert.Single(requests, url => IsAssembly(url, "RonSijm.Demo.Blazyload.Components.Burger"));
        Assert.DoesNotContain(requests, url => IsAssembly(url, "RonSijm.Demo.BobsBurgers"));

        await page.GetByTestId("choose-extensive").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync($"{baseUri}Extensive/");
        await VerifyExtensiveAsync(page, requests);
        await Assertions.Expect(page.GetByTestId("pizza-calendar")).ToHaveCountAsync(0);
        await page.SetViewportSizeAsync(1280, 900);

        await page.GoBackAsync();
        await Assertions.Expect(page.GetByTestId("show-pizza")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("pizza-calendar")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("selected-date")).ToBeEmptyAsync();
        await page.GetByTestId("show-pizza").ClickAsync();
        await Assertions.Expect(page.GetByTestId("pizza-dependency")).ToHaveTextAsync("Pizza dependency loaded");

        await page.GoForwardAsync();
        await Assertions.Expect(page.GetByTestId("show-events")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("event-calendar")).ToHaveCountAsync(0);
        await page.GetByTestId("show-events").ClickAsync();
        await Assertions.Expect(page.GetByTestId("wharf-events")).ToContainTextAsync("2026-10-10: Fall festival");
        await page.GetByTestId("demo-home").ClickAsync();
        await Assertions.Expect(page.GetByTestId("open-simple")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("show-events")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("show-pizza")).ToHaveCountAsync(0);

        Assert.Single(documents);
        Assert.Equal(runtimeMarker, await page.EvaluateAsync<string>("window.__blazyRuntimeMarker"));
        var laterRuntimeRequests = GetRuntimeRequests();
        Assert.True(runtimeRequests.SequenceEqual(laterRuntimeRequests), $"Unexpected runtime requests while switching demos:{Environment.NewLine}{string.Join(Environment.NewLine, laterRuntimeRequests.Skip(runtimeRequests.Length))}");
        foreach (var name in demoAssemblies)
        {
            Assert.Single(requests, url => IsAssembly(url, name));
        }

        await page.GotoAsync($"{baseUri}Simple/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("show-pizza")).ToBeVisibleAsync();
        await page.GetByTestId("show-pizza").ClickAsync();
        await Assertions.Expect(page.GetByTestId("pizza-dependency")).ToHaveTextAsync("Pizza dependency loaded");
        await page.GotoAsync($"{baseUri}Extensive/", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("show-events")).ToBeVisibleAsync();
        await page.GetByTestId("show-events").ClickAsync();
        await Assertions.Expect(page.GetByTestId("event-calendar")).ToBeVisibleAsync();
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await Assertions.Expect(page.GetByTestId("show-events")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("event-calendar")).ToHaveCountAsync(0);
    }

    private static async Task VerifyExtensiveAsync(IPage page, ConcurrentQueue<string> requests)
    {
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Compose first. Download later.");
        await Assertions.Expect(page.GetByTestId("show-events")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(page.GetByTestId("burger-of-the-day")).ToContainTextAsync("<BurgerOfTheDay />");
        Assert.True(await page.Locator("img[alt=\"Bob's Burgers static asset\"]").EvaluateAsync<bool>("image => image.complete && image.naturalWidth > 0"));
        await Assertions.Expect(page.GetByTestId("burger-storefront").Locator("details")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("demo-status")).ToContainTextAsync("No calendar requested by this dashboard yet");
        await Assertions.Expect(page.GetByTestId("restaurant-consumer-code")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("restaurant-consumer-code")).ToContainTextAsync("<BlazyComponent Name=\"@WonderWharfComponents.Events\" Parameters=\"@_parameters\" />");
        await Assertions.Expect(page.GetByTestId("reference-boundary")).ToContainTextAsync("Bob's Burgers only references Components and WonderWharf.Contracts");
        await Assertions.Expect(page.GetByTestId("loading-steps")).ToContainTextAsync("DynamicComponent");
        await Assertions.Expect(page.GetByTestId("restaurant-parameter-code")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("demo-comparison")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("simple-composition-code")).ToContainTextAsync("Name=\"pizza.calendar\"");
        await Assertions.Expect(page.GetByTestId("extensive-composition-code")).ToContainTextAsync("Name=\"@WonderWharfComponents.Events\"");
        await Assertions.Expect(page.GetByTestId("contract-source-code")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("contract-source-code")).ToContainTextAsync("[BlazyContract]");
        await Assertions.Expect(page.GetByTestId("generated-contract-code")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("generated-contract-code")).ToContainTextAsync("public const string Events = \"wonderwharf.events\";");
        await Assertions.Expect(page.GetByTestId("contracts-reference-code")).ToContainTextAsync("BlazyComponentContractReference");
        await Assertions.Expect(page.GetByTestId("shared-demo-runtime")).ToContainTextAsync("Simple also demonstrates DI services, images, JavaScript and scoped CSS");
        await page.SetViewportSizeAsync(1280, 900);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        foreach (var testId in new[] { "show-events", "restaurant-consumer-code" })
        {
            var bounds = await page.GetByTestId(testId).BoundingBoxAsync();
            Assert.NotNull(bounds);
            Assert.True(bounds.Y >= 0 && bounds.Y + bounds.Height <= 900, $"{testId} must be fully visible in the initial desktop viewport.");
        }
        await Assertions.Expect(page.GetByTestId("change-title")).ToBeDisabledAsync();
        await Assertions.Expect(page.GetByTestId("event-calendar")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("ride-schedule")).ToHaveCountAsync(0);
        Assert.Contains(requests, url => IsAssembly(url, "RonSijm.Demo.WonderWharf.Contracts"));
        Assert.DoesNotContain(requests, url => IsAssembly(url, "RonSijm.Demo.WonderWharf"));
        Assert.DoesNotContain(requests, url => IsAssembly(url, "RonSijm.Demo.WonderWharf.Services"));

        await page.GetByTestId("show-events").ClickAsync();
        await Assertions.Expect(page.GetByTestId("event-calendar")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(page.GetByTestId("demo-status")).ToContainTextAsync("has been requested");
        await Assertions.Expect(page.GetByTestId("wharf-producer-code")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("wharf-producer-code")).ToContainTextAsync("@attribute [BlazyComponent(\"wonderwharf.events\")]");
        await Assertions.Expect(page.GetByTestId("wharf-contract-code")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("wharf-contract-code")).ToContainTextAsync("[BlazyContract]");
        await Assertions.Expect(page.GetByTestId("change-title")).ToBeEnabledAsync();
        await Assertions.Expect(page.GetByTestId("restaurant-name")).ToHaveTextAsync("Bob's Burgers");
        await Assertions.Expect(page.GetByTestId("wharf-events")).ToContainTextAsync("2026-10-10: Fall festival");
        await Assertions.Expect(page.GetByTestId("wharf-events")).ToContainTextAsync("2026-10-17: Pier concert");
        await Assertions.Expect(page.GetByTestId("ride-schedule")).ToContainTextAsync("<RideSchedule />");
        await Assertions.Expect(page.GetByTestId("wharf-js")).ToHaveTextAsync("Wonder Wharf JavaScript loaded");
        Assert.Single(requests, url => IsAssembly(url, "RonSijm.Demo.WonderWharf"));
        Assert.Single(requests, url => IsAssembly(url, "RonSijm.Demo.WonderWharf.Services"));
        var wharfRequest = requests.Single(url => IsAssembly(url, "RonSijm.Demo.WonderWharf"));
        Assert.NotEqual("RonSijm.Demo.WonderWharf.wasm", Path.GetFileName(new Uri(wharfRequest).AbsolutePath));
        Assert.Contains(requests, url => url.EndsWith("/blazy-components.json", StringComparison.Ordinal));

        var border = await page.GetByTestId("event-calendar").EvaluateAsync<string>("element => getComputedStyle(element).borderTopWidth");
        Assert.Equal("3px", border);
        Assert.True(await page.Locator("img[alt='Wonder Wharf']").EvaluateAsync<bool>("image => image.complete && image.naturalWidth > 0"));
        await page.GetByTestId("select-date").ClickAsync();
        await Assertions.Expect(page.GetByTestId("selected-date")).ToHaveTextAsync("2026-10-10");
        await page.GetByTestId("change-title").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=event-calendar] h2")).ToHaveTextAsync("Wonder Wharf events for the restaurant team");
        await page.GetByTestId("show-events").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=event-calendar] h2")).ToHaveTextAsync("Upcoming Wonder Wharf events");

        foreach (var width in new[] { 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 844);
            await Assertions.Expect(page.GetByTestId("restaurant-consumer-code")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("wharf-producer-code")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("wharf-contract-code")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("demo-comparison")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("contract-source-code")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("generated-contract-code")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("contracts-reference-code")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByTestId("demo-explanation")).ToContainTextAsync("Network");
            await Assertions.Expect(page.GetByTestId("burger-storefront").Locator("details")).ToHaveCountAsync(0);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"), $"The storefront must not overflow horizontally at {width}px.");
        }

        Assert.Single(requests, url => IsAssembly(url, "RonSijm.Demo.WonderWharf"));
        Assert.Single(requests, url => IsAssembly(url, "RonSijm.Demo.WonderWharf.Services"));
        foreach (var name in new[] { "RonSijm.Blazyload.Components.Build", "RonSijm.Blazyload.Components.SourceGenerator", "RonSijm.Blazyload.Components.SourceGenerator.Build" })
        {
            Assert.DoesNotContain(requests, url => IsAssembly(url, name));
        }
    }

    private static async Task VerifySimpleAsync(IPage page, ConcurrentQueue<string> requests)
    {
        await Assertions.Expect(page.GetByTestId("show-pizza")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync("Burger editor");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync("minimal changes and dependencies");
        await Assertions.Expect(page.GetByTestId("pizza-calendar")).ToHaveCountAsync(0);
        Assert.Contains(requests, url => IsAssembly(url, "RonSijm.Demo.Blazyload.Components.Pizza.Contracts"));
        Assert.DoesNotContain(requests, url => IsAssembly(url, "RonSijm.Demo.Blazyload.Components.Pizza"));
        Assert.DoesNotContain(requests, url => IsAssembly(url, "RonSijm.Demo.Blazyload.Components.Pizza.Services"));
        Assert.DoesNotContain(requests, url => IsAssembly(url, "RonSijm.Blazyload.Components.SourceGenerator"));

        await page.GetByTestId("show-pizza").ClickAsync();
        await Assertions.Expect(page.GetByTestId("pizza-calendar")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 30000 });
        await Assertions.Expect(page.Locator("[data-testid=pizza-calendar] h2")).ToHaveTextAsync("Pizza for burgers");
        await Assertions.Expect(page.GetByTestId("pizza-customer")).ToHaveTextAsync("Burger customer");
        await Assertions.Expect(page.GetByTestId("pizza-dependency")).ToHaveTextAsync("Pizza dependency loaded");
        await Assertions.Expect(page.GetByTestId("pizza-js")).ToHaveTextAsync("Pizza JavaScript loaded");
        Assert.Single(requests, url => IsAssembly(url, "RonSijm.Demo.Blazyload.Components.Pizza"));
        Assert.Single(requests, url => IsAssembly(url, "RonSijm.Demo.Blazyload.Components.Pizza.Services"));
        var pizzaRequest = requests.Single(url => IsAssembly(url, "RonSijm.Demo.Blazyload.Components.Pizza"));
        Assert.NotEqual("RonSijm.Demo.Blazyload.Components.Pizza.wasm", Path.GetFileName(new Uri(pizzaRequest).AbsolutePath));
        Assert.Contains(requests, url => url.EndsWith("/blazy-components.json", StringComparison.Ordinal));
        Assert.Equal("3px", await page.GetByTestId("pizza-calendar").EvaluateAsync<string>("element => getComputedStyle(element).borderTopWidth"));
        Assert.True(await page.Locator("img[alt=Pizza]").EvaluateAsync<bool>("image => image.complete && image.naturalWidth > 0"));

        await page.GetByTestId("select-date").ClickAsync();
        await Assertions.Expect(page.GetByTestId("selected-date")).ToHaveTextAsync("2026-10-01");
        await page.GetByTestId("change-title").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=pizza-calendar] h2")).ToHaveTextAsync("Updated pizza calendar");
        Assert.Single(requests, url => IsAssembly(url, "RonSijm.Demo.Blazyload.Components.Pizza"));
        Assert.Single(requests, url => IsAssembly(url, "RonSijm.Demo.Blazyload.Components.Pizza.Services"));
    }

    private static bool IsAssembly(string url, string name)
    {
        var filename = Path.GetFileName(new Uri(url).AbsolutePath);
        if (!filename.EndsWith(".wasm", StringComparison.Ordinal))
        {
            return false;
        }

        var stem = filename[..^5];
        if (stem == name)
        {
            return true;
        }

        if (!stem.StartsWith($"{name}.", StringComparison.Ordinal))
        {
            return false;
        }

        var fingerprint = stem[(name.Length + 1)..];
        return !fingerprint.Contains('.');
    }
}
