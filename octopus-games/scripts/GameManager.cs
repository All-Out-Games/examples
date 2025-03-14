using AO;
using TinyJson;
using System.Runtime.Serialization;

public class GameManagerSystem : System<GameManagerSystem>
{
    public override void Awake()
    {
        if (!Network.IsServer)
        {
            Keybinds.OverrideKeybindDefault("Ability 1", Input.UnifiedInput.MOUSE_LEFT);
            Analytics.EnableAutomaticAnalytics("<REDACTED>", "<REDACTED>");
        }
    }
}

public partial class GameManager : Component, INetworkedComponent
{
    public static GameManager _instance;
    public static GameManager Instance
    {
        get
        {
            if (_instance == null)
            {
                foreach (var component in Scene.Components<GameManager>())
                {
                    _instance = component;
                    _instance.Awaken();
                    break;
                }
            }
            return _instance;
        }
    }

    public Dictionary<PowerupKind, PowerupDefinition> AllPowerups = new();

    public SyncVar<int> _state = new();
    public GameState State
    {
        get => (GameState)_state.Value;
        set => _state.Set((int)value, true);
    }

    public GameState StateLastFrame = (GameState)(-1);

    public SyncVar<bool> FrontmanEnabled = new();

    public bool ServerIsQueueingEnabled;

    public float TimeStateStarted;
    public float PrevTimeInState;

    public SyncVar<bool> IsPrivateServer = new();

    public const float MainThemeMaxVolume = 0.5f;
    public float MainThemeCurrentVolume = 0f;
    public float MainThemeTargetVolume = 0f;

    [NetSync] public float MinigameTimer;
    [NetSync] public bool MinigameTimerEnabled;

    [Serialized] public Texture[] PlacementSprites;

    public SyncVar<Entity> CurrentMinigameSyncVar = new();
    public MinigameInstance CurrentMinigame;

    [Serialized] public Spine_Animator FountainSpine;

    public SyncVar<bool> TournamentIsRunning = new();

    public SyncVar<float> ServerTimeCurrentMinigameStarted = new();

    [NetSync] public float PrepPhaseTimeRemaining;

    public List<MyPlayer> PlayersInCurrentTournament = new();
    public List<MyPlayer> PlayersInCurrentMinigame = new();

    public SyncVar<int> CurrentRoundIndex = new();

    [Serialized] public Sprite_Renderer PodiumSprite;

    public float PowerupSpawnTimerForThisRound;
    public List<Vector2> PowerupSpawnsForThisMinigame = new();

    public Entity PodiumFirstPlace;
    public List<Entity> PodiumLosers = new();

    public string WinnerName;

    public ulong MinigameThemeSFX;
    public ulong PodiumSFX;
    public ulong MainThemeSFX;
    public SFX.PlaySoundDesc MainThemeSFXDesc;

    public int GoldRewardForThisRound
    {
        get
        {
            return (CurrentRoundIndex+1) * 100;
        }
    }

    public int XPRewardForThisRound
    {
        get
        {
            return (CurrentRoundIndex+1) * 10;
        }
    }

    public float CurrentMoneyHeight;

    public float FullscreenFaderCurrent;
    public float FullscreenFaderTarget;

    public ulong GlobalRng;

    public CameraControl CutsceneCamera;
    public Vector2 CameraPanStart;
    public Vector2 CameraPanEnd;

    public List<MinigameKind> MinigameQueue = new();

    public MinigameKind NextMinigameOverride = MinigameKind.None;
    public MinigameKind PrevMinigame = MinigameKind.None;

    public bool FastMode;

    public List<TextPopup> TextPopups = new();

    public const float LEADERBOARD_ENTRY_HEIGHT = 40;

    public bool ServerCanReceivePlayers;

    public void ServerEnsureQueueingIsEnabled(bool enabled)
    {
        Util.Assert(Network.IsServer);
        if (ServerIsQueueingEnabled == enabled)
        {
            return;
        }
        ServerIsQueueingEnabled = enabled;
        if (enabled)
        {
            // Matchmaking priority is for the root "join from the website" queue. The other queueing is for re-queueing after losing. 
            Game.SetMatchmakingPriority(0);
            Network.QueueSetServerAvailable("main", true);
        }
        else
        {
            Game.SetMatchmakingPriority(1);
            Network.QueueSetServerAvailable("main", false);
        }
    }

    public bool CheckMinigame(MinigameKind kind)
    {
        if (CurrentMinigame.Alive() == false) return false;
        if (CurrentMinigame.Kind != kind) return false;
        return true;
    }

    public class KillFeedEntry
    {
        public string PlayerName;
        public string Message;
        public float TimeCreated;
        public float Alpha = 1.0f;

        public const float LIFETIME = 5.0f; // How long each entry stays visible
        public const float FADE_TIME = 0.5f; // How long it takes to fade out
        public const float SLIDE_IN_TIME = 0.3f; // How long it takes to slide in

        public KillFeedEntry(string playerName, string message)
        {
            PlayerName = playerName;
            Message = message;
            TimeCreated = Time.TimeSinceStartup;
        }
    }

    public List<KillFeedEntry> KillFeedEntries = new();
    public const int MAX_KILL_FEED_ENTRIES = 5;
    public const float KILL_FEED_ENTRY_HEIGHT = 30f;

    public void KillFeedReport(MyPlayer player, string message)
    {
        if (!Network.IsServer) return;
        CallClient_AddKillFeedEntry(player.Name, message);
    }

    [ClientRpc]
    public void AddKillFeedEntry(string playerName, string message)
    {
        KillFeedEntries.Insert(0, new KillFeedEntry(playerName, message));
        if (KillFeedEntries.Count > MAX_KILL_FEED_ENTRIES)
        {
            KillFeedEntries.RemoveAt(KillFeedEntries.Count - 1);
        }
    }

    public static void WriteListOfNetworkedComponents<T>(AO.StreamWriter writer, List<T> list) where T : Component
    {
        var countPtr = writer.Write(0);
        var actualCount = 0;
        foreach (var comp in list)
        {
            if (comp.Alive())
            {
                actualCount += 1;
                writer.WriteNetworkedComponent(comp);
            }
        }
        writer.Update(countPtr, actualCount);
    }

    public static List<T> ReadListOfNetworkedComponents<T>(AO.StreamReader reader) where T : Component
    {
        var results = new List<T>();
        var count = reader.Read<int>();
        for (int i = 0; i < count; i++)
        {
            var comp = reader.ReadNetworkedComponent<T>();
            results.Add(comp);
        }
        return results;
    }

    public static byte[] WriteListOfNetworkedComponentsToBytes<T>(List<T> list) where T : Component
    {
        var writer = new AO.StreamWriter();
        WriteListOfNetworkedComponents(writer, list);
        return writer.byteStream.ToArray();
    }

    public static List<T> ReadListOfNetworkedComponentsFromBytes<T>(byte[] bytes) where T : Component
    {
        var reader = new AO.StreamReader(bytes);
        return ReadListOfNetworkedComponents<T>(reader);
    }

    public void NetworkSerialize(AO.StreamWriter writer)
    {
        WriteListOfNetworkedComponents(writer, PlayersInCurrentTournament);
        WriteListOfNetworkedComponents(writer, PlayersInCurrentMinigame);
    }

    public void NetworkDeserialize(AO.StreamReader reader)
    {
        PlayersInCurrentTournament = ReadListOfNetworkedComponents<MyPlayer>(reader);
        PlayersInCurrentMinigame   = ReadListOfNetworkedComponents<MyPlayer>(reader);
    }

