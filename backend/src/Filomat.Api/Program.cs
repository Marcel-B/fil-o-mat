using Filomat.Api.Data;
using Filomat.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<FilomatDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Filomat") ?? "Data Source=filomat.db"));
builder.Services.AddProblemDetails();
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
app.MapGet("/health", () => TypedResults.Ok("ok"));

app.Run();

public partial class Program;
