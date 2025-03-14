using AO;

public class RuntimeMon
{
    public string itemDef;
    public string type;
    public int level, displayLevel;
    public int exp { get; private set; }
    public int currentHealth;

    private int rarity => (int)FishItemManager.GetFishRarity(itemDef);
    public int maxHealth => BaseHealth + ((4 + 2 * rarity) * level);
    public int attack => BaseAttack + ((1 + 1 * rarity) * level);
    int BaseHealth => 40 + 15 * rarity;
    int BaseAttack => 10 + 4 * rarity;

    public static int CalculateTypeBonus(int startDamage, string type, string targetType)
    {
        //grass > fire > water
        if (type == "water" && targetType == "fire") return (int)(startDamage * 1.2f);
        if (type == "fire" && targetType == "grass") return (int)(startDamage * 1.2f);
        if (type == "grass" && targetType == "water") return (int)(startDamage * 1.2f);
        //if weak
        if (type == "water" && targetType == "grass") return (int)(startDamage * 0.8f);
        if (type == "grass" && targetType == "fire") return (int)(startDamage * 0.8f);
        if (type == "fire" && targetType == "water") return (int)(startDamage * 0.8f);
        return startDamage;
    }

    public int baseExp => (int)(100 * Math.Pow(level, 1.4));
    public int battleExp => (int)(25 + 12.5f * level);

    public void AddExp(int exp)
    {
        this.exp += exp;
        while (this.exp >= baseExp)
        {
            int lastBaseExp = baseExp;
            level++;
            this.exp -= lastBaseExp;
            //If mon not dead, reset health
            if (currentHealth > 0)
                currentHealth = maxHealth;
        }
        this.displayLevel = level;
    }

    public RuntimeMon(string itemDef, string type, int level, int exp)
    {
        this.itemDef = itemDef;
        this.type = type;
        this.level = level;
        this.displayLevel = level;
        this.exp = exp;
        currentHealth = maxHealth;
    }

    public static RuntimeMon FromCurrent(string itemDef, string type, int currentHealth, int exp, int level)
    {
        var mon = new RuntimeMon();
        mon.itemDef = itemDef;
        mon.type = type;
        mon.currentHealth = currentHealth;
        mon.exp = exp;
        mon.level = level;
        mon.displayLevel = level;
        return mon;
    }

    public RuntimeMon() { }
}

public partial class MyPlayer : Player, IFighter
{
    public bool SupportMultiBattle => true;
    Entity IFighter.Entity => Entity;
    SyncVar<bool> IFighter.inBattle => inBattle;
    Vector2 IFighter.battleDirection { get => battleDirection.Value; set => battleDirection.Set(value); }

    //Player net stuff
    public int Level
    {
        get
        {
            return _level.Value;
        }
        set
        {
            if (Network.IsServer)
            {
                _level.Set(value);
            }
        }
    }

    public BasicDateTimer starterPackTimer;

    private SyncVar<int> _level = new SyncVar<int>(1);
    public SyncVar<int> experience = new SyncVar<int>(0);
    public SyncVar<string> currentRodID = new SyncVar<string>();
    public SyncVar<string> lastRodID = new SyncVar<string>();

    public SyncVar<string> currentBoatID = new SyncVar<string>();
    public SyncVar<bool> inBattle = new SyncVar<bool>(false);

    public SyncVar<bool> hasFish = new SyncVar<bool>(false);
    public SyncVar<Vector2> battleDirection = new SyncVar<Vector2>(Vector2.Zero);
    public SyncVar<bool> inArenaBattle = new SyncVar<bool>(false);
    public SyncVar<bool> boughtStarterPack = new SyncVar<bool>(false);
    public SyncVar<Vector3> playerBuffs = new SyncVar<Vector3>();
    public void AddToBuff(float coin, float xp, float luck)
    {
        if (Network.IsClient)
        {
            return;
        }
        playerBuffs.Set(new Vector3(playerBuffCoin + coin, playerBuffXP + xp, playerBuffLuck + luck));
    }

    public float playerBuffCoin => playerBuffs.Value.X;
    public bool playerBuffCoinActive => playerBuffCoin > 0;
    public float playerBuffXP => playerBuffs.Value.Y;
    public bool playerBuffXPActive => playerBuffXP > 0;
    public float playerBuffLuck => playerBuffs.Value.Z;
    public bool playerBuffLuckActive => playerBuffLuck > 0;

    public int maxExp => 20 + Level * Level * 20;

    public PlayerCamera camera;

    public static List<MyPlayer> players = new List<MyPlayer>();
    public static MyPlayer localPlayer;

    //Fish Data
    public Item_Definition currentProcessedFish;
    public string currentProcessedFishType;
    public string currentProcessedFishLevel;

    public Inventory FishTeam;
    bool fishTeamLoaded = false;

    public int battleId { get; set; } = -1;
    public RuntimeMon currentMonData { get; set; }
    public FishMon currentMon;
    private bool[] aliveStatus;

    public bool acceptBattle = false;

    //Boat stuff
    public bool inBoat => HasEffect<BoatEffect>();
    public float boatSpeed;

    //Sound stuff
    private bool firstTimeSync_exp = true;
    private bool firstTimeSync_level = true;

    private int currentSaveVersion = -1;

    private HashSet<string> _caughtFish = new HashSet<string>();
    public bool HasCaughtFish(string id)
    {
        return _caughtFish?.Contains(id) ?? false;
    }

    public override void Awake()
    {
        if (IsLocal)
        {
            FishTeam = Inventory.CreateInventory("fishTeam_" + Name, FightSystem.TEAM_SIZE);
            DefaultInventory.SetCapacity(10);
            localPlayer = this;
            camera = new PlayerCamera();
            camera.Init(Entity, PlayerCamera.DEFAULT_ZOOM);
            experience.OnSync += (int oldValue, int newValue) =>
            {
                if (experience != 0 && firstTimeSync_exp)
                {
                    SliderUI.lastKnownExp = experience;
                    firstTimeSync_exp = false;
                }
            };
            _level.OnSync += (int oldValue, int newValue) =>
            {
                if (newValue > oldValue)
                {
                    if (!firstTimeSync_level)
                    {
                        LevelUpUI.OnLevelUp(newValue);
                    }
                    firstTimeSync_level = false;
                }
            };
            CallServer_RequestVerify((int)Leaderboard.GetLocalScore());
        }
        if (Network.IsServer)
        {
            //Figure out why that causes battles to fail?
            // foreach (IFighter player in IFighter.fighters)
            // {
            //     player.SendMonData();
            // }

            FishTeam = Inventory.CreateInventory("fishTeam_" + Name, FightSystem.TEAM_SIZE);
            currentRodID.Set(AO.Save.GetString(this, "currentRod", "Rod_Wood"));
            if (!RodsManager.RodsProducts.Any(x => x.Product.Id == currentRodID.Value))
            {
                currentRodID.Set("Rod_Wood");
            }
            lastRodID.Set(AO.Save.GetString(this, "lastRod", currentRodID));
            if (!lastRodID.Value.IsNullOrEmpty() && !RodsManager.RodsProducts.Any(x => x.Product.Id == lastRodID.Value))
            {
                lastRodID.Set("Rod_Wood");
            }
            currentBoatID.Set(AO.Save.GetString(this, "currentBoat", ""));
            if (!currentBoatID.Value.IsNullOrEmpty() && !Store.BoatsProducts.Any(x => x.Id == currentBoatID.Value))
            {
                currentBoatID.Set("Boat_Basic");
            }
            Level = AO.Save.GetInt(this, "level", 0);
            //level.Set(0);
            experience.Set(AO.Save.GetInt(this, "exp", 0));

            _caughtFish = new HashSet<string>(AO.Save.GetString(this, "caughtFish", "").Split(','));

            LoadBuffs();

            //Temporary
            {
                // AO.Save.SetInt(this, "welcome", 0);
                //AO.Save.SetInt(this, "boughtStarterPack", 0);
                //AO.Save.SetString(this, "starterPackTimer", "");
            }

            if (AO.Save.GetInt(this, "welcome", 0) == 0)
            {
                AO.Save.SetInt(this, "welcome", 1);
                CallClient_WelcomeMessage();
            }

            //Starter pack stuff
            boughtStarterPack.Set(AO.Save.GetInt(this, "boughtStarterPack", 0) == 1);
            starterPackTimer = BasicDateTimer.ServerSet(this, "starterPackTimer", 3);
            CallClient_SetStarterPackTimer(starterPackTimer.GetSerializedStartTime());

            currentSaveVersion = AO.Save.GetInt(this, "saveVersion", 0);
        }

        players.Add(this);
        IFighter.fighters.Add(this);
        InitRig();
    }