    public override void Awake()
    {
        if (Network.IsServer)
        {
            IsPrivateServer.Set(Network.ServerPrivateInstanceHostId().Has());
            ServerEnsureQueueingIsEnabled(true);
        }

        foreach (var minigame in Scene.Components<MinigameInstance>())
        {
            foreach (var spawn in minigame.PowerupSpawnParent.Children)
            {
                spawn.GetComponent<Sprite_Renderer>().LocalEnabled = false;
            }

            foreach (var spawn in minigame.PlayerSpawnsParent.Children)
            {
                spawn.GetComponent<Sprite_Renderer>().LocalEnabled = false;
            }
        }

        MainThemeSFXDesc = new SFX.PlaySoundDesc(){Volume = 0.0f, Loop=true};
        MainThemeSFX = SFX.Play(Assets.GetAsset<AudioAsset>("sfx/main_theme_no_voice_loop.wav"), MainThemeSFXDesc);

        {
            int index = 0;
            foreach (var spawn in PodiumSprite.Entity.Children)
            {
                if (index == 0)
                {
                    PodiumFirstPlace = spawn;
                }
                else
                {
                    PodiumLosers.Add(spawn);
                }
                spawn.GetComponent<Sprite_Renderer>().LocalEnabled = false;
                index += 1;
            }
            PodiumSprite.LocalEnabled = false;
        }

        FountainSpine.Awaken();
        FountainSpine.SpineInstance.SetAnimation("idle", true);

        if (!Network.IsServer)
        {
            CutsceneCamera = CameraControl.Create(-1);
        }

        UI.SetLeaderboardOpen(false);
        UI.SetChatOpen(false);
        Game.SetVoiceEnabled(true);

        GlobalRng = RNG.Seed((ulong)(new Random().Next()));

        Chat.RegisterChatCommandHandler(RunChatCommand);

        CurrentMinigameSyncVar.OnSync += (old, value) =>
        {
            if (value == null)
            {
                CurrentMinigame = null;
            }
            else
            {
                CurrentMinigame = value.GetComponent<MinigameInstance>();
            }
        };

        _state.OnSync += (old, value) =>
        {
            TimeStateStarted = Time.TimeSinceStartup;
            PrevTimeInState = 0f;
        };

        PowerupDefinition CreatePowerup(string name, PowerupKind kind, MinigameKind minigame, Type abilityType, Texture icon, string description)
        {
            var result = new PowerupDefinition()
            {
                Name = name,
                Kind = kind,
                Minigame = minigame,
                AbilityType = abilityType,
                Icon = icon,
                Description = description,
            };
            AllPowerups.Add(kind, result);
            return result;
        }

        CreatePowerup("Shield",             PowerupKind.RLGL_Shield,            MinigameKind.RedLightGreenLight, typeof(RLGL_ShieldAbility),            Assets.GetAsset<Texture>("AbilityIcons/shield_icon.png"),              "Blocks one shot");
        CreatePowerup("Lightning Dash",     PowerupKind.RLGL_LightningDash,     MinigameKind.RedLightGreenLight, typeof(RLGL_LightningDashAbility),     Assets.GetAsset<Texture>("AbilityIcons/lightning_dash_icon.png"),      "Ultra fast dash");
        CreatePowerup("Pig Rider",          PowerupKind.RLGL_PigRider,          MinigameKind.RedLightGreenLight, typeof(RLGL_PigRiderAbility),          Assets.GetAsset<Texture>("AbilityIcons/pig_rider_icon.png"),           "Ride a chaotic pig");
        CreatePowerup("Iron Golem",         PowerupKind.RLGL_IronGolem,         MinigameKind.RedLightGreenLight, typeof(RLGL_IronGolemAbility),         Assets.GetAsset<Texture>("AbilityIcons/iron_golem_icon.png"),          "Transform into a tough golem");
        CreatePowerup("Snowboard",          PowerupKind.RLGL_Snowboard,         MinigameKind.RedLightGreenLight, typeof(RLGL_SnowboardAbility),         Assets.GetAsset<Texture>("AbilityIcons/snowboard_icon.png"),           "Shred the gnar, bro!");
        CreatePowerup("Ice Patch",          PowerupKind.RLGL_IcePatch,          MinigameKind.RedLightGreenLight, typeof(RLGL_IcePatchAbility),          Assets.GetAsset<Texture>("AbilityIcons/ice_patch.png"),                "Place a slippery trap");
        CreatePowerup("Discoball Launcher", PowerupKind.RLGL_DiscoballLauncher, MinigameKind.RedLightGreenLight, typeof(RLGL_DiscoballLauncherAbility), Assets.GetAsset<Texture>("AbilityIcons/disco_ball_icon.png"),          "Force target to dance");
        CreatePowerup("Stampede",           PowerupKind.RLGL_Stampede,          MinigameKind.RedLightGreenLight, typeof(RLGL_StampedeAbility),          Assets.GetAsset<Texture>("AbilityIcons/stampede_icon.png"),            "Schleem Stampede crushing people");
        CreatePowerup("Banana Peel",        PowerupKind.RLGL_BananaPeel,        MinigameKind.RedLightGreenLight, typeof(RLGL_BananaPeelAbility),        Assets.GetAsset<Texture>("AbilityIcons/banana_peel_icon.png"),         "Place a banana trap");
        CreatePowerup("Glue Bomb",          PowerupKind.RLGL_GlueBomb,          MinigameKind.RedLightGreenLight, typeof(RLGL_GlueBombAbility),          Assets.GetAsset<Texture>("AbilityIcons/glue_bomb_icon.png"),           "Place a glue trap");
        
        CreatePowerup("Lightning Dash",     PowerupKind.HP_LightningDash, MinigameKind.HotPotato, typeof(RLGL_LightningDashAbility),                    Assets.GetAsset<Texture>("AbilityIcons/lightning_dash_icon.png"),      "Ultra fast dash");
        CreatePowerup("Reverse",            PowerupKind.HP_Reverse,       MinigameKind.HotPotato, null,                                        Assets.GetAsset<Texture>("AbilityIcons/reverse_icon.png"),             "Return bomb to sender");
        CreatePowerup("Air Mail",           PowerupKind.HP_AirMail,       MinigameKind.HotPotato, typeof(HP_AirMailAbility),                            Assets.GetAsset<Texture>("AbilityIcons/air_mail_icon.png"),            "Throw the bomb to somebody");
        CreatePowerup("More Time",          PowerupKind.HP_MoreTime,      MinigameKind.HotPotato, null,                                        Assets.GetAsset<Texture>("AbilityIcons/more_time_icon.png"),           "Add time when bomb reaches 0");
        CreatePowerup("Shadow Decoy",       PowerupKind.HP_ShadowDecoy,   MinigameKind.HotPotato, typeof(HP_ShadowDecoyAbility),                        Assets.GetAsset<Texture>("AbilityIcons/shadow_decoy.png"),             "Disguise as an object");
        CreatePowerup("Time Out",           PowerupKind.HP_TimeOut,       MinigameKind.HotPotato, typeof(HP_TimeOutAbility),                            Assets.GetAsset<Texture>("AbilityIcons/time_out.png"),                 "Freeze time for everyone else");
        CreatePowerup("Oil Spill",          PowerupKind.HP_OilSpill,      MinigameKind.HotPotato, typeof(HP_OilSpillAbility),                           Assets.GetAsset<Texture>("AbilityIcons/oill_spill.png"),               "Place slippery oil trap");
        CreatePowerup("Spring Glove",       PowerupKind.HP_SpringGlove,   MinigameKind.HotPotato, typeof(HP_SpringGloveAbility),                        Assets.GetAsset<Texture>("AbilityIcons/spring_boxing_glove_icon.png"), "Punch players away");
        CreatePowerup("Magnet Trap",        PowerupKind.HP_MagnetTrap,    MinigameKind.HotPotato, typeof(HP_MagnetTrapAbility),                         Assets.GetAsset<Texture>("AbilityIcons/magnet_trap.png"),              "Teleport bomb holder to another player");
        
        CreatePowerup("Chain Lightning",    PowerupKind.BP_ChainLightning, MinigameKind.BalloonPop, typeof(BP_ChainLightningAbility),                   Assets.GetAsset<Texture>("AbilityIcons/chain_lightning.png"),          "Bounces to nearest balloons of your color");
        CreatePowerup("Color Bomb",         PowerupKind.BP_ColorBomb,      MinigameKind.BalloonPop, typeof(BP_ColorBombAbility),                        Assets.GetAsset<Texture>("AbilityIcons/red_colour_bomb_icon.png"),     "Turn balloons to your color");
        CreatePowerup("Balloon Magnet",     PowerupKind.BP_BalloonMagnet,  MinigameKind.BalloonPop, null,                                      Assets.GetAsset<Texture>("AbilityIcons/balloon_magnet_icon.png"),      "Pull balloons toward you");
        CreatePowerup("Mini Tornado",       PowerupKind.BP_MiniTornado,    MinigameKind.BalloonPop, typeof(BP_MiniTornadoAbility),                      Assets.GetAsset<Texture>("AbilityIcons/mini_tornado.png"),             "Shoot a tornado");
        CreatePowerup("Locked In",          PowerupKind.BP_LockedIn,       MinigameKind.BalloonPop, typeof(BP_LockedInAbility),                         Assets.GetAsset<Texture>("AbilityIcons/locked_in.png"),                "Shoot super fast");
        CreatePowerup("Decoy Balloon",      PowerupKind.BP_DecoyBalloon,   MinigameKind.BalloonPop, typeof(BP_DecoyBalloonAbility),                     Assets.GetAsset<Texture>("AbilityIcons/decoy_balloon.png"),            "Spawn a decoy balloon");
        CreatePowerup("Anvil Trap",         PowerupKind.BP_AnvilTrap,      MinigameKind.BalloonPop, null,                                      Assets.GetAsset<Texture>("AbilityIcons/anvil_trap_spot.png"),          "Trap that drops an anvil");
        CreatePowerup("EMP",                PowerupKind.BP_EMP,            MinigameKind.BalloonPop, null,                                      Assets.GetAsset<Texture>("AbilityIcons/emp_icon.png"),                 "Disable other players darts");
        CreatePowerup("Balloon Nuke",       PowerupKind.BP_BalloonNuke,    MinigameKind.BalloonPop, typeof(BP_BalloonNukeAbility),                      Assets.GetAsset<Texture>("AbilityIcons/nuke_icon.png"),                "Nuke the enemy balloons");
        
        CreatePowerup("Bat",                PowerupKind.Mingle_Bat,        MinigameKind.Mingle, typeof(Mingle_BatAbility),                              Assets.GetAsset<Texture>("AbilityIcons/bat_icon.png"),                 "Smack your opponents away!");
        CreatePowerup("Snowboard",          PowerupKind.Mingle_Snowboard,  MinigameKind.Mingle, typeof(RLGL_SnowboardAbility),                          Assets.GetAsset<Texture>("AbilityIcons/snowboard_icon.png"),           "Shred the gnar, bro!");
        CreatePowerup("Glue Bomb",          PowerupKind.Mingle_GlueBomb,   MinigameKind.Mingle, typeof(RLGL_GlueBombAbility),                           Assets.GetAsset<Texture>("AbilityIcons/glue_bomb_icon.png"),           "Place a glue trap");
        //CreatePowerup("Lasso",              PowerupKind.Mingle_Lasso,        MinigameKind.Mingle, typeof(Mingle_LassoAbility),                          Assets.GetAsset<Texture>("AbilityIcons/chain_lightning.png"),          "");
        //CreatePowerup("Glue Gun",           PowerupKind.Mingle_GlueGun,      MinigameKind.Mingle, typeof(Mingle_GlueGunAbility),                        Assets.GetAsset<Texture>("AbilityIcons/balloon_magnet_icon.png"),      "");
        //CreatePowerup("Frying Pan",         PowerupKind.Mingle_FryingPan,    MinigameKind.Mingle, typeof(Mingle_FryingPanAbility),                      Assets.GetAsset<Texture>("AbilityIcons/mini_tornado.png"),             "");
        //CreatePowerup("Cool Stick",         PowerupKind.Mingle_CoolStick,    MinigameKind.Mingle, typeof(Mingle_CoolStickAbility),                      Assets.GetAsset<Texture>("AbilityIcons/locked_in.png"),                "");
        //CreatePowerup("Lemon Blaster",      PowerupKind.Mingle_LemonBlaster, MinigameKind.Mingle, typeof(Mingle_LemonBlasterAbility),                   Assets.GetAsset<Texture>("AbilityIcons/decoy_balloon.png"),            "");
    }

    [ClientRpc]
    public static void GlobalNotification(string message)
    {
        Notifications.Show(message);
    }

    public static bool IsMinigame(MinigameKind kind)
    {
        return GameManager.Instance.CurrentMinigame.Alive() && GameManager.Instance.CurrentMinigame.Kind == kind;
    }

    public static UI.ButtonSettings GetButtonSettings(Texture sprite)
    {
        var bs = new UI.ButtonSettings();
        bs.Sprite = sprite;
        return bs;
    }

    public static UI.TextSettings GetTextSettings(float size, UI.HorizontalAlignment halign = UI.HorizontalAlignment.Center, UI.VerticalAlignment valign = UI.VerticalAlignment.Center)
    {
        return GetTextSettings(size, new Vector4(1, 1, 1, 1), halign, valign);
    }

    public static UI.TextSettings GetTextSettings(float size, Vector4 textColor, UI.HorizontalAlignment halign = UI.HorizontalAlignment.Center, UI.VerticalAlignment valign = UI.VerticalAlignment.Center)
    {
        var ts = new UI.TextSettings()
        {
            Font = UI.Fonts.BarlowBold,
            Size = size,
            Color = textColor,
            DropShadow = true,
            DropShadowColor = new Vector4(0f,0f,0.02f,0.5f),
            DropShadowOffset = new Vector2(0f,-3f),
            HorizontalAlignment = halign,
            VerticalAlignment = valign,
            WordWrap = false,
            WordWrapOffset = 0,
            Outline = true,
            OutlineThickness = 3,
        };
        return ts;
    }

    public static void DrawFullscreenFader(float alpha)
    {
        UI.Image(UI.ScreenRect, null, new Vector4(0, 0, 0, alpha));
    }

    public static void DrawBottomText(string message, float offset = 0)
    {
        var bottomTextRect = UI.SafeRect.CutBottom(350).Offset(0, offset);
        var ts = GetTextSettings(52);
        ts.WordWrap = true;
        UI.Text(bottomTextRect, message, ts);
    }

    public void PlayerInteractedWithMinigameDoor(MyPlayer player, MinigameKind minigame)
    {
        if (TrySetMinigameForThisServer(minigame))
        {
            NextMinigameOverride = minigame;
            State = GameState.WaitingForPlayers;
        }
    }

    public static float TestPiggyHeight;

    // [UIPreview]
    public static void DrawPiggyTest()
    {
        DrawPiggy(ref TestPiggyHeight, 0.25f, 0.75f, (Time.TimeSinceStartup*(60f/144f)) % 6);
    }

    public static void DrawPiggy(ref float currentHeight, float startHeight, float endHeight, float time)
    {
        var slide = Ease.OutQuart(Ease.FadeInAndOut(1.5f, 0.5f, 5f, time));
        var mainRect = UI.ScreenRect.Slide(0, 1f-slide);

        var piggyBack = Assets.GetAsset<Texture>("ui/piggy/piggy_bank.png");
        var piggyFront = Assets.GetAsset<Texture>("ui/piggy/piggy_bank_front.png");
        var money1 = Assets.GetAsset<Texture>("ui/piggy/bills_wad_A.png");

        var piggyRect = mainRect.CenterRect().Grow(400).FitAspect(piggyFront.Aspect).Offset(0, 200);
        UI.Image(piggyRect.FitAspect(piggyBack.Aspect).Offset(0, 14),  piggyBack);

        var billSize = 35f;

        // falling bills
        {
            var fallRotateRng = RNG.Seed(12378);
            var fallStart = mainRect.CenterRect().Grow(billSize).Offset(0, 800.0f);
            var fallEnd   = mainRect.CenterRect().Grow(billSize).Offset(0, -125f);
            for (int i = 0; i < 15; i++)
            {
                var fall01 = Ease.InSine(Ease.T(time - ((float)i * 0.1f), 2.0f));
                var xOffset = RNG.RangeFloat(ref fallRotateRng, -75f, 75f);
                var billRect = fallStart;
                billRect.Min = Vector2.Lerp(fallStart.Min, fallEnd.Min, fall01);
                billRect.Max = Vector2.Lerp(fallStart.Max, fallEnd.Max, fall01);
                billRect = billRect.Offset(xOffset, 0);
                using var _ = UI.PUSH_ROTATE_ABOUT_POINT(RNG.RangeFloat(ref fallRotateRng, 0, 360) + fall01 * 180f, billRect.Center);
                var alpha01 = Ease.T(fall01-1f, 0.1f);
                UI.Image(billRect.FitAspect(money1.Aspect), money1, new Vector4(1, 1, 1, 1f-alpha01));
            }
        }

        var startDelay = 1.75f;
        var fill01 = Ease.T(time-startDelay, 1.25f);

        var piggyCenter = piggyRect.Offset(10, -175).Center;
        var d = 8;
        var scale = 40f * UI.ScreenScaleFactor;
        var jitterRng = RNG.Seed(1337);
        var topRng = RNG.Seed(12321);
        var rotateRng = RNG.Seed(1235);
        var jitter = 5f * UI.ScreenScaleFactor;
        var radius = 175f * UI.ScreenScaleFactor;
        currentHeight = AOMath.Lerp(startHeight, endHeight, fill01);
        var highWaterMarkFloat = currentHeight;
        var highWaterMark = (int)MathF.Ceiling(d * highWaterMarkFloat);
        for (int y = 0; y < highWaterMark; y++)
        {
            for (int x = 0; x < d; x++)
            {
                var pos = new Vector2(x, y);
                pos -= new Vector2((float)(d-1) / 2, (float)(d-1) / 2);
                pos *= scale;
                pos += piggyCenter;
                pos += new Vector2(RNG.RangeFloat(ref jitterRng, -jitter, jitter), RNG.RangeFloat(ref jitterRng, -jitter, jitter));
                var layerDim = 1.0f / d + 0.0000001f; // note(josh): tiny epsilon prevents wrapping to 0 when highWaterMarkFloat is 1.0f
                var layer01 = 1.0f - ((highWaterMarkFloat % layerDim) * d);
                var skip = false;
                var alpha = 1f;
                if (y == highWaterMark-1)
                {
                    skip = RNG.RangeFloat(ref topRng, 0, 1) < layer01;
                    alpha = Ease.T(time-startDelay+0.05f, 1f);
                }

                var rect = new Rect(pos, pos).Grow(billSize);
                using var _ = UI.PUSH_ROTATE_ABOUT_POINT(RNG.RangeFloat(ref rotateRng, 0, 360), rect.Center);
                if (!skip)
                {
                    if ((pos - piggyCenter).LengthSquared < (radius*radius))
                    {
                        UI.Image(rect.FitAspect(money1.Aspect), money1, new Vector4(1, 1, 1, alpha));
                    }
                    else
                    {
                        // UI.Image(rect.FitAspect(money1.Aspect), money1, new Vector4(1, 1, 1, 0.25f));
                    }
                }
            }
        }

        UI.Image(piggyRect.FitAspect(piggyFront.Aspect), piggyFront);

        var spotlightTexture = Assets.GetAsset<Texture>("ui/piggy/spotlight.png");
        var spotlightRect = UI.ScreenRect.TopCenterRect().GrowBottom(spotlightTexture.Height * 0.35f).FitAspect(spotlightTexture.Aspect, Rect.FitAspectKind.KeepHeight).Offset(0, 80);
        UI.Image(spotlightRect, spotlightTexture, new Vector4(1, 1, 1, slide));
    }

