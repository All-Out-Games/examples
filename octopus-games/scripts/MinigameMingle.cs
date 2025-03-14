using AO;

public enum MingleState
{
    Spinning,
    RunToRooms,
    WaitForNextRound,
    Done,
}

public partial class MinigameMingle : MinigameInstance
{
    public static MinigameMingle _instance;
    public static MinigameMingle Instance
    {
        get
        {
            if (_instance == null)
            {
                foreach (var component in Scene.Components<MinigameMingle>())
                {
                    _instance = component;
                    _instance.Awaken();
                    break;
                }
            }
            return _instance;
        }
    }

    [Serialized] public Entity SpinCenter;
    [Serialized] public Entity Spinner;
    [Serialized] public Entity RoomParent;

    [Serialized] public Sprite_Renderer PlayersPerRoomSign;
    [Serialized] public Texture[] NumberTextures;

    public List<MingleRoom> MingleRooms = new();

    public List<MingleRoom> RoomsOpenThisRound = new();
    
    public SyncVar<int> _state = new();
    public MingleState State
    {
        get => (MingleState)_state.Value;
        set => _state.Set((int)value);
    }

    public SyncVar<int> NumberOfPlayersPerRoomThisRound = new();

    public float TimeStateStarted;
    public float TimeInCurrentState => Time.TimeSinceStartup - TimeStateStarted;
    public bool _JustEnteredState;

    public float SpinTime;

    [NetSync] public float MingleTimer;
    
    public void NetworkSerialize(AO.StreamWriter writer)
    {
        GameManager.WriteListOfNetworkedComponents(writer, RoomsOpenThisRound);
    }

    public void NetworkDeserialize(AO.StreamReader reader)
    {
        RoomsOpenThisRound = GameManager.ReadListOfNetworkedComponents<MingleRoom>(reader);
    }

    public override void Awake()
    {
        foreach (var sprite in NumberTextures)
        {
            Assets.KeepLoaded(sprite, synchronous: false, includeInLoadingScreen: false);
        }

        foreach (var roomEntity in RoomParent.Children)
        {
            var room = roomEntity.GetComponent<MingleRoom>();
            Util.Assert(room.Alive());
            MingleRooms.Add(room);
        }

        _state.OnSync += (old, value) =>
        {
            TimeStateStarted = Time.TimeSinceStartup;
            _JustEnteredState = true;
        };
    }

    public override void MinigameSetup()
    {
        PlayersPerRoomSign.LocalEnabled = false;
        if (Network.IsServer)
        {
            foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
            {
                player.UseLivesForMinigame.Set(true);
                player.MinigameLivesLeft.Set(3);
            }

            _state.Set((int)MingleState.Spinning, true);
            RoomsOpenThisRound.Clear();
        }
    }

    public override void MinigameStart()
    {
        // Initialize any game-specific effects or states when the minigame starts
        if (Network.IsServer)
        {
            _state.Set((int)MingleState.Spinning, true);
        }
    }

    public void DrawPlayersPerRoomSign(int count)
    {
        using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
        using var _2 = UI.PUSH_LAYER(10);
        var pos = PlayersPerRoomSign.Position;
        pos.Y += 0.05f;
        var rect = new Rect(pos, pos);
        UI.Text(rect, $"{count}", GameManager.GetTextSettings(1f));
    }

