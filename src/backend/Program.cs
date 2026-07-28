using MuriloAI.Backend.Engines;
using MuriloAI.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddHttpClient<IImageEngine, FalImageEngine>();
builder.Services.AddSingleton<IImageEnhancementService, ImageEnhancementService>();

var app = builder.Build();

app.UseCors("AllowAngularDev");

app.MapPost("/api/image/enhance", async (IImageEnhancementService enhancer, IImageEngine engine, CancellationToken cancellationToken) =>
{
    var success = await enhancer.EnhanceImageAsync(engine, cancellationToken);
    return Results.Json(new { success });
});

app.Run();
