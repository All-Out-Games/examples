using System.Collections;
using System.Diagnostics;
using System.Net;
using AO;

public partial class MinigameBalloonPop : MinigameInstance
{
    public static MinigameBalloonPop _instance;
    public static MinigameBalloonPop Instance
    {
        get
        {
            if (_instance == null)
            {
                foreach (var component in Scene.Components<MinigameBalloonPop>())
                {
                    _instance = component;
                    _instance.Awaken();
                    break;
                }
            }
            return _instance;
        }
    }

    [Serialized] public Entity BalloonSpawnParent;

    public List<Entity> BalloonSpawns = new();

    public int TeamCount;

    public override void Awake()
    {
        foreach (var spawn in BalloonSpawnParent.Children)
        {
            BalloonSpawns.Add(spawn);
            spawn.GetComponent<Sprite_Renderer>().LocalEnabled = false;
        }
    }

    public override void MinigameSetup()
    {
        if (Network.IsServer)
        {
            GameManager.Instance.EnableMinigameTimer(60 * 2);

            ServerAssignTeamsRandomly();

            foreach (var spawner in Scene.Components<BalloonPopBalloonSpawner>())
            {
                spawner.TrySpawnBalloon();
            }
        }
    }

    public void ServerAssignTeamsRandomly()
    {
        Util.Assert(Network.IsServer);

        var allPlayers = new List<MyPlayer>();
        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
        {
            if (player.IsFrontman) continue;
            allPlayers.Add(player);
        }
        GameManager.Shuffle(allPlayers, ref GameManager.Instance.GlobalRng);

        TeamCount = 0;
        foreach (var player in allPlayers)
        {
            player.BalloonPopTeam.Set(TeamCount % BalloonPopBalloon.TeamColors.Length);
            TeamCount += 1;
        }
    }

    public override void MinigameStart()
    {
        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
        {
            if (player.IsFrontman) continue;
            player.AddEffect<BalloonPopIKEffect>();
        }
        
        /*if (Network.IsServer)
        {
            foreach (var player in GameManager.Instance.PlayersInCurrentMinigame)
            {
                player.ServerTryGivePowerup(PowerupKind.BP_BalloonNuke);
                player.ServerTryGivePowerup(PowerupKind.BP_BalloonNuke);
                player.ServerTryGivePowerup(PowerupKind.BP_BalloonNuke);
            }
        }*/
    }

    public override void MinigameTick()
    {
        if (Network.IsServer)
        {
            foreach (var spawner in Scene.Components<BalloonPopBalloonSpawner>())
            {
                if (spawner.BalloonPoppedDestroyTimer > 0)
                {
                    spawner.BalloonPoppedDestroyTimer -= Time.DeltaTime;
                    if (spawner.BalloonPoppedDestroyTimer <= 0)
                    {
                        spawner.DespawnBalloon();
                    }
                }

                if (spawner.SpawnTimer > 0)
                {
                    spawner.SpawnTimer -= Time.DeltaTime;
                    if (spawner.SpawnTimer <= 0)
                    {
                        spawner.TrySpawnBalloon();
                    }
                }
            }
        }
    }

    public override void MinigameLateTick()
    {
    }

    public override void MinigameEnd()
    {
        return;
    }

    public override int GetPlayerScore(MyPlayer player)
    {
        return player.BalloonsPopped.Value;
    }

    public override string LeaderboardPointsHeader()
    {
        return "Balloons Popped";
    }

    public override List<MyPlayer> SortPlayers(List<MyPlayer> players)
    {
        var sortedPlayers = new List<MyPlayer>(players);
        sortedPlayers.Sort((a, b) =>
        {
            if (a.IsEliminated && !b.IsEliminated)
            {
                return 1;
            }
            else if (b.IsEliminated && !a.IsEliminated)
            {
                return -1;
            }
            return GetPlayerScore(b).CompareTo(GetPlayerScore(a));
        });
        return sortedPlayers;
    }

    public override bool ControlsRespawning() => true;
}

