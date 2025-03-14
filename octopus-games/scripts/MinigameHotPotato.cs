using System.Collections;
using System.Drawing;
using AO;

public partial class MinigameHotPotato : MinigameInstance
{
    public enum HotPotatoState
    {
        JustStartedRound,
        BombsActive,
        WaitingForNextRound,
        Done,
    }

    public SyncVar<int> _state = new();
    public HotPotatoState State
    {
        get => (HotPotatoState)_state.Value;
        set => _state.Set((int)value, true);
    }

    public HotPotatoState StateLastFrame = (HotPotatoState)(-1);

    public float TimeStateStarted;
    public float PrevTimeInState;

    public static MinigameHotPotato _instance;
    public static MinigameHotPotato Instance
    {
        get
        {
            if (_instance == null)
            {
                foreach (var component in Scene.Components<MinigameHotPotato>())
                {
                    _instance = component;
                    _instance.Awaken();
                    break;
                }
            }
            return _instance;
        }
    }

    public override void Awake()
    {
        _state.OnSync += (old, value) =>
        {
            TimeStateStarted = Time.TimeSinceStartup;
            PrevTimeInState = 0f;
        };
    }

    public override void MinigameSetup()
    {
        if (Network.IsServer)
        {
            foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
            {
                player.UseLivesForMinigame.Set(true);
                player.MinigameLivesLeft.Set(3);
            }
            State = HotPotatoState.JustStartedRound;
        }
    }

    public override void MinigameStart()
    {
        if (Network.IsServer)
        {
            // foreach (var player in GameManager.Instance.PlayersInCurrentMinigame)
            // {
            //     player.ServerTryGivePowerup(PowerupKind.HP_AirMail);
            //     player.ServerTryGivePowerup(PowerupKind.HP_AirMail);
            //     player.ServerTryGivePowerup(PowerupKind.HP_AirMail);
            // }
        }
    }

    public void ServerDespawnAllDynamite()
    {
        Util.Assert(Network.IsServer);
        foreach (var dynamite in Scene.Components<HP_Dynamite>())
        {
            Network.Despawn(dynamite.Entity);
            dynamite.Entity.Destroy();
        }
    }