    public override void MinigameTick()
    {
        var justEnteredState = _JustEnteredState;
        _JustEnteredState = false;

        switch (State)
        {
            case MingleState.Spinning:
            {
                if (justEnteredState)
                {
                    GameManager.Instance.StopCurrentTheme();
                    GameManager.Instance.PlayTheme("sfx/mingle_game_music_loop.wav");
                    
                    ResetRooms();
                }

                float rotationSpeed = 90f;
                var spinnerRotation = Spinner.LocalRotation;
                spinnerRotation += rotationSpeed * Time.DeltaTime;
                while (spinnerRotation < 0)
                {
                    spinnerRotation += 360f;
                }
                while (spinnerRotation >= 360f)
                {
                    spinnerRotation -= 360f;
                }
                Spinner.LocalRotation = spinnerRotation;

                DrawPlayersPerRoomSign(RNG.RangeInt(ref GameManager.Instance.GlobalRng, 1, 9));

                if (Network.IsServer)
                {
                    if (justEnteredState)
                    {
                        SpinTime = Random.Shared.NextFloat(4, 7);
                        CallClient_SetPlayerSpinning(true);
                    }

                    foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
                    {
                        var newPos = Vector2.Rotate(player.Entity.Position, AOMath.ToRadians(rotationSpeed * Time.DeltaTime), SpinCenter.Position);
                        player.InputOverride = (newPos - player.Entity.Position).Normalized;
                    }

                    if (TimeInCurrentState >= SpinTime)
                    {
                        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
                        {
                            player.InputOverride = default;
                        }
                        CallClient_SetPlayerSpinning(false);

                        //
                        // Calculate the number of rooms that should be open such that at least one person will die
                        //

                        int numberOfAlivePlayers = 0;
                        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
                        {
                            if (player.IsFrontman) continue;
                            numberOfAlivePlayers += 1;
                        }

                        int count = (int)RNG.RangeInt(ref GameManager.Instance.GlobalRng, 1, 3);
                        if (count >= numberOfAlivePlayers)
                        {
                            count = numberOfAlivePlayers - 1;
                        }
                        if (count < 1) count = 1;
                        NumberOfPlayersPerRoomThisRound.Set(count);

                        int numberOfRooms = numberOfAlivePlayers / NumberOfPlayersPerRoomThisRound;
                        if (numberOfRooms < 1) numberOfRooms = 1;
                        if (numberOfAlivePlayers % numberOfRooms == 0)
                        {
                            numberOfRooms -= 1;
                        }
                        if (numberOfRooms <= 0)
                        {
                            numberOfRooms = 1;
                        }

                        var rooms = new List<MingleRoom>(MingleRooms);
                        GameManager.Shuffle(rooms, ref GameManager.Instance.GlobalRng);
                        RoomsOpenThisRound.Clear();
                        while (numberOfRooms > 0)
                        {
                            numberOfRooms -= 1;
                            var room = rooms.Pop();
                            CallClient_OpenRoom(room);
                            RoomsOpenThisRound.Add(room);
                        }

                        ServerSyncOpenRooms();
                        MingleTimer = 20f;
                        State = MingleState.RunToRooms;
                    }
                }
                break;
            }
            case MingleState.RunToRooms:
            {
                if (justEnteredState)
                {
                    GameManager.Instance.StopCurrentTheme();
                    GameManager.Instance.PlayTheme("sfx/intense_music_loop.wav");
                }

                GameManager.DrawTimer("Find a Room!", MingleTimer, scale: 1.35f);

                // room pedestal numbers
                {
                    using var _ = UI.PUSH_CONTEXT(UI.Context.WORLD);
                    foreach (var room in MingleRooms)
                    {
                        using var _1 = IM.PUSH_Z(room.Pedestal.Position.Y-0.001f);
                        var rect = new Rect(room.Pedestal.Position + new Vector2(0, 1.075f));
                        using var _2 = UI.PUSH_COLOR_MULTIPLIER(new Vector4(1, 0, 0, 1), room.PlayersInRoom.Count != 0 && room.PlayersInRoom.Count != NumberOfPlayersPerRoomThisRound);
                        using var _3 = UI.PUSH_COLOR_MULTIPLIER(new Vector4(0, 1, 0, 1), room.PlayersInRoom.Count != 0 && room.PlayersInRoom.Count == NumberOfPlayersPerRoomThisRound);
                        UI.Text(rect, room.PlayersInRoom.Count.ToString(), GameManager.GetTextSettings(0.4f));
                    }
                }

                DrawPlayersPerRoomSign(NumberOfPlayersPerRoomThisRound);

                if (Network.IsServer)
                {
                    MingleTimer -= Time.DeltaTime;
                    if (MingleTimer <= 0f)
                    {
                        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
                        {
                            player.IsSafeThisRound = false;
                        }

                        foreach (var room in MingleRooms)
                        {
                            if (room.PlayersInRoom.Count == NumberOfPlayersPerRoomThisRound)
                            {
                                foreach (var player in room.PlayersInRoom)
                                {
                                    player.IsSafeThisRound = true;
                                }
                            }
                        }

                        foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
                        {
                            if (player.IsFrontman) continue;
                            if (player.IsSafeThisRound == false)
                            {
                                CallClient_KillPlayer(player);
                            }
                        }

                        if (GameManager.Instance.ServerEndMinigameIfEnoughPlayersArePermadead() == false)
                        {
                            State = MingleState.WaitForNextRound;
                        }
                    }
                }

                {
                    var rect = UI.ScreenRect;
                    rect.Max.Y = UI.SafeRect.Max.Y;
                    rect = rect.TopRect().Offset(0, -175);
                    var ts = GameManager.GetTextSettings(30);
                    UI.Text(rect.Offset(0, -25), "Players Per Room", ts);
                    ts.Size = 50;
                    UI.Text(rect.Offset(0, 15), NumberOfPlayersPerRoomThisRound.ToString(), ts);
                }

                break;
            }
            case MingleState.WaitForNextRound:
            {
                if (justEnteredState)
                {
                    GameManager.Instance.StopCurrentTheme();

                    SFX.Play(Assets.GetAsset<AudioAsset>("sfx/round_eliminated_v2.wav"), new SFX.PlaySoundDesc() { Volume = 0.4f});
                    
                    foreach (var room in MingleRooms)
                    {
                        room.PlayersInRoom.Clear();
                    }
                }

                GameManager.DrawTimer("Find a Room!", 0f, scale: 1.35f);

                if (Network.IsServer && TimeInCurrentState >= 5f)
                {
                    foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive())
                    {
                        if (player.IsDead && !player.IsEliminated)
                        {
                            player.ServerRespawnOrGoIntoSpectator();
                        }
                    }

                    var startNewRound = true;
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
                        if (onlyFrontmanIsLeft)
                        {
                            startNewRound = false;
                        }
                    }

                    if (startNewRound)
                    {
                        GameManager.Instance.ServerMoveAllPlayersToMinigameSpawnPoints();
                        State = MingleState.Spinning;
                    }
                    else
                    {
                        State = MingleState.Done;
                    }
                }
                break;
            }
            case MingleState.Done:
            {
                break;
            }
        }

        if (Network.IsServer)
        {
            // Server-only logic here
        }
    }

    public void ServerSyncOpenRooms()
    {
        CallClient_SyncOpenRooms(GameManager.WriteListOfNetworkedComponentsToBytes(RoomsOpenThisRound));
    }

    [ClientRpc]
    public void SyncOpenRooms(byte[] roomBytes)
    {
        RoomsOpenThisRound = GameManager.ReadListOfNetworkedComponentsFromBytes<MingleRoom>(roomBytes);
    }

    [ClientRpc]
    public void OpenRoom(MingleRoom room)
    {
        room.Door.LocalEnabled = false;
    }

    [ClientRpc]
    public void CloseRoom(MingleRoom room)
    {
        room.Door.LocalEnabled = true;
    }

    public void ResetRooms()
    {
        foreach (var room in MingleRooms)
        {
            room.Door.LocalEnabled = true;
            room.PlayersInRoom.Clear();
        }
    }

    [ClientRpc]
    public void KillPlayer(MyPlayer player)
    {
        player.AddEffect<MingleDeathEffect>();
    }

    [ClientRpc]
    public void SetPlayerSpinning(bool set)
    {
        if (set)
        {
            foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
            {
                player.AddEffect<MingleSpinningEffect>();
            }
        }
        else
        {
            foreach (var player in GameManager.Instance.PlayersInCurrentMinigame) if (player.Alive() && player.IsDead == false)
            {
                player.RemoveEffect<MingleSpinningEffect>(false);
            }
        }
    }

    public override void MinigameLateTick()
    {
        // Any late update logic here
    }

    public override void MinigameEnd()
    {
        // Cleanup or end-game logic here
    }

    public override int GetPlayerScore(MyPlayer player)
    {
        return player.MinigameLivesLeft; // Implement scoring logic
    }

    public override string LeaderboardPointsHeader()
    {
        return "Lives Left"; // Change to appropriate scoring metric
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

    public override bool ControlsRespawning() => true;
}