public partial class BalloonPopBalloonSpawner : Component
{
    public float SpawnTimer;
    public float BalloonPoppedDestroyTimer;
    public BalloonPopBalloon ActiveBalloon;

    public bool TrySpawnBalloon()
    {
        Util.Assert(Network.IsServer);
        if (ActiveBalloon.Alive())
        {
            return false;
        }

        if (MinigameBalloonPop.Instance.TeamCount <= 0) // can happen with frontman mode in solo play
        {
            return false;
        }

        ActiveBalloon = Assets.GetAsset<Prefab>("BalloonPopBalloon.prefab").Instantiate<BalloonPopBalloon>();
        ActiveBalloon.Entity.Position = Position;
        ActiveBalloon.Spawner = this;
        Network.Spawn(ActiveBalloon.Entity);
        {

        }
        int team = (int)RNG.RangeInt(ref GameManager.Instance.GlobalRng, 0, MinigameBalloonPop.Instance.TeamCount-1);
        if (RNG.RangeFloat(ref GameManager.Instance.GlobalRng, 0, 1) < 0.3f)
        {
            team = -1;
        }
        ActiveBalloon.BalloonPopTeam.Set(team);
        ActiveBalloon.MaxLifetime = RNG.RangeFloat(ref GameManager.Instance.GlobalRng, 8, 12);
        return true;
    }

    public void DespawnBalloon()
    {
        Util.Assert(Network.IsServer);
        Network.Despawn(ActiveBalloon.Entity);
        ActiveBalloon.Entity.Destroy();
        ActiveBalloon = null;
    }

    public void StartSpawnTimer()
    {
        Util.Assert(Network.IsServer);
        SpawnTimer = RNG.RangeFloat(ref GameManager.Instance.GlobalRng, 5, 10);
    }

    public void OnBalloonPopped()
    {
        Util.Assert(Network.IsServer);
        BalloonPoppedDestroyTimer = 0.5f;
        StartSpawnTimer();
    }
}

public partial class BalloonPopBalloon : Component
{
    [Serialized] public Spine_Animator Spine;
    [Serialized] public Circle_Collider Collider;

    public SyncVar<int> BalloonPopTeam = new();
    public SyncVar<Entity> Owner = new();

    public float MaxLifetime;
    public float CurrentLifetime;
    public BalloonPopBalloonSpawner Spawner;
    public bool AlreadyPopped;
    public bool FlyingAway;
    public float TimeStartedFlyingAway;
    public float TimeStartedPopped;
    public float FlyAwayWarnTime;

    public static Vector4[] TeamColors = new[]
    {
        new Vector4(1, 0, 0, 1),
        new Vector4(0, 1, 0, 1),
        new Vector4(0, 0, 1, 1),

        new Vector4(1, 1, 0, 1),
        new Vector4(1, 0, 1, 1),
        new Vector4(0, 1, 1, 1),

        // new Vector4(0.5f, 1, 0, 1),
        new Vector4(0.5f, 0, 1, 1),
        new Vector4(0, 0.5f, 1, 1),

        new Vector4(1, 0.5f, 0, 1),
        new Vector4(1, 0, 0.5f, 1),
        new Vector4(0, 1, 0.5f, 1),

        new Vector4(1, 0.5f, 0.5f, 1),
        new Vector4(1, 0.5f, 0.5f, 1),
        new Vector4(0.5f, 1, 0.5f, 1),

        new Vector4(0.5f, 1, 0.5f, 1),
        new Vector4(0.5f, 0.5f, 1, 1),
        new Vector4(0.5f, 0.5f, 1, 1),
    };

    public static string[] TeamNames = new[]
    {
        "red",
        "green",
        "blue",
        
        "yellow",
        "pink",
        "cyan",
        
        //"lime",
        "purple",
        "sky blue",
        
        "orange",
        "scarlet",
        "toxic green",
        
        "light pink",
        "light pink",
        "toxic green",
        
        "toxic green",
        "light purple",
        "light purple",
    };