    public override void MinigameTick()
    {
        var timeInCurrentState = Time.TimeSinceStartup - TimeStateStarted;
        var justEnteredState = StateLastFrame != State;
        StateLastFrame = State;
        using var _ = AllOut.Defer(() => PrevTimeInState = timeInCurrentState);

        switch (State)
        {
            case HotPotatoState.JustStartedRound:
            {
                if (Network.IsServer)
                {
                    if (timeInCurrentState >= 2f)
                    {
                        var alivePlayers = 0;
                        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
                        {
                            alivePlayers += 1;
                        }

                        ServerDespawnAllDynamite();

                        int dynamiteCount = alivePlayers / 3 + 1;
                        if (GameManager.Instance.FrontmanEnabled)
                        {
                            dynamiteCount = 1;
                        }
                        for (int i = 0; i < dynamiteCount; i++)
                        {
                            var dynamite = Assets.GetAsset<Prefab>("HPDynamite.prefab").Instantiate<HP_Dynamite>();
                            Network.Spawn(dynamite.Entity);

                            dynamite.TimeLeft = 20;
                            if (dynamite.ServerTryGiveRandomDynamite() == false)
                            {
                                // shouldn't fail but might as well handle it
                                Network.Despawn(dynamite.Entity);
                                dynamite.Entity.Destroy();
                                continue;
                            }
                        }

                        State = HotPotatoState.BombsActive;
                    }
                }
                break;
            }
            case HotPotatoState.BombsActive:
            {
                var dynamiteCount = 0;
                foreach (var dynamite in Scene.Components<HP_Dynamite>())
                {
                    if (Network.IsServer)
                    {
                        if (dynamite.CurrentPlayer.Alive() == false)
                        {
                            Network.Despawn(dynamite.Entity);
                            dynamite.Entity.Destroy();
                            continue;
                        }
                    }

                    dynamiteCount += 1;

                    // pass visual
                    var dynamiteColor = new Vector4(1, 1, 1, 0);
                    if (dynamite.PassTargetPlayer.Alive())
                    {
                        var pass01 = Ease.T(Time.TimeSinceStartup - dynamite.TimePassStarted, 0.25f);
                        dynamiteColor = Vector4.White;
                        var offset = Vector2.Up * 0.5f;
                        dynamite.Entity.Position = Vector2.Lerp(dynamite.PassStartPosition + offset, dynamite.PassTargetPlayer.Entity.Position + offset, pass01);
                        dynamite.DynamiteRenderer.Entity.LocalRotation = 360f * pass01 * 2f;
                        dynamite.DynamiteRenderer.Entity.LocalY = ParabolaArcHeight(pass01) * 0.35f;

                        if (Network.IsServer && pass01 >= 1f)
                        {
                            // complete the pass. maybe parrying
                            var oldHolder = dynamite.CurrentPlayer;
                            var newHolder = dynamite.PassTargetPlayer;
                            dynamite.ServerPlaceDynamiteOnPlayer(newHolder);
                            if (oldHolder.Alive() && newHolder.ServerTryConsumePowerup(PowerupKind.HP_Reverse))
                            {
                                dynamite.ServerBeginPass(newHolder, oldHolder);
                            }
                        }
                    }
                    dynamite.DynamiteRenderer.Tint = dynamiteColor;

                    // maybe explode
                    if (Network.IsServer)
                    {
                        if (dynamite.IsAirMail == false && dynamite.CurrentPlayer.Alive() && dynamite.PassTargetPlayer.Alive() == false)
                        {
                            if (dynamite.CurrentPlayer.IsDead)
                            {
                                Network.Despawn(dynamite.Entity);
                                dynamite.Entity.Destroy();
                            }
                            else
                            {
                                if (dynamite.TimeLeft >= 0f)
                                {
                                    dynamite.TimeLeft -= Time.DeltaTime;
                                    if (dynamite.TimeLeft <= 0)
                                    {
                                        dynamite.TimeLeft = 0;
                                        if (dynamite.CurrentPlayer.ServerTryConsumePowerup(PowerupKind.HP_MoreTime))
                                        {
                                            dynamite.TimeLeft = 5;
                                            CallClient_ShowTimeExtendedText(dynamite.CurrentPlayer, rpcTarget: dynamite.CurrentPlayer);
                                        }
                                        else
                                        {
                                            CallClient_Explode(dynamite);
                                            dynamite.CurrentPlayer = null;
                                            Network.Despawn(dynamite.Entity);
                                            dynamite.Entity.Destroy();
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                if (Network.IsServer)
                {
                    if (dynamiteCount == 0)
                    {
                        State = HotPotatoState.WaitingForNextRound;
                    }
                }
                break;
            }
            case HotPotatoState.WaitingForNextRound:
            {
                if (Network.IsServer)
                {
                    if (timeInCurrentState >= 2f)
                    {
                        CallClient_RespawnPlayers();
                        var startNewRound = false;
                        if (GameManager.Instance.FrontmanEnabled)
                        {
                            var onlyFrontmanIsLeft = true;
                            foreach (var other in GameManager.Instance.PlayersInCurrentMinigame) if (other.Alive())
                            {
                                if (other.IsFrontman) continue;
                                if (other.IsDead == false)
                                {
                                    onlyFrontmanIsLeft = false;
                                }
                            }
                            if (onlyFrontmanIsLeft == false)
                            {
                                startNewRound = true;
                            }
                        }
                        else
                        {
                            if (GameManager.Instance.ServerEndMinigameIfEnoughPlayersArePermadead() == false)
                            {
                                startNewRound = true;
                            }
                        }

                        if (startNewRound)
                        {
                            GameManager.Instance.ServerMoveAllPlayersToMinigameSpawnPoints();
                            State = HotPotatoState.JustStartedRound;
                        }
                        else
                        {
                            State = HotPotatoState.Done;
                        }
                    }
                }
                break;
            }
            case HotPotatoState.Done:
            {
                break;
            }
        }

        if (Network.LocalPlayer != null)
        {
            var rect = UI.SafeRect.TopRightRect().Grow(50, 0, 50, 175).Offset(-50, -300f);
            UI.Image(rect.LeftRect().Grow(0, 100, 0, 0), Assets.GetAsset<Texture>("AbilityIcons/reverse_icon.png"));
        
            var ts = new UI.TextSettings()
            {
                Font = UI.Fonts.BarlowBold,
                Size = 100f,
                Color = Vector4.White,
                DropShadowColor = new Vector4(0f,0f,0.02f,0.5f),
                DropShadowOffset = new Vector2(0f,-3f),
                HorizontalAlignment = UI.HorizontalAlignment.Right,
                VerticalAlignment = UI.VerticalAlignment.Center,
                WordWrap = false,
                WordWrapOffset = 0,
                Outline = true,
                OutlineThickness = 3,
                Offset = new Vector2(0, 0),
            };

            int reverseCount = 0;
            int moreTimeCount = 0;
            
            foreach (var kind in ((MyPlayer)Network.LocalPlayer).EquippedPowerups)
            {
                if (kind == PowerupKind.HP_Reverse) reverseCount++;
                if (kind == PowerupKind.HP_MoreTime) moreTimeCount++;
            }
            
            UI.Text(rect, reverseCount.ToString(), ts);
        
            rect = rect.Offset(0, -120f);
            UI.Image(rect.LeftRect().Grow(0, 100, 0, 0), Assets.GetAsset<Texture>("AbilityIcons/more_time_icon.png"));
            UI.Text(rect, moreTimeCount.ToString(), ts);
        }
    }

    [ClientRpc]
    public void Explode(HP_Dynamite dynamite)
    {
        dynamite.CurrentPlayer.ClearAllEffects();
        dynamite.CurrentPlayer.RemoveEffect<HP_HoldingDynamiteEffect>(true);
        dynamite.CurrentPlayer.AddEffect<HP_DeathEffect>();
    }

    [ClientRpc]
    public void RespawnPlayers()
    {
        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
        {
            player.RemoveEffect<HP_DeathEffect>(false);
            if (Network.IsServer && player.IsDead && player.IsEliminated == false)
            {
                player.ServerRespawnOrGoIntoSpectator();
            }
        }
    }

    public override void MinigameLateTick()
    {

    }

    public override void MinigameEnd()
    {
        foreach (var player in Scene.Components<MyPlayer>())
        {
            player.RemoveEffect<HP_HoldingDynamiteEffect>(false);
        }

        if (Network.IsServer)
        {
            ServerDespawnAllDynamite();
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

    public void StartNewRound()
    {
        State = HotPotatoState.WaitingForNextRound;
    }

    public float ParabolaArcHeight(float t)
    {
        return MathF.Max(0, -MathF.Pow(2f * t - 1, 2) + 1);
    }

    [ClientRpc]
    public void ShowTimeExtendedText(MyPlayer player)
    {
        player.AddEffect<PulseTextEffect>(preInit: effect => effect.Text = "Power up activated! +5 Seconds");
    }

    public override bool ControlsRespawning() => true;
}

public partial class HP_Dynamite : Component
{
    [Serialized] public Sprite_Renderer DynamiteRenderer;
    [NetSync] public float TimeLeft;

    public SyncVar<bool> IsAirMail = new();
    public SyncVar<Entity> CurrentPlayerEntity = new();
    public SyncVar<Entity> PassTargetPlayerEntity = new();
    public SyncVar<Vector2> PassStartPosition  = new();
    public MyPlayer CurrentPlayer
    {
        get
        {
            return CurrentPlayerEntity.Value.Alive() ? CurrentPlayerEntity.Value.GetComponent<MyPlayer>() : null;
        }
        set
        {
            CurrentPlayerEntity.Set(value.Alive() ? value.Entity : null);
        }
    }
    public MyPlayer PassTargetPlayer
    {
        get
        {
            return PassTargetPlayerEntity.Value.Alive() ? PassTargetPlayerEntity.Value.GetComponent<MyPlayer>() : null;
        }
        set
        {
            PassTargetPlayerEntity.Set(value.Alive() ? value.Entity : null);
        }
    }

    public float TimePassStarted = -1000f;

    public void ServerBeginPass(MyPlayer from, MyPlayer target)
    {
        PassTargetPlayer = target;
        PassStartPosition.Set(from.Entity.Position);
        CallClient_BeginPass(from, target);
    }

    [ClientRpc]
    public void BeginPass(MyPlayer caster, MyPlayer target)
    {
        if (CurrentPlayer.Alive())
        {
            CurrentPlayer.RemoveEffect<HP_HoldingDynamiteEffect>(false);
        }
        TimePassStarted = Time.TimeSinceStartup;
    }

    public void ServerPlaceDynamiteOnPlayer(MyPlayer player)
    {
        CallClient_PlaceDynamiteOnPlayer(player);
        IsAirMail.Set(false);
        CurrentPlayer = player;
        PassTargetPlayer = null;
    }

    [ClientRpc]
    public void PlaceDynamiteOnPlayer(MyPlayer newPlayer)
    {
        newPlayer.AddEffect<HP_HoldingDynamiteEffect>(preInit: effect => effect.Dynamite = this);
        newPlayer.AddEffect<HotPotatoSlowEffect>(duration: 3f);
    }

    public bool ServerTryGiveRandomDynamite()
    {
        Util.Assert(Network.IsServer);

        var options = new List<MyPlayer>();
        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
        {
            if (HP_PassDynamiteAbility.CanPassDynamiteToPlayer(player) == false)
            {
                continue;
            }

            options.Add(player);
        }

        CurrentPlayer = null;
        if (options.Count == 0)
        {
            return false;
        }

        var randomPlayer = options.GetRandom();
        Util.Assert(CurrentPlayer.Alive() == false);
        ServerPlaceDynamiteOnPlayer(randomPlayer);
        return true;
    }
    
    public float ParabolaArcHeight(float height, float range, float x)
    {
        return -height * MathF.Pow(x / (0.5f * range) - 1, 2) + height;
    }
}

public class HP_DeathEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => true;

    public bool Exploded;

    public override void OnEffectStart(bool isDropIn)
    {
        SFX.Play(Assets.GetAsset<AudioAsset>("sfx/bomb_explode.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Player.Position });
    }

    public override void OnEffectUpdate()
    {
        if (Util.OneTime(ElapsedTime >= 0.25f, ref Exploded))
        {
            Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("death_sniped");
            if (Network.IsServer)
            {
                Player.ServerKillPlayer();
            }
            var explosionEntity = Entity.Create();
            explosionEntity.Position = Player.Position;
            var animator = explosionEntity.AddComponent<Spine_Animator>();
            animator.SpineInstance.SetSkeleton(Assets.GetAsset<SpineSkeletonAsset>("anims/dynamite/dynamite.spine"));
            animator.SpineInstance.SetAnimation("explode", false);
            animator.DestroyEntityWhenDoneCurrentAnimation = true;

            var range = 2f;
            foreach (var other in GameManager.Instance.PlayersInCurrentMinigame) if (other.Alive() && other.IsDead == false)
            {
                var distance = (other.Position - Player.Position).Length;
                if (distance < range)
                {
                    other.SmashHit((other.Position - Player.Position).Normalized * 10);
                }
            }
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }
}

public partial class HP_PassDynamiteAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override float Cooldown => 1f;
    public override Texture Icon => Assets.GetAsset<Texture>("dynamite.png");

    public static bool TryUpdateClosest<T>(Vector2 position, ref Component componentResult, ref float maxDistance, Predicate<T> predicate = null) where T : Component
    {
        bool result = false;
        foreach (var thing in Scene.Components<T>())
        {
            var sqrDist = (thing.Position - position).LengthSquared;
            if (sqrDist < (maxDistance * maxDistance) && (predicate == null || predicate(thing)))
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
        return base.CanUse() && Player.HasEffect<HP_HoldingDynamiteEffect>() && GetClosestPlayer(out _);
    }
    
    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        var effect = Player.GetEffect<HP_HoldingDynamiteEffect>();
        if (effect.Alive() == false) return false;
        GetClosestPlayer(out MyPlayer target);
        if (Network.IsServer && target.Alive())
        {
            effect.Dynamite.ServerBeginPass(Player, target);
            Log.Info("passing from " + Player.Name + " to " + target.Name);
            return true;
        }

        return target.Alive();
    }

    public static bool CanPassDynamiteToPlayer(MyPlayer player)
    {
        if (player.IsFrontman) return false;
        if (player.IsDead) return false;
        if (player.IsEliminated) return false;
        foreach (var dynamite in Scene.Components<HP_Dynamite>())
        {
            if (dynamite.CurrentPlayer == player || dynamite.PassTargetPlayer == player)
            {
                return false;
            }
        }
        return true;
    }

    public bool GetClosestPlayer(out MyPlayer player)
    {
        float maxDistance = 2.5f;
        Component component = null;
        bool result = TryUpdateClosest<MyPlayer>(Player.Position, ref component, ref maxDistance, predicate: p => p.IsDead == false && p != Player && p.IsValidTarget && CanPassDynamiteToPlayer(p));
        player = (MyPlayer)component;
        return result;
    }
}

public class HP_HoldingDynamiteEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public HP_Dynamite Dynamite;

    public override void NetworkSerialize(AO.StreamWriter writer)
    {
        if (!Dynamite.Alive())
        {
            Dynamite = null;
        }
        
        writer.WriteNetworkedComponent(Dynamite);
    }

    public override void NetworkDeserialize(AO.StreamReader reader)
    {
        Dynamite = reader.ReadNetworkedComponent<HP_Dynamite>();
    }
    
    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.EnableSkin("weapons/dynamite");
        Player.SpineAnimator.SpineInstance.RefreshSkins();
    }

    public override void OnEffectUpdate() { }

    public override void LateUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.RemoveEffect<HotPotatoSlowEffect>(false);
        
        Player.SpineAnimator.SpineInstance.DisableSkin("weapons/dynamite");
        Player.SpineAnimator.SpineInstance.RefreshSkins();
    }
}

public class PulseTextEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public string Text = "Nothing Interesting Happened";
    
    public override void OnEffectStart(bool isDropIn)
    {
    }

    public override void OnEffectUpdate()
    {
        using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
        using var _2 = IM.PUSH_Z(Player.GetZOffset());
        using var _3 = UI.PUSH_PLAYER_MATERIAL(Player);

        var ts = new UI.TextSettings()
        {
            Font = UI.Fonts.BarlowBold,
            Size = 0.25f,
            Color = Vector4.White,
            DropShadowColor = new Vector4(0f,0f,0.02f,0.5f),
            DropShadowOffset = new Vector2(0f,-3f),
            HorizontalAlignment = UI.HorizontalAlignment.Center,
            VerticalAlignment = UI.VerticalAlignment.Center,
            WordWrap = false,
            WordWrapOffset = 0,
            Outline = true,
            OutlineThickness = 3,
            Offset = new Vector2(0, 0),
        };
        
        var pos = Player.Entity.Position + Vector2.Up * 2f;
        pos.Y += AOMath.Lerp(0, 0.5f, Ease.OutQuart(ElapsedTime));
        var rect = new Rect(pos, pos);
        var color01 = Ease.FadeInAndOut(0.1f, 1, ElapsedTime);
        ts.Color = Vector4.Lerp(new Vector4(0, 0, 0, 0), Vector4.Green, color01);
        UI.Text(rect, Text, ts);

        if (ElapsedTime > 1.5f)
        {
            Player.RemoveEffect(this, false);
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }
}

// Powerups

public partial class HP_AirMailAbility : MyAbility
{
    public override float Cooldown => 10f;
    public override bool MonitorEffectDuration => true;
    public override TargettingMode TargettingMode => TargettingMode.Line;
    public override Type TargettingEffect => typeof(HP_AirMailAimingEffect);
    public override float MaxDistance => 10;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/air_mail_icon.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.HP_AirMail) && Player.HasEffect<HP_HoldingDynamiteEffect>();
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        var effect = Player.GetEffect<HP_HoldingDynamiteEffect>();
        if (effect.Alive() == false) return false;

        HP_Dynamite dynamiteToShoot = null;
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.HP_AirMail) == false)
            {
                return false;
            }

            dynamiteToShoot = effect.Dynamite;
            effect.Dynamite.IsAirMail.Set(true);
            CallClient_OnActivate(Player);
        }

        var projectileEntity = Game.SpawnProjectile(Player, "AirMailProjectile.prefab", "air_mail_projectile", Player.Position, positionOrDirection);
        var dart = projectileEntity.GetComponent<AirMailProjectile>();
        dart.OwningPlayer = Player;
        dart.Sprite.Entity.LocalRotation = AOMath.ToDegrees(MathF.Atan2(positionOrDirection.Y, positionOrDirection.X));
        if (Network.IsServer)
        {
            dart.ServerDynamite = dynamiteToShoot;
        }
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("throw");

        return true;
    }

    [ClientRpc]
    public static void OnActivate(MyPlayer player)
    {
        player.RemoveEffect<HP_HoldingDynamiteEffect>(false);
    }
}

public class HP_AirMailAimingEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override bool GetInterruptedByNewActiveEffects => true;

    public override List<Type> AbilityWhitelist { get; } = new(){typeof(HP_AirMailAbility)};

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SetMouseIKEnabled(true);
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SetMouseIKEnabled(false);
    }

