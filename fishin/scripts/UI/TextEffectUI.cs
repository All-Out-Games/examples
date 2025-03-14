using AO;

public class TextEffect
{
    public string Text;
    public Vector2 Position;
    public Vector4 Color;
    public float T;
    public UI.TextSettings TextSettings;
    public bool SpaceText;
    public bool DoingFading = false;
    public Vector2 LastPosition;
}

public class TextEffectUI : Component
{
    static TextEffectUI instance;
    public List<TextEffect> WorldTextEffects = new();
    public List<TextEffect> ScreenTextEffects = new();

    public override void Awake()
    {
        instance = this;
    }

    public static void SpawnWorldText(FontAsset font, Vector2 worldPosition, Vector4 color, string text, float size = 0.3f, float slant = 0.0f, bool spaceText = false)
    {
        float randX = Random.Shared.NextFloat(-1f, 1f);
        float randY = Random.Shared.NextFloat(1f, 1.5f);
        var searchResult = new TextEffect
        {
            Text = text,
            Position = worldPosition + new Vector2(randX, randY),
            Color = color,
            T = 0,
            TextSettings = GetTextSettingsDamageNumbers(font, Game.IsMobile ? 0.5f : size, color, slant),
            SpaceText = spaceText
        };
        instance.WorldTextEffects.Add(searchResult);
    }

    public static void SpawnScreenText(FontAsset font, Vector2 screenPosition, Vector4 color, string text, float size = 3f, float slant = 0.0f, bool spaceText = false)
    {
        float randX = Random.Shared.NextFloat(-1f, 1f);
        float randY = Random.Shared.NextFloat(1f, 1.5f);
        var searchResult = new TextEffect
        {
            Text = text,
            Position = screenPosition + new Vector2(randX, randY),
            Color = color,
            T = 0,
            TextSettings = GetTextSettingsDamageNumbers(font, size, color, slant, -10f),
            SpaceText = spaceText
        };
        instance.ScreenTextEffects.Add(searchResult);
    }

    private static UI.TextSettings GetTextSettingsDamageNumbers(FontAsset font, float size, Vector4 color, float slant, float shadowOffset = -3f)
    {
        var ts = new UI.TextSettings()
        {
            Font = font,
            Size = size,
            Color = color,
            DropShadow = true,
            DropShadowColor = new Vector4(0f, 0f, 0f, 1f),
            DropShadowOffset = new Vector2(0f, shadowOffset),
            HorizontalAlignment = UI.HorizontalAlignment.Center,
            VerticalAlignment = UI.VerticalAlignment.Center,
            WordWrap = false,
            WordWrapOffset = 0,
            Outline = true,
            OutlineThickness = 3,
            Slant = slant,
        };
        return ts;
    }

    public override void Update()
    {
        UpdateWorldTextEffects();
        UpdateScreenTextEffects();
    }

    public void UpdateWorldTextEffects()
    {
        using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
        using var _2 = UI.PUSH_LAYER(5);
                List<TextEffect> numbers = WorldTextEffects;
        for (int i = numbers.Count - 1; i >= 0; i -= 1)
        {
            var result = numbers[i];
            float speed = 0.5f;
            var ts = result.TextSettings;

            result.T += Time.DeltaTime * speed;
            if (result.T >= 1 && result.DoingFading)
            {
                numbers.UnorderedRemoveAt(i);
                continue;
            }
            if (result.T >= 1 && !result.DoingFading)
            {
                result.T = 0.0f;
                result.DoingFading = true;
            }

            if (!result.DoingFading)
            {
                var pos = result.Position;
                pos.Y += AOMath.Lerp(0, 0.5f, Ease.OutQuart(result.T));
                var color01 = Ease.FadeInAndOut(0.1f, 1f, result.T);
                ts.Color = Vector4.Lerp(ts.Color, result.Color, color01);
                result.LastPosition = pos;
            }
            else
            {
                ts.SpacingMultiplier = 1f;
                var colorAlpha = Vector4.Zero;
                ts.Color = Vector4.Lerp(ts.Color, colorAlpha, result.T);
            }

            var rect = new Rect(result.LastPosition, result.LastPosition);
            UI.Text(rect, result.Text, ts);
        }
    }

    public void UpdateScreenTextEffects()
    {
        using var _1 = UI.PUSH_CONTEXT(UI.Context.SCREEN);
        using var _2 = UI.PUSH_LAYER(5);
        using var _3 = UI.PUSH_SCALE_FACTOR(10.0f);
        List<TextEffect> numbers = ScreenTextEffects;
        for (int i = numbers.Count - 1; i >= 0; i -= 1)
        {
            var result = numbers[i];
            float speed = 0.5f;
            var ts = result.TextSettings;

            result.T += Time.DeltaTime * speed;
            if (result.T >= 1 && result.DoingFading)
            {
                numbers.UnorderedRemoveAt(i);
                continue;
            }
            if (result.T >= 1 && !result.DoingFading)
            {
                result.T = 0.0f;
                result.DoingFading = true;
            }

            if (!result.DoingFading)
            {
                var pos = result.Position;
                pos.Y -= AOMath.Lerp(0, 5f, Ease.OutQuart(result.T));
                var color01 = Ease.FadeInAndOut(0.1f, 1f, result.T);
                ts.Color = Vector4.Lerp(ts.Color, result.Color, color01);
                result.LastPosition = pos;
            }
            else
            {
                ts.SpacingMultiplier = 1f;
                var colorAlpha = Vector4.Zero;
                ts.Color = Vector4.Lerp(ts.Color, colorAlpha, result.T);
            }

            var rect = new Rect(result.LastPosition, result.LastPosition);
            UI.Text(rect, result.Text, ts);
        }
    }
}
