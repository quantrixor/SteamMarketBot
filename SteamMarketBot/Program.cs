using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

public class Program
{
    static HttpClient httpClient = new HttpClient();
    static ConcurrentDictionary<string, (ItemDetails details, DateTime expiration)> cache = new ConcurrentDictionary<string, (ItemDetails, DateTime)>();
    static TimeSpan cacheDuration = TimeSpan.FromMinutes(10);

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
                                      var link = context.Request.Query["link"].ToString();
                                      if (string.IsNullOrEmpty(link))
                                      {
                                          context.Response.StatusCode = 400;
                                          await context.Response.WriteAsync("Invalid link format.");
                                          return;
                                      }

                                      Console.WriteLine($"Received link: {link}");

                                      if (cache.TryGetValue(link, out var cacheEntry) && cacheEntry.expiration > DateTime.Now)
                                      {
                                          await context.Response.WriteAsJsonAsync(cacheEntry.details);
                                          return;
                                      }

                                      var itemDetails = await FetchItemDetails(link);
                                      if (itemDetails != null)
                                      {
                                          cache[link] = (itemDetails, DateTime.Now.Add(cacheDuration));
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

    private static async Task<ItemDetails?> FetchItemDetails(string link)
    {
        // Extract the M value from the steam:// URL
        var match = Regex.Match(link, @"M(\d+)");
        if (!match.Success)
        {
            Console.WriteLine("Invalid link format.");
            return null;
        }

        var mValue = match.Groups[1].Value;
        var url = $"https://api.csgofloat.com/?url={Uri.EscapeDataString($"https://steamcommunity.com/profiles/76561198034390551/inventory/#730_2_{mValue}")}";

        try
        {
            Console.WriteLine($"Fetching item details from URL: {url}");
            var response = await SendRequestWithRetries(url);
            if (response == null)
            {
                return null;
            }

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

    private static async Task<string?> SendRequestWithRetries(string url, int maxRetries = 3)
    {
        int retryCount = 0;
        int delay = 10000; // Start with a 10 second delay

        while (retryCount < maxRetries)
        {
            try
            {
                var response = await httpClient.GetAsync(url);
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMilliseconds(delay);
                    Console.WriteLine($"Rate limited. Retrying after {retryAfter.TotalSeconds} seconds...");
                    await Task.Delay(retryAfter);
                    retryCount++;
                    delay *= 2; // Exponential backoff
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (HttpRequestException httpEx)
            {
                Console.WriteLine($"HTTP request error: {httpEx.Message}");
                if (retryCount >= maxRetries - 1)
                {
                    throw;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(delay));
                retryCount++;
                delay *= 2;
            }
        }
        return null;
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