    public override void OnEffectUpdate()
    {
    }
}

public class AirMailProjectile : Component
{
    [Serialized] public Sprite_Renderer Sprite;
    [Serialized] public Circle_Collider Collider;
    [Serialized] public MyPlayer OwningPlayer;

    public HP_Dynamite ServerDynamite; // only valid on server

    public bool AlreadyHitSomething;

    public float Lifetime;

    public override void Awake()
    {
        if (Network.IsClient)
        {
            // Play one of three dart throw variants
            int variant = (int)RNG.RangeInt(ref GameManager.Instance.GlobalRng, 1, 3);
            SFX.Play(Assets.GetAsset<AudioAsset>($"sfx/throw_dart_0{variant}.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Position = Entity.Position });
        }

        Collider.OnCollisionEnter += (other) =>
        {
            if (other.Alive() == false) return;

            if (GameManager.Instance.CurrentMinigame != MinigameHotPotato.Instance)
            {
                return;
            }

            if (AlreadyHitSomething)
            {
                return;
            }
            
            if (other.TryGetComponent<MyPlayer>(out var player))
            {
                if (player == OwningPlayer)
                {
                    return;
                }

                foreach (var dynamite in Scene.Components<HP_Dynamite>())
                {
                    if (dynamite.CurrentPlayer == player || dynamite.PassTargetPlayer == player) // this player is already reserved by another dynamite
                    {
                        return;
                    }
                }

                AlreadyHitSomething = true;
                if (Network.IsServer)
                {
                    ServerHitPlayer(player);
                }
                Entity.Destroy();
            }
        };
    }

    public void ServerHitPlayer(MyPlayer player)
    {
        Util.Assert(Network.IsServer);
        ServerDynamite.ServerPlaceDynamiteOnPlayer(player);
    }

    public override void Update()
    {
        Sprite.Entity.LocalRotation = (Time.TimeSinceStartup * 360f * 2f) % 360;
        
        Lifetime += Time.DeltaTime;
        if (Lifetime > 1.0f)
        {
            if (AlreadyHitSomething == false)
            {
                AlreadyHitSomething = true;
                if (Network.IsServer)
                {
                    ServerHitPlayer(OwningPlayer);
                }

                Entity.Destroy();
            }
        }
    }
}

public partial class HP_ShadowDecoyAbility : MyAbility
{
    public override float Cooldown => 10f;
    public override bool MonitorEffectDuration => true;
    public override bool EffectIsCancelable => true;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/shadow_decoy.png");
    public override TargettingMode TargettingMode => TargettingMode.Self;
    
    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.HP_ShadowDecoy);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Player.HasEffect<HP_ShadowDecoyEffect>())
        {
            return false;
        }

        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.HP_ShadowDecoy) == false)
            {
                return false;
            }
            
            string[] texturePath = {
                "environments/hot_potato/seashell1.png",
                "environments/hot_potato/seashell2.png",
                "environments/hot_potato/trashcanA.png",
                "environments/hot_potato/trashcanB.png",
                "environments/hot_potato/branch1.png",
                "environments/hot_potato/branch2.png",
                "environments/hot_potato/pines/pine1.png",
                "environments/hot_potato/pines/pine2.png",
            };
            
            CallClient_ActivateShadowDecoy(Player, texturePath.GetRandom());
        }

        return true;
    }

    [ClientRpc]
    public static void ActivateShadowDecoy(MyPlayer player, string spritePath)
    {
        player.RemoveEffect<HP_ShadowDecoyEffect>(true);
        player.AddEffect<HP_ShadowDecoyEffect>(preInit: e => e.SpritePath = spritePath);
    }
}

