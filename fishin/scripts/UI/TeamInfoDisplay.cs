using AO;

public static class TeamInfoDisplay
{
    private static Vector4 panelColor = new Vector4(0.1f, 0.1f, 0.1f, 0.95f);
    private static Vector4 selectedColor = new Vector4(0.2f, 0.5f, 0.2f, 0.95f);
    private static int selectedIndex = 0;
    private static float openTime = 0;
    private static bool isOpen = false;
    private static bool showInventory = false;
    private static bool showAllFish = true;

    static UI.NineSlice defaultSlice = new UI.NineSlice()
    {
        slice = new Vector4(64, 64, 64, 64),
        sliceScale = 0.4f
    };

    public static void DisplayTeam(bool openOnTeamPage = false)
    {
        if (isOpen)
        {
            isOpen = false;
            return;
        }
        isOpen = true;
        openTime = Time.TimeSinceStartup;
        showAllFish = !openOnTeamPage;
        selectedIndex = 0; // Reset selection when opening
        UIManager.OpenUI(DrawTeamInfo);
    }

    private static void FindNextFish()
    {
        selectedIndex++;
        selectedIndex %= MyPlayer.localPlayer.FishTeam.Items.Length;
    }

    public static bool ItemValid(Item_Instance item)
    {
        return item != null && FishItemManager.IsFishTeamable(item.Definition.Id);
    }


    public static bool DrawTeamInfo()
    {
        bool isMobile = Game.IsMobile;
        if (isMobile)
        {
            UI.PushScaleFactor(1.3f);
        }

        var screenRect = UI.ScreenRect;
        //darken screen
        UI.Image(screenRect, UI.WhiteSprite, new Vector4(0, 0, 0, 0.75f));

        // Make window even larger and ensure it fits on screen
        var teamRect = screenRect.CenterRect().Grow(300, 500, 250, 500);

        if (isMobile)
        {
            teamRect = teamRect.Offset(0, -40);
        }

        var fadeIn = MathF.Min(1.0f, Ease.OutQuart(Ease.T(Time.TimeSinceStartup - openTime, 0.5f)));

        var backgroundImage = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_10.png");

        // Create title bar above main content with two sections
        var titleBarRect = teamRect.CutTop(60).CenterRect().Grow(30, 200, 30, 200).Offset(-30, 2.5f);
        UI.Image(titleBarRect, backgroundImage, new Vector4(0.8f, 0.8f, 0.8f, fadeIn), defaultSlice);

        // Split title bar into two sections
        var leftTitleRect = titleBarRect.CutLeftUnscaled(titleBarRect.Width / 2).Grow(-5f);
        var rightTitleRect = titleBarRect.Grow(-5f);

        // Draw section toggle buttons
        var titleSettings = new UI.TextSettings()
        {
            Font = UI.Fonts.BarlowBold,
            Size = 32,
            Color = Vector4.White,
            HorizontalAlignment = UI.HorizontalAlignment.Center,
            VerticalAlignment = UI.VerticalAlignment.Center,
            Outline = true,
            OutlineThickness = 2
        };

        var buttonSettings = new UI.ButtonSettings()
        {
            Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_3.png"),
            ColorMultiplier = !showAllFish ? Vector4.White : new Vector4(0.5f, 0.5f, 0.5f, 1),
            PressScaling = 0.95f,
            Slice = defaultSlice
        };

        var allFishButtonSettings = buttonSettings;
        allFishButtonSettings.ColorMultiplier = showAllFish ? Vector4.White : new Vector4(0.5f, 0.5f, 0.5f, 1);

        if (UI.Button(leftTitleRect, "MY TEAM", buttonSettings, titleSettings).Clicked)
        {
            showAllFish = false;
        }

        if (UI.Button(rightTitleRect, "FISHPEDIA", allFishButtonSettings, titleSettings).Clicked)
        {
            showAllFish = true;
        }

        // Draw close button
        using var _closeId = UI.PUSH_ID("close_button");
        var closeRect = titleBarRect.RightRect().CutLeft(60).Offset(5, 0);
        var closeButtonSettings = new UI.ButtonSettings()
        {
            Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_3.png"),
            Slice = defaultSlice,
            PressScaling = 0.9f
        };

        var closeTextSettings = titleSettings;
        closeTextSettings.Size = 32;

        if (UI.Button(closeRect, "X", closeButtonSettings, closeTextSettings).Clicked)
        {
            isOpen = false;
        }

        var contentRect = teamRect.Inset(10);

        if (showAllFish)
        {
            DrawAllFishGrid(contentRect, backgroundImage, fadeIn);
        }
        else
        {
            // Original team info display code
            var leftRect = contentRect.CutLeft(375).Grow(-20, 0, -20, 0).Offset(20, 0);
            var rightRect = contentRect;

            UI.Image(leftRect, backgroundImage, new Vector4(0.7f, 0.7f, 0.7f, fadeIn), defaultSlice);
            leftRect = leftRect.Grow(-5f, -20f, -5f, -5f);

            DrawTeamList(leftRect);

            UI.Image(rightRect, backgroundImage, new Vector4(1, 1, 1, fadeIn), defaultSlice);
            rightRect = rightRect.Grow(-5f);

            var items = showInventory ? MyPlayer.localPlayer.DefaultInventory.Items : MyPlayer.localPlayer.FishTeam.Items;
            if (selectedIndex >= 0 && selectedIndex < items.Length)
            {
                var selectedFish = items[selectedIndex];
                if (!ItemValid(selectedFish))
                {
                    int checkedCount = 0;
                    int startIndex = selectedIndex;

                    while (!ItemValid(selectedFish) && checkedCount < items.Length)
                    {
                        FindNextFish();
                        selectedFish = items[selectedIndex];
                        checkedCount++;

                        if (selectedIndex == startIndex)
                            break;
                    }
                }

                if (ItemValid(selectedFish))
                {
                    DrawFishDetails(rightRect, selectedFish);
                }
                else
                {
                    var messageSettings = titleSettings;
                    messageSettings.Size = 30;
                    messageSettings.Color = new Vector4(0.7f, 0.7f, 0.7f, 1);
                    UI.Text(rightRect.CenterRect(), "No fish available", messageSettings);
                }
            }
            else
            {
                var messageSettings = titleSettings;
                messageSettings.Size = 30;
                messageSettings.Color = new Vector4(0.7f, 0.7f, 0.7f, 1);
                UI.Text(rightRect.CenterRect(), "Select a fish to view details", messageSettings);
            }
        }

        if (isMobile)
        {
            UI.PopScaleFactor();
        }

        return isOpen;
    }

