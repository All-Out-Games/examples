using AO;
using System.Collections;

public partial class MinigameRLGL : MinigameInstance
{
    public static MinigameRLGL _instance;
    public static MinigameRLGL Instance
    {
        get
        {
            if (_instance == null)
            {
                foreach (var component in Scene.Components<MinigameRLGL>())
                {
                    _instance = component;
                    _instance.Awaken();
                    break;
                }
            }
            return _instance;
        }
    }

    [Serialized] public Box_Collider FinishCollider;

    [Serialized] public Entity StartLine;
    [Serialized] public Entity EndLine;

    [Serialized] public Entity GirlEyesClosedJitter;
    [Serialized] public Sprite_Renderer GirlEyesClosed;
    [Serialized] public Sprite_Renderer GirlEyesOpen;

    public SyncVar<bool> FrontmanIsControlling = new();
    public SyncVar<bool> LightsReversed = new();

    public SyncVar<bool> IsGreen = new();

    [NetSync] public float ServerTimer;
    [NetSync] public float YellowTimer;

    public class LaserVFX
    {
        public Vector2 TargetPosition;
        public float StartTime;
        public float PulseTime;

        public LaserVFX(Vector2 targetPosition, float startTime)
        {
            TargetPosition = targetPosition;
            StartTime = startTime;
            PulseTime = 0f;
        }
    }

    public List<LaserVFX> ActiveLasers = new();
    public const float LaserDuration = 0.35f;
    public const float PulseSpeed = 8f;
    public const float PulseIntensity = 0.2f;
    public const float InitialLaserWidth = 0.1f;
    public const float FinalLaserWidth = 0.5f;

    public override void Awake()
    {
        IsGreen.OnSync += (_, newVal) =>
        {
            if (GameManager.Instance.CurrentMinigame != this)
                return;
            
            if (newVal)
            {
                SFX.Play(Assets.GetAsset<AudioAsset>("sfx/greenlight-better.wav"), new SFX.PlaySoundDesc { Volume=0.25f });
                GameManager.Instance.UnmuteCurrentTheme();
            }
            else
            {
                SFX.Play(Assets.GetAsset<AudioAsset>("sfx/redlight-better.wav"), new SFX.PlaySoundDesc { Volume=0.25f });
                GameManager.Instance.MuteCurrentTheme();    
            }
        };
        
        if (Network.IsServer)
        {
            FinishCollider.OnCollisionEnter += (other) =>
            {
                if (other.Alive() == false) return;

                if (GameManager.Instance.CurrentMinigame != this)
                {
                    return;
                }
                
                var player = other.GetComponent<MyPlayer>();
                if (player.Alive() == false) return;

                if (GameManager.Instance.State != GameState.RunningMinigame)
                {
                    return;
                }
                
                if (player.IsDead)
                {
                    return;
                }

                if (player.TryGetEffect<RLGLAimedAtEffect>(out var e))
                {
                    CallClient_RemoveAimedEffectBecausePlayerFinished(player);
                }

                player.RLGLFinished.Set(true);
                player.RLGLDistanceReached = 100;

                if (GameManager.Instance.FrontmanEnabled == false)
                {
                    int countNotFinished = 0;
                    int countFinished = 0;
                    foreach (var check in GameManager.Instance.PlayersInCurrentMinigame) if (check.Alive() && check.IsEliminated == false)
                    {
                        if (check.RLGLFinished == false)
                        {
                            countNotFinished += 1;
                        }
                        else
                        {
                            countFinished += 1;
                        }
                    }

                    var countToEliminate = GameManager.Instance.CalculatePlayerCountToEliminateThisRound();
                    if (countNotFinished <= countToEliminate && countFinished > 0) // countFinished > 0 is for when only one player is in the game (likely during testing)
                    {
                        GameManager.Instance.ServerEndMinigame();
                    }
                }
            };
        }
    }

    [ClientRpc]
    public void RemoveAimedEffectBecausePlayerFinished(MyPlayer player)
    {
        player.RemoveEffect<RLGLAimedAtEffect>(true);
    }

    public override void Update()
    {
        if (GameManager.Instance.CurrentMinigame == this && GameManager.Instance.State == GameState.NextGameScreen)
        {
            if (Time.TimeSinceStartup % 2f < 1.9f)
            {
                GirlEyesOpen.LocalEnabled = true;
                GirlEyesClosed.LocalEnabled = false;
            }
            else
            {
                GirlEyesOpen.LocalEnabled = false;
                GirlEyesClosed.LocalEnabled = true;
            }
        }
    }

