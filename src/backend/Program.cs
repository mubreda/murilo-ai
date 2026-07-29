using MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;
using MuriloAI.Backend.Domain.Contracts;
using MuriloAI.Backend.Infrastructure.Engines;
using MuriloAI.Backend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularDev", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddSingleton<PythonEngineRunner>();
builder.Services.AddScoped<IEngineGateway, PythonEngineGateway>();
builder.Services.AddScoped<IProcessImageWithEngineUseCase, ProcessImageWithEngineUseCase>();

var app = builder.Build();

app.UseCors("AllowAngularDev");

app.MapControllers();

app.Run();

public partial class Program;
