using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;

public class Program
{
    static HttpClient httpClient = new HttpClient();

    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Please provide a link as a command-line argument.");
            return;
        }

        var link = args[0];
        var itemDetails = await FetchItemDetails(link);
        if (itemDetails != null)
        {
            Console.WriteLine("Item Details:");
            Console.WriteLine($"Float Value: {itemDetails.FloatValue}");
            Console.WriteLine($"Paint Seed: {itemDetails.PaintSeed}");
            Console.WriteLine($"Paint Index: {itemDetails.PaintIndex}");
            if (itemDetails.Stickers != null)
            {
                foreach (var sticker in itemDetails.Stickers)
                {
                    Console.WriteLine($"Sticker: {sticker.Name}, Wear: {sticker.Wear}");
                }
            }
        }
        else
        {
            Console.WriteLine("Error fetching item details.");
        }

        Console.ReadLine();
    }

    private static async Task<ItemDetails> FetchItemDetails(string link)
    {
        var url = $"https://api.csgofloat.com/?url={Uri.EscapeDataString(link)}";

        try
        {
            Console.WriteLine($"Fetching item details from URL: {url}");
            var response = await httpClient.GetStringAsync(url);
            Console.WriteLine($"Response from CSGOFloat API: {response}");
            var json = JObject.Parse(response);

            if (json["error"] != null)
            {
                Console.WriteLine("Error in API response: " + json["error"]);
                return null;
            }

            var itemDetails = new ItemDetails
            {
                FloatValue = json["iteminfo"]?["floatvalue"]?.Value<float>() ?? 0,
                PaintSeed = json["iteminfo"]?["paintseed"]?.Value<int>() ?? 0,
                PaintIndex = json["iteminfo"]?["paintindex"]?.Value<int>() ?? 0,
                Stickers = json["iteminfo"]?["stickers"]?.Select(s => new Sticker
                {
                    Name = s["name"]?.Value<string>() ?? string.Empty,
                    Wear = s["wear"]?.Value<float>() ?? 0
                }).ToArray()
            };

            return itemDetails;
        }
        catch (HttpRequestException httpEx)
        {
            Console.WriteLine($"HTTP request error: {httpEx.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching item details: {ex.Message}");
            return null;
        }
    }
}

public class ItemDetails
{
    public float FloatValue { get; set; }
    public int PaintSeed { get; set; }
    public int PaintIndex { get; set; }
    public Sticker[] Stickers { get; set; } = Array.Empty<Sticker>();
}

public class Sticker
{
    public string Name { get; set; } = string.Empty;
    public float Wear { get; set; }
}
