using AO;
using System.Linq;

public partial class Store : System<Store>
{
    public const string MoneyCurrency = "FishCoin";

    public Shop rodShop, boatShop, fishShop, sparksShop;
    public ShopCategory fishCat;

    public void CreateBoatShop()
    {
        boatShop = Economy.CreateShop("Boat Shop");
        boatShop.SetPurchaseModifier(OnBeforeItemPurchase);
        if (Network.IsClient)
        {
            boatShop.SetCustomDisplay(CustomItemShopDisplay);
        }
        if (Network.IsServer)
        {
            boatShop.SetPurchaseHandler(OnBoatPurchaseSuccessfull);
        }

        var boatsCat = boatShop.AddCategory("Boats");
        boatsCat.Icon = "Boats/boat_1.png";
        foreach (var p in BoatsProducts)
        {
            boatsCat.AddProduct(p);
        }
    }

    public void CreateRodShop()
    {
        rodShop = Economy.CreateShop("Rod Shop");
        rodShop.SetPurchaseModifier(OnBeforeItemPurchase);
        if (Network.IsClient)
        {
            rodShop.SetCustomDisplay(CustomRodsDisplay);
        }
        if (Network.IsServer)
        {
            rodShop.SetPurchaseHandler(OnFisherPurchaseSuccessfull);
        }
        var rodsCat = rodShop.AddCategory("Rods");
        rodsCat.Icon = "Rods/rod_ability.png";

        RodsManager.InitRodsDescriptions();
        foreach (var p in RodsManager.RodsProducts)
        {
            rodsCat.AddProduct(p.Product);
        }
    
        // var paidCat = rodShop.AddCategory("Sparks");
        // paidCat.Icon = "ui/buffs/master_combo.png";
        // foreach (var p in BuffProducts)
        // {
        //     paidCat.AddProduct(p);
        // }
        // foreach (var p in CandyProducts)
        // {
        //     paidCat.AddProduct(p);
        // }
    }

    public void CreateSparksShop()
    {
        sparksShop = Economy.CreateShop("Sparks Shop");
        sparksShop.SetPurchaseModifier(OnBeforeItemPurchase);
        if (Network.IsClient)
        {
            sparksShop.SetCustomDisplay(CustomRodsDisplay);
        }

        var paidCat = sparksShop.AddCategory("Buffs");
        paidCat.Icon = "ui/buffs/master_combo.png";
        foreach (var p in BuffProducts)
        {
            paidCat.AddProduct(p);
        }
        paidCat = sparksShop.AddCategory("Candy");
        paidCat.Icon = "ui/fish_candy.png";
        foreach (var p in CandyProducts)
        {
            paidCat.AddProduct(p);
        }
    }

    public void CreateFishShop()
    {
        fishShop = Economy.CreateShop("Fish Shop");
        fishShop.SetPurchaseModifier(OnBeforeItemPurchase);
        if (Network.IsClient)
        {
            fishShop.SetCustomDisplay(CustomItemShopDisplay);
        }
        if (Network.IsServer)
        {
            fishShop.SetPurchaseHandler(OnFisherPurchaseSuccessfull);
        }

        fishCat = fishShop.AddCategory("Fishes");
        fishCat.Icon = "new_fish/fish-fish.png";
    }

    public override void Start()
    {
        Economy.RegisterCurrency(MoneyCurrency, "ui/coin.png");
        if (Network.IsServer) Purchasing.SetPurchaseHandler(SparksPurchaseHandler);

        CreateBoatShop();
        CreateRodShop();
        CreateFishShop();
        CreateSparksShop();
    }

    private bool SparksPurchaseHandler(Player _player, string productId)
    {
        var player = (MyPlayer)_player;

        if(productId == Store.StarterPackProductId)
        {
            GameManager.TryGiveCandy(player, 10);
            var legendaryFish = FishItemManager.Instance.GetFishOfRarity(ItemRarity.Legendary);
            GameManager.AddFish(player, legendaryFish.Id);
            AddMoney(player, 50000);
            player.boughtStarterPack.Set(true);
            AO.Save.SetInt(player, "boughtStarterPack", 1);
            return true;
        }

        if (BuffProducts.Any(x => x.SparksProductId == productId))
        {
            var buffProduct = BuffProducts.FirstOrDefault(x => x.SparksProductId == productId);
            return GameManager.ApplyBuffToPlayer(player, buffProduct.Id);
        }

        if (CandyProducts.Any(x => x.SparksProductId == productId))
        {
            var candyProduct = CandyProducts.FirstOrDefault(x => x.SparksProductId == productId);
            switch (candyProduct.Id)
            {
                case "Fish_Candy":
                    return GameManager.TryGiveCandy(player, 1);
                case "Fish_Candy_10_Combo":
                    return GameManager.TryGiveCandy(player, 10);
                case "Fish_Candy_50_Combo":
                    return GameManager.TryGiveCandy(player, 50);
            }
        }

        return false;
    }


