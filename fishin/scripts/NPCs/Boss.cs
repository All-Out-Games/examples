using AO;
using System.Collections;

public partial class Boss : Component, IFighter
{
    public bool SupportMultiBattle => true;
    Entity IFighter.Entity => Entity;
    SyncVar<bool> IFighter.inBattle => _inBattle;
    Vector2 IFighter.battleDirection { get => battleDirection.Value; set => battleDirection.Set(value); }

    [Serialized] public Interactable Interactable;
    [Serialized] public Spine_Animator SpineAnimator;
    [Serialized] public string FishId;  // The specific fish this boss uses
    [Serialized] public int Level = 10;  // Boss level
    [Serialized] public string Type = "water";  // Boss type (water/fire/grass)

    public int bossLevel => Level * 2;
    public int displayLevel => Level;

    SyncVar<bool> _inBattle = new SyncVar<bool>(false);
    public SyncVar<Vector2> battleDirection = new SyncVar<Vector2>(Vector2.Zero);

    public Vector2 MonPosition;
    public string Name => Entity.Name;
    //Only set the battleId on the client since server handles multiple battles at once
    public int battleId { get { return currentBattleId; } set { if (Network.IsClient) { currentBattleId = value; } } }
    public RuntimeMon currentMonData
    {
        get => battleMons.TryGetValue(battleId, out RuntimeMon mon) ? mon : null;
        set
        {
            if (battleId >= 0)
            {
                battleMons[battleId] = value;
            }
        }
    }

    public int currentBattleId = -1;
    public bool IsLocal => false;
    public Vector2 Velocity => _velocity;
    private Vector2 _velocity;
    public Vector2 startPosition;

    private RuntimeMon[] team = new RuntimeMon[1];  // Boss only has one fish
    public FishMon currentMon;

    public List<Action> onNextFrame = new();

    private Dictionary<int, RuntimeMon> battleMons = new Dictionary<int, RuntimeMon>();

    public override void Awake()
    {
        startPosition = Entity.Position;
        IFighter.fighters.Add(this);
        Interactable.Awaken();

        Interactable.CanUseCallback = (Player p) =>
        {
            MyPlayer mp = (MyPlayer)p;
            return (Network.IsServer || mp.IsLocal) && !mp.inBattle.Value && !mp.IsTeamEmpty() && !UIManager.IsUIActive();
        };

        Interactable.OnInteract = (Player p) =>
        {
            if (p.IsLocal)
            {
                GameManager.StopMusic();
                var bossTexture = FishItemManager.GetFish(FishId).Icon;
                BossBattleAnim.ShowBattleIntro(Name, bossTexture, Type);
            }

            float startTime = Time.TimeSinceStartup;
            Coroutine.Start(p.Entity, function());
            IEnumerator function()
            {
                while (Time.TimeSinceStartup - startTime < BossBattleAnim.FlashAnimLength * 8)
                {
                    yield return null;
                }
                FightSystem.StartBattle((MyPlayer)p, this, BattleType.Arena);
                if (p.IsLocal)
                {
                    GameManager.PlayMusic("battle");
                }
            }
        };

        StartRig();
        FishItemManager.RequestInit(SetTeam);
    }

    private void SetTeam()
    {
        team[0] = new RuntimeMon(FishId, Type, bossLevel, 0);
    }

    public override void Update()
    {
        if (_inBattle)
        {
            //hide animator if in battle
            SpineAnimator.LocalEnabled = false;
        }
        else
        {
            SpineAnimator.LocalEnabled = true;
        }

        foreach (var action in onNextFrame)
        {
            action();
        }
        onNextFrame.Clear();

        if (needsMonDisplay && Velocity.Length < 0.01f)
        {
            HandleMonDisplay();
            needsMonDisplay = false;
        }
    }

    public void StartRig()
    {
        SpineAnimator.Awaken();
        var sm = StateMachine.Make();
        SpineAnimator.SpineInstance.SetStateMachine(sm, SpineAnimator.Entity);
        var mainLayer = sm.CreateLayer("main");
        var idleState = mainLayer.CreateState("swim_under_water_idle", 0, true);
        mainLayer.InitialState = idleState;
    }

    public override void OnDestroy()
    {
        IFighter.fighters.Remove(this);
        if (currentMon?.Entity.Alive() == true)
        {
            currentMon.Entity.Destroy();
            currentMon = null;
        }
    }