    [ClientRpc]
    public void WelcomeMessage()
    {
        if (Network.IsServer || !IsLocal) return;
        PopupUI.Show("Welcome to Fishermon!", $"Cast your line to catch amazing fish, train them to become stronger, and challenge other players to exciting battles!\nSell your catches to earn money and upgrade your Rods and Boats\nYour adventure begins now!", () => { });
    }

    [ClientRpc]
    public void SetStarterPackTimer(string startTime)
    {
        if(Network.IsClient)
        {
            starterPackTimer = BasicDateTimer.ClientSet(this, startTime, 3);
        }
    }

    [ServerRpc]
    public void RequestVerify(int expectedLevel)
    {
        //Leaderboard weirdness? idk. Let's trust client and reset leaderboard. Worst case they go below Level?
        if (Level < expectedLevel)
        {
            //Set increment to the difference so that leaderboard is at level
            GameManager.OnIncrementScore?.Invoke(this, Level - expectedLevel);
        }
        ValidateFishTeam(true);
        CallClient_GetCaughtFishes(string.Join(",", _caughtFish), rpcTarget: this);
    }

    [ClientRpc]
    public void GetCaughtFishes(string fishIds)
    {
        if (Network.IsServer)
            return;
        _caughtFish = new HashSet<string>(fishIds.Split(','));
    }

    public void ResetPlayerData()
    {
        Level = 0;
        experience.Set(0);
        currentRodID.Set("Rod_Wood");
        lastRodID.Set("Rod_Wood");
        currentBoatID.Set("");
        Economy.WithdrawCurrency(this, "FishCoin", Economy.GetBalance(this, "FishCoin"));
        for (int i = 0; i < FishTeam.Items.Length; i++)
        {
            if (FishTeam.Items[i] != null)
                Inventory.DestroyItem(FishTeam.Items[i]);
        }
        for (int i = 0; i < DefaultInventory.Items.Length; i++)
        {
            if (DefaultInventory.Items[i] != null)
                Inventory.DestroyItem(DefaultInventory.Items[i]);
        }
        SaveFishTeam();
        Inventory.ServerForceSyncInventory(FishTeam);
        Inventory.ServerForceSyncInventory(DefaultInventory);
        AO.Save.SetInt(this, "boughtStarterPack", 0);
        boughtStarterPack.Set(false);
        _caughtFish = new HashSet<string>();
        AO.Save.SetString(this, "caughtFish", "");
        CallClient_GetCaughtFishes("", rpcTarget: this);
    }

    public static void SendMessageToAll(string message)
    {
        foreach (MyPlayer player in players)
        {
            Chat.SendMessage(player, message);
        }
    }

    public void SaveBuffs()
    {
        AO.Save.SetDouble(this, "playerBuff_Coin_Duration", playerBuffCoin);
        AO.Save.SetDouble(this, "playerBuff_XP_Duration", playerBuffXP);
        AO.Save.SetDouble(this, "playerBuff_Luck_Duration", playerBuffLuck);
    }

    public void LoadBuffs()
    {
        float coinDuration = (float)AO.Save.GetDouble(this, "playerBuff_Coin_Duration", -1);
        float xpDuration = (float)AO.Save.GetDouble(this, "playerBuff_XP_Duration", -1);
        float luckDuration = (float)AO.Save.GetDouble(this, "playerBuff_Luck_Duration", -1);
        playerBuffs.Set(new Vector3(coinDuration, xpDuration, luckDuration));
    }

    public override void OnDestroy()
    {
        if (Network.IsServer)
        {
            SaveBuffs();
            AO.Save.SetInt(this, "level", Level);
            AO.Save.SetInt(this, "exp", experience.Value);
            ValidateFishTeam();
            if (!IsTeamEmpty())
            {
                SaveFishTeam();
            }
        }

        if (FishTeam != null)
            Inventory.DestroyInventory(FishTeam);

        players.Remove(this);
        IFighter.fighters.Remove(this);
        if (currentMon?.Entity.Alive() == true)
        {
            currentMon.Entity.Destroy();
            currentMon = null;
        }
    }

    private void InitRig()
    {
        var aoLayer = SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0);
        var gameLayer = SpineAnimator.SpineInstance.StateMachine.CreateLayer("game_layer", 10);
        var empty = gameLayer.CreateState("__CLEAR_TRACK__", 0, true);
        var fish_idle = gameLayer.CreateState("011FISH/fishing_wait", 0, true);

        var castRod = gameLayer.CreateState("011FISH/fishing_cast", 0, false);
        castRod.Speed = 1.33f;
        var castRodTrigger = SpineAnimator.SpineInstance.StateMachine.CreateVariable("cast_rod", StateMachineVariableKind.TRIGGER);
        gameLayer.CreateGlobalTransition(castRod).CreateTriggerCondition(castRodTrigger);
        gameLayer.CreateTransition(castRod, fish_idle, true);

        var pullOut = gameLayer.CreateState("011FISH/fishing_pull_out", 0, false);
        pullOut.Speed = 1.33f;
        var pullOutTrigger = SpineAnimator.SpineInstance.StateMachine.CreateVariable("pull_out", StateMachineVariableKind.TRIGGER);
        gameLayer.CreateGlobalTransition(pullOut).CreateTriggerCondition(pullOutTrigger);
        gameLayer.CreateTransition(pullOut, empty, true);

        aoLayer.AddSimpleTriggeredState("force_idle", "Idle", goBackToIdle: false, loop: true);
        aoLayer.AddSimpleTriggeredState("cry_loop", "Emote/crying_loop", goBackToIdle: false, loop: true);
        aoLayer.AddSimpleTriggeredState("victory_loop", "Emote/Celebrate", goBackToIdle: false, loop: true);