    public override void MinigameSetup()
    {
        Assets.KeepLoaded<Texture>("rlgl_ui/active_yellow.png", synchronous: false);
        Assets.KeepLoaded<Texture>("rlgl_ui/active_green.png",  synchronous: false);
        Assets.KeepLoaded<Texture>("rlgl_ui/active_red.png",    synchronous: false);
        if (Network.IsServer)
        {
            FrontmanIsControlling.Set(false);
            LightsReversed.Set(false);
            foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
            {
                player.ServerTryGivePowerup(PowerupKind.RLGL_LightningDash);
            }
        }
    }

    public override void MinigameStart()
    {
        if (Network.IsServer)
        {
            GameManager.Instance.EnableMinigameTimer(60 * 2);
            GoGreen();
        }
    }

    public void GoGreen()
    {
        ServerTimer = RNG.RangeFloat(ref GameManager.Instance.GlobalRng, 3, 5);
        if (LightsReversed)
        {
            YellowTimer = 1f;
        }
        IsGreen.Set(true);
    }

    public void GoRed()
    {
        ServerTimer = RNG.RangeFloat(ref GameManager.Instance.GlobalRng, 2, 3f);
        if (LightsReversed == false)
        {
            YellowTimer = 1f;
        }
        IsGreen.Set(false);
    }

    public void AddLaserVFX(Vector2 targetPosition)
    {
        ActiveLasers.Add(new LaserVFX(targetPosition, Time.TimeSinceStartup));
    }