    public bool needsMonDisplay;
    public void HandleMonDisplay()
    {
        if (currentMonData == null || currentMonData.itemDef.IsNullOrEmpty())
        {
            if (currentMon?.Entity.Alive() == true)
                currentMon.Entity.Destroy();
            currentMon = null;
            return;
        }

        try
        {
            if (currentMon == null)
            {
                var monEntity = Entity.Instantiate(Assets.GetAsset<Prefab>("Mon.prefab"));
                if (monEntity != null)
                {
                    currentMon = monEntity.GetComponent<FishMon>();
                }
                else
                {
                    Notifications.Show("Failed to create mon entity!");
                    return;
                }
            }

            if (Network.IsClient)
            {
                currentMonData.displayLevel = displayLevel;
            }

            currentMon.SetMon(this, currentMonData);
            currentMon.Entity.LocalScaleX = battleDirection.Value.X;
            currentMon.Entity.Position = MonPosition;
            currentMon.Entity.Position += Vector2.Up * 0.5f;
        }
        catch (Exception e)
        {
            Notifications.Show($"Error in HandleMonDisplay: {e.Message}");
        }
    }

    public void Attack(bool quickAttack)
    {
        if (Network.IsServer)
            return;
        currentMon?.Attack(quickAttack);
    }

    public void GetFishTeam(ref RuntimeMon[] mons)
    {
        var newMon = new RuntimeMon(FishId, Type, bossLevel, 0);
        if (battleId >= 0)
        {
            battleMons[battleId] = newMon;
        }
        mons[0] = newMon;
    }

    public void SendFishChoice(List<RuntimeMon> mons, int battleId)
    {
        onNextFrame.Add(() =>
        {
            // Boss always chooses its only mon
            FightSystem.ValidateFishChoice(battleId, Name, 0);
        });
    }

    public void GetSendableMonData(ref AO.StreamWriter writer)
    {
        if (!battleMons.ContainsKey(battleId))
        {
            writer.WriteString("");
            writer.WriteString("");
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        }
        else
        {
            var battleMon = battleMons[battleId];
            writer.WriteString(battleMon.itemDef);
            writer.WriteString(battleMon.type);
            writer.Write(battleMon.currentHealth);
            writer.Write(battleMon.exp);
            writer.Write(battleMon.level);
        }
    }

    public void SendMonData()
    {
        // Send to the correct player for this battle
        if (battleId >= 0 && battleId < FightSystem.instance.battles.Count)
        {
            var battle = FightSystem.instance.battles[battleId];
            if (battle.player1 is MyPlayer p1 && p1.Alive())
            {
                AO.StreamWriter writer = new AO.StreamWriter();
                GetSendableMonData(ref writer);
                CallClient_ReceiveMonData(writer.byteStream.ToArray(), rpcTarget: p1);
            }

        }
    }

    public void SetFromSendableMonData(ref AO.StreamReader reader)
    {
        currentMonData = RuntimeMon.FromCurrent(reader.ReadString(), reader.ReadString(), reader.Read<int>(), reader.Read<int>(), reader.Read<int>());
        needsMonDisplay = true;
    }

    [ClientRpc]
    public void ReceiveMonData(byte[] data)
    {
        AO.StreamReader reader = new AO.StreamReader(data);
        SetFromSendableMonData(ref reader);
    }

    public void SetFishData(int index, int level, int exp)
    {
        if (index == 0 && battleMons.TryGetValue(battleId, out RuntimeMon mon))
        {
            mon.level = level;
        }
    }

    public void HandleBattleEnd(bool won)
    {
        if (battleMons.ContainsKey(battleId))
        {
            currentMonData = null;
            battleMons.Remove(battleId);
        }

        if (battleId >= 0 && battleId < FightSystem.instance.battles.Count)
        {
            var battle = FightSystem.instance.battles[battleId];
            if (battle.player1 is MyPlayer p1 && p1.Alive())
            {
                ItemRarity? forcedRarity = GameManager.GetRarityFor(p1.Name);
                bool isMythic = forcedRarity.HasValue && forcedRarity.Value == ItemRarity.Mythic;
                if (!won && p1.currentRodID == "Rod_Rainbow" && (isMythic || Random.Shared.NextDouble() < 0.01f))
                {
                    var fish = FishItemManager.GetFish(team[0].itemDef);
                    if (fish != null)
                    {
                        p1.ProcessNewFish(fish, displayLevel);
                    }
                }
                CallClient_DestroyMon(rpcTarget: p1);
            }
        }
    }

    [ClientRpc]
    public void DestroyMon()
    {
        if (currentMon != null && currentMon.Entity.Alive())
        {
            currentMon.Entity.Destroy();
            currentMon = null;
            needsMonDisplay = false;
        }
    }

    public void BattleFail(string reason) { }

    public void SetPosition(Vector2 position)
    {
        MonPosition = position;
    }

    public void SetBattleContext(int battleId)
    {
        currentBattleId = battleId;
    }

    public FishMon GetLocalMon() => currentMon;

    public void SendTeamStatus(ref RuntimeMon[] mons)
    {
        //Boss doesn't have a team
    }
}