    // [UIPreview]
    public static void DrawNextUp()
    {
        var timeInCurrentState = Time.TimeSinceStartup % 6;
        var timeOffset = 0.25f;
        var nextUp01 = Ease.OutQuart(Ease.FadeInAndOut(0.25f, 0.25f, 4.5f, timeInCurrentState - timeOffset));
        DrawNextUp("Red Light, Green Light", nextUp01);
    }

    public static void DrawNextUp(string minigameName, float t)
    {
        var nextUpTexture = Assets.GetAsset<Texture>("ui/next_game_banner.png");
        float size = 1f;
        var nextUpRect = UI.SafeRect.TopLeftRect().Grow(0, nextUpTexture.Width * size, nextUpTexture.Height * size, 0).Offset(0, -25);
        nextUpRect = nextUpRect.Slide((-1 + t), 0);
        UI.Image(nextUpRect, nextUpTexture);

        var ts = GetTextSettings(60);
        ts.Outline = false;
        ts.DropShadow = true;
        ts.DropShadowOffset = new Vector2(0, -4);
        ts.DropShadowColor = new Vector4(0, 0, 0, 1);

        {
            var textRect = nextUpRect.Offset(-20, -20);
            using var _ = UI.PUSH_ROTATE_ABOUT_POINT(1, textRect.Center);
            UI.Text(textRect, minigameName, ts);
        }

        {
            var textRect = nextUpRect.Offset(-200, 55);
            using var _ = UI.PUSH_ROTATE_ABOUT_POINT(2, textRect.Center);
            ts.Size = 40;
            ts.DropShadow = false;
            UI.Text(textRect, "NEXT UP", ts);
        }

        {
            var descRect = UI.SafeRect.BottomRightRect().Grow(175, 0, 0, 700).Slide(1f-t, 0).Inset(0, 25, 25, 0);
            var descTs = GetTextSettings(35);
            descTs.HorizontalAlignment = UI.HorizontalAlignment.Right;
            descTs.VerticalAlignment = UI.VerticalAlignment.Center;
            descTs.WordWrap = true;
            descTs.Slant = 0.4f;
            var schleem = Assets.GetAsset<Texture>("sprites/schleem.png");
            var iconRect = descRect.CutRightUnscaled(descRect.Height * schleem.Aspect);
            UI.Image(iconRect.Scale(-1, 1), schleem);
            descRect.CutRight(25);
            UI.Text(descRect, GameManager.Instance.CurrentMinigame.Description, descTs);
        }
    }

    // [UIPreview]
    public static void DrawPlayerGrid(List<MyPlayer> players, float prevTime, float time, float duration, bool doRewardsAndElims)
    {
        if (Network.IsServer)
        {
            return;
        }

        var gridPositions = new Vector2[]
        {
            new Vector2(-0.5f, 0), new Vector2(0.5f, 0), new Vector2(0f, -0.5f), new Vector2(-1f, -0.5f), new Vector2(1f, -0.5f), new Vector2(-1.5f, 0),  new Vector2(1.5f, 0),
        };
        var playerDiamondNumber = Assets.GetAsset<Texture>("ui/player_list/player_diamond_number.png");
        var numberBack          = Assets.GetAsset<Texture>("ui/player_list/number_back.png");
        var playerDiamondBack   = Assets.GetAsset<Texture>("ui/player_list/player_diamond_back.png");

        var countPerRow = 7;
        var rowCount = (int)MathF.Ceiling((float)players.Count / 7f);
        var entrySize = 175;
        var entryRect = UI.ScreenRect.CenterRect().Grow(entrySize / 2f);
        var numberTs = GetTextSettings(35);
        var timeOffsetPerPlayer = 0.5f / (float)players.Count;
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];
            var indexInRow = i % countPerRow;
            var rowIndex = i / countPerRow;
            var myEntryRect = entryRect.Slide(gridPositions[indexInRow].X, gridPositions[indexInRow].Y);
            myEntryRect = myEntryRect.Slide(0, -0.25f + rowCount * 0.5f - rowIndex);

            var prevLocalTime = prevTime - (players.Count - i - 1) * timeOffsetPerPlayer;
            var localTime     =     time - (players.Count - i - 1) * timeOffsetPerPlayer;
            var localDuration = duration - (players.Count - i - 1) * (timeOffsetPerPlayer * 2);
            var show01 = 1f - Ease.OutQuart(Ease.FadeInAndOut(0.4f, 0.4f, localDuration, localTime-0.5f));
            myEntryRect = myEntryRect.Offset(0, 1080f * show01);

            var diamondRect = myEntryRect.FitAspect(playerDiamondBack.Aspect);
            UI.Image(diamondRect, playerDiamondBack);

            if (player.Alive())
            {
                var maskScope = IM.CreateMaskScope(diamondRect);
                {
                    using var _123 = IM.BUILD_MASK_SCOPE(maskScope);
                    UI.Image(diamondRect, playerDiamondBack);
                }

                {
                    using var _323 = IM.USE_MASK_SCOPE(maskScope);
                    using var _ = UI.PUSH_UNLIT_PLAYER_MATERIAL(player);

                    var scale = new Vector2(entrySize * 0.7f, entrySize * 0.7f);
                    UI.DrawSkeleton(myEntryRect.Offset(0, -65), player.GetSpineInstanceForPlayerList(), scale, 0);
                }
            }

            UI.Image(myEntryRect.Grow(2).FitAspect(playerDiamondNumber.Aspect).Grow(2), playerDiamondNumber);
            UI.Text(myEntryRect.Offset(0, -40), player.PlayerNumberForThisTournament.Value.ToString("D3"), numberTs);

