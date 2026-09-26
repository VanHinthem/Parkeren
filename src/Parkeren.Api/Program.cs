var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health");
app.MapGet("/api/status", () => Results.Ok(new { status = "ok" }));

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
