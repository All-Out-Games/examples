using AO;

public partial class MinigameCirclePush : MinigameInstance
{
    public static MinigameCirclePush _instance;
    public static MinigameCirclePush Instance
    {
        get
        {
            if (_instance == null)
            {
                foreach (var component in Scene.Components<MinigameCirclePush>())
                {
                    _instance = component;
                    _instance.Awaken();
                    break;
                }
            }
            return _instance;
        }
    }

    [Serialized] public Prefab SpiderWebHazardPrefab;
    [Serialized] public Prefab LightningStrikeHazardPrefab;
    [Serialized] public Prefab DynamiteHazardPrefab;

    [Serialized] public Edge_Collider Edge;

    [Serialized] public Entity CrateSpawnsParent;

    [Serialized] public Spine_Animator[] WindmillSpines;
    [Serialized] public Spine_Animator[] WindSpines;

    public const float TimeBetweenCratesLo = 3;
    public const float TimeBetweenCratesHi = 6;

    public const float TimeBetweenHazardsLo = 2;
    public const float TimeBetweenHazardsHi = 3;

    public List<Entity> CrateSpawns = new();
    public float CrateSpawnTimer;
    public float HazardSpawnTimer;

    public SyncVar<bool> HazardsEnabled  = new(true);
    public SyncVar<bool> PowerupsEnabled = new(true);

    public SyncVar<bool> FrenzyEnabled = new();
    public float FrenzyEnabledTime;

    public float[] WindmillTimers;
    public ulong[] WindmillSFX;

    public Entity GetNextCrateSpawn()
    {
        // grab one of the first 5 to return. add it back to the end
        var index = (int)RNG.RangeInt(ref GameManager.Instance.GlobalRng, 0, 4);
        var result = CrateSpawns[index];
        CrateSpawns.RemoveAt(index);
        CrateSpawns.Add(result);
        return result;
    }

    public override void Awake()
    {
        foreach (var spawn in CrateSpawnsParent.Children)
        {
            CrateSpawns.Add(spawn);
            spawn.GetComponent<Sprite_Renderer>().LocalEnabled = false;
        }

        Edge.OnCollisionEnter += (other) =>
        {
            if (other.Alive() == false) return;

            if (GameManager.Instance.CurrentMinigame != this)
            {
                return;
            }

            if (Network.IsServer)
            {
                var player = other.GetComponent<MyPlayer>();
                if (player.Alive() == false) return;
                if (player.IsDead) return;
                if (player.IsFrontman) return;
                CallClient_KillPlayer(player, GetNextCrateSpawn().Position);
            }
        };

        WindmillTimers = new float[WindmillSpines.Length];
        WindmillSFX = new ulong[WindmillSpines.Length];
    }

    [ClientRpc]
    public void KillPlayer(MyPlayer player, Vector2 respawnPosition)
    {
        var e = player.AddEffect<CirclePushDeathEffect>();
        e.RespawnPosition = respawnPosition;
    }

    public override void MinigameSetup()
    {
        if (Network.IsServer)
        {
            HazardsEnabled.Set(true);
            PowerupsEnabled.Set(true);
            FrenzyEnabled.Set(false);
            GameManager.Shuffle(CrateSpawns, ref GameManager.Instance.GlobalRng);

            GameManager.Instance.EnableMinigameTimer(60 * 3);

            foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
            {
                player.UseLivesForMinigame.Set(true);
                player.MinigameLivesLeft.Set(3);
            }
        }

        foreach (var windmill in WindmillSpines)
        {
            windmill.LocalEnabled = false;
        }
        foreach (var windmill in WindSpines)
        {
            windmill.LocalEnabled = false;
        }
    }

    public void ResetCrateSpawnTimer()
    {
        CrateSpawnTimer = RNG.RangeFloat(ref GameManager.Instance.GlobalRng, TimeBetweenCratesLo, TimeBetweenCratesHi);
        float factor = AOMath.Lerp(1f, 0.25f, (float)Math.Clamp((float)GameManager.Instance.PlayersInCurrentMinigame.Count / 10f, 0, 1));
        CrateSpawnTimer *= factor;
    }