    private static void DrawTeamList(Rect rect)
    {
        // Cut space at bottom for the toggle switch
        var pageRect = rect;
        var toggleRect = pageRect.CutBottom(50).Offset(0, -55);

        var itemHeight = 100f;
        var spacing = 15f;

        var sv = UI.PushScrollView("team_list", rect, new UI.ScrollViewSettings()
        {
            Vertical = true
        });
        {
            var contentRect = sv.contentRect;
            var items = showInventory ? MyPlayer.localPlayer.DefaultInventory.Items : MyPlayer.localPlayer.FishTeam.Items;

            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (!ItemValid(item))
                {
                    //contentRect = contentRect.CutTop(itemHeight + spacing);
                    continue;
                }


                var itemRect = contentRect.CutTop(itemHeight);
                contentRect = contentRect.CutTop(spacing);

                using var _ = UI.PUSH_ID(i);

                // Get rarity color for background
                var rarityColor = FishItemManager.Instance.GetFishColor(item.Definition.Id);

                // Make the entire row clickable with proper nine-slice
                var buttonSettings = new UI.ButtonSettings()
                {
                    Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_10.png"),
                    ColorMultiplier = i == selectedIndex ?
                        rarityColor * new Vector4(1f, 1f, 1f, 1.0f) :
                        rarityColor * new Vector4(1f, 1f, 1f, 0.8f),
                    PressScaling = 0.98f,
                    Slice = defaultSlice
                };

                if (UI.Button(itemRect.Inset(5), "", buttonSettings, default).Clicked)
                {
                    selectedIndex = i;
                }

                // Draw fish icon with jump animation when selected
                var iconRect = itemRect.CutLeft(itemHeight).Inset(8);
                var fishTexture = Assets.GetAsset<Texture>(item.Definition.Icon);
                if (i == selectedIndex)
                {
                    // Bouncy jump animation
                    float bounce = MathF.Abs(MathF.Sin(Time.TimeSinceStartup * 3)) * 8;
                    iconRect = iconRect.Offset(0, bounce);
                }
                UI.Image(iconRect.FitAspect(fishTexture.Aspect), fishTexture);

                // Draw fish info
                var textRect = itemRect.Inset(20).Offset(30, 0);
                var nameSettings = new UI.TextSettings()
                {
                    Font = UI.Fonts.BarlowBold,
                    Size = 28,
                    Color = Vector4.White,
                    VerticalAlignment = UI.VerticalAlignment.Top,
                    Outline = true,
                    OutlineThickness = 5
                };

                var nameRect = textRect.Grow(0, 0, -20, 0);
                UI.Text(nameRect, item.Definition.Name, nameSettings);

                // Draw level
                var levelSettings = nameSettings;
                levelSettings.Size = 23;
                levelSettings.VerticalAlignment = UI.VerticalAlignment.Bottom;
                levelSettings.HorizontalAlignment = UI.HorizontalAlignment.Right;
                UI.Text(textRect.Offset(-25, 0), $"Lv. {item.GetMetadata("Level")}", levelSettings);

                // Draw type icon
                var typeRect = nameRect.CutLeft(nameRect.Height).Offset(-nameRect.Height, 0);
                var typeTexture = Assets.GetAsset<Texture>($"ui/type_{item.GetMetadata("Type")}.png");
                UI.Image(typeRect.FitAspect(typeTexture.Aspect), typeTexture);


            }
        }
        UI.PopScrollView();

