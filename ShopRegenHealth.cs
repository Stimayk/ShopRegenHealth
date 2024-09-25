using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using ShopAPI;

namespace ShopRegenHealth
{
    public class ShopRegenHealth : BasePlugin
    {
        public override string ModuleName => "[SHOP] Health Regeneration";
        public override string ModuleDescription => "";
        public override string ModuleAuthor => "E!N";
        public override string ModuleVersion => "v1.0.1";

        private IShopApi? SHOP_API;
        private const string CategoryName = "HealthRegen";
        public static JObject? JsonHealthRegen { get; private set; }
        private readonly PlayerRegenHealth[] playerRegenHealths = new PlayerRegenHealth[65];

        public override void OnAllPluginsLoaded(bool hotReload)
        {
            SHOP_API = IShopApi.Capability.Get();
            if (SHOP_API == null) return;

            LoadConfig();
            InitializeShopItems();
            SetupTimersAndListeners();
        }

        private void LoadConfig()
        {
            string configPath = Path.Combine(ModuleDirectory, "../../configs/plugins/Shop/HealthRegen.json");
            if (File.Exists(configPath))
            {
                JsonHealthRegen = JObject.Parse(File.ReadAllText(configPath));
            }
        }

        private void InitializeShopItems()
        {
            if (JsonHealthRegen == null || SHOP_API == null) return;

            SHOP_API.CreateCategory(CategoryName, "Регенерация здоровья");

            foreach (var item in JsonHealthRegen.Properties().Where(p => p.Value is JObject))
            {
                Task.Run(async () =>
                {
                    int itemId = await SHOP_API.AddItem(
                        item.Name,
                        (string)item.Value["name"]!,
                        CategoryName,
                        (int)item.Value["price"]!,
                        (int)item.Value["sellprice"]!,
                        (int)item.Value["duration"]!
                    );
                    SHOP_API.SetItemCallbacks(itemId, OnClientBuyItem, OnClientSellItem, OnClientToggleItem);
                }).Wait();
            }
        }

        private void SetupTimersAndListeners()
        {
            RegisterListener<Listeners.OnClientDisconnect>(playerSlot =>
            {
                playerRegenHealths[playerSlot] = null!;
            });

            AddTimer(1.0f, Timer_HealthRegen, TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);

            RegisterEventHandler<EventPlayerHurt>((@event, info) =>
            {
                var player = @event.Userid;

                if (player != null && playerRegenHealths[player.Slot] != null && @event.DmgHealth > 0)
                {
                    playerRegenHealths[player.Slot].IsRegenActive = true;
                }

                return HookResult.Continue;
            });
        }

        public HookResult OnClientBuyItem(CCSPlayerController player, int itemId, string categoryName, string uniqueName,
            int buyPrice, int sellPrice, int duration, int count)
        {
            if (TryGetRegenSettings(uniqueName, out var regenSettings))
            {
                playerRegenHealths[player.Slot] = new PlayerRegenHealth(regenSettings, itemId);
            }
            else
            {
                Logger.LogError($"{uniqueName} has invalid or missing settings in config!");
            }
            return HookResult.Continue;
        }

        public HookResult OnClientToggleItem(CCSPlayerController player, int itemId, string uniqueName, int state)
        {
            if (state == 1 && TryGetRegenSettings(uniqueName, out var regenSettings))
            {
                playerRegenHealths[player.Slot] = new PlayerRegenHealth(regenSettings, itemId);
            }
            else if (state == 0)
            {
                OnClientSellItem(player, itemId, uniqueName, 0);
            }
            return HookResult.Continue;
        }

        public HookResult OnClientSellItem(CCSPlayerController player, int itemId, string uniqueName, int sellPrice)
        {
            playerRegenHealths[player.Slot] = null!;
            return HookResult.Continue;
        }

        private void Timer_HealthRegen()
        {
            foreach (var player in Utilities.GetPlayers()
                         .Where(u => u.PlayerPawn.Value != null && u.PlayerPawn.Value.IsValid && u.PawnIsAlive))
            {
                if (playerRegenHealths[player.Slot] != null && playerRegenHealths[player.Slot] is var playerRegen && playerRegen.IsRegenActive)
                {
                    if (playerRegen.RegenSettings.Delay > 0)
                    {
                        playerRegen.RegenSettings.Delay--;
                        continue;
                    }

                    if (playerRegen.RegenInterval > 0)
                    {
                        playerRegen.RegenInterval--;
                        continue;
                    }

                    if (HealthRegen(player, playerRegen.RegenSettings))
                    {
                        playerRegen.RegenInterval = playerRegen.RegenSettings.Interval;
                    }
                }
            }
        }

        private bool HealthRegen(CCSPlayerController player, Regen regen)
        {
            var playerPawn = player.PlayerPawn.Value;
            if (playerPawn == null) return true;

            if (playerPawn.Health < playerPawn.MaxHealth)
            {
                playerPawn.Health += regen.Health;
                Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");
                if (playerPawn.Health < playerPawn.MaxHealth)
                    return true;

                playerPawn.Health = playerPawn.MaxHealth;
                Utilities.SetStateChanged(playerPawn, "CBaseEntity", "m_iHealth");
            }

            playerRegenHealths[player.Slot].IsRegenActive = false;
            return false;
        }

        private static bool TryGetRegenSettings(string uniqueName, out Regen regenSettings)
        {
            regenSettings = new Regen();
            if (JsonHealthRegen != null && JsonHealthRegen.TryGetValue(uniqueName, out var obj) &&
                obj is JObject jsonItem && jsonItem["health"] != null && jsonItem["health"]!.Type != JTokenType.Null &&
                jsonItem["delay"] != null && jsonItem["delay"]!.Type != JTokenType.Null &&
                jsonItem["interval"] != null && jsonItem["interval"]!.Type != JTokenType.Null)
            {
                regenSettings.Health = (int)jsonItem["health"]!;
                regenSettings.Delay = (int)jsonItem["delay"]!;
                regenSettings.Interval = (int)jsonItem["interval"]!;
                return true;
            }

            return false;
        }

        public record class PlayerRegenHealth(Regen RegenSettings, int ItemID)
        {
            public bool IsRegenActive { get; set; } = false;
            public int RegenInterval { get; set; }
        }
    }

    public class Regen
    {
        public int Health { get; set; } = 0;
        public int Delay { get; set; } = 0;
        public int Interval { get; set; } = 0;
    }
}