using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AO;

class ObtainFishUI : Component
{
    [Serialized] public Entity ray1;
    [Serialized] public Entity ray2;

    private static ObtainFishUI instance;

    private Item_Definition _fish;
    private int price;
    private bool teamFull;
    private Action _keep, _add;
    private Action<Vector2> _sell;

    private Texture _fishTexture;
    private Texture _typeTexture;

    Vector4 Blue = new Vector4(49f / 255f, 255f / 255f, 255f / 255f, 1);      // rgb(49, 255, 255)
    Vector4 DarkBlue = new Vector4(10f / 255f, 22f / 255f, 65f / 255f, 1);   // rgb(10, 22, 65)
    Vector4 Green = new Vector4(0f / 255f, 255f / 255f, 76f / 255f, 1);     // rgb(0, 255, 76)
    Vector4 DarkGreen = new Vector4(0f / 255f, 36f / 255f, 29f / 255f, 1);    // rgb(0, 36, 29)

    public string level;

    public override void Awake()
    {
        instance = this;
    }

    public bool DrawUI()
    {
        if (_fish == null)
        {
            Entity.Position = new Vector2(10000, 10000);
            return false;
        }

        ray1.LocalRotation += Time.DeltaTime * 45.0f;
        ray2.LocalRotation -= Time.DeltaTime * 5.0f;
        Entity.Position = MyPlayer.localPlayer.camera.position - new Vector2(0, 0.5f);

        Rect screen = UI.ScreenRect;
        Rect fishRect = screen.CenterRect().Grow(128).Offset(0, -32).FitAspect(_fishTexture.Aspect);
        UI.Image(fishRect, _fishTexture);
        if (_typeTexture != null)
        {
            Rect typeRect = fishRect.TopRightRect().Grow(0, 0, 64, 64).FitAspect(_typeTexture.Aspect);
            UI.Image(typeRect, _typeTexture);
        }
        Rect buttonArea = screen.CutBottom(120.0f);
        buttonArea = buttonArea.Offset(0.0f, 250.0f);
        Rect textRect = buttonArea.Offset(0.0f, 390.0f);
        var settings = UIUtils.CenteredText(true);
        if (!FishItemManager.IsFishTeamable(_fish.Id))
        {
            UI.Text(textRect, $"You caught a {_fish.Name}... Unlucky!", settings);
        }
        else
        {
            UI.Text(textRect, $"You caught a Lvl.{level} {_fish.Name}!", settings);
        }
        textRect = textRect.Offset(0.0f, -50.0f);
        settings.AutofitMaxSize = 40;
        settings.Color = FishItemManager.Instance.GetFishColor(_fish.Id);
        var rarity = FishItemManager.GetFishRarity(_fish.Id);
        //textRect = screen.TopRect().Grow(0, 0, 64, 0).Offset(0, -64);
        if(rarity == ItemRarity.Mythic)
        {
            UI.Text(textRect, $"Mythical Boss", settings);
        }
        else
        {
            UI.Text(textRect, $"{Enum.GetName(rarity)}", settings);
        }
        buttonArea = buttonArea.CenterRect().Grow(50f, 300, 50f, 300);
        var buttonRects = buttonArea.VerticalSlice(2, 5.0f);
        UI.ButtonSettings buttonSettings = new()
        {
            Sprite = Assets.GetAsset<Texture>("ui/btn.png"),
            Slice = new UI.NineSlice() { slice = new Vector4(35, 35, 35, 35), sliceScale = 1.0f },
        };
        settings.Offset = new Vector2(0, 5);
        settings.Color = Vector4.White;
        settings.AutofitMaxSize = 48;
        settings.OutlineColor = DarkBlue;
        settings.DropShadowColor = DarkBlue;
        buttonSettings.BackgroundColorMultiplier = Blue;
        if (!teamFull)
        {
            var resultAdd = UI.Button(buttonRects[0], "Add To Team", buttonSettings, settings);
            if (resultAdd.Clicked)
            {
                SFX.Play(Assets.GetAsset<AudioAsset>("audio/bag.wav"), new SFX.PlaySoundDesc() { Positional = false, Volume = 0.7f });
                _add?.Invoke();
                _fish = null;
            }
        }
        else
        {
            var _keepAction = _keep;
            if (Inventory.CalculateRoomInInventoryForItem(_fish, MyPlayer.localPlayer.DefaultInventory) == 0)
            {
                _keepAction = () => { Notifications.Show("Inventory is full! Go sell some fish first."); };
                buttonSettings.BackgroundColorMultiplier = new Vector4(0.1f, 0.1f, 0.1f, 1);
            }
            else
            {
                _keepAction += () => { _fish = null; };
            }
            var resultKeep = UI.Button(buttonRects[0], "Keep", buttonSettings, settings);
            if (resultKeep.Clicked)
            {
                SFX.Play(Assets.GetAsset<AudioAsset>("audio/bag.wav"), new SFX.PlaySoundDesc() { Positional = false, Volume = 0.7f });
                _keepAction?.Invoke();
            }
        }
        settings.OutlineColor = DarkGreen;
        settings.DropShadowColor = DarkGreen;
        buttonSettings.BackgroundColorMultiplier = Green;
        settings.Offset = new Vector2(-20, 5);
        var resultSell = UI.Button(buttonRects[1], $"Sell for {price} ", buttonSettings, settings);
        var coinTexture = Assets.GetAsset<Texture>("ui/coin.png");
        UI.Image(resultSell.TextRect.RightRect().GrowRight(50).FitAspect(coinTexture.Aspect).Offset(-25,0), coinTexture);
        if (resultSell.Pressed)
        {
            _sell?.Invoke(buttonRects[1].Center);
        }
        return true;
    }

