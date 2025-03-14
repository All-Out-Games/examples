using AO;
using System.Linq;

struct ItemBackup
{
    public Item_Definition itemDef;
    public string type;
    public string level;
    public string exp;

    public static ItemBackup? FromItem(Item_Instance item)
    {
        if (item == null) return null;

        return new ItemBackup
        {
            itemDef = item.Definition,
            type = item.GetMetadata("Type"),
            level = item.GetMetadata("Level"),
            exp = item.GetMetadata("Exp")
        };
    }

    public void ToItem(Inventory inventory)
    {
        Item_Instance item = Inventory.CreateItem(itemDef);
        if (Inventory.CanMoveItemToInventory(item, inventory))
        {
            Inventory.MoveItemToInventory(item, inventory);
            item.SetMetadata("Type", type);
            item.SetMetadata("Level", level);
            item.SetMetadata("Exp", exp);
        }
    }
}

struct PlayerBackup
{
    public int level;
    public int experience;
    public string currentRodID;
    public string lastRodID;
    public string currentBoatID;
    public int fishCoinBalance;
    public ItemBackup?[] fishTeam;
    public ItemBackup?[] inventory;

    public static PlayerBackup FromPlayer(MyPlayer player)
    {
        return new PlayerBackup
        {
            level = player.Level,
            experience = player.experience,
            currentRodID = player.currentRodID,
            lastRodID = player.lastRodID,
            currentBoatID = player.currentBoatID,
            fishCoinBalance = (int)Economy.GetBalance(player, "FishCoin"),
            fishTeam = player.FishTeam.Items.Select(ItemBackup.FromItem).ToArray(),
            inventory = player.DefaultInventory.Items.Select(ItemBackup.FromItem).ToArray()
        };
    }

    public readonly void LoadIntoPlayer(MyPlayer player)
    {
        player.Level = level;
        player.experience.Set(experience);
        player.currentRodID.Set(currentRodID);
        player.lastRodID.Set(lastRodID);
        player.currentBoatID.Set(currentBoatID);

        // Reset currencies to 0 then set to backup values
        Economy.WithdrawCurrency(player, Store.MoneyCurrency, Economy.GetBalance(player, Store.MoneyCurrency));
        Economy.DepositCurrency(player, Store.MoneyCurrency, fishCoinBalance);

        // Clear and restore fish team
        for (int i = 0; i < player.FishTeam.Items.Length; i++)
        {
            if (player.FishTeam.Items[i] != null)
                Inventory.DestroyItem(player.FishTeam.Items[i]);
        }

        for (int i = 0; i < player.FishTeam.Items.Length; i++)
        {
            if (fishTeam[i].HasValue)
            {
                fishTeam[i].Value.ToItem(player.FishTeam);
            }
        }

        // Clear and restore inventory
        for (int i = 0; i < player.DefaultInventory.Items.Length; i++)
        {
            if (player.DefaultInventory.Items[i] != null)
                Inventory.DestroyItem(player.DefaultInventory.Items[i]);
        }

        for (int i = 0; i < player.DefaultInventory.Items.Length; i++)
        {
            if (inventory[i].HasValue)
            {
                inventory[i].Value.ToItem(player.DefaultInventory);
            }
        }

        // Force sync inventories
        player.SaveFishTeam();
        Inventory.ServerForceSyncInventory(player.FishTeam);
        Inventory.ServerForceSyncInventory(player.DefaultInventory);

        //Force save
        AO.Save.SetInt(player, "exp", player.experience);
        AO.Save.SetInt(player, "level", player.Level);
    }
}

public class GameManager : System<GameManager>
{
    private Dictionary<string, PlayerBackup> playerBackups = new Dictionary<string, PlayerBackup>();
    private static Dictionary<string, ItemRarity> forcedRarity = new Dictionary<string, ItemRarity>();
    private static Dictionary<string, string> forcedFish = new Dictionary<string, string>();

    public static Action<Player, double> OnIncrementScore;
    public static Action<Player> OnRequestLeaderboard;

