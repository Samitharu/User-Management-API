using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.UseHttpsRedirection();

// 1. Error-handling middleware
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Unhandled exception: {ex.Message}");
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new { error = "Internal server error." });
    }
});

// 2. Authentication middleware
app.Use(async (context, next) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var token) ||
        token.ToString() != "Bearer valid-token")
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Unauthorized" });
        return;
    }

    await next();
});

// 3. Logging middleware
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    var path = context.Request.Path;

    await next();

    var statusCode = context.Response.StatusCode;
    Console.WriteLine($"[{DateTime.UtcNow}] {method} {path} => {statusCode}");
});

// In-memory dictionary to store users
var users = new Dictionary<int, User>
{
    { 1, new User { Id = 1, Name = "Alice", Email = "alice@example.com" } },
    { 2, new User { Id = 2, Name = "Bob", Email = "bob@example.com" } }
};
var nextId = users.Keys.Max() + 1;

// Root endpoint
app.MapGet("/", () => "Welcome to the User Management API!");

// GET: Retrieve all users (with optional pagination)
app.MapGet("/users", (int? page, int? pageSize) =>
{
    var allUsers = users.Values.AsQueryable();

    if (page.HasValue && pageSize.HasValue && page > 0 && pageSize > 0)
    {
        allUsers = allUsers
            .Skip((page.Value - 1) * pageSize.Value)
            .Take(pageSize.Value);
    }

    return Results.Ok(allUsers);
})
.WithName("GetUsers");

// GET: Retrieve a user by ID
app.MapGet("/users/{id}", (int id) =>
{
    if (id <= 0) return Results.BadRequest("Invalid user ID.");

    return users.TryGetValue(id, out var user)
        ? Results.Ok(user)
        : Results.NotFound($"User with ID {id} not found.");
})
.WithName("GetUserById");

// POST: Add a new user
app.MapPost("/users", (User newUser) =>
{
    var validationResults = new List<ValidationResult>();
    var context = new ValidationContext(newUser);

    if (!Validator.TryValidateObject(newUser, context, validationResults, true))
        return Results.BadRequest(validationResults.Select(v => v.ErrorMessage));

    newUser.Id = nextId++;
    users[newUser.Id] = newUser;
    return Results.Created($"/users/{newUser.Id}", newUser);
})
.WithName("CreateUser");

// PUT: Update an existing user
app.MapPut("/users/{id}", (int id, User updatedUser) =>
{
    if (!users.ContainsKey(id))
        return Results.NotFound($"User with ID {id} not found.");

    var validationResults = new List<ValidationResult>();
    var context = new ValidationContext(updatedUser);

    if (!Validator.TryValidateObject(updatedUser, context, validationResults, true))
        return Results.BadRequest(validationResults.Select(v => v.ErrorMessage));

    updatedUser.Id = id;
    users[id] = updatedUser;
    return Results.Ok(updatedUser);
})
.WithName("UpdateUser");

// DELETE: Remove a user by ID
app.MapDelete("/users/{id}", (int id) =>
{
    if (!users.Remove(id))
        return Results.NotFound($"User with ID {id} not found.");

    return Results.NoContent();
})
.WithName("DeleteUser");

// SEARCH: Find users by name or email
app.MapGet("/users/search/{term}", (string term) =>
{
    var results = users.Values
        .Where(u => u.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                 || u.Email.Contains(term, StringComparison.OrdinalIgnoreCase));
    return Results.Ok(results);
})
.WithName("SearchUsers");

app.Run();

// User class with validations
public class User
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Name is required.")]
    [StringLength(50, ErrorMessage = "Name cannot exceed 50 characters.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Invalid email format.")]
    public string Email { get; set; } = string.Empty;
}