    #region ItemShop
    public PurchaseModification OnBeforeItemPurchase(Player _player, GameProduct product)
    {
        var player = (MyPlayer)_player;

        var modification = new PurchaseModification(product)
        {
            ModifyProduct = false
        };

        if (MyPlayer.localPlayer != null && product.Id.StartsWith("FISH"))
        {
            modification.ModifyProduct = true;
            modification.PurchaseButtonText = "Sell";
            int fish = int.Parse(product.Id.Substring(5, 1));
            modification.OnBuyButtonClicked = () => MyPlayer.localPlayer.RequestSellFish(fish);
        }

        if (MyPlayer.localPlayer != null && product.Id.StartsWith("Rod"))
        {
            if (MyPlayer.localPlayer.currentRodID == product.Id)
            {
                modification.ModifyProduct = true;
                modification.PurchaseButtonText = "Owned";
                modification.Color = PurchaseButtonColor.Grey;
                modification.OnBuyButtonClicked = () => PurchaseFail(_player);
            }
            else
            {
                var currentRodIndex = RodsManager.RodsProducts.First(x => x.Product.Id == MyPlayer.localPlayer.lastRodID.Value).RodIndex;
                var newRodIndex = RodsManager.RodsProducts.First(x => x.Product.Id == product.Id).RodIndex;
                if (newRodIndex > currentRodIndex)
                {
                    if (newRodIndex - currentRodIndex > 1)
                    {
                        modification.ModifyProduct = true;
                        modification.PurchaseButtonText = "Locked";
                        modification.Color = PurchaseButtonColor.Grey;
                        modification.OnBuyButtonClicked = () => PurchaseFail(_player);
                    }
                }
                else
                {
                    modification.ModifyProduct = true;
                    modification.PurchaseButtonText = "Select";
                    modification.Color = PurchaseButtonColor.Green;
                    modification.OnBuyButtonClicked = () => MyPlayer.localPlayer.RequestSetRod(product.Id);
                }
            }
        }

        return modification;
    }


    void PurchaseFail(Player player)
    {
        MyPlayer p = (MyPlayer)player;
        if (p.IsLocal)
        {
            //SFXE.Play(Assets.GetAsset<AudioAsset>("sfx/retro_fail_sound_05.wav"), new() { });
            //p.ShakeScreen(0.35f, 0.1f);
        }
    }

    public void CustomItemShopDisplay(GameProduct product, Rect rect)
    {
        rect.CutTop(10);
        var descriptionRect = rect.CutTop(200).Inset(0, 15, 0, 15);
        UI.Text(descriptionRect, product.Description, new UI.TextSettings()
        {
            Font = UI.Fonts.Barlow,
            Size = 32,
            VerticalAlignment = UI.VerticalAlignment.Top,
            HorizontalAlignment = UI.HorizontalAlignment.Center,
            Color = Vector4.White,
            WordWrap = true,
            Outline = true,
            OutlineThickness = 3.0f,
            DoAutofit = true,
            AutofitMinSize = 16,
            AutofitMaxSize = 32,
        });
        rect.CutTop(25);
    }