    public override void Awake()
    {
        if (Network.LocalPlayer.Alive())
        {
            var localPlayer = (MyPlayer)Network.LocalPlayer;
            if (BalloonPopTeam == localPlayer.BalloonPopTeam || BalloonPopTeam == -1)
            {
                if (GameManager.Instance.State == GameState.RunningMinigame) // dont play during the intro cutscene
                {
                    SFX.Play(Assets.GetAsset<AudioAsset>("sfx/balloon_of_your_color_spawn.wav"), new SFX.PlaySoundDesc() { Volume = 0.5f, Positional = true, Position = Entity.Position, SpeedPerturb=0.2f, VolumePerturb=0.2f });
                }
            }
        }
        Spine.Awaken();
        var sm = StateMachine.Make();
        var layer = sm.CreateLayer("main", 0);

        var spawnState = layer.CreateState("Appear", 0, false);
        var idleState  = layer.CreateState("Idle_Loop", 0, true);
        var popState   = layer.CreateState("Pop", 0, false);

        var popTrigger = sm.CreateVariable("pop", StateMachineVariableKind.TRIGGER);

        layer.CreateTransition(spawnState, idleState, true);
        layer.CreateTransition(idleState, popState, false).CreateTriggerCondition(popTrigger);

        layer.InitialState = spawnState;

        Spine.SpineInstance.SetStateMachine(sm, Entity);
        Spine.SpineInstance.SetSkin("red");
        Spine.SpineInstance.RefreshSkins();

        BalloonPopTeam.OnSync += (old, value) =>
        {
            if (value == -2)
            {
                if (Network.LocalPlayer.Alive())
                {
                    if (Owner.Value.Alive() && Owner.Value == Network.LocalPlayer.Entity)
                    {
                        Spine.SpineInstance.ColorMultiplier = new Vector4(0.2f, 0.2f, 0.2f, 1f);
                    }
                    else
                    {
                        Spine.SpineInstance.ColorMultiplier = TeamColors[((MyPlayer)Network.LocalPlayer).BalloonPopTeam];
                    }
                }
                    
            }
            else if (value == -1)
            {
                Spine.SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, 1);
                Spine.SpineInstance.SetSkin("multicoloured");
                Spine.SpineInstance.RefreshSkins();
                Spine.SpineInstance.Scale = new Vector2(1.25f, 1.25f);
            }
            else
            {
                Spine.SpineInstance.ColorMultiplier = TeamColors[value];
            }
        };
    }

    [ClientRpc]
    public void Pop()
    {
        AlreadyPopped = true;
        TimeStartedPopped = Time.TimeSinceStartup;
        SFX.Play(Assets.GetAsset<AudioAsset>("sfx/balloon_pop.wav"), new SFX.PlaySoundDesc() { Volume = 0.6f, Positional = true, Position = Entity.Position, SpeedPerturb=0.2f, VolumePerturb=0.2f });
        Spine.SpineInstance.StateMachine.SetTrigger("pop");
    }

    public static void ServerPlayerPoppedBalloon(MyPlayer player, BalloonPopBalloon balloon)
    {
        if (player.Alive())
        {
            if (player.BalloonPopTeam == balloon.BalloonPopTeam || balloon.BalloonPopTeam == -1)
            {
                int points = 1;
                if (balloon.BalloonPopTeam == -1)
                {
                    points = 3;
                }
                player.BalloonsPopped.Set(player.BalloonsPopped + points);
                player.CallClient_SpawnTextPopup($"+{points} {Util.Pluralize("point", "points", points)}", player.Position + new Vector2(0, 1), new Vector4(0, 1, 0, 1), rpcTarget: player);
            }
            else if (balloon.BalloonPopTeam == -2)
            {
                var newScore = (int)MathF.Max(0, player.BalloonsPopped - 3);
                player.BalloonsPopped.Set(newScore);
                player.CallClient_SpawnTextPopup("Decoy! -3 points!", player.Position + new Vector2(0, 1), new Vector4(1, 0, 0, 1), rpcTarget: player);
            }
            else
            {
                var newScore = (int)MathF.Max(0, player.BalloonsPopped - 1);
                player.BalloonsPopped.Set(newScore);
                player.CallClient_SpawnTextPopup("-1 point", player.Position + new Vector2(0, 1), new Vector4(1, 0, 0, 1), rpcTarget: player);
                player.CallClient_ShowNotificationLocal($"Only pop the { TeamNames[player.BalloonPopTeam]} or shiny balloons!");
            }
        }
        balloon.CallClient_Pop();

        if (balloon.Spawner.Alive())
        {
            balloon.Spawner.OnBalloonPopped();
        }
    }

    public bool TryHitWithBullet(BalloonPopBullet bullet)
    {
        if (AlreadyPopped)
        {
            return false;
        }

        if (FlyingAway)
        {
            return false;
        }

        AlreadyPopped = true;

        if (Network.IsServer)
        {
            ServerPlayerPoppedBalloon(bullet.OwningPlayer, this);
        }

        return true;
    }

    [ClientRpc]
    public void FlyAway()
    {
        FlyingAway = true;
        TimeStartedFlyingAway = Time.TimeSinceStartup;

        SFX.Play(Assets.GetAsset<AudioAsset>("sfx/balloon_despawn.wav"), new SFX.PlaySoundDesc() { Volume = 0.6f, Positional = true, Position = Entity.Position, SpeedPerturb=0.2f, VolumePerturb=0.2f });
    }

    [ClientRpc]
    public void WarnForFlyAway()
    {
        SFX.Play(Assets.GetAsset<AudioAsset>("sfx/balloon_wiggle.wav"), new SFX.PlaySoundDesc() { Volume = 0.4f, Positional = true, Position = Entity.Position, SpeedPerturb=0.2f, VolumePerturb=0.2f });
    }

    public override void Update()
    {
        if (AlreadyPopped && BalloonPopTeam == -2 && Network.IsServer && Time.TimeSinceStartup - TimeStartedPopped > 1f)
        {
            Network.Despawn(Entity);
            Entity.Destroy();
        }
        
        if (FlyingAway)
        {
            var t = Ease.T(Time.TimeSinceStartup - TimeStartedFlyingAway, 1f);
            var pos = Spine.Entity.LocalPosition;
            pos.Y = AOMath.Lerp(pos.Y, 4, Ease.InQuart(t));
            Spine.Entity.LocalPosition = pos;
            var color = Spine.SpineInstance.ColorMultiplier;
            color.W = 1 - t;
            Spine.SpineInstance.ColorMultiplier = color;

            if (Time.TimeSinceStartup - TimeStartedFlyingAway > 1f && BalloonPopTeam == -2 && Network.IsServer)
            {
                Network.Despawn(Entity);
                Entity.Destroy();
            }
        }
        else
        {
            var t = Ease.T(Time.TimeSinceStartup - FlyAwayWarnTime, 1f);
            Spine.Entity.LocalRotation = Util.Jitter(t * 0.5f, 16) * 5f;
            if (Network.IsServer && !AlreadyPopped && GameManager.Instance.State == GameState.RunningMinigame && BalloonPopTeam != -2)
            {
                var timeLeftBefore = MaxLifetime - CurrentLifetime;
                CurrentLifetime += Time.DeltaTime;
                var timeLeft = MaxLifetime - CurrentLifetime;
                if ((timeLeft < 2.0f && timeLeftBefore >= 2.0f) || (timeLeft < 1.0f && timeLeftBefore >= 1.0f))
                {
                    CallClient_WarnForFlyAway();
                }
                if (CurrentLifetime >= MaxLifetime)
                {
                    CallClient_FlyAway();

                    if (Spawner != null)
                    {
                        Spawner.OnBalloonPopped();
                    }
                }
            }
        }
    }
}