    public override void MinigameTick()
    {
        if (Network.IsServer)
        {
            if (YellowTimer > 0)
            {
                YellowTimer -= Time.DeltaTime;
            }


            if (YellowTimer <= 0f)
            {
                if (FrontmanIsControlling == false)
                {
                    ServerTimer = ServerTimer - Time.DeltaTime;
                    if (ServerTimer <= 0)
                    {
                        if (IsGreen)
                        {
                            GoRed();
                        }
                        else
                        {
                            GoGreen();
                        }
                    }
                }
            }

            bool canSnipePlayers = false;
            if (YellowTimer <= 0f)
            {
                if (LightsReversed == false && IsGreen == false)
                {
                    canSnipePlayers = true;
                }
                else if (LightsReversed && IsGreen)
                {
                    canSnipePlayers = true;
                }
            }

            if (canSnipePlayers)
            {
                foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false && player.RLGLFinished == false)
                {
                    if (player.HasEffect<RLGLSpawnProtectionEffect>()) continue;
                    if (player.HasEffect<RLGLAimedAtEffect>())         continue;
                    if (player.IsValidTarget == false)                 continue;
                    if (player.IsFrontman)                             continue;

                    if (player.InputThisFrame.Length > 0 || 
                        player.HasEffect<RLGL_IcePatchEffect>() ||
                        player.HasEffect<RLGL_ForcedDanceEffect>())
                    {
                        CallClient_SniperTargetPlayer(player);
                    }
                }
            }
        }
        else
        {
            GirlEyesClosedJitter.LocalPosition = default;
            if (YellowTimer > 0)
            {
                GirlEyesClosed.LocalEnabled = true;
                GirlEyesOpen.LocalEnabled = false;
                GirlEyesClosedJitter.LocalPosition = Util.RandomPositionOnUnitDisc(ref GameManager.Instance.GlobalRng) * 0.035f;
            }
            else if (IsGreen)
            {
                GirlEyesClosed.LocalEnabled = true;
                GirlEyesOpen.LocalEnabled = false;
            }
            else
            {
                GirlEyesClosed.LocalEnabled = false;
                GirlEyesOpen.LocalEnabled = true;
            }

            var isSafe = false;
            var me = (MyPlayer)Network.LocalPlayer;
            if (me.Alive() && me.IsPartOfTheCurrentTournament && !me.IsEliminated && me.RLGLFinished)
            {
                isSafe = true;
            }

            var tex = Assets.GetAsset<Texture>("rlgl_ui/active_green.png");
            var lightRect = UI.ScreenRect;
            lightRect.Max.Y = UI.SafeRect.Max.Y;
            lightRect = lightRect.TopRect().Grow(0, 0, 100, 0).Offset(0, -10).FitAspect(tex.Aspect);
            {
                using var _32 = UI.PUSH_COLOR_MULTIPLIER(new Vector4(0.5f, 0.5f, 0.5f, 1.0f), isSafe);
                if (YellowTimer > 0)
                {
                    UI.Image(lightRect, Assets.GetAsset<Texture>("rlgl_ui/active_yellow.png"));
                }
                else if (IsGreen)
                {
                    UI.Image(lightRect, Assets.GetAsset<Texture>("rlgl_ui/active_green.png"));
                }
                else
                {
                    UI.Image(lightRect, Assets.GetAsset<Texture>("rlgl_ui/active_red.png"));
                }
            }

            if (isSafe)
            {
                UI.Text(lightRect, "SAFE", GameManager.GetTextSettings(100));
            }

            // Draw all active lasers
            for (int i = ActiveLasers.Count - 1; i >= 0; i--)
            {
                var laser = ActiveLasers[i];

                // Remove expired lasers
                if (Time.TimeSinceStartup - laser.StartTime > LaserDuration)
                {
                    ActiveLasers.RemoveAt(i);
                    continue;
                }

                // Update pulse time
                laser.PulseTime += Time.DeltaTime;
                float pulse = 1f + (MathF.Sin(laser.PulseTime * PulseSpeed) * PulseIntensity);

                // Calculate progress for width growth and fade
                float progress = (Time.TimeSinceStartup - laser.StartTime) / LaserDuration;
                float width = AOMath.Lerp(InitialLaserWidth, FinalLaserWidth, progress);
                float fadeMultiplier = 1f - progress;

                // Draw laser layers
                using (var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD))
                using (var _2 = UI.PUSH_LAYER(-10))
                {
                    // Outer glow
                    var outerRect = new Rect(laser.TargetPosition, laser.TargetPosition).Grow(200, width * 2f * pulse, 0, width * 2f * pulse);
                    UI.Image(outerRect, null, new Vector4(1, 0, 0, 0.3f * fadeMultiplier));

                    // Main beam
                    var mainRect = new Rect(laser.TargetPosition, laser.TargetPosition).Grow(200, width * pulse, 0, width * pulse);
                    UI.Image(mainRect, null, new Vector4(1, 0, 0, 0.6f * fadeMultiplier));

                    // Core
                    var coreRect = new Rect(laser.TargetPosition, laser.TargetPosition).Grow(200, width * 0.4f * pulse, 0, width * 0.4f * pulse);
                    UI.Image(coreRect, null, new Vector4(1, 0.3f, 0.3f, 1f * fadeMultiplier));
                }
            }
        }
    }

    public override void MinigameLateTick()
    {
        if (Network.IsServer)
        {
            foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false && player.RLGLFinished == false)
            {
                var t = (player.Position.Y - StartLine.Position.Y) / (EndLine.Position.Y - StartLine.Position.Y);
                int distance = (int)(t * 100);
                if (distance > player.RLGLDistanceReached)
                {
                    player.RLGLDistanceReached = distance;
                }
            }   
        }
    }

    public override void MinigameEnd()
    {
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
            if (a.RLGLFinished && !b.RLGLFinished)
            {
                return -1;
            }
            else if (b.RLGLFinished && !a.RLGLFinished)
            {
                return 1;
            }
            return GetPlayerScore(b).CompareTo(GetPlayerScore(a));
        });
        return sortedPlayers;
    }

    public override int GetPlayerScore(MyPlayer player)
    {
        return player.RLGLDistanceReached;
    }

    public override string LeaderboardPointsHeader()
    {
        return "Distance Reached";
    }

    [ClientRpc]
    public void SniperTargetPlayer(MyPlayer player)
    {
        player.AddEffect<RLGLAimedAtEffect>();
    }

    public override bool ControlsRespawning() => false;
}

