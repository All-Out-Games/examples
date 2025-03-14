using AO;

public static class UIUtils
{
    public static UI.TextSettings CenteredText(bool dropShadow = false)
    {
        UI.TextSettings textSettings = UI.TextSettings.Default;
        textSettings.HorizontalAlignment = UI.HorizontalAlignment.Center;
        textSettings.VerticalAlignment = UI.VerticalAlignment.Center;
        textSettings.DoAutofit = true;
        textSettings.AutofitMaxSize = 40;
        if (dropShadow)
        {
            textSettings.DropShadow = true;
            textSettings.DropShadowColor = new Vector4(0.0f, 0.0f, 0.0f, 1.0f);
            textSettings.DropShadowOffset = new Vector2(0.0f, -5f);
        }
        return textSettings;
    }

    public static UI.TextSettings WorldCenteredText(bool dropShadow = false)
    {
        UI.TextSettings textSettings = CenteredText(dropShadow);
        textSettings.AutofitMinSize /= 20;
        textSettings.AutofitMaxSize /= 20;
        return textSettings;
    }

    public static void IncreaseOutline(this UI.TextSettings settings, float delta)
    {
        settings.OutlineThickness += delta;
        if (settings.DropShadow)
        {
            settings.DropShadowOffset += new Vector2(0.0f, delta);
        }
    }

    public static Rect[] VerticalSlice(this Rect rect, int numSlices, float spacing)
    {
        if (numSlices <= 0) return Array.Empty<Rect>();
        
        Rect[] returnVal = new Rect[numSlices];
        float sliceSize = (rect.Width - spacing * (numSlices - 1)) / numSlices;
        
        for (int i = 0; i < numSlices - 1; i++)
        {
            returnVal[i] = rect.CutLeftUnscaled(sliceSize);
            rect.CutLeftUnscaled(spacing);
        }
        returnVal[^1] = rect;
        return returnVal;
    }

    public static Vector4 GetRarityColor(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => new Vector4(1f, 1f, 1f, 1),           // White (no tint)
            ItemRarity.Rare => new Vector4(0.3f, 0.7f, 1f, 1),        // Sky blue
            ItemRarity.Epic => new Vector4(0.8f, 0.3f, 1f, 1),        // Bright purple
            ItemRarity.Legendary => new Vector4(1f, 0.8f, 0.2f, 1),   // Golden yellow
            ItemRarity.Mythic => new Vector4(1f, 0.2f, 0.2f, 1),   // Red
            _ => new Vector4(1f, 1f, 1f, 1),
        };
    }
}
