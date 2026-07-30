using MuriloAI.Backend.Application.Jobs;
using MuriloAI.Backend.Application.UseCases.ProcessImageWithEngine;
using MuriloAI.Backend.Domain.Contracts;
using MuriloAI.Backend.Infrastructure.Engines;
using MuriloAI.Backend.Infrastructure.Jobs;
using MuriloAI.Backend.Services;
using Microsoft.Extensions.Options;

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
builder.Services.AddSingleton<IImageProcessingJobStore, InMemoryImageProcessingJobStore>();
builder.Services.AddSingleton<IImageProcessingJobQueue, ChannelImageProcessingJobQueue>();
builder.Services.AddScoped<IEngineGateway, PythonEngineGateway>();
builder.Services.AddScoped<IProcessImageWithEngineUseCase, ProcessImageWithEngineUseCase>();
builder.Services.Configure<ImageJobsOptions>(builder.Configuration.GetSection(ImageJobsOptions.SectionName));
builder.Services.AddHostedService<ImageProcessingBackgroundService>();

var app = builder.Build();

app.UseCors("AllowAngularDev");

app.MapControllers();

app.Run();

public partial class Program;