    public void CustomRodsDisplay(GameProduct product, Rect rect)
    {
        var rarityRect = rect.TopRect().Grow(50, 0, 0, 0).Inset(0, 5, 0, 5).Offset(0, 400);
        var rarityColor = UIUtils.GetRarityColor(product.Rarity);
        var textSettings = UIUtils.CenteredText(true);
        textSettings.Color = rarityColor;

        bool isRod = product.Id.StartsWith("Rod");

        if (isRod)
        {
            UI.Text(rarityRect, product.Rarity.ToString(), textSettings);
        }
        
        var descriptionRect = rect.CutTop(isRod ? 90 : 150).Inset(0, 5, 0, 5);

        UI.Text(descriptionRect, product.Description, new UI.TextSettings()
        {
            Font = UI.Fonts.Barlow,
            Size = 30,
            VerticalAlignment = UI.VerticalAlignment.Top,
            HorizontalAlignment = UI.HorizontalAlignment.Center,
            Color = Vector4.White,
            WordWrap = true,
            Outline = true,
            OutlineThickness = 3.0f,
            DoAutofit = true,
            AutofitMinSize = 18,
            AutofitMaxSize = 28,
        });
        if (isRod)
        {
            rarityRect = rect.CutTop(25).Inset(0, 5, 0, 5);
            textSettings.AutofitMaxSize = 90;
            textSettings.Color = Vector4.White;
            UI.Text(rarityRect, "Catch Rates:", textSettings);
            RodsManager.GetRod(product.Id).DisplayStats(rect);
        }
        else if(product.Id.Contains("Combo"))
        {
            switch (product.Id)
            {
                case "Buff_Master_Combo":
                    UI.Text(rect, "SAVE 68 SPARKS!", textSettings);
                    break;
                case "Fish_Candy_10_Combo":
                    UI.Text(rect, "SAVE 491 SPARKS!", textSettings);
                    break;
                case "Fish_Candy_50_Combo":
                    UI.Text(rect, "SAVE 3451 SPARKS!", textSettings);
                    break;
            }
        }
    }

    public bool OnFisherPurchaseSuccessfull(Player _player, GameProduct product)
    {
        var player = (MyPlayer)_player;

        if (product.Id.StartsWith("Rod"))
        {
            player.currentRodID.Set(product.Id);
            AO.Save.SetString(player, "currentRod", product.Id);
            {
                var currentRodIndex = RodsManager.RodsProducts.First(x => x.Product.Id == player.lastRodID.Value).RodIndex;
                var newRodIndex = RodsManager.RodsProducts.First(x => x.Product.Id == product.Id).RodIndex;
                if (newRodIndex > currentRodIndex)
                {
                    player.lastRodID.Set(product.Id);
                    AO.Save.SetString(player, "lastRod", product.Id);
                }
            }
            //Economy.WithdrawCurrency(player, Store.MoneyCurrency, product.Price);
            return true;
        }
        return false;
    }

    public bool OnBoatPurchaseSuccessfull(Player _player, GameProduct product)
    {
        var player = (MyPlayer)_player;

        if (product.Id.StartsWith("Boat"))
        {
            player.currentBoatID.Set(product.Id);
            AO.Save.SetString(player, "currentBoat", product.Id);
            //Economy.WithdrawCurrency(player, Store.MoneyCurrency, product.Price);
            return true;
        }

        return false;
    }
    #endregion

    public static bool DrawShop(Shop shop)
    {
        Rect rect = UI.ScreenRect.CenterRect().Grow(300, 500, 300, 500);
        using var _ = UI.PUSH_LAYER(100);
        return shop.Draw(rect);
    }

    public static void AddMoney(MyPlayer player, int amount)
    {
        long previousBalance = Economy.GetBalance(player, MoneyCurrency);
        Economy.DepositCurrency(player, MoneyCurrency, amount);
        long currentBalance = previousBalance + amount;
        var currentRodIndex = RodsManager.RodsProducts.First(x => x.Product.Id == player.lastRodID.Value).RodIndex + 1;
        if (currentRodIndex >= 0 && currentRodIndex < RodsManager.RodsProducts.Count)
        {
            var nextRod = RodsManager.RodsProducts[currentRodIndex];
            if (player.Alive() &&previousBalance <= nextRod.Product.Price && currentBalance > nextRod.Product.Price)
            {
                player.CallClient_SendMessage($"You can now afford the next rod!", rpcTarget: player);
            }
        }
    }

    public void RefreshSell()
    {
        fishCat.ClearProducts();
        foreach (var i in MyPlayer.localPlayer.DefaultInventory.Items)
        {
            if (i == null || i.Definition.Id == "Fish_Candy")
                continue;
            string id = "FISH_" + i.InventorySlot + "_" + i.Id;
            ItemRarity rarity = FishItemManager.GetFishRarity(i.Definition.Id);
            string metadata = i.GetMetadata("Level");
            int fishLevel = int.Parse(metadata.IsNullOrEmpty() ? "1" : metadata);
            int price = FishItemManager.PriceForRarity(rarity, fishLevel, MyPlayer.localPlayer.playerBuffCoinActive ? 1 : 0);
            ShopCategory.ProductDescription product = new() { Id = id, Rarity = rarity, Price = price, Icon = i.Definition.Icon, Currency = MoneyCurrency, Name = i.Definition.Name, Description = "Lv. " + fishLevel };
            fishCat.AddProduct(product);
        }
    }
}