public class MingleDeathEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => true;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("death_sniped");
        Player.PlayDeathSound();

        if (Network.IsServer)
        {
            Player.ServerKillPlayer();
        }
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
    }
}

public class MingleSpinningEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool DisableMovementInput => true;

    public override void OnEffectStart(bool fromReplication)
    {
        // Add any initialization logic when the spinning effect starts
        Player.DoOverrideStateMachineMovingVariable = true;
        Player.StateMachineMovingVariableValue = false;
    }

    public override void OnEffectUpdate()
    {
        // Add any per-frame update logic for the spinning effect
    }

    public override void OnEffectEnd(bool fromReplication)
    {
        // Add any cleanup logic when the spinning effect ends
        Player.DoOverrideStateMachineMovingVariable = false;
    }
}

public class MingleRoom : Component, INetworkedComponent
{
    public HashSet<MyPlayer> PlayersInRoom = new();

    public Sprite_Renderer RoomSprite;

    public Entity Door;

    public Entity Pedestal;

    public Interactable Interactable;

    public void NetworkSerialize(AO.StreamWriter writer)
    {
        GameManager.WriteListOfNetworkedComponents(writer, PlayersInRoom.ToList());
    }

    public void NetworkDeserialize(AO.StreamReader reader)
    {
        var list = GameManager.ReadListOfNetworkedComponents<MyPlayer>(reader);
        PlayersInRoom.Clear();
        foreach (var element in list)
        {
            PlayersInRoom.Add(element);
        }
    }

