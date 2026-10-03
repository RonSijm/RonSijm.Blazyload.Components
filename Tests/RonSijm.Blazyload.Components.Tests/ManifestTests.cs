using System.Net;
using System.Text;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using TestContext = Xunit.TestContext;

namespace RonSijm.Blazyload.Components.Tests;

public sealed class ManifestTests
{
    [Fact]
    public async Task ReadsManifestRelativeToApplicationBase()
    {
        var assembly = typeof(SimpleComponent).Assembly.GetName().Name;
        var type = typeof(SimpleComponent).FullName;
        var entry = JsonSerializer.Serialize(new { assembly, type });
        using var handler = new ManifestHandler("{\"components\":{\"simple\":" + entry + "}}");
        using var client = new HttpClient(handler);
        using var context = new BunitContext();
        var navigation = new ManifestNavigationManager();
        navigation.SetBaseUri("https://example.test/subapp/");
        context.Services.AddSingleton<Microsoft.AspNetCore.Components.NavigationManager>(navigation);
        context.Services.AddSingleton(client);
        context.Services.AddBlazyloadComponents();
        var provider = context.Services.GetRequiredService<IBlazyComponentManifestProvider>();

        var entries = await provider.GetComponentsAsync(TestContext.Current.CancellationToken);
        Assert.Single(entries);
        Assert.Equal("simple", entries[0].Name);
        Assert.Equal("https://example.test/subapp/blazy-components.json", handler.RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"components\":[]}")]
    [InlineData("{\"components\":{},\"components\":{}}")]
    [InlineData("{\"components\":{\"broken\":{\"assembly\":1,\"type\":\"Type\"}}}")]
    [InlineData("not json")]
    public async Task MalformedManifestHasContext(string json)
    {
        using var handler = new ManifestHandler(json);
        using var client = new HttpClient(handler);
        using var context = new BunitContext();
        context.Services.AddSingleton(client);
        context.Services.AddBlazyloadComponents();

        var provider = context.Services.GetRequiredService<IBlazyComponentManifestProvider>();
        var exception = await Assert.ThrowsAsync<BlazyComponentRegistrationException>(() => provider.GetComponentsAsync(TestContext.Current.CancellationToken));
        Assert.Contains("manifest", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepeatedJsonPropertyIsNotSilentlyOverwritten()
    {
        var assembly = typeof(SimpleComponent).Assembly.GetName().Name;
        var type = typeof(SimpleComponent).FullName;
        var entry = JsonSerializer.Serialize(new { assembly, type });
        using var handler = new ManifestHandler("{\"components\":{\"simple\":" + entry + ",\"simple\":" + entry + "}}");
        using var client = new HttpClient(handler);
        using var context = new BunitContext();
        context.Services.AddSingleton(client);
        context.Services.AddSingleton(RegistryTests.CreateLoader());
        context.Services.AddBlazyloadComponents();
        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        await Assert.ThrowsAsync<BlazyComponentRegistrationException>(async () => await registry.ResolveAsync("simple", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ManualRegistrationDoesNotRequestManifest()
    {
        using var handler = new ManifestHandler("");
        using var client = new HttpClient(handler);
        using var context = new BunitContext();
        context.Services.AddSingleton(client);
        context.Services.AddSingleton(RegistryTests.CreateLoader());
        context.Services.AddBlazyloadComponents(options =>
        {
            options.ManifestPath = null;
            options.Register("manual", typeof(SimpleComponent).Assembly.GetName().Name!, typeof(SimpleComponent).FullName!);
        });

        var registry = context.Services.GetRequiredService<IBlazyComponentRegistry>();
        Assert.Equal(typeof(SimpleComponent), (await registry.ResolveAsync("manual", TestContext.Current.CancellationToken)).ComponentType);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task MissingManifestPreservesHttpFailure()
    {
        using var handler = new ManifestHandler("", HttpStatusCode.NotFound);
        using var client = new HttpClient(handler);
        using var context = new BunitContext();
        context.Services.AddSingleton(client);
        context.Services.AddBlazyloadComponents();
        var provider = context.Services.GetRequiredService<IBlazyComponentManifestProvider>();

        var exception = await Assert.ThrowsAsync<BlazyComponentRegistrationException>(() => provider.GetComponentsAsync(TestContext.Current.CancellationToken));
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    private sealed class ManifestHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }

    }

    private sealed class ManifestNavigationManager : Microsoft.AspNetCore.Components.NavigationManager
    {
        public void SetBaseUri(string value) => Initialize(value, value);

        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }
}