            if (doRewardsAndElims)
            {
                using var _ = UI.PUSH_LAYER_RELATIVE(10);
                if (player.IsEliminated || player.Alive() == false)
                {
                    var elim01 = Ease.OutBack(Ease.T(localTime - 1f, 0.35f));
                    if (player.WasEliminatedThisRound == false || player.Alive() == false)
                    {
                        elim01 = 1.0f;
                    }
                    var ts = GetTextSettings(elim01 * 150);
                    ts.Color = new Vector4(1, 0, 0, 1);
                    UI.Text(myEntryRect, "X", ts);
                }
                else
                {
                    if (Instance.IsPrivateServer == false) // no xp in private servers
                    {
                        var xpReward01 = Ease.OutBack(Ease.FadeInAndOut(0.15f, 0.15f, 1.0f, localTime - 1f));
                        var levelUp01  = Ease.OutBack(Ease.FadeInAndOut(0.15f, 0.15f, 1.0f, localTime - 2f));

                        if (player.IsLocal)
                        {
                            if (prevLocalTime < 1f && localTime >= 1f)
                            {
                                SFX.Play(Assets.GetAsset<AudioAsset>("sfx/Special Click 03.wav"), new SFX.PlaySoundDesc(){Volume=0.7f});
                            }

                            if (player.DidLevelUpThisMinigame)
                            {
                                if (prevLocalTime < 2f && localTime >= 2f)
                                {
                                    SFX.Play(Assets.GetAsset<AudioAsset>("sfx/Special Click 05.wav"), new SFX.PlaySoundDesc(){Volume=0.7f});
                                }
                            }
                        }

                        var rewardTs = GetTextSettings(50);
                        var textRect = myEntryRect.Offset(0, 25);

                        using var _32123 = UI.PUSH_ROTATE_ABOUT_POINT(MathF.Sin(2 * MathF.PI * Time.TimeSinceStartup) * 10f, textRect.Center);

                        rewardTs.Color = new Vector4(0.5f, 0, 0.5f, 1f);
                        rewardTs.Size = 50f * xpReward01;
                        UI.Text(textRect, $"+{GameManager.Instance.XPRewardForThisRound}XP", rewardTs);

                        if (player.DidLevelUpThisMinigame)
                        {
                            var time1 = Time.TimeSinceStartup+0f;
                            var time2 = Time.TimeSinceStartup+0.33f;
                            var time3 = Time.TimeSinceStartup+0.66f;
                            var r = AOMath.SinXY(0f, 1f, 2f * MathF.PI * time1);
                            var g = AOMath.SinXY(0f, 1f, 2f * MathF.PI * time2);
                            var b = AOMath.SinXY(0f, 1f, 2f * MathF.PI * time3);
                            rewardTs.Color = new Vector4(r, g, b, 1f);
                            rewardTs.Size = 50f * levelUp01;
                            UI.Text(textRect, $"LEVEL {player.PlayerLevel}!", rewardTs);
                        }
                    }
                }
            }
        }

        // UI.Image(UI.ScreenRect.SubRect(0.5f, 0, 0.5f, 1).Grow(0, 5, 0, 5), null, new Vector4(1, 1, 0, 1));
        // UI.Image(UI.ScreenRect.SubRect(0, 0.5f, 1, 0.5f).Grow(5, 0, 5, 0), null, new Vector4(1, 1, 0, 1));
    }

    // [UIPreview]
    public static void DrawPassScreen()
    {
        var time = ((Time.TimeSinceStartup * (60f/144f)) % 5f);
        time -= 0.5f;
        DrawPassScreen(1, time, time - 0.01666666f);
    }

    public static void DrawPassScreen(int passElimNeither, float time, float prevTime)
    {
        using var _1 = UI.PUSH_SCALE_FACTOR(UI.ScreenScaleFactor * 3);

        float blackLine01;
        float mainGrow01;
        if (time < 3.0f)
        {
            blackLine01 = Ease.OutQuart(Ease.T(time, 0.25f));
            mainGrow01  = Ease.OutQuart(Ease.T(time-0.2f, 0.25f));
        }
        else
        {
            blackLine01 = Ease.OutQuart(Ease.T(time-3.2f, 0.25f));
            mainGrow01  = 1f-Ease.OutQuart(Ease.T(time-3.0f, 0.25f));
        }

        var blackLineRect = UI.ScreenRect.CenterRect().Grow(20, 200, 20, 200);
        if (time < 3.0f)
        {
            blackLineRect = blackLineRect.SubRect(0, 0, blackLine01, 1);
        }
        else
        {
            blackLineRect = blackLineRect.SubRect(blackLine01, 0, 1, 1);
        }
        UI.Image(blackLineRect, null, new Vector4(0, 0, 0, 1));

        var pulse1 = AOMath.SinXY(0.95f, 1.05f, 2f * MathF.PI * time);
        using var _2 = UI.PUSH_SCALE_FACTOR(UI.ScreenScaleFactor * pulse1);

        var elimTexture = Assets.GetAsset<Texture>("ui/pass/eliminated_backing.png");
        var passTexture = Assets.GetAsset<Texture>("ui/pass/round_pass_backing.png");
        var texture = passTexture;
        if (passElimNeither == 1)
        {
            texture = elimTexture;
        }
        var hs = new Vector2(texture.Width, texture.Height) * 0.5f * 0.25f;
        var mainRect = UI.ScreenRect.CenterRect().Grow(hs.Y, hs.X, hs.Y, hs.X).Scale(mainGrow01);
        UI.Image(mainRect, texture);

        var ts = GetTextSettings(40 * mainGrow01);
        ts.DropShadow = true;
        ts.DropShadowColor = new Vector4(0, 0, 0, 1);
        ts.DropShadowOffset = new Vector2(0, -5);
        ts.Outline = false;
        var youText = "YOU";
        switch (passElimNeither)
        {
            case 0: UI.Text(mainRect, "PASSED", ts);         youText = "YOU";      if (prevTime <= 0f && time > 0f) { SFX.Play(Assets.GetAsset<AudioAsset>("sfx/round_pass_v2.wav"),       new SFX.PlaySoundDesc(){Volume=0.6f}); } break;
            case 1: UI.Text(mainRect, "ELIMINATED", ts);     youText = "YOU WERE"; if (prevTime <= 0f && time > 0f) { SFX.Play(Assets.GetAsset<AudioAsset>("sfx/round_eliminated_v2.wav"), new SFX.PlaySoundDesc(){Volume=0.6f}); } break;
            case 2: UI.Text(mainRect, "ROUND FINISHED", ts); youText = "";         if (prevTime <= 0f && time > 0f) { SFX.Play(Assets.GetAsset<AudioAsset>("sfx/round_finished_v2.wav"),   new SFX.PlaySoundDesc(){Volume=0.6f}); } break;
        }

        ts.Size *= 0.35f;
        UI.Text(mainRect.Offset(0, 28), youText, ts);
    }

    public void DrawCinematicBlackBars(float t)
    {
        UI.Image(UI.ScreenRect.BottomRect().GrowTop(200 * t), null, new Vector4(0, 0, 0, 1));
        UI.Image(UI.ScreenRect.TopRect().GrowBottom(200 * t), null, new Vector4(0, 0, 0, 1));
    }

    [ServerRpc]
    public void SetPrepPhaseReady()
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (player.Alive() == false) return;
        player.PrepPhaseReady.Set(true);
    }

    public void ServerMoveAllPlayersToMinigameSpawnPoints()
    {
        var spawns = new List<Vector2>();
        foreach (var spawn in CurrentMinigame.PlayerSpawnsParent.Children)
        {
            spawns.Add(spawn.Position);
        }
        Shuffle(spawns, ref GlobalRng);

        var spawnIndex = 0;
        foreach (var player in Scene.Components<MyPlayer>())
        {
            player.SpawnForThisMinigame.Set(spawns[spawnIndex]);
            spawnIndex += 1;
            player.CallClient_ForceTeleport(player.SpawnForThisMinigame);
        }
    }

    public const int GameHUDLayer = 100000;

    public override void Update()
    {
        using var _ = UI.PUSH_LAYER(GameHUDLayer);

        var timeInCurrentState = Time.TimeSinceStartup - TimeStateStarted;
        var justEnteredState = StateLastFrame != State;
        StateLastFrame = State;
        using var _324 = AllOut.Defer(() => PrevTimeInState = timeInCurrentState);

        var cutsceneCameraActive = false;
        var cutsceneCameraZoom = 1.35f;
        MainThemeTargetVolume = 0f;

        ServerCanReceivePlayers = false;

        const int MinimumRequiredPlayers = 8;

        switch (State)
        {
            case GameState.WaitingForPlayers:
            {
                if (Network.IsServer)
                {
                    if (justEnteredState)
                    {
                        FrontmanEnabled.Set(false);
                        foreach (var player in Scene.Components<MyPlayer>())
                        {
                            player.IsFrontman.Set(false);
                        }
                    }
                }

                FullscreenFaderTarget = 0;
                if (FrontmanEnabled == false)
                {
                    ServerCanReceivePlayers = true;
                    var playerCount = Scene.Components<MyPlayer>().Count();
                    if (Network.IsServer)
                    {
                        if (playerCount < MinimumRequiredPlayers)
                        {
                            MinigameTimer = 30;
                        }
                        else
                        {
                            MinigameTimer -= Time.DeltaTime;
                            if (MinigameTimer <= 0)
                            {
                                State = GameState.StartTournament;
                            }
                        }
                        if (MinigameTimer < 10f)
                        {
                            ServerCanReceivePlayers = false;
                        }
                    }
                    else
                    {
                        if (playerCount < MinimumRequiredPlayers)
                        {
                            DrawBottomText($"Waiting for players ({playerCount}/{MinimumRequiredPlayers})...");
                        }
                        else
                        {

                            DrawBottomText($"Starting in {MinigameTimer:F3}s...");
                        }
                    }
                }
                else
                {
                    var player = (MyPlayer)Network.LocalPlayer;
                    if (player.Alive() && player.IsFrontman)
                    {
                        DrawBottomText($"Start the tournament with the `/start` chat command.");
                    }
                }
                break;
            }
            case GameState.StartTournament:
            {
                if (justEnteredState)
                {
                    SFX.Restart(MainThemeSFX);
                    MainThemeCurrentVolume = MainThemeMaxVolume;
                }
                MainThemeTargetVolume = MainThemeMaxVolume;

                FullscreenFaderTarget = 1;
                if (Network.IsServer)
                {
                    if (timeInCurrentState >= 1.5f)
                    {
                        CurrentRoundIndex.Set(-1);
                        TournamentIsRunning.Set(true);

                        MinigameQueue.Clear();
                        if (FrontmanEnabled)
                        {
                            MinigameQueue.Add(MinigameKind.RedLightGreenLight);
                            MinigameQueue.Add(MinigameKind.Mingle);
                            MinigameQueue.Add(MinigameKind.HotPotato);
                            MinigameQueue.Add(MinigameKind.BalloonPop);
                            MinigameQueue.Add(MinigameKind.CirclePush);
                        }
                        else
                        {
                            for (int minigameKind = (int)MinigameKind.FirstMinigame; minigameKind <= (int)MinigameKind.LastMinigame; minigameKind++)
                            {
                                MinigameQueue.Add((MinigameKind)minigameKind);
                            }
                            Shuffle(MinigameQueue, ref GlobalRng);
                        }

                        PlayersInCurrentTournament.Clear();
                        int i = 0;
                        foreach (var player in Scene.Components<MyPlayer>())
                        {
                            player.PositionInTournamentList.Set(i);
                            player.IsPartOfTheCurrentTournament.Set(true);
                            PlayersInCurrentTournament.Add(player);
                            i += 1;
                        }

                        foreach (var player in PlayersInCurrentTournament)
                        {
                            if (player.IsFrontman)
                            {
                                player.PlayerNumberForThisTournament.Set(1);
                            }
                            else
                            {
                                var playerNumber = 149;
                                var iters = 10;
                                do {
                                    playerNumber = (int)RNG.RangeInt(ref GameManager.Instance.GlobalRng, 2, 456);
                                }
                                while (Scene.Components<MyPlayer>().Any(p => p.PlayerNumberForThisTournament == playerNumber) && iters --> 0);
                                player.PlayerNumberForThisTournament.Set(playerNumber);
                            }
                        }

                        CallClient_UpdateTournamentPlayerList(WriteListOfNetworkedComponentsToBytes(PlayersInCurrentTournament));
                        if (FastMode)
                        {
                            State = GameState.SetupNextGame;
                        }
                        else
                        {
                            State = GameState.ShowPlayersBeforeTournament;
                        }
                    }
                }
                break;
            }
            case GameState.ShowPlayersBeforeTournament:
            {
                MainThemeTargetVolume = MainThemeMaxVolume;
                FullscreenFaderTarget = 1;
                DrawPlayerGrid(PlayersInCurrentTournament, PrevTimeInState, timeInCurrentState, 4.0f, false);
                if (Network.IsServer)
                {
                    if (timeInCurrentState >= 5f)
                    {
                        State = GameState.SetupNextGame;
                    }
                }
                break;
            }
            case GameState.SetupNextGame:
            {
                MainThemeTargetVolume = MainThemeMaxVolume;
                FullscreenFaderTarget = 1;
                if (Network.IsServer)
                {
                    MinigameTimer = 0;
                    MinigameTimerEnabled = false;
                    CurrentRoundIndex.Set(CurrentRoundIndex+1);

                    // pick next minigame
                    {
                        Util.Assert(CurrentRoundIndex >= 0);
                        MinigameKind nextMinigame = MinigameQueue[CurrentRoundIndex % MinigameQueue.Count];
                        if (NextMinigameOverride != MinigameKind.None)
                        {
                            nextMinigame = NextMinigameOverride;
                            NextMinigameOverride = MinigameKind.None;
                        }
                        PrevMinigame = nextMinigame;
                        Util.Assert(nextMinigame != MinigameKind.None);
                        bool ok = TrySetMinigameForThisServer(nextMinigame);
                        Util.Assert(ok);
                    }

                    PlayersInCurrentMinigame.Clear();
                    foreach (var player in PlayersInCurrentTournament) if (player.Alive())
                    {
                        player.WasEliminatedThisRound.Set(false);
                        player.UseLivesForMinigame.Set(false);
                        player.IsDead.Set(false);
                        player.PrepPhaseReady.Set(false);
                        player.ServerClearPowerups();
                        if (player.IsEliminated == false)
                        {
                            PlayersInCurrentMinigame.Add(player);
                        }
                    }

                    ServerMoveAllPlayersToMinigameSpawnPoints();
                    foreach (var player in Scene.Components<MyPlayer>())
                    {
                        player.ServerTimeRanOutOfLives.Set(0);
                        player.IsDead.Set(false);
                        player.MinigameLivesLeft.Set(3);
                        player.UseLivesForMinigame.Set(false);
                        player.RLGLFinished.Set(false);
                        player.RLGLDistanceReached = 0;
                        player.BalloonsPopped.Set(0);
                        player.DidLevelUpThisMinigame.Set(false);
                        player.SmashMeter = 1f;
                        player.TotalSmashDamage = 0f;
                    }

                    CallClient_SetupMinigame(WriteListOfNetworkedComponentsToBytes(PlayersInCurrentMinigame));

                    if (FastMode)
                    {
                        ServerStartMinigame();
                    }
                    else
                    {
                        State = GameState.NextGameScreen;
                    }
                }
                break;
            }
            case GameState.NextGameScreen:
            {
                MainThemeTargetVolume = MainThemeMaxVolume;

                var stateDuration = 6f;
                var stateDuration01 = Ease.T(timeInCurrentState, stateDuration);
                FullscreenFaderTarget = AOMath.Lerp(1.0f, 0.35f, Ease.FadeInAndOut(0.35f, 0.35f, stateDuration-0.5f, timeInCurrentState));
                cutsceneCameraActive = true;
                cutsceneCameraZoom = CurrentMinigame.IntroLerpCameraZoom;

                var maxRoundsForMoneyHeight = 5f;
                if (justEnteredState)
                {
                    CurrentMoneyHeight = (float)CurrentRoundIndex / maxRoundsForMoneyHeight;
                    UI.SetLeaderboardOpen(false);
                    UI.SetChatOpen(false);
                    var desc = new SFX.PlaySoundDesc() { Volume = 0.7f };
                    // desc.Delay = 2f;
                    SFX.Play(Assets.GetAsset<AudioAsset>("sfx/piggybank_cutscene.wav"), desc);
                }

                DrawCinematicBlackBars(1);

                if (CutsceneCamera != null)
                {
                    if (CurrentMinigame.IntroLerpStart.Alive() && CurrentMinigame.IntroLerpEnd.Alive())
                    {
                        CutsceneCamera.Position = Vector2.Lerp(CurrentMinigame.IntroLerpStart.Position, CurrentMinigame.IntroLerpEnd.Position, stateDuration01);
                    }
                    else
                    {
                        CutsceneCamera.Position = CurrentMinigame.Position;
                    }
                }

                var startHeight = MathF.Min(1, (float)CurrentRoundIndex / maxRoundsForMoneyHeight);
                var endHeight   = MathF.Min(1, (float)(CurrentRoundIndex+1) / maxRoundsForMoneyHeight);
                DrawPiggy(ref CurrentMoneyHeight, startHeight, endHeight, (timeInCurrentState - 2f) * 1.8f);

                var timeOffset = 0.25f;
                var nextUp01 = Ease.OutQuart(Ease.FadeInAndOut(0.25f, 0.25f, 4.5f, timeInCurrentState - timeOffset));
                DrawNextUp(CurrentMinigame.NiceName, nextUp01);

                if (Network.IsServer)
                {
                    if (timeInCurrentState >= stateDuration)
                    {
                        State = GameState.StartGameFadeIn;
                    }
                }
                break;
            }
            case GameState.StartGameFadeIn:
            {
                MainThemeTargetVolume = MainThemeMaxVolume;
                FullscreenFaderTarget = 0.5f;
                if (FrontmanEnabled == false && MinigameTimerEnabled)
                {
                    DrawTimer("Time Left", MinigameTimer);
                }

                if (timeInCurrentState >= 0.5f)
                {
                    float time3 = 0.5f;
                    float time2 = 1.5f;
                    float time1 = 2.5f;
                    if (timeInCurrentState >= time1)
                    {
                        if (PrevTimeInState < time1)
                        {
                            SFX.Play(Assets.GetAsset<AudioAsset>("sfx/Special Click 09.wav"), new SFX.PlaySoundDesc(){Volume = 0.7f});
                        }
                        var t = Ease.InQuart(Ease.T(timeInCurrentState - time1, 1f));
                        var ts = GetTextSettings(125);
                        ts.Color.W = 1f - t;
                        UI.Text(UI.ScreenRect.CenterRect().Offset(0, -100 * t), "1...", ts);
                    }
                    else if (timeInCurrentState >= time2)
                    {
                        if (PrevTimeInState < time2)
                        {
                            SFX.Play(Assets.GetAsset<AudioAsset>("sfx/Special Click 09.wav"), new SFX.PlaySoundDesc(){Volume = 0.7f});
                        }
                        var t = Ease.InQuart(Ease.T(timeInCurrentState - time2, 1f));
                        var ts = GetTextSettings(125);
                        ts.Color.W = 1f - t;
                        UI.Text(UI.ScreenRect.CenterRect().Offset(0, -100 * t), "2...", ts);
                    }
                    else if (timeInCurrentState >= time3)
                    {
                        if (PrevTimeInState < time3)
                        {
                            SFX.Play(Assets.GetAsset<AudioAsset>("sfx/Special Click 09.wav"), new SFX.PlaySoundDesc(){Volume = 0.7f});
                        }
                        var t = Ease.InQuart(Ease.T(timeInCurrentState - time3, 1f));
                        var ts = GetTextSettings(125);
                        ts.Color.W = 1f - t;
                        UI.Text(UI.ScreenRect.CenterRect().Offset(0, -100 * t), "3...", ts);
                    }
                }

                if (timeInCurrentState >= 3.5f)
                {
                    if (Network.IsServer)
                    {
                        ServerStartMinigame();
                    }
                }
                break;
            }
            case GameState.RunningMinigame:
            {
                if (justEnteredState)
                {
                    SFX.Play(Assets.GetAsset<AudioAsset>("sfx/Special Click 07.wav"), new SFX.PlaySoundDesc(){Volume = 0.7f});
                    if (CurrentMinigame.ThemeSong.Has())
                    {
                        var play = true;
                        if (FrontmanEnabled && CurrentMinigame.Kind != MinigameKind.Mingle && CurrentMinigame.Kind != MinigameKind.RedLightGreenLight)
                        {
                            play = false;
                        }

                        if (play)
                        {
                            PlayTheme(CurrentMinigame.ThemeSong);
                        }
                    }
                }

                SFX.SetLoopTimeout(MinigameThemeSFX, 1f);

                // draw GO
                {
                    var t = Ease.InQuart(Ease.T(timeInCurrentState, 1f));
                    if (t < 1f)
                    {
                        var ts = GetTextSettings(150);
                        var offset = new Vector2(RNG.RangeFloat(ref GlobalRng, -1, 1), RNG.RangeFloat(ref GlobalRng, -1, 1)) * 10f;
                        var rect = UI.ScreenRect.CenterRect().Offset(0, 0);
                        UI.Text(rect.Offset(offset.X, offset.Y), "GO!!!", ts);
                    }
                }

                FullscreenFaderTarget = 0;

                if (FrontmanEnabled == false && MinigameTimerEnabled)
                {
                    DrawTimer("Time Left", MinigameTimer);
                    if (Network.IsServer)
                    {
                        MinigameTimer -= Time.DeltaTime;
                        if (MinigameTimer <= 0)
                        {
                            ServerEndMinigame();
                            break;
                        }
                    }
                }

                // update powerup spawning
                if (Network.IsServer && PowerupSpawnsForThisMinigame.Count > 0)
                {
                    PowerupSpawnTimerForThisRound += Time.DeltaTime;
                    var powerupCooldown = 5f;
                    powerupCooldown *= AOMath.Lerp(1f, 0.5f, (float)Math.Clamp((float)PlayersInCurrentMinigame.Count / 10, 0, 1));
                    if (PowerupSpawnTimerForThisRound > powerupCooldown)
                    {
                        PowerupSpawnTimerForThisRound = 0;

                        Vector2 spawn;
                        int iters = 10;
                        bool goodSpawn = true;
                        do {
                            var index = (int)RNG.RangeInt(ref GlobalRng, 0, (int)MathF.Min(3, PowerupSpawnsForThisMinigame.Count-1));
                            spawn = PowerupSpawnsForThisMinigame[index];
                            PowerupSpawnsForThisMinigame.RemoveAt(index); // ordered remove
                            PowerupSpawnsForThisMinigame.Add(spawn);
                            goodSpawn = true;
                            foreach (var powerup in Scene.Components<PowerupBox>())
                            {
                                if ((powerup.Position - spawn).LengthSquared < 0.001f)
                                {
                                    goodSpawn = false;
                                    break;
                                }
                            }
                        }
                        while (goodSpawn == false && iters --> 0);

                        if (goodSpawn)
                        {
                            var powerupOptions = new List<PowerupKind>();
                            for (int i = (int)PowerupKind.FirstPowerup; i < (int)PowerupKind.Count; i++)
                            {
                                if (GetPowerupMinigameKind((PowerupKind)i) == CurrentMinigame.Kind)
                                {
                                    powerupOptions.Add((PowerupKind)i);
                                }
                            }
                            
                            if (powerupOptions.Count > 0)
                            {
                                var kind = powerupOptions[(int)RNG.RangeInt(ref GlobalRng, 0, powerupOptions.Count-1)];
                                var powerup = Assets.GetAsset<Prefab>("PowerupBox.prefab").Instantiate<PowerupBox>();
                                powerup.PowerupKind = kind;
                                powerup.Entity.Position = spawn;
                                Network.Spawn(powerup.Entity);
                            }
                        }
                    }
                }

                // Draw leaderboard UI
                if (Network.IsClient)
                {
                    var sortedPlayers = CurrentMinigame.SortPlayers(PlayersInCurrentMinigame);
                    var leaderboardData = new List<PlayerLeaderboardData>();

                    // Update target positions for all players
                    foreach (var player in sortedPlayers)
                    {
                        if (!player.Alive()) continue;
                        if (player.IsFrontman) continue;
                        player.TargetLeaderboardYOffset = leaderboardData.Count * LEADERBOARD_ENTRY_HEIGHT;  // Target is based on position in sorted list
                        player.CurrentLeaderboardYOffset = AOMath.Lerp(player.CurrentLeaderboardYOffset, player.TargetLeaderboardYOffset, 20f * Time.DeltaTime);
                        leaderboardData.Add(new PlayerLeaderboardData(
                            player.Name,
                            CurrentMinigame.GetPlayerScore(player),
                            player.CurrentLeaderboardYOffset,
                            player.TargetLeaderboardYOffset
                        ));
                    }

                    LeaderboardUI.DrawLeaderboard(leaderboardData, CurrentMinigame.LeaderboardPointsHeader(), Network.LocalPlayer.Name);
                }

                CurrentMinigame.MinigameTick();
                break;
            }
            case GameState.EndGameDelay:
            {
                if (justEnteredState)
                {
                    SFX.Play(Assets.GetAsset<AudioAsset>("sfx/game_end_v2.wav"), new SFX.PlaySoundDesc(){Volume=0.5f});
                    StopCurrentTheme();
                }

                const float HitTime = 0.529f;
                if (timeInCurrentState >= HitTime)
                {
                    if (PrevTimeInState < HitTime)
                    {
                        SFX.Restart(MainThemeSFX);
                        MainThemeCurrentVolume = MainThemeMaxVolume;
                    }
                    MainThemeTargetVolume = MainThemeMaxVolume;
                }

                FullscreenFaderTarget = 0.0f;

                if (FrontmanEnabled == false && MinigameTimerEnabled)
                {
                    DrawTimer("Time Left", 0);
                }

                if (Network.IsServer)
                {
                    if (timeInCurrentState >= 2.0f)
                    {
                        CallClient_EndMinigame();
                        foreach (var comp in Scene.Components<DespawnOnMinigameEnd>())
                        {
                            Network.Despawn(comp.Entity);
                            comp.Entity.Destroy();
                        }
                        State = GameState.PassOrElimScreen;
                    }
                }
                break;
            }
            case GameState.PassOrElimScreen:
            {
                MainThemeTargetVolume = MainThemeMaxVolume;
                FullscreenFaderTarget = 0.5f;

                var localPlayer = (MyPlayer)Network.LocalPlayer;
                if (localPlayer.Alive())
                {
                    int passState = 0;
                    if (localPlayer.WasEliminatedThisRound)
                    {
                        passState = 1;
                    }
                    else if (localPlayer.IsEliminated || localPlayer.IsPartOfTheCurrentTournament == false)
                    {
                        passState = 2;
                    }
                    else
                    {
                        passState = 0;
                    }
                    DrawPassScreen(passState, timeInCurrentState, PrevTimeInState);
                }

                if (Network.IsServer)
                {
                    if (timeInCurrentState >= 4f)
                    {
                        State = GameState.PlayersLeftScreen;

                        if (IsPrivateServer == false) // no XP in private servers!
                        {
                            foreach (var player in PlayersInCurrentTournament) if (player.Alive())
                            {
                                if (player.IsEliminated == false)
                                {
                                    if (player.ServerAddXP(XPRewardForThisRound))
                                    {
                                        player.DidLevelUpThisMinigame.Set(true);
                                    }
                                }
                            }
                        }
                    }
                }
                break;
            }
            case GameState.PlayersLeftScreen:
            {
                MainThemeTargetVolume = MainThemeMaxVolume;

                FullscreenFaderTarget = 1;
                DrawPlayerGrid(PlayersInCurrentTournament, PrevTimeInState, timeInCurrentState, 4.5f, true);

                if (timeInCurrentState >= 3f)
                {
                    // preload the podium area
                    cutsceneCameraActive = true;
                    if (CutsceneCamera != null)
                    {
                        CutsceneCamera.Position = new Vector2(0, 0);
                    }
                }

                if (Network.IsServer)
                {
                    if (timeInCurrentState >= 5f)
                    {
                        var shouldDoAnotherRound = PlayersInCurrentTournament.Count(p => p.Alive() && p.IsEliminated == false) > 1;
                        if (shouldDoAnotherRound)
                        {
                            State = GameState.SetupNextGame;
                        }
                        else
                        {
                            CallClient_WinnersPodiumBegin((ulong)RNG.RangeInt(ref GlobalRng, 0, 100000));
                            State = GameState.WinnerPodiumScreen;
                        }
                    }
                }
                break;
            }
            case GameState.WinnerPodiumScreen:
            {
                ServerCanReceivePlayers = true;
                if (justEnteredState)
                {
                    UI.SetLeaderboardOpen(false);
                    UI.SetChatOpen(false);
                }
                SFX.SetLoopTimeout(PodiumSFX, 1f);

                cutsceneCameraActive = true;

                var bufferForLoading = 0.25f;
                if (timeInCurrentState < bufferForLoading)
                {
                    FullscreenFaderTarget = 1;
                }
                else
                {
                    timeInCurrentState -= bufferForLoading;
                    FullscreenFaderTarget = 0;
                    var cameraEaseDuration = 3f;
                    var maxStateTime = 5f;
                    if (timeInCurrentState >= (maxStateTime-1))
                    {
                        FullscreenFaderTarget = 1;
                    }

                    // var bar01 = Ease.OutQuart(Ease.T(timeInCurrentState, 0.5f));
                    DrawCinematicBlackBars(1);

                    {
                        var t = Ease.FadeInAndOut(0.2f, 0.2f, maxStateTime, timeInCurrentState);
                        var ts = GetTextSettings(70);
                        ts.Color = new Vector4(1, 0, 0, 1);
                        var rect = UI.ScreenRect.TopRect().Offset(0, 90);
                        rect = rect.Offset(0, -180 * t);
                        UI.Text(rect, $"{WinnerName} is the Winner!", ts);
                    }

                    if (CutsceneCamera != null)
                    {
                        var camera01 = Ease.T(timeInCurrentState, cameraEaseDuration);
                        CutsceneCamera.Position = Vector2.Lerp(CameraPanStart, CameraPanEnd, camera01);
                    }

                    if (Network.IsServer)
                    {
                        if (timeInCurrentState >= maxStateTime)
                        {
                            State = GameState.EndTournamentFadeInWait;
                        }
                    }
                }
                break;
            }
            case GameState.EndTournamentFadeInWait:
            {
                ServerCanReceivePlayers = true;
                if (justEnteredState)
                {
                    SFX.FadeOutAndStop(PodiumSFX, 1f);
                }

                FullscreenFaderTarget = 1;

                if (Network.IsServer)
                {
                    if (timeInCurrentState >= 1.25f)
                    {
                        CallClient_WinnersPodiumEnd();
                        foreach (var player in PlayersInCurrentTournament) if (player.Alive())
                        {
                            player.IsPartOfTheCurrentTournament.Set(false);
                            player.IsEliminated.Set(false);
                            player.WasEliminatedThisRound.Set(false);
                        }
                        bool ok = TrySetMinigameForThisServer(MinigameKind.None);
                        Util.Assert(ok);
                        State = GameState.WaitingForPlayers;

                        TournamentIsRunning.Set(false);
                    }
                }
                break;
            }
        }

        if (Network.IsServer)
        {
            ServerEnsureQueueingIsEnabled(ServerCanReceivePlayers);
            if (ServerCanReceivePlayers)
            {
                // remove all players from queueing
                foreach (var player in Scene.Components<MyPlayer>())
                {
                    if (player.IsQueuedForNewGame)
                    {
                        player.IsQueuedForNewGame.Set(false);
                        player.ServerSetQueuedForNewGame(false);
                    }
                }
            }
        }
        else
        {
            if (ServerCanReceivePlayers == false && Network.LocalPlayer.Alive())
            {
                var player = (MyPlayer)Network.LocalPlayer;

                var queueRect = UI.ScreenRect;
                queueRect.Min.Y = UI.SafeRect.Min.Y;
                queueRect = queueRect.BottomCenterRect().Offset(0, 100);
                if (player.HasEffect<SpectatorEffect>())
                {
                    if (player.IsQueuedForNewGame)
                    {
                        var timeWaited = Time.TimeSinceStartup - player.QueueStartTime;
                        UI.Text(queueRect.Offset(0, 55), $"Searching for game... ({timeWaited:F0}s)", GetTextSettings(40));
                        var ts = GetTextSettings(30);
                        var bs = new UI.ButtonSettings();
                        bs.Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_3.png");
                        if (UI.Button(queueRect.Grow(30, 70, 30, 70), "Cancel", bs, ts).JustPressed)
                        {
                            CallServer_RequestPlayerSetQueuedForNewGame(false);
                        }
                    }
                    else
                    {
                        UI.Text(queueRect.Offset(0, 55), "Queue for new game?", GetTextSettings(40));
                        var ts = GetTextSettings(30);
                        var bs = new UI.ButtonSettings();
                        bs.Sprite = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_2.png");
                        if (UI.Button(queueRect.Grow(30, 70, 30, 70), "Yes", bs, ts).JustPressed)
                        {
                            CallServer_RequestPlayerSetQueuedForNewGame(true);
                        }
                    }
                }
            }
        }

        MainThemeCurrentVolume = AOMath.MoveToward(MainThemeCurrentVolume, MainThemeTargetVolume, Time.DeltaTime * 0.25f);
        MainThemeSFXDesc.Volume = MainThemeCurrentVolume;
        SFX.UpdateSoundDesc(MainThemeSFX, MainThemeSFXDesc);

        PodiumSprite.LocalEnabled = State == GameState.WinnerPodiumScreen;

        SetCutsceneCameraActive(cutsceneCameraActive);
        if (CutsceneCamera != null)
        {
            CutsceneCamera.Zoom = cutsceneCameraZoom;
        }

        FullscreenFaderCurrent = AOMath.MoveToward(FullscreenFaderCurrent, FullscreenFaderTarget, Time.DeltaTime);
        if (FullscreenFaderCurrent > 0)
        {
            using var _1 = UI.PUSH_LAYER_RELATIVE(-10);
            DrawFullscreenFader(FullscreenFaderCurrent);
        }

        // Update and draw kill feed
        if (Network.IsClient)
        {
            // Update kill feed entries
            for (int i = KillFeedEntries.Count - 1; i >= 0; i--)
            {
                var entry = KillFeedEntries[i];

                // Update alpha (inlined from UpdateAlpha)
                float timeLeft = (entry.TimeCreated + KillFeedEntry.LIFETIME) - Time.TimeSinceStartup;
                if (timeLeft < KillFeedEntry.FADE_TIME)
                {
                    entry.Alpha = timeLeft / KillFeedEntry.FADE_TIME;
                }

                // Check expiry (inlined from IsExpired)
                if (Time.TimeSinceStartup > entry.TimeCreated + KillFeedEntry.LIFETIME)
                {
                    KillFeedEntries.RemoveAt(i);
                }
            }

            // Draw kill feed entries
            for (int i = 0; i < KillFeedEntries.Count; i++)
            {
                var entry = KillFeedEntries[i];

                // Calculate slide-in animation
                float slideT = Ease.T(Time.TimeSinceStartup - entry.TimeCreated, KillFeedEntry.SLIDE_IN_TIME);
                float slideAmount = Ease.OutBack(slideT);

                // Calculate base position with easing
                float baseOffset = -(250f + (i * KILL_FEED_ENTRY_HEIGHT));
                float xOffset = AOMath.Lerp(200, 0, slideAmount); // Start 200 pixels to the right and slide left

                // Start from the top center and grow/offset for each entry
                var rect = UI.ScreenRect.TopCenterRect()
                    .Grow(30, 200, 30, 200)  // Height of 60, width of 400
                    .Offset(xOffset, baseOffset);

                var textSettings = GetTextSettings(20, new Vector4(1, 1, 1, entry.Alpha));
                UI.Text(rect, $"{entry.PlayerName} {entry.Message}", textSettings);
            }
        }
    }

    public void StopCurrentTheme()
    {
        SFX.FadeOutAndStop(MinigameThemeSFX, 0.25f);
    }

    public void MuteCurrentTheme()
    {
        SFX.UpdateSoundDesc(MinigameThemeSFX, new SFX.PlaySoundDesc() { Volume = 0.0f, Loop = true, LoopTimeout = 1 });
    }

    public void UnmuteCurrentTheme()
    {
        SFX.UpdateSoundDesc(MinigameThemeSFX, new SFX.PlaySoundDesc() { Volume = 0.4f, Loop = true, LoopTimeout = 1 });
    }

    public void PlayTheme(string themeSong)
    {
        Log.Info($"Playing {themeSong}");
        MinigameThemeSFX = SFX.Play(Assets.GetAsset<AudioAsset>(themeSong), new SFX.PlaySoundDesc(){Volume=0.4f, Loop=true, LoopTimeout=1});
    }

    [ServerRpc]
    public void RequestPlayerSetQueuedForNewGame(bool request)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (player.Alive() == false) return;
        if (ServerCanReceivePlayers)
        {
            // the server they're in is already waiting for players! no need to requeue!
            return;
        }
        if (player.HasEffect<SpectatorEffect>() == false)
        {
            return;
        }
        player.ServerSetQueuedForNewGame(request);
    }

    public void ServerStartMinigame()
    {
        Util.Assert(Network.IsServer);
        ServerTimeCurrentMinigameStarted.Set(Time.TimeSinceStartup);
        CallClient_StartMinigame();
        State = GameState.RunningMinigame;
    }

    public override void LateUpdate()
    {
        if (State == GameState.RunningMinigame)
        {
            CurrentMinigame.MinigameLateTick();
        }

        if (FrontmanEnabled)
        {
            using var _1 = UI.PUSH_CONTEXT(UI.Context.SCREEN);
            var rect = UI.ScreenRect;
            rect.Max.X = UI.SafeRect.Max.X;
            rect = rect.RightCenterRect();
            rect = rect.Grow(40, 0, 40, 80);
            rect = rect.Offset(-10, 100);
            UI.Image(rect, Assets.GetAsset<Texture>("sprites/frontman_mask.png"));
            var ts = GetTextSettings(18);
            ts.WordWrap = true;
            UI.Text(rect.Offset(0, -40), "Frontman\nEnabled", ts);
        }

        // draw world text popups
        {
            using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
            using var _2 = UI.PUSH_LAYER(10);

            const float duration = 1.0f;
            var ts = GetTextSettings(0.5f);
            for (int i = TextPopups.Count-1; i >= 0; i -= 1)
            {
                var popup = TextPopups[i];
                popup.Time += Time.DeltaTime;
                if (popup.Time >= duration)
                {
                    TextPopups.UnorderedRemoveAt(i);
                    continue;
                }

                var t = Ease.OutQuart(Ease.T(popup.Time, duration));
                var a = 1f-Ease.InQuart(Ease.T(popup.Time, duration));
                var pos = Vector2.Lerp(popup.Position, popup.Position + new Vector2(0, 0.5f), t);

                ts.Color = popup.Color;
                ts.Color.W = a;
                var rect = new Rect(pos, pos);

                using var _321 = IM.PUSH_Z(popup.Position.Y);
                UI.Text(rect, popup.Text, ts);
            }
        }
    }

    [ClientRpc]
    public void FreezePlayers()
    {
        foreach (var player in PlayersInCurrentTournament) if (player.Alive())
        {
            player.AddEffect<FreezeEffect>();
        }
    }

    [ClientRpc]
    public void UnfreezePlayers()
    {
        foreach (var player in PlayersInCurrentTournament) if (player.Alive())
        {
            player.RemoveEffect<FreezeEffect>(false);
        }
    }

    [ClientRpc]
    public void WinnersPodiumBegin(ulong seed)
    {
        // UI.SetLeaderboardOpen(false);
        // UI.SetChatOpen(false);

        ulong rng = RNG.Seed(seed);
        Shuffle(PodiumLosers, ref rng);
        var loserSpawns = new List<Entity>(PodiumLosers);

        CameraPanStart = PodiumSprite.Position + new Vector2(0, -8);
        CameraPanEnd = PodiumSprite.Position;
        if (!Network.IsServer)
        {
            CutsceneCamera.Position = CameraPanStart;
        }

        WinnerName = "Player";
        foreach (var player in PlayersInCurrentTournament) if (player.Alive())
        {
            Entity spawn;
            if (player.IsEliminated)
            {
                spawn = loserSpawns.Pop();
            }
            else
            {
                WinnerName = player.Name;
                spawn = PodiumFirstPlace;
                PodiumSFX = SFX.Play(Assets.GetAsset<AudioAsset>("sfx/podium_loop.wav"), new SFX.PlaySoundDesc(){Volume=0.7f, Positional=true, Position=spawn.Position, Loop=true, LoopTimeout=1f});
            }
            player.ClearAllEffects();
            player.Teleport(spawn.Position);
            player.AddEffect<WinnerPodiumEffect>();
        }
    }

    [ClientRpc]
    public void WinnersPodiumEnd()
    {
        foreach (var player in Scene.Components<MyPlayer>())
        {
            player.ClearAllEffects();
            player.Teleport(Vector2.Zero);
        }
    }

    public void SetCutsceneCameraActive(bool active)
    {
        if (!Network.IsServer)
        {
            if (active)
            {
                CutsceneCamera.ControlLevel = 10;
            }
            else
            {
                CutsceneCamera.ControlLevel = -1;
            }
        }
    }

    public static void DrawTimer(string label, float timeLeft, float scale = 1f)
    {
        using var _1 = UI.PUSH_CONTEXT(UI.Context.SCREEN);
        using var _2 = UI.PUSH_LAYER(10);
        using var _3 = UI.PUSH_SCALE_FACTOR(UI.ScreenScaleFactor * scale);

        var seconds = (int)MathF.Ceiling(timeLeft);
        var textColor = Vector4.White;
        if (seconds <= 60)
        {
            float frac = timeLeft % 1.0f;
            textColor = Vector4.Lerp(Vector4.Red, Vector4.White, 1.0f-frac);
        }

        var ts = UI.TextSettings.Default;
        ts.WordWrap = false;
        ts.Size = 20;
        ts.Color = textColor;
        ts.VerticalAlignment = UI.VerticalAlignment.Center;
        ts.HorizontalAlignment = UI.HorizontalAlignment.Center;

        float height = 75f;
        Rect timerRect = UI.ScreenRect;
        timerRect.Max.Y = UI.SafeRect.Max.Y;
        timerRect = timerRect.TopCenterRect().Grow(0, 70, height, 70);
        if (GameManager.Instance.CurrentMinigame.Kind == MinigameKind.RedLightGreenLight)
        {
            timerRect = timerRect.Offset(220, -10);
        }
        var bgs = IM.GetNextSerial();
        var textRect = UI.Text(timerRect.Offset(0, -48), label, ts);
        IM.SetNextSerial(bgs);
        UI.Image(textRect.Grow(5, 7, 3, 7), null, new Vector4(0, 0, 0, 0.9f));

        UI.Image(timerRect, Assets.GetAsset<Texture>("sprites/timer.png"));

        var roundString = $"{(seconds / 60).ToString("D2")}:{(seconds % 60).ToString("D2")}";
        ts.Size = 40;
        UI.Text(timerRect.Offset(0, 0), roundString, ts);
    }

    // [UIPreview]
    public static void DrawTimerTest(Rect rect)
    {
        DrawTimer("Find a Room!", 60);
    }

    [ClientRpc]
    public void UpdateTournamentPlayerList(byte[] playerBytes)
    {
        PlayersInCurrentTournament = ReadListOfNetworkedComponentsFromBytes<MyPlayer>(playerBytes);
    }

    [ClientRpc]
    public void SetupMinigame(byte[] playerBytes)
    {
        PowerupSpawnsForThisMinigame.Clear();
        if (CurrentMinigame.PowerupSpawnParent.Alive())
        {
            foreach (var spawn in CurrentMinigame.PowerupSpawnParent.Children)
            {
                PowerupSpawnsForThisMinigame.Add(spawn.Position);
            }
            Shuffle(PowerupSpawnsForThisMinigame, ref GlobalRng);
        }

        PlayersInCurrentMinigame = ReadListOfNetworkedComponentsFromBytes<MyPlayer>(playerBytes);

        // Sort players and set their leaderboard offsets
        var sortedPlayers = CurrentMinigame.SortPlayers(PlayersInCurrentMinigame);
        for (int i = 0; i < sortedPlayers.Count; i++)
        {
            var player = sortedPlayers[i];
            player.CurrentLeaderboardYOffset = i * LEADERBOARD_ENTRY_HEIGHT;
            player.TargetLeaderboardYOffset = i * LEADERBOARD_ENTRY_HEIGHT;
        }

        CurrentMinigame.MinigameSetup();
        FreezePlayers();

        foreach (var player in PlayersInCurrentTournament) if (player.Alive())
        {
            if (player.IsEliminated)
            {
                player.AddEffect<SpectatorEffect>();
            }
        }
    }

    [ClientRpc]
    public void StartMinigame()
    {
        UnfreezePlayers();
        PowerupSpawnTimerForThisRound = 0;
        CurrentMinigame.MinigameStart();
    }

    public int CalculatePlayerCountToEliminateThisRound()
    {
        // int[] EliminationCounts = new int[20]
        // {
        //     0, // 0  players
        //     0, // 1  players
        //     1, // 2  players
        //     1, // 3  players
        //     2, // 4  players
        //     2, // 5  players
        //     2, // 6  players
        //     2, // 7  players
        //     3, // 8  players
        //     3, // 9  players
        //     3, // 10 players
        //     4, // 11 players
        //     4, // 12 players
        //     4, // 13 players
        //     5, // 14 players
        //     5, // 15 players
        //     5, // 16 players
        //     5, // 17 players
        //     5, // 18 players
        //     5, // 19 players
        // };
        // return EliminationCounts.IndexClamped(PlayersInCurrentMinigame.Count);

        var count = CurrentRoundIndex + 1;
        if (count >= PlayersInCurrentMinigame.Count)
        {
            count = PlayersInCurrentMinigame.Count - 1;
        }
        return count;
    }

    public void ServerEndMinigame()
    {
        Util.Assert(Network.IsServer);
        GameManager.Instance.State = GameState.EndGameDelay;
        ServerClearAllPlayerPowerups();
    }

    public void ServerClearAllPlayerPowerups()
    {
        Util.Assert(Network.IsServer);
        foreach (var player in PlayersInCurrentTournament) if (player.Alive())
        {
            player.ServerClearPowerups();
        }
    }

    [ClientRpc]
    public void EndMinigame()
    {
        var results = CurrentMinigame.SortPlayers(GameManager.Instance.PlayersInCurrentMinigame.ToList());
        CurrentMinigame.MinigameEnd();

        foreach (var player in PlayersInCurrentTournament) if (player.Alive())
        {
            if (player.IsEliminated == false)
            {
                player.ClearAllEffects();
            }
            player.InputOverride = default;
        }

        if (FrontmanEnabled == false)
        {
            if (Network.IsServer)
            {
                var playersToEliminate = CalculatePlayerCountToEliminateThisRound();
                for (int i = 0; i < playersToEliminate; i++)
                {
                    var index = results.Count-1-i;
                    if (index == 0)
                    {
                        // dont eliminate the only player left!
                        break;
                    }

                    var player = results[index];
                    if (player.Alive())
                    {
                        player.ServerEliminate();
                    }
                }
            }
        }

        FreezePlayers();
    }

    public bool TrySetMinigameForThisServer(MinigameKind kind)
    {
        Util.Assert(Network.IsServer);
        if (kind == MinigameKind.None)
        {
            CurrentMinigameSyncVar.Set(null);
            return true;
        }

        var minigame = TryGetMinigameInstance(kind);
        if (minigame.Alive() == false)
        {
            Log.Error("Unknown minigame " + kind);
            CurrentMinigameSyncVar.Set(null);
            return false;
        }

        CurrentMinigameSyncVar.Set(minigame.Entity);
        return true;
    }

    public MinigameInstance TryGetMinigameInstance(MinigameKind kind)
    {
        foreach (var minigame in Scene.Components<MinigameInstance>())
        {
            if (minigame.Kind == kind)
            {
                return minigame;
            }
        }
        return null;
    }

    public void EnableMinigameTimer(int time)
    {
        if (Network.IsServer)
        {
            MinigameTimer = time;
            MinigameTimerEnabled = true;
        }
    }

    public void RunChatCommand(Player p, string command)
    {
        MyPlayer player = (MyPlayer)p;
        var isAdmin = player.IsAdmin || Game.LaunchedFromEditor;
        if (player.UserId == "65976031d3af49fc5eca9b3f") isAdmin = true; // Ian
        if (player.UserId == "65cd083718c6f2983c66f9d3") isAdmin = true; // Biffle
        if (player.UserId == "66ce0e8baae1b31a8f7768b8") isAdmin = true; // Kate
        if (player.UserId == "65ca67c00d4a1ed143081960") isAdmin = true; // Zud
        if (player.UserId == "65976449148672d9e03145c9") isAdmin = true; // Pat
        if (player.UserId == "65cd086d472b41412560f395") isAdmin = true; // Sigils
        if (player.UserId == "668d8f7536ce6a5ad8f3e7ba") isAdmin = true; // Nico

        var allowCommands = player.IsAdmin || Network.ServerPrivateInstanceHostId() == player.UserId;

        if (!allowCommands)
        {
            Chat.SendMessage(player, "You are not allowed to send chat commands.");
            return;
        }

        var parts = command.Split(' ');
        var cmd = parts[0].ToLowerInvariant();
        switch (cmd)
        {
            case "?":
            {
                Chat.SendMessage(p, "\nChat Commands:\n"+
                "/? : print this help message\n"+
                "/frontman [<player name> | off] : Set <player name> as Frontman or disable Frontman Mode.\n"+
                "/s[tart] : start round\n"+
                "");
                break;
            }
            case "xp":
            {
                if (isAdmin == false)
                {
                    return;
                }

                if (parts.Length != 2)
                {
                    Chat.SendMessage(player, "/xp needs a value as a parameter.");
                    break;
                }
                if (int.TryParse(parts[1], out var xp))
                {
                    player.ServerAddXP(xp);
                }
                break;
            }
            case "end":
            {
                if (isAdmin == false)
                {
                    return;
                }

                ServerEndMinigame();
                break;
            }
            case "f":
            case "fast":
            {
                if (isAdmin == false)
                {
                    return;
                }

                FastMode = true;
                goto case "s";
            }
            case "s":
            case "start":
            {
                if (State == GameState.WaitingForPlayers)
                {
                    State = GameState.StartTournament;
                }
                break;
            }
            case "frontman":
            {
                if (parts.Length != 2)
                {
                    Chat.SendMessage(player, "Usage:\n/frontman me : set self as Frontman\n/frontman <name> : set <name> as Frontman\n/frontman off : disable frontman mode");
                    break;
                }

                foreach (var other in Scene.Components<MyPlayer>())
                {
                    other.IsFrontman.Set(false);
                }
                FrontmanEnabled.Set(false);

                if (parts[1] == "off")
                {
                    Chat.SendMessage(null, "Frontman mode disabled.");
                }
                else
                {
                    MyPlayer playerToSetAsFrontman = null;
                    if (parts[1] == "me")
                    {
                        playerToSetAsFrontman = player;
                    }
                    else
                    {
                        foreach (var other in Scene.Components<MyPlayer>())
                        {
                            if (other.Name == parts[1])
                            {
                                playerToSetAsFrontman = other;
                                break;
                            }
                        }
                    }

                    if (playerToSetAsFrontman.Alive() == false)
                    {
                        Chat.SendMessage(player, $"Unknown player {parts[1]}. Frontman mode disabled.");
                    }
                    else
                    {
                        FrontmanEnabled.Set(true);
                        playerToSetAsFrontman.IsFrontman.Set(true);
                        Chat.SendMessage(null, $"Frontman mode enabled. {playerToSetAsFrontman.Name} is the Frontman.");
                    }
                }

                break;
            }
            case "god":
            {
                if (isAdmin == false)
                {
                    return;
                }

                if (parts.Length == 1 || parts[1] == "on")
                {
                    RunChatCommand(p, "noclip on");
                    RunChatCommand(p, "zoom 3");
                    RunChatCommand(p, "sp 3");
                }
                else
                {
                    RunChatCommand(p, "noclip off");
                    RunChatCommand(p, "zoom 1");
                    RunChatCommand(p, "sp 1");
                }
                break;
            }
            case "noclip":
            {
                if (isAdmin == false)
                {
                    return;
                }

                if (parts.Length == 1 || parts[1] == "on")
                {
                    CallClient_Noclip(player, true);
                }
                else
                {
                    CallClient_Noclip(player, false);
                }
                break;
            }
            case "z":
            case "zoom":
            {
                if (isAdmin == false)
                {
                    return;
                }

                if (parts.Length != 2)
                {
                    Chat.SendMessage(player, "/zoom needs a zoom value as a parameter.");
                    break;
                }
                if (float.TryParse(parts[1], out var z))
                {
                    player.CheatZoomMultiplier.Set(z);
                }
                break;
            }
            case "sp":
            case "speed":
            {
                if (isAdmin == false)
                {
                    return;
                }

                if (parts.Length != 2)
                {
                    Chat.SendMessage(player, "/speed needs a speed value as a parameter.");
                    break;
                }
                if (float.TryParse(parts[1], out var s))
                {
                    player.CheatSpeedMultiplier.Set(s);
                }
                break;
            }
        }
    }

    public bool ServerEndMinigameIfEnoughPlayersArePermadead()
    {
        if (FrontmanEnabled)
        {
            return false;
        }

        var countOfPermadeadPlayers = 0;
        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame)
        {
            // note(josh): intentionally not checking alive here. if somebody dies and then leaves we want to count them as dead
            if (player.IsDead && player.MinigameLivesLeft <= 0)
            {
                countOfPermadeadPlayers += 1;
            }
        }

        var playersToEliminate = GameManager.Instance.CalculatePlayerCountToEliminateThisRound();
        if (countOfPermadeadPlayers >= playersToEliminate)
        {
            if (playersToEliminate == 0 && countOfPermadeadPlayers == 0)
            {
                return false;
            }

            GameManager.Instance.ServerEndMinigame();
            return true;
        }

        return false;
    }

    [ClientRpc]
    public void Noclip(MyPlayer player, bool on)
    {
        player.RemoveEffect<NoClipEffect>(false);
        if (on)
        {
            player.AddEffect<NoClipEffect>();
        }
    }

    public static string GetPowerupName(PowerupKind kind)
    {
        // :PowerupSwitch
        switch (kind)
        {
            case PowerupKind.Invalid:                return "Invalid";
            case PowerupKind.RLGL_Shield:            return "Shield";
            case PowerupKind.RLGL_LightningDash:     return "Lightning Dash";
            case PowerupKind.RLGL_PigRider:          return "Pig Rider";
            case PowerupKind.RLGL_IronGolem:         return "Iron Golem";
            case PowerupKind.RLGL_Snowboard:         return "Snowboard";
            case PowerupKind.RLGL_IcePatch:          return "Ice Patch";
            case PowerupKind.RLGL_DiscoballLauncher: return "Discoball Launcher";
            case PowerupKind.RLGL_Stampede:          return "Stampede";
            case PowerupKind.RLGL_BananaPeel:        return "Banana Peel";
            case PowerupKind.RLGL_GlueBomb:          return "Glue Bomb";
        }
        return "Invalid";
    }

    public static MinigameKind GetPowerupMinigameKind(PowerupKind kind)
    {
        // :PowerupSwitch
        switch (kind)
        {
            case PowerupKind.Invalid:                return MinigameKind.None;
            case PowerupKind.RLGL_Shield:            return MinigameKind.RedLightGreenLight;
            case PowerupKind.RLGL_LightningDash:     return MinigameKind.RedLightGreenLight;
            case PowerupKind.RLGL_PigRider:          return MinigameKind.RedLightGreenLight;
            case PowerupKind.RLGL_IronGolem:         return MinigameKind.RedLightGreenLight;
            case PowerupKind.RLGL_Snowboard:         return MinigameKind.RedLightGreenLight;
            case PowerupKind.RLGL_IcePatch:          return MinigameKind.RedLightGreenLight;
            case PowerupKind.RLGL_DiscoballLauncher: return MinigameKind.RedLightGreenLight;
            //case PowerupKind.RLGL_Stampede:          return MinigameKind.RedLightGreenLight; Not implemented
            case PowerupKind.RLGL_BananaPeel:        return MinigameKind.RedLightGreenLight;
            case PowerupKind.RLGL_GlueBomb:          return MinigameKind.RedLightGreenLight;
            
            case PowerupKind.HP_LightningDash:       return MinigameKind.HotPotato;
            case PowerupKind.HP_Reverse:             return MinigameKind.HotPotato;
            case PowerupKind.HP_AirMail:             return MinigameKind.HotPotato;
            case PowerupKind.HP_MoreTime:            return MinigameKind.HotPotato;
            case PowerupKind.HP_ShadowDecoy:         return MinigameKind.HotPotato;
            case PowerupKind.HP_TimeOut:             return MinigameKind.HotPotato;
            case PowerupKind.HP_OilSpill:            return MinigameKind.HotPotato;
            //case PowerupKind.HP_SpringGlove:         return MinigameKind.HotPotato; Not implemented
            //case PowerupKind.HP_MagnetTrap:          return MinigameKind.HotPotato;
            
            case PowerupKind.BP_ChainLightning:      return MinigameKind.BalloonPop;
            case PowerupKind.BP_ColorBomb:           return MinigameKind.BalloonPop;
            //case PowerupKind.BP_BalloonMagnet:       return MinigameKind.BalloonPop; Not implemented
            case PowerupKind.BP_MiniTornado:         return MinigameKind.BalloonPop;
            case PowerupKind.BP_LockedIn:            return MinigameKind.BalloonPop;
            case PowerupKind.BP_DecoyBalloon:        return MinigameKind.BalloonPop;
            //case PowerupKind.BP_AnvilTrap:           return MinigameKind.BalloonPop; Not implemented
            //case PowerupKind.BP_EMP:                 return MinigameKind.BalloonPop; Not implemented
            case PowerupKind.BP_BalloonNuke:         return MinigameKind.BalloonPop;
            
            //case PowerupKind.Mingle_Lasso:           return MinigameKind.Mingle; Not implemented
            case PowerupKind.Mingle_Bat:             return MinigameKind.Mingle;
            //case PowerupKind.Mingle_GlueGun:         return MinigameKind.Mingle; Not implemented
            //case PowerupKind.Mingle_FryingPan:       return MinigameKind.Mingle; Not implemented
            //case PowerupKind.Mingle_CoolStick:       return MinigameKind.Mingle; Not implemented
            //case PowerupKind.Mingle_LemonBlaster:    return MinigameKind.Mingle; Not implemented
            case PowerupKind.Mingle_Snowboard:        return MinigameKind.Mingle;
            case PowerupKind.Mingle_GlueBomb:          return MinigameKind.Mingle;
        }
        return MinigameKind.None;
    }

    public static void Shuffle<T>(List<T> list, ref ulong rng)
    {
        if (list.Count < 2) return;
        for (int i = 0; i < list.Count; i++)
        {
            int j = (int)RNG.RangeInt(ref rng, 0, list.Count-1);
            var tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }
}

