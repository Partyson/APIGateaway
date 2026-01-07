using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using APIGateaway.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
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
        client.BaseAddress = new Uri("http://user-service:8080");
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
        client.BaseAddress = new Uri("http://order-service:8080");
    })
    .AddStandardResilienceHandler();

builder.Services.AddHttpClient("products", client =>
    {
        client.BaseAddress = new Uri("http://product-service:8080");
    })
    .AddStandardResilienceHandler();

const string jwtKey = "super-secret-key-12345123=0457y123-04587y12354";

builder.Services.AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey))
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

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation();
        metrics.AddHttpClientInstrumentation();
        metrics.AddPrometheusExporter();
    });

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapPrometheusScrapingEndpoint();

app.MapPost("/auth/login", ([FromBody] LoginRequest request) =>
{
    if (request is null)
        return Results.BadRequest("Request body is null");
    if (request.Username != "admin" || request.Password != "password")
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, request.Username),
        new Claim(ClaimTypes.Role, "User")
    };

    var key = new SymmetricSecurityKey(
        Encoding.UTF8.GetBytes(jwtKey));

    var creds = new SigningCredentials(
        key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddMinutes(30),
        signingCredentials: creds);

    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    return Results.Ok(new { access_token = jwt });
}).AllowAnonymous();

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
            try
            {
                o.Product = await products
                    .GetFromJsonAsync<Product>($"/products/{o.ProductId}");
            }
            catch
            {
                o.Product = new Product(o.ProductId, "Unknown product", 0);
            }
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