    public static void ShowFish(Item_Definition fishObtained, string type, string level, Action onKeep, Action onSell, Action onAddToTeam)
    {
        instance._fishTexture = Assets.GetAsset<Texture>(fishObtained.Icon);
        if (!type.IsNullOrEmpty())
        {
            instance._typeTexture = Assets.GetAsset<Texture>($"ui/type_{type}.png");
        }
        else
        {
            instance._typeTexture = null;
        }
        instance._fish = fishObtained;
        instance.level = level;
        instance.price = FishItemManager.Instance.GetFishPrice(fishObtained.Id, int.Parse(level), MyPlayer.localPlayer.playerBuffCoinActive ? 1 : 0);
        instance._keep = onKeep;

        void SellAction(Vector2 pos)
        {
            MyPlayer.SpawnCoinParticles(pos, instance.price);
            AudioAsset audio = instance.price > 45 ? Assets.GetAsset<AudioAsset>("audio/sell_big.wav") : Assets.GetAsset<AudioAsset>("audio/sell_small.wav");
            SFX.Play(audio, new SFX.PlaySoundDesc() { Positional = false, Volume = 0.7f });
            onSell?.Invoke();
            instance._fish = null;
        }

        if (FishItemManager.GetFishRarity(fishObtained.Id) == ItemRarity.Legendary || FishItemManager.GetFishRarity(fishObtained.Id) == ItemRarity.Mythic)
        {
            instance._sell = (pos) =>
            {
                PopupUI.Show($"!{Enum.GetName(FishItemManager.GetFishRarity(fishObtained.Id))} Fish!", "Are you sure you want to sell this fish?", () =>
                {
                    SellAction(pos);
                }, () => { }, "Sell Fish", "Keep Fish");
            };
        }
        else
        {
            instance._sell = SellAction;
        }

        instance._add = onAddToTeam;
        instance.teamFull = MyPlayer.localPlayer.IsTeamFull() || !FishItemManager.IsFishTeamable(fishObtained.Id);

        instance.ray1.GetComponent<Sprite_Renderer>().Tint = FishItemManager.Instance.GetFishColor(fishObtained.Id);
        instance.ray2.GetComponent<Sprite_Renderer>().Tint = FishItemManager.Instance.GetFishColor(fishObtained.Id);

        instance.GetComponent<Sprite_Renderer>().Tint = FishItemManager.Instance.GetFishColor(fishObtained.Id);
        UIManager.OpenUI(instance.DrawUI, 1);
    }
}