        // Draw toggle switch at bottom
        var switchWidth = 300f;

        toggleRect.Grow(-10);

        // Draw two buttons side by side
        var teamRect = toggleRect.CutLeft(switchWidth / 2);
        var invRect = toggleRect;

        var teamSettings = new UI.ButtonSettings()
        {
            Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_2.png"),  // Green for team
            ColorMultiplier = !showInventory ? Vector4.White : new Vector4(0.5f, 0.5f, 0.5f, 1),
            PressScaling = 0.95f,
            Slice = defaultSlice
        };

        var invSettings = new UI.ButtonSettings()
        {
            Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_5.png"),  // Blue for inventory
            ColorMultiplier = showInventory ? Vector4.White : new Vector4(0.5f, 0.5f, 0.5f, 1),
            PressScaling = 0.95f,
            Slice = defaultSlice
        };


        var textSettings = new UI.TextSettings()
        {
            Font = UI.Fonts.BarlowBold,
            Size = 24,
            Color = Vector4.White,
            HorizontalAlignment = UI.HorizontalAlignment.Center,
            VerticalAlignment = UI.VerticalAlignment.Center,
            Outline = true
        };

        if (UI.Button(teamRect.Inset(5), "Team", teamSettings, textSettings).Clicked)
        {
            showInventory = false;
            selectedIndex = 0;
        }

