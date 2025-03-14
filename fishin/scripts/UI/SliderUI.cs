using AO;

public class SliderUI : Component
{
    [Serialized] public Texture background;
    [Serialized] public Texture filler;
    [Serialized] public Texture filler_white;
    [Serialized] public Texture border;
    [Serialized] public Texture cursor;
    private static SliderUI instance;

    AudioAsset audio_xp_start = Assets.GetAsset<AudioAsset>("audio/xp_gain_start.wav");
    AudioAsset audio_xp_end = Assets.GetAsset<AudioAsset>("audio/xp_gain_end.wav");

    public enum SliderType {Slanted, Red, Green}

    ulong audio_xp_gain_id = 0;

    public override void Awake()
    {
        instance = this;
    }

    float currentPercentage = 0;
    public static int lastKnownExp = 0;
    float incrementPercentage = 0;

    public override void Update()
    {
        if (!Network.IsClient) return;
        bool animateExp = lastKnownExp < MyPlayer.localPlayer.experience;
        float percentage = (float)MyPlayer.localPlayer.experience / MyPlayer.localPlayer.maxExp;
        if (animateExp)
        {
            if (audio_xp_gain_id == 0)
            {
                audio_xp_gain_id = SFX.Play(audio_xp_start, new SFX.PlaySoundDesc() { Positional = false, Volume = 0.6f, Speed = 1.0f, Loop = false });
                currentPercentage = (float)lastKnownExp / MyPlayer.localPlayer.maxExp;
                incrementPercentage = percentage - currentPercentage;
            }
            currentPercentage += incrementPercentage * Time.DeltaTime;
            if (currentPercentage >= percentage)
            {
                SFX.Play(audio_xp_end, new SFX.PlaySoundDesc() { Positional = false, Volume = 0.75f, Speed = 1.0f, Loop = false });
                Rect levelScreenRect = GetLevelScreenRect();
                Vector2 textPos = new Vector2(GetX(levelScreenRect, currentPercentage), levelScreenRect.Min.Y - 1);
                TextEffectUI.SpawnScreenText(UI.Fonts.BarlowBold, textPos, Vector4.White, $"+{MyPlayer.localPlayer.experience - lastKnownExp} XP",3.0f,0.0f,true);
                lastKnownExp = MyPlayer.localPlayer.experience;
                currentPercentage = percentage;
                audio_xp_gain_id = 0;
            }
            string expText = $"{lastKnownExp}/{MyPlayer.localPlayer.maxExp}";
            DrawLevel(currentPercentage, percentage, expText);
            DrawLevelBadge(MyPlayer.localPlayer.Level);
        }
        else
        {
            lastKnownExp = MyPlayer.localPlayer.experience;
            string expText = $"{MyPlayer.localPlayer.experience}/{MyPlayer.localPlayer.maxExp}";
            DrawLevel(percentage, percentage, expText);
            DrawLevelBadge(MyPlayer.localPlayer.Level);
        }
    }

    Rect GetLevelScreenRect()
    {
        float sizeX = Game.IsMobile ? 200.0f : 300.0f;
        float sizeY = Game.IsMobile ? 45.0f : 38.0f;
        Rect SliderArea = UI.SafeRect.TopCenterRect();
        SliderArea = SliderArea.Grow(0.0f, sizeX, sizeY, sizeX).Offset(0.0f, -20.0f);

        return SliderArea;
    }

    void DrawLevel(float percentage, float realPercentage, string text)
    {
        Rect SliderArea = GetLevelScreenRect();
        SliderArea = SliderArea.Grow(0.0f, 0, -1, 0);
        UpdateSliderUI(SliderArea, percentage, realPercentage);
        var settings = UIUtils.CenteredText(true);
        UI.Text(SliderArea, text, settings);
    }

    void DrawLevelBadge(int level)
    {
        Rect SliderArea = GetLevelScreenRect();
        var settings = UIUtils.CenteredText(false);
        Texture badge = Assets.GetAsset<Texture>($"ui/ranks/Rank-Icon-{MyPlayer.localPlayer.GetBadgeId(level)}.png");
        var badgeRect = SliderArea.Offset(-10.0f, 0.0f).CutLeft(60.0f).FitAspect(badge.Aspect, Rect.FitAspectKind.KeepWidth);
        UI.Image(badgeRect, badge);
        UI.Text(badgeRect, $"{level + 1}", settings);
    }

