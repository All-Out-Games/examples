using AO;

public class FishSelectionUI : Component
{
    [AOIgnore] public static FishSelectionUI Instance;
    private static bool isShowing;
    private float _percentageShowing;
    private float percentageShowing;
    private static List<RuntimeMon> mons;
    private static System.Action<int> onChooseFish;
    private static float fishTimer = 0.0f;

    const float SELECTION_TIME = 10.0f;

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

    public override void Awake()
    {
        if (!Network.IsClient) return;
        Instance = this;
    }

    public static void Show(List<RuntimeMon> fishes, System.Action<int> onChooseFishCallback)
    {
        if (!Network.IsClient) return;
        fishTimer = 0.0f;
        mons = fishes;
        onChooseFish = onChooseFishCallback;
        isShowing = true;
        UIManager.OpenUI(Instance.DrawUI, 4);
    }

    public bool DrawUI()
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
        popupRect = popupRect.Offset(0, -200);
        if (isMobile)
        {
            popupRect = popupRect.Offset(0, -40);
        }

        // Draw background panel - white with slight transparency
        UI.Image(popupRect, Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_10.png"), new Vector4(1, 1, 1, 0.95f), defaultSlice);

        // Draw title bar with shadow effect
        var titleBarRect = popupRect.CutTop(60).CenterRect().Grow(30, 100, 30, 100).Offset(0, 30).Scale(percentageShowing);
        UI.Image(titleBarRect.Offset(2, 2), Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_8.png"), new Vector4(0, 0, 0, 0.5f), smallSlice);
        UI.Image(titleBarRect, Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_8.png"), new Vector4(0.7f, 0.7f, 0.7f, 1f), smallSlice);

        var titleSettings = UIUtils.CenteredText(true);
        UI.Text(titleBarRect, "Choose a fish!", titleSettings);

        fishTimer += Time.DeltaTime;

        if (fishTimer > SELECTION_TIME)
        {
            onChooseFish?.Invoke(0);
            isShowing = false;
            return true;
        }

        // Timer bar with yellow color
        var sliderRect = popupRect.CutTop(40).Grow(-10, -30, 0, -30);
        var progress = fishTimer / SELECTION_TIME;

        // Use SliderUI for consistent look
        SliderUI.UpdateSliderUI(sliderRect, progress, 0.0f);

        var basicTextSettings = UIUtils.CenteredText(false);

        // Draw timer text
        UI.Text(sliderRect, $"{(SELECTION_TIME - fishTimer):F1}s", basicTextSettings);

        var contentRect = popupRect.Grow(-5);
        contentRect.CutBottom(10);

        float spacing = 15;

        // Calculate square size based on available width and number of fish
        float squareSize = MathF.Min(MathF.Min(contentRect.Width / mons.Count - spacing, contentRect.Height), 150);

        // Center the grid horizontally
        float totalWidth = (squareSize + spacing) * mons.Count - spacing;
        contentRect.Min.X = contentRect.Min.X + (contentRect.Width - totalWidth) * 0.5f;
        contentRect.Max.X = contentRect.Min.X + totalWidth;

        // Draw fish grid with proper spacing
        var fishRects = UIUtils.VerticalSlice(contentRect, mons.Count, spacing);
        for (int i = 0; i < mons.Count; i++)
        {
            var fish = mons[i];
            var fishDef = FishItemManager.GetFish(fish.itemDef);
            var fishIcon = Assets.GetAsset<Texture>(fishDef.Icon);

            using var _1 = UI.PUSH_ID(i);

            // Make the rect square
            var itemRect = fishRects[i].FitAspect(1);

            // Draw item background with shadow
            UI.Image(itemRect.Offset(2, 2), Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_8.png"), new Vector4(0, 0, 0, 0.3f), smallSlice);
            UI.Image(itemRect, Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_8.png"), new Vector4(0.95f, 0.95f, 0.95f, 0.95f), smallSlice);

            var buttonSettings = new UI.ButtonSettings()
            {
                Sprite = UI.WhiteSprite,
                ColorMultiplier = new Vector4(1f, 1f, 1f, 0.0f),
                PressScaling = 0.95f
            };
            var buttonResult = UI.Button(itemRect, "", buttonSettings, default);

            // Apply scaling animation on hover/press
            float scale = 1.0f;
            if (buttonResult.Hovering)
            {
                scale = buttonResult.Active ? 0.95f : 1.05f;
            }

            itemRect = itemRect.Scale(scale);

            if (scale != 1.0f)
            {
                UI.Image(itemRect, Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_8.png"), new Vector4(1f, 1f, 1f, 0.2f), smallSlice);
            }

            if (buttonResult.Clicked)
            {
                isShowing = false;
                onChooseFish?.Invoke(i);
            }

            // Draw fish icon
            var iconRect = itemRect.FitAspect(fishIcon.Aspect);
            UI.Image(iconRect, fishIcon);

            // Draw type icon overlay in top right
            var typeIconSize = squareSize * 0.25f; // Make type icon scale with square size

            if (fish.type != "")
            {
                var typeIconRect = itemRect.TopRightRect().Grow(0, 0, typeIconSize, typeIconSize).Offset(-5, -5).Scale(percentageShowing);
                var typeTexture = Assets.GetAsset<Texture>($"ui/type_{fish.type}.png");
                UI.Image(typeIconRect.FitAspect(typeTexture.Aspect), typeTexture);
            }

            // Draw level overlay in bottom left
            var levelRect = itemRect.BottomLeftRect().Grow(typeIconSize, typeIconSize * 2, 0, 0).Offset(5, 5).Scale(percentageShowing);
            UI.Text(levelRect, $"Lv. {fish.level}", new UI.TextSettings()
            {
                Font = UI.Fonts.BarlowBold,
                Size = 23,
                Color = Vector4.White,
                HorizontalAlignment = UI.HorizontalAlignment.Center,
                VerticalAlignment = UI.VerticalAlignment.Center,
                Outline = true,
                OutlineThickness = 4
            });

            // Info section at the bottom - now just the name
            var nameRect = itemRect.BottomRect().CutTop(25).Scale(percentageShowing);
            UI.Text(nameRect, fishDef.Name, basicTextSettings);
        }

        return true;
    }
}