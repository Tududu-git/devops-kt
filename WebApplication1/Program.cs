using Npgsql;
using System.Security.Cryptography;
using System.Xml.Linq;
using WebApplication1;
using StackExchange.Redis;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default не задана");

builder.Services.AddNpgsqlDataSource(connectionString);

var redisConnection =
    builder.Configuration["Redis:ConnectionString"]
    ?? throw new InvalidOperationException("Redis:ConnectionString не задан");
builder.Services.AddSingleton<IConnectionMultiplexer>(
    _ => ConnectionMultiplexer.Connect(redisConnection));

var app = builder.Build();

var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();

const string initSql = """
    CREATE TABLE IF NOT EXISTS books (
        id     SERIAL PRIMARY KEY,
        name   TEXT NOT NULL,
        price  INT  NOT NULL,
        author TEXT NOT NULL
    );
    """;

for (var attempt = 1; ; attempt++)
{
    try
    {
        await using var cmd = dataSource.CreateCommand(initSql);
        await cmd.ExecuteNonQueryAsync();
        break;
    }
    catch (NpgsqlException) when (attempt < 10)
    {
        app.Logger.LogWarning("БД ещё не готова, попытка {Attempt}/10", attempt);
        await Task.Delay(2000);
    }
}

app.MapGet("/bookstore/books/all", async (NpgsqlDataSource db, IConnectionMultiplexer redis) =>
{
    try
    {
        var cache = redis.GetDatabase();

        var rows = new List<(int Id, int Price, string Author)>();

        await using (var cmd = db.CreateCommand(
            "SELECT id, price, author FROM books"))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetInt32(reader.GetOrdinal("id")),
                    reader.GetInt32(reader.GetOrdinal("price")),
                    reader.GetString(reader.GetOrdinal("author"))
                ));
            }
        }

        var books = new List<Book>();

        foreach (var row in rows)
        {
            var cacheKey = $"book:name:{row.Id}";
            var cachedName = await cache.StringGetAsync(cacheKey);

            string name;

            if (cachedName.HasValue)
            {
                name = cachedName.ToString();
            }
            else
            {
                await using var nameCmd = db.CreateCommand("SELECT name FROM books WHERE id = $1");
                nameCmd.Parameters.AddWithValue(row.Id);

                var result = await nameCmd.ExecuteScalarAsync();

                if (result is not string dbName)
                    continue;

                name = dbName;

                await cache.StringSetAsync(
                    cacheKey,
                    name,
                    expiry: TimeSpan.FromMinutes(5));
            }

            books.Add(new Book(name, row.Price, row.Author));
        }

        return Results.Ok(books);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex);
    }
});
app.MapGet("/bookstore/books/search/name/{name}", async (string name, NpgsqlDataSource db, IConnectionMultiplexer redis ) =>
{
    try
    {
        var cache = redis.GetDatabase();
        var rows = new List<(int Id, int Price, string Author)>();
        await using var cmd = db.CreateCommand("SELECT id, price, author FROM books WHERE name = $1");
        cmd.Parameters.AddWithValue(name);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new(
                reader.GetInt32(reader.GetOrdinal("id")),
                reader.GetInt32(reader.GetOrdinal("price")),
                reader.GetString(reader.GetOrdinal("author"))));
        }
        var a = new List<Book>();
        foreach (var row in rows)
        {
            var cacheKey = $"book:name:{row.Id}";
            var cachedName = await cache.StringGetAsync(cacheKey);
            string redisName;
            if (cachedName.HasValue)
            {
                redisName = cachedName.ToString();
            }
            else
            {
                await using var nameCmd = db.CreateCommand("SELECT name FROM books WHERE id = $1");
                nameCmd.Parameters.AddWithValue(row.Id);

                var result = await nameCmd.ExecuteScalarAsync();

                if (result is not string dbName)
                    continue;
                redisName = dbName;
                await cache.StringSetAsync(
                    cacheKey,
                    redisName,
                    expiry: TimeSpan.FromMinutes(5));
            }
            a.Add(new(redisName, row.Price, row.Author));
        }
        if (a.Count > 0)
            return Results.Ok(a);
        else
            return Results.NotFound(new { message = $"книги с таким названием нету"});
    }catch(Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});