public class HP_ShadowDecoyEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override float DefaultDuration => 10f;

    public string SpritePath;
    
    public override void OnEffectStart(bool isDropIn)
    {
        var renderer = AddComponent<Sprite_Renderer>();
        renderer.Sprite = Assets.GetAsset<Texture>(SpritePath);
        
        if (Player.IsLocal)
        {
            Player.GetAbility<HP_ShadowDecoyAbility>().AppliedEffect = this;
        }

        if (isDropIn == false)
        {
            Player.AddInvisibilityReason(nameof(HP_ShadowDecoyEffect));
            Player.AddNameInvisibilityReason(nameof(HP_ShadowDecoyEffect));
        }
        Player.NameInvisCounter += 1;
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.NameInvisCounter -= 1;
        Player.RemoveInvisibilityReason(nameof(HP_ShadowDecoyEffect));
        Player.RemoveNameInvisibilityReason(nameof(HP_ShadowDecoyEffect));
    }
}

public partial class HP_TimeOutAbility : MyAbility
{
    public override float Cooldown => 10f;
    public override bool MonitorEffectDuration => true;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/time_out.png");
    public override TargettingMode TargettingMode => TargettingMode.Self;

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.HP_TimeOut);
    }
    
    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Player.HasEffect<HP_ShadowDecoyEffect>())
        {
            return false;
        }

        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.HP_TimeOut) == false)
            {
                return false;
            }
            
            CallClient_ActivateTimeOut(Player);
        }

        return true;
    }

    [ClientRpc]
    public static void ActivateTimeOut(MyPlayer player)
    {
        Notifications.Show($"{player.Name} called a Timeout!");
        
        foreach (var target in GameManager.Instance.PlayersInCurrentMinigame) if (target.Alive() && target.IsDead == false)
        {
            if (target == player)
                continue;

            target.AddEffect<HP_TimeOutEffect>(duration: 3);
        }

    }
}

