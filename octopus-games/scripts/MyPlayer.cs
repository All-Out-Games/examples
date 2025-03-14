using AO;
using TinyJson;

public partial class MyPlayer : Player
{
    //
    // General
    //

    public const int MaxLevel = 30;

    public float CurrentLeaderboardYOffset;
    public float TargetLeaderboardYOffset;

    public SyncVar<bool> IsDead = new(); // reusable for each minigame

    public SyncVar<bool> IsPartOfTheCurrentTournament = new();
    public SyncVar<bool> IsEliminated = new();
    public SyncVar<bool> WasEliminatedThisRound = new();

    public SyncVar<float> CheatSpeedMultiplier = new(1);

    [NetSync] public float CurrentZoomLevel = 1f;
    public SyncVar<float> CheatZoomMultiplier = new(1f);

    public float LocalCheatZoomMultiplier = 1f;

    public SyncVar<int> PositionInTournamentList = new();

    public SyncVar<Vector2> SpawnForThisMinigame = new();

    public CameraControl CameraControl;

    public SyncVar<int> PlayerNumberForThisTournament = new();

    public SyncVar<bool> IsQueuedForNewGame = new();
    public float QueueStartTime;

    public SyncVar<bool> IsFrontman = new();
    public float FrontmanEndGameButtonHold;
    public Vector2 FrontmanRLGLTargetLerp;
    public bool FrontmanWasTargetingLastFrame;
    public Action FrontmanAbilityToActivate;

    public Vector2 Dash;

    public SyncVar<bool> PrepPhaseReady = new();
    public bool LocalPrepPhaseReady;

    public PowerupKind[] EquippedPowerups = new PowerupKind[3];

    public Vector2 InputOverride;

    [NetSync] public int NameInvisCounter;

    public float TimeLastTookDamage = -1000f;

    public float LastPowerupTime = -1000f;
    public PowerupDefinition LastPowerupDefn;

    public SyncVar<bool> DidLevelUpThisMinigame = new();
    public SyncVar<int> PlayerLevel = new(1);
    public SyncVar<int> PlayerXP = new();

    public SyncVar<bool> UseLivesForMinigame = new();
    public SyncVar<int> MinigameLivesLeft = new();
    public SyncVar<float> ServerTimeRanOutOfLives = new();

    public void ServerSetQueuedForNewGame(bool queued)
    {
        Util.Assert(Network.IsServer);
        if (IsQueuedForNewGame == queued)
        {
            return;
        }
        IsQueuedForNewGame.Set(queued);
        if (IsQueuedForNewGame)
        {
            Network.QueueAddPlayer("main", this);
        }
        else
        {
            Network.QueueRemovePlayer("main", this);
        }
        CallClient_UpdateQueueStartTime(rpcTarget: this);
    }

    [ClientRpc]
    public void UpdateQueueStartTime()
    {
        QueueStartTime = Time.TimeSinceStartup;
    }

    public int CompareLives(MyPlayer other)
    {
        if (MinigameLivesLeft > other.MinigameLivesLeft)
        {
            return -1;
        }
        else if (other.MinigameLivesLeft > MinigameLivesLeft)
        {
            return 1;
        }
        if (MinigameLivesLeft == 0)
        {
            Util.Assert(other.MinigameLivesLeft == 0);
            if (ServerTimeRanOutOfLives > other.ServerTimeRanOutOfLives)
            {
                return -1;
            }
            else if (other.ServerTimeRanOutOfLives > ServerTimeRanOutOfLives)
            {
                return 1;
            }
        }
        return GameManager.Instance.PlayersInCurrentMinigame.IndexOf(this).CompareTo(GameManager.Instance.PlayersInCurrentMinigame.IndexOf(other));
    }

    [ClientRpc]
    public void SpawnTextPopup(string text, Vector2 position, Vector4 color)
    {
        var popup = new TextPopup();
        popup.Text = text;
        popup.Color = color;
        popup.Position = position;
        GameManager.Instance.TextPopups.Add(popup);
    }

    public override bool CanUseAbility(Ability ability)
    {
        if (GameManager.Instance.State != GameState.RunningMinigame)
        {
            return false;
        }
        if (IsDead)
        {
            return false;
        }
        if (IsEliminated)
        {
            return false;
        }
        return true;
    }

    public void ServerEliminate()
    {
        Util.Assert(Network.IsServer);
        IsEliminated.Set(true);
        WasEliminatedThisRound.Set(true);
    }

    public void ServerPutInSpectatorMode()
    {
        CallClient_PutInSpectatorMode();
    }

    public void ServerRespawn()
    {
        IsDead.Set(false);
        CallClient_Respawn();
    }

    public void ServerKillPlayer()
    {
        TryLocalSetCurrentTargettingAbility(null);
        IsDead.Set(true);
        var newLives = MinigameLivesLeft - 1;
        if (newLives < 0) newLives = 0;
        MinigameLivesLeft.Set(newLives);
        if (UseLivesForMinigame && GameManager.Instance.FrontmanEnabled == false)
        {
            if (MinigameLivesLeft > 0)
            {
                GameManager.Instance.KillFeedReport(this, "lost a life");
            }
            else
            {
                ServerTimeRanOutOfLives.Set(Time.TimeSinceStartup);
                GameManager.Instance.KillFeedReport(this, "is out of lives!");
            }
        }
        else
        {
            GameManager.Instance.KillFeedReport(this, "died");
        }
    }

    public void ServerRespawnOrGoIntoSpectator()
    {
        if (GameManager.Instance.FrontmanEnabled == false && ((UseLivesForMinigame && MinigameLivesLeft <= 0) || IsEliminated))
        {
            ServerPutInSpectatorMode();
        }
        else
        {
            ServerRespawn();
        }
    }

    [ClientRpc]
    public void PutInSpectatorMode()
    {
        ClearAllEffects();
        SpineAnimator.SpineInstance.StateMachine.SetTrigger("RESET");
        AddEffect<SpectatorEffect>();
    }

    [ClientRpc]
    public void Respawn()
    {
        ClearAllEffects();
        SpineAnimator.SpineInstance.StateMachine.SetTrigger("RESET");
        Teleport(SpawnForThisMinigame);
    }

    public bool ServerTryGivePowerup(PowerupKind kind)
    {
        Util.Assert(Network.IsServer);
        for (int i = 0; i < EquippedPowerups.Length; i++)
        {
            if (EquippedPowerups[i] == PowerupKind.Invalid)
            {
                EquippedPowerups[i] = kind;
                ServerSyncPowerups();
                return true;
            }
        }
        return false;
    }

    [ClientRpc]
    public void DoPowerupAnimation(int powerup)
    {
        var kind = (PowerupKind)powerup;
        var powerupDefn = GameManager.Instance.AllPowerups[kind];
        LastPowerupTime = Time.TimeSinceStartup;
        LastPowerupDefn = powerupDefn;
    }

    public void ServerClearPowerups()
    {
        Util.Assert(Network.IsServer);
        for (int i = 0; i < EquippedPowerups.Length; i++)
        {
            EquippedPowerups[i] = PowerupKind.Invalid;
        }
        ServerSyncPowerups();
    }

    public bool ServerTryConsumePowerup(PowerupKind kind)
    {
        Util.Assert(Network.IsServer);
        
        Battlepass.IncrementProgress(this, "67a4871171992f28e2a0da26", 1);

        for (int i = 0; i < EquippedPowerups.Length; i++)
        {
            if (EquippedPowerups[i] == kind)
            {
                EquippedPowerups[i] = PowerupKind.Invalid;
                ServerSyncPowerups();
                return true;
            }
        }
        return false;
    }

    // [ServerRpc]
    // public static void RequestSetEquippedPowerups(string powerupsJson)
    // {
    //     if (GameManager.Instance.Alive() == false) return;
    //     if (GameManager.Instance.State != GameState.PrepPhase) return;

    //     var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
    //     if (!player.Alive()) return;

    //     var equippedPowerups = powerupsJson.FromJson<List<EquippedPowerup>>();
    //     if (equippedPowerups == null) return;

    //     player.ClearPowerups();

    //     bool needResync = false;
    //     int room = GameManager.MaxEquippedPowerups;
    //     for (int i = 0; i < equippedPowerups.Count; i++)
    //     {
    //         var equipped = equippedPowerups[i];
    //         if (equipped.Index < 0 || equipped.Index >= player.OwnedPowerups.Count)
    //         {
    //             needResync = true;
    //             equippedPowerups.RemoveAt(i);
    //             i -= 1;
    //             continue;
    //         }

    //         var owned = player.OwnedPowerups[equipped.Index];
    //         if (room < equipped.RequestedCount)
    //         {
    //             needResync = true;
    //             equipped.RequestedCount = room;
    //         }
    //         owned.Equipped = equipped.RequestedCount;
    //         room -= equipped.RequestedCount;
    //     }

