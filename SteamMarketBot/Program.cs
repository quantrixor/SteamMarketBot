using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SteamKit2;
using SteamKit2.GC;
using SteamKit2.GC.CSGO.Internal;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public class Program
{
    static SteamClient steamClient;
    static CallbackManager manager;
    static SteamUser steamUser;
    static SteamGameCoordinator gameCoordinator;
    static bool isRunning = false;
    static uint APPID = 730; // CS:GO App ID

    static ConcurrentDictionary<string, TaskCompletionSource<ItemDetails>> pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<ItemDetails>>();

    public static void Main(string[] args)
    {
        steamClient = new SteamClient();
        manager = new CallbackManager(steamClient);

        steamUser = steamClient.GetHandler<SteamUser>();
        gameCoordinator = steamClient.GetHandler<SteamGameCoordinator>();

        manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
        manager.Subscribe<SteamGameCoordinator.MessageCallback>(OnGCMessage);

        isRunning = true;

        Console.WriteLine("Connecting to Steam...");
        steamClient.Connect();

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

                                      var instanceId = ParseInstanceIdFromLink(link);
                                      if (string.IsNullOrEmpty(instanceId))
                                      {
                                          context.Response.StatusCode = 400;
                                          await context.Response.WriteAsync("Invalid link format.");
                                          return;
                                      }

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

        var steamThread = new Thread(() =>
        {
            while (isRunning)
            {
                manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
            }
        })
        {
            IsBackground = true
        };
        steamThread.Start();

        host.Run();
    }

    static void OnConnected(SteamClient.ConnectedCallback callback)
    {
        Console.WriteLine("Connected to Steam! Logging in...");

        steamUser.LogOn(new SteamUser.LogOnDetails
        {
            Username = "Cofdu2336",
            Password = "RqKRmY0m2b366l",
        });
    }

    static void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        Console.WriteLine("Disconnected from Steam");
        isRunning = false;
    }

    static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            Console.WriteLine($"Unable to log in to Steam: {callback.Result} / {callback.ExtendedResult}");
            isRunning = false;
            return;
        }

        Console.WriteLine("Successfully logged on!");
    }

    static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
    {
        Console.WriteLine($"Logged off of Steam: {callback.Result}");
    }

    static void OnGCMessage(SteamGameCoordinator.MessageCallback callback)
    {
        var msg = callback.Message;
        Console.WriteLine($"Received GC message: {msg.MsgType}");

        if (msg.MsgType == (uint)ECsgoGCMsg.k_EMsgGCCStrike15_v2_Client2GCEconPreviewDataBlockResponse)
        {
            Console.WriteLine("Received Client2GCEconPreviewDataBlockResponse message.");

            var response = new ClientGCMsgProtobuf<CMsgGCCStrike15_v2_Client2GCEconPreviewDataBlockResponse>(msg);
            var instanceId = response.Body.iteminfo.itemid.ToString(); // используем itemid как идентификатор экземпляра

            Console.WriteLine($"InstanceId: {instanceId}");

            if (pendingRequests.TryRemove(instanceId, out var tcs))
            {
                var itemDetails = new ItemDetails
                {
                    FloatValue = response.Body.iteminfo.paintwear,
                    PaintSeed = (int)response.Body.iteminfo.paintseed,
                    PaintIndex = (int)response.Body.iteminfo.paintindex,
                    Stickers = response.Body.iteminfo.stickers.Select(s => new Sticker
                    {
                        Name = s.sticker_id.ToString(), // Используем sticker_id, так как поле name отсутствует
                        Wear = s.wear
                    }).ToArray()
                };

                Console.WriteLine($"FloatValue: {itemDetails.FloatValue}, PaintSeed: {itemDetails.PaintSeed}, PaintIndex: {itemDetails.PaintIndex}");
                foreach (var sticker in itemDetails.Stickers)
                {
                    Console.WriteLine($"Sticker Name: {sticker.Name}, Wear: {sticker.Wear}");
                }

                tcs.SetResult(itemDetails);
            }
            else
            {
                Console.WriteLine("Failed to find pending request for instanceId.");
            }
        }
        else
        {
            Console.WriteLine($"Received unhandled GC message: {msg.MsgType}");
        }
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
        catch
        {
            return null;
        }
    }

    private static async Task<ItemDetails?> FetchItemDetails(string instanceId)
    {
        var tcs = new TaskCompletionSource<ItemDetails>();
        pendingRequests[instanceId] = tcs;

        // Отправка запроса к Steam Game Coordinator
        var msg = new ClientGCMsgProtobuf<CMsgGCCStrike15_v2_Client2GCEconPreviewDataBlockRequest>((uint)ECsgoGCMsg.k_EMsgGCCStrike15_v2_Client2GCEconPreviewDataBlockRequest);
        msg.Body.param_s = ulong.Parse(instanceId);

        Console.WriteLine($"Sending request to GC for instanceId: {instanceId}");
        gameCoordinator.Send(msg, APPID);

        var timeoutTask = Task.Delay(10000); // 10 секунд таймаут
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            Console.WriteLine("Request timed out.");
            pendingRequests.TryRemove(instanceId, out _);
            return null;
        }

        return await tcs.Task;
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