public partial class BalloonPopShootAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Line;
    // public override Type TargettingEffect => typeof(BalloonPopAimingEffect);

    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/revolver_icon.png");
    public override float MaxDistance => 10f;
    public override float Cooldown =>  (Player.HasEffect<BP_LockedInEffect>() ? 0.125f : 0.7f);

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        var projectileEntity = Game.SpawnProjectile(Player, "BalloonPopBullet.prefab", "balloon_pop_bullet", Player.Position, positionOrDirection);
        var dart = projectileEntity.GetComponent<BalloonPopBullet>();
        dart.OwningPlayer = Player;
        dart.Sprite.Entity.LocalRotation = AOMath.ToDegrees(MathF.Atan2(positionOrDirection.Y, positionOrDirection.X));
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("throw");

        if (Network.IsClient)
        {
            int variant = (int)RNG.RangeInt(ref GameManager.Instance.GlobalRng, 1, 3);
            SFX.Play(Assets.GetAsset<AudioAsset>($"sfx/throw_dart_0{variant}.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Player.Position, SpeedPerturb=0.2f, VolumePerturb=0.2f});
        }

        return true;
    }
}

// public class BalloonPopAimingEffect : MyEffect
// {
//     public override bool IsActiveEffect => false;
//     public override bool GetInterruptedByNewActiveEffects => true;

