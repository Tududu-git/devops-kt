using Npgsql;
using StackExchange.Redis;
using System.Timers;

namespace WebApplication1
{
    public class WeatherForecast
    {
        public DateOnly Date { get; set; }

        public int TemperatureC { get; set; }

        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);

        public string? Summary { get; set; }
    }

    class Book
    {
        public static Dictionary<int, int> Buruh = new Dictionary<int, int>();
        public string Name { get; set; }

        public int Price { get; set; }

        public string Author { get; set; }

        public string TextLink {  get; set; }

        public int Vievs{  get; set; }

        public Book(string name, int price, string author,string textLink,int views) 
        {
            this.Name = name;
            this.Price = price;
            this.Author = author;
            this.TextLink = textLink;
            this.Vievs = views;
        }
        public static async Task GTAsync(NpgsqlDataSource db)
        {
            foreach (var item in Buruh.ToArray())
            {
                await using var cmd = db.CreateCommand(
                    "UPDATE books SET views = views + $1 WHERE id = $2");
                cmd.Parameters.AddWithValue(item.Value);
                cmd.Parameters.AddWithValue(item.Key);
                await cmd.ExecuteNonQueryAsync();
            }
        }

    }
}