public enum MinigameKind
{
    None,
    RedLightGreenLight,
    CirclePush,
    BalloonPop,
    Mingle,
    HotPotato,
    // DeadlyRace,

    Count,

    FirstMinigame = MinigameKind.None + 1,
    LastMinigame = MinigameKind.Count - 1,
}

public enum GameState
{
    WaitingForPlayers,

    StartTournament,
    ShowPlayersBeforeTournament,

    SetupNextGame,
    NextGameScreen,
    StartGameFadeIn,
    RunningMinigame,
    EndGameDelay,
    PassOrElimScreen,
    PlayersLeftScreen,

    WinnerPodiumScreen,
    EndTournamentFadeInWait,
}

public abstract partial class MinigameInstance : Component
{
    [Serialized] public MinigameKind Kind;
    [Serialized] public string NiceName;
    [Serialized] public string Description;
    [Serialized] public string ThemeSong;
    [Serialized] public Entity PowerupSpawnParent;
    [Serialized] public float GameplayZoomLevel = 1f;
    [Serialized] public Entity IntroLerpStart;
    [Serialized] public Entity IntroLerpEnd;
    [Serialized] public float IntroLerpCameraZoom = 1.35f;
    [Serialized] public Entity PlayerSpawnsParent;

    public abstract bool ControlsRespawning();
    public abstract void MinigameSetup();
    public abstract void MinigameStart();
    public abstract void MinigameTick();
    public abstract void MinigameLateTick();
    public abstract void MinigameEnd();
    public virtual List<MyPlayer> SortPlayers(List<MyPlayer> players) => players;
    public virtual int GetPlayerScore(MyPlayer player) => 0;
    public virtual string LeaderboardPointsHeader() => "Points";
}

