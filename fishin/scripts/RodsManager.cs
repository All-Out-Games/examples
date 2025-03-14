using AO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

public struct Rod
{
    public int RodIndex;
    public ShopCategory.ProductDescription Product;

    public float price => RodIndex > 1 ? 200 * (RodIndex - 1) * (RodIndex - 1) * 2.5f : 200 * RodIndex;

    public readonly float RarityToFloat(ItemRarity rarity, int boosterAmount = 0)
    {
        return rarity switch
        {
            ItemRarity.Common => 70.0f - ((4.0f * RodIndex) - (15 * boosterAmount)) * RodsManager.GlobalLuckBoost,
            ItemRarity.Rare => 25.0f + ((2.4f * RodIndex) - (5 * boosterAmount)) * RodsManager.GlobalLuckBoost,
            ItemRarity.Epic => 5.0f + ((1.5f * RodIndex) + (18f * boosterAmount)) * RodsManager.GlobalLuckBoost,
            ItemRarity.Legendary => 0.0f + ((0.1f * RodIndex) + (2.0f * boosterAmount)) * RodsManager.GlobalLuckBoost,
            _ => 0,
        };
    }

    public void DisplayStats(Rect current)
    {
        var textRect = current.CutTop(25);
        var textSettings = UIUtils.CenteredText(false);
        textSettings.AutofitMaxSize = 100;
        textSettings.Color = UIUtils.GetRarityColor(ItemRarity.Common);
        UI.Text(textRect, $"{Enum.GetName(ItemRarity.Common)}: {RarityToFloat(ItemRarity.Common):F1}%", textSettings);
        textRect = current.CutTop(25);
        textSettings.Color = UIUtils.GetRarityColor(ItemRarity.Rare);
        UI.Text(textRect, $"{Enum.GetName(ItemRarity.Rare)}: {RarityToFloat(ItemRarity.Rare):F1}%", textSettings);
        textRect = current.CutTop(25);
        textSettings.Color = UIUtils.GetRarityColor(ItemRarity.Epic);
        UI.Text(textRect, $"{Enum.GetName(ItemRarity.Epic)}: {RarityToFloat(ItemRarity.Epic):F1}%", textSettings);
        textRect = current.CutTop(25);
        textSettings.Color = UIUtils.GetRarityColor(ItemRarity.Legendary);
        UI.Text(textRect, $"{Enum.GetName(ItemRarity.Legendary)}: {RarityToFloat(ItemRarity.Legendary):F1}%", textSettings);
        if (RodIndex >= 12)
        {
            textRect = current.CutTop(25);
            textSettings.Color = UIUtils.GetRarityColor(ItemRarity.Mythic);
            UI.Text(textRect, $"Mythical Bosses: {1}%", textSettings);
        }
    }
}