//     public override List<Type> AbilityWhitelist { get; } = new List<Type>(){typeof(BalloonPopShootAbility)};

//     public override void OnEffectStart(bool isDropIn)
//     {
//         Player.SetMouseIKEnabled(true);
//     }

//     public override void OnEffectEnd(bool interrupt)
//     {
//         Player.SetMouseIKEnabled(false);
//     }

//     public override void OnEffectUpdate()
//     {
//     }
// }

public class BalloonPopBullet : Component
{
    [Serialized] public Sprite_Renderer Sprite;
    [Serialized] public Circle_Collider Collider;
    [Serialized] public MyPlayer OwningPlayer;

    public bool AlreadyHitSomething;

    public float Lifetime;

    public override void Awake()
    {
        Collider.OnCollisionEnter += (other) =>
        {
            if (GameManager.Instance.CurrentMinigame != MinigameBalloonPop.Instance)
            {
                return;
            }

            if (other.Alive() == false)
            {
                return;
            }

            if (other.GetComponent<BalloonPopBulletIgnoreCollision>() != null)
            {
                return;
            }

            if (AlreadyHitSomething)
            {
                return;
            }

            if (other.TryGetComponent<BalloonPopBalloon>(out var balloon))
            {
                if (balloon.TryHitWithBullet(this) == false)
                {
                    return;
                }
            }
            else if (other.TryGetComponent<MyPlayer>(out var player))
            {
                if (player == OwningPlayer) return;
                if (player.IsDead) return; // can happen in frontman mode
                player.OnHitWithBalloonPopBullet(this);
            }

            AlreadyHitSomething = true;

            Entity.Destroy();
        };
    }

    public override void Update()
    {
        Lifetime += Time.DeltaTime;
        if (Lifetime > 1.0f)
        {
            this.Entity.Destroy();
        }
    }
}

public class BalloonPopSlowEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public override void OnEffectStart(bool isDropIn)
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }

    public override void OnEffectUpdate()
    {
    }
}

public class BalloonPopIKEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetBool("use_dart", true);
        Player.SetMouseIKEnabled(true);
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SetMouseIKEnabled(false);
        Player.SpineAnimator.SpineInstance.StateMachine.SetBool("use_dart", false);
    }

    public override void OnEffectUpdate()
    {
        if (Player.IsFrontman)
        {
            Player.RemoveEffect(this, true);
        }
    }
}

public class BalloonPopBulletIgnoreCollision : Component
{
}

// Color Bomb
 
public partial class BP_ColorBombAbility : MyAbility
{
    public const float RADIUS = 3f;
    
