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
    lc.WriteTo.Console()
      .WriteTo.File("logs/gateway.log", rollingInterval: RollingInterval.Day)
      .ReadFrom.Configuration(ctx.Configuration)
);

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

app.UseSerilogRequestLogging(); // логирует все HTTP запросы автоматически

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapPrometheusScrapingEndpoint();

// Получаем ILogger через DI
var logger = app.Services.GetRequiredService<ILogger<Program>>();

app.MapPost("/auth/login", ([FromBody] LoginRequest request) =>
{
    logger.LogInformation("Login attempt for user {Username}", request?.Username ?? "null");

    if (request is null)
    {
        logger.LogWarning("Login request body is null");
        return Results.BadRequest("Request body is null");
    }

    if (request.Username != "admin" || request.Password != "password")
    {
        logger.LogWarning("Unauthorized login attempt for {Username}", request.Username);
        return Results.Unauthorized();
    }

    var claims = new[]
    {
        new Claim(ClaimTypes.Name, request.Username),
        new Claim(ClaimTypes.Role, "User")
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: creds);
    var jwt = new JwtSecurityTokenHandler().WriteToken(token);

    logger.LogInformation("User {Username} logged in successfully", request.Username);

    return Results.Ok(new { access_token = jwt });
}).AllowAnonymous();

app.MapGet("/api/profile/{userId}", async (
        string userId,
        IDistributedCache cache,
        IHttpClientFactory factory) =>
{
    logger.LogInformation("Fetching profile for userId {UserId}", userId);

    var cacheKey = $"profile:{userId}";
    var cached = await cache.GetStringAsync(cacheKey);

    if (cached != null)
    {
        logger.LogInformation("Cache hit for userId {UserId}", userId);
        return Results.Ok(JsonSerializer.Deserialize<object>(cached));
    }

    logger.LogInformation("Cache miss for userId {UserId}, fetching from services...", userId);

    var users = factory.CreateClient("users");
    var orders = factory.CreateClient("orders");
    var products = factory.CreateClient("products");

    User? user = null;
    try
    {
        user = await users.GetFromJsonAsync<User>($"/users/{userId}");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error fetching user {UserId}", userId);
        return Results.Problem("User service error");
    }

    if (user == null)
    {
        logger.LogWarning("User {UserId} not found", userId);
        return Results.NotFound();
    }

    List<Order> userOrders = new();
    try
    {
        userOrders = await orders.GetFromJsonAsync<List<Order>>($"/orders/user/{userId}") ?? new List<Order>();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error fetching orders for user {UserId}", userId);
    }

    foreach (var o in userOrders)
    {
        try
        {
            o.Product = await products.GetFromJsonAsync<Product>($"/products/{o.ProductId}");
        }
        catch
        {
            o.Product = new Product(o.ProductId, "Unknown product", 0);
            logger.LogWarning("Product {ProductId} not found, using fallback", o.ProductId);
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

    logger.LogInformation("Profile for userId {UserId} returned successfully", userId);

    return Results.Ok(result);
})
.RequireAuthorization()
.RequireRateLimiting("default");

app.Run();