public class DespawnOnMinigameEnd : Component
{
}

// note(josh): serialized, do not change order!!!
public enum PowerupKind
{
    Invalid = 0,

    RLGL_Shield            = 1, FirstPowerup = RLGL_Shield,
    RLGL_LightningDash     = 2,
    RLGL_PigRider          = 3,
    RLGL_IronGolem         = 4,
    RLGL_Snowboard         = 5,
    RLGL_IcePatch          = 6,
    RLGL_DiscoballLauncher = 7,
    RLGL_Stampede          = 8,
    RLGL_BananaPeel        = 9,
    RLGL_GlueBomb          = 10,
    
    HP_LightningDash       = 11,
    HP_Reverse             = 12,
    HP_AirMail             = 13,
    HP_MoreTime            = 14,
    HP_ShadowDecoy         = 15,
    HP_TimeOut             = 16,
    HP_OilSpill            = 17,
    HP_SpringGlove         = 18,
    HP_MagnetTrap          = 19,
    
    BP_ChainLightning      = 20,
    BP_ColorBomb           = 21,
    BP_BalloonMagnet       = 22,
    BP_MiniTornado         = 23,
    BP_LockedIn            = 24,
    BP_DecoyBalloon        = 25,
    BP_AnvilTrap           = 26,
    BP_EMP                 = 27,
    BP_BalloonNuke         = 28,
    
