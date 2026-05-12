using System.Text.Json.Serialization;
using ForexAI.Application;
using ForexAI.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .WithOrigins("http://localhost:3000", "http://localhost:3001")
        .AllowAnyHeader()
        .AllowAnyMethod()));

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(appBuilder => appBuilder.Run(async ctx =>
{
    ctx.Response.ContentType = "application/json";
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var message = ex?.Message ?? "An unexpected error occurred";
    // InvalidOperationException = known domain error (e.g. EA not connected)
    ctx.Response.StatusCode = ex is InvalidOperationException ? 503 : 500;
    await ctx.Response.WriteAsJsonAsync(new { error = message });
}));

app.UseCors();
app.MapControllers();
app.Run("http://localhost:8080");

public partial class Program { }
