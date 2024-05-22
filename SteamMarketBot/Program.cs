using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class Program
{
    static HttpClient httpClient = new HttpClient();
    static string steamApiKey = "4C93431B3DB7CEA9C550DBBE65F3A784"; // Замените на ваш Steam API ключ

    public static void Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseKestrel()
                          .ConfigureServices(services => services.AddControllers())
                          .Configure(app =>
                          {
                              app.UseRouting();
                              app.UseEndpoints(endpoints =>
                              {
                                  endpoints.MapGet("/api/item", async context =>
                                  {
                                      var link = context.Request.Query["link"];
                                      if (string.IsNullOrEmpty(link))
                                      {
                                          context.Response.StatusCode = 400;
                                          await context.Response.WriteAsync("Invalid link format.");
                                          return;
                                      }

                                      Console.WriteLine($"Received link: {link}");
                                      var instanceId = ParseInstanceIdFromLink(link);
                                      if (string.IsNullOrEmpty(instanceId))
                                      {
                                          context.Response.StatusCode = 400;
                                          await context.Response.WriteAsync("Invalid link format.");
                                          return;
                                      }

                                      Console.WriteLine($"Parsed instance ID: {instanceId}");
                                      var itemDetails = await FetchItemDetails(instanceId);
                                      if (itemDetails != null)
                                      {
                                          await context.Response.WriteAsJsonAsync(itemDetails);
                                      }
                                      else
                                      {
                                          context.Response.StatusCode = 500;
                                          await context.Response.WriteAsync("Error fetching item details.");
                                      }
                                  });
                              });
                          });
            })
            .Build();

        host.Run();
    }

    private static string? ParseInstanceIdFromLink(string link)
    {
        try
        {
            var match = Regex.Match(link, @"M(\d+)A");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing link: {ex.Message}");
            return null;
        }
    }


    private static async Task<ItemDetails?> FetchItemDetails(string instanceId)
    {
        var url = $"https://api.steampowered.com/ISteamEconomy/GetAssetClassInfo/v1/?key={steamApiKey}&appid=730&class_count=1&classid0={instanceId}";

        try
        {
            Console.WriteLine($"Fetching item details from URL: {url}");
            var response = await httpClient.GetStringAsync(url);
            Console.WriteLine($"Response from Steam API: {response}");
            var json = JObject.Parse(response);
            var item = json["result"]?[instanceId];

            if (item == null)
            {
                Console.WriteLine("Item not found in response.");
                return null;
            }

            var itemDetails = new ItemDetails
            {
                FloatValue = item["floatvalue"]?.Value<float>() ?? 0,
                PaintSeed = item["paintseed"]?.Value<int>() ?? 0,
                PaintIndex = item["paintindex"]?.Value<int>() ?? 0,
                Stickers = item["stickers"]?.Select(s => new Sticker
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