public partial class RLGLAimedAtEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public float PulseTime = 0f;
    public const float TelegraphPulseSpeed = 8f;
    public const float TelegraphPulseIntensity = 0.1f;

    public override void OnEffectStart(bool isDropIn)
    {
    }

    public override void OnEffectUpdate()
    {
        // Show telegraph during the aiming
        PulseTime += Time.DeltaTime;
        float pulse = 1f + (MathF.Sin(PulseTime * TelegraphPulseSpeed) * TelegraphPulseIntensity);

        using (var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD))
        using (var _2 = UI.PUSH_LAYER(-10))
        {
            var rect = new Rect(Player.Position, Player.Position).Grow(300, 0.25f * pulse, 0, 0.25f * pulse);
            UI.Image(rect, null, new Vector4(1, 0, 0, 0.3f));
        }

        if (ElapsedTime > 1f)
        {
            if (Network.IsServer)
            {
                bool hasShield = Player.HasEffect<RLGL_ShieldEffect>();
                Player.ServerKillPlayer();
                CallClient_SnipePlayer(Player, hasShield);
            }
        }
    }

    [ClientRpc]
    public static void SnipePlayer(MyPlayer player, bool playerToSnipeHasShield)
    {
        // Add the laser VFX at the moment of the shot
        MinigameRLGL.Instance.AddLaserVFX(player.Position);
        SFX.Play(Assets.GetAsset<AudioAsset>("sfx/rlgl_shoot_player.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = player.Position });

        player.RemoveEffect<RLGLAimedAtEffect>(false);

        if (playerToSnipeHasShield)
        {
            player.RemoveEffect<RLGL_ShieldEffect>(true);
        }
        else if (!player.HasEffect<RLGL_IronGolemEffect>())
        {
            player.AddEffect<RLGLDeathEffect>();
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }
}

public class SquidGamesTargettingEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override bool GetInterruptedByNewActiveEffects => true;

    public override void OnEffectStart(bool isDropIn) {}
    public override void OnEffectUpdate() {}
    public override void OnEffectEnd(bool interrupt) {}
}

public class RLGLSpawnProtectionEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override void OnEffectStart(bool isDropIn) { }
    public override void OnEffectUpdate() { }
    public override void OnEffectEnd(bool interrupt) { }
}

public class RLGLDeathEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => true;

    public bool Sniped;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.TryLocalSetCurrentTargettingAbility(null);
        Player.RemoveEffect<RLGLAimedAtEffect>(true);
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("death_sniped");
        DurationRemaining = 1.5f;
    }

    public override void OnEffectUpdate()
    {
        if (ElapsedTime >= 1.0f)
        {
            Player.RemoveEffect(this, false);
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
        if (Network.IsServer)
        {
            if (interrupt == false) // i.e. we're not ending the minigame or something
            {
                Player.ServerRespawnOrGoIntoSpectator();
                Player.AddEffect<RLGLSpawnProtectionEffect>(duration: 0.5f);
            }
        }
    }
}

public partial class RLGL_ShieldAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override float Cooldown => 5f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/shield_icon.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.RLGL_Shield);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        if (Player.HasEffect<RLGL_ShieldEffect>())
        {
            return false;
        }

        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_Shield) == false)
            {
                return false;
            }
            CallClient_EnableShield(Player);
        }

        return true;
    }

    [ClientRpc]
    public static void EnableShield(MyPlayer player)
    {
        player.RemoveEffect<RLGL_ShieldEffect>(true);
        player.AddEffect<RLGL_ShieldEffect>(duration: 10f);
    }
}

public class RLGL_ShieldEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public RLGLShield Shield;
    
    public override void OnEffectStart(bool isDropIn)
    {
        Shield = RLGLShield.Create(Entity);
        
        
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        if (Shield.Alive())
        {
            Shield.CallDestroy();
        }
    }
}

public class RLGLShield : Component
{    
    public static RLGLShield Create(Entity parent)
    {
        var instance = Entity.Instantiate(Assets.GetAsset<Prefab>("RLGLShield.prefab")).GetComponent<RLGLShield>();
        if (parent.Alive())
        {
            instance.Entity.SetParent(parent, false);
            instance.Entity.LocalPosition = Vector2.Up * 0.65f;
            instance.Entity.LocalScale = Vector2.One * 2f;
        }

        return instance;
    }

    public override void Awake()
    {
        var skeleton = GetComponent<Spine_Animator>();
        skeleton.Awaken();
        var sm = StateMachine.Make();
        var layer = sm.CreateLayer("main");
        var activateState = layer.CreateState("activate", 0, false);
        var loopState = layer.CreateState("loop", 0, true);
        var endState = layer.CreateState("end", 0, false);
        var fadeOutTrigger = sm.CreateVariable("fade_out", StateMachineVariableKind.TRIGGER);
        layer.CreateTransition(activateState, loopState, false);
        layer.CreateGlobalTransition(endState).CreateTriggerCondition(fadeOutTrigger);
        layer.InitialState = activateState;
        skeleton.SpineInstance.SetStateMachine(sm, Entity);
    }

    public void CallDestroy()
    {
        Coroutine.Start(Entity, DestroyRoutine());  
        IEnumerator DestroyRoutine()
        {
            GetComponent<Spine_Animator>().SpineInstance.StateMachine.SetTrigger("fade_out");
            yield return 0.5f;
            Entity.Destroy();
        }
    }
}

//