public class HP_TimeOutEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => true;

    public override void OnEffectStart(bool isDropIn) { }

    public override void OnEffectUpdate()
    {
        if (Player.IsLocal)
        {
            var rect = UI.SafeRect.TopCenterRect().Grow(20, 50, 20, 50);
            rect.Offset(0, -200);
            
            var textSettings = GameManager.GetTextSettings(15f);
            textSettings.Color = new Vector4(1, 0, 0, 1);

            UI.Text(rect, $"Timeout: {DurationRemaining}", textSettings);
        }
    }

    public override void OnEffectEnd(bool interrupt) { }
}

public class HP_OilSpillAbility : MyAbility 
{
    public override TargettingMode TargettingMode => TargettingMode.CircleAOE;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override float Cooldown => 1f;
    public override float MaxDistance => 10f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/oill_spill.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.HP_OilSpill);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.HP_OilSpill) == false)
            {
                return false;
            }
            HP_OilSpill.Spawn(Player.Position + positionOrDirection * magnitude, Player);
        }

        return true;
    }
}

public partial class HP_OilSpill : Component
{
    public MyPlayer Owner;

    public static HP_OilSpill Spawn(Vector2 position, MyPlayer owner)
    {
        var instance = Entity.Instantiate(Assets.GetAsset<Prefab>("OilSpill.prefab")).GetComponent<HP_OilSpill>();
        instance.Entity.Position = position;
        Network.Spawn(instance.Entity);
        instance.CallClient_SetOwner(owner);
        return instance;
    }

