using AO;
using System.Linq;

public static class StarterPackUI
{
    private static bool isShowing;
    private static float _percentageShowing;
    private static float percentageShowing;
    private static System.Action onPurchase;

    private static UI.NineSlice defaultSlice = new UI.NineSlice()
    {
        slice = new Vector4(64, 64, 64, 64),
        sliceScale = 0.4f
    };

    private static UI.NineSlice smallSlice = new UI.NineSlice()
    {
        slice = new Vector4(32, 32, 32, 32),
        sliceScale = 0.4f
    };

    public static void Show()
    {
        if (!Network.IsClient) return;
        
        isShowing = true;
        UIManager.OpenUI(DrawUI, 4);
    }

    public static bool DrawUI()
    {
        _percentageShowing += isShowing ? Time.DeltaTime * 2.0f : -Time.DeltaTime * 2.0f;
        if (_percentageShowing >= 1)
        {
            _percentageShowing = 1;
        }

        percentageShowing = Ease.InOutQuart(_percentageShowing);

        if (!isShowing && _percentageShowing <= 0)
        {
            _percentageShowing = 0;
            return false;
        }

        using var _col = UI.PUSH_COLOR_MULTIPLIER(new Vector4(1, 1, 1, percentageShowing));

        bool isMobile = Game.IsMobile;
        using var _ = UI.PUSH_SCALE_FACTOR(UI.ScreenScaleFactor * 1.3f, isMobile);

        var screenRect = UI.ScreenRect;
        //darken screen
        UI.Image(screenRect, UI.WhiteSprite, new Vector4(0, 0, 0, 0.75f * percentageShowing));

        float width = (isMobile ? 75 : 60) * 5;
        var popupRect = UI.ScreenRect.CenterRect().Grow(150, width, 150, width).Scale(percentageShowing);

        // Draw background panel - white with slight transparency
        var backgroundSlice = new UI.NineSlice()
        {
            slice = new Vector4(64, 64, 64, 64),
            sliceScale = 0.2f
        };
        var buttonSettings = new UI.ButtonSettings()
        {
            Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_10.png"),
            ColorMultiplier = new Vector4(0, 0, 0, 1f),
            Slice = defaultSlice
        };
        if (UI.Button(popupRect, "", buttonSettings, default).Clicked)
        {
            var player = MyPlayer.localPlayer;
            bool hasInventorySpace = player.DefaultInventory.Items.Any(item => item == null);
            
            if (player.IsTeamFull())
            {
                Notifications.Show("Cannot purchase Starter Pack - Please make space in your fish team first!");
            }
            else if (!hasInventorySpace)
            {
                Notifications.Show("Cannot purchase Starter Pack - Please make space in your inventory first!");
            }
            else
            {
                Purchasing.PromptPurchase(Store.StarterPackProductId);
                isShowing = false;
                onPurchase?.Invoke();
            }
        }

        var gradientTexture = Assets.GetAsset<Texture>("ui/display_gradient.png");
        var gradientRect = popupRect.Grow(0, 0, -50, 0);
        var gradientColor = UIUtils.GetRarityColor(ItemRarity.Legendary);
        UI.Image(gradientRect, gradientTexture, gradientColor, backgroundSlice);

        // Draw title bar with shadow effect
        var titleBarRect = gradientRect.CutTop(60).CenterRect().Grow(30, 100, 30, 100).Offset(0, 30).Scale(percentageShowing);
        UI.Image(titleBarRect.Offset(2, 2), Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_8.png"), new Vector4(0, 0, 0, 0.5f), smallSlice);
        UI.Image(titleBarRect, Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_8.png"), new Vector4(0.7f, 0.7f, 0.7f, 1f), smallSlice);

        var titleSettings = UIUtils.CenteredText(true);
        UI.Text(titleBarRect, "Starter Pack", titleSettings);

        // Add timer display
        var timerRect = titleBarRect.Offset(0, -40).Grow(0,50,10,50);
        var timerTextSettings = UIUtils.CenteredText(false);
        timerTextSettings.Color = Vector4.White;
        timerTextSettings.Outline = true;
        timerTextSettings.OutlineColor = Vector4.Black;
        UI.Text(timerRect, $"Time Left: {MyPlayer.localPlayer.starterPackTimer.GetRemainingTimeFormattedFancy()}", timerTextSettings);

        var contentRect = gradientRect.Grow(-10,-5,-5,-5);
        contentRect.CutBottom(10);

        // Draw three reward sections
        var rewardSections = UIUtils.VerticalSlice(contentRect, 3, 20);
        var basicTextSettings = UIUtils.CenteredText(true);
        basicTextSettings.AutofitMaxSize = 30;

        // 1. Rare Candies
        var candyRect = rewardSections[0];
        var candyIcon = Assets.GetAsset<Texture>("ui/fish_candy10.png"); // Pink candy icon
        var candyIconRect = candyRect.CutTop(100).FitAspect(candyIcon.Aspect);
        UI.Image(candyIconRect, candyIcon);
        candyRect = candyRect.Offset(0, 20);
        UI.Text(candyRect, "10", basicTextSettings);
        candyRect = candyRect.Offset(0, -30);
        UI.Text(candyRect, "Rare Candies", basicTextSettings);

        // 2. Legendary Fish
        var fishRect = rewardSections[1];
        var fishIcon = Assets.GetAsset<Texture>("new_fish/fish-river_monster.png");
        var fishIconRect = fishRect.CutTop(100).FitAspect(fishIcon.Aspect);
        UI.Image(fishIconRect, fishIcon, new Vector4(0, 0, 0, 1)); // Black silhouette
        var iconTextSettings = UIUtils.CenteredText(true);
        iconTextSettings.AutofitMaxSize = 100;
        UI.Text(fishIconRect, "?", iconTextSettings);
        fishRect = fishRect.Offset(0, 20);
        UI.Text(fishRect, "1", basicTextSettings);
        fishRect = fishRect.Offset(0, -30);
        UI.Text(fishRect, "Legendary Fish", basicTextSettings);

        // 3. Gold
        var goldRect = rewardSections[2];
        var goldIcon = Assets.GetAsset<Texture>("ui/coin.png");
        var goldIconRect = goldRect.CutTop(100).FitAspect(goldIcon.Aspect);
        UI.Image(goldIconRect, goldIcon);
        goldRect = goldRect.Offset(0, 20);
        UI.Text(goldRect, "50K", basicTextSettings);
        goldRect = goldRect.Offset(0, -30);
        UI.Text(goldRect, "Gold", basicTextSettings);

        var price = Purchasing.GetProduct(Store.StarterPackProductId).Price;

        // Draw black price section at the bottom
        var priceRect = popupRect.BottomCenterRect().Grow(50, 60, 0, 60);
        var priceTextSettings = UIUtils.CenteredText(true);
        priceTextSettings.VerticalAlignment = UI.VerticalAlignment.Top;
        priceTextSettings.Color = Vector4.White;
        UI.Text(priceRect, $"{price}", priceTextSettings);
        var sparksIcon = Assets.GetAsset<Texture>("$AO/new/modal/modal_v2/spark_icon_temp.png");
        var sparksIconRect = priceRect.CutRight(10).FitAspect(sparksIcon.Aspect, Rect.FitAspectKind.KeepHeight);
        UI.Image(sparksIconRect, sparksIcon);

        var _exit = UI.PUSH_ID("exitButton");
        var exitButtonIcon = Assets.GetAsset<Texture>("$AO/new/modal/modal_v2/close_button.png");
        var exitButtonRect = popupRect.TopRightRect().Grow(0,0,40,40).Offset(-15, -15).FitAspect(exitButtonIcon.Aspect);
        if (UI.Button(exitButtonRect, "", new UI.ButtonSettings()
        {
            Sprite = exitButtonIcon
        }, default).Clicked)
        {
            isShowing = false;
        }
        return true;
    }
}