public partial class RLGL_LightningDashAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.FiniteLine;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override float MaxDistance => 2;
    public override float Cooldown => 1f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/lightning_dash_icon.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.RLGL_LightningDash);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_LightningDash) == false && Player.ServerTryConsumePowerup(PowerupKind.HP_LightningDash) == false)
            {
                return false;
            }
            CallClient_Activate(Player, positionOrDirection);
        }
        return true;
    }

    [ClientRpc]
    public static void Activate(MyPlayer player, Vector2 direction)
    {
        player.AddEffect<RLGL_LightningDashEffect>(preInit: e => e.AbilityDirection = direction);
    }
}

public class RLGL_LightningDashEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override float DefaultDuration => 0.1f;
    public override bool DisableMovementInput => true;

    public override void OnEffectStart(bool isDropIn)
    {
        if (Network.IsServer)
        {
            Player.Dash = AbilityDirection * 35;
        }
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        if (Network.IsServer)
        {
            Player.Dash = default;
        }
    }
}

//

public partial class RLGL_PigRiderAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override float Cooldown => 1f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/pig_rider_icon.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.RLGL_PigRider);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        if (Player.HasEffect<RLGL_PigRiderEffect>())
        {
            return false;
        }

        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_PigRider) == false)
            {
                return false;
            }
            CallClient_ActivatePigRider(Player);
        }

        return true;
    }

    [ClientRpc]
    public static void ActivatePigRider(MyPlayer player)
    {
        player.RemoveEffect<RLGL_PigRiderEffect>(true);
        player.AddEffect<RLGL_PigRiderEffect>();
    }
}

public partial class RLGL_PigRiderEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override float DefaultDuration => 5f;

    public Vector2 LastKnownInput;
    public float PigCooldown;

    public override void OnEffectStart(bool isDropIn)
    {
        LastKnownInput = Player.InputThisFrame;

        if (LastKnownInput.LengthSquared <= 0)
        {
            LastKnownInput = new Vector2(Random.Shared.NextFloat(-1f, 1f), Random.Shared.NextFloat(-1f, 1f)).Normalized;
        }

        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("pig_ride_loop");
    }

    public override void OnEffectUpdate()
    {
        PigCooldown -= Time.DeltaTime;
        Player.SetFacingDirection(LastKnownInput.X > 0);

        if (Player.InputThisFrame.LengthSquared <= 0)
        {
            if (PigCooldown < 0)
            {
                PigCooldown = Random.Shared.NextFloat(0.25f, 3);
                LastKnownInput = new Vector2(Random.Shared.NextFloat(-1f, 1f), Random.Shared.NextFloat(-1f, 1f)).Normalized;
            }
        }
        else
        {
            LastKnownInput = Player.InputThisFrame;
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).ResetToInitialState();
    }
}

//

public partial class RLGL_IronGolemAbility : MyAbility 
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override float Cooldown => 10f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/iron_golem_icon.png");
    public override bool MonitorEffectDuration => true;

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.RLGL_IronGolem);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Player.HasEffect<RLGL_IronGolemEffect>())
        {
            return false;
        }

        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_IronGolem) == false)
            {
                return false;
            }
            CallClient_ActivateIronGolem(Player);
        }

        return true;
    }

    [ClientRpc]
    public static void ActivateIronGolem(MyPlayer player)
    {
        player.RemoveEffect<RLGL_IronGolemEffect>(true);
        player.AddEffect<RLGL_IronGolemEffect>();
    }
}

public partial class RLGL_IronGolemEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override float DefaultDuration => 10f;

    private Spine_Animator Spine;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.LocalEnabled = false;

        Spine = Entity.AddComponent<Spine_Animator>();
        Spine.Awaken();
        Spine.SpineInstance.SetSkeleton(Assets.GetAsset<SpineSkeletonAsset>("anims/009SG_golem/009SG_golem.spine"));
        Spine.SpineInstance.SetSkin("golem");
        Spine.SpineInstance.RefreshSkins();
        var sm = StateMachine.Make();
        var layer = sm.CreateLayer("main");
        var idleState = layer.CreateState("VIL109/idle", 0, true);
        var runState = layer.CreateState("VIL109/run", 0, true);
        var moveBool = sm.CreateVariable("move", StateMachineVariableKind.BOOLEAN);
        layer.CreateTransition(idleState, runState, false).CreateBoolCondition(moveBool, true);
        layer.CreateTransition(runState, idleState, false).CreateBoolCondition(moveBool, false);
        layer.InitialState = idleState;
        Spine.SpineInstance.SetStateMachine(sm, Entity);

    }

    public override void OnEffectUpdate()
    {
        Spine.SpineInstance.StateMachine.SetBool("move", Player.InputThisFrame.LengthSquared > 0);
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SpineAnimator.LocalEnabled = true;
    }
}