    public override void Awake()
    {
        if (Network.IsServer)
        {
            GetComponent<Polygon_Collider>().OnCollisionEnter += entity =>
            {
                if (entity.Alive() == false) return;

                if (!Network.IsServer || !entity.TryGetComponent(out MyPlayer player) || !player.Alive() || player.IsDead || player == Owner)
                {
                    return;
                }

                if (player.IsFrontman)
                {
                    return;
                }

                if (player.HasEffect<ImmuneToOilSpillEffect>())
                {
                    return;
                }
            
                CallClient_SlipPlayer(player);
            };
        }
    }

    [ClientRpc]
    public void SetOwner(MyPlayer owner)
    {
        Owner = owner;
        GetComponent<Sprite_Renderer>().Tint = new Vector4(1, 1, 1, owner.IsLocal ? 0.5f : 0f);
    }   

    [ClientRpc]
    public void SlipPlayer(MyPlayer player)
    {
        GetComponent<Sprite_Renderer>().Tint = new Vector4(1, 1, 1, 1f);
        player.AddEffect<BananaPeelSlipEffect>();
        player.AddEffect<ImmuneToOilSpillEffect>(duration: 10f);
    }
}

public class ImmuneToOilSpillEffect : MyEffect
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

public class HP_SpringGloveAbility : MyAbility
{
    public override float Cooldown => 10f;
    public override bool MonitorEffectDuration => true;
    public override TargettingMode TargettingMode => TargettingMode.Line;
    public override Type TargettingEffect => typeof(HP_SpringGloveAimingEffect);
    public override Type Effect => typeof(HP_SpringGlovePunchEffect);
    public override float MaxDistance => 10;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/spring_boxing_glove_icon.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.HP_SpringGlove);
    }
}

