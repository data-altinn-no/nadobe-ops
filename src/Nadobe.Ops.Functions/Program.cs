using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Nadobe.Ops.Functions.KeyVault;
using Nadobe.Ops.Functions.Slack;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton(TimeProvider.System);

// Options bind from the configuration root, so each property is a flat app setting.
builder.Services.Configure<KeyVaultExpiryOptions>(builder.Configuration);
builder.Services.Configure<SlackOptions>(builder.Configuration);

builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());
builder.Services.AddSingleton<IKeyVaultInventory, AzureKeyVaultInventory>();
builder.Services.AddSingleton<KeyVaultExpiryScanner>();

builder.Services
    .AddHttpClient<ISlackNotifier, SlackWebhookNotifier>(client => client.Timeout = TimeSpan.FromSeconds(15));

await builder.Build().RunAsync();