//

public partial class RLGL_SnowboardAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Line;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override float Cooldown => 1f;
    public override float MaxDistance => 3f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/snowboard_icon.png");
    public override bool MonitorEffectDuration => true;

    public override bool CanUse()
    {
        return base.CanUse() && (CheckHasPowerup(PowerupKind.RLGL_Snowboard) || CheckHasPowerup(PowerupKind.Mingle_Snowboard));
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Player.HasEffect<RLGL_SnowboardEffect>())
        {
            return false;
        }

        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_Snowboard) == false && Player.ServerTryConsumePowerup(PowerupKind.Mingle_Snowboard) == false)
            {
                return false;
            }
            CallClient_ActivateSnowboard(Player, positionOrDirection);
        }

        return true;
    }

    [ClientRpc]
    public static void ActivateSnowboard(MyPlayer player, Vector2 direction)
    {
        player.RemoveEffect<RLGL_SnowboardEffect>(true);
        player.AddEffect<RLGL_SnowboardEffect>(preInit: e => e.AbilityDirection = direction);
    }
}

public partial class RLGL_SnowboardEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override float DefaultDuration => 3f;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("on_snowboard_loop");
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).ResetToInitialState();
    }
}

//

public partial class RLGL_IcePatchAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.CircleAOE;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override float Cooldown => 1f;
    public override float MaxDistance => 10f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/ice_patch.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.RLGL_IcePatch);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_IcePatch) == false)
            {
                return false;
            }
            RLGL_IcePatch.Spawn(Player.Position + positionOrDirection * magnitude, Player);
        }

        return true;
    }
}

public partial class RLGL_IcePatch : Component
{
    public bool DestroyHasBeenCalled;
    public MyPlayer Owner;

    public static RLGL_IcePatch Spawn(Vector2 position, MyPlayer owner)
    {
        var instance = Entity.Instantiate(Assets.GetAsset<Prefab>("IcePatch.prefab")).GetComponent<RLGL_IcePatch>();
        instance.Entity.Position = position;
        Network.Spawn(instance.Entity);
        instance.CallClient_SetOwner(owner);
        return instance;
    }

    public override void Awake()
    {
        if (Network.IsServer)
        {
            GetComponent<Polygon_Collider>().OnCollisionEnter += (other) =>
            {
                if (this.Alive() == false) return; // note(josh): necessary for people on old clients right now
                if (other.Alive() == false) return;

                if (other.TryGetComponent(out MyPlayer player) && player != Owner && player.Alive() && player.IsDead == false)
                {
                    CallClient_Activate(player, player.InputThisFrame.LengthSquared > 0 ? player.InputThisFrame.Normalized : new Vector2(Random.Shared.NextFloat(-1f, 1f), Random.Shared.NextFloat(-1f, 1f)).Normalized);
                }
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
    public void Activate(MyPlayer player, Vector2 direction)
    {  
        player.AddEffect<RLGL_IcePatchEffect>(preInit: e => e.Direction = direction);

        GetComponent<Sprite_Renderer>().Tint = new Vector4(1, 1, 1, 1f);

        if (Network.IsServer && DestroyHasBeenCalled == false)
        {
            DestroyHasBeenCalled = true;
            Coroutine.Start(Entity, FadeOutRoutine());

            IEnumerator FadeOutRoutine()
            {
                yield return 2f;
                Network.Despawn(Entity);
            }
        }
    }
}

public class RLGL_IcePatchEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override float DefaultDuration => 3f;

    public Vector2 Direction;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("ice_slide_loop");
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).ResetToInitialState();
    }   
}

public class RLGL_GetUpEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override float DefaultDuration => 1.966f;
    public override bool FreezePlayer => true;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("slip");
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }
    
}

//

public partial class RLGL_DiscoballLauncherAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Line;
    public override Type TargettingEffect => typeof(RLGL_AimDiscoLauncherEffect);

    public override float Cooldown => 1f;
    public override Type Effect => typeof(RLGL_ShootDiscoLauncherEffect);
    public override float MaxDistance => 10f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/disco_ball_icon.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.RLGL_DiscoballLauncher);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_DiscoballLauncher) == false)
            {
                return false;
            }
        }

        return true;
    }
}

