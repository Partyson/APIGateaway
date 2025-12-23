using System.Text.Json;
using APIGateaway.Models;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) =>
    lc.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddStackExchangeRedisCache(opt =>
{
    opt.Configuration = "redis:6379";
});

builder.Services.AddHttpClient("users", client =>
    {
        client.BaseAddress = new Uri("http://user-service");
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);

        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    });

builder.Services.AddHttpClient("orders", client =>
    {
        client.BaseAddress = new Uri("http://order-service");
    })
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient("products", client =>
    {
        client.BaseAddress = new Uri("http://product-service");
    })
    .AddStandardResilienceHandler();

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            ValidateIssuerSigningKey = false
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(opt =>
{
    opt.AddFixedWindowLimiter("default", o =>
    {
        o.Window = TimeSpan.FromSeconds(10);
        o.PermitLimit = 5;
    });
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/api/profile/{userId}", async (
        string userId,
        IDistributedCache cache,
        IHttpClientFactory factory) =>
    {
        var cacheKey = $"profile:{userId}";
        var cached = await cache.GetStringAsync(cacheKey);

        if (cached != null)
            return Results.Ok(JsonSerializer.Deserialize<object>(cached));

        var users = factory.CreateClient("users");
        var orders = factory.CreateClient("orders");
        var products = factory.CreateClient("products");

        var user = await users.GetFromJsonAsync<User>($"/users/{userId}");
        if (user == null)
            return Results.NotFound();

        var userOrders =
            await orders.GetFromJsonAsync<List<Order>>($"/orders/user/{userId}")
            ?? [];

        foreach (var o in userOrders)
        {
            o.Product = await products
                .GetFromJsonAsync<Product>($"/products/{o.ProductId}");
        }

        var result = new { user, orders = userOrders };

        await cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(result),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
            });

        return Results.Ok(result);
    })
    .RequireAuthorization()
    .RequireRateLimiting("default");

app.Run();