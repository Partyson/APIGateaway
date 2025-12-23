var app = WebApplication.Create(args);

app.MapGet("/users/{id}", (string id) => Results.Ok(new
{
    id,
    name = "Ivan Ivanov",
    email = "ivan@mail.com"
}));

app.Run();