    public override void Awake()
    {
        RoomSprite = GetComponent<Sprite_Renderer>();

        //
        Door = Entity.TryGetChildByName("Door");
        Util.Assert(Door != null);

        Pedestal = Entity.TryGetChildByName("RoomPedestal");
        Interactable = Pedestal.GetComponent<Interactable>();

        Interactable.OnInteract += (Player p) =>
        {
            var player = (MyPlayer)p;
            if (player.Alive() == false) return;

            if (Network.IsServer)
            {
                if (Door.LocalEnabled)
                {
                    MinigameMingle.Instance.CallClient_OpenRoom(this);
                }
                else
                {
                    MinigameMingle.Instance.CallClient_CloseRoom(this);
                }
            }
        };

        //
        var collider = Entity.TryGetChildByIndex(0).GetComponent<Box_Collider>();
        Util.Assert(collider != null);
        collider.OnCollisionEnter += (other) =>
        {
            if (other.Alive() == false) return;

            var player = other.GetComponent<MyPlayer>();
            if (!player.Alive()) return;

            if (PlayersInRoom.Contains(player) == false)
            {
                Log.Info($"{player.Name} entered {Entity.Name}");
                PlayersInRoom.Add(player);
            }
        };

        collider.OnCollisionExit += (other) =>
        {
            var player = other.GetComponent<MyPlayer>();
            if (!player.Alive()) return;

            if (PlayersInRoom.Contains(player))
            {
                Log.Info($"{player.Name} exited {Entity.Name}");
                PlayersInRoom.Remove(player);
            }
        };
    }

    public override void Update()
    {
        if (GameManager.Instance.CurrentMinigame.Alive() && GameManager.Instance.CurrentMinigame.Kind == MinigameKind.Mingle)
        {
            if (Door.LocalEnabled)
            {
                Interactable.Text = "Open Door";
            }
            else
            {
                Interactable.Text = "Close Door";
            }

            var visible = false;
            if (GameManager.Instance.State != GameState.RunningMinigame)
            {
                visible = true;
            }
            else if (MinigameMingle.Instance.State != MingleState.RunToRooms && MinigameMingle.Instance.State != MingleState.WaitForNextRound)
            {
                visible = true;
            }
            else
            {
                foreach (var other in MinigameMingle.Instance.RoomsOpenThisRound)
                {
                    if (other == this)
                    {
                        visible = true;
                    }
                }
            }
            if (visible)
            {
                RoomSprite.Tint = new Vector4(1, 1, 1, 1);
            }
            else
            {
                RoomSprite.Tint = new Vector4(0.1f, 0.1f, 0.1f, 1);
            }
        }
    }
}

// Lasso

public class Mingle_LassoAbility : MyAbility
{
    
}

// Bat

public class Mingle_BatAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override float Cooldown => 1f;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/bat_icon.png");

    public Component TryGetTarget()
    {
        float maxDistance = 1.5f;
        Component component = null;
        TryUpdateClosest<MyPlayer>(Player.Position, ref component, ref maxDistance, predicate: p => p.IsDead == false && p != Player && p.IsValidTarget);
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
            SFX.Play(Assets.GetAsset<AudioAsset>("sfx/sword_swing.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Player.Position });
            Player.AddEffect<MingleBatEffect>(preInit: e => e.Target = target);

            if (Network.IsServer)
            {
                Player.ServerTryConsumePowerup(PowerupKind.Mingle_Bat);
            }

            return true;
        }
        return false;
    }
}

public partial class MingleBatEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => false;

    public Component Target;

    public bool Hit;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("attack");
        DurationRemaining = Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).GetCurrentStateLength();
        
        Player.SetWeaponSkin(CirclePushWeapon.Bat);
    }

    public override void OnEffectUpdate()
    {
        if (Util.OneTime(ElapsedTime >= 0.15f, ref Hit))
        {
            if (Target.Alive())
            {
                if (Target is MyPlayer target)
                {
                    target.SmashHit((target.Position - Player.Position).Normalized * 50f);
                }
            }
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SetWeaponSkin(CirclePushWeapon.None);
    }

    [ClientRpc]
    public static void DestroyCrate(MyPlayer player, CirclePushCrate crate)
    {
        SFX.Play(Assets.GetAsset<AudioAsset>("sfx/crate_destroy.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = crate.Entity.Position });
        player.EquipTimedWeapon((int)crate.Weapon);
    }
}


// Glue Gun

public class Mingle_GlueGunAbility : MyAbility
{
    
}

// Frying Pan

public class Mingle_FryingPanAbility : MyAbility
{
    
}

// Cool Stick

public class Mingle_CoolStickAbility : MyAbility
{
    
}

// Lemon Blaster

public class Mingle_LemonBlasterAbility : MyAbility
{
    
}
