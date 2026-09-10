using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Bridges;
using Bridges.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Puzzle pack loading and progress tracking. No in-app generation any more — puzzles come from
// the pre-generated pack shipped in wwwroot.
builder.Services.AddScoped<PackService>();
builder.Services.AddScoped<ProgressStore>();

var host = builder.Build();

// Load the pack and the player's solved-progress before the UI needs them.
await host.Services.GetRequiredService<PackService>().LoadAsync();
await host.Services.GetRequiredService<ProgressStore>().LoadAsync();

await host.RunAsync();
