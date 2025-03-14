using AO;

public partial class Store
{
    public const string StarterPackProductId = "67c6032156b855d48dbbd74e";

    public static List<ShopCategory.ProductDescription> BoatsProducts = new()
    {
        new () {
            Id = "Boat_Basic",
            Rarity = ItemRarity.Common,
            Price = 3000,
            Icon = "Boats/boat_1.png",
            Currency = Store.MoneyCurrency,
            Name = "Basic Boat",
            Description = "A basic boat that can be used to travel across the water.",
        },
        new () {
            Id = "Boat_Speed",
            Rarity = ItemRarity.Rare,
            Price = 30000,
            Icon = "Boats/boat_2.png",
            Currency = Store.MoneyCurrency,
            Name = "Speed Boat",
            Description = "A boat that can travel faster than the basic boat.",
        }
    };

    public static List<ShopCategory.ProductDescription> BuffProducts = new()
    {
        new () { Id = "Buff_Luck_Booster",Rarity = ItemRarity.Legendary, Price = 0, SparksProductId = "67a530b510f92be19e341609", Icon = "ui/buffs/luck_boost.png", Name = "Fish Luck Booster", Description = "Get +20% increased chance to catch Epic or Legendary rarity fishes for 20 minutes!", },
        new () { Id = "Buff_Coin_Booster",Rarity = ItemRarity.Legendary, Price = 0, SparksProductId = "67a53103fc3017a7b4c5a5be", Icon = "ui/buffs/coin_boost.png", Name = "Coin Booster", Description = "Get 2x Fish Coins from selling any fish for 20 minutes!", },
        new () { Id = "Buff_XP_Booster",Rarity = ItemRarity.Legendary, Price = 0, SparksProductId = "67a53171157778c5f58316ed", Icon = "ui/buffs/xp_boost.png", Name = "XP Booster", Description = "Get 2x Player XP gain from any battles or fishes caught for 20 minutes!", },
        new () { Id = "Buff_Master_Combo",Rarity = ItemRarity.Mythic, Price = 0, SparksProductId = "67a531ee7a373e7f1be23264", Icon = "ui/buffs/deal.png", Name = "Fish Master Combo", Description = "Get 20% higher chance to catch Epic or Legendary fishes, 2x Player XP gain from battles and caught fish, and 2x Fish Coins from every sold fish, all at the same time, for 20 minutes!", },
    };

    public static List<ShopCategory.ProductDescription> CandyProducts = new()
    {
        new () { Id = "Fish_Candy",Rarity = ItemRarity.Legendary, Price = 0, SparksProductId = "67af86569e355e84b3be8837", Icon = "ui/fish_candy.png", Name = "Fish Candy", Description = "Get 1 Level for the fish of your choice!", },
        new () { Id = "Fish_Candy_10_Combo",Rarity = ItemRarity.Mythic, Price = 0, SparksProductId = "67af86b0c3cef7a5d31dc063", Icon = "ui/fish_candy10.png", Name = "Fish Candy (10x)", Description = "Get 10 Levels for the fish of your choice!", },
        new () { Id = "Fish_Candy_50_Combo",Rarity = ItemRarity.Mythic, Price = 0, SparksProductId = "67af870873e8d6ab1cb679e2", Icon = "ui/fish_candy50.png", Name = "Fish Candy (50x)", Description = "Get 50 Levels for the fish of your choice!", },
    };
}