    public override TargettingMode TargettingMode => TargettingMode.CircleAOE;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override float Cooldown => 1f;
    public override float MaxDistance => 10f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/red_colour_bomb_icon.png");
    
    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.BP_ColorBomb);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.BP_ColorBomb) == false)
            {
                return false;
            }
            LobbedProjectile.Spawn(Player.Position, Player.Position + positionOrDirection * magnitude, Assets.GetAsset<Prefab>("ColorBomb.prefab"), OnImpact);
        }

        return true;
    }

    public void OnImpact(Vector2 position)
    {
        foreach (var balloon in Scene.Components<BalloonPopBalloon>())
        {
            if (Vector2.Distance(balloon.Entity.Position, position) > RADIUS)
                continue;

            balloon.BalloonPopTeam.Set(Player.BalloonPopTeam.Value);
        }
    }
}

// Decoy

public partial class BP_DecoyBalloonAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.CircleAOE;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override float Cooldown => 1f;
    public override float MaxDistance => 10f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/decoy_balloon.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.BP_DecoyBalloon);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.BP_DecoyBalloon) == false)
            {
                return false;
            }

            Spawn(Player.Position + positionOrDirection * magnitude, Player);
        }

        return true;
    }

    public static void Spawn(Vector2 position, MyPlayer owner)
    {
        var decoy = Assets.GetAsset<Prefab>("DecoyBalloon.prefab").Instantiate<BalloonPopBalloon>();
        decoy.Entity.Position = position;
        Network.Spawn(decoy.Entity);
        decoy.Awaken();
        decoy.Owner.Set(owner.Entity);
        decoy.BalloonPopTeam.Set(-2);
        decoy.MaxLifetime = 999;
    }
}

// Locked In

public partial class BP_LockedInAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override float Cooldown => 1f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/locked_in.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.BP_LockedIn);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.BP_LockedIn) == false)
            {
                return false;
            }
            
            CallClient_Activate(Player);
        }

        return true;
    }
    
    [ClientRpc]
    public static void Activate(MyPlayer player)
    {
        player.AddEffect<BP_LockedInEffect>();
    }
}

public class BP_LockedInEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override float DefaultDuration => 10f;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.AddEffect<PulseTextEffect>(preInit: effect => effect.Text = "Locked in!");
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }
}

// Mini Tornado

public partial class BP_MiniTornadoAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Line;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/mini_tornado.png");
    public override float MaxDistance => 10f;

    public override float Cooldown => 1f;

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.BP_MiniTornado);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.BP_MiniTornado) == false)
            {
                return false;
            }
            
            var projectileEntity = Game.SpawnProjectile(Player, "MiniTornado.prefab", "mini_tornado", Player.Position, positionOrDirection);
            var dart = projectileEntity.GetComponent<BP_Tornado>();
            dart.OwningPlayer = Player;
        }

        return true;
    }
}

public class BP_Tornado : Component
{
    [Serialized] public Spine_Animator Spine;
    [Serialized] public Circle_Collider Collider;
    [Serialized] public MyPlayer OwningPlayer;
    
    public float Lifetime;

    public override void Awake()
    {
        Spine.Awaken();
        
        var sm = StateMachine.Make();
        var layer = sm.CreateLayer("main", 0);
        var idleState = layer.CreateState("wind", 0, true);
        layer.InitialState = idleState;
        Spine.SpineInstance.SetStateMachine(sm, Entity);
        
        Collider.OnCollisionEnter += (other) =>
        {
            if (other.Alive() == false) return;

            if (!Network.IsServer)
            {
                return;
            }
            
            if (GameManager.Instance.CurrentMinigame != MinigameBalloonPop.Instance)
            {
                return;
            }

            if (other.GetComponent<BalloonPopBulletIgnoreCollision>() != null)
            {
                return;
            }

            if (other.TryGetComponent<BalloonPopBalloon>(out var balloon))
            {
                balloon.CallClient_FlyAway();
                return;
            }
            
            if (other.TryGetComponent<MyPlayer>(out var player))
            {
                if (player == OwningPlayer)
                {
                    return;
                }
                
                player.CallClient_HitWithMiniTornado();
            }
        };
    }