app.MapGet("/bookstore/books/search/price/{price}", async (int price, NpgsqlDataSource db, IConnectionMultiplexer redis) =>
{
    try
    {
        var cache = redis.GetDatabase();
        var rows = new List<(int Id, int Price, string Author)>();
        await using var cmd = db.CreateCommand("SELECT id, price, author FROM books WHERE price = $1");
        cmd.Parameters.AddWithValue(price);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new(
                reader.GetInt32(reader.GetOrdinal("id")),
                reader.GetInt32(reader.GetOrdinal("price")),
                reader.GetString(reader.GetOrdinal("author"))));
        }
        var a = new List<Book>();
        foreach (var row in rows)
        {
            var cacheKey = $"book:name:{row.Id}";
            var cachedName = await cache.StringGetAsync(cacheKey);
            string redisName;
            if (cachedName.HasValue)
            {
                redisName = cachedName.ToString();
            }
            else
            {
                await using var nameCmd = db.CreateCommand("SELECT name FROM books WHERE id = $1");
                nameCmd.Parameters.AddWithValue(row.Id);

                var result = await nameCmd.ExecuteScalarAsync();

                if (result is not string dbName)
                    continue;
                redisName = dbName;
                await cache.StringSetAsync(
                    cacheKey,
                    redisName,
                    expiry: TimeSpan.FromMinutes(5));
            }
            a.Add(new(redisName, row.Price, row.Author));
        }
        if (a.Count > 0)
            return Results.Ok(a);
        else
            return Results.NotFound(new { message = $"книги с такой ценой нету" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});
app.MapGet("/bookstore/books/search/author/{author}",async (string author, NpgsqlDataSource db, IConnectionMultiplexer redis) =>
{
    try
    {
        var cache = redis.GetDatabase();
        var rows = new List<(int Id, int Price, string Author)>();
        await using var cmd = db.CreateCommand("SELECT id, price, author FROM books WHERE author = $1");
        cmd.Parameters.AddWithValue(author);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(new(
                reader.GetInt32(reader.GetOrdinal("id")),
                reader.GetInt32(reader.GetOrdinal("price")),
                reader.GetString(reader.GetOrdinal("author"))));
        }
        var a = new List<Book>();
        foreach (var row in rows)
        {
            var cacheKey = $"book:name:{row.Id}";
            var cachedName = await cache.StringGetAsync(cacheKey);
            string redisName;
            if (cachedName.HasValue)
            {
                redisName = cachedName.ToString();
            }
            else
            {
                await using var nameCmd = db.CreateCommand("SELECT name FROM books WHERE id = $1");
                nameCmd.Parameters.AddWithValue(row.Id);

                var result = await nameCmd.ExecuteScalarAsync();

                if (result is not string dbName)
                    continue;
                redisName = dbName;
                await cache.StringSetAsync(
                    cacheKey,
                    redisName,
                    expiry: TimeSpan.FromMinutes(5));
            }
            a.Add(new(redisName, row.Price, row.Author));
        }
        if (a.Count > 0)
            return Results.Ok(a);
        else
            return Results.NotFound(new { message = $"книги с таким названием нету" });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.ToString());
    }
});
app.MapPost("/bookstore/books/add", async (Book newBook, NpgsqlDataSource db) =>
{
    var errors = new List<string>();
    if (string.IsNullOrWhiteSpace(newBook.Name))
        errors.Add("название книги отсутствует");
    if (string.IsNullOrWhiteSpace(newBook.Author))
        errors.Add("автор книги отсутствует");
    if (newBook.Price <= 0)
        errors.Add("цена 0 или отрицательная");

    if (errors.Count > 0)
        return Results.BadRequest(new { errors });

    try
    {
        await using var cmd = db.CreateCommand(
            "INSERT INTO books (name, price, author) VALUES ($1, $2, $3)");
        cmd.Parameters.AddWithValue(newBook.Name);
        cmd.Parameters.AddWithValue(newBook.Price);
        cmd.Parameters.AddWithValue(newBook.Author);
        await cmd.ExecuteNonQueryAsync();

        return Results.Created($"/bookstore/books/search/name/{newBook.Name}", newBook);
    }
    catch (Exception ex)
    {
        return Results.Problem(Convert.ToString(ex));
    }
});
app.MapDelete("/bookstore/books/delete/{name}", async (string name, NpgsqlDataSource db) =>
{
    await using var cmd = db.CreateCommand("DELETE FROM books WHERE name = $1");
    cmd.Parameters.AddWithValue(name);
    int affected = await cmd.ExecuteNonQueryAsync();
    if (affected == 0)
        return Results.NotFound(new { message = $"книги с названием {name} нету" });
    return Results.Ok(new { deleted = affected });
});
app.MapPut("/bookstore/books/change/{ID}", async (Book newBook, int ID, NpgsqlDataSource db, IConnectionMultiplexer redis) =>
{
var errors = new List<string>();
if (string.IsNullOrWhiteSpace(newBook.Name))
    errors.Add("название книги отсутствует");
if (string.IsNullOrWhiteSpace(newBook.Author))
    errors.Add("автор книги отсутствует");
if (newBook.Price <= 0)
    errors.Add("цена 0 или отрицательная");

if (errors.Count > 0)
    return Results.BadRequest(new { errors });
try
{
        var cache = redis.GetDatabase();
        var cacheKey = $"book:name:{ID}";
        await using var cmd = db.CreateCommand("UPDATE Books SET name = $1, price = $2, author = $3 WHERE id = $4;");
    cmd.Parameters.AddWithValue(newBook.Name);
    cmd.Parameters.AddWithValue(newBook.Price);
    cmd.Parameters.AddWithValue(newBook.Author);
    cmd.Parameters.AddWithValue(ID);
    int affected = await cmd.ExecuteNonQueryAsync();
    await cache.StringSetAsync(cacheKey, newBook.Name,expiry:TimeSpan.FromMinutes(5));
    if (affected == 0)
        return Results.NotFound(new { message = $"книги с id {ID} нету" });
    return Results.Ok(newBook);

    }
    catch (Exception ex) 
    {
        return Results.Problem(Convert.ToString(ex));
    }
});
app.Run();