    public static void UpdateSliderUI(Rect SliderArea, float percentage, float nextPercentage)
    {
        var slice = new UI.NineSlice() { slice = new Vector4(24, 24, 24, 24), sliceScale = 1.0f };
        UI.Image(SliderArea, instance.background, slice);
        {
            SliderArea = SliderArea.Grow(-2.5f);
            float minX = SliderArea.Min.X;
            float maxX = SliderArea.Min.X + SliderArea.Width * nextPercentage;
            Rect fillArea = new Rect(new Vector2(minX, SliderArea.Min.Y), new Vector2(maxX, SliderArea.Max.Y));
            UI.Image(fillArea, instance.filler_white, slice);
            maxX = SliderArea.Min.X + SliderArea.Width * percentage;
            fillArea = new Rect(new Vector2(minX, SliderArea.Min.Y), new Vector2(maxX, SliderArea.Max.Y));
            UI.Image(fillArea, instance.filler, slice);
        }
    }

    public static void BasicSliderUI(Rect SliderArea, float percentage)
    {
        percentage = Math.Clamp(percentage, 0.0f, 1.0f);
        var slice = new UI.NineSlice() { slice = new Vector4(24, 24, 24, 24), sliceScale = 1.0f };
        UI.Image(SliderArea, instance.background, slice);
        {
            SliderArea = SliderArea.Grow(-2.5f);
            float minX = SliderArea.Min.X;
            float maxX = SliderArea.Min.X + SliderArea.Width * percentage;
            Rect fillArea = new Rect(new Vector2(minX, SliderArea.Min.Y), new Vector2(maxX, SliderArea.Max.Y));
            UI.Image(fillArea, instance.filler, slice);
        }
    }

    public static float GetX(Rect SliderArea, float percentage)
    {
        return SliderArea.Min.X + SliderArea.Width * percentage;
    }

    public static void WorldSliderUI(float percentage, Vector2 world_pos, SliderType type, Vector2 size, bool showText = false, string valueText = "")
    {
        percentage = Math.Clamp(percentage, 0.0f, 1.0f);

        //Draw slider
        Rect sliderArea = new Rect(
            new Vector2(world_pos.X - size.X / 2f, world_pos.Y - size.Y / 2f),
            new Vector2(world_pos.X + size.X / 2f, world_pos.Y + size.Y / 2f)
        );

        // Draw background
        var slice = new UI.NineSlice() { slice = new Vector4(20,20,20,20), sliceScale = 2.0f};
        if(type == SliderType.Slanted)
        {
            slice.slice.Z += 35;
            slice.sliceScale = 3.0f;
        }
        Texture backgroundTexture = type switch
            {
                SliderType.Slanted => Assets.GetAsset<Texture>($"ui/exp_bg_slanted.png"),
                _ => instance.background
            };
        UI.Image(sliderArea, backgroundTexture, slice);

        // Draw the fill bar
        {
            sliderArea = sliderArea.Grow(-0.01f);
            float minX = sliderArea.Min.X;
            float maxX = sliderArea.Min.X + sliderArea.Width * percentage;
            Rect fillArea = new Rect(new Vector2(sliderArea.Min.X, sliderArea.Min.Y), new Vector2(maxX, sliderArea.Max.Y));
            Texture fillTexture = type switch
            {
                SliderType.Slanted => Assets.GetAsset<Texture>($"ui/slider_yellow_slanted.png"),
                SliderType.Red => Assets.GetAsset<Texture>($"ui/slider_red.png"),
                SliderType.Green => Assets.GetAsset<Texture>($"ui/slider_green.png"),
                _ => instance.filler_white
            };
            UI.Image(fillArea, fillTexture, slice);
        }

        if(showText)
        {
            sliderArea = sliderArea.Grow(-0.05f);
            var settings = UIUtils.WorldCenteredText(true);
            UI.Text(sliderArea, valueText, settings);
        }
    }
}