    public override void Update()
    {
        Lifetime += Time.DeltaTime;
        if (Lifetime > 4.0f)
        {
            this.Entity.Destroy();
        }
    }
}

// Chain Lightning

public partial class BP_ChainLightningAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Line;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/chain_lightning.png");
    public override float MaxDistance => 10f;
    public override float Cooldown => 1f;

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.BP_ChainLightning);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.BP_ChainLightning) == false)
            {
                return false;
            }

            var instance = Assets.GetAsset<Prefab>("ChainLightning.prefab").Instantiate().GetComponent<BP_ChainLightning>();
            instance.ServerInit(Player, Player.Position + new Vector2(0, 0.5f), positionOrDirection);
            Network.Spawn(instance.Entity);
        }

        return true;
    }
}

public partial class BP_ChainLightning : Component
{
    [Serialized] public Spine_Animator Spine;
    [Serialized] public MyPlayer OwningPlayer;
    
    public float Lifetime;
    public int Durability = 3;

    public Vector2 Direction;
    public Vector2 PosLastFrame;

    public float Timer;
    public Vector2 StartPosition;

    public void ServerInit(MyPlayer player, Vector2 position, Vector2 direction)
    {
        Timer = 0;
        StartPosition = position;
        Direction = direction;
        OwningPlayer = player;
    }

    public override void Awake()
    {
        Spine.Awaken();
        
        var sm = StateMachine.Make();
        var layer = sm.CreateLayer("main", 0);
        var idleState = layer.CreateState("idle", 0, true);
        layer.InitialState = idleState;
        Spine.SpineInstance.SetStateMachine(sm, Entity);
    }

    public static bool UpdateClosest<T>(ref T closest, ref float distance, T maybe, float maybeDistance)
    {
        if (maybeDistance < distance)
        {
            distance = maybeDistance;
            closest = maybe;
            return true;
        }
        return false;
    }

    public override void Update()
    {
        var dir = (Entity.Position - PosLastFrame).Normalized;
        Spine.Entity.LocalRotation = AOMath.ToDegrees(MathF.Atan2(dir.Y, dir.X));
        
        if (Network.IsServer)
        {
            Timer += Time.DeltaTime;

            var distanceTravelled = Timer * 10f;
            Entity.Position = StartPosition + Direction * distanceTravelled;
            
            Lifetime += Time.DeltaTime;
            if (Lifetime > 6.0f)
            {
                Network.Despawn(Entity);
                Entity.Destroy();
            }
            
            foreach (var balloon in Scene.Components<BalloonPopBalloon>())
            {
                if (OwningPlayer.Alive())
                {
                    if (Vector2.Distance(balloon.Entity.Position, Entity.Position) < 0.75f && (balloon.BalloonPopTeam == -1 || balloon.BalloonPopTeam == OwningPlayer.BalloonPopTeam) && !balloon.AlreadyPopped && !balloon.FlyingAway)
                    {
                        BalloonPopBalloon.ServerPlayerPoppedBalloon(OwningPlayer, balloon);

                        Durability--;

                        bool die = true;
                        if (Durability > 0)
                        {
                            die = false;
                            float closestDistance = float.MaxValue;
                            BalloonPopBalloon closestBalloon = null;
                            foreach (var nextBalloon in Scene.Components<BalloonPopBalloon>())
                            {
                                if (nextBalloon.AlreadyPopped || nextBalloon.FlyingAway || nextBalloon == balloon)
                                {
                                    continue;
                                }

                                if (nextBalloon.BalloonPopTeam != -1)
                                {
                                    if (nextBalloon.BalloonPopTeam != OwningPlayer.BalloonPopTeam)
                                    {
                                        continue;
                                    }
                                }

                                UpdateClosest(ref closestBalloon, ref closestDistance, nextBalloon, (nextBalloon.Position - Entity.Position).LengthSquared);
                            }

                            if (closestBalloon.Alive())
                            {
                                ServerInit(OwningPlayer, Entity.Position, (closestBalloon.Entity.Position - Entity.Position).Normalized);
                            }
                            else
                            {
                                die = true;
                            }
                        }

                        if (die)
                        {
                            Network.Despawn(Entity);
                            Entity.Destroy();
                        }
                    }
                }
            }
        }
    }