    Mingle_Lasso           = 29,
    Mingle_Bat             = 30,
    Mingle_GlueGun         = 31,
    Mingle_FryingPan       = 32,
    Mingle_CoolStick       = 33,
    Mingle_LemonBlaster    = 34,
    Mingle_Snowboard       = 35,
    Mingle_GlueBomb        = 36,

    Count,
}

public class PowerupDefinition
{
    public PowerupKind Kind;
    public MinigameKind Minigame;

    public string Name;
    public string Description;

    public Type AbilityType;

    public Texture Icon;
}

public class TextPopup
{
    public string Text;
    public Vector4 Color;
    public float Time;
    public Vector2 Position;
}

public struct PlayerLeaderboardData
{
    public string Name;
    public int Points;
    public float CurrentYOffset;
    public float TargetYOffset;

    public PlayerLeaderboardData(string name, int points, float currentYOffset = 0, float targetYOffset = 0)
    {
        Name = name;
        Points = points;
        CurrentYOffset = currentYOffset;
        TargetYOffset = targetYOffset;
    }
}

public static class LeaderboardUI
{
    public const float TransitionDuration = 0.5f;

    // [UIPreview]
    public static void DrawLeaderboardTest()
    {
        var mockPlayers = new List<PlayerLeaderboardData>
        {
            new PlayerLeaderboardData("Player 1", 100, 0, 0),
            new PlayerLeaderboardData("You", 75, GameManager.LEADERBOARD_ENTRY_HEIGHT, GameManager.LEADERBOARD_ENTRY_HEIGHT),
            new PlayerLeaderboardData("Player 3", 50, GameManager.LEADERBOARD_ENTRY_HEIGHT * 2, GameManager.LEADERBOARD_ENTRY_HEIGHT * 2),
            new PlayerLeaderboardData("Player 4", 25, GameManager.LEADERBOARD_ENTRY_HEIGHT * 3, GameManager.LEADERBOARD_ENTRY_HEIGHT * 3),
            new PlayerLeaderboardData("Player 5", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 4, GameManager.LEADERBOARD_ENTRY_HEIGHT * 4),
            new PlayerLeaderboardData("Player 6", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 5, GameManager.LEADERBOARD_ENTRY_HEIGHT * 5),
            new PlayerLeaderboardData("Player 7", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 6, GameManager.LEADERBOARD_ENTRY_HEIGHT * 6),
            new PlayerLeaderboardData("Player 8", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 7, GameManager.LEADERBOARD_ENTRY_HEIGHT * 7),
            new PlayerLeaderboardData("Player 9", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 8, GameManager.LEADERBOARD_ENTRY_HEIGHT * 8),
            new PlayerLeaderboardData("Player 10", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 9, GameManager.LEADERBOARD_ENTRY_HEIGHT * 9),
            new PlayerLeaderboardData("Player 11", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 10, GameManager.LEADERBOARD_ENTRY_HEIGHT * 10),
            new PlayerLeaderboardData("Player 12 with very long name", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 11, GameManager.LEADERBOARD_ENTRY_HEIGHT * 11),
            new PlayerLeaderboardData("Player 13", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 12, GameManager.LEADERBOARD_ENTRY_HEIGHT * 12),
            new PlayerLeaderboardData("Player 14", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 13, GameManager.LEADERBOARD_ENTRY_HEIGHT * 13),
            new PlayerLeaderboardData("Player 15", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 14, GameManager.LEADERBOARD_ENTRY_HEIGHT * 14),
            new PlayerLeaderboardData("Player 16", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 15, GameManager.LEADERBOARD_ENTRY_HEIGHT * 15),
            new PlayerLeaderboardData("Player 17", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 16, GameManager.LEADERBOARD_ENTRY_HEIGHT * 16),
            new PlayerLeaderboardData("Player 18", 0, GameManager.LEADERBOARD_ENTRY_HEIGHT * 17, GameManager.LEADERBOARD_ENTRY_HEIGHT * 17),
        };

        DrawLeaderboard(mockPlayers, "Test Points", "You");
    }

    public static void DrawLeaderboard(List<PlayerLeaderboardData> players, string headerText, string localPlayerName = null)
    {
        var leaderboardRect = UI.SafeRect.LeftRect().Grow(0, 250, 400, 0);

        using var _ = UI.PUSH_LAYER(-10);

        // Draw header
        var headerRect = leaderboardRect.CutTop(GameManager.LEADERBOARD_ENTRY_HEIGHT);
        var headerSettings = GameManager.GetTextSettings(30, UI.HorizontalAlignment.Center);
        headerSettings.Color = new Vector4(1, 1, 1, 1);
        headerSettings.Offset = new Vector2(0, 2);
        UI.Text(headerRect, headerText, headerSettings);

        var textSettings = GameManager.GetTextSettings(22, UI.HorizontalAlignment.Left);
        textSettings.Color = new Vector4(1, 1, 1, 1);
        textSettings.Offset = new Vector2(0, 2);

        // Calculate base positions for entries
        var baseRect = leaderboardRect.CutTop(GameManager.LEADERBOARD_ENTRY_HEIGHT);

        // Define alternating background colors
        var bgColor1 = new Vector4(0.2f, 0.2f, 0.2f, 0.8f); // Slightly lighter
        var bgColor2 = new Vector4(0.1f, 0.1f, 0.1f, 0.8f);    // Slightly darker

        // Draw entries with lerped positions
        for (int i = 0; i < players.Count; i++)
        {
            var player = players[i];

            // Calculate entry rectangle based on lerped position
            var entryRect = baseRect.Offset(0, -player.CurrentYOffset);

            // Draw alternating background first
            UI.Image(entryRect, null, i % 2 == 0 ? bgColor1 : bgColor2);

            // Draw special background colors - check last place first, then medals
            int elimPlayersStart = players.Count - GameManager.Instance.CalculatePlayerCountToEliminateThisRound();
            if (GameManager.Instance.FrontmanEnabled == false && players.Count > 1 && i >= elimPlayersStart)
            {
                UI.Image(entryRect, null, new Vector4(1f, 0.3f, 0.3f, 0.5f));  // Red background for last place
            }
            else if (i < 3)  // Medal colors for top 3 (if not in last place)
            {
                Vector4 medalColor = i switch
                {
                    0 => new Vector4(1.0f, 0.84f, 0.0f, 0.5f),    // Gold - brighter yellow
                    1 => new Vector4(0.85f, 0.85f, 0.9f, 0.5f),   // Silver - slightly blue-tinted silver
                    2 => new Vector4(0.87f, 0.45f, 0.23f, 0.5f),  // Bronze - more saturated bronze
                    _ => default
                };
                UI.Image(entryRect, null, medalColor);
            }

            // Set text color for local player
            var nameColor = localPlayerName != null && player.Name == localPlayerName
                ? new Vector4(0.3f, 1f, 0.3f, 1f)  // Bright green for local player
                : new Vector4(1, 1, 1, 1);         // White for others

            // Draw player name with appropriate color
            textSettings.Color = nameColor;
            textSettings.DoAutofit = true;
            textSettings.AutofitMinSize = 15;
            textSettings.AutofitMaxSize = textSettings.Size;
            var nameRect = entryRect.CutLeft(240).Inset(0, 0, 0, 4);  // 4 pixels left padding
            UI.Text(nameRect, player.Name, textSettings);

            // Draw score (always white)
            var scoreSettings = GameManager.GetTextSettings(30, UI.HorizontalAlignment.Right);
            scoreSettings.Color = new Vector4(1, 1, 1, 1);
            scoreSettings.Offset = new Vector2(0, 2);
            var scoreRect = entryRect.Inset(0, 4, 0, 0);  // 4 pixels right padding
            UI.Text(scoreRect, player.Points.ToString(), scoreSettings);
        }
    }
}

public partial class PowerupBox : Component
{
    [Serialized] public Sprite_Renderer Sprite;
    [Serialized] public Circle_Collider Collider;

    public PowerupKind PowerupKind;
    public float TimeSpawned;

    public bool AlreadyCollected;

    public override void Awake()
    {
        TimeSpawned = Time.TimeSinceStartup;
        UpdateSpawnAnim();

        Collider.OnCollisionEnter += (other) =>
        {
            if (other.Alive() == false) return;

            var player = other.GetComponent<MyPlayer>();
            if (player.Alive() == false) return;
            if (player.IsFrontman) return;

            if (Network.IsServer)
            {
                if (AlreadyCollected) return;

                if (player.ServerTryGivePowerup(PowerupKind))
                {
                    player.CallClient_DoPowerupAnimation((int)PowerupKind, rpcTarget: player);
                    AlreadyCollected = true;
                    Network.Despawn(Entity);
                    Entity.Destroy();
                }
            }
        };
    }

    public void UpdateSpawnAnim()
    {
        float a = Ease.T(Time.TimeSinceStartup - TimeSpawned, 0.15f);
        Sprite.Tint = new Vector4(1, 1, 1, a);

        var pos = Sprite.Entity.LocalPosition;
        pos.Y = 0.5f * (1f-a);
        Sprite.Entity.LocalPosition = pos;

        var scale = Sprite.Entity.LocalScale;
        scale.X = AOMath.Lerp(0.5f, 1f, a);
        scale.Y = AOMath.Lerp(1.5f, 1f, a);
        Sprite.Entity.LocalScale = scale;
    }

    public override void Update()
    {
        UpdateSpawnAnim();
    }
}

public partial class BananaPeel : Component
{
    [Serialized] public Spine_Animator Spine;

    public float DespawnTimer = 15f;

    public static BananaPeel Spawn(Vector2 position)
    {
        var instance = Entity.Instantiate(Assets.GetAsset<Prefab>("BananaPeel.prefab")).GetComponent<BananaPeel>();
        instance.Entity.Position = position;
        Network.Spawn(instance.Entity);
        return instance;
    }

    public override void Awake()
    {
        Spine.Awaken();
        Spine.SpineInstance.SetAnimation("Land", false);

        GetComponent<Circle_Collider>().OnCollisionEnter += entity =>
        {
            if (entity.Alive() == false) return;

            if (!Network.IsServer || !entity.TryGetComponent(out MyPlayer player) || !player.Alive() || player.IsDead)
                return;

            CallClient_SlipPlayer(player);
        };
    }

    public override void Update()
    {
        if (Network.IsServer)
        {
            if (DespawnTimer > 0f)
            {
                DespawnTimer -= Time.DeltaTime;
                if (DespawnTimer <= 0)
                {
                    Network.Despawn(Entity);
                    Entity.Destroy();
                }
            }
        }
    }

    [ClientRpc]
    public void SlipPlayer(MyPlayer player)
    {
        DespawnTimer = 2f;
        player.AddEffect<BananaPeelSlipEffect>();
    }
}

public class BananaPeelSlipEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override bool BlockAbilityActivation => true;
    public override bool FreezePlayer => true;
    public override bool DisableMovementInput => true;
    public override float DefaultDuration => 3f;

    private bool StartedGetUpAnim;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("slip");
    }

    public override void OnEffectUpdate()
    {
        if (Util.OneTime(DurationRemaining < 1.233f, ref StartedGetUpAnim))
        {
            Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("get_up");
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).ResetToInitialState();
    }
}

public partial class GlueTrap : Component
{
    [Serialized] public Spine_Animator Spine;
    private float Timer = 15f;

    public static GlueTrap Spawn(Vector2 position)
    {
        var instance = Assets.GetAsset<Prefab>("GluePool.prefab").Instantiate<GlueTrap>();
        instance.Entity.Position = position;
        Network.Spawn(instance.Entity);
        return instance;
    }

    public override void Awake()
    {
        Spine.Awaken();
        Spine.SpineInstance.SetAnimation("Splat_Loop", true);
    }

    public override void Update()
    {
        if (Network.IsServer)
        {
            Timer -= Time.DeltaTime;
            if (Timer < 0)
            {
                Network.Despawn(Entity);
                Entity.Destroy();
            }
        }

        foreach (var player in Scene.Components<MyPlayer>()) if (player.Alive() && player.IsDead == false)
        {
            if (Vector2.Distance(player.Position, Entity.Position) <= 1f)
            {
                var effect = player.GetEffect<GlueTrapEffect>();
                if (effect == null)
                {
                    effect = player.AddEffect<GlueTrapEffect>();
                }
                effect.DurationRemaining = 0.1f;
            }
        }
    }
}

public class GlueTrapEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public override void OnEffectStart(bool isDropIn) { }

    public override void OnEffectUpdate() { }

    public override void OnEffectEnd(bool interrupt) { }
}