    public void ResetHazardSpawnTimer()
    {
        float hazardMultiplier;
        if (FrenzyEnabled)
        {
            hazardMultiplier = 0.5f;
        }
        else
        {
            // Timer starts at 180 (3 min) and counts down, so normalize based on that
            float normalizedTime = (180.0f - GameManager.Instance.MinigameTimer) / (60.0f * 2.0f);
            hazardMultiplier = AOMath.Lerp(1.0f, 0.75f, normalizedTime);
        }
        HazardSpawnTimer = RNG.RangeFloat(ref GameManager.Instance.GlobalRng, TimeBetweenHazardsLo, TimeBetweenHazardsHi) * hazardMultiplier;
    }

    public override void MinigameStart()
    {
        if (Network.IsServer)
        {
            ResetCrateSpawnTimer();
            ResetHazardSpawnTimer();
        }

        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
        {
            player.SmashMeter = 1.0f;
            player.TotalSmashDamage = 0;
        }
    }

    public void ServerEnableFrenzyMode()
    {
        if (FrenzyEnabled) return;
        Util.Assert(Network.IsServer);
        FrenzyEnabled.Set(true);
        CallClient_EnableFrenzyMode();
    }

    [ClientRpc]
    public void EnableFrenzyMode()
    {
        FrenzyEnabledTime = Time.TimeSinceStartup;
    }