    public override void LateUpdate()
    {
        PosLastFrame = Entity.Position;
    }
}

// Balloon Nuke

public partial class BP_BalloonNukeAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override float MaxDistance => 2;
    public override float Cooldown => 1f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/nuke_icon.png");

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.BP_BalloonNuke) == false)
            {
                return false;
            }
            CallClient_Activate(Player);
        }
        return true;
    }

    [ClientRpc]
    public static void Activate(MyPlayer player)
    {
        BalloonNuke.Spawn(player);
    }
} 

public class BalloonNuke : Component
{
    public MyPlayer Owner;

    [Serialized] public Spine_Animator Spine;
    
    private float Timer;
    private bool Exploded;
    private Vector2 PosStart = new(-104.374f, 0f); 
    private Vector2 PosEnd = new(-104.374f, -10.513f); 
    
    public static void Spawn(MyPlayer owner)
    {
        Assets.GetAsset<Prefab>("BalloonNuke.prefab").Instantiate(onBeforeAwake: e => e.GetComponent<BalloonNuke>().Owner = owner);
    }

    public override void Awake()
    {
        Spine.Awaken();
        var sm = StateMachine.Make();
        var layer = sm.CreateLayer("main");
        var flyState = layer.CreateState("nuke_fly", 0, true);
        var explosionState = layer.CreateState("explosion", 0, false);
        var explodeTrigger = sm.CreateVariable("explode", StateMachineVariableKind.TRIGGER);
        layer.CreateGlobalTransition(explosionState).CreateTriggerCondition(explodeTrigger);
        layer.InitialState = flyState;
        Spine.SpineInstance.SetStateMachine(sm, Entity);
        
        Notifications.Show($"{Owner.Name} called in a nuke!");

        Coroutine.Start(Entity, _PulseRoutine());
        IEnumerator _PulseRoutine()
        {
            var blankTime = new WaitForSeconds(0.5f);
            int count = 0;
            
            while (count < 2)
            {
                var drawTime = 0.5f;
                while (drawTime > 0f)
                {
                    UI.Image(UI.ScreenRect, Assets.GetAsset<Texture>("sprites/emptysquare.png"), tint: new Vector4(1, 0, 0, 0.25f));
                    drawTime -= Time.DeltaTime;
                    yield return null;
                }

                yield return blankTime;
                count++;
            }
        }

        SFX.Play(Assets.GetAsset<AudioAsset>("sfx/nuke_fall.wav"), new SFX.PlaySoundDesc() { Positional = false, Volume = 0.5f});
    }

    public override void Update()
    {
        Timer += Time.DeltaTime;

        if (Timer < 1.5f)
        {
            Entity.Position = Vector2.Lerp(PosStart, PosEnd, Timer / 1.5f);
        }
        
        if (Util.OneTime(Timer > 1.5f, ref Exploded))
        {
            Entity.Position = PosEnd;
            Spine.SpineInstance.StateMachine.SetTrigger("explode");
            
            SFX.Play(Assets.GetAsset<AudioAsset>("sfx/nuke_explode.wav"), new SFX.PlaySoundDesc() { Positional = false, Volume = 0.5f});

            if (Network.LocalPlayer != null)
            {
                ((MyPlayer)Network.LocalPlayer).CameraControl.Shake(3f, 0.25f);
            }

            if (Network.IsServer)
            {
                foreach (var balloon in Scene.Components<BalloonPopBalloon>())
                {
                    if (Owner.Alive() && (balloon.BalloonPopTeam == Owner.BalloonPopTeam || balloon.AlreadyPopped || balloon.FlyingAway))
                        continue;
                    
                    BalloonPopBalloon.ServerPlayerPoppedBalloon(null, balloon);
                }
            }
        }
        
        if (Timer > 5f)
        {
            Entity.Destroy();
        }
    }
}