public class HP_SpringGloveAimingEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override bool GetInterruptedByNewActiveEffects => true;

    public override List<Type> AbilityWhitelist { get; } = new(){typeof(HP_SpringGlovePunchEffect)};

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SetMouseIKEnabled(true);
        Player.SpineAnimator.SpineInstance.StateMachine.SetBool("use_boxing_glove", true);
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SetMouseIKEnabled(false);
        Player.SpineAnimator.SpineInstance.StateMachine.SetBool("use_boxing_glove", false);
    }

    public override void OnEffectUpdate()
    {
    }
}

public class HP_SpringGlovePunchEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override float DefaultDuration => 1f;

    public override void OnEffectStart(bool isDropIn)
    {
        if (!isDropIn)
        {
            if (Network.IsServer)
            {
                Player.ServerTryConsumePowerup(PowerupKind.HP_SpringGlove);
            }

            Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("glove_punch");
        }
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetBool("use_boxing_glove", false);
    }
}

public class HP_MagnetTrapAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.CircleAOE;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override float Cooldown => 1f;
    public override float MaxDistance => 10f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/magnet_trap.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.HP_MagnetTrap);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.HP_MagnetTrap) == false)
            {
                return false;
            }
            HP_MagnetTrap.Spawn(Player.Position + positionOrDirection * magnitude, Player);
        }

        return true;
    }
}

public partial class HP_MagnetTrap : Component
{
    public MyPlayer Owner;

    public int State = 0;
    public float DisappearTimer = 2;
    public float DespawnTimer = 1;

