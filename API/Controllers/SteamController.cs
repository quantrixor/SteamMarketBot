using Microsoft.AspNetCore.Mvc;
using SteamKit2;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SteamController : ControllerBase
    {
        private static readonly SteamClient steamClient = new SteamClient();
        private static readonly CallbackManager manager = new CallbackManager(steamClient);
        private static readonly SteamUser steamUser = steamClient.GetHandler<SteamUser>();
        private static readonly SteamFriends steamFriends = steamClient.GetHandler<SteamFriends>();
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<ItemDetails>> pendingRequests = new();

        static SteamController()
        {
            manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
        }

        [HttpGet("item")]
        public async Task<IActionResult> GetItemDetails([FromQuery] string link)
        {
            var instanceId = ParseInstanceIdFromLink(link);
            if (string.IsNullOrEmpty(instanceId))
            {
                return BadRequest("Invalid link format.");
            }

            var itemDetails = await FetchItemDetails(instanceId);
            return Ok(itemDetails);
        }

        private string? ParseInstanceIdFromLink(string link)
        {
            try
            {
                // Находим часть ссылки, которая содержит идентификатор предмета
                var uri = new Uri(link);
                var segments = uri.AbsolutePath.Split('/');
                var instanceIdSegment = segments.FirstOrDefault(segment => segment.StartsWith("+csgo_econ_action_preview"));
                if (instanceIdSegment != null)
                {
                    var instanceId = instanceIdSegment.Split('M')[1];
                    return instanceId;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<ItemDetails> FetchItemDetails(string instanceId)
        {
            var tcs = new TaskCompletionSource<ItemDetails>();
            pendingRequests[instanceId] = tcs;

            // Подключаемся к Steam
            if (!steamClient.IsConnected)
            {
                steamClient.Connect();
            }

            // Ожидаем результат
            return await tcs.Task;
        }

        private static void OnConnected(SteamClient.ConnectedCallback callback)
        {
            //if (callback.Result != EResult.OK)
            //{
            //    // Обрабатываем ошибку
            //    Console.WriteLine($"Unable to connect to Steam: {callback.Result}");
            //    return;
            //}

            Console.WriteLine("Connected to Steam! Logging in...");

            steamUser.LogOn(new SteamUser.LogOnDetails
            {
                Username = "Cofdu2336",
                Password = "RqKRmY0m2b366l",
            });
        }

        private static void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                // Обрабатываем ошибку
                Console.WriteLine($"Unable to log in to Steam: {callback.Result} / {callback.ExtendedResult}");
                return;
            }

            Console.WriteLine("Successfully logged on!");

            // Здесь можно добавить логику для получения данных о предмете
        }

        private static void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            Console.WriteLine($"Logged off of Steam: {callback.Result}");
        }

        private static void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            Console.WriteLine("Disconnected from Steam");
        }
    }

    public class ItemDetails
    {
        public float FloatValue { get; set; }
        public int PaintSeed { get; set; }
        public int PaintIndex { get; set; }
        public Sticker[] Stickers { get; set; } = Array.Empty<Sticker>(); // Инициализируем пустым массивом
    }

    public class Sticker
    {
        public string Name { get; set; } = string.Empty; // Инициализируем пустой строкой
        public float Wear { get; set; }
    }
}
