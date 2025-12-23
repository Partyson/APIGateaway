var app = WebApplication.Create(args);

app.MapGet("/orders/user/{userId}", (string userId) =>
{
    return Results.Ok(new[]
    {
        new
        {
            orderId = "101",
            productId = "A1",
            quantity = 2
        },
        new
        {
            orderId = "102",
            productId = "B2",
            quantity = 1
        }
    });
});

app.Run();