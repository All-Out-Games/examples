using AO;

public struct FishItem
{
    public Item_Definition Item;
    public ItemRarity Rarity;
    public bool teamable;
    public bool catchable;
    public IslandType islandType;
}

public class FishItemManager : Component
{
    [AOIgnore] public static FishItemManager Instance;

    public Dictionary<string, Item_Definition> unusedItems = new Dictionary<string, Item_Definition>();
    public Dictionary<string, FishItem> allItems = new Dictionary<string, FishItem>();
    private Dictionary<string, string> fishDescriptions = new Dictionary<string, string>();

    private void CreateUnused(string ID, string Name, string Icon, int stackSize = 1)
    {
        var item = Item_Definition.Create(new ItemDescription
        {
            Id = ID,

            Name = Name,
            Icon = Icon,
            StackSize = stackSize
        });
        unusedItems.Add(ID, item);
    }

    private void AddFish(string ID, string Name, string Icon, ItemRarity rarity, bool teamable, IslandType islandType = IslandType.MainIsland)
    {
        var item = Item_Definition.Create(new ItemDescription
        {
            Id = ID,

            Name = Name,
            Icon = Icon
        });
        allItems.Add(ID, new FishItem() { Item = item, Rarity = rarity, teamable = teamable, islandType = islandType, catchable = true });
        Assets.KeepLoaded<Texture>(Icon, false);
    }

    private void AddBossFish(string ID, string Name, string Icon, ItemRarity rarity)
    {
        var item = Item_Definition.Create(new ItemDescription
        {
            Id = ID,

            Name = Name,
            Icon = Icon
        });
        allItems.Add(ID, new FishItem() { Item = item, Rarity = rarity, teamable = true, islandType = IslandType.MainIsland, catchable = false });
        Assets.KeepLoaded<Texture>(Icon, false);
    }

    bool ValidCatchableRarity(ItemRarity rarity)
    {
        return rarity == ItemRarity.Common || rarity == ItemRarity.Rare || rarity == ItemRarity.Epic || rarity == ItemRarity.Legendary || rarity == ItemRarity.Mythic;
    }

    private ItemRarity RandomRarity(MyPlayer requestingPlayer, Rod rod, int boosterAmount)
    {
        ItemRarity? forcedRarity = GameManager.GetRarityFor(requestingPlayer.Name);
        if (forcedRarity.HasValue && ValidCatchableRarity(forcedRarity.Value))
        {
            return forcedRarity.Value;
        }
        float val = (float)Random.Shared.NextDouble(0.01, 100.0);
        val -= rod.RarityToFloat(ItemRarity.Common, boosterAmount);
        if (val <= 0)
        {
            return ItemRarity.Common;
        }
        val -= rod.RarityToFloat(ItemRarity.Rare, boosterAmount);
        if (val <= 0)
        {
            return ItemRarity.Rare;
        }
        val -= rod.RarityToFloat(ItemRarity.Epic, boosterAmount);
        if (val <= 0)
        {
            return ItemRarity.Epic;
        }
        return ItemRarity.Legendary;
    }