public class RLGL_AimDiscoLauncherEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override bool GetInterruptedByNewActiveEffects => true;

    public override List<Type> AbilityWhitelist { get; } = new List<Type>(){typeof(RLGL_DiscoballLauncherAbility)};

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SetMouseIKEnabled(true);

        Player.SpineAnimator.SpineInstance.EnableSkin("weapons/disco_ball_shooter");
        Player.SpineAnimator.SpineInstance.RefreshSkins();
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SetMouseIKEnabled(false);

        if (interrupt)
        {
            Player.SpineAnimator.SpineInstance.DisableSkin("weapons/disco_ball_shooter");
            Player.SpineAnimator.SpineInstance.RefreshSkins();
        }
    }

    public override void OnEffectUpdate() { }
}

public partial class RLGL_ShootDiscoLauncherEffect : MyEffect
{
    public override bool IsActiveEffect => true;

    public override void OnEffectStart(bool isDropIn)
    {
        if (!isDropIn)
        {
            var projectileEntity = Game.SpawnProjectile(Player, "DiscoProjectile.prefab", "disco_ball", Player.Position, AbilityDirection);
            var projectile = projectileEntity.GetComponent<RLGL_DiscoProjectile>();
            projectile.Animator.Entity.LocalRotation = MathF.Atan2(AbilityDirection.Y, AbilityDirection.X) * (180.0f / MathF.PI);
        }
        
        Player.RemoveEffect(this, false);
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SpineAnimator.SpineInstance.DisableSkin("weapons/disco_ball_shooter");
        Player.SpineAnimator.SpineInstance.RefreshSkins();
    }

    public override void OnEffectUpdate() { }
}

public partial class RLGL_DiscoProjectile : Component
{
    [Serialized] public Spine_Animator Animator;

    public float Lifetime;
    public const float MaxLife = 1f;
    public bool AlreadyHitSomething;

    public override void Awake()
    {
        Entity.GetComponent<Projectile>().OnHit += OnHit;
        Animator.Awaken();
        Animator.SpineInstance.SetAnimation("projectile", true);
    }

    public override void Update()
    {
        Lifetime += Time.DeltaTime;
        if (Lifetime > MaxLife)
        {
            Entity.Destroy();
        }
    }

    private void OnHit(Entity other, bool predicted)
    {
        if (!other.Alive() || AlreadyHitSomething || !other.TryGetComponent(out MyPlayer player) || player.IsDead)
        {
            return;
        }

        var projectile = Entity.GetComponent<Projectile>();
        
        if (player == projectile.Owner) return;

        // HIT CONFIRMED
        AlreadyHitSomething = true;
        if (predicted == false)
        {
            CallClient_HitPlayer(player);
        }
        
        Entity.Destroy();
    }
    
    [ClientRpc]
    public static void HitPlayer(MyPlayer target)
    {
        target.AddEffect<RLGL_ForcedDanceEffect>();
    }
}

public class RLGL_ForcedDanceEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool BlockAbilityActivation => true;
    public override bool FreezePlayer => true;
    public override bool DisableMovementInput => true;
    public override float DefaultDuration => 3f;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("dance_loop");

        var animator = AddComponent<Spine_Animator>();
        animator.Awaken();
        animator.SpineInstance.SetSkeleton(Assets.GetAsset<SpineSkeletonAsset>("anims/009SG_uno_reverse/009SG_uno_reverse.spine"));
        var sm = StateMachine.Make();
        var layer = sm.CreateLayer("main");
        var land = layer.CreateState($"land", 0, false);
        var idle = layer.CreateState($"loop", 0, true);
        layer.CreateTransition(land, idle, true);
        layer.InitialState = land;
        animator.SpineInstance.SetStateMachine(sm, Entity);
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).ResetToInitialState();
    }
} 

//

public partial class RLGL_StampedeAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override float Cooldown => 1f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/stampede_icon.png");

    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.RLGL_Stampede);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_Stampede) == false)
            {
                return false;
            }
        }

        return true;
    }
}

//

public partial class RLGL_BananaPeelAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.CircleAOE;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override float Cooldown => 1f;
    public override float MaxDistance => 10f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/banana_peel_icon.png");
    
    public override bool CanUse()
    {
        return base.CanUse() && CheckHasPowerup(PowerupKind.RLGL_BananaPeel);
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_BananaPeel) == false)
            {
                return false;
            }
            LobbedProjectile.Spawn(Player.Position, Player.Position + positionOrDirection * magnitude, Assets.GetAsset<Prefab>("BananaPeelProjectile.prefab"), OnImpact);
        }

        return true;
    }

    public void OnImpact(Vector2 position)
    {
        BananaPeel.Spawn(position);
    }
}

