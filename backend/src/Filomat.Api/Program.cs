using Filomat.Api.Data;
using Filomat.Api.Catalog;
using Filomat.Api.Endpoints;
using Filomat.Api.Printers;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FilomatDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Filomat") ?? "Data Source=filomat.db"));
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient<MoonrakerAdapter>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddTransient<IPrinterAdapter>(sp => sp.GetRequiredService<MoonrakerAdapter>());
builder.Services.AddHttpClient<SnapmakerU1Adapter>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddTransient<IPrinterAdapter>(sp => sp.GetRequiredService<SnapmakerU1Adapter>());
builder.Services.AddHttpClient<PrusaLinkAdapter>(client => client.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddTransient<IPrinterAdapter>(sp => sp.GetRequiredService<PrusaLinkAdapter>());
builder.Services.AddHttpClient<IAnycubicLan, AnycubicLan>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddTransient<IPrinterAdapter, AnycubicAdapter>();
builder.Services.AddScoped<ConsumptionSync>();
builder.Services.AddScoped<SpoolStock>();
builder.Services.AddHttpClient(nameof(SpoolWeightCatalog), client => client.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton(sp => new SpoolWeightCatalog(
    sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SpoolWeightCatalog)),
    sp.GetRequiredService<IConfiguration>(),
    sp.GetRequiredService<ILogger<SpoolWeightCatalog>>()));
builder.Services.AddHostedService<PrinterSyncService>();
builder.Services.AddOpenApi();

var app = builder.Build();

await DatabaseInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();
app.MapOpenApi();

// Optionaler API-Schlüssel: ist "Filomat:ApiKey" gesetzt, muss jeder /api-Aufruf ihn im Header X-Api-Key mitschicken.
var apiKey = app.Configuration["Filomat:ApiKey"];
var api = app.MapGroup("/api");
if (!string.IsNullOrEmpty(apiKey))
{
    api.AddEndpointFilter(async (context, next) =>
        context.HttpContext.Request.Headers["X-Api-Key"] == apiKey
            ? await next(context)
            : TypedResults.Unauthorized());
}

api.MapContainerEndpoints();
api.MapItemEndpoints();
api.MapPrinterEndpoints();
api.MapSyncEndpoints();
api.MapSpoolEndpoints();
api.MapUpdateEndpoints();
api.MapGet("/spool-weights", (SpoolWeightCatalog catalog, CancellationToken cancellationToken) => catalog.GetAsync(cancellationToken))
    .WithTags("Katalog")
    .WithSummary("Leergewichte von Spulen pro Hersteller (SpoolmanDB)");
app.MapGet("/health", () => TypedResults.Ok("ok"));

app.Run();

public partial class Program;