    //     player.EquippedPowerups = equippedPowerups; // we should have filtered out any potential bad data above!

    //     if (needResync)
    //     {
    //         Log.Info("Resyncing player owned powerups.");
    //         player.ServerSyncPowerups();
    //     }
    // }

    // public void ServerGrantPowerup(PowerupKind kind, bool sync)
    // {
    //     Util.Assert(Network.IsServer);
    //     foreach (var existing in OwnedPowerups)
    //     {
    //         if (existing.Definition.Kind == kind)
    //         {
    //             existing.Count += 1;
    //             return;
    //         }
    //     }

    //     var powerup = new OwnedPowerup();
    //     powerup.Kind = kind;
    //     powerup.Definition = GameManager.Instance.AllPowerups[kind];
    //     powerup.Count = 1;
    //     OwnedPowerups.Add(powerup);

    //     if (sync)
    //     {
    //         ServerSyncPowerups();
    //     }
    // }

    public void ServerSyncPowerups()
    {
        Util.Assert(Network.IsServer);
        CallClient_SyncPowerups(EquippedPowerups.ToJson());
    }

    [ClientRpc]
    public void SyncPowerups(string equippedPowerups)
    {
        Log.Info("equippedPowerups: " + equippedPowerups);
        EquippedPowerups = equippedPowerups.FromJson<PowerupKind[]>();
    }

    public override Vector2 CalculatePlayerVelocity(Vector2 currentVelocity, Vector2 input, float deltaTime)
    {
        if (TryGetEffect<RLGL_SnowboardEffect>(out var snowboardEffect))
        {
            if (GameManager.Instance.CurrentMinigame.Kind == MinigameKind.Mingle)
            {
                return snowboardEffect.AbilityDirection * 7;    
            } 
            
            return snowboardEffect.AbilityDirection * 5;
        }

        if (TryGetEffect<RLGL_IcePatchEffect>(out var icePatchEffect))
        {
            return icePatchEffect.Direction * 3f;
        }

        var multiplier = 0.625f * CheatSpeedMultiplier * (CurrentDrag * 2);
        float speed = 400.0f;
        foreach (var effect in Effects)
        {
            if (effect.GetType() == typeof(BalloonPopSlowEffect))
            {
                speed *= 0.75f;
            }

            if (effect.GetType() == typeof(HotPotatoSlowEffect))
            {
                speed *= 0.5f;
            }

            if (effect.GetType() == typeof(HP_HoldingDynamiteEffect))
            {
                speed *= 1.1f;
            }

            if (effect.GetType() == typeof(GlueTrapEffect))
            {
                speed *= 0.25f;
            }

            if (effect.GetType() == typeof(CirclePushSpiderWebEffect))
            {
                speed *= 0.5f;
            }
        }

        if (TryGetEffect<RLGL_IronGolemEffect>(out var ironGolem))
        {
            speed *= 0.5f;
        }

        if (TryGetEffect<RLGL_PigRiderEffect>(out var pigRider))
        {
            input = pigRider.LastKnownInput;
            speed *= 1.3f;
        }

        if (InputOverride.Length > 0)
        {
            input = InputOverride;
        }

        if (HasEffect<RLGL_IronGolemEffect>())
        {
            currentVelocity *= 0.35f;
        }

        if (HasEffect<SpectatorEffect>())
        {
            speed *= 2f;
        }

        if (TryGetEffect<RLGL_PigRiderEffect>(out var pigEffect))
        {
            input = pigEffect.LastKnownInput;
            currentVelocity *= 1.5f;
        }

        if (HasEffect<GlueTrapEffect>())
        {
            currentVelocity *= 0.6f;
        }
        
        currentVelocity += input * deltaTime * speed * multiplier;
        currentVelocity += Dash;
        currentVelocity += Impulse;
        currentVelocity *= 1.0f - CurrentDrag;
        Impulse = default;

        return currentVelocity;
    }

    public bool ServerAddXP(int add)
    {
        var newXP = PlayerXP.Value;
        var newLevel = PlayerLevel.Value;
        newXP += add;
        bool didLevelUp = false;
        while (true)
        {
            var req = GetXPRequiredForLevelUp(newLevel);
            Util.Assert(req > 0);
            if (newXP >= req)
            {
                didLevelUp = true;
                newXP -= req;
                newLevel += 1;
            }
            else
            {
                break;
            }
        }
        if (newLevel >= MaxLevel)
        {
            newLevel = MaxLevel;
            newXP = 0;
        }
        PlayerXP.Set(newXP);
        PlayerLevel.Set(newLevel);
        Save.SetInt(this, "player_xp", PlayerXP);
        Save.SetInt(this, "player_level", PlayerLevel);
        return didLevelUp;
    }

    public static int GetXPRequiredForLevelUp(int currentLevel)
    {
        Util.Assert(currentLevel >= 1);
        var xpRequired = (int)(100f * MathF.Pow(1.2f, currentLevel-1));
        return (xpRequired / 10) * 10; // round down to multiple of 10
    }