//

public partial class RLGL_GlueBombAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.CircleAOE;
    public override Type TargettingEffect => typeof(SquidGamesTargettingEffect);

    public override float Cooldown => 1f;
    public override float MaxDistance => 10f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/glue_bomb_icon.png");

    public override bool CanUse()
    {
        return base.CanUse() && (CheckHasPowerup(PowerupKind.RLGL_GlueBomb) || CheckHasPowerup(PowerupKind.Mingle_GlueBomb));
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {       
        if (Network.IsServer)
        {
            if (Player.ServerTryConsumePowerup(PowerupKind.RLGL_GlueBomb) == false && Player.ServerTryConsumePowerup(PowerupKind.Mingle_GlueBomb) == false)
            {
                return false;
            }
            LobbedProjectile.Spawn(Player.Position, Player.Position + positionOrDirection * magnitude, Assets.GetAsset<Prefab>("LobbedProjectile.prefab"), OnImpact);
        }

        return true;
    }

    public void OnImpact(Vector2 position)
    {
        GlueTrap.Spawn(position);
    }
}

public partial class LobbedProjectile : Component
{
    [Serialized] public Entity GFXRoot;
    [Serialized] public Spine_Animator Spine;

    [Serialized] public string InAirAnim;
    [Serialized] public string OnImpactAnim;
    [Serialized] public float ImpactAnimDuration;

    public Vector2 StartPosition;
    public Vector2 Direction;
    public float Range;
    public float Speed;
    public Action<Vector2> OnImpact;

    private float Timer = 0;
    private bool Despawned = false;

    //Spawn with spine
    public static LobbedProjectile Spawn(Vector2 start, Vector2 destination, Prefab prefab, Action<Vector2> onImpact, float speed = 5f)
    {
        var instance = Entity.Instantiate(prefab).GetComponent<LobbedProjectile>();

        instance.Entity.Position = start;
        instance.Speed = speed;        
        instance.OnImpact = onImpact;

        Network.Spawn(instance.Entity);
        instance.CallClient_Launch(start, destination, speed, Vector2.Distance(start, destination));   
        return instance;
    }

    [ClientRpc]
    public void Launch(Vector2 start, Vector2 destination, float speed, float range)
    {
        StartPosition = start;
        Direction = (destination - start).Normalized;
        Range = range;
        Speed = speed;
    }

    public override void Awake()
    {
        Spine.Awaken();

        var sm = StateMachine.Make();
        var layer = sm.CreateLayer("main");
        var inAirState = layer.CreateState(InAirAnim, 0, true);
        var onImpactState = layer.CreateState(OnImpactAnim, 0, false);

        var trigger = sm.CreateVariable("land", StateMachineVariableKind.TRIGGER);
        layer.CreateGlobalTransition(onImpactState).CreateTriggerCondition(trigger);

        layer.InitialState = inAirState;
        Spine.SpineInstance.SetStateMachine(sm, Entity);
    }

    public override void Update()
    {
        Timer += Time.DeltaTime;

        var distanceTravelled = Timer * Speed;
        if (distanceTravelled <= Range)
        {
            var height = ParabolaArcHeight(0.35f * Range, Range, distanceTravelled);
            GFXRoot.LocalY = height;
        }

        if (distanceTravelled >= Range)
        {
            Entity.Rotation = 0;
            Spine.SpineInstance.StateMachine.SetTrigger("land");

            if (Network.IsServer)
            {
                if (OnImpact != null)
                {
                    OnImpact(Entity.Position);
                }

                if (Despawned == false)
                {
                    Despawned = true;

                    if (ImpactAnimDuration > 0)
                    {
                        Coroutine.Start(Entity, _DespawnRoutine());
                        IEnumerator _DespawnRoutine()
                        {
                            yield return new WaitForSeconds(ImpactAnimDuration);
                            _Despawn();
                        }
                    }
                    else
                    {
                        _Despawn();
                    }

                    void _Despawn()
                    {
                        Network.Despawn(Entity);
                        Entity.Destroy();
                    }
                }
            }
        }
        else if (Network.IsServer)
        {
            Entity.Position = StartPosition + Direction * distanceTravelled;
        }
    }

    public float ParabolaArcHeight(float height, float range, float x)
    {
        return -height * MathF.Pow(x / (0.5f * range) - 1, 2) + height;
    }
}
