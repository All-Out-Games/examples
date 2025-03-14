using AO;

public static class BossBattleAnim
{
    public static string BossName;
    public static string BossTexture;
    public const float FlashAnimLength = 0.2f;
    public const float AnimLength = 8.0f;

    public const float BarSize = 300;

    private static float timer;

    public static Vector4 bgColor;
    public static Vector4 textColor;



    public static void ShowBattleIntro(string bossName, string bossTexture, string type)
    {
        BossName = bossName;
        BossTexture = bossTexture;
        switch (type)
        {
            case "grass":
                bgColor = new Vector4(0.1f, 0.5f, 0.1f, 1.0f);
                textColor = new Vector4(0, 0.1f, 0, 1);
                break;
            case "water":
                bgColor = new Vector4(0.1f, 0.1f, 0.5f, 1.0f);
                textColor = new AO.Vector4(0, 0, 0.1f, 1);
                break;
            case "fire":
                bgColor = new Vector4(0.5f, 0.1f, 0.1f, 1.0f);
                textColor = new AO.Vector4(0.1f, 0, 0, 1);
                break;
        }
        timer = 0;
        MyPlayer.localPlayer.camera.Shake(3.5f);
        UIManager.OpenUI(ShowUI, 100);
    }

    static void DrawBlackBars(float amount)
    {
        var spacing = BarSize + 100;
        var topRect = UI.ScreenRect.CutTop(BarSize).Offset(0, spacing * (1 - amount)).Grow(150, 100, 0, 100);
        var bottomRect = UI.ScreenRect.CutBottom(BarSize).Offset(0, -spacing * (1 - amount)).Grow(0, 100, 100, 150);
        UI.Image(topRect, UI.WhiteSprite, new Vector4(0, 0, 0, 1), default, 4.5f);
        UI.Image(bottomRect, UI.WhiteSprite, new Vector4(0, 0, 0, 1), default, 4.5f);
    }

    public static bool ShowUI()
    {
        if (Network.IsServer)
            return false;

        timer += Time.DeltaTime;

        if (timer > AnimLength)
        {
            timer = 0;
            return false;
        }

        float progress = timer / AnimLength;
        float blackBarSlide = Ease.InCubic(Ease.T(progress, 0.2f));
        float nameSlide = Ease.InOutCirc(Ease.T(progress - 0.25f, 0.2f));
        float imageSlide = Ease.OutExpo(Ease.T(progress - 0.3f, 0.2f));
        float blackFade = Ease.OutExpo(Ease.T(progress - 0.4f, 0.2f));
        float bounceScale = Ease.OutBounce(Ease.T(progress - 0.425f, 0.2f)) * 0.65f;
        float slowSlide = Ease.Linear(Ease.T(progress, 1.0f));

        if (progress < 0.2f)
        {
            DrawBlackBars(blackBarSlide - progress * 0.1f);
        }

        // Fade from white to black after flash animation
        float fadeIn = Ease.InQuart(Ease.T(progress, 0.15f));

        float fadeOut = Ease.InQuart(Ease.T(progress - 0.9f, 0.1f));
        float backgroundAlpha = fadeIn - fadeOut;
        using var _1 = UI.PUSH_COLOR_MULTIPLIER(new Vector4(1, 1, 1, backgroundAlpha));

        // Draw white background fading to black
        UI.Image(UI.ScreenRect, UI.WhiteSprite, bgColor);

        // Center container for boss intro
        var centerRect = UI.ScreenRect.CenterRect().Grow(500, 850, 500, 0);

        var imageRect = centerRect.CutRight(650).Offset(800 * (1 - imageSlide), 0).Offset(50 * -slowSlide, 0);

        // Draw boss image
        if (imageSlide > 0)
        {
            var rayTexture = Assets.GetAsset<Texture>("ui/rays/ray_burst_2.png");
            var flashRect = imageRect.Grow(40).FitAspect(rayTexture.Aspect);
            UI.Image(flashRect, rayTexture, new Vector4(1, 1, 1, 1 - blackFade), new UI.NineSlice(), progress * 360 * 4);

            var centerTexture = Assets.GetAsset<Texture>("ui/rays/centre_aura.png");
            UI.Image(imageRect.Grow(-25).FitAspect(centerTexture.Aspect), centerTexture, new Vector4(1, 1, 1, 1 - blackFade));

            DrawBlackBars(blackBarSlide - progress * 0.1f);

            var bossTexture = Assets.GetAsset<Texture>(BossTexture);
            UI.Image(imageRect.FitAspect(bossTexture.Aspect).Scale(0.45f + bounceScale), bossTexture, new Vector4(blackFade, blackFade, blackFade, 1));
        }
        else
        {
            DrawBlackBars(blackBarSlide - progress * 0.1f);
        }

        // Draw boss name with slide animation
        if (nameSlide > 0)
        {
            var nameRect = centerRect.CutLeft(200).Grow(200).Offset(-300, 0);
            for (int i = 0; i < 10; i++)
            {
                var textRect = nameRect.Offset((-1500 + (i * 50)) * (1 - nameSlide), 0).Offset(50 *  slowSlide, 0);
                var color = new Vector4(textColor.X * blackFade, textColor.Y * blackFade, textColor.Z * blackFade, 1);
                UI.Text(textRect, BossName, new UI.TextSettings()
                {
                    Font = UI.Fonts.BarlowBold,
                    Size = 180,
                    Color = new Vector4(blackFade, blackFade, blackFade, (i / 10f)),
                    HorizontalAlignment = UI.HorizontalAlignment.Center,
                    VerticalAlignment = UI.VerticalAlignment.Center,
                    Outline = true,
                    OutlineColor = color,
                    OutlineThickness = 3,
                    DropShadow = true,
                    DropShadowColor = color,
                    DropShadowOffset = new AO.Vector2(0, 4),
                    SpacingMultiplier = 1.2f,
                });
            }
        }

        return true;
    }
}