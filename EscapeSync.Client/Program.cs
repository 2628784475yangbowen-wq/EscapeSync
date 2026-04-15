using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using EscapeSync.Client;
using EscapeSync.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The Blazor WASM client talks to the ASP.NET Core server over HTTP + SignalR.
// Server dev URL is fixed by launchSettings.json -> http://localhost:5088.
const string serverBaseUrl = "http://localhost:5088";
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(serverBaseUrl) });

// Singleton client-side game state + hub connection so pages share them.
builder.Services.AddSingleton(new ServerEndpoint(serverBaseUrl));
builder.Services.AddSingleton<GameClient>();

await builder.Build().RunAsync();