    public override void Awake()
    {
        BalloonUISpine = SpineInstance.Make();
        BalloonUISpine.SetSkeleton(Assets.GetAsset<SpineSkeletonAsset>("anims/balloon/009SG_Balloon.spine"));
        BalloonUISpine.SetSkin("red");
        BalloonUISpine.RefreshSkins();
        BalloonUISpine.SetAnimation("Idle_Loop", true);

        if (IsLocal)
        {
            CameraControl = CameraControl.Create(1);
            CameraControl.Zoom = CurrentZoomLevel;
        }

        if (Network.IsServer)
        {
            var level = Save.GetInt(this, "player_level", 1);
            var xp = Save.GetInt(this, "player_xp", 0);

            PlayerLevel.Set(level);
            PlayerXP.Set(xp);
        }

        var sm = SpineAnimator.SpineInstance.StateMachine;
        var aoLayer = sm.TryGetLayerByIndex(0);
        var aoIdleState = aoLayer.TryGetStateByName("Idle");

        var animLayer  = sm.CreateLayer("squid_AL", 5);
        animLayer.InitialState = animLayer.CreateState("__CLEAR_TRACK__", 0, true);

        var useBoxingGloveVar = sm.CreateVariable("use_boxing_glove", StateMachineVariableKind.BOOLEAN);
        var useDartVar = sm.CreateVariable("use_dart", StateMachineVariableKind.BOOLEAN);
        var boxingGloveIdle  = animLayer.CreateState("SquidGame_009/Boxing_Glove_Idle_mIK_AL", 0, true);
        animLayer.CreateGlobalTransition(animLayer.InitialState).CreateBoolCondition(useBoxingGloveVar, false);
        animLayer.CreateTransition(animLayer.InitialState, boxingGloveIdle, false).CreateBoolCondition(useBoxingGloveVar, true);
        animLayer.CreateTransition(boxingGloveIdle, animLayer.InitialState, false).CreateBoolCondition(useBoxingGloveVar, false);

        animLayer.AddSimpleTriggeredState("glove_punch",    "SquidGame_009/Boxing_Glove_Hit_mIK_AL");
        aoLayer.AddSimpleTriggeredState("ban_hammer_swing", "SquidGame_009/Ban_Hammer_Attack_alt");
        aoLayer.AddSimpleTriggeredState("death_fall",       "SquidGame_009/Death_Fall_alt_nomove", goBackToIdle: false);
        aoLayer.AddSimpleTriggeredState("death_sniped",     "SquidGame_009/Death_Shot_Sniper", goBackToIdle: false);

        aoLayer.AddSimpleTriggeredState("cry_loop",     "Emote/crying_loop", goBackToIdle: false, loop: true);
        aoLayer.AddSimpleTriggeredState("victory_loop", "Emote/Celebrate",   goBackToIdle: false, loop: true);

        aoLayer.AddSimpleTriggeredState("on_snowboard_loop", "SquidGame_009/On_Snowboard_Loop", goBackToIdle: false, loop: true);

        aoLayer.AddSimpleTriggeredState("ice_slide_loop", "SquidGame_009/Slip_And_Slide_Loop", goBackToIdle: false, loop: true);

        aoLayer.AddSimpleTriggeredState("pig_ride_loop", "____Run_Pig_Rodeo", goBackToIdle: false, loop: true);
        aoLayer.AddSimpleTriggeredState("dance_loop", "Emote/Dance", goBackToIdle: false, loop: true);
        
        aoLayer.AddSimpleTriggeredState("slip", "SquidGame_009/Slip", goBackToIdle: false, loop: false);
        aoLayer.AddSimpleTriggeredState("get_up", "SquidGame_009/Get_Up", goBackToIdle: true, loop: false);

        /* aoLayer.AddSimpleTriggeredState("slip", "SquidGame_009/Slip", goBackToIdle: false, loop: false);
        var getUpState = animLayer.CreateState("SquidGame_009/Get_Up", 0, false);
        animLayer.CreateTransition(aoLayer.TryGetStateByName("SquidGame_009/Slip"), getUpState, true);
        animLayer.CreateTransition(getUpState, animLayer.InitialState, true); */

        var ikLayer = SpineAnimator.SpineInstance.StateMachine.CreateLayer("ik", 10);
        {
            var idleState = ikLayer.CreateState("__CLEAR_TRACK__", 0, true);
            ikLayer.InitialState = idleState;

            var useIkVar = SpineAnimator.SpineInstance.StateMachine.TryGetVariableByName("use_ik");
            var movingVar = SpineAnimator.SpineInstance.StateMachine.TryGetVariableByName("moving");

            var dartIdle = ikLayer.CreateState("SquidGame_009/Throw_Aim_mIK_AL", 0, true);
            var gunIdle = ikLayer.CreateState("Idle_mIK", 0, true);
            var gunRun  = ikLayer.CreateState("Run_mIK", 0, true);

            var states = new List<StateMachineState>();
            states.Add(idleState);
            states.Add(dartIdle);
            states.Add(gunIdle);
            states.Add(gunRun);

            void AddTransitionsToState(StateMachineState toState, List<StateMachineState> states, bool ik, bool? dart = null, bool? moving = null)
            {
                foreach (var state in states)
                {
                    if (state == toState) continue;

                    var transition = ikLayer.CreateTransition(state, toState, false);
                    transition.CreateBoolCondition(useIkVar, ik);
                    if (moving is bool v1)
                    {
                        transition.CreateBoolCondition(movingVar, v1);
                    }
                    if (dart is bool v2)
                    {
                        transition.CreateBoolCondition(useDartVar, v2);
                    }
                }
            }

            AddTransitionsToState(idleState, states, ik: false);
            AddTransitionsToState(idleState, states, ik: false);
            AddTransitionsToState(dartIdle,  states, ik: true, dart: true);
            AddTransitionsToState(gunIdle,   states, ik: true, dart: false, moving: false);
            AddTransitionsToState(gunRun,    states, ik: true, dart: false, moving: true);

            ikLayer.AddSimpleTriggeredState("throw", "SquidGame_009/Throw_mIK", allowTransitionToSelf: true);
            // ikLayer.AddSimpleTriggeredState("shoot", "Shoot_Gun_mIK_AL",        allowTransitionToSelf: true);
        }

        if (Network.IsServer)
        {
            // ServerGrantPowerup(PowerupKind.RLGL_Shield, false);
            // ServerGrantPowerup(PowerupKind.RLGL_LightningDash, false);
            // ServerGrantPowerup(PowerupKind.RLGL_PigRider, false);
            // ServerGrantPowerup(PowerupKind.RLGL_IronGolem, false);
            // ServerGrantPowerup(PowerupKind.RLGL_Snowboard, false);
            // ServerGrantPowerup(PowerupKind.RLGL_IcePatch, false);
            // ServerGrantPowerup(PowerupKind.RLGL_DiscoballLauncher, false);
            // ServerGrantPowerup(PowerupKind.RLGL_Stampede, false);
            // ServerGrantPowerup(PowerupKind.RLGL_BananaPeel, false);
            // ServerGrantPowerup(PowerupKind.RLGL_GlueBomb, false);
            
            // ServerGrantPowerup(PowerupKind.HP_LightningDash, false);
            // ServerGrantPowerup(PowerupKind.HP_Reverse, false);
            // ServerGrantPowerup(PowerupKind.HP_AirMail, false);
            // ServerGrantPowerup(PowerupKind.HP_MoreTime, false);
            // ServerGrantPowerup(PowerupKind.HP_ShadowDecoy, false);
            // ServerGrantPowerup(PowerupKind.HP_TimeOut, false);
            // ServerGrantPowerup(PowerupKind.HP_OilSpill, false);
            // ServerGrantPowerup(PowerupKind.HP_SpringGlove, false);
            // ServerGrantPowerup(PowerupKind.HP_MagnetTrap, false);
            // ServerSyncPowerups();
        }

        if (GameManager.Instance.TournamentIsRunning)
        {
            AddEffect<SpectatorEffect>();
            if (GameManager.Instance.CurrentMinigame.Alive())
            {
                Teleport(GameManager.Instance.CurrentMinigame.Position);
            }
        }
    }

    public override void OnDestroy()
    {
        if (CameraControl != null)
        {
            CameraControl.Destroy();
        }
    }

