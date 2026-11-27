using Microsoft.AspNetCore.Mvc.Razor.Compilation;
using Microsoft.AspNetCore.SignalR;
using Npgsql;
using StackExchange.Redis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using WebApplication1;
using System.Timers;

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

ConnectionMultiplexer redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");

var app = builder.Build();

var dataSource = app.Services.GetRequiredService<NpgsqlDataSource>();

const string initSql = """
    CREATE TABLE IF NOT EXISTS books (
        id        SERIAL PRIMARY KEY,
        name      TEXT NOT NULL,
        price     INT  NOT NULL,
        author    TEXT NOT NULL,
        textLink  TEXT NOT NULL,
        views int default 0
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

var timer = new System.Timers.Timer(600000);
timer.Elapsed += async (sender, EventArgs) =>
{
    await Book.GTAsync(dataSource);
};

app.MapGet("/bookstore/books/all", async (NpgsqlDataSource db, IConnectionMultiplexer redis) =>
{
    try
    {
        var cache = redis.GetDatabase();

        var rows = new List<(int Id, int Price, string Author, string Name,int views)>();

        await using (var cmd = db.CreateCommand(
            "SELECT id, price, author, name, views FROM books"))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((
                    reader.GetInt32(reader.GetOrdinal("id")),
                    reader.GetInt32(reader.GetOrdinal("price")),
                    reader.GetString(reader.GetOrdinal("author")),
                    reader.GetString(reader.GetOrdinal("name")),
                    reader.GetInt32(reader.GetOrdinal("views"))
                ));
            }
        }

        var books = new List<Book>();
        foreach (var row in rows)
        {
            var cacheKey = $"book:textLink:{row.Id}";
            var cachedName = await cache.StringGetAsync(cacheKey);

            string link;

            if (cachedName.HasValue)
            {
                link = cachedName.ToString();
            }
            else
            {
                await using var nameCmd = db.CreateCommand("SELECT textLink FROM books WHERE id = $1");
                nameCmd.Parameters.AddWithValue(row.Id);

                var result = await nameCmd.ExecuteScalarAsync();

                if (result is not string dbLink)
                    continue;

                link = dbLink;

                await cache.StringSetAsync(
                    cacheKey,
                    link,
                    expiry: TimeSpan.FromMinutes(5));
            }

            books.Add(new Book(row.Name, row.Price, row.Author,link,row.views));
        }

        return Results.Ok(books);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(ex);
    }
});
 
app.MapGet("/bookstore/books/search/price/{price}", async (int price, NpgsqlDataSource db, IConnectionMultiplexer redis) =>
{
    try
    {
        var cache = redis.GetDatabase();
        var rows = new List<(int Id, int Price, string Author, string Name, int views)>();
        await using var cmd = db.CreateCommand("SELECT id, price, author, name, views FROM books WHERE price = $1");
        cmd.Parameters.AddWithValue(price);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                reader.GetInt32(reader.GetOrdinal("id")),
                reader.GetInt32(reader.GetOrdinal("price")),
                reader.GetString(reader.GetOrdinal("author")),
                reader.GetString(reader.GetOrdinal("name")),
                reader.GetInt32(reader.GetOrdinal("views"))
                   ));
        }
        var a = new List<Book>();
        foreach (var row in rows)
        {
            var cacheKey = $"book:textLink:{row.Id}";
            var cachedName = await cache.StringGetAsync(cacheKey);
            string redisLink;
            if (cachedName.HasValue)
            {
                redisLink = cachedName.ToString();
            }
            else
            {
                await using var nameCmd = db.CreateCommand("SELECT textLink FROM books WHERE id = $1");
                nameCmd.Parameters.AddWithValue(row.Id);

                var result = await nameCmd.ExecuteScalarAsync();

                if (result is not string dbLink)
                    continue;
                redisLink = dbLink;
                await cache.StringSetAsync(
                    cacheKey,
                    redisLink,
                    expiry: TimeSpan.FromMinutes(5));
            }
            a.Add(new(row.Name, row.Price, row.Author,redisLink,row.views));
        }
        if (a.Count == 1)
        {
            int b = rows.Find(n => n.Name == a[0].Name).Id;
            if (Book.Buruh.ContainsKey(b))
            {
                Book.Buruh[b] += 1;
            }
            else
            {
                Book.Buruh.Add(b, 1);
            }
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
        var rows = new List<(int Id, int Price, string Author, string Name, int views)>();
        await using var cmd = db.CreateCommand("SELECT id, price, author, name, views FROM books WHERE author = $1");
        cmd.Parameters.AddWithValue(author);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((
                     reader.GetInt32(reader.GetOrdinal("id")),
                     reader.GetInt32(reader.GetOrdinal("price")),
                     reader.GetString(reader.GetOrdinal("author")),
                     reader.GetString(reader.GetOrdinal("name")),
                     reader.GetInt32(reader.GetOrdinal("views"))
                ));
        }
        var a = new List<Book>();
        foreach (var row in rows)
        {
            var cacheKey = $"book:textLink:{row.Id}";
            var cachedName = await cache.StringGetAsync(cacheKey);
            string redisLink;
            if (cachedName.HasValue)
            {
                redisLink = cachedName.ToString();
            }
            else
            {
                await using var nameCmd = db.CreateCommand("SELECT name FROM books WHERE id = $1");
                nameCmd.Parameters.AddWithValue(row.Id);

                var result = await nameCmd.ExecuteScalarAsync();

                if (result is not string dbLink)
                    continue;
                redisLink = dbLink;
                await cache.StringSetAsync(
                    cacheKey,
                    redisLink,
                    expiry: TimeSpan.FromMinutes(5));
            }
            a.Add(new(row.Name, row.Price, row.Author,redisLink,row.views));
        }
        if (a.Count == 1)
        {
            int b = rows.Find(n => n.Name == a[0].Name).Id;
            if (Book.Buruh.ContainsKey(b))
            {
                Book.Buruh[b] += 1;
            }
            else
            {
                Book.Buruh.Add(b, 1);
            }
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
    if (string.IsNullOrWhiteSpace(newBook.TextLink))
        errors.Add("ссылка отсутствует");

    if (errors.Count > 0)
        return Results.BadRequest(new { errors });

    try
    {
        await using var cmd = db.CreateCommand(
            "INSERT INTO books (name, price, author, textLink) VALUES ($1, $2, $3, $4)");
        cmd.Parameters.AddWithValue(newBook.Name);
        cmd.Parameters.AddWithValue(newBook.Price);
        cmd.Parameters.AddWithValue(newBook.Author);
        cmd.Parameters.AddWithValue(newBook.TextLink);
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
if (string.IsNullOrWhiteSpace(newBook.TextLink))
    errors.Add("ссылка дерьмо");

if (errors.Count > 0)
    return Results.BadRequest(new { errors });
try
{
        var cache = redis.GetDatabase();
        var cacheKey = $"book:textLink:{ID}";
        await using var cmd = db.CreateCommand("UPDATE Books SET name = $1, price = $2, author = $3, textLink = $4 WHERE id = $5;");
    cmd.Parameters.AddWithValue(newBook.Name);
    cmd.Parameters.AddWithValue(newBook.Price);
    cmd.Parameters.AddWithValue(newBook.Author);
    cmd.Parameters.AddWithValue(newBook.TextLink);
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