    public override void MinigameTick()
    {
        if (Network.IsServer)
        {
            if (FrenzyEnabled == false && GameManager.Instance.MinigameTimer <= (60 * 1))
            {
                ServerEnableFrenzyMode();
            }

            if (PowerupsEnabled)
            {
                CrateSpawnTimer -= Time.DeltaTime;
                if (CrateSpawnTimer <= 0)
                {
                    ResetCrateSpawnTimer();

                    // iterate until we find a spawn that doesn't already have a crate on it. quit after N tries.
                    int iters = 10;
                    while (iters > 0)
                    {
                        iters -= 1;
                        var spawn = GetNextCrateSpawn().Position;
                        bool allGood = true;
                        foreach (var other in Scene.Components<CirclePushCrate>())
                        {
                            if ((other.Position - spawn).LengthSquared < 0.01f)
                            {
                                allGood = false;
                                break;
                            }
                        }

                        if (allGood)
                        {
                            var newCrate = Entity.Create();

                            var spineAnimator = newCrate.AddComponent<Spine_Animator>();
                            var crateComponent = newCrate.AddComponent<CirclePushCrate>();
                            crateComponent.Weapon = (CirclePushWeapon)RNG.RangeInt(ref GameManager.Instance.GlobalRng, (int)CirclePushWeapon.None + 1, (int)CirclePushWeapon.Count - 1);
                            newCrate.AddComponent<DespawnOnMinigameEnd>();

                            newCrate.Position = spawn;

                            Network.Spawn(newCrate);

                            break;
                        }
                    }
                }
            }

            if (HazardsEnabled)
            {
                HazardSpawnTimer -= Time.DeltaTime;
                if (HazardSpawnTimer <= 0)
                {
                    ResetHazardSpawnTimer();
                    var spawn = GetNextCrateSpawn();

                    switch (RNG.RangeInt(ref GameManager.Instance.GlobalRng, 0, 3))
                    {
                        case 0: // spider web
                        {
                            CirclePushSpiderWeb.Spawn(spawn.Position);
                            break;
                        }
                        case 1: // lightning
                        {
                            LightningStrike.Spawn(spawn.Position);
                            break;
                        }
                        case 2: // dynamite
                        {
                            CirclePushDynamite.Spawn(spawn.Position);
                            break;
                        }
                        case 3: // wind turbine
                        {
                            var windmillIndex = (int)RNG.RangeInt(ref GameManager.Instance.GlobalRng, 0, WindmillTimers.Length-1);
                            CallClient_StartWindmill(windmillIndex);
                            break;
                        }
                        case 4: // wrecking ball
                        {
                            // todo(josh): @Incomplete
                            break;
                        }
                    }
                }
            }

            GameManager.Instance.ServerEndMinigameIfEnoughPlayersArePermadead();
        }

        //
        {
            void DrawSlidingText(string text, UI.TextSettings ts, float time)
            {
                var t = Ease.FadeInAndOut(0.1f, 5.0f, time);
                if (t > 0)
                {
                    var pos01 = Ease.SlideInAndOut(0.1f, 5.0f, time);
                    var rect = UI.ScreenRect.CenterRect().Offset(0, 250);
                    UI.Text(rect.Offset(100 * pos01, 0), text, ts);
                }
            }

            var ts = GameManager.GetTextSettings(60);
            ts.Color = new Vector4(1, 0.2f, 0.2f, 1.0f);
            DrawSlidingText("Frenzy Mode - 2x Knockback!", ts, Time.TimeSinceStartup - FrenzyEnabledTime);
        }

        for (int i = 0; i < WindmillSpines.Length; i++)
        {
            if (WindmillTimers[i] > 0)
            {
                if (WindmillSpines[i].LocalEnabled == false)
                {
                    WindmillSpines[i].LocalEnabled = true;
                    WindmillSpines[i].SpineInstance.SetAnimation("turbine_idle_loop", true);
                    WindmillSpines[i].SpineInstance.Speed = 4;

                    WindSpines[i].LocalEnabled = true;
                    WindSpines[i].SpineInstance.SetAnimation("wind_tunnel", true);
                    WindSpines[i].SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, 0.5f);

                    WindmillSFX[i] = SFX.Play(Assets.GetAsset<AudioAsset>("sfx/pinmill_loop.wav"), new SFX.PlaySoundDesc() {
                        Volume = 0.7f,
                        Positional = true,
                        Position = WindmillSpines[i].Entity.Position,
                        Loop = true,
                        LoopTimeout = 1f,
                        RangeMultiplier = 2f
                    });
                }

                WindmillTimers[i] -= Time.DeltaTime;
                if (WindmillTimers[i] <= 0)
                {
                    LocalStopWindmill(i);
                }

                if (WindmillSpines[i].LocalEnabled)
                {
                    SFX.SetLoopTimeout(WindmillSFX[i], 1f);
                    foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
                    {
                        float xDistance = MathF.Abs(WindmillSpines[i].Position.X - player.Position.X);
                        if (xDistance < 2)
                        {
                            player.RemoveEffect<EmoteEffect>(true);
                            player.Impulse += new Vector2(0, -1) * 0.25f;
                        }
                    }
                }
            }
        }
    }

    public void LocalStopWindmill(int i)
    {
        SFX.Stop(WindmillSFX[i]);
        WindmillSpines[i].LocalEnabled = false;
        WindSpines[i].LocalEnabled = false;
    }

    public override void MinigameLateTick()
    {
    }

    [ClientRpc]
    public void StartWindmill(int index)
    {
        WindmillTimers[index] = 10.0f;
    }

    public override void MinigameEnd()
    {
        for (int i = 0; i < WindmillSpines.Length; i++)
        {
            LocalStopWindmill(i);
        }

        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
        {
            if (Network.IsServer)
            {
                player.IsDead.Set(false);
            }
        }
    }

    public override int GetPlayerScore(MyPlayer player)
    {
        return player.MinigameLivesLeft;
    }

    public override string LeaderboardPointsHeader()
    {
        return "Lives Left";
    }

    public override List<MyPlayer> SortPlayers(List<MyPlayer> players)
    {
        var sortedPlayers = new List<MyPlayer>(players);
        sortedPlayers.Sort((a, b) =>
        {
            return a.CompareLives(b);
        });
        return sortedPlayers;
    }

    public override bool ControlsRespawning() => false;
}

public enum CirclePushWeapon
{
    None,

    FeatherSword,
    BalloonSword,
    Chicken,
    Plunger,
    Bat,
    BoxingGlove,
    BanHammer,
    Flail,
    InfinityGauntlet,

    Count,
}

