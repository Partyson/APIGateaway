var app = WebApplication.Create(args);

app.MapGet("/products/{id}", (string id) => Results.Ok(new
{
    id,
    name = id == "A1" ? "Laptop" : "Mouse",
    price = id == "A1" ? 1200 : 50
}));

app.Run();
