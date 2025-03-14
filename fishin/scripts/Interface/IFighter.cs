using AO;

public interface IFighter
{
    public bool SupportMultiBattle { get; }
    public static List<IFighter> fighters = new List<IFighter>();
    public Entity Entity { get; }
    public SyncVar<bool> inBattle { get; }
    public string Name { get; }
    public int battleId { get; set; }
    public bool IsLocal { get; }
    public Vector2 Velocity { get; }
    public RuntimeMon currentMonData { get; set; }
    public Vector2 battleDirection { get; set; }

    public void Attack(bool quickAttack);
    public void GetFishTeam(ref RuntimeMon[] mons);
    public void SendFishChoice(List<RuntimeMon> mons, int battleId);
    public void SendMonData();
    public void SetFishData(int index, int level, int exp);
    public void HandleBattleEnd(bool won);
    public void BattleFail(string reason);
    public void SetPosition(Vector2 position);
    public void SetBattleContext(int battleId);
    public FishMon GetLocalMon();
    public void SendTeamStatus(ref RuntimeMon[] mons);
    public void GetSendableMonData(ref AO.StreamWriter writer);
    public void SetFromSendableMonData(ref AO.StreamReader reader);
 }