        if (UI.Button(invRect.Inset(5), "Inventory", invSettings, textSettings).Clicked)
        {
            showInventory = true;
            selectedIndex = 0;
        }
    }

    private static void DrawFishDetails(Rect rect, Item_Instance fish)
    {
        var gradientTexture = Assets.GetAsset<Texture>("ui/display_gradient.png");
        var backgroundSlice = new UI.NineSlice()
        {
            slice = new Vector4(64, 64, 64, 64),
            sliceScale = 0.2f
        };

        // Get rarity color and name
        var rarityColor = FishItemManager.Instance.GetFishColor(fish.Definition.Id);
        var rarity = Enum.GetName(FishItemManager.GetFishRarity(fish.Definition.Id));
        var level = int.Parse(fish.GetMetadata("Level"));

        // Draw rarity background for top section
        var topSection = rect.CutTop(240);
        UI.Image(topSection.Grow(6), gradientTexture, rarityColor, backgroundSlice);

        // Draw large fish icon in background
        var iconRect = topSection.Grow(-30).Offset(0, -10);
        var fishTexture = Assets.GetAsset<Texture>(fish.Definition.Icon);
        UI.Image(iconRect.FitAspect(fishTexture.Aspect), fishTexture);

        // Draw fish name at the top
        var nameSettings = UIUtils.CenteredText(true);
        nameSettings.VerticalAlignment = UI.VerticalAlignment.Top;
        UI.Text(topSection.CutTop(45), fish.Definition.Name, nameSettings);

        // Draw rarity text right below name
        var raritySettings = nameSettings;
        raritySettings.IncreaseOutline(2);
        raritySettings.Size = 24;
        raritySettings.Color = rarityColor * 1.2f;
        raritySettings.OutlineColor = Vector4.Black;
        UI.Text(topSection.CutTop(25).Offset(0, 5), rarity, raritySettings);

        // Draw level text below rarity
        var levelSettings = raritySettings;
        levelSettings.Color = Vector4.White;
        UI.Text(topSection.CutBottom(25).Offset(0, 5), $"Lv. {level}", levelSettings);

        
        // Stats section
        var infoRect = rect.InsetTop(10);

        // Create RuntimeMon to get stats
        var exp = int.Parse(fish.GetMetadata("Exp"));
        var mon = new RuntimeMon(fish.Definition.Id, fish.GetMetadata("Type"), level, exp);

        // Helper function to draw a stat
        void DrawStat(Rect rect, string name, string value, Vector4 iconColor, Texture iconTexture = null)
        {
            // Draw stat name above
            var nameSettings = UIUtils.CenteredText(false);
            nameSettings.Size = 20;
            UI.Text(rect.CutTop(25), name, nameSettings);

            var valueRect = rect.Inset(5);  // Smaller background area

            // Draw background just for value area
            UI.Image(valueRect, Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_8.png"),
                new Vector4(0.2f, 0.2f, 0.2f, 0.5f),
                new UI.NineSlice() { slice = new Vector4(24, 24, 24, 24), sliceScale = 1.0f });

            // Draw icon
            var iconRect = valueRect.CutLeft(20).CenterRect().Offset(15, 0).Grow(30);  // Made icon slightly bigger
            if (iconTexture != null)
            {
                UI.Image(iconRect.FitAspect(iconTexture.Aspect), iconTexture);
            }
            else
            {
                UI.Image(iconRect.FitAspect(1), UI.WhiteSprite, iconColor);
            }

            // Draw value
            var valueSettings = UIUtils.CenteredText(true);
            UI.Text(valueRect.Offset(0, 5), value, valueSettings);
        }

        // Draw stats in a row
        var statsRow = infoRect.CutTop(90);  // Keep same total height
        var statWidth = statsRow.Width / 3;  // Divide into 3 equal columns

        // Type stat
        var typeRect = statsRow.CutLeftUnscaled(statWidth);
        var typeTexture = Assets.GetAsset<Texture>($"ui/type_{fish.GetMetadata("Type")}.png");
        DrawStat(typeRect, "Type", fish.GetMetadata("Type"), Vector4.White, typeTexture);


        // Health stat
        var healthRect = statsRow.CutLeftUnscaled(statWidth);
        var healthTexture = Assets.GetAsset<Texture>("ui/health.png");
        DrawStat(healthRect, "Health", mon.maxHealth.ToString(), Vector4.White, healthTexture);

        // Attack stat
        var attackRect = statsRow;
        var attackTexture = Assets.GetAsset<Texture>("ui/attack.png");
        DrawStat(attackRect, "Attack", mon.attack.ToString(), Vector4.White, attackTexture);

        infoRect = infoRect.CutTop(40).Grow(-5);

        // Experience bar
        var expProgress = exp / (float)mon.baseExp;

        SliderUI.UpdateSliderUI(infoRect, expProgress, expProgress);

        var expText = $"{exp}/{mon.baseExp} XP";
        UI.Text(infoRect, expText, new UI.TextSettings()
        {
            Font = UI.Fonts.BarlowBold,
            Size = 16,
            Color = Vector4.White,
            HorizontalAlignment = UI.HorizontalAlignment.Center,
            VerticalAlignment = UI.VerticalAlignment.Center,
            Outline = true
        });

        Texture candyTexture = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_7.png");
        string candyText = MyPlayer.localPlayer.HasCandy() ? "Use Candy (+1 Lv.)" : "Buy Candy";

        //Add button to use candy
        if (UI.Button(infoRect.Offset(0,-55).CenterRect().Grow(35, 110, 35, 110), candyText, new UI.ButtonSettings() 
        { 
            Sprite = candyTexture,
            Slice = defaultSlice
        }, UIUtils.CenteredText(true)).Clicked)
        {
            if (MyPlayer.localPlayer.HasCandy())
            {
                MyPlayer.localPlayer.UseCandy(showInventory, selectedIndex);
            }
            else
            {
                UIManager.OpenUI(() => Store.DrawShop(Store.Instance.sparksShop), 2);
            }
        }
    }

    private static void DrawAllFishGrid(Rect rect, Texture backgroundImage, float fadeIn)
    {
        // Split into left (list) and right (details) sections like the team page
        var leftRect = rect.CutLeft(375).Grow(-20, 0, -20, 0).Offset(20, 0);
        var rightRect = rect;

        // Draw darker background for the list section
        UI.Image(leftRect, backgroundImage, new Vector4(0.7f, 0.7f, 0.7f, fadeIn), defaultSlice);
        leftRect = leftRect.Grow(-5f, -20f, -5f, -5f);

        // Draw main panel background
        UI.Image(rightRect, backgroundImage, new Vector4(1, 1, 1, fadeIn), defaultSlice);
        rightRect = rightRect.Grow(-5f);

        static_selectedFishId = DrawAllFishList(leftRect);

        if (!string.IsNullOrEmpty(static_selectedFishId))
        {
            var fishItem = FishItemManager.Instance.allItems[static_selectedFishId];
            DrawSimpleFishDetails(rightRect, fishItem);
        }
        else
        {
            var messageSettings = new UI.TextSettings()
            {
                Font = UI.Fonts.BarlowBold,
                Size = 30,
                Color = new Vector4(0.7f, 0.7f, 0.7f, 1),
                HorizontalAlignment = UI.HorizontalAlignment.Center,
                VerticalAlignment = UI.VerticalAlignment.Center,
                Outline = true,
                OutlineThickness = 2
            };
            UI.Text(rightRect.CenterRect(), "Select a fish to view details", messageSettings);
        }
    }

    private static string static_selectedFishId = "";

    private static string DrawAllFishList(Rect rect)
    {
        var itemHeight = 100f;
        var spacing = 15f;

        var sv = UI.PushScrollView("all_fish_list", rect, new UI.ScrollViewSettings()
        {
            Vertical = true
        });
        {
            var contentRect = sv.contentRect;

            foreach (var fishPair in FishItemManager.Instance.allItems)
            {
                var fishId = fishPair.Key;
                var fishItem = fishPair.Value;

                if (!fishItem.teamable) continue; // Skip non-teamable fish

                var itemRect = contentRect.CutTop(itemHeight);
                contentRect = contentRect.CutTop(spacing);

                using var _ = UI.PUSH_ID(fishId);

                bool isCaught = MyPlayer.localPlayer.HasCaughtFish(fishId);
                string displayName = isCaught ? fishItem.Item.Name : "?????";

                // Get rarity color for background
                var rarityColor = FishItemManager.Instance.GetFishColor(fishId);

                // Make the entire row clickable with proper nine-slice
                var buttonSettings = new UI.ButtonSettings()
                {
                    Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_10.png"),
                    ColorMultiplier = fishId == static_selectedFishId ?
                        rarityColor * new Vector4(1f, 1f, 1f, 1.0f) :
                        rarityColor * new Vector4(1f, 1f, 1f, 0.8f),
                    PressScaling = 0.98f,
                    Slice = defaultSlice
                };

                if (UI.Button(itemRect.Inset(5), "", buttonSettings, default).Clicked)
                {
                    static_selectedFishId = fishId;
                }

                // Draw fish icon
                var iconRect = itemRect.CutLeft(itemHeight).Inset(8);
                var fishTexture = Assets.GetAsset<Texture>(fishItem.Item.Icon);
                if (fishId == static_selectedFishId)
                {
                    // Bouncy jump animation
                    float bounce = MathF.Abs(MathF.Sin(Time.TimeSinceStartup * 3)) * 8;
                    iconRect = iconRect.Offset(0, bounce);
                }
                UI.Image(iconRect.FitAspect(fishTexture.Aspect), fishTexture, isCaught ? Vector4.White : new Vector4(0f, 0f, 0f, 1));

                // Draw fish name
                var textRect = itemRect.Inset(35);
                var nameSettings = new UI.TextSettings()
                {
                    Font = UI.Fonts.BarlowBold,
                    Size = 25,
                    Color = Vector4.White,
                    VerticalAlignment = UI.VerticalAlignment.Center,
                    HorizontalAlignment = UI.HorizontalAlignment.Left,
                    Outline = true,
                    OutlineThickness = 5,
                    DoAutofit = true,
                    AutofitMinSize = 5,
                    AutofitMaxSize = 120
                };

                UI.Text(textRect, displayName, nameSettings);
            }
        }
        UI.PopScrollView();
        return static_selectedFishId;
    }

    private static void DrawSimpleFishDetails(Rect rect, FishItem fish)
    {
        bool isCaught = MyPlayer.localPlayer.HasCaughtFish(fish.Item.Id);
        string displayName = isCaught ? fish.Item.Name : "?????";

        // Get rarity color and name
        var rarityColor = FishItemManager.Instance.GetFishColor(fish.Item.Id);
        var rarity = Enum.GetName(fish.Rarity);

        var gradientTexture = Assets.GetAsset<Texture>("ui/display_gradient.png");
        var backgroundSlice = new UI.NineSlice()
        {
            slice = new Vector4(64, 64, 64, 64),
            sliceScale = 0.2f
        };

        // Reserve small space at bottom for caught status and description
        var topSection = rect.CutTop(240);
        UI.Image(topSection.Grow(6), gradientTexture, rarityColor, backgroundSlice);

        // Draw large fish icon in background
        var iconRect = topSection.Grow(-30).Offset(0, -10);
        var fishTexture = Assets.GetAsset<Texture>(fish.Item.Icon);
        UI.Image(iconRect.FitAspect(fishTexture.Aspect), fishTexture, isCaught ? Vector4.White : Vector4.Black);

        // Draw fish name at the top
        var nameSettings = UIUtils.CenteredText(true);
        nameSettings.VerticalAlignment = UI.VerticalAlignment.Top;
        UI.Text(topSection.CutTop(45), displayName, nameSettings);

        // Draw rarity text right below name
        var raritySettings = nameSettings;
        raritySettings.IncreaseOutline(2);
        raritySettings.Size = 24;
        raritySettings.Color = rarityColor * 1.2f;
        raritySettings.OutlineColor = Vector4.Black;
        UI.Text(topSection.CutTop(25).Offset(0, 5), rarity, raritySettings);

        // Draw caught status at bottom
        var statusSettings = UIUtils.CenteredText(true);
        statusSettings.Size = 24;
        statusSettings.OutlineColor = Vector4.Black;
        statusSettings.OutlineThickness = 2;
        statusSettings.Color = isCaught ? new Vector4(0.2f, 1f, 0.2f, 1) : new Vector4(1f, 0.2f, 0.2f, 1);

        var statusRect = topSection.CutBottom(50);
        UI.Text(statusRect.Inset(5), isCaught ? "CAUGHT" : "NOT CAUGHT", statusSettings);

        // Draw description
        var descriptionRect = rect.Inset(5);
        var descriptionSettings = UIUtils.CenteredText(false);
        descriptionSettings.Size = 20;
        descriptionSettings.Color = Vector4.White;
        descriptionSettings.DoAutofit = true;
        descriptionSettings.AutofitMinSize = 16;
        descriptionSettings.AutofitMaxSize = 64;

        string description = FishItemManager.Instance.GetFishDescription(fish.Item.Id, !isCaught);
        UI.Text(descriptionRect.Inset(15), description, descriptionSettings);
    }
}