        gameLayer.InitialState = empty;
    }

    [ClientRpc]
    public void CutMusic()
    {
        GameManager.StopMusic();
        GameManager.allowMusicPlay = false;
    }

    public override void ReadFrameData(AO.StreamReader reader)
    {
        //Is this a good time to save? Probably-ish :shrug:
        if (Network.IsServer)
        {
            if (!fishTeamLoaded)
            {
                LoadFishTeam();
                fishTeamLoaded = true;
            }
            //Server validate & save IF NECESSARY
            ValidateFishTeam();
        }
    }

    public bool needsMonDisplay;
    public void HandleMonDisplay()
    {
        if (currentMonData == null)
        {
            if (currentMon?.Entity.Alive() == true)
                currentMon.Entity.Destroy();
            currentMon = null;
            return;
        }

        if (currentMonData.itemDef.IsNullOrEmpty())
        {
            if (currentMon?.Entity.Alive() == true)
                currentMon.Entity.Destroy();
            currentMon = null;
            return;
        }

        if (currentMon == null)
        {
            var monEntity = Entity.Instantiate(Assets.GetAsset<Prefab>("Mon.prefab"));
            if (monEntity != null)
            {
                currentMon = monEntity.GetComponent<FishMon>();
                currentMon.Awaken();
            }
            else
            {
                Notifications.Show("Failed to create mon entity!");
                return;
            }
        }

        if (currentMon == null)
        {
            Notifications.Show("Failed to get FishMon component!");
            return;
        }

        currentMon.animator.SpineInstance.Scale = new Vector2(battleDirection.Value.X, 1.0f);
        currentMon.SetMon(this, currentMonData);
        //currentMon.Entity.LocalScaleX = battleDirection.Value.X;
        float dist = 1.7f + (inBoat ? 2.0f : 0.0f);
        currentMon.Entity.Position = Entity.Position + (battleDirection.Value.X == 1.0f ? Vector2.Right * (dist - FishItemManager.GetFishXOffset(currentMonData.itemDef)) : Vector2.Left * (dist - FishItemManager.GetFishXOffset(currentMonData.itemDef)));
        currentMon.Entity.Position += Vector2.Up * 0.5f;
    }

    public override Vector2 CalculatePlayerVelocity(Vector2 currentVelocity, Vector2 input, float deltaTime)
    {
        if (HasEffect<BoatEffect>() && HasEffect<EmoteEffect>())
        {
            RemoveFreezeReason(nameof(EmoteEffect));
        }

        if (battleId != -1)
        {
            input = FightSystem.GetPlayerDirection(battleId, this);
            return DefaultPlayerVelocityCalculation(currentVelocity, input, deltaTime, battleId == -1 ? 1.0f : FightSystem.GetPlayerSpeedMultiplier(battleId, this));
        }

        // If in boat, don't process normal movement.
        if (inBoat)
        {
            // Add force based on input
            currentVelocity += input * deltaTime * 5 * boatSpeed;

            // Add water resistance/dampening
            float waterResistance = 0.92f;
            currentVelocity *= MathF.Pow(waterResistance, deltaTime * 60);

            // Clamp maximum velocity to prevent excessive speed
            float maxSpeed = 15f * boatSpeed;
            if (currentVelocity.Length > maxSpeed)
            {
                currentVelocity = currentVelocity.Normalized * maxSpeed;
            }

            return currentVelocity;
        }

        return DefaultPlayerVelocityCalculation(currentVelocity, input, deltaTime, battleId == -1 ? 1.0f : FightSystem.GetPlayerSpeedMultiplier(battleId, this));
    }

    int FishTeamCount => FishTeam.Items.Count(item => item != null);

    public bool IsTeamFull()
    {
        if (!Network.IsServer && !IsLocal)
            return false;
        return FishTeamCount >= FightSystem.TEAM_SIZE;
    }

    public bool IsTeamEmpty()
    {
        if (!Network.IsServer && !IsLocal)
            return false;
        return FishTeamCount == 0;
    }

    public void SaveFishTeam()
    {
        if (!fishTeamLoaded)
        {
            return;
        }

        AO.Save.SetDouble(this, "has_team", 1);
        int numElements = 0;
        for (int i = 0; i < FishTeam.Items.Length; i++)
        {
            Item_Instance item = FishTeam.Items[i];
            if (item != null)
            {
                AO.Save.SetString(this, "team_" + i, item.Definition.Id);
                AO.Save.SetString(this, "team_" + i + "_LVL", item.GetMetadata("Level"));
                AO.Save.SetString(this, "team_" + i + "_EXP", item.GetMetadata("Exp"));
                AO.Save.SetString(this, "team_" + i + "_TYPE", item.GetMetadata("Type"));
                numElements++;
            }
            else
            {
                AO.Save.SetString(this, "team_" + i, "NULL");
            }
        }
    }

    public void ValidateFishTeam(bool forceSync = false)
    {
        if (!Network.IsServer)
            return;

        int mythFishCount = 0;
        bool fishTeamChanged = false;
        foreach (Item_Instance item in FishTeam.Items)
        {
            if (item == null)
                continue;

            if (!FishItemManager.IsFishTeamable(item.Definition.Id))
            {
                fishTeamChanged = true;
                if (Inventory.CanMoveItemToInventory(item, DefaultInventory))
                {
                    Inventory.MoveItemToInventory(item, DefaultInventory);
                }
                else
                {
                    Inventory.DestroyItem(item);
                }
            }
            else if (FishItemManager.GetFishRarity(item.Definition.Id) == ItemRarity.Mythic)
            {
                mythFishCount++;
                if (mythFishCount > 1)
                {
                    CallClient_SendMessage("You can only have one Mythical Boss in your team!");
                    fishTeamChanged = true;
                    if (Inventory.CanMoveItemToInventory(item, DefaultInventory))
                    {
                        Inventory.MoveItemToInventory(item, DefaultInventory);
                    }
                    else
                    {
                        Inventory.DestroyItem(item);
                    }
                }
            }
            else
            {
                fishTeamChanged |= ValidateFishItem(item);
            }
        }
        HandleVersionChanges();
        if (forceSync || fishTeamChanged)
        {
            Inventory.ServerForceSyncInventory(FishTeam);
            SaveFishTeam();
        }
    }

    public void HandleVersionChanges()
    {
        if (currentSaveVersion == 4)
            return;

        GameManager.OnIncrementScore?.Invoke(this, Level);

        currentSaveVersion = 4;
        AO.Save.SetInt(this, "saveVersion", currentSaveVersion);
    }

    public void ResetFishLvlTo(int newMax)
    {
        bool fishReset = false;
        foreach (Item_Instance item in FishTeam.Items)
        {
            if (item == null)
                continue;
            if (FishItemManager.IsFishTeamable(item.Definition.Id))
            {
                ValidateFishItem(item);
                if (int.Parse(item.GetMetadata("Level")) > newMax)
                {
                    item.SetMetadata("Level", newMax.ToString());
                    item.SetMetadata("Exp", "0");
                    fishReset = true;
                }
            }
        }
        foreach (Item_Instance item in DefaultInventory.Items)
        {
            if (item == null)
                continue;

            if (FishItemManager.IsFishTeamable(item.Definition.Id))
            {
                ValidateFishItem(item);
                int maxAllowed = (int)(Level * 1.75f);
                if (int.Parse(item.GetMetadata("Level")) > newMax)
                {
                    item.SetMetadata("Level", newMax.ToString());
                    item.SetMetadata("Exp", "0");
                    fishReset = true;
                }
            }
        }
        if (fishReset && Entity.Alive())
        {
            CallClient_SendPopup("Fish Team Reset", "As part of the Early Access rebalancing, your fish team level has been reduced.\nYou can now restart leveling your team normally!", rpcTarget: this);
        }
    }

    static bool ValidateFishItem(Item_Instance item)
    {
        bool changed = false;
        if (item != null && FishItemManager.IsFishTeamable(item.Definition.Id))
        {
            if (item.GetMetadata("Level").IsNullOrEmpty())
            {
                item.SetMetadata("Level", "1");
                changed = true;
            }
            if (item.GetMetadata("Exp").IsNullOrEmpty())
            {
                item.SetMetadata("Exp", "0");
                changed = true;
            }
            if (item.GetMetadata("Type").IsNullOrEmpty())
            {
                item.SetMetadata("Type", new string[] { "grass", "fire", "water" }.GetRandom());
                changed = true;
            }
        }
        return changed;
    }

    static void ValidateFishItem(Item_Definition item, ref string type, ref string level)
    {
        level = "1";
        type = "";
        if (item != null && FishItemManager.IsFishTeamable(item.Id))
        {
            type = new string[] { "grass", "fire", "water" }.GetRandom();
        }
    }

    void LoadFishTeam()
    {
        if (AO.Save.GetDouble(this, "has_team") == 1)
        {
            for (int i = 0; i < FightSystem.TEAM_SIZE; i++)
            {
                var itemId = AO.Save.GetString(this, "team_" + i);
                if (!itemId.IsNullOrEmpty() && !itemId.StartsWith("NULL"))
                {
                    var fish = FishItemManager.GetFish(itemId);
                    if (fish == null)
                        continue;
                    var item = Inventory.CreateItem(fish, 1);
                    if (Inventory.CanMoveItemToInventory(item, FishTeam))
                        Inventory.MoveItemToInventory(item, FishTeam);
                    item.SetMetadata("Level", AO.Save.GetString(this, "team_" + i + "_LVL"));
                    item.SetMetadata("Exp", AO.Save.GetString(this, "team_" + i + "_EXP"));
                    item.SetMetadata("Type", AO.Save.GetString(this, "team_" + i + "_TYPE"));
                }
            }
            Inventory.ServerForceSyncInventory(FishTeam);
        }
        ValidateFishTeam();
    }

    public override void Update()
    {
        if (!IsLocal) return;

        camera.Update();
    }

    //Server side
    public void ProcessNewFish(Item_Definition fish, int forcedLevel = -1)
    {
        if (!this.Alive() || fish == null)
            return;
        _caughtFish.Add(fish.Id);
        AO.Save.SetString(this, "caughtFish", string.Join(",", _caughtFish));

        currentProcessedFish = fish;
        ValidateFishItem(currentProcessedFish, ref currentProcessedFishType, ref currentProcessedFishLevel);
        int fishlvl = (int)Math.Clamp(Level / 2.0f, 1, 100);
        int fishLvlMin = Math.Clamp(fishlvl - 3, 1, 100);
        int fishLvlMax = Math.Clamp(fishlvl + 2, 1, 100);
        //Ramdom bettween min and max
        currentProcessedFishLevel = forcedLevel != -1 ? forcedLevel.ToString() : new Random().Next(fishLvlMin, fishLvlMax).ToString();
        if (FishItemManager.GetFishRarity(fish.Id) == ItemRarity.Legendary)
        {
            SendMessageToAll($"{Name} has caught a Lv. {currentProcessedFishLevel} {fish.Name}!");
        }
        CallClient_CaughtFish(fish.Id, currentProcessedFishType, currentProcessedFishLevel);
        //We use the xp boost for the price here as it's only used to calculate the gained exp
        int price = FishItemManager.Instance.GetFishPrice(fish.Id, int.Parse(currentProcessedFishLevel), playerBuffXPActive ? 1 : 0);
        AddExperience(price * 2);
        if (!Game.LaunchedFromEditor)
        {
            Battlepass.IncrementProgress(this, "67a485e7a9736d165c7b6d64", 1);
        }
    }

    //Battle stuff
    public void GetFishTeam(ref RuntimeMon[] mons)
    {
        for (int j = 0; j < FishTeam.Items.Length; j++)
        {
            var fish = FishTeam.Items[j];
            if (fish == null)
                continue;
            int level = fish.GetMetadata("Level").IsNullOrEmpty() ? 1 : int.Parse(fish.GetMetadata("Level"));
            int exp = fish.GetMetadata("Exp").IsNullOrEmpty() ? 0 : int.Parse(fish.GetMetadata("Exp"));
            string type = fish.GetMetadata("Type");
            mons[j] = new RuntimeMon(fish.Definition.Id, type, level, exp);
        }
    }

    public void SendFishChoice(List<RuntimeMon> mons, int battleId)
    {
        AO.StreamWriter writer = new AO.StreamWriter();
        writer.Write(mons.Count);
        foreach (var mon in mons)
        {
            writer.WriteString(mon.itemDef);
            writer.WriteString(mon.type);
            writer.Write(mon.level);
        }
        if (!Entity.Alive())
            return;
        CallClient_RequestFishChoice(battleId, writer.byteStream.ToArray(), rpcTarget: this);
    }

    [ClientRpc]
    public void RequestFishChoice(int battleId, byte[] data)
    {
        if (!IsLocal) return;
        AO.StreamReader reader = new AO.StreamReader(data);
        int count = reader.Read<int>();
        List<RuntimeMon> mons = new List<RuntimeMon>();
        for (int i = 0; i < count; i++)
        {
            mons.Add(new RuntimeMon(reader.ReadString(), reader.ReadString(), reader.Read<int>(), 0));
        }
        FishSelectionUI.Show(mons, (int index) => { MyPlayer.CallServer_ValidateFishChoice(battleId, this.Name, index); });
    }

    [ServerRpc]
    public static void ValidateFishChoice(int battleId, string playerName, int index)
    {
        FightSystem.ValidateFishChoice(battleId, playerName, index);
    }

    public void SetFishData(int index, int level, int exp)
    {
        if (index < 0 || index >= FishTeam.Items.Length || FishTeam.Items[index] == null)
            return;
        FishTeam.Items[index].SetMetadata("Level", level.ToString());
        FishTeam.Items[index].SetMetadata("Exp", exp.ToString());
    }

    public void Attack(bool quickAttack)
    {
        if (Network.IsServer)
            return;
        currentMon?.Attack(quickAttack);
    }

    public void GetSendableMonData(ref AO.StreamWriter writer)
    {
        if (currentMonData == null)
        {
            writer.WriteString("");

            writer.WriteString("");
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        }
        else
        {
            writer.WriteString(currentMonData.itemDef);
            writer.WriteString(currentMonData.type);
            writer.Write(currentMonData.currentHealth);
            writer.Write(currentMonData.exp);
            writer.Write(currentMonData.level);
        }
    }

    public void SendMonData()
    {
        AO.StreamWriter writer = new AO.StreamWriter();
        GetSendableMonData(ref writer);

        // Check if this is an arena battle
        if (battleId >= 0 && battleId < FightSystem.instance.battles.Count)
        {
            var battle = FightSystem.instance.battles[battleId];
            if (battle.type == BattleType.Arena && Entity.Alive())
            {
                // Only send to self in arena battles
                CallClient_ReceiveMonData(writer.byteStream.ToArray(), rpcTarget: this);
                return;
            }
        }

        // For regular battles, send to all
        CallClient_ReceiveMonData(writer.byteStream.ToArray());
    }

    public void SetFromSendableMonData(ref AO.StreamReader reader)
    {
        currentMonData = RuntimeMon.FromCurrent(reader.ReadString(), reader.ReadString(), reader.Read<int>(), reader.Read<int>(), reader.Read<int>());
        needsMonDisplay = true;

        if (currentMonData.itemDef.IsNullOrEmpty() && battleId != -1)
        {
            FightSystem.EndBattle(battleId, this);
            if (IsLocal)
            {
                GameManager.PlayMusic("default");
            }
        }
    }

    [ClientRpc]
    public void ReceiveMonData(byte[] data)
    {
        AO.StreamReader reader = new AO.StreamReader(data);
        SetFromSendableMonData(ref reader);
    }

    public void SendTeamStatus(ref RuntimeMon[] mons)
    {
        var filteredMons = mons.Where(mon => mon != null).ToArray();
        bool[] updatedaliveStatus = new bool[filteredMons.Length];
        for (int i = 0; i < filteredMons.Length; i++)
        {
            updatedaliveStatus[i] = filteredMons[i].currentHealth > 0;
        }
        CallClient_ReceiveTeamStatus(updatedaliveStatus);
    }

    [ClientRpc]
    public void ReceiveTeamStatus(bool[] updatedaliveStatus)
    {
        aliveStatus = updatedaliveStatus;
    }

    public void HandleBattleEnd(bool won)
    {
        AddExperience((46 + (4 * Level)) * (playerBuffXPActive ? 2 : 1));
        inBattle.Set(false);
        inArenaBattle.Set(false);  // Reset arena state
        currentMonData = null;
        SendMonData();
        CallClient_PlayEndBattleEmote(won);
    }

    [ClientRpc]
    public void PlayEndBattleEmote(bool won)
    {
        if (won)
        {
            AddEffect<MyEmoteEffect>(this, 3, (e) => e.EmoteName = "victory_loop");
        }
        else
        {
            AddEffect<MyEmoteEffect>(this, 3, (e) => e.EmoteName = "cry_loop");
        }

    }

    public void BattleFail(string reason)
    {
        if (!Entity.Alive())
            return;
        CallClient_SendMessage($"Failed to start battle: {reason}", rpcTarget: this);
    }

    [ClientRpc]
    public void SendPopup(string title, string message)
    {
        if (Network.IsServer || !IsLocal) return;
        PopupUI.Show(title, message);
    }

    [ClientRpc]
    public void SendMessage(string message)
    {
        if (!IsLocal)
            return;
        Notifications.Show(message);
    }


    public void RequestSellFish(int inventoryIndex)
    {
        var Fish = DefaultInventory.Items[inventoryIndex];
        if (Fish == null)
        {
            Store.Instance.RefreshSell();
            //Notifications.Show($"This fish doesn't exist!");
            return;
        }
        CallServer_SellFish(inventoryIndex);
        //Needs to be called on client for some reason
        Inventory.RemoveItemFromInventory(Fish, DefaultInventory);
        Store.Instance.RefreshSell();
        Notifications.Show($"Fish sold!");
        string metadata = Fish.GetMetadata("Level");
        int fishLevel = int.Parse(metadata.IsNullOrEmpty() ? "1" : metadata);
        int price = FishItemManager.Instance.GetFishPrice(Fish.Definition.Id, fishLevel, playerBuffCoinActive ? 1 : 0);
        SpawnCoinParticles(Input.GetMouseScreenPosition(), price);
        AudioAsset audio = price > 100 ? Assets.GetAsset<AudioAsset>("audio/sell_big.wav") : Assets.GetAsset<AudioAsset>("audio/sell_small.wav");
        SFX.Play(audio, new SFX.PlaySoundDesc() { Positional = false, Volume = 0.5f });
    }

    [ServerRpc]
    public void SellFish(int inventoryIndex)
    {
        if (inventoryIndex < 0 || inventoryIndex > DefaultInventory.Items.Length)
            return;
        var Fish = DefaultInventory.Items[inventoryIndex];
        if (Fish == null)
        {
            Inventory.ServerForceSyncInventory(DefaultInventory);
            return;
        }
        Inventory.RemoveItemFromInventory(Fish, DefaultInventory);
        string metadata = Fish.GetMetadata("Level");
        int fishLevel = int.Parse(metadata.IsNullOrEmpty() ? "1" : metadata);
        int price = FishItemManager.Instance.GetFishPrice(Fish.Definition.Id, fishLevel, playerBuffCoinActive ? 1 : 0);
        if (price > 0)
        {
            Store.AddMoney(this, price);
        }
    }

    public void RequestSetRod(string rodId)
    {
        CallServer_SetRod(rodId);
    }

    [ServerRpc]
    public void SetRod(string rodId)
    {
        var currentRodIndex = RodsManager.RodsProducts.First(x => x.Product.Id == lastRodID.Value).RodIndex;
        var newRodIndex = RodsManager.RodsProducts.First(x => x.Product.Id == rodId).RodIndex;
        if (newRodIndex <= currentRodIndex)
        {
            currentRodID.Set(rodId);
            AO.Save.SetString(this, "currentRod", rodId);
        }
    }


    [ServerRpc]
    public void SellNewFish()
    {
        if (currentProcessedFish == null) return;
        int price = FishItemManager.Instance.GetFishPrice(currentProcessedFish.Id, int.Parse(currentProcessedFishLevel), playerBuffCoinActive ? 1 : 0);
        if (price > 0)
        {
            Store.AddMoney(this, price);
        }
        currentProcessedFish = null;
    }

    [ServerRpc]
    public void KeepNewFish()
    {
        if (currentProcessedFish == null) return;
        var item = Inventory.CreateItem(currentProcessedFish, 1);
        if (Inventory.CanMoveItemToInventory(item, DefaultInventory))
        {
            Inventory.MoveItemToInventory(item, DefaultInventory);
            item.SetMetadata("Level", currentProcessedFishLevel);
            item.SetMetadata("Type", currentProcessedFishType);
            ValidateFishItem(item);
        }
        currentProcessedFish = null;
    }

    [ServerRpc]
    public void AddNewFish()
    {
        if (currentProcessedFish == null) return;
        var item = Inventory.CreateItem(currentProcessedFish, 1);
        if (Inventory.CanMoveItemToInventory(item, FishTeam))
        {
            Inventory.MoveItemToInventory(item, FishTeam);
            item.SetMetadata("Level", currentProcessedFishLevel);
            item.SetMetadata("Type", currentProcessedFishType);
            ValidateFishItem(item);
        }
        currentProcessedFish = null;

    }

    public void AddExperience(int amount)
    {
        if (!Network.IsServer || !Entity.Alive())
            return;
        int nextExp = experience.Value + amount;
        while (nextExp >= maxExp)
        {
            nextExp -= maxExp;
            Level++;
            GameManager.OnIncrementScore?.Invoke(this, 1);
        }
        experience.Set(nextExp);
    }

    public void CallKeepNewFish() { CallServer_KeepNewFish(); }
    public void CallSellNewFish() { CallServer_SellNewFish(); }
    public void CallAddNewFish() { CallServer_AddNewFish(); }

    [ClientRpc]
    public void CaughtFish(string fishID, string type, string level)
    {
        _caughtFish.Add(fishID);
        currentProcessedFish = FishItemManager.GetFish(fishID);
        if (IsLocal)
            ObtainFishUI.ShowFish(FishItemManager.GetFish(fishID), type, level, CallKeepNewFish, CallSellNewFish, CallAddNewFish);
    }

    public void SetBattleContext(int battleId)
    {
        //Don't do anything as player can only be in one battle at a time
    }

    public void SetZoom(float zoom)
    {
        if (!IsLocal) return;
        camera.SetZoom(zoom);
    }

    public void ResetZoom()
    {
        if (!IsLocal) return;
        camera.SetZoom(PlayerCamera.DEFAULT_ZOOM);
    }

    [ServerRpc]
    public void AttemptBattle(string targetId)
    {
        ValidateFishTeam();
        MyPlayer target = players.Find(p => p.Name == targetId);
        if (!target.Alive() || target == this || target.inBattle || inBattle)
            return;
        if (IsTeamEmpty() || target.IsTeamEmpty())
            return;
        acceptBattle = true;
        target.CallClient_RequestBattle(Name, rpcTarget: target);
    }

    public Dictionary<string, int> numRequests = new Dictionary<string, int>();

    [ClientRpc]
    public void RequestBattle(string requester)
    {
        if (!IsLocal)
            return;
        if (!numRequests.ContainsKey(requester))
            numRequests.Add(requester, 0);
        numRequests[requester]++;
        if (numRequests[requester] > 3)
            return;

        Action onRefuse;
        if (numRequests[requester] >= 3)
        {
            onRefuse = () =>
            {
                PopupUI.Show("Are you sure?", $"You will not receive any more requests from {requester} for the rest of the session! You will still be able to send requests to this player.",
                 () => { numRequests[requester] = 2; }, () => { }, "No!", "Yes! Ignore for this session");
            };
        }
        else
        {
            onRefuse = () => { };
        }

        PopupUI.Show("Battle Request", $"{requester} wants to battle you!", () =>
        {
            CallServer_ConfirmBattle(requester);
            numRequests[requester] = 0;
        }, onRefuse, "Accept", numRequests[requester] >= 3 ? "Decline and ignore for this session" : "Decline");
    }

    [ServerRpc]
    public void ConfirmBattle(string requester)
    {
        ValidateFishTeam();
        MyPlayer target = players.Find(p => p.Name == requester);
        if (target == null || target.acceptBattle == false)
            return;
        if (IsTeamEmpty() || target.IsTeamEmpty())
            return;
        acceptBattle = false;
        target.acceptBattle = false;
        FightSystem.StartBattle(target, this, BattleType.InWorld);
    }

    float slowUpdateTimer = 0;
    private int lastHotbarIndex = -1;
    public override void LateUpdate()
    {
        if (Network.IsServer)
        {
            //Only run this once every 10 seconds. 
            //Buffs don't need to be updated every frame.
            //And syncvars are expensive
            if (slowUpdateTimer > 10)
            {
                if (playerBuffLuckActive || playerBuffXPActive || playerBuffCoinActive)
                {

                    playerBuffs.Set(new Vector3(playerBuffs.Value.X > 10 ? playerBuffs.Value.X - 10 : -1, playerBuffs.Value.Y > 10 ? playerBuffs.Value.Y - 10 : -1, playerBuffs.Value.Z > 10 ? playerBuffs.Value.Z - 10 : -1));
                }
                SaveBuffs();
                //Also use that time to save some other data that saving somewhere else crashes for some reason
                AO.Save.SetInt(this, "level", Level);
                AO.Save.SetInt(this, "exp", experience.Value);
                SaveFishTeam();
                slowUpdateTimer = 0;
            }
            else
            {
                slowUpdateTimer += Time.DeltaTime;
            }
            return;

        }

        // Hide player in arena battles except for local player
        if (inArenaBattle && !IsLocal)
        {
            SpineAnimator.LocalEnabled = false;
            return;
        }
        SpineAnimator.LocalEnabled = true;

        DrawWorldUI();

        if (needsMonDisplay && Velocity.Length < 0.01f)
        {
            HandleMonDisplay();
            needsMonDisplay = false;
        }

        if (inBattle)
        {
            Entity.LocalScaleX = battleDirection.Value.X;
        }

        if (!IsLocal) return;

        DrawFishTeam();

        if (inBattle.Value)
        {
            UIManager.DrawUI(Position);
            return;
        }

        DrawScreenUI();

        if (currentBoatID.Value.IsNullOrEmpty())
        {
            DrawDefaultAbilityUI(new AbilityDrawOptions()
            {
                AbilityElementSize = 75,
                Abilities = new Ability[] { GetAbility<FishingAbility>(), GetAbility<FightAbility>() }
            });
        }
        else if (inBoat)
        {
            DrawDefaultAbilityUI(new AbilityDrawOptions()
            {
                AbilityElementSize = 75,
                Abilities = new Ability[] { GetAbility<BoatAbility>() }
            });
        }
        else
        {
            DrawDefaultAbilityUI(new AbilityDrawOptions()
            {
                AbilityElementSize = 75,
                Abilities = new Ability[] { GetAbility<FishingAbility>(), GetAbility<FightAbility>(), GetAbility<BoatAbility>() }
            });
        }

        var hotbarResult = Inventory.DrawHotbar(DefaultInventory.Id, new Inventory.DrawOptions()
        {
            HotbarItemCount = 5,
            AllowDragDrop = true
        });

        //Draw icons copying the inventory draw but on top to add type and level
        var iconRect = hotbarResult.EntireRect.CutTop(100).FitAspect(1).Offset(-155, -10);
        for (int i = 0; i < (hotbarResult.InventoryOpen ? 10 : 5); i++)
        {
            var item = DefaultInventory.Items[i];
            if (item != null)
            {
                if (!item.GetMetadata("Type").IsNullOrEmpty())
                {
                    Rect innerRect = iconRect.Grow(-10);
                    //UI.Image(iconRect, UI.WhiteSprite);
                    Rect topRight = innerRect.TopRightRect().Grow(0, 0, 30, 30).Offset(0, 5);
                    UI.Image(topRight, Assets.GetAsset<Texture>($"ui/type_{item.GetMetadata("Type")}.png"));
                    Rect bottomRight = innerRect.BottomRightRect().Grow(30, 0, 0, 30).Offset(-5, 10);
                    UI.Text(bottomRight, $"Lv. {item.GetMetadata("Level")}", new UI.TextSettings()
                    {
                        Font = UI.Fonts.BarlowBold,
                        HorizontalAlignment = UI.HorizontalAlignment.Right,
                        Size = 24,
                        Color = Vector4.White,
                        Outline = true,
                        OutlineColor = Vector4.Black
                    });
                }
                else if (item.Definition.Id == "Fish_Candy")
                {
                    if (UI.Button(iconRect, "Candy", new UI.ButtonSettings() { Sprite = Assets.GetAsset<Texture>("ui/candy.png"), PressedColor = new Vector4(0.7f, 0.7f, 0.7f, 1.0f) }, new UI.TextSettings() { Size = 24, Color = Vector4.Black }).Clicked)
                    {
                        TeamInfoDisplay.DisplayTeam(true);
                    }
                }
            }
            iconRect = iconRect.Offset(99, 0);
            if (i == 4 && hotbarResult.InventoryOpen)
            {
                iconRect = iconRect.Offset(-99 * 5, -110);
            }
        }

        //Draw Icon below that opens the team info display
        float xOffset = Game.IsMobile ? 170 : 290;
        var teamInfoIconRect = UI.SafeRect.BottomCenterRect().Offset(xOffset, 60).Grow(50, 50, 50, 50).FitAspect(1);
        UI.PushId("teamInfo_Button");
        var button = UI.Button(teamInfoIconRect, "", new UI.ButtonSettings() { Sprite = Assets.GetAsset<Texture>("ui/fishdex_icon.png") }, new UI.TextSettings() { Size = 24, Color = Vector4.Black });
        if (button.Clicked)
            TeamInfoDisplay.DisplayTeam();
        UI.PopId();

        UIManager.DrawUI(Position);
    }

    public int GetBadgeId(int level)
    {
        return Math.Clamp(1 + (int)MathF.Floor(((float)level + 1.5f) / 10), 1, 10);
    }

    private float fishBubbleTimer = 0;
    public void DrawWorldUI()
    {
        //Setup world UI
        using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
        using var _2 = IM.PUSH_Z(GetZOffset() - 0.0001f);
        using var _3 = UI.PUSH_SCALE_FACTOR(0.01f);

        //Level
        UI.TextSettings textSettings = UI.TextSettings.Default;
        textSettings.Size = 26;
        textSettings.VerticalAlignment = UI.VerticalAlignment.Center;
        textSettings.HorizontalAlignment = UI.HorizontalAlignment.Center;
        var levelRect = FullNameplateRect.CutLeft(20).Offset(-32.5f, 0);
        float badgeHeight = 0.39f;
        var badgeRect = levelRect.RightRect();
        float extraHeight = (badgeRect.Height - badgeHeight) / 2.0f;
        badgeRect = badgeRect.GrowUnscaled(-extraHeight, 0, -extraHeight, badgeHeight);
        badgeRect = badgeRect.OffsetUnscaled(0.09f, 0);
        Texture badge = Assets.GetAsset<Texture>($"ui/ranks/Rank-Icon-{GetBadgeId(Level)}.png");
        badgeRect = badgeRect.FitAspect(badge.Aspect, Rect.FitAspectKind.KeepHeight);
        UI.Image(badgeRect, badge);
        UI.Text(levelRect, $"{Level + 1}", textSettings);

        //Draw buffs
        float buffSize = 40;
        var buffRect = FullNameplateRect.CutRight(buffSize).Offset(buffSize + 5f, 0);

        void DrawBuff(Texture tex, float value)
        {
            Rect imageRect = buffRect;
            imageRect = imageRect.FitAspect(tex.Aspect, Rect.FitAspectKind.KeepWidth);
            UI.Image(imageRect, tex);
            Rect sliderRect = buffRect;
            sliderRect = sliderRect.CutBottom(10).Offset(0, -10);
            SliderUI.BasicSliderUI(sliderRect, value / (20 * 60));
            buffRect = buffRect.Offset(buffSize + 2.5f, 0);
        }

        if (playerBuffCoinActive)
        {
            DrawBuff(Assets.GetAsset<Texture>("ui/buffs/coin_boost.png"), playerBuffCoin);
        }
        if (playerBuffXPActive)
        {
            DrawBuff(Assets.GetAsset<Texture>("ui/buffs/xp_boost.png"), playerBuffXP);
        }
        if (playerBuffLuckActive)
        {
            DrawBuff(Assets.GetAsset<Texture>("ui/buffs/luck_boost.png"), playerBuffLuck);
        }

        if (currentProcessedFish != null)
        {
            fishBubbleTimer += Time.DeltaTime;
            if (fishBubbleTimer > 5.0f)
            {
                fishBubbleTimer = 0;
                currentProcessedFish = null;
                return;
            }

            float displaySize = 1.0f;
            if (fishBubbleTimer < 1.0f)
            {
                displaySize = Ease.OutBounce(fishBubbleTimer);
            }
            else if (fishBubbleTimer > 4.0f)
            {
                displaySize = Ease.InExpo(1 - (fishBubbleTimer - 4.0f));
            }

            var iconRect = FullNameplateRect.TopRect().Grow(Game.IsMobile ? 120 : 100, 0, 0, 0).FitAspect(1, Rect.FitAspectKind.KeepHeight).Scale(displaySize);
            UI.Image(iconRect, Assets.GetAsset<Texture>("ui/bubble.png"));
            var rarity = FishItemManager.GetFishRarity(currentProcessedFish.Id);
            var rarityColor = UIUtils.GetRarityColor(rarity);
            var rarityRect = iconRect;
            rarityRect = rarityRect.Grow(-15).Offset(0, 10 * displaySize).Scale(displaySize);
            UI.Image(rarityRect, Assets.GetAsset<Texture>("ui/rays/centre_aura.png"), rarityColor);
            rarityRect = rarityRect.Grow(8).Scale(displaySize);
            UI.Image(rarityRect, Assets.GetAsset<Texture>("ui/rays/ray_burst_2.png"), rarityColor);
            Texture fish = Assets.GetAsset<Texture>(currentProcessedFish.Icon);
            iconRect = iconRect.Grow(-20).Offset(0, 10 * displaySize).FitAspect(fish.Aspect).Scale(displaySize);
            UI.Image(iconRect, fish);

        }

        if (!inBattle || aliveStatus == null)
            return;

        int numFish = aliveStatus.Length;

        var fishInfoPos = Entity.Position + Vector2.Down * 0.25f;
        var itemSize = 0.32f;
        var spacing = 0.02f;
        var width = (itemSize + spacing) * numFish;
        var fishInfoRect = new Rect(fishInfoPos - new Vector2(width / 2.0f, itemSize / 2.0f), fishInfoPos + new Vector2(width / 2.0f, itemSize / 2.0f));
        Texture aliveTex = Assets.GetAsset<Texture>("ui/fish_alive.png");
        Texture deadTex = Assets.GetAsset<Texture>("ui/fish_dead.png");
        var rects = fishInfoRect.VerticalSlice(numFish, spacing);
        for (int i = 0; i < numFish; i++)
        {
            var fishRect = rects[i].FitAspect(aliveTex.Aspect);
            UI.Image(fishRect, aliveStatus[i] ? aliveTex : deadTex);
        }
    }

    void DrawFishTeam()
    {
        var leftRect = UI.SafeRect.LeftRect().Grow(-375, 55, -25, 75).Offset(100, 0);
        var topRect = leftRect.CutTop(30).Grow(0, 20, 0, -5);
        UI.Text(topRect, "BATTLE FISHES", new UI.TextSettings()
        {
            Font = UI.Fonts.BarlowBold,
            Size = 25,
            VerticalAlignment = UI.VerticalAlignment.Center,
            HorizontalAlignment = UI.HorizontalAlignment.Left,
            Color = Vector4.White,
            Outline = true,
            OutlineColor = Vector4.Black
        });
        var inventoryRect = leftRect.TopLeftRect().Grow(0, 115, 110 * 5, 0);
        Inventory.Draw(inventoryRect, FishTeam.Id, new Inventory.DrawOptions()
        {
            AllowDragDrop = battleId == -1,
            ShowExitButton = false,
            ShowBackground = false,
            Columns = 1,
            Rows = 5
        });

        //Draw icons copying the inventory draw but on top to add type and level
        var iconRect = inventoryRect.CutTop(115).FitAspect(1).Offset(0, -11);
        foreach (var item in FishTeam.Items)
        {
            if (item != null && !item.GetMetadata("Type").IsNullOrEmpty())
            {
                Rect innerRect = iconRect.Grow(-10);
                //UI.Image(iconRect, UI.WhiteSprite);
                Rect topRight = innerRect.TopRightRect().Grow(0, 0, 30, 30).Offset(0, 5);
                UI.Image(topRight, Assets.GetAsset<Texture>($"ui/type_{item.GetMetadata("Type")}.png"));
                Rect bottomRight = innerRect.BottomRightRect().Grow(30, 0, 0, 30).Offset(-5, 10);
                UI.Text(bottomRight, $"Lv. {item.GetMetadata("Level")}", new UI.TextSettings()
                {
                    Font = UI.Fonts.BarlowBold,
                    HorizontalAlignment = UI.HorizontalAlignment.Right,
                    Size = 24,
                    Color = Vector4.White,
                    Outline = true,
                    OutlineColor = Vector4.Black
                });
            }
            iconRect = iconRect.Offset(0, -109);
        }
    }

    public static void SpawnCoinParticles(Vector2 startPosition, int amount)
    {
        amount = Math.Clamp(amount, 0, 500);
        var leftRect = UI.ScreenRect.LeftRect().Inset(500, 0, 500, 0);
        leftRect = leftRect.Offset(Game.IsMobile ? 130 : 30, 150);
        var mainCurrencyRect = leftRect.CutLeft(200);
        ParticleData UpdateParticleVelocity(ParticleData data)
        {
            //move toward mainCurrencyRect
            if ((mainCurrencyRect.Center - data.Position).Length < 15)
            {
                data.Lifetime = 0;
            }
            data.Velocity = Vector2.Lerp(data.Velocity, (mainCurrencyRect.Center - data.Position).Normalized * 700, Time.DeltaTime * 2);
            return data;
        }
        Particle particle = new Particle(Assets.GetAsset<Texture>("ui/coin.png"), UpdateParticleVelocity, startPosition, Vector2.Zero, Vector4.White, 25, 1000);
        ParticlesUI.SpawnScreenParticle(particle, amount, true, 500);
    }

    float _starterRotation = 0;
    void DrawScreenUI()
    {
        var topItemBg = Assets.GetAsset<Texture>("ui/topbar_bg.png");
        {
            var leftRect = UI.SafeRect.LeftRect().TopRect().Grow(25, 75, 25, 75).Offset(100, -350);
            var mainCurrencyRect = leftRect.CutLeft(200);
            UI.Image(mainCurrencyRect, topItemBg, new UI.NineSlice() { slice = new Vector4(35, 35, 35, 35), sliceScale = 1.0f });
            var currencyIcon = Assets.GetAsset<Texture>("ui/coin.png");
            var currencyIconRect = mainCurrencyRect.CutLeft(80).Grow(5).FitAspect(currencyIcon.Aspect);
            UI.Image(currencyIconRect, currencyIcon);
            mainCurrencyRect.CutLeft(10);
            UI.Text(mainCurrencyRect, $"{Economy.GetBalance(this, Store.MoneyCurrency):N0}", new UI.TextSettings()
            {
                Font = UI.Fonts.BarlowBold,
                Size = 40,
                DoAutofit = true,
                AutofitMinSize = 10,
                AutofitMaxSize = 40,
                VerticalAlignment = UI.VerticalAlignment.Center,
                HorizontalAlignment = UI.HorizontalAlignment.Left,
                Color = Vector4.Black
            });

            // Luck boost
            if (RodsManager.GlobalLuckBoost > 1)
            {
                var luckBonusRect = UI.SafeRect.RightRect().TopRect().Grow(20, 0, 20, 240).Offset(-10, -440);
                var text = UIUtils.CenteredText(true);
                UI.Text(luckBonusRect, $"{RodsManager.GlobalLuckBoost}x luck boost!", text);
            }

            // Sparks Shop
            var buttonText = UIUtils.CenteredText(true);
            buttonText.Color = Vector4.Zero;
            var sparksButtonRect = UI.SafeRect.RightRect().TopRect().Grow(60).Offset(-80, -530);
            if (UI.Button(sparksButtonRect, "sparks", new UI.ButtonSettings()
            {
                Sprite = Assets.GetAsset<Texture>("ui/buffs/master_combo.png"),
                PressScaling = 0.95f
            }, buttonText).Clicked)
            {
                UIManager.OpenUI(() => Store.DrawShop(Store.Instance.sparksShop), 2);
            }
            var sparksTextRect = sparksButtonRect.CutBottom(45).Offset(0, -15);
            UI.Text(sparksTextRect, "Store", UIUtils.CenteredText(true));

            // Starter Pack
            if (!boughtStarterPack && starterPackTimer != null && !starterPackTimer.IsExpired())
            {
                sparksButtonRect = UI.SafeRect.RightRect().TopRect().Grow(75).Offset(-80, -670);
                _starterRotation += Time.DeltaTime * 100;
                UI.Image(sparksButtonRect, Assets.GetAsset<Texture>("ui/rays/ray_burst_2.png"), default, _starterRotation);
                sparksButtonRect = sparksButtonRect.Grow(-15);
                if (UI.Button(sparksButtonRect, "starter pack", new UI.ButtonSettings()
                {
                    Sprite = Assets.GetAsset<Texture>("ui/starter.png"),
                    PressScaling = 0.95f
                }, buttonText).Clicked)
                {
                    StarterPackUI.Show();
                }
                UI.Text(sparksButtonRect, starterPackTimer.GetRemainingTimeFormatted(), UIUtils.CenteredText(true));
                sparksTextRect = sparksButtonRect.Grow(15).CutBottom(45).Offset(0, -15);
                UI.Text(sparksTextRect, "Starter Pack", UIUtils.CenteredText(true));
            }
        }
    }

    public void SetPosition(Vector2 position)
    {
        if (!Entity.Alive() || Network.IsClient)
            return;
        Teleport(position);
    }

    public FishMon GetLocalMon() => currentMon;

    public bool HasCandy()
    {
        return DefaultInventory.Items.Any(item => item != null && item.Definition.Id == "Fish_Candy");
    }

    public void UseCandy(bool inInventory, int fishIndex)
    {
        if (!HasCandy())
            return;
        Inventory currentInv = inInventory ? DefaultInventory : FishTeam;
        var candyDef = FishItemManager.Instance.unusedItems["Fish_Candy"];
        int candyIndex = -1;
        for (int i = 0; i < DefaultInventory.Items.Length; i++)
        {
            if (DefaultInventory.Items[i] != null && DefaultInventory.Items[i].Definition.Id == candyDef.Id)
            {
                candyIndex = i;
                break;
            }
        }
        if (candyIndex == -1)
        {
            return;
        }
        var item = DefaultInventory.Items[candyIndex];
        if (item != null)
        {
            var fish = currentInv.Items[fishIndex];
            if (fish != null && FishItemManager.IsFishTeamable(fish.Definition.Id))
            {
                int fishLevel = int.Parse(fish.GetMetadata("Level"));
                if (fishLevel >= 100)
                {
                    Notifications.Show("Fish is already max level!");
                    return;
                }
                CallServer_ConsumeCandy(inInventory, fishIndex);
                SFX.Play(Assets.GetAsset<AudioAsset>("audio/level_up.wav"), new SFX.PlaySoundDesc() { Positional = false, Volume = 0.7f });
            }
        }
    }

    [ServerRpc]
    public void ConsumeCandy(bool inInventory, int fishIndex)
    {
        Inventory currentInv = inInventory ? DefaultInventory : FishTeam;
        var candyDef = FishItemManager.Instance.unusedItems["Fish_Candy"];
        int candyIndex = -1;
        for (int i = 0; i < DefaultInventory.Items.Length; i++)
        {
            if (DefaultInventory.Items[i] != null && DefaultInventory.Items[i].Definition.Id == candyDef.Id)
            {
                candyIndex = i;
                break;
            }
        }
        if (candyIndex == -1)
        {
            return;
        }
        var item = DefaultInventory.Items[candyIndex];
        if (item != null)
        {
            Inventory.DestroyItem(item, 1);
            var fish = currentInv.Items[fishIndex];
            if (fish != null && FishItemManager.IsFishTeamable(fish.Definition.Id))
            {
                int fishLevel = int.Parse(fish.GetMetadata("Level"));
                if (fishLevel >= 100)
                {
                    return;
                }
                ValidateFishItem(fish);
                fish.SetMetadata("Level", (int.Parse(fish.GetMetadata("Level")) + 1).ToString());
            }
        }
    }
}

