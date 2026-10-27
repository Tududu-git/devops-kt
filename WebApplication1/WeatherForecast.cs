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
        public string Name { get; set; }

        public int Price { get; set; }

        public string Author { get; set; }

        public Book(string name, int price, string author) 
        {
            this.Name = name;
            this.Price = price;
            this.Author = author;
        }

    }
}