    public override void Update()
    {
        if (GameManager.Instance.State == GameState.RunningMinigame && GameManager.Instance.CheckMinigame(MinigameKind.CirclePush))
        {
            TargetDrag = 0.05f;
        }
        else
        {
            TargetDrag = 0.5f;
        }

        CurrentDrag = AOMath.Lerp(CurrentDrag, TargetDrag, 1f * Time.DeltaTime);

        if (IsLocal)
        {
            if (GameManager.Instance.CurrentMinigame.Alive() && GameManager.Instance.State == GameState.RunningMinigame && IsDead == false && HasEffect<SpectatorEffect>() == false && IsEliminated == false)
            {
                List<Ability> abilities = new();

                if (GameManager.Instance.FrontmanEnabled && IsFrontman)
                {
                    abilities.Add(GetAbility<FrontmanGunAbility>());
                    abilities.Add(GetAbility<FrontmanInvisAbility>());
                }
                else
                {
                    switch (GameManager.Instance.CurrentMinigame.Kind)
                    {
                        case MinigameKind.CirclePush:
                        {
                            abilities.Add(GetAbility<CirclePushPunchAbility>());
                            break;
                        }
                        case MinigameKind.BalloonPop:
                        {
                            if (Game.IsMobile)
                            {
                                abilities.Add(GetAbility<BalloonPopShootAbility>());
                            }
                            else
                            {
                                var offset = GetMousePosition() - Position;
                                CurrentTargettingDirection = offset.Normalized;
                                CurrentTargettingMagnitude = offset.Length;
                                DrawLineAimingIndicator(CurrentTargettingDirection);
                                if (IsInputDown(Input.UnifiedInput.MOUSE_LEFT))
                                {
                                    ActivateAbility<BalloonPopShootAbility>();
                                }
                            }
                            break;
                        }
                        case MinigameKind.HotPotato:
                        {
                            if (HasEffect<HP_HoldingDynamiteEffect>())
                            {
                                abilities.Add(GetAbility<HP_PassDynamiteAbility>());
                            }

                            break;
                        }
                    }

                    if (abilities.Count == 0)
                    {
                        abilities.Add(null); // ensures that powerups are always not the "main" button. not sure if good idea
                    }

                    foreach (var equipped in EquippedPowerups)
                    {
                        if (equipped != PowerupKind.Invalid)
                        {
                            var defn = GameManager.Instance.AllPowerups[equipped];
                            abilities.Add(GetAbility(defn.AbilityType));
                        }
                        else
                        {
                            abilities.Add(null);
                        }
                    }
                }

                if (abilities.Count > 0)
                {
                    DrawDefaultAbilityUI(new AbilityDrawOptions()
                    {
                        AbilityElementSize = 75,
                        Abilities = abilities.ToArray(),
                    });
                }
            }

            // powerup animation
            if (LastPowerupDefn != null)
            {
                var t = Ease.OutQuart(Ease.FadeInAndOut(0.25f, 0.25f, 3f, Time.TimeSinceStartup - LastPowerupTime));
                if (t > 0f)
                {
                    using var _ = UI.PUSH_LAYER(GameManager.GameHUDLayer + 1000);
                    var ts = GameManager.GetTextSettings(35);
                    var rect = UI.ScreenRect.InsetBottomUnscaled(UI.SafeRect.Min.Y).BottomRect().Offset(0, 50).Offset(0, -500 * (1f-t));
                    if (LastPowerupDefn.Description.Has())
                    {
                        var descRect = rect.CutBottom(40);
                        ts.Slant = 0.4f;
                        UI.Text(descRect, LastPowerupDefn.Description, ts);
                    }
                    var nameRect = rect.CutBottom(50);
                    ts.Size = 50;
                    ts.Slant = 0f;
                    UI.Text(nameRect, LastPowerupDefn.Name, ts);
                }
            }

            if (GameManager.Instance.FrontmanEnabled && IsFrontman)
            {
                var minigame = GameManager.Instance.CurrentMinigame;
                var cheatsRect = UI.ScreenRect.BottomLeftRect().Grow(0, 500, 0, 0).Offset(20, 20);
                if (minigame.Alive() && GameManager.Instance.State == GameState.RunningMinigame)
                {
                    Rect CutRow1(ref Rect rect)
                    {
                        var row = rect.CutBottom(100);
                        return row.Inset(6);
                    }
                    (Rect, Rect) CutRow2(ref Rect rect)
                    {
                        var row = rect.CutBottom(100);
                        var a = row.SubRect(0, 0, 0.5f, 1);
                        var b = row.SubRect(0.5f, 0, 1, 1);
                        return (a.Inset(6), b.Inset(6));
                    }
                    (Rect, Rect, Rect) CutRow3(ref Rect rect)
                    {
                        var row = rect.CutBottom(100);
                        var a = row.SubRect(0, 0, 0.3333f, 1);
                        var b = row.SubRect(0.3333f, 0, 0.6666f, 1);
                        var c = row.SubRect(0.6666f, 0, 1, 1);
                        return (a.Inset(6), b.Inset(6), c.Inset(6));
                    }

                    bool UpdateClosest<T>(ref T closest, ref float distance, T maybe, float maybeDistance)
                    {
                        if (maybeDistance < distance)
                        {
                            distance = maybeDistance;
                            closest = maybe;
                            return true;
                        }
                        return false;
                    }

                    MyPlayer TryGetClosestPlayerPosition(Vector2 toPos, out Vector2 outPos)
                    {
                        MyPlayer closestPlayer = null;
                        float d = 1f * CameraControl.Zoom;
                        foreach (var other in GameManager.Instance.PlayersInCurrentMinigame) if (other.Alive() && other.IsValidTarget && other.IsDead == false)
                        {
                            if (other == this) continue;
                            UpdateClosest(ref closestPlayer, ref d, other, (toPos - other.Position).LengthSquared);
                        }

                        outPos = toPos;
                        if (closestPlayer.Alive())
                        {
                            outPos = closestPlayer.Position + new Vector2(0, 0.5f);
                        }
                        return closestPlayer;
                    }

                    var wasTargeting = false;
                    var mouseWorldPos = Input.GetMousePosition();
                    Texture abilitySprite = null;

                    if (UI.DragDropTarget(UI.ScreenRect, "activate_cheat").DroppedPayload)
                    {
                        if (FrontmanAbilityToActivate != null)
                        {
                            FrontmanAbilityToActivate();
                        }
                    }

                    var orangeButton = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_1.png");
                    var greenButton = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_2.png");
                    var redButton = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_3.png");
                    var yellowButton = Assets.GetAsset<Texture>("$AO/new/modal/buttons_2/button_4.png");
                    var frontmanTs = GameManager.GetTextSettings(40);

                    switch (minigame.Kind)
                    {
                        case MinigameKind.RedLightGreenLight:
                        {
                            // Abilities
                            {
                                var row = CutRow1(ref cheatsRect);

                                // Target players to be shot
                                {
                                    using var _321 = UI.PUSH_ID("snipe");

                                    var icon = Assets.GetAsset<Texture>("sprites/crosshair.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);
                                    var target = TryGetClosestPlayerPosition(Input.GetMousePosition(), out var targetPosition);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        mouseWorldPos = targetPosition;
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { if (target.Alive()) CallServer_FrontmanRLGLShootPlayerNearPosition(target); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Force dance
                                {
                                    using var _321 = UI.PUSH_ID("dance");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/disco_ball_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);
                                    var target = TryGetClosestPlayerPosition(Input.GetMousePosition(), out var targetPosition);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        mouseWorldPos = targetPosition;
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { if (target.Alive()) CallServer_FrontmanRLGLForceDance(target); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop banana peel
                                {
                                    using var _321 = UI.PUSH_ID("banana");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/banana_peel_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropBanana(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop glue trap
                                {
                                    using var _321 = UI.PUSH_ID("glue");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/glue_bomb_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropGlue(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }
                            }

                            // light controls
                            {
                                var (go, stop, reset) = CutRow3(ref cheatsRect);
                                if (UI.Button(go, "Go", GameManager.GetButtonSettings(greenButton), frontmanTs).Clicked)
                                {
                                    CallServer_FrontmanRLGLGo();
                                }
                                if (UI.Button(stop, "Stop", GameManager.GetButtonSettings(redButton), frontmanTs).Clicked)
                                {
                                    CallServer_FrontmanRLGLStop();
                                }
                                if (MinigameRLGL.Instance.FrontmanIsControlling)
                                {
                                    if (UI.Button(reset, "Reset", GameManager.GetButtonSettings(yellowButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanRLGLReset();
                                    }
                                }
                            }

                            // light reversing
                            {
                                var reverse = CutRow1(ref cheatsRect);
                                if (MinigameRLGL.Instance.LightsReversed)
                                {
                                    if (UI.Button(reverse, "Lights: Reversed", GameManager.GetButtonSettings(redButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanRLGLReverse(false);
                                    }
                                }
                                else
                                {
                                    if (UI.Button(reverse, "Lights: Normal", GameManager.GetButtonSettings(greenButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanRLGLReverse(true);
                                    }
                                }
                            }

                            break;
                        }
                        case MinigameKind.CirclePush:
                        {
                            // abilities
                            {
                                var row = CutRow1(ref cheatsRect);

                                // Drop banana peel
                                {
                                    using var _321 = UI.PUSH_ID("banana");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/banana_peel_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropBanana(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop glue trap
                                {
                                    using var _321 = UI.PUSH_ID("glue");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/glue_bomb_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropGlue(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop circle push dynamite
                                {
                                    using var _321 = UI.PUSH_ID("dynamite");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/air_mail_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropCPDynamite(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop circle push lightning
                                {
                                    using var _321 = UI.PUSH_ID("lightning");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/lightning_strike_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropLightningStrike(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }
                            }

                            // Hazard/crate controls
                            {
                                var localTs = frontmanTs;
                                localTs.WordWrap = true;
                                var (hazards, powerups) = CutRow2(ref cheatsRect);
                                if (UI.Button(hazards, MinigameCirclePush.Instance.HazardsEnabled ? "Hazards:\nEnabled" : "Hazards:\nDisabled", GameManager.GetButtonSettings(MinigameCirclePush.Instance.HazardsEnabled ? greenButton : redButton), localTs).Clicked)
                                {
                                    CallServer_FrontmanCirclePushSetHazardsEnabled(!MinigameCirclePush.Instance.HazardsEnabled);
                                }
                                if (UI.Button(powerups, MinigameCirclePush.Instance.PowerupsEnabled ? "Powerups:\nEnabled" : "Powerups:\nDisabled", GameManager.GetButtonSettings(MinigameCirclePush.Instance.PowerupsEnabled ? greenButton : redButton), localTs).Clicked)
                                {
                                    CallServer_FrontmanCirclePushSetPowerupsEnabled(!MinigameCirclePush.Instance.PowerupsEnabled);
                                }
                            }

                            // Enable frenzy mode
                            if (MinigameCirclePush.Instance.FrenzyEnabled == false)
                            {
                                var frenzy = CutRow1(ref cheatsRect);
                                if (UI.Button(frenzy, "Enable Frenzy Mode", GameManager.GetButtonSettings(yellowButton), frontmanTs).Clicked)
                                {
                                    CallServer_FrontmanCirclePushEnableFrenzyMode();
                                }
                            }

                            break;
                        }
                        case MinigameKind.HotPotato:
                        {
                            // Abilities
                            {
                                var row = CutRow1(ref cheatsRect);

                                // Force dance
                                {
                                    using var _321 = UI.PUSH_ID("dance");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/disco_ball_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);
                                    var target = TryGetClosestPlayerPosition(Input.GetMousePosition(), out var targetPosition);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        mouseWorldPos = targetPosition;
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { if (target.Alive()) CallServer_FrontmanRLGLForceDance(target); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop oil trap
                                {
                                    using var _321 = UI.PUSH_ID("oil");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/oill_spill.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropOil(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop circle push dynamite
                                {
                                    using var _321 = UI.PUSH_ID("dynamite");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/air_mail_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropCPDynamite(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop circle push lightning
                                {
                                    using var _321 = UI.PUSH_ID("lightning");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/lightning_strike_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropLightningStrike(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }
                            }

                            // Time cheats
                            {
                                var timeRow = CutRow1(ref cheatsRect);

                                // Less time
                                {
                                    using var _321 = UI.PUSH_ID("less_time");

                                    var buttonRect = timeRow.CutLeftUnscaled(timeRow.Height); timeRow.CutLeft(6);
                                    if (UI.Button(buttonRect, "-5s", GameManager.GetButtonSettings(orangeButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanHPAddTime(-5f);
                                    }
                                }

                                // More time
                                {
                                    using var _321 = UI.PUSH_ID("more_time");

                                    var buttonRect = timeRow.CutLeftUnscaled(timeRow.Height); timeRow.CutLeft(6);
                                    if (UI.Button(buttonRect, "+5s", GameManager.GetButtonSettings(orangeButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanHPAddTime(5f);
                                    }
                                }
                            }

                            break;
                        }
                        case MinigameKind.Mingle:
                        {
                            // Abilities
                            {
                                var row = CutRow1(ref cheatsRect);

                                // Force dance
                                {
                                    using var _321 = UI.PUSH_ID("dance");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/disco_ball_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);
                                    var target = TryGetClosestPlayerPosition(Input.GetMousePosition(), out var targetPosition);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        mouseWorldPos = targetPosition;
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { if (target.Alive()) CallServer_FrontmanRLGLForceDance(target); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop glue trap
                                {
                                    using var _321 = UI.PUSH_ID("glue");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/glue_bomb_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropGlue(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop circle push dynamite
                                {
                                    using var _321 = UI.PUSH_ID("dynamite");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/air_mail_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropCPDynamite(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop circle push lightning
                                {
                                    using var _321 = UI.PUSH_ID("lightning");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/lightning_strike_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropLightningStrike(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }
                            }

                            // Players per room
                            {
                                var row = CutRow1(ref cheatsRect);

                                // Fewer
                                {
                                    using var _321 = UI.PUSH_ID("minus_one");

                                    var buttonRect = row.CutLeftUnscaled(row.Height * 3f); row.CutLeft(6);
                                    if (UI.Button(buttonRect, "-1 Mingle Size", GameManager.GetButtonSettings(orangeButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanMingleAddPlayersPerRoom(-1);
                                    }
                                }

                                // More
                                {
                                    using var _321 = UI.PUSH_ID("add_one");

                                    var buttonRect = row.CutLeftUnscaled(row.Height * 3f); row.CutLeft(6);
                                    if (UI.Button(buttonRect, "+1 Mingle Size", GameManager.GetButtonSettings(orangeButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanMingleAddPlayersPerRoom(1);
                                    }
                                }
                            }

                            // door cheats
                            {
                                var row = CutRow1(ref cheatsRect);

                                MingleRoom TryGetClosestDoorPosition(Vector2 toPos, out Vector2 outPos)
                                {
                                    MingleRoom closest = null;
                                    float d = 1f;
                                    foreach (var room in Scene.Components<MingleRoom>())
                                    {
                                        UpdateClosest(ref closest, ref d, room, (toPos - room.Door.Position).LengthSquared);
                                    }
                                    outPos = toPos;
                                    if (closest.Alive())
                                    {
                                        outPos = closest.Door.Position;
                                    }
                                    return closest;
                                }

                                // Open door
                                {
                                    using var _321 = UI.PUSH_ID("open_door");

                                    var icon = Assets.GetAsset<Texture>("sprites/door_key.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);
                                    var closestRoom = TryGetClosestDoorPosition(Input.GetMousePosition(), out var targetPosition);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        mouseWorldPos = targetPosition;
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanMingleOpenRoom(closestRoom); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Close door
                                {
                                    using var _321 = UI.PUSH_ID("close_door");

                                    var icon = Assets.GetAsset<Texture>("sprites/door_lock.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);
                                    var closestRoom = TryGetClosestDoorPosition(Input.GetMousePosition(), out var targetPosition);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        mouseWorldPos = targetPosition;
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanMingleCloseRoom(closestRoom); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }
                            }

                            // Time cheats
                            {
                                var timeRow = CutRow1(ref cheatsRect);

                                // Less time
                                {
                                    using var _321 = UI.PUSH_ID("less_time");

                                    var buttonRect = timeRow.CutLeftUnscaled(timeRow.Height); timeRow.CutLeft(6);
                                    if (UI.Button(buttonRect, "-5s", GameManager.GetButtonSettings(orangeButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanMingleAddTime(-5f);
                                    }
                                }

                                // More time
                                {
                                    using var _321 = UI.PUSH_ID("more_time");

                                    var buttonRect = timeRow.CutLeftUnscaled(timeRow.Height); timeRow.CutLeft(6);
                                    if (UI.Button(buttonRect, "+5s", GameManager.GetButtonSettings(orangeButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanMingleAddTime(5f);
                                    }
                                }
                            }

                            break;
                        }
                        case MinigameKind.BalloonPop:
                        {
                            // Abilities
                            {
                                var row = CutRow1(ref cheatsRect);

                                // Force dance
                                {
                                    using var _321 = UI.PUSH_ID("dance");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/disco_ball_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);
                                    var target = TryGetClosestPlayerPosition(Input.GetMousePosition(), out var targetPosition);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        mouseWorldPos = targetPosition;
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { if (target.Alive()) CallServer_FrontmanRLGLForceDance(target); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop glue trap
                                {
                                    using var _321 = UI.PUSH_ID("glue");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/glue_bomb_icon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropGlue(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop oil trap
                                {
                                    using var _321 = UI.PUSH_ID("oil");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/oill_spill.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanDropOil(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }

                                // Drop decoy
                                {
                                    using var _321 = UI.PUSH_ID("decoy");

                                    var icon = Assets.GetAsset<Texture>("AbilityIcons/decoy_balloon.png");
                                    var buttonRect = row.CutLeftUnscaled(row.Height); row.CutLeft(6);

                                    if (UI.DragDropSource(buttonRect, "activate_cheat", null))
                                    {
                                        abilitySprite = icon;
                                        wasTargeting = true;
                                        UI.Blocker(buttonRect, "cheat_blocker");
                                        FrontmanAbilityToActivate = () => { CallServer_FrontmanBPDropDecoy(mouseWorldPos); };
                                    }

                                    var targetButton = UI.BeginButton(buttonRect, "", GameManager.GetButtonSettings(orangeButton), frontmanTs);
                                    UI.Image(targetButton.Rect.Inset(15).FitAspect(icon.Aspect), icon);
                                    UI.EndButton();
                                }
                            }

                            // Shuffle teams
                            {
                                var row = CutRow1(ref cheatsRect);

                                // Fewer
                                {
                                    using var _321 = UI.PUSH_ID("shuffle");

                                    var buttonRect = row.CutLeftUnscaled(row.Height * 3f); row.CutLeft(6);
                                    if (UI.Button(buttonRect, "Shuffle teams", GameManager.GetButtonSettings(orangeButton), frontmanTs).Clicked)
                                    {
                                        CallServer_FrontmanBPShuffleTeams();
                                    }
                                }
                            }

                            break;
                        }
                    }

                    // End game
                    {
                        var prevValue = FrontmanEndGameButtonHold;

                        var endGameRect = CutRow1(ref cheatsRect);
                        var mask01 = 1f;
                        if (FrontmanEndGameButtonHold > 0f)
                        {
                            mask01 = Ease.T(FrontmanEndGameButtonHold, 1f);
                        }

                        var maskRect = endGameRect.SubRect(0, 0, mask01, 1);
                        var maskScope = IM.CreateMaskScope(endGameRect);
                        {
                            using var _123 = IM.BUILD_MASK_SCOPE(maskScope);
                            UI.Image(maskRect, null);
                        }

                        {
                            using var _323 = IM.USE_MASK_SCOPE(maskScope);
                            var button = UI.BeginButton(endGameRect, "End Game (hold)", GameManager.GetButtonSettings(redButton), frontmanTs);
                            if (button.Active && button.Hovering)
                            {
                                FrontmanEndGameButtonHold += Time.DeltaTime;
                            }
                            else
                            {
                                FrontmanEndGameButtonHold = 0;
                            }

                            var endGame01 = Ease.T(FrontmanEndGameButtonHold, 1f);

                            UI.EndButton();
                        }

                        if (prevValue < 1f && FrontmanEndGameButtonHold >= 1f)
                        {
                            CallServer_FrontmanRequestEndGame();
                        }
                    }

                    if (FrontmanWasTargetingLastFrame == false)
                    {
                        FrontmanRLGLTargetLerp = mouseWorldPos;
                    }
                    FrontmanWasTargetingLastFrame = wasTargeting;

                    if (FrontmanWasTargetingLastFrame)
                    {
                        using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
                        using var _2 = UI.PUSH_LAYER(10);

                        FrontmanRLGLTargetLerp = Vector2.Lerp(FrontmanRLGLTargetLerp, mouseWorldPos, 20 * Time.DeltaTime);
                        var rect = new Rect(FrontmanRLGLTargetLerp, FrontmanRLGLTargetLerp);
                        UI.Image(rect.Grow(0.5f), abilitySprite);
                    }
                }
            }
        }
    }

    public bool CheckFrontmanStuff(MyPlayer player)
    {
        if (GameManager.Instance.FrontmanEnabled == false) return false;
        if (GameManager.Instance.State != GameState.RunningMinigame) return false;
        if (player.Alive() == false) return false;
        if (player.IsFrontman == false) return false;
        return true;
    }

    [ServerRpc]
    public void FrontmanRequestEndGame()
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        GameManager.Instance.ServerEndMinigame();
    }

    [ServerRpc]
    public void FrontmanRLGLGo()
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.RedLightGreenLight)) return;
        MinigameRLGL.Instance.FrontmanIsControlling.Set(true);
        MinigameRLGL.Instance.GoGreen();
    }

    [ServerRpc]
    public void FrontmanRLGLStop()
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.RedLightGreenLight)) return;
        MinigameRLGL.Instance.FrontmanIsControlling.Set(true);
        MinigameRLGL.Instance.GoRed();
    }

    [ServerRpc]
    public void FrontmanRLGLReset()
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.RedLightGreenLight)) return;
        MinigameRLGL.Instance.FrontmanIsControlling.Set(false);
    }

    [ServerRpc]
    public void FrontmanRLGLReverse(bool reversed)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.RedLightGreenLight)) return;

        MinigameRLGL.Instance.LightsReversed.Set(reversed);
        if (MinigameRLGL.Instance.IsGreen)
        {
            MinigameRLGL.Instance.GoRed();
        }
        else
        {
            MinigameRLGL.Instance.GoGreen();
        }

        if (reversed)
        {
            GameManager.CallClient_GlobalNotification("Lights Reversed!");
        }
        else
        {
            GameManager.CallClient_GlobalNotification("Lights back to normal!");
        }
    }

    [ServerRpc]
    public void FrontmanRLGLShootPlayerNearPosition(MyPlayer target)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.RedLightGreenLight)) return;

        if (target.Alive())
        {
            MinigameRLGL.Instance.CallClient_SniperTargetPlayer(target);
        }
    }

    [ServerRpc]
    public void FrontmanRLGLForceDance(MyPlayer target)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (target.Alive())
        {
            RLGL_DiscoProjectile.CallClient_HitPlayer(target);
        }
    }

    [ServerRpc]
    public void FrontmanDropBanana(Vector2 pos)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        BananaPeel.Spawn(pos);
    }

    [ServerRpc]
    public void FrontmanDropGlue(Vector2 pos)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        GlueTrap.Spawn(pos);
    }

    [ServerRpc]
    public void FrontmanDropOil(Vector2 pos)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        HP_OilSpill.Spawn(pos, player);
    }

    [ServerRpc]
    public void FrontmanDropCPDynamite(Vector2 pos)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        CirclePushDynamite.Spawn(pos);
    }

    [ServerRpc]
    public void FrontmanDropLightningStrike(Vector2 pos)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        LightningStrike.Spawn(pos);
    }

    [ServerRpc]
    public void FrontmanMingleOpenRoom(MingleRoom room)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.Mingle)) return;
        if (room.Alive())
        {
            if (MinigameMingle.Instance.RoomsOpenThisRound.Contains(room) == false)
            {
                MinigameMingle.Instance.RoomsOpenThisRound.Add(room);
                MinigameMingle.Instance.ServerSyncOpenRooms();
                MinigameMingle.Instance.CallClient_OpenRoom(room);
            }
        }
    }

    [ServerRpc]
    public void FrontmanMingleCloseRoom(MingleRoom room)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.Mingle)) return;
        if (room.Alive())
        {
            MinigameMingle.Instance.CallClient_CloseRoom(room);
        }
    }

    [ServerRpc]
    public void FrontmanMingleAddTime(float add)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.Mingle)) return;
        MinigameMingle.Instance.MingleTimer += add;
    }

    [ServerRpc]
    public void FrontmanMingleAddPlayersPerRoom(int add)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.Mingle)) return;
        MinigameMingle.Instance.NumberOfPlayersPerRoomThisRound.Set(MinigameMingle.Instance.NumberOfPlayersPerRoomThisRound + add);
    }

    [ServerRpc]
    public void FrontmanHPAddTime(float add)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.HotPotato)) return;
        foreach (var dynamite in Scene.Components<HP_Dynamite>())
        {
            var newTime = dynamite.TimeLeft + add;
            if (newTime < 0.1f)
            {
                newTime = 0.1f;
            }
            dynamite.TimeLeft = newTime;
        }
    }

    [ServerRpc]
    public void FrontmanBPDropDecoy(Vector2 position)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.BalloonPop)) return;
        BP_DecoyBalloonAbility.Spawn(position, player);
    }

    [ServerRpc]
    public void FrontmanBPShuffleTeams()
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.BalloonPop)) return;
        MinigameBalloonPop.Instance.ServerAssignTeamsRandomly();
        GameManager.CallClient_GlobalNotification("Teams shuffled!");
    }

    [ServerRpc]
    public void FrontmanCirclePushSetHazardsEnabled(bool enabled)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.CirclePush)) return;
        MinigameCirclePush.Instance.HazardsEnabled.Set(enabled);
    }

    [ServerRpc]
    public void FrontmanCirclePushSetPowerupsEnabled(bool enabled)
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.CirclePush)) return;
        MinigameCirclePush.Instance.PowerupsEnabled.Set(enabled);
    }

    [ServerRpc]
    public void FrontmanCirclePushEnableFrenzyMode()
    {
        var player = (MyPlayer)Network.GetRemoteCallContextPlayer();
        if (!CheckFrontmanStuff(player)) return;
        if (!GameManager.Instance.CheckMinigame(MinigameKind.CirclePush)) return;
        MinigameCirclePush.Instance.ServerEnableFrenzyMode();
    }



    // [UIPreview]
    public static void DrawXPBar()
    {
        var t = AOMath.SinXY(0.1f, 0.9f, Time.TimeSinceStartup * 0.25f);
        var level = 29;
        var req = GetXPRequiredForLevelUp(level);
        DrawXPBar(level, (int)((float)req * t));
    }

    public static void DrawXPBar(int level, int xp)
    {
        var req = GetXPRequiredForLevelUp(level);
        var progress01 = (float)xp / (float)req;

        var fullBarRect = UI.ScreenRect;
        fullBarRect.Max.Y = UI.SafeRect.Max.Y;
        fullBarRect = fullBarRect.TopCenterRect().Grow(0, 500, 50, 500).Offset(0, -35);
        var bg   = Assets.GetAsset<Texture>("xp_bar/XP_backing.png");
        var fill = Assets.GetAsset<Texture>("xp_bar/XP_bar_fancy.png");
        var levelBacking = Assets.GetAsset<Texture>("xp_bar/level_backing.png");
        UI.Image(fullBarRect, bg);

        if (level >= MaxLevel)
        {
            progress01 = 1f;
        }

        // fill
        {
            var fillRect = fullBarRect.SubRect(0, 0, progress01, 1);
            var maskScope = IM.CreateMaskScope(fullBarRect);
            {
                using var _123 = IM.BUILD_MASK_SCOPE(maskScope);
                UI.Image(fillRect, null);
            }

            {
                using var _323 = IM.USE_MASK_SCOPE(maskScope);
                UI.Image(fullBarRect.Inset(7), fill);
            }
        }

        // xp req
        {
            var ts = GameManager.GetTextSettings(35);
            if (level < MaxLevel)
            {
                UI.Text(fullBarRect.Offset(0, 3), $"{xp}/{req}", ts);
            }
            else
            {
                UI.Text(fullBarRect.Offset(0, 3), $"MAX LEVEL!", ts);
            }
        }

        // level diamond
        {
            var leftLevelRect = fullBarRect.LeftRect().Offset(-40, 0).Grow(10, 0, 10, 0).FitAspect(levelBacking.Aspect, Rect.FitAspectKind.KeepHeight);
            UI.Image(leftLevelRect, levelBacking);
            UI.Text(leftLevelRect.Offset(0, 3), level.ToString(), GameManager.GetTextSettings(50));
        }

        // notches
        {
            var notch = Assets.GetAsset<Texture>("xp_bar/notch_back.png");
            var notchInset = fullBarRect.Inset(4, 0, 4, 0);
            UI.Image(notchInset.SubRect(0.20f, 0, 0.20f, 1).Grow(0, 2, 0, 2), notch);
            UI.Image(notchInset.SubRect(0.40f, 0, 0.40f, 1).Grow(0, 2, 0, 2), notch);
            UI.Image(notchInset.SubRect(0.60f, 0, 0.60f, 1).Grow(0, 2, 0, 2), notch);
            UI.Image(notchInset.SubRect(0.80f, 0, 0.80f, 1).Grow(0, 2, 0, 2), notch);
        }
    }

    public override void LateUpdate()
    {
        if (Network.IsServer)
        {
            var targetZoom = 1f;
            if (GameManager.Instance.State >= GameState.SetupNextGame && GameManager.Instance.State <= GameState.PlayersLeftScreen && GameManager.Instance.CurrentMinigame.Alive())
            {
                targetZoom = GameManager.Instance.CurrentMinigame.GameplayZoomLevel;
            }

            if (GameManager.Instance.State >= GameState.EndGameDelay && GameManager.Instance.State <= GameState.PlayersLeftScreen)
            {
                targetZoom *= 1.1f;
            }

            if (HasEffect<SpectatorEffect>())
            {
                targetZoom = 1.5f;
            }

            targetZoom *= CheatZoomMultiplier;
            CurrentZoomLevel = AOMath.Lerp(CurrentZoomLevel, targetZoom, 1 * Time.DeltaTime);
        }

        if (IsFrontman && HasEffect<FrontmanMaskSkinEffect>() == false)
        {
            AddEffect<FrontmanMaskSkinEffect>();
        }
        else if (IsFrontman == false && HasEffect<FrontmanMaskSkinEffect>())
        {
            RemoveEffect<FrontmanMaskSkinEffect>(true);
        }

        if (IsFrontman && (HasEffect<FrontmanAimingGunEffect>() || SpineAnimator.SpineInstance.StateMachine.TryGetLayerByName("attack").CurrentState.Name == "Shoot_Gun_mIK_AL"))
        {
            var gun = Assets.GetAsset<Texture>("sprites/black_gun.png");
            var position = SpineAnimator.GetBonePosition("Hand_R");
            var rotation = (SpineAnimator.GetBoneRotation("Hand_R") - 25) * (Entity.LocalScaleX < 0 ? -1 : 1);
            var scale = Entity.LocalScaleX < 0 ? -1f : 1f;

            using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
            using var _2 = IM.PUSH_Z(GetZOffset()-0.001f);
            var t = AOMath.Translate(position);
            var r = AOMath.Rotate(rotation, new Vector3(0, 0, 1));
            var s = AOMath.Scale(new Vector3(scale, 1, 1));
            using var _3 = UI.PUSH_MATRIX(t * r * s);
            var rect = new Rect().Grow(0.25f).Offset(0.25f, 0.1f).FitAspect(gun.Aspect);
            UI.Image(rect, gun);
        }

        var currentColor = SpineAnimator.SpineInstance.ColorMultiplier;
        SpineAnimator.SpineInstance.ColorMultiplier = Vector4.Lerp(new Vector4(1, 0, 0, currentColor.W), new Vector4(1, 1, 1, currentColor.W), Ease.T(Time.TimeSinceStartup - TimeLastTookDamage, 0.5f));

        if (IsLocal)
        {
            if (GameManager.Instance.FrontmanEnabled && IsFrontman)
            {
                if (Input.GetKeyHeld(Input.Keycode.KEYCODE_2))
                {
                    LocalCheatZoomMultiplier *= 1.035f;
                }
                if (Input.GetKeyHeld(Input.Keycode.KEYCODE_1))
                {
                    LocalCheatZoomMultiplier *= 0.965f;
                }
            }
            else
            {
                LocalCheatZoomMultiplier = 1f;
            }

            CameraControl.Zoom = CurrentZoomLevel * LocalCheatZoomMultiplier;
            CameraControl.Position = Vector2.Lerp(CameraControl.Position, Entity.Position + new Vector2(0, 0.5f), 0.5f);
        }

        int slowEffectCount = 0;
        float longestSlow = 0f;
        foreach (var effect in Effects)
        {
            if (effect is BalloonPopSlowEffect slow1)
            {
                slowEffectCount += 1;
                longestSlow = MathF.Max(longestSlow, slow1.DurationRemaining);
            }
            else if (effect is HotPotatoSlowEffect slow2)
            {
                slowEffectCount += 1;
                longestSlow = MathF.Max(longestSlow, slow2.DurationRemaining);
            }
        }

        // XP bar
        if (IsLocal)
        {
            switch (GameManager.Instance.State)
            {
                case GameState.WaitingForPlayers:
                {
                    DrawXPBar(PlayerLevel, PlayerXP);
                    break;
                }
            }
        }

        if (NameInvisCounter == 0 || HasEffect<SpectatorEffect>() && Network.LocalPlayer != null && ((MyPlayer)Network.LocalPlayer).HasEffect<SpectatorEffect>())
        {
            using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
            using var _2 = IM.PUSH_Z(GetZOffset()-0.0001f);
            var aboveHeadRect = FinalNameRect.TopCenterRect();
            var leftOfNameRect = FullNameplateRect.LeftCenterRect();
            var rightOfNameRect = FullNameplateRect.RightCenterRect();

            // level
            {
                var levelRect = leftOfNameRect.CutRight(0.4f).Offset(-0.05f, 0);
                var sprite = Assets.GetAsset<Texture>("xp_bar/level_backing.png");
                var rect = levelRect.FitAspect(sprite.Aspect, Rect.FitAspectKind.KeepWidth);
                UI.Image(rect, sprite);
                UI.Text(rect.Offset(0.0f, 0.017f), PlayerLevel.ToString(), GameManager.GetTextSettings(0.27f));
            }

            if (UseLivesForMinigame)
            {
                if (HasEffect<SpectatorEffect>() == false && HasEffect<HP_ShadowDecoyEffect>() == false && GameManager.Instance.State == GameState.RunningMinigame)
                {
                    var livesRect = rightOfNameRect.CutLeft(0.4f).Offset(0.05f, 0);
                    var sprite = Assets.GetAsset<Texture>("sprites/lives.png");
                    var rect = livesRect.FitAspect(sprite.Aspect, Rect.FitAspectKind.KeepWidth);
                    UI.Image(rect, sprite);
                    UI.Text(rect.Offset(0.0f, 0.017f), MinigameLivesLeft.ToString(), GameManager.GetTextSettings(0.24f));
                }
            }

            if (GameManager.Instance.CurrentMinigame.Alive() && GameManager.Instance.CurrentMinigame.Kind == MinigameKind.BalloonPop)
            {
                if (IsPartOfTheCurrentTournament && IsEliminated == false && IsFrontman == false)
                {
                    BalloonUISpine.ColorMultiplier = BalloonPopBalloon.TeamColors[BalloonPopTeam];
                    BalloonUISpine.Update(Time.DeltaTime);
                    var balloonRect = rightOfNameRect.CutLeft(0.4f).Offset(0.05f, -0.2f);
                    UI.DrawSkeleton(balloonRect, BalloonUISpine, new Vector2(0.45f, 0.45f), 0);
                }
            }

            void DoOverheadEffectText(ref Rect aboveHeadRect, string str, float time, UI.TextSettings ts)
            {
                UI.Text(aboveHeadRect.CutBottom(0.35f), $"{str}: {time:F1}s", ts);
            }

            var ts = GameManager.GetTextSettings(0.25f);
            if (TryGetEffect<HP_HoldingDynamiteEffect>(out var dynamiteEffect))
            {
                DoOverheadEffectText(ref aboveHeadRect, "EXPLODING", dynamiteEffect.Dynamite.TimeLeft, ts);
            }

            if (slowEffectCount > 0)
            {
                DoOverheadEffectText(ref aboveHeadRect, "SLOWED", longestSlow, ts);
            }

            if (TryGetEffect<RLGL_ShieldEffect>(out var shieldEffect))
            {
                DoOverheadEffectText(ref aboveHeadRect, "SHIELD", shieldEffect.DurationRemaining, ts);
            }

            if (TryGetEffect<RLGL_PigRiderEffect>(out var pigRider))
            {
                DoOverheadEffectText(ref aboveHeadRect, "YEEHAW", pigRider.DurationRemaining, ts);
            }

            if (TryGetEffect<RLGL_IronGolemEffect>(out var golem))
            {
                DoOverheadEffectText(ref aboveHeadRect, "GOLEM", golem.DurationRemaining, ts);
            }

            if (TryGetEffect<RLGL_SnowboardEffect>(out var snowboard))
            {
                DoOverheadEffectText(ref aboveHeadRect, "SHRED", snowboard.DurationRemaining, ts);
            }

            if (TryGetEffect<RLGL_ForcedDanceEffect>(out var dance))
            {
                DoOverheadEffectText(ref aboveHeadRect, "DANCE", dance.DurationRemaining, ts);
            }

            if (TryGetEffect<BP_LockedInEffect>(out var lockedIn))
            {
                DoOverheadEffectText(ref aboveHeadRect, "LOCKED IN", lockedIn.DurationRemaining, ts);
            }
        }
    }

    [ClientRpc]
    public void ShowNotificationLocal(string message)
    {
        if (IsLocal)
        {
            Notifications.Show(message);
        }
    }

    [ClientRpc]
    public void ForceTeleport(Vector2 position)
    {
        Teleport(position);
    }

    [NetSync, Serialized] public float TargetDrag = 0.5f;
    [NetSync, Serialized] public float CurrentDrag = 0.5f;

    //
    // Circle Push
    //

    [NetSync, Serialized] public float SmashMeter; // smash meter gets reset between lives, TotalSmashDamage doesn't
    [NetSync, Serialized] public float TotalSmashDamage;
    [NetSync, Serialized] public Vector2 Impulse;

    public CirclePushWeapon CurrentCirclePushWeapon;

    [ClientRpc]
    public void EquipTimedWeapon(int weapon)
    {
        RemoveEffect<CirclePushWeaponEffect>(true);
        AddEffect<CirclePushWeaponEffect>(duration: 10, preInit: e =>
        {
            e.Weapon = (CirclePushWeapon)weapon;
        });
        if (IsLocal)
        {
            SpawnTextPopup(CirclePushCrate.GetWeaponName((CirclePushWeapon)weapon), Position + new Vector2(0, 1), new Vector4(0, 1, 0, 1));
        }
    }

    public void EnableOrDisableWeaponSkin(bool enable, string skin)
    {
        if (enable)
        {
            SpineAnimator.SpineInstance.EnableSkin(skin);
        }
        else
        {
            SpineAnimator.SpineInstance.DisableSkin(skin);
        }
    }

    public void SetWeaponSkin(CirclePushWeapon weapon)
    {
        EnableOrDisableWeaponSkin(weapon == CirclePushWeapon.FeatherSword,     "weapons/feather_sword");
        EnableOrDisableWeaponSkin(weapon == CirclePushWeapon.BalloonSword,     "weapons/balloon_sword");
        EnableOrDisableWeaponSkin(weapon == CirclePushWeapon.Chicken,          "weapons/chicken");
        EnableOrDisableWeaponSkin(weapon == CirclePushWeapon.Plunger,          "weapons/plunger");
        EnableOrDisableWeaponSkin(weapon == CirclePushWeapon.Bat,              "weapons/baseball_bat");
        EnableOrDisableWeaponSkin(weapon == CirclePushWeapon.BanHammer,        "weapons/ban_hammer");
        EnableOrDisableWeaponSkin(weapon == CirclePushWeapon.Flail,            "weapons/flail");
        EnableOrDisableWeaponSkin(weapon == CirclePushWeapon.InfinityGauntlet, "weapons/infintity_gauntlet"); // theres a typo in the rig

        if (weapon == CirclePushWeapon.BoxingGlove)
        {
            SpineAnimator.SpineInstance.StateMachine.SetBool("use_boxing_glove", true);
        }
        else
        {
            SpineAnimator.SpineInstance.StateMachine.SetBool("use_boxing_glove", false);
        }

        SpineAnimator.SpineInstance.RefreshSkins();
    }

    public void SmashHit(Vector2 impulse)
    {
        if (IsFrontman)
        {
            return;
        }

        RemoveEffect<EmoteEffect>(true);

        TimeLastTookDamage = Time.TimeSinceStartup;
        if (GameManager.Instance.CurrentMinigame.Alive() && GameManager.Instance.CurrentMinigame.Kind == MinigameKind.CirclePush)
        {
            if (MinigameCirclePush.Instance.FrenzyEnabled)
            {
                impulse *= 2;
            }
            impulse *= SmashMeter;
            float diff = MathF.Sqrt(impulse.Length) * 0.625f;
            SmashMeter += diff;
            TotalSmashDamage += diff;
        }
        else
        {
            CurrentDrag = 0.05f;
            impulse *= 2f;
        }
        Impulse += impulse;
    }

    //
    // RLGL
    //

    [NetSync] public int RLGLDistanceReached;

    public SyncVar<bool> RLGLFinished = new();

    //
    // Balloon Pop
    //

    public SpineInstance BalloonUISpine;

    public SyncVar<int> BalloonPopTeam = new();
    public SyncVar<int> BalloonsPopped = new();

    public void OnHitWithBalloonPopBullet(BalloonPopBullet bullet)
    {
        if (Network.IsServer)
        {
            if (bullet.OwningPlayer == this)
            {
                return;
            }
            CallClient_HitWithBalloonPopBullet();
        }
    }
    
    
    [ClientRpc]
    public void HitWithBalloonPopBullet()
    {
        int variant = (int)RNG.RangeInt(ref GameManager.Instance.GlobalRng, 1, 4);
        SFX.Play(Assets.GetAsset<AudioAsset>($"sfx/dart_hit_player_0{variant}.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Entity.Position });
        AddEffect<BalloonPopSlowEffect>(duration: 3);
    }
    
    
    [ClientRpc]
    public void HitWithMiniTornado()
    {
        AddEffect<BananaPeelSlipEffect>();
    }

    //
    // Mingle
    //

    public bool IsSafeThisRound;

    public void PlayDeathSound()
    {
        if (Network.IsClient)
        {
            SFX.Play(Assets.GetAsset<AudioAsset>("sfx/schleemer_death.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Entity.Position });
        }
    }
    
    //
    // Hot Potato
    //
    
    public void OnHitWithAirMailProjectile(AirMailProjectile proj)
    {
        if (Network.IsServer)
        {
            if (proj.OwningPlayer == this)
            {
                return;
            }
            if (GameManager.Instance.State != GameState.RunningMinigame)
            {
                return;
            }
            Util.Assert(proj.ServerDynamite.Alive());

            proj.ServerDynamite.ServerPlaceDynamiteOnPlayer(this);
        }
    }
}

public abstract class MyEffect : AEffect
{
    public new MyPlayer Player => (MyPlayer)base.Player;
}

public abstract class MyAbility : Ability
{
    public new MyPlayer Player => (MyPlayer)base.Player;

    public override bool CanTarget(Player p)
    {
        var player = (MyPlayer)p;
        return true;
    }

    public override bool CanUse()
    {
        if (Player.IsDead) return false;
        return true;
    }

    public bool CheckHasPowerup(PowerupKind kind)
    {
        foreach (var powerup in Player.EquippedPowerups)
        {
            if (powerup == kind)
            {
                return true;
            }
        }
        return false;
    }   
}

public class NoClipEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override bool IsValidTarget => false;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.Entity.GetComponent<Circle_Collider>().LocalEnabled = false;
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.Entity.GetComponent<Circle_Collider>().LocalEnabled = true;
    }
}

public class FreezeEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => true;

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

public class WinnerPodiumEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => true;

    public override void OnEffectStart(bool isDropIn)
    {
        if (Player.IsEliminated)
        {
            Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("cry_loop");

        }
        else
        {
            Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("victory_loop");
        }
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }
}

public class SpectatorEffect : MyEffect
{
    public override bool IsActiveEffect => false;
    public override bool IsValidTarget => false;
    public override bool BlockAbilityActivation => true;

    public bool HasNameInvisReason = false;

    public void UpdateInvis()
    {
        if (Player.IsLocal || (Network.LocalPlayer.Alive() && Network.LocalPlayer.HasEffect<SpectatorEffect>()))
        {
            Player.SpineAnimator.SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, 0.5f);
            if (HasNameInvisReason)
            {
                HasNameInvisReason = false;
                Player.RemoveNameInvisibilityReason(nameof(SpectatorEffect));
            }
        }
        else
        {
            Player.SpineAnimator.SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, 0);
            if (!HasNameInvisReason)
            {
                HasNameInvisReason = true;
                Player.AddNameInvisibilityReason(nameof(SpectatorEffect));
                Player.NameInvisCounter += 1;
            }
        }
    }

    public override void OnEffectStart(bool isDropIn)
    {
        UpdateInvis();
        Player.SpineAnimator.DepthOffset = -10000;
        Player.SpineAnimator.SpineInstance.StateMachine.SetBool("ghost_form", true);
        Player.Entity.GetComponent<Circle_Collider>().LocalEnabled = false;
        if (!isDropIn)
        {
            Player.AddEmoteBlockReason(nameof(SpectatorEffect));
        }

        if (Player.IsLocal)
        {
            Game.SetVoiceEnabled(false);
        }
    }

    public override void OnEffectUpdate()
    {
        UpdateInvis();
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.RemoveEmoteBlockReason(nameof(SpectatorEffect));
        Player.SpineAnimator.DepthOffset = 0;
        Player.SpineAnimator.SpineInstance.StateMachine.SetBool("ghost_form", false);
        Player.SpineAnimator.SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, 1);
        if (HasNameInvisReason)
        {
            Player.NameInvisCounter -= 1;
            Player.RemoveNameInvisibilityReason(nameof(SpectatorEffect));
        }
        Player.Entity.GetComponent<Circle_Collider>().LocalEnabled = true;

        if (Player.IsLocal)
        {
            Game.SetVoiceEnabled(true);
        }
    }
}