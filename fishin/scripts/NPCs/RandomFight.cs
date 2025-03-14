using System.Diagnostics;
using AO;

public partial class RandomFight : Component, IFighter
{
    public bool SupportMultiBattle => false;
    Entity IFighter.Entity => Entity;
    SyncVar<bool> IFighter.inBattle => _inBattle;
    Vector2 IFighter.battleDirection { get => new Vector2(battleDirection.Value, 0); set => battleDirection.Set(value.X); }

    [Serialized] public Spine_Animator SpineAnimator;
    [Serialized] public BattleType battleType;
    [Serialized] public float encounterRadius = 1.0f;

    SyncVar<bool> _inBattle = new SyncVar<bool>(false);
    public SyncVar<float> battleDirection = new SyncVar<float>(0);
    public SyncVar<bool> isVisible = new SyncVar<bool>(false);

    public Vector2 startPosition;
    public string Name => Entity.Name;
    public int battleId { get; set; } = -1;
    public bool IsLocal => false;
    public Vector2 Velocity => _velocity;
    private Vector2 _velocity;
    public RuntimeMon currentMonData { get; set; }
    public RuntimeMon monTeam { get; set; }
    private RuntimeMon[] team = new RuntimeMon[1];
    private static readonly string[] types = new[] { "water", "fire", "grass" };
    public FishMon currentMon;

    [Serialized] public int medianTeamLevel = 10;

    private bool[] aliveStatus;

    public void SetBattleContext(int battleId)
    {
        //Don't do anything as NPC can only be in one battle at a time
    }

    public List<Action> onNextFrame = new();

    public override void Awake()
    {
        startPosition = Entity.Position;
        IFighter.fighters.Add(this);

        StartRig();
    }

    public override void Update()
    {
        if (Network.IsServer)
        {
            CheckRandomAppearance();
        }

        if (Network.IsClient)
        {
            SpineAnimator.LocalEnabled = isVisible.Value;
        }

        foreach (var action in onNextFrame)
        {
            action();
        }
        onNextFrame.Clear();

        if (battleType == BattleType.InWorld)
        {
            if (battleId != -1)
                _velocity = FightSystem.GetPlayerDirection(battleId, this) * 5;

            Entity.Position += _velocity * Time.DeltaTime;
        }

        if (needsMonDisplay && Velocity.Length < 0.01f)
        {
            HandleMonDisplay();
            needsMonDisplay = false;
        }

        DrawWorldUI();
    }

    private float lastAppearanceCheck = 0;
    private void CheckRandomAppearance()
    {
        if (!isVisible.Value && Time.TimeSinceStartup - lastAppearanceCheck > 10.0f)
        {
            lastAppearanceCheck = Time.TimeSinceStartup;

            if (Random.Shared.NextDouble() < 0.05f) // 5% chance every 10 seconds
            {
                isVisible.Set(true);
            }
        }
    }

    public bool CheckForBattle(MyPlayer player, Vector2 fishingPosition, Item_Definition fishResult, bool allowbattleStart)
    {
        if (!isVisible.Value || _inBattle.Value || player.inBattle.Value || player.IsTeamEmpty())
            return false;

        if (Vector2.Distance(Entity.Position, fishingPosition) <= encounterRadius)
        {
            if (allowbattleStart && Network.IsServer)
            {
                GenerateMon(fishResult);
                bool NpcIsPlayer1 = player.Entity.Position.X > Entity.Position.X;
                if (NpcIsPlayer1)
                {
                    FightSystem.StartBattle(this, player, battleType);
                }
                else
                {
                    FightSystem.StartBattle(player, this, battleType);
                }
            }
            return true;
        }
        return false;
    }

    public void DrawWorldUI()
    {
        //Setup world UI
        using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
        using var _3 = UI.PUSH_SCALE_FACTOR(0.01f);

        if (!_inBattle || aliveStatus == null)
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

    public void StartRig()
    {
        SpineAnimator.Awaken();
        var sm = StateMachine.Make();
        SpineAnimator.SpineInstance.SetStateMachine(sm, SpineAnimator.Entity);
        var mainLayer = sm.CreateLayer("main");
        var idleState = mainLayer.CreateState("more_bubbling_water", 0, true);
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

    private void GenerateMon(Item_Definition fish)
    {
        int level = Random.Shared.Next(medianTeamLevel - 4, medianTeamLevel + 4);
        string type = types[Random.Shared.Next(types.Length)];
        monTeam = new RuntimeMon(fish.Id, type, level, 0);
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

            if (currentMon == null)
            {
                Notifications.Show("Failed to get FishMon component!");
                return;
            }

            currentMon.SetMon(this, currentMonData);
            currentMon.Entity.LocalScaleX = battleDirection.Value;
            // Position directly above the NPC
            currentMon.Entity.Position = Entity.Position + Vector2.Up * 0.1f;
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

        // Only send to the player we're battling against
        if (battleType == BattleType.Arena && battleId >= 0 && battleId < FightSystem.instance.battles.Count)
        {
            var battle = FightSystem.instance.battles[battleId];
            if (battle.player1 is MyPlayer p1 && p1.Alive())
            {
                CallClient_ReceiveMonData(writer.byteStream.ToArray(), rpcTarget: p1);
                return;
            }
        }
        CallClient_ReceiveMonData(writer.byteStream.ToArray());
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

    public void GetFishTeam(ref RuntimeMon[] mons)
    {
        var newMon = new RuntimeMon(monTeam.itemDef, monTeam.type, monTeam.level, 0);
        currentMonData = newMon;
        mons[0] = newMon;
    }

    public void SendFishChoice(List<RuntimeMon> mons, int battleId)
    {
        onNextFrame.Add(() =>
        {
            FightSystem.ValidateFishChoice(battleId, Name, 0);
        });
    }

    public void SetFishData(int index, int level, int exp)
    {
        if (index >= 0 && index < team.Length && team[index] != null)
        {
            team[index].level = level;
        }
    }

    public void HandleBattleEnd(bool won)
    {
        if (Network.IsServer)
        {
            isVisible.Set(false);
            if(!won)
            {
                //Get current battle
                var battle = FightSystem.instance.battles.Find(b => b.player1 == this || b.player2 == this);
                //get other player
                var otherPlayer = battle.player1 == this ? battle.player2 : battle.player1;
                if(otherPlayer is MyPlayer p1)
                {
                    var fish = FishItemManager.GetFish(monTeam.itemDef);
                    if(fish != null)
                    {
                        p1.ProcessNewFish(fish, monTeam.level / 2);
                    }
                }
            }
            // When defeated, make invisible again
            CallClient_DestroyMonAndResetPos();
        }
        _inBattle.Set(false);
        currentMonData = null;
    }

    [ClientRpc]
    public void DestroyMonAndResetPos()
    {
        if (currentMon?.Entity.Alive() == true)
        {
            currentMon.Entity.Destroy();
            currentMon = null;
        }
        Entity.Position = startPosition;
        SpineAnimator.LocalEnabled = false;
    }

    public void BattleFail(string reason) { }

    public void SetPosition(Vector2 position)
    {
        Entity.Position = position;
    }

    public FishMon GetLocalMon() => currentMon;

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
}