    public static bool allowMusicPlay = true;

    public override void Awake()
    {
        Chat.RegisterChatCommandHandler(RunChatCommand);
        PlayerList.RegisterSortCallback((Player[] players) =>
        {
            Array.Sort(players, (a, b) =>
            {
                return ((MyPlayer)b).Level.CompareTo(((MyPlayer)a).Level);
            });
        });

        PlayerList.Register("Level", (Player[] players, string[] scores) =>
        {
            for (int i = 0; i < players.Length; i++)
            {
                var player = (MyPlayer)players[i];
                scores[i] = $"{player.Level + 1}";
            }
        });

        if (Network.IsClient)
        {
            Game.SetVoiceEnabled(true);
            Analytics.EnableAutomaticAnalytics("<REDACTED>", "<REDACTED>");
            PlayMusic("default");
        }
    }

    public static ulong musicId;

    public static ItemRarity? GetRarityFor(string playerName)
    {
        if (forcedRarity.TryGetValue(playerName, out var rarity))
        {
            forcedRarity.Remove(playerName);
            return rarity;
        }
        return null;
    }

    public static string GetFishFor(string playerName)
    {
        if (forcedFish.TryGetValue(playerName, out var id))
        {
            forcedFish.Remove(playerName);
            return id;
        }
        return "";
    }

    private static Dictionary<string, AudioAsset> MusicAssets = new Dictionary<string, AudioAsset>()
    {
        { "default", Assets.GetAsset<AudioAsset>("audio/fishermon_soundtrack_loop.wav") },
        //{ "battle", Assets.GetAsset<AudioAsset>("audio/fishermon_soundtrack_loop.wav") },
        { "battle", Assets.GetAsset<AudioAsset>("audio/fish_battle.wav") },
    };

    private static Dictionary<string, SFX.PlaySoundDesc> MusicDescs = new Dictionary<string, SFX.PlaySoundDesc>()
    {
        { "default", new SFX.PlaySoundDesc() { Positional = false, Volume = 0.5f, Loop = true } },
        //{ "battle", new SFX.PlaySoundDesc() { Positional = false, Volume = 0.5f, Loop = true } },
        { "battle", new SFX.PlaySoundDesc() { Positional = false, Volume = 0.25f, Loop = true } },
    };

    static string currentMusic = "";

    public static void PlayMusic(string id)
    {
        if (!allowMusicPlay) return;
        if (currentMusic == id) return;
        if (musicId != 0)
        {
            SFX.Stop(musicId);
        }
        if (MusicAssets.TryGetValue(id, out var asset))
        {
            musicId = SFX.Play(asset, MusicDescs[id]);
        }
    }

    public static void StopMusic()
    {
        SFX.FadeOutAndStop(musicId, 1.0f);
    }

    public bool CheckAdmin(Player player)
    {
        if (!player.IsAdmin)
        {
            Chat.SendMessage(player, "You must be an admin to use this command.");
            return false;
        }
        return true;
    }

    public static bool TryGiveCandy(MyPlayer player, int amount)
    {
        if (amount > 999)
        {
            Chat.SendMessage(player, "You cannot receive more than 999 candy.");
            return false;
        }
        var candyDef = FishItemManager.Instance.unusedItems["Fish_Candy"];
        var item = Inventory.CreateItem(candyDef, amount);
        if (Inventory.CanMoveItemToInventory(item, player.DefaultInventory))
        {
            Inventory.MoveItemToInventory(item, player.DefaultInventory);
            return true;
        }
        return false;
    }

    public static void AddFish(MyPlayer player, string id)
    {
        var fish = FishItemManager.GetFish(id);
        if (fish == null) return;
        player.currentProcessedFish = fish;
        player.currentProcessedFishLevel = player.Level.ToString();
        player.ProcessNewFish(fish, player.Level);
    }