static class RodsManager
{
    public static float GlobalLuckBoost = 1.0f;
    public static List<Rod> RodsProducts = new()
    {
        new () { RodIndex = 0, Product = new () {
            Id = "Rod_Wood",
            Rarity = ItemRarity.Common,
            Price = 1,
            Icon = "Rods/wood_rod.png",
            Currency = Store.MoneyCurrency,
            Name = "Wood Rod",
            Description = "A simple handcrafted wooden rod. While basic, it gets the job done. Perfect for beginners starting their fishing journey.",
        }},
        new () { RodIndex = 1, Product = new () {
            Id = "Rod_Basic",
            Rarity = ItemRarity.Common,
            Price = 25,
            Icon = "Rods/rod_hand_basic.png",
            Currency = Store.MoneyCurrency,
            Name = "Basic Rod",
            Description = "A standard fishing rod with improved durability and better catch rates. A reliable companion for any aspiring fisher.",
        }},
        new () { RodIndex = 2, Product = new () {
            Id = "Rod_Advanced",
            Rarity = ItemRarity.Common,
            Price = 100,
            Icon = "Rods/rod_advanced.png",
            Currency = Store.MoneyCurrency,
            Name = "Advanced Rod",
            Description = "A professionally crafted rod with enhanced features. Its special design increases the chances of catching rare fish.",
        }},
        new () { RodIndex = 3, Product = new () {
            Id = "Rod_Banana",
            Rarity = ItemRarity.Rare,
            Price = 1000,
            Icon = "Rods/banana_rod_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Banana Rod",
            Description = "A whimsical rod shaped like a banana. Fish seem strangely attracted to its fruity appearance. Some say it brings good luck!",
        }},
        new () { RodIndex = 4, Product = new () {
            Id = "Rod_Spoon",
            Rarity = ItemRarity.Rare,
            Price = 1000,
            Icon = "Rods/spoon_rod_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Spoon Rod",
            Description = "A peculiar rod with a spoon-shaped lure. Its shiny surface creates mesmerizing reflections that attract rare fish species.",
        }},
        new () { RodIndex = 5, Product = new () {
            Id = "Rod_Balloon",
            Rarity = ItemRarity.Rare,
            Price = 1000,
            Icon = "Rods/balloon_rod_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Balloon String Rod",
            Description = "An unconventional rod made from enchanted balloon string. Its lightweight design allows for incredibly precise casting.",
        }},
        new () { RodIndex = 6, Product = new () {
            Id = "Rod_Lightsaber",
            Rarity = ItemRarity.Epic,
            Price = 1000,
            Icon = "Rods/lightsaber_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Lightsaber Rod",
            Description = "A high-tech fishing rod that hums with energy. Its beam of light can be used to fish in the darkest depths. May the fish be with you!",
        }},
        new () { RodIndex = 7, Product = new () {
            Id = "Rod_Plasma",
            Rarity = ItemRarity.Legendary,
            Price = 1000,
            Icon = "Rods/plasma_rod_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Plasma Rod",
            Description = "An experimental rod containing superheated plasma. The energy field it generates seems to excite nearby fish into a feeding frenzy.",
        }},
        new () { RodIndex = 8, Product = new () {
            Id = "Rod_Hologram",
            Rarity = ItemRarity.Legendary,
            Price = 1000,
            Icon = "Rods/hologram_rod_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Hologram Rod",
            Description = "A rod that exists between reality and digital space. Projects holographic lures that fish mistake for their favorite prey.",
        }},
        new () { RodIndex = 9, Product = new () {
            Id = "Rod_Galaxy",
            Rarity = ItemRarity.Legendary,
            Price = 1000,
            Icon = "Rods/galaxy_rod_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Galaxy Rod",
            Description = "A cosmic rod containing the essence of distant stars. Its otherworldly power attracts fish from across the universe.",
        }},
        new () { RodIndex = 10, Product = new () {
            Id = "Rod_Aurora",
            Rarity = ItemRarity.Legendary,
            Price = 1000,
            Icon = "Rods/aurora_rod_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Aurora Rod",
            Description = "A mystical rod that glows like the northern lights. Its ethereal aura greatly increases the chances of legendary catches.",
        }},
        new () { RodIndex = 11, Product = new () {
            Id = "Rod_Crystal",
            Rarity = ItemRarity.Legendary,
            Price = 1000,
            Icon = "Rods/crystal_rod_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Crystal Rod",
            Description = "A rod carved from pure crystal that resonates with magical energy. Each cast creates harmonious vibrations that entrance rare fish.",
        }},
        new () { RodIndex = 12, Product = new () {
            Id = "Rod_Rainbow",
            Rarity = ItemRarity.Mythic,
            Price = 1000,
            Icon = "Rods/rainbow_rod_icon.png",
            Currency = Store.MoneyCurrency,
            Name = "Rainbow Rod",
            Description = "A magical rod that shimmers with all colors of the rainbow. Its prismatic energy seems to attract the most elusive fish.",
        }}
    };

    public static void InitRodsDescriptions()
    {
        for (int i = 0; i < RodsProducts.Count; i++)
        {
            Rod rod = RodsProducts[i];
            rod.Product.Price = (long)rod.price;
            RodsProducts[i] = rod;
        }
    }

    public static Rod GetRod(string productID)
    {
        foreach (var item in RodsProducts)
        {
            if (item.Product.Id == productID)
            {
                return item;
            }
        }
        return RodsProducts[0];
    }
}