    public static HP_MagnetTrap Spawn(Vector2 position, MyPlayer owner)
    {
        var instance = Entity.Instantiate(Assets.GetAsset<Prefab>("MagnetTrap.prefab")).GetComponent<HP_MagnetTrap>();
        instance.Entity.Position = position;
        Network.Spawn(instance.Entity);
        instance.CallClient_SetOwner(owner);
        return instance;
    }

    public override void Awake()
    {
        var anim = GetComponent<Spine_Animator>();
        anim.Awaken();
        
        var sm = StateMachine.Make();
        var layer = sm.CreateLayer("main");

        var activateTrigger = sm.CreateVariable("activate", StateMachineVariableKind.TRIGGER);
        var despawnTrigger = sm.CreateVariable("despawn", StateMachineVariableKind.TRIGGER);

        var appearState = layer.CreateState("trap_appear", 0, false);
        var idleState = layer.CreateState("trap_idle", 0, true);
        var activateState = layer.CreateState("trap_activate", 0, false);
        var loopState = layer.CreateState("trap_activate_idle", 0, true);
        var despawnState = layer.CreateState("trap_disappear", 0, false);
        layer.CreateTransition(appearState, idleState, true);
        layer.CreateTransition(activateState, loopState, true);
        layer.CreateGlobalTransition(activateState).CreateTriggerCondition(activateTrigger);
        layer.CreateGlobalTransition(despawnState).CreateTriggerCondition(despawnTrigger);

        layer.InitialState = appearState;
        anim.SpineInstance.SetStateMachine(sm, Entity);
        
        if (Network.IsServer)
        {
            GetComponent<Circle_Collider>().OnCollisionEnter += entity =>
            {
                if (entity.Alive() == false) return;

                if (Network.IsServer == false)
                {
                    return;
                }

                if (!entity.TryGetComponent(out MyPlayer player) || !player.Alive() || player.IsDead || player == Owner || player.HasEffect<HP_HoldingDynamiteEffect>())
                {
                    return;
                }

                float closestDistance = float.MaxValue;
                MyPlayer closestPlayerWithBomb = null;
                foreach (var other in GameManager.Instance.PlayersInCurrentMinigame) if (other.Alive() && other.IsDead == false)
                {
                    var effect = other.GetEffect<HP_HoldingDynamiteEffect>();
                    if (effect.Alive())
                    {
                        var distance = (other.Position - player.Position).LengthSquared;
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            closestPlayerWithBomb = other;
                        }
                    }
                }

                if (closestPlayerWithBomb.Alive())
                {
                    CallClient_TrapPlayer(player, closestPlayerWithBomb);
                }
            };
        }
    }

    public override void Update()
    {
        if (State == 1)
        {
            DisappearTimer -= Time.DeltaTime;

            if (DisappearTimer <= 0)
            {
                GetComponent<Spine_Animator>().SpineInstance.StateMachine.SetTrigger("despawn");
                State = 2;
            }
        }
        else if (State == 2)
        {
            DespawnTimer -= Time.DeltaTime;

            if (DespawnTimer <= 0)
            {
                if (Network.IsServer)
                {
                    Network.Despawn(Entity);
                    Entity.Destroy();
                }
            }
        }
    }

    [ClientRpc]
    public void SetOwner(MyPlayer owner)
    {
        Owner = owner;
        GetComponent<Spine_Animator>().SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, owner.IsLocal ? 0.5f : 0f);
    }   

    [ClientRpc]
    public void TrapPlayer(MyPlayer player, MyPlayer playerWithBomb)
    {
        GetComponent<Spine_Animator>().SpineInstance.StateMachine.SetTrigger("activate");
        GetComponent<Spine_Animator>().SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, 1f);

        player.AddEffect<HP_MoveTowardsMagnetTrap>(duration: 1f, preInit: e => e.EndPosition = Entity.Position + new Vector2(0.5f, 0));
        playerWithBomb.AddEffect<HP_MoveTowardsMagnetTrap>(duration: 1f, preInit: e => e.EndPosition = Entity.Position - new Vector2(0.5f, 0));

        State = 1;
    }
}

public class HP_MoveTowardsMagnetTrap : MyEffect
{
    public override bool IsActiveEffect => false;
    public override bool BlockAbilityActivation => true;
    public override bool DisableMovementInput => true;
    public override bool IsValidTarget => false;

    public Vector2 StartPosition;
    public Vector2 EndPosition;
    
    public override void OnEffectStart(bool isDropIn)
    {
        StartPosition = Player.Position;
    }

    public override void OnEffectUpdate()
    {
        if (Network.IsServer)
        {
            Player.Entity.Position = Vector2.Lerp(StartPosition, EndPosition, DurationProgress01);
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }

}

public class HotPotatoSlowEffect : MyEffect
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