    public static bool ApplyBuffToPlayer(MyPlayer player, string id, float currentValue = -1)
    {
        float newVal = currentValue == -1 ? 20 * 60 : currentValue;
        switch (id)
        {
            case "Buff_Coin_Booster":
                player.AddToBuff(newVal, 0, 0);
                break;
            case "Buff_XP_Booster":
                player.AddToBuff(0, newVal, 0);
                break;
            case "Buff_Luck_Booster":
                player.AddToBuff(0, 0, newVal);
                break;
            case "Buff_Master_Combo":
                player.AddToBuff(newVal, newVal, newVal);
                break;
        }
        return true;
    }


    public void RunChatCommand(Player p, string command)
    {
        if (!CheckAdmin(p)) return;
        var parts = command.Split(' ');
        var cmd = parts[0].ToLowerInvariant();
        MyPlayer player = (MyPlayer)p;
        switch (cmd)
        {
            case "music_off":
                player.CallClient_CutMusic();
                Chat.SendMessage(p, $"Music is now disabled");
                break;
            case "reset":
                {
                    MyPlayer target = parts.Length > 1 && parts[1] != "self" ? MyPlayer.players.Find(p => p.Name == parts[1]) : player;
                    if (target == null)
                    {
                        Chat.SendMessage(p, "Invalid player name");
                        break;
                    }
                    target.ResetPlayerData();
                    Chat.SendMessage(p, $"Player data reset for {target.Name}!");
                    break;
                }
            case "backup":
                if (parts.Length < 2)
                {
                    Chat.SendMessage(p, "Usage: backup save|load");
                    break;
                }
                switch (parts[1].ToLowerInvariant())
                {
                    case "save":
                        playerBackups[player.Name] = PlayerBackup.FromPlayer(player);
                        Chat.SendMessage(p, "Backup saved!");
                        break;
                    case "load":
                        if (playerBackups.TryGetValue(player.Name, out var backup))
                            backup.LoadIntoPlayer(player);
                        else
                            Chat.SendMessage(p, "No backup found for player");
                        break;
                    default:
                        Chat.SendMessage(p, "Usage: backup save|load");
                        break;
                }
                break;
            case "set":
                if (parts.Length < 3)
                {
                    Chat.SendMessage(p, "Usage: set lvl|exp <amount>");
                    break;
                }
                switch (parts[1].ToLowerInvariant())
                {
                    case "lvl":
                        if (!int.TryParse(parts[2], out int level))
                        {
                            Chat.SendMessage(p, "Invalid level number");
                            break;
                        }
                        player.Level = level - 1;
                        Chat.SendMessage(p, $"Set {player.Name} to lvl {parts[2]}");
                        break;
                    case "exp":
                        if (!int.TryParse(parts[2], out int expAmount))
                        {
                            Chat.SendMessage(p, "Invalid experience amount");
                            break;
                        }
                        player.experience.Set(expAmount);
                        Chat.SendMessage(p, $"Set {player.Name} to {parts[2]} exp");
                        break;
                    default:
                        Chat.SendMessage(p, "Usage: set lvl|exp <amount>");
                        break;
                }
                break;
            case "give":
                if (parts.Length < 3)
                {
                    Chat.SendMessage(p, "Usage: give gold|exp|candy <amount>");
                    break;
                }
                switch (parts[1].ToLowerInvariant())
                {
                    case "gold":
                        if (!int.TryParse(parts[2], out int goldAmount))
                        {
                            Chat.SendMessage(p, "Invalid gold amount");
                            break;
                        }
                        Economy.DepositCurrency(player, Store.MoneyCurrency, goldAmount);
                        Chat.SendMessage(p, $"Gave {goldAmount} gold to {player.Name}");
                        break;
                    case "exp":
                        if (!int.TryParse(parts[2], out int expGive))
                        {
                            Chat.SendMessage(p, "Invalid experience amount");
                            break;
                        }
                        player.experience.Set(player.experience + expGive);
                        Chat.SendMessage(p, $"Gave {expGive} exp to {player.Name}");
                        break;
                    case "candy":
                        if (!int.TryParse(parts[2], out int candyAmount))
                        {
                            Chat.SendMessage(p, "Invalid candy amount");
                            break;
                        }
                        TryGiveCandy(player, candyAmount);
                        Chat.SendMessage(p, $"Gave {candyAmount} candy to {player.Name}");
                        break;
                    default:
                        Chat.SendMessage(p, "Usage: give gold|exp|candy <amount>");
                        break;
                }
                break;
            case "buff":
                if (parts.Length < 2)
                {
                    Chat.SendMessage(p, "Usage: buff <id>");
                    break;
                }
                if (parts[1] == "all")
                {
                    ApplyBuffToPlayer(player, "Buff_Master_Combo");
                }
                else if (parts[1] == "clear")
                {
                    player.playerBuffs.Set(new Vector3(0, 0, 0));
                }
                else
                {
                    ApplyBuffToPlayer(player, parts[1]);
                }
                break;
            case "leaderboard":
                OnRequestLeaderboard?.Invoke(p);
                break;
            case "log_team":
                if (parts.Length != 2)
                {
                    Chat.SendMessage(p, "Usage: log_team <player_name>|self");
                    break;
                }
                {
                    MyPlayer target = parts[1] != "self" ? MyPlayer.players.Find(p => p.Name == parts[1]) : player;
                    if (target == null)
                    {
                        Chat.SendMessage(p, "Invalid player");
                        break;
                    }
                    Chat.SendMessage(p, $"Team for Player {target.Name}:");
                    for (int i = 0; i < target.FishTeam.Items.Length; i++)
                    {
                        var item = target.FishTeam.Items[i];
                        if (item != null)
                        {
                            Chat.SendMessage(p, $"Slot {i}: {item.Definition.Name} (Lvl {item.GetMetadata("Level")}) (Exp {item.GetMetadata("Exp")}) (Type {item.GetMetadata("Type")})");
                        }
                        else
                        {
                            Chat.SendMessage(p, $"Slot {i}: Empty");
                        }
                    }
                    break;
                }
            case "fish_set":
                if (parts.Length < 4)
                {
                    Chat.SendMessage(p, "Usage: fish_set (lvl|exp|type) <slot> <amount|type> <player_name|self>");
                    break;
                }
                {
                    switch (parts[1])
                    {
                        case "lvl":
                            {
                                MyPlayer target = parts.Length > 4 && parts[4] != "self" ? MyPlayer.players.Find(p => p.Name == parts[4]) : player;
                                if (target == null)
                                {
                                    Chat.SendMessage(p, "Invalid player name");
                                    break;
                                }
                                if (!int.TryParse(parts[2], out int fishSlot))
                                {
                                    Chat.SendMessage(p, "Invalid slot number");
                                    break;
                                }
                                if (fishSlot < 0 || fishSlot >= target.FishTeam.Items.Length)
                                {
                                    Chat.SendMessage(p, "Invalid slot number");
                                    break;
                                }
                                var fishItem = target.FishTeam.Items[fishSlot];
                                if (fishItem == null)
                                {
                                    Chat.SendMessage(p, "No fish in that slot");
                                    break;
                                }
                                fishItem.SetMetadata("Level", parts[3]);
                                Chat.SendMessage(p, $"Set fish in slot {fishSlot} to level {parts[3]} for Player {target.Name}");
                                break;
                            }
                        case "exp":
                            {
                                MyPlayer target = parts.Length > 4 && parts[4] != "self" ? MyPlayer.players.Find(p => p.Name == parts[4]) : player;
                                if (target == null)
                                {
                                    Chat.SendMessage(p, "Invalid player name");
                                    break;
                                }
                                if (!int.TryParse(parts[2], out int fishSlot))
                                {
                                    Chat.SendMessage(p, "Invalid slot number");
                                    break;
                                }
                                if (fishSlot < 0 || fishSlot >= target.FishTeam.Items.Length)
                                {
                                    Chat.SendMessage(p, "Invalid slot number");
                                    break;
                                }
                                var fishItem = target.FishTeam.Items[fishSlot];
                                if (fishItem == null)
                                {
                                    Chat.SendMessage(p, "No fish in that slot");
                                    break;
                                }
                                fishItem.SetMetadata("Exp", parts[3]);
                                Chat.SendMessage(p, $"Set fish in slot {fishSlot} to {parts[3]} exp for Player {target.Name}");
                                break;
                            }
                        case "type":
                            {
                                MyPlayer target = parts.Length > 4 && parts[4] != "self" ? MyPlayer.players.Find(p => p.Name == parts[4]) : player;
                                if (target == null)
                                {
                                    Chat.SendMessage(p, "Invalid player name");
                                    break;
                                }
                                if (!int.TryParse(parts[2], out int fishSlot))
                                {
                                    Chat.SendMessage(p, "Invalid slot number");
                                    break;
                                }
                                if (fishSlot < 0 || fishSlot >= target.FishTeam.Items.Length)
                                {
                                    Chat.SendMessage(p, "Invalid slot number");
                                    break;
                                }
                                var fishItem = target.FishTeam.Items[fishSlot];
                                if (fishItem == null)
                                {
                                    Chat.SendMessage(p, "No fish in that slot");
                                    break;
                                }
                                string type = parts[3].ToLower();
                                if (type != "water" && type != "fire" && type != "grass")
                                {
                                    Chat.SendMessage(p, "Invalid type. Must be water, fire, or grass");
                                    break;
                                }
                                fishItem.SetMetadata("Type", type);
                                Chat.SendMessage(p, $"Set fish in slot {fishSlot} to type {type} for Player {target.Name}");
                                break;
                            }
                    }
                    break;
                }
            case "fish_add":
                if (parts.Length < 2)
                {
                    Chat.SendMessage(p, "Usage: fish_add <id> <player_name>|self");
                    break;
                }
                {
                    MyPlayer target = parts.Length > 2 && parts[2] != "self" ? MyPlayer.players.Find(p => p.Name == parts[2]) : player;
                    if (target == null)
                    {
                        Chat.SendMessage(p, "Invalid player name");
                        break;
                    }
                    if (target.IsTeamFull())
                    {
                        Chat.SendMessage(p, "Team is full");
                        break;
                    }
                    var fish = FishItemManager.GetFish(parts[1]);
                    if (fish == null)
                    {
                        Chat.SendMessage(p, "Invalid fish ID");
                        break;
                    }
                    target.currentProcessedFish = fish;
                    target.currentProcessedFishLevel = "1";
                    target.currentProcessedFishType = new string[] { "grass", "fire", "water" }.GetRandom();
                    target.AddNewFish();
                    Chat.SendMessage(p, $"Added {fish.Name} to team for Player {target.Name}");
                    break;
                }
            case "force_luck":
                if (parts.Length < 2)
                {
                    Chat.SendMessage(p, "Usage: force_luck <rarity>");
                    break;
                }
                string rarity = parts[1];
                if (Enum.TryParse(rarity, out ItemRarity itemRarity))
                {
                    forcedRarity[player.Name] = itemRarity;
                    Chat.SendMessage(p, $"Forced {Enum.GetName(typeof(ItemRarity), itemRarity)} luck on next encounter!");
                }
                else
                {
                    Chat.SendMessage(p, "Invalid rarity");
                }
                break;
            case "force_fish":
                if (parts.Length < 2)
                {
                    Chat.SendMessage(p, "Usage: force_fish <id>");
                    break;
                }
                else
                {
                    string id = parts[1];
                    var fish = FishItemManager.GetFish(id);
                    if (fish != null)
                    {
                        forcedFish[player.Name] = id;
                        Chat.SendMessage(p, $"Forced {fish.Name} on next encounter!");
                    }
                    else
                    {
                        Chat.SendMessage(p, "Invalid fish ID");
                    }
                }
                break;
            default:
                Chat.SendMessage(p, "Invalid command");
                break;

        }
    }
}
