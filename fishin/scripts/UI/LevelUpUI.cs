using AO;

public class LevelUpUI : Component
{
    [Serialized] public Entity spinElement;
    private static LevelUpUI instance;

    public override void Awake()
    {
        instance = this;
    }

    bool playedNoise = false;

    public static void OnLevelUp(int level)
    {
        UIManager.OpenUI(() => instance.DrawLevelUpUI(level), 2);
    }

    private bool DrawLevelUpUI(int level)
    {
        spinElement.LocalRotation += Time.DeltaTime * 45.0f;
        Entity.Position = MyPlayer.localPlayer.camera.position - new Vector2(0, 0.5f);
        Entity.Scale = new Vector2(MyPlayer.localPlayer.camera.scale, MyPlayer.localPlayer.camera.scale);

        if (!playedNoise)
        {
            SFX.Play(Assets.GetAsset<AudioAsset>("audio/level_up.wav"), new SFX.PlaySoundDesc() { Positional = false, Volume = 0.7f });
            playedNoise = true;
            return true;
        }

        Rect screen = UI.ScreenRect;
        var buttonSettings = new UI.ButtonSettings() { Color = new Vector4(0.0f, 0.0f, 0.0f, 0.0f) };
        UI.PushId("lvl_up_button");
        if (UI.Button(screen, "", buttonSettings, UI.TextSettings.Default).Clicked)
        {
            playedNoise = false;
            Entity.Position = new Vector2(10000, 10000);
            return false;
        }
        UI.PopId();
        Rect centerRect = screen.CenterRect().Grow(128).Offset(0, -32);
        Texture badge = Assets.GetAsset<Texture>($"ui/ranks-white/rank{MyPlayer.localPlayer.GetBadgeId(level)}-white.png");
        Rect badgeRect = centerRect.FitAspect(badge.Aspect);
        UI.Image(badgeRect, badge);
        var settings = UIUtils.CenteredText(true);
        settings.AutofitMaxSize = 64;
        UI.Text(badgeRect, $"{level + 1}", settings);
        Texture ribbon = Assets.GetAsset<Texture>($"ui/lvl_up/ribbon.png");
        Rect ribbonRect = centerRect.Offset(0.0f, 256.0f).Grow(128).FitAspect(ribbon.Aspect, Rect.FitAspectKind.KeepWidth);
        UI.Image(ribbonRect, ribbon);
        Rect textRect = screen.CutBottom(120.0f);
        textRect = textRect.Offset(0.0f, 150.0f);
        settings.AutofitMaxSize = 32;
        UI.Text(textRect, $"Press anywhere to continue", settings);
        return true;
    }
}

