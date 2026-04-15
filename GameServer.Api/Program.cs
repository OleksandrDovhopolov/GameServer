using GameServer.Api.Services;
using GameServer.Api.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IPackSelectionStrategy, PackSelectionStrategy>();
builder.Services.AddScoped<IPackService, PackService>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.MapGet("/ping", () => "pong");

app.Run();