    public static string RarityArticle(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Uncommon => "an",
            ItemRarity.Epic => "an",
            _ => "a",
        };
    }

    public static bool IsFishOnIsland(string fishID, IslandType islandType)
    {
        IslandType fishIsland = Instance.allItems[fishID].islandType;
        return fishIsland == IslandType.MainIsland || fishIsland == islandType;
    }

    private static bool initialized = false;
    private static Action OnFishManagerReady;
    public static void RequestInit(Action onReady)
    {
        if (initialized)
            onReady?.Invoke();
        else
            OnFishManagerReady += onReady;
    }


    public override void Awake()
    {
        Instance = this;
        InitializeFishDescriptions();

        //Currently unused (or at the very least, not fishes)
        CreateUnused("Fish_Candy", "Fish Candy", "ui/fish_candy.png", 999);
        CreateUnused("Remains_of_the_Sea", "Remains of the Sea", "new_fish/fish-fish_skeleton.png");

        // Trash/Common non-teamable items
        AddFish("Soggy_Boot", "Soggy Boot", "new_fish/fish-boot.png", ItemRarity.Common, false);
        AddFish("Trash_Bag", "Trash Bag", "new_fish/fish-trash.png", ItemRarity.Common, false);
        AddFish("Rusted_Can", "Rusted Can", "new_fish/fish-can.png", ItemRarity.Common, false);

        // Common Fish
        AddFish("Troutling", "Troutling", "new_fish/fish-fish1.png", ItemRarity.Common, true);
        AddFish("Tiny_Goldie", "Tiny Goldie", "new_fish/fish-fish2.png", ItemRarity.Common, true);
        AddFish("Pufferperil", "Pufferperil", "new_fish/fish-fish4.png", ItemRarity.Common, true);
        AddFish("Clown_Bubbler", "Clown Bubbler", "new_fish/fish-clown_fish.png", ItemRarity.Common, true);
        AddFish("BigLipBrute", "Big Lip Brute", "new_fish/fish-ugly_fish.png", ItemRarity.Common, true);
        AddFish("Amberfin", "Amberfin", "new_fish/fish-fish_orange.png", ItemRarity.Common, true);
        AddFish("RosyDart", "Rosy Dart", "new_fish/fish-fish_pink.png", ItemRarity.Common, true);
        AddFish("AmethystSwimmer", "Amethyst Swimmer", "new_fish/fish-fish_purple.png", ItemRarity.Common, true);
        AddFish("GoldenGlimmer", "Golden Glimmer", "new_fish/fish-fish_icon.png", ItemRarity.Common, true);
        AddFish("Sad_Sardine", "Sad Sardine", "new_fish/fish-sad_sardine.png", ItemRarity.Common, true, IslandType.MainIsland);
        AddFish("Blob_Fish", "Blob Fish", "new_fish/fish-blob_fish.png", ItemRarity.Common, true, IslandType.MainIsland);

        // Rare Fish
        AddFish("Noob_Fish", "Noob Fish", "new_fish/fish-noob_fish.png", ItemRarity.Rare, true, IslandType.MainIsland);
        AddFish("Boomer_Bass", "Boomer Bass", "new_fish/fish-boomer_bass.png", ItemRarity.Rare, true, IslandType.MainIsland);
        AddFish("Blue_Fish", "Blue Fish", "new_fish/fish-blue_fish.png", ItemRarity.Rare, true, IslandType.MainIsland);
        AddFish("Block_Fish", "Block Fish", "new_fish/fish-blockfish.png", ItemRarity.Rare, true, IslandType.SkullIsland);
        AddFish("Red_Claw_Crab", "Red Claw Crab", "new_fish/fish-crab.png", ItemRarity.Rare, true, IslandType.SkullIsland);
        AddFish("EmeraldBream", "Emerald Bream", "new_fish/fish-fish.png", ItemRarity.Rare, true);
        AddFish("Sword_Fish", "Sword Fish", "new_fish/fish-sword_fish.png", ItemRarity.Rare, true);
        AddFish("Lobster", "Lobster", "new_fish/fish-lobster.png", ItemRarity.Rare, true);
        AddFish("SharkyToy", "Sharky Toy", "new_fish/fish-rubber_shark.png", ItemRarity.Rare, true);
        AddFish("Chad_Cod", "Chad Cod", "new_fish/fish-chad_cod.png", ItemRarity.Rare, true, IslandType.FootIsland);
        AddFish("Sus_Snapper", "Sus Snapper", "new_fish/fish-sus_snapper.png", ItemRarity.Rare, true, IslandType.AmogusIsland);
        AddFish("BubbleBlob", "Bubble Blob", "new_fish/fish-jellyfish.png", ItemRarity.Rare, true);

        // Epic Fish
        AddFish("Chill_Fish", "Chill Fish", "new_fish/fish-chill_fish.png", ItemRarity.Epic, true, IslandType.MainIsland);
        AddFish("Skibidi_Swordfish", "Skibidi Swordfish", "new_fish/fish-skibidi_swordfish.png", ItemRarity.Epic, true, IslandType.AmogusIsland);
        AddFish("Vibing_Bass", "Vibing Bass", "new_fish/fish-vibing_bass.png", ItemRarity.Epic, true, IslandType.MainIsland);
        AddFish("Big_Chungus_Bass", "Big Chungus Bass", "new_fish/fish-big_chungus_bass.png", ItemRarity.Epic, true, IslandType.MainIsland);
        AddFish("Sigma_Shark", "Sigma Shark", "new_fish/fish-sigma_shark.png", ItemRarity.Epic, true, IslandType.MonkeyIsland);
        AddFish("Angy_Fish", "Angy Fish", "new_fish/fish-angry_fish.png", ItemRarity.Epic, true, IslandType.SkullIsland);
        AddFish("PrismFin", "Prism Fin", "new_fish/fish-rainbow_fish.png", ItemRarity.Epic, true, IslandType.CoralIsland);

        // Legendary Fish
        AddFish("Glitched_Fish", "Glitched Fish", "new_fish/fish-glitched.png", ItemRarity.Legendary, true, IslandType.MainIsland);
        AddFish("Pogger_Fish", "Pogger Fish", "new_fish/fish-pogger_fish.png", ItemRarity.Legendary, true, IslandType.MainIsland);
        AddFish("Sonic_Fish", "Sanic Fish", "new_fish/fish-sonic_fish.png", ItemRarity.Legendary, true, IslandType.CaveIsland);
        AddFish("Rizz_Fish", "Rizz Fish", "new_fish/fish-rizz_fish.png", ItemRarity.Legendary, true, IslandType.SusIsland);
        AddFish("River_Monster", "River Monster", "new_fish/fish-river_monster.png", ItemRarity.Legendary, true, IslandType.VolcanIsland);

        //Bosses
        AddBossFish("Kraken", "Kraken", "new_fish/fish-kraken.png", ItemRarity.Mythic);
        AddBossFish("Megalodon", "Megalodon", "new_fish/fish-megalodon.png", ItemRarity.Mythic);
        AddBossFish("El_Gran_Maja", "El Gran Maja", "new_fish/fish-el_gran_maja.png", ItemRarity.Mythic);
        AddFish("Bloop", "Bloop", "new_fish/fish-bloop.png", ItemRarity.Mythic, true, IslandType.FootIsland);

        initialized = true;
        OnFishManagerReady?.Invoke();
        OnFishManagerReady = null;
    }

    public static string GetFishSkin(string fishID)
    {
        return fishID switch
        {
            // Common Fish
            "Troutling" => "fish1",
            "Tiny_Goldie" => "fish2",
            "Pufferperil" => "fish4",
            "Clown_Bubbler" => "clown_fish",
            "BigLipBrute" => "ugly_fish",
            "Red_Claw_Crab" => "crab",
            "Noob_Fish" => "noob_fish",
            "Blue_Fish" => "blue_fish",
            "Sad_Sardine" => "sad_sardine",
            "Blob_Fish" => "blob_fish",
            "Block_Fish" => "blockfish",
            "Bloop" => "bloop",

            // Rare Fish
            "Amberfin" => "fish_orange",
            "RosyDart" => "fish_pink",
            "EmeraldBream" => "fish",
            "AmethystSwimmer" => "fish_purple",
            "Sword_Fish" => "sword_fish",
            "Lobster" => "lobster",
            "SharkyToy" => "rubber_shark",
            "Chad_Cod" => "chad_cod",
            "Vibing_Bass" => "vibing_bass",
            "Boomer_Bass" => "boomer_bass",
            "Sus_Snapper" => "sus_snapper",

            // Epic Fish
            "GoldenGlimmer" => "fish_icon",
            "Angy_Fish" => "angry_fish",
            "BubbleBlob" => "jellyfish",
            "Big_Chungus_Bass" => "big_chungus_bass",
            "Chill_Fish" => "chill_fish",
            "Skibidi_Swordfish" => "skibidi_swordfish",
            "Sigma_Shark" => "sigma_shark",

            // Legendary Fish
            "Sonic_Fish" => "sonic_fish",
            "PrismFin" => "rainbow_fish",
            "Glitched_Fish" => "glitched",
            "Pogger_Fish" => "pogger_fish",
            "Rizz_Fish" => "rizz_fish",
            "River_Monster" => "river_monster",

            //Non Catchable
            "El_Gran_Maja" => "el_gran_maja",
            "Megalodon" => "megalodon",
            "Kraken" => "kraken",

            // Default case
            _ => "fish",
        };
    }

    public static float GetFishXOffset(string fishID)
    {
        return fishID switch
        {
            // Common Fish
            "Troutling" => 0,
            "Tiny_Goldie" => 0,
            "Pufferperil" => 0,
            "Clown_Bubbler" => 0,
            "BigLipBrute" => 0,
            "Red_Claw_Crab" => 0,
            "Noob_Fish" => 0,
            "Blue_Fish" => 0,
            "Sad_Sardine" => 0,
            "Blob_Fish" => 0,
            "Block_Fish" => 0,
            "Bloop" => 0,

            // Rare Fish
            "Amberfin" => 0,
            "RosyDart" => 0,
            "EmeraldBream" => 0,
            "AmethystSwimmer" => 0,
            "Sword_Fish" => 0,
            "Lobster" => 0,
            "SharkyToy" => 0,
            "Chad_Cod" => 0,
            "Vibing_Bass" => 0,
            "Boomer_Bass" => 0,
            "Sus_Snapper" => 0,

            // Epic Fish
            "GoldenGlimmer" => 0,
            "Angy_Fish" => 0,
            "BubbleBlob" => 0,
            "Big_Chungus_Bass" => 0,
            "Chill_Fish" => 0,
            "Skibidi_Swordfish" => 0,
            "Sigma_Shark" => 0,

            // Legendary Fish
            "Sonic_Fish" => 0,
            "PrismFin" => 0,
            "Glitched_Fish" => 0,
            "Pogger_Fish" => 0,
            "Rizz_Fish" => 0,
            "River_Monster" => 0,

            //Non Catchable
            "El_Gran_Maja" => 1,
            "Megalodon" => 1,
            "Kraken" => 1.5f,

            // Default case
            _ => 0,
        };
    }

    public Vector4 GetFishColor(string fishID)
    {
        return UIUtils.GetRarityColor(allItems[fishID].Rarity);
    }

    List<string> fishList = new();
    public Item_Definition GetRandomFish(MyPlayer requestingPlayer, Water currentWater, Rod currentRod, int boosterAmount)
    {
        string forcedFish = GameManager.GetFishFor(requestingPlayer.Name);
        if (forcedFish != "")
        {
            return GetFish(forcedFish);
        }
        ItemRarity randomRarity = RandomRarity(requestingPlayer, currentRod, boosterAmount);
        fishList.Clear();
        foreach (string id in allItems.Keys)
        {
            if (allItems[id].Rarity == randomRarity && IsFishOnIsland(id, currentWater.islandType) && allItems[id].catchable)
                fishList.Add(id);
        }
        if(fishList.Count == 0)
        {
            return GetFish("Soggy_Boot");
        }
        return GetFish(fishList.GetRandom(Random.Shared));
    }

    public Item_Definition GetFishOfRarity(ItemRarity rarity)
    {
        fishList.Clear();
        foreach (string id in allItems.Keys)
        {
            if (allItems[id].Rarity == rarity && allItems[id].catchable)
                fishList.Add(id);
        }
        return GetFish(fishList.GetRandom(Random.Shared));
    }

    public static Item_Definition GetFish(string fishID)
    {
        return Instance.allItems.TryGetValue(fishID, out FishItem item) ? item.Item : null;
    }

    //TODO: Handle setting unique random price :D
    public static int PriceForRarity(ItemRarity rarity, int fishLevel, int boosterAmount)
    {
        //Random r = new Random();
        int price = rarity switch
        {
            ItemRarity.Common => (10 + (2 * (fishLevel - 1))) * (1 + boosterAmount),
            ItemRarity.Rare => (20 + (4 * (fishLevel - 1))) * (1 + boosterAmount),
            ItemRarity.Epic => (50 + (6 * (fishLevel - 1))) * (1 + boosterAmount),
            ItemRarity.Legendary => (100 + (10 * (fishLevel - 1))) * (1 + boosterAmount),
            ItemRarity.Mythic => (250 + (25 * (fishLevel - 1))) * (1 + boosterAmount),
            _ => 0,
        };
        //return price + (int)MathF.Round(price * (r.NextFloat() - 0.5f));
        return price;
    }



    public static ItemRarity GetFishRarity(string fishID)
    {
        return Instance.allItems.TryGetValue(fishID, out FishItem item) ? item.Rarity : ItemRarity.Common;
    }

    public static bool IsFishTeamable(string fishID)
    {
        return Instance.allItems.TryGetValue(fishID, out FishItem item) && item.teamable;
    }

    public int GetFishPrice(string fishID, int fishLevel, int boosterAmount)
    {
        var FishRarity = GetFishRarity(fishID);
        return PriceForRarity(FishRarity, fishLevel, boosterAmount);
    }

    public string GetFishDescription(string fishId, bool censorName = true)
    {
        if (!fishDescriptions.TryGetValue(fishId, out string desc))
            return "No description available.";

        string name = censorName ? "?????" : allItems[fishId].Item.Name;
        return desc.Replace("{NAME}", name);
    }

    private void InitializeFishDescriptions()
    {
        // Common Fish
        fishDescriptions["Troutling"] = "Often found in shallow waters, the {NAME} is known for its playful nature. These small fish gather in groups and are frequently seen jumping out of the water at dawn.";
        fishDescriptions["Tiny_Goldie"] = "Despite its small size, the {NAME} has a heart of gold. Local legends say spotting one brings good fortune to novice anglers.";
        fishDescriptions["Pufferperil"] = "When startled, the {NAME} can inflate to twice its size. Its distinctive pattern serves as a warning to would-be predators.";
        fishDescriptions["Clown_Bubbler"] = "The {NAME} creates small bubbles that reflect rainbow colors. These social creatures are known to perform elaborate dances to communicate.";
        fishDescriptions["BigLipBrute"] = "With its unique appearance, the {NAME} uses its oversized lips to search for food in rocky crevices. Despite its intimidating look, it's quite gentle.";
        fishDescriptions["Amberfin"] = "The scales of the {NAME} shimmer like amber in sunlight. They are said to bring warmth to the coldest waters.";
        fishDescriptions["RosyDart"] = "Swift and graceful, the {NAME} darts through the water with pink scales gleaming. They're often seen during sunset when their colors are most vibrant.";
        fishDescriptions["AmethystSwimmer"] = "The purple hue of the {NAME} deepens with age. Ancient tales speak of these fish guiding lost sailors to shore.";
        fishDescriptions["GoldenGlimmer"] = "When moonlight hits its scales, the {NAME} creates a mesmerizing light show beneath the waves. They are treasured by those lucky enough to spot one.";
        fishDescriptions["Sad_Sardine"] = "Despite its melancholic expression, the {NAME} is actually quite content. Its perpetual frown is believed to be a clever disguise to avoid attention.";
        fishDescriptions["Blob_Fish"] = "Living in the shallows, the {NAME} has a unique appearance that has made it an unlikely star among marine enthusiasts.";

        // Rare Fish
        fishDescriptions["Noob_Fish"] = "The {NAME} is notorious for its clumsy swimming and tendency to bump into things. Despite being a rare species, it still hasn't quite figured out the basics of being a fish.";
        fishDescriptions["Boomer_Bass"] = "This experienced fish has seen it all. The {NAME} is known for its wisdom and tendency to shake its head at younger fish's antics.";
        fishDescriptions["Blue_Fish"] = "The {NAME} is instantly recognizable by its distinctive button-like eye. Local legends say it traded its original eye for the ability to see the true nature of other fish.";
        fishDescriptions["Block_Fish"] = "Found near treacherous rocky outcrops and dark waters, the {NAME}'s unusual cubic shape has puzzled marine biologists for generations.";
        fishDescriptions["Red_Claw_Crab"] = "A territorial defender of treacherous waters, the {NAME} builds intricate fortresses from dark coral and obsidian stone.";
        fishDescriptions["EmeraldBream"] = "Its scales contain the same mineral composition as emeralds. The {NAME} is considered a living jewel of the sea.";
        fishDescriptions["Sword_Fish"] = "The distinctive pointed snout of the {NAME} can slice through water at incredible speeds. They are respected warriors of the deep.";
        fishDescriptions["Lobster"] = "Unlike its cousins, the {NAME} has developed a sophisticated society beneath the waves. They are known to collect shiny objects.";
        fishDescriptions["SharkyToy"] = "No one knows where these peculiar fish came from. The {NAME} appears to be a regular shark that was somehow transformed into a living toy.";
        fishDescriptions["Chad_Cod"] = "Often spotted near peculiar toe-shaped rock formations, the {NAME} spends hours perfecting its swimming form.";
        fishDescriptions["Sus_Snapper"] = "Dwelling in waters filled with mysterious vents and strange bubbles, the {NAME}'s behavior has led to countless theories about its true nature.";
        fishDescriptions["BubbleBlob"] = "Masters of disguise, the {NAME} can alter its transparency to blend with surrounding bubbles. They are living works of art.";

        // Epic Fish
        fishDescriptions["Chill_Fish"] = "Nothing seems to bother the {NAME}. These laid-back creatures float through life with an enviable serenity that affects nearby fish.";
        fishDescriptions["Skibidi_Swordfish"] = "Found in waters filled with strange mechanical sounds, the {NAME} moves to an unheard rhythm that seems to hypnotize other fish.";
        fishDescriptions["Vibing_Bass"] = "The presence of the {NAME} creates a harmonic resonance in the water that other fish find irresistible.";
        fishDescriptions["Big_Chungus_Bass"] = "An absolute unit of a fish, the {NAME} has achieved mythical status among anglers. Its mere presence causes smaller fish to stare in awe.";
        fishDescriptions["Sigma_Shark"] = "Often spotted near waters teeming with playful primates, the {NAME} follows its own path and never swims with the school.";
        fishDescriptions["Angy_Fish"] = "Dwelling in dark waters near ominous rock formations, the {NAME}'s perpetual scowl is said to scare away even the bravest predators.";
        fishDescriptions["PrismFin"] = "The scales of the {NAME} split light into beautiful rainbow patterns. They are the guardians of the most vibrant coral reefs.";

        // Legendary Fish
        fishDescriptions["Glitched_Fish"] = "Some say the {NAME} isn't supposed to exist, having swum through a tear in reality. Its presence causes strange phenomena in nearby waters.";
        fishDescriptions["Pogger_Fish"] = "Ancient scrolls speak of the {NAME} appearing during moments of great achievement. Its expression has become legendary among fish.";
        fishDescriptions["Sonic_Fish"] = "The fastest known fish in existence, the {NAME} leaves a trail of rings in its wake as it zooms through dark, echoing caverns.";
        fishDescriptions["Rizz_Fish"] = "Found in waters filled with impostor fish, the {NAME} has a mysterious ability to charm both fish and humans alike.";
        fishDescriptions["River_Monster"] = "Thriving in scalding hot waters near thermal vents, the {NAME} is said to be as old as the molten rocks it calls home.";

        // Boss Fish
        fishDescriptions["El_Gran_Maja"] = "A mythical being of immense power, the {NAME} is said to be the judge of all sea creatures. Its wisdom is matched only by its strength.";
        fishDescriptions["Megalodon"] = "An ancient terror thought to be extinct, the {NAME} has returned from the depths. Its presence alone causes the seas to tremble.";
        fishDescriptions["Kraken"] = "Master of the deepest trenches, the {NAME} is the stuff of sailors' nightmares. Some say it's not just a creature, but a force of nature itself.";
        fishDescriptions["Bloop"] = "A sound from the abyss, the {NAME} has never before been seen...";
    }

}