public class CirclePushCrate : Component
{
    public static float GetWeaponKnockback(CirclePushWeapon weapon)
    {
        switch (weapon)
        {
            case CirclePushWeapon.None:             return 1.0f;
            case CirclePushWeapon.FeatherSword:     return 1.1f;
            case CirclePushWeapon.BalloonSword:     return 1.2f;
            case CirclePushWeapon.Chicken:          return 1.3f;
            case CirclePushWeapon.Plunger:          return 1.4f;
            case CirclePushWeapon.Bat:              return 1.5f;
            case CirclePushWeapon.BoxingGlove:      return 1.6f;
            case CirclePushWeapon.BanHammer:        return 2.7f;
            case CirclePushWeapon.Flail:            return 1.8f;
            case CirclePushWeapon.InfinityGauntlet: return 1.9f;
        }
        return 1.0f;
    }

    public static string GetWeaponName(CirclePushWeapon weapon)
    {
        switch (weapon)
        {
            case CirclePushWeapon.FeatherSword:     return "Feather Sword";
            case CirclePushWeapon.BalloonSword:     return "Balloon Sword";
            case CirclePushWeapon.Chicken:          return "Chicken";
            case CirclePushWeapon.Plunger:          return "Plunger";
            case CirclePushWeapon.Bat:              return "Bat";
            case CirclePushWeapon.BoxingGlove:      return "Boxing Glove";
            case CirclePushWeapon.BanHammer:        return "Ban Hammer";
            case CirclePushWeapon.Flail:            return "Flail";
            case CirclePushWeapon.InfinityGauntlet: return "Infinity Gauntlet";
        }
        return "None";
    }

    [Serialized] public CirclePushWeapon Weapon;

    public Spine_Animator Animator;

    public override void Awake()
    {
        Animator = GetComponent<Spine_Animator>();
        Animator.Awaken();
        Animator.SpineInstance.SetSkeleton(Assets.GetAsset<SpineSkeletonAsset>("anims/crate/BAT003_crate.spine"));
        Animator.SpineInstance.SetSkin("default");
        Animator.SpineInstance.RefreshSkins();
        Animator.SpineInstance.SetAnimation("idle_loop", true);
    }
}

public class CirclePushPunchAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override float Cooldown => 1f;
    public override Texture Icon => Assets.GetAsset<Texture>("$AO/new/icons/ability icons/punch.png");

    public Component TryGetTarget()
    {
        float maxDistance = 1.5f;
        Component component = null;
        TryUpdateClosest<MyPlayer>(Player.Position, ref component, ref maxDistance, predicate: p => p.IsDead == false && p != Player && p.IsValidTarget && p.IsFrontman == false);
        TryUpdateClosest<CirclePushCrate>(Player.Position, ref component, ref maxDistance);
        return component;
    }

    public static bool TryUpdateClosest<T>(Vector2 position, ref Component componentResult, ref float maxDistance, Predicate<T> predicate = null) where T : Component
    {
        bool result = false;
        foreach (var thing in Scene.Components<T>())
        {
            var sqrDist = (thing.Position - position).LengthSquared;
            if (sqrDist < (maxDistance*maxDistance) && (predicate == null || predicate(thing)))
            {
                maxDistance = MathF.Sqrt(sqrDist);
                componentResult = thing;
                result = true;
            }
        }
        return result;
    }

    public override bool CanUse()
    {
        if (!base.CanUse()) return false;
        var target = TryGetTarget();
        if (target.Alive() == false) return false;
        return true;
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        var target = TryGetTarget();
        if (target.Alive())
        {
            string soundEffect = Player.CurrentCirclePushWeapon switch
            {
                CirclePushWeapon.FeatherSword => "sfx/sword_swing.wav",
                CirclePushWeapon.BalloonSword => "sfx/sword_swing.wav",
                CirclePushWeapon.Chicken => "sfx/chicken_sword.wav",
                CirclePushWeapon.Plunger => "sfx/sword_swing.wav",
                CirclePushWeapon.Bat => "sfx/sword_swing.wav",
                CirclePushWeapon.BoxingGlove => "sfx/sword_swing.wav",
                CirclePushWeapon.BanHammer => "sfx/sword_swing.wav",
                CirclePushWeapon.Flail => "sfx/sword_swing.wav",
                CirclePushWeapon.InfinityGauntlet => "sfx/sword_swing.wav",
                _ => "sfx/generic_punch.wav"
            };

            SFX.Play(Assets.GetAsset<AudioAsset>(soundEffect), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Player.Position });
            Player.AddEffect<CirclePushPunchEffect>(preInit: e => e.Target = target);
            return true;
        }
        return false;
    }
}

public partial class CirclePushPunchEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => false;

    public Component Target;

    public bool Hit;

    public override void OnEffectStart(bool isDropIn)
    {
        switch (Player.CurrentCirclePushWeapon)
        {
            case CirclePushWeapon.None:
            case CirclePushWeapon.InfinityGauntlet:
            {
                Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("punch");
                break;
            }
            case CirclePushWeapon.BoxingGlove:
            {
                Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("glove_punch");
                break;
            }
            case CirclePushWeapon.BanHammer:
            {
                Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("ban_hammer_swing");
                break;
            }
            default:
            {
                Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("attack");
                break;
            }
        }

        switch (Player.CurrentCirclePushWeapon)
        {
            case CirclePushWeapon.BoxingGlove:
            {
                DurationRemaining = Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByName("squid_AL").GetCurrentStateLength();
                break;
            }
            default:
            {
                DurationRemaining = Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).GetCurrentStateLength();
                break;
            }
        }
    }

    public override void OnEffectUpdate()
    {
        if (Util.OneTime(ElapsedTime >= 0.15f, ref Hit))
        {
            if (Target.Alive())
            {
                if (Target is MyPlayer target)
                {
                    target.SmashHit((target.Position - Player.Position).Normalized * CirclePushCrate.GetWeaponKnockback(Player.CurrentCirclePushWeapon) * 2.5f);
                }
                else if (Target is CirclePushCrate crate)
                {
                    if (Network.IsServer)
                    {
                        Player.CallClient_EquipTimedWeapon((int)crate.Weapon);
                        CallClient_DestroyCrateFX(crate);
                        Network.Despawn(crate.Entity);
                        crate.Entity.Destroy();
                    }
                }
            }
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }

    [ClientRpc]
    public static void DestroyCrateFX(CirclePushCrate crate)
    {
        if (crate.Alive())
            SFX.Play(Assets.GetAsset<AudioAsset>("sfx/crate_destroy.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = crate.Entity.Position });
    }
}

public class CirclePushWeaponEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public CirclePushWeapon Weapon;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.CurrentCirclePushWeapon = Weapon;
        Player.SetWeaponSkin(Weapon);
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SetWeaponSkin(CirclePushWeapon.None);
        Player.CurrentCirclePushWeapon = CirclePushWeapon.None;
    }
}

public class CirclePushSpiderWeb : Component
{
    [Serialized] public float TimeRemaining;
    public bool TriggeredFade;

    public Spine_Animator Animator;

    public static CirclePushSpiderWeb Spawn(Vector2 position)
    {
        var instance = Assets.GetAsset<Prefab>("SpiderWeb.prefab").Instantiate<CirclePushSpiderWeb>();
        instance.Entity.Position = position;
        Network.Spawn(instance.Entity);
        return instance;
    }

    public override void Awake()
    {
        if (Network.IsClient)
        {
            SFX.Play(Assets.GetAsset<AudioAsset>("sfx/web_spawn.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Entity.Position });
        }
        Animator = Entity.TryGetChildByIndex(0).GetComponent<Spine_Animator>();
        Animator.Awaken();
        var sm = StateMachine.Make();
        var mainLayer = sm.CreateLayer("main");

        var fadeTrigger = sm.CreateVariable("fade", StateMachineVariableKind.TRIGGER);

        var appearState = mainLayer.CreateState("Appear", 0, false);
        var idleState   = mainLayer.CreateState("Idle",   0, true);
        var fadeState   = mainLayer.CreateState("Fade",   0, false);

        mainLayer.CreateTransition(appearState, idleState, true);
        var idleToFade = mainLayer.CreateTransition(idleState, fadeState, false);
        idleToFade.CreateTriggerCondition(fadeTrigger);

        mainLayer.InitialState = appearState;

        Animator.SpineInstance.SetStateMachine(sm, Entity);
    }

    public override void LateUpdate()
    {
        TimeRemaining -= Time.DeltaTime;
        if (Util.OneTime(TimeRemaining < 0.34f, ref TriggeredFade))
        {
            Animator.SpineInstance.StateMachine.SetTrigger("fade");
        }
        if (TimeRemaining <= 0)
        {
            if (Network.IsServer)
            {
                Network.Despawn(Entity);
                Entity.Destroy();
            }
        }

        var range = 0.75f * Entity.LocalScale.X;

        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
        {
            var distance = (player.Position - Position).Length;
            if (distance < range)
            {
                var effect = player.GetEffect<CirclePushSpiderWebEffect>();
                if (effect != null)
                {
                    effect.DurationRemaining = 0.1f;
                }
                else
                {
                    player.AddEffect<CirclePushSpiderWebEffect>(duration: 0.1f);
                }
            }
        }
    }
}

public class CirclePushSpiderWebEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public override void OnEffectStart(bool isDropIn)
    {
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }
}

public partial class CirclePushDeathEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => true;

    public float StartDuration;
    public float Alpha01 = 1f;
    public Vector2 RespawnPosition;

    public override void NetworkSerialize(AO.StreamWriter writer)
    {
        writer.Write(StartDuration);
    }

    public override void NetworkDeserialize(AO.StreamReader reader)
    {
        StartDuration = reader.Read<float>();
    }

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("death_fall");
        if (Network.IsServer)
        {
            Player.ServerKillPlayer();
            Player.SmashMeter = 1;
            Player.TotalSmashDamage += 0.5f;
        }
        DurationRemaining = Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).GetCurrentStateLength() + 1.5f;
        StartDuration = DurationRemaining;
        if (!isDropIn)
        {
            Player.AddNameInvisibilityReason(nameof(CirclePushDeathEffect));
        }
        Player.NameInvisCounter += 1;
    }

    public override void OnEffectUpdate()
    {
        Alpha01 -= Time.DeltaTime;
        if (Alpha01 < 0) Alpha01 = 0;
        var a = Ease.InQuart(Alpha01);
        Player.SpineAnimator.SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, a);
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.RemoveNameInvisibilityReason(nameof(CirclePushDeathEffect));
        Player.NameInvisCounter -= 1;
        Player.SpineAnimator.SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, 1);
        if (Network.IsServer)
        {
            if (interrupt == false)
            {
                Player.ServerRespawnOrGoIntoSpectator();
            }
        }
    }

    [ClientRpc]
    public static void Respawn(MyPlayer player, Vector2 position)
    {
        player.Teleport(position);
        player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).ResetToInitialState();
    }
}

public class LightningStrike : Component
{
    public float TimeElapsed;
    public bool Triggered;
    public bool Spawned;

    public Spine_Animator Animator;

    public static LightningStrike Spawn(Vector2 position)
    {
        var instance = Assets.GetAsset<Prefab>("LightningStrike.prefab").Instantiate<LightningStrike>();
        instance.Entity.Position = position;
        Network.Spawn(instance.Entity);
        return instance;
    }

    public override void Awake()
    {
        Animator = Entity.TryGetChildByIndex(0).GetComponent<Spine_Animator>();
        Animator.Awaken();
        Animator.LocalEnabled = false;
        var sm = StateMachine.Make();
        var mainLayer = sm.CreateLayer("main");

        var appearState = mainLayer.CreateState("start", 0, false);
        var endState    = mainLayer.CreateState("end",   0, false);

        mainLayer.CreateTransition(appearState, endState, true);
        mainLayer.InitialState = appearState;

        Animator.SpineInstance.Speed = 1.5f;
        
        Animator.SpineInstance.SetStateMachine(sm, Entity);
    }

    public override void LateUpdate()
    {
        TimeElapsed += Time.DeltaTime;
        const float WarningTime = 0;
        if (Util.OneTime(TimeElapsed >= WarningTime, ref Spawned))
        {
            Animator.LocalEnabled = true;
        }
        var animTime = TimeElapsed - WarningTime;
        float radius = 1.5f;
        if (animTime >= 0)
        {
            if (Util.OneTime(animTime >= 0.167f, ref Triggered))
            {
                var range = radius * Entity.LocalScale.X;
                foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
                {
                    var distance = (player.Position - Position).Length;
                    if (distance < range)
                    {
                        player.SmashHit((player.Position - Position).Normalized * 5);
                    }
                }
                SFX.Play(Assets.GetAsset<AudioAsset>("sfx/lightning_impact.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Position });
            }
            if (animTime > 1.5f)
            {
                if (Network.IsServer)
                {
                    Network.Despawn(Entity);
                    Entity.Destroy();
                }
            }
        }
        else
        {
            using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
            using var _2 = UI.PUSH_LAYER(-5);
            Texture circle = Assets.GetAsset<Texture>("$AO/circle.png");
            var pos = Entity.Position;
            var hs = new Vector2(radius, radius);
            UI.Image(new Rect(pos-hs, pos+hs), circle, new Vector4(1, 0, 0, 0.5f));
        }
    }
}

public class CirclePushDynamite : Component
{
    public float TimeElapsed;
    public bool Landed;
    public bool Armed;
    public bool Exploded;

    public Spine_Animator Animator;

    public static CirclePushDynamite Spawn(Vector2 position)
    {
        var instance = Assets.GetAsset<Prefab>("CPDynamite.prefab").Instantiate<CirclePushDynamite>();
        instance.Entity.Position = position;
        Network.Spawn(instance.Entity);
        return instance;
    }

    public static float SampleArc(float arcLength, float arcHeight, float sample)
    {
        float a = arcLength;
        float h = arcHeight;
        float o = a / 2f;
        float xo = sample - o;
        float numerator = (-(xo*xo) + o*o);
        float denominator = (o*o);
        float y = arcHeight * (numerator / denominator);
        return y;
    }

    public override void Awake()
    {
        Animator = Entity.TryGetChildByIndex(0).GetComponent<Spine_Animator>();
        Animator.Awaken();
        var sm = StateMachine.Make();
        var mainLayer = sm.CreateLayer("main");

        var spinState    = mainLayer.CreateState("spin",    0, true);
        var idleState    = mainLayer.CreateState("idle",    0, true);
        var armedState   = mainLayer.CreateState("armed",   0, true);
        var explodeState = mainLayer.CreateState("explode", 0, false);

        mainLayer.InitialState = spinState;
        var spinToIdle = mainLayer.CreateTransition(spinState, idleState, false);
        spinToIdle.CreateTriggerCondition(sm.CreateVariable("idle", StateMachineVariableKind.TRIGGER));

        var idleToArmed = mainLayer.CreateTransition(idleState, armedState, false);
        idleToArmed.CreateTriggerCondition(sm.CreateVariable("arm", StateMachineVariableKind.TRIGGER));

        var armedToExplode = mainLayer.CreateTransition(armedState, explodeState, false);
        armedToExplode.CreateTriggerCondition(sm.CreateVariable("explode", StateMachineVariableKind.TRIGGER));

        Animator.SpineInstance.SetStateMachine(sm, Entity);
    }

    public override void LateUpdate()
    {
        TimeElapsed += Time.DeltaTime;
        float radius = 2f;
        float y = MathF.Max(0, SampleArc(1, 2.5f, TimeElapsed));
        Animator.SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, Ease.T(TimeElapsed, 0.15f));
        var scale = Ease.T(TimeElapsed, 0.15f);
        Animator.Entity.LocalScale = new Vector2(scale, scale);
        Animator.Entity.LocalY = y;
        if (Util.OneTime(TimeElapsed >= 1f, ref Landed))
        {
            Animator.SpineInstance.StateMachine.SetTrigger("idle");
        }
        if (Util.OneTime(TimeElapsed >= 1.75f, ref Armed))
        {
            Animator.SpineInstance.StateMachine.SetTrigger("arm");
            SFX.Play(Assets.GetAsset<AudioAsset>("sfx/bomb_explode.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Position });
        }
        if (Util.OneTime(TimeElapsed >= 2f, ref Exploded))
        {
            Animator.SpineInstance.StateMachine.SetTrigger("explode");
            var range = radius * Entity.LocalScale.X;
            foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
            {
                var distance = (player.Position - Position).Length;
                if (distance < range)
                {
                    player.SmashHit((player.Position - Position).Normalized * 10);
                }
            }
        }
        if (TimeElapsed > 4.0f)
        {
            if (Network.IsServer)
            {
                Network.Despawn(Entity);
                Entity.Destroy();
            }
        }

    }
}