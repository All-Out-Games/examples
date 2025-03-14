using AO;
using System;
using System.Security.Cryptography.X509Certificates;

public enum BattleType { InWorld, StaticWorld, Arena }
public enum BattleState { Init, Choice, Fight, End, Wait }

public struct Battle
{
    public BattleType type;
    public BattleState state;
    public Vector2 position;
    public IFighter player1;
    public IFighter player2;
    public RuntimeMon[] player1Mons;
    public int player1MonIndex;
    public RuntimeMon[] player2Mons;
    public int player2MonIndex;
    public bool player1Turn;
    public float attackTimer;
    public bool displayedAttack;
    public Vector2 playerLastPosition;
}

public partial class FightSystem : Component
{
    [Serialized] public Entity BattleArena;
    public static FightSystem instance;
    public List<Battle> battles = new();

    public const float START_BATTLE_RANGE = 4.0f;
    public const float BATTLE_RADIUS = 3.0f;
    public const float ARENA_RADIUS = 3.8f;

    public const float BEFORE_ATTACK_DELAY = 0.2f;
    public const float ATTACK_ANIM_DELAY = 0.75f;
    public const float ATTACK_QUICK_DELAY = 0.5f;
    public const float BETWEEN_ATTACK_DELAY = 0.1f;

    public const int TEAM_SIZE = 5;


    public override void Awake()
    {
        instance = this;
    }

    public static void StartBattle(IFighter player1, IFighter player2, BattleType type)
    {
        if (Network.IsClient || !player1.Entity.Alive() || !player2.Entity.Alive())
            return;

        if (!player1.SupportMultiBattle && player1.inBattle.Value)
        {
            player2.BattleFail($"{player1.Name} is already in a battle!");
            return;
        }

        if (!player2.SupportMultiBattle && player2.inBattle.Value)
        {
            player1.BattleFail($"{player2.Name} is already in a battle!");
            return;
        }

        if (type == BattleType.InWorld && Vector2.Distance(player1.Entity.Position, player2.Entity.Position) > START_BATTLE_RANGE)
        {
            player1.BattleFail("One player walked away!");
            player2.BattleFail("One player walked away!");
            return;
        }

        Vector2 midPoint = (player1.Entity.Position + player2.Entity.Position) / 2.0f;
        Vector2 goalPosition = midPoint;
        var blackList = MyPlayer.players.Select(p => p.Entity).ToArray();
        var whiteList = new IFighter[] { player1, player2 };
        bool foundPosition = false;

        if (type == BattleType.Arena)
        {
            goalPosition = instance.BattleArena.Position;
            foundPosition = true;

            //IsBattlePositionValid(ref blackList, ref whiteList, ref goalPosition)
        }
        else if (type == BattleType.StaticWorld)
        {
            if (player1 is not MyPlayer)
                goalPosition = player1.Entity.Position + Vector2.Right * BATTLE_RADIUS;
            else
                goalPosition = player2.Entity.Position + Vector2.Left * BATTLE_RADIUS;
            foundPosition = true;
        }
        else
        {
            foundPosition = FindValidBattlePosition(blackList, whiteList, ref goalPosition);
        }

        if (!foundPosition)
        {
            player1.BattleFail("Not enough space to start battle!");
            player2.BattleFail("Not enough space to start battle!");
            return;
        }

        StartBattleFrom(goalPosition, player1, player2, type);

        player1.inBattle.Set(true);
        player2.inBattle.Set(true);

        player1.battleDirection = Vector2.Right;
        player2.battleDirection = Vector2.Left;
        if (player1 is MyPlayer p1 && p1.Alive())
        {
            CallClient_BattleReady(goalPosition, player2.Name, true, type, rpcTarget: p1);
            if (type == BattleType.Arena)
            {
                p1.inArenaBattle.Set(true);
            }
        }
        if (player2 is MyPlayer p2 && p2.Alive())
        {
            CallClient_BattleReady(goalPosition, player1.Name, false, type, rpcTarget: p2);
        }
    }

    [ClientRpc]
    public static void BattleReady(Vector2 position, string targetName, bool isPlayer1, BattleType type)
    {
        if (Network.IsClient)
        {
            IFighter target = IFighter.fighters.Find(p => p.Name == targetName);
            if (isPlayer1)
                StartBattleFrom(position, MyPlayer.localPlayer, target, type);

            else
                StartBattleFrom(position, target, MyPlayer.localPlayer, type);
        }
    }

    public static void StartBattleFrom(Vector2 position, IFighter player1, IFighter player2, BattleType type)
    {
        Vector2 originalPosition = player1.Entity.Position;
        Battle battle = new Battle
        {
            type = type,
            position = position,
            player1 = player1,
            player2 = player2,
            state = BattleState.Init,
            player1Mons = new RuntimeMon[TEAM_SIZE],
            player2Mons = new RuntimeMon[TEAM_SIZE],
            player1MonIndex = 0,
            player2MonIndex = 0,
            player1Turn = false,
            playerLastPosition = originalPosition
        };
        int battleId = -1;
        // Look for an available battle slot
        for (int i = 0; i < instance.battles.Count; i++)
        {
            if (instance.battles[i].state == BattleState.Wait)
            {
                battleId = i;
                break;
            }
        }

        if (battleId != -1)
        {
            player1.battleId = battleId;
            player2.battleId = battleId;
            instance.battles[battleId] = battle;
        }
        else
        {
            player1.battleId = instance.battles.Count;
            player2.battleId = instance.battles.Count;
            instance.battles.Add(battle);
        }

        if (type == BattleType.Arena)
        {
            float extra = 0.0f;
            if (player1 is MyPlayer p1 && p1.inBoat)
            {
                extra = 1.0f;
            }
            player1.SetPosition(instance.BattleArena.Position + Vector2.Left * (ARENA_RADIUS + extra));
            player2.SetPosition(instance.BattleArena.Position + Vector2.Right * ARENA_RADIUS);
        }

        if (Network.IsClient)
        {
            if (player1.IsLocal || player2.IsLocal)
            {
                MyPlayer.localPlayer.camera.SetZoom(0.95f);
                if (type == BattleType.Arena)
                {
                    var sand = instance.BattleArena.TryGetChildByName("Battle_Arena_Sand");
                    sand.LocalEnabled = !MyPlayer.localPlayer.inBoat;
                    MyPlayer.localPlayer.camera.SetTarget(instance.BattleArena, Vector2.Zero);
                    MyPlayer.localPlayer.camera.Reset();
                }
                else
                {
                    MyPlayer.localPlayer.camera.SetTarget(instance.Entity, battle.position + Vector2.Up);
                }
            }

            //Notifications.Show($"Starting battle {battleId}!");
        }
    }

    private static bool FindValidBattlePosition(Entity[] blackList, IFighter[] players, ref Vector2 goalPosition)
    {
        // First try the initial position
        if (IsBattlePositionValid(ref blackList, ref players, ref goalPosition))
            return true;

        // Search in an expanding rectangular pattern, prioritizing horizontal space
        float xStep = 2f; // Horizontal step size
        float yStep = 1f; // Vertical step size
        int maxIterations = 20; // Maximum search distance

        // Try positions in a rectangular spiral pattern
        for (int i = 1; i <= maxIterations; i++)
        {
            // Store original position to reset after each iteration
            Vector2 originalPos = goalPosition;

            // Try positions to the right
            for (float x = xStep; x <= i * xStep; x += xStep)
            {
                goalPosition = originalPos + new Vector2(x, 0);
                if (IsBattlePositionValid(ref blackList, ref players, ref goalPosition))
                    return true;
            }

            // Try positions to the left
            for (float x = -xStep; x >= -i * xStep; x -= xStep)
            {
                goalPosition = originalPos + new Vector2(x, 0);
                if (IsBattlePositionValid(ref blackList, ref players, ref goalPosition))
                    return true;
            }

            // Try positions above and below, but with smaller steps
            for (float y = yStep; y <= i * yStep; y += yStep)
            {
                // Try above
                goalPosition = originalPos + new Vector2(0, y);
                if (IsBattlePositionValid(ref blackList, ref players, ref goalPosition))
                    return true;

                // Try below
                goalPosition = originalPos + new Vector2(0, -y);
                if (IsBattlePositionValid(ref blackList, ref players, ref goalPosition))
                    return true;

                // Try diagonal positions
                for (float x = xStep; x <= i * xStep; x += xStep)
                {
                    // Upper right and left
                    goalPosition = originalPos + new Vector2(x, y);
                    if (IsBattlePositionValid(ref blackList, ref players, ref goalPosition))
                        return true;

                    goalPosition = originalPos + new Vector2(-x, y);
                    if (IsBattlePositionValid(ref blackList, ref players, ref goalPosition))
                        return true;

                    // Lower right and left
                    goalPosition = originalPos + new Vector2(x, -y);
                    if (IsBattlePositionValid(ref blackList, ref players, ref goalPosition))
                        return true;

                    goalPosition = originalPos + new Vector2(-x, -y);
                    if (IsBattlePositionValid(ref blackList, ref players, ref goalPosition))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsBattlePositionValid(ref Entity[] blackList, ref IFighter[] players, ref Vector2 goalPosition)
    {
        // Check distance from active battles
        foreach (var b in instance.battles)
        {
            if ((b.state != BattleState.Wait) && Vector2.Distance(b.position, goalPosition) < BATTLE_RADIUS * 2f)
                return false;
        }

        // Check if any players are already in battle near this position
        foreach (var player in IFighter.fighters)
        {
            if (player.inBattle && Vector2.Distance(player.Entity.Position, goalPosition) < BATTLE_RADIUS * 1.5f)
                return false;
        }

        // Check horizontal space first (most important for battle positioning)
        var leftBlocked = Physics.RaycastWithWhitelist(
            goalPosition,
            Vector2.Left,
            BATTLE_RADIUS,
            null,
            blackList,
            out _
        );

        var rightBlocked = Physics.RaycastWithWhitelist(
            goalPosition,
            Vector2.Right,
            BATTLE_RADIUS,
            null,
            blackList,
            out _
        );

        if (leftBlocked || rightBlocked)
            return false;

        // Check paths from players to their positions
        foreach (var player in players)
        {
            var playerPos = player.Entity.Position;
            var goalPlayerPos = GetPlayerPosition(goalPosition, player == players[0] ? 0 : 1);
            var dirToCenter = (goalPlayerPos - playerPos).Normalized;
            var distToCenter = Vector2.Distance(playerPos, goalPlayerPos);

            if (distToCenter < 0.1f)
                continue;

            if (Physics.RaycastWithWhitelist(
                playerPos,
                dirToCenter,
                distToCenter,
                null,
                blackList,
                out _
            ))
                return false;
        }

        return true;
    }

    public static Vector2 GetPlayerPosition(Vector2 position, int id)
    {
        return id == 0 ? position + Vector2.Left * BATTLE_RADIUS : position + Vector2.Right * BATTLE_RADIUS;
    }

    public static Vector2 GetPlayerDirection(int battleId, IFighter player)
    {
        if (battleId < 0 || battleId >= instance.battles.Count)
            return Vector2.Zero;

        Vector2 position = GetPlayerPosition(instance.battles[battleId].position, player == instance.battles[battleId].player1 ? 0 : 1);
        if (Vector2.Distance(position, player.Entity.Position) < 0.1f)
            return Vector2.Zero;
        return (position - player.Entity.Position).Normalized;
    }

    public static float GetPlayerSpeedMultiplier(int battleId, MyPlayer player)
    {
        if (battleId < 0 || battleId >= instance.battles.Count)
            return 0;
        Vector2 position = player == instance.battles[battleId].player1 ?
            instance.battles[battleId].position + Vector2.Left * BATTLE_RADIUS :
            instance.battles[battleId].position + Vector2.Right * BATTLE_RADIUS;
        return (position - player.Position).Length > 1 ? 1 : 0.1f;
    }

    public static float GetLookDirection(int battleId, MyPlayer player)
    {
        if (battleId < 0 || battleId >= instance.battles.Count)
            return 1;
        if (player.Alive() || !instance.battles[battleId].player1.Entity.Alive() || !instance.battles[battleId].player2.Entity.Alive())
            return 1;
        return player == instance.battles[battleId].player1 ? 1.0f : -1.0f;
    }

    public static void EndBattle(int battleId, MyPlayer player)
    {
        //Notifications.Show($"Battle {battleId} ended");
        if (battleId < 0 || battleId >= instance.battles.Count)
            return;

        IFighter player1 = instance.battles[battleId].player1;
        IFighter player2 = instance.battles[battleId].player2;

        bool player1Alive = player1 != null && player1.Entity.Alive();
        bool player2Alive = player2 != null && player2.Entity.Alive();

        if (player1Alive)
            player1.battleId = -1;
        if (player2Alive)
            player2.battleId = -1;

        Battle battle = instance.battles[battleId];

        if (Network.IsClient && ((player1Alive && player1.IsLocal) || (player2Alive && player2.IsLocal)))
        {
            MyPlayer.localPlayer.camera.SetZoom(PlayerCamera.DEFAULT_ZOOM);
            MyPlayer.localPlayer.camera.SetTarget(MyPlayer.localPlayer.Entity, Vector2.Zero);
            MyPlayer.localPlayer.camera.Reset();
        }

        battle.state = BattleState.Wait;
        instance.battles[battleId] = battle;
    }

    public static void ValidateFishChoice(int battleId, string playerName, int index)
    {
        Battle battle = instance.battles[battleId];

        if (battle.state != BattleState.Choice)
            return;

        if (battle.player1.Name == playerName)
        {
            // Convert filtered list index to actual array index
            int validMonsFound = 0;
            for (int i = 0; i < battle.player1Mons.Length; i++)
            {
                if (battle.player1Mons[i] != null && battle.player1Mons[i].currentHealth > 0)
                {
                    if (validMonsFound == index)
                    {
                        battle.player1MonIndex = i;
                        break;
                    }
                    validMonsFound++;
                }
            }
        }
        else if (battle.player2.Name == playerName)
        {
            // Convert filtered list index to actual array index
            int validMonsFound = 0;
            for (int i = 0; i < battle.player2Mons.Length; i++)
            {
                if (battle.player2Mons[i] != null && battle.player2Mons[i].currentHealth > 0)
                {
                    if (validMonsFound == index)
                    {
                        battle.player2MonIndex = i;
                        break;
                    }
                    validMonsFound++;
                }
            }
        }
        instance.battles[battleId] = battle;
    }

    public void SendDamage(Battle battle, IFighter Attacker, IFighter Receiver, bool quickAttack)
    {
        AO.StreamWriter writer = new AO.StreamWriter();
        writer.WriteString(Attacker.Name);
        writer.WriteString(Receiver.Name);
        writer.Write(quickAttack);
        Receiver.GetSendableMonData(ref writer);
        if (battle.type == BattleType.Arena)
        {
            if (Attacker is MyPlayer p1 && p1.Alive())
            {
                CallClient_ReceiveDamage(writer.byteStream.ToArray(), rpcTarget: p1);
            }
            else if (Receiver is MyPlayer p2 && p2.Alive())
            {
                CallClient_ReceiveDamage(writer.byteStream.ToArray(), rpcTarget: p2);
            }
        }
        else
        {
            CallClient_ReceiveDamage(writer.byteStream.ToArray());
        }
    }


    [ClientRpc]
    public void ReceiveDamage(byte[] data)
    {
        //This is display only, aka only for clients!
        if (!Network.IsClient)
            return;

        AO.StreamReader reader = new AO.StreamReader(data);

        //Attacking Player Data
        string attackerName = reader.ReadString();
        string receiverName = reader.ReadString();
        bool quickAttack = reader.Read<bool>();

        //Handle Battle
        IFighter Attacker = IFighter.fighters.Find(p => p.Name == attackerName);
        IFighter Receiver = IFighter.fighters.Find(p => p.Name == receiverName);
        if (Receiver.Entity.Alive())
        {
            Receiver.SetFromSendableMonData(ref reader);
        }
        if (Attacker.Entity.Alive())
        {
            var AttackerMon = Attacker.GetLocalMon();
            if (AttackerMon != null)
            {
                AttackerMon._cachedTargetMon = Receiver.GetLocalMon();
                Attacker.Attack(quickAttack);
            }
        }
    }

    public override void Update()
    {
        if (Network.IsServer)
        {
            for (int i = 0; i < battles.Count; i++)
            {
                if (battles[i].state == BattleState.Wait)
                    continue;

                var battle = battles[i];
                battle.player1.SetBattleContext(i);
                battle.player2.SetBattleContext(i);

                //Validate battle
                if (!battle.player1.Entity.Alive() || !battle.player2.Entity.Alive())
                {
                    battle.state = BattleState.End;
                    instance.battles[i] = battle;
                    break;
                }

                switch (battle.state)
                {
                    case BattleState.Init:
                        battle.attackTimer += Time.DeltaTime;
                        //Wait until they are both in position with a maximum wait delay
                        if (battle.attackTimer < 2 && (battle.player1.Velocity.Length > 0.1f || battle.player2.Velocity.Length > 0.1f))
                        {
                            break;
                        }
                        //Generate valid mons teams
                        battle.player1.GetFishTeam(ref battle.player1Mons);
                        battle.player2.GetFishTeam(ref battle.player2Mons);
                        battle.player1MonIndex = -1;
                        battle.player2MonIndex = -1;
                        battle.state = BattleState.Choice;
                        break;
                    case BattleState.Choice:
                        //if awaiting player choice, break
                        if (battle.player1MonIndex == 10 || battle.player2MonIndex == 10)
                        {
                            //Skip setting the battle to give RPC a chance to set the choice
                            continue;
                        }

                        battle.player1.SendTeamStatus(ref battle.player1Mons);
                        battle.player2.SendTeamStatus(ref battle.player2Mons);

                        //Generate valid mons teams
                        List<RuntimeMon> player1Mons = new List<RuntimeMon>();
                        List<RuntimeMon> player2Mons = new List<RuntimeMon>();
                        foreach (var mon in battle.player1Mons)
                        {
                            if (mon != null && mon.currentHealth > 0)
                            {
                                player1Mons.Add(mon);
                            }
                        }
                        foreach (var mon in battle.player2Mons)
                        {
                            if (mon != null && mon.currentHealth > 0)
                            {
                                player2Mons.Add(mon);
                            }
                        }

                        //Both either have no mons or all mons are dead, end battle
                        if (player1Mons.Count == 0 || player2Mons.Count == 0)
                        {
                            battle.state = BattleState.End;
                            instance.battles[i] = battle;
                            break;
                        }

                        //Send fish choice to player
                        bool needbreak = false;
                        if (battle.player1MonIndex == -1)
                        {
                            battle.player1MonIndex = 10;
                            battle.player1.SendFishChoice(player1Mons, i);
                            needbreak = true;
                        }

                        if (battle.player2MonIndex == -1)
                        {
                            battle.player2MonIndex = 10;
                            battle.player2.SendFishChoice(player2Mons, i);
                            needbreak = true;
                        }
                        if (needbreak)
                        {
                            instance.battles[i] = battle;
                            break;
                        }

                        // Only proceed if both players have made valid choices
                        if (battle.player1MonIndex >= 0 && battle.player1MonIndex < battle.player1Mons.Length &&
                            battle.player2MonIndex >= 0 && battle.player2MonIndex < battle.player2Mons.Length)
                        {
                            battle.player1.currentMonData = battle.player1Mons[battle.player1MonIndex];
                            battle.player2.currentMonData = battle.player2Mons[battle.player2MonIndex];
                            battle.player1.SendMonData();
                            battle.player2.SendMonData();

                            //continue the battle
                            battle.state = BattleState.Fight;
                            battle.attackTimer = -BEFORE_ATTACK_DELAY;
                            instance.battles[i] = battle;
                        }
                        break;
                    case BattleState.Fight:
                        battle.attackTimer += Time.DeltaTime;
                        if (battle.attackTimer < BETWEEN_ATTACK_DELAY)
                            break;

                        bool quickAttack = Random.Shared.NextFloat() < 0.5f;
                        battle.attackTimer = !battle.displayedAttack ? -((quickAttack ? ATTACK_QUICK_DELAY : ATTACK_ANIM_DELAY) - BETWEEN_ATTACK_DELAY) : 0;

                        //Process battle
                        if (!battle.displayedAttack)
                        {
                            if (battle.player1Turn)
                            {
                                battle.player2Mons[battle.player2MonIndex].currentHealth -= RuntimeMon.CalculateTypeBonus(battle.player1.currentMonData.attack, battle.player1.currentMonData.type, battle.player2.currentMonData.type);
                                battle.player2.currentMonData = battle.player2Mons[battle.player2MonIndex];
                                SendDamage(battle, battle.player1, battle.player2, quickAttack);
                            }
                            else
                            {
                                battle.player1Mons[battle.player1MonIndex].currentHealth -= RuntimeMon.CalculateTypeBonus(battle.player2.currentMonData.attack, battle.player2.currentMonData.type, battle.player1.currentMonData.type);
                                battle.player1.currentMonData = battle.player1Mons[battle.player1MonIndex];
                                SendDamage(battle, battle.player2, battle.player1, quickAttack);
                            }
                            battle.displayedAttack = true;
                            battles[i] = battle;
                            break;
                        }

                        battle.displayedAttack = false;
                        battle.player1Turn = !battle.player1Turn;

                        if (battle.player1Mons[battle.player1MonIndex].currentHealth <= 0 || battle.player2Mons[battle.player2MonIndex].currentHealth <= 0)
                        {
                            bool player1GotKill = battle.player2Mons[battle.player2MonIndex].currentHealth <= 0;
                            RuntimeMon winnerMon = player1GotKill ? battle.player1Mons[battle.player1MonIndex] : battle.player2Mons[battle.player2MonIndex];
                            RuntimeMon loserMon = player1GotKill ? battle.player2Mons[battle.player2MonIndex] : battle.player1Mons[battle.player1MonIndex];
                            if (battle.type == BattleType.Arena)
                            {
                                if (player1GotKill)
                                {
                                    for (int x = 0; x < battle.player1Mons.Length; x++)
                                    {
                                        if (battle.player1Mons[x] != null)
                                        {
                                            battle.player1Mons[x].AddExp((int)MathF.Round((float)loserMon.battleExp / TEAM_SIZE));
                                            battle.player1.SetFishData(x, battle.player1Mons[x].level, battle.player1Mons[x].exp);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                winnerMon.AddExp((int)MathF.Round(loserMon.battleExp));
                                loserMon.AddExp((int)MathF.Round(winnerMon.battleExp * 0.1f));
                            }
                            if (player1GotKill)
                            {
                                battle.player1.SetFishData(battle.player1MonIndex, winnerMon.level, winnerMon.exp);
                                battle.player2.SetFishData(battle.player2MonIndex, loserMon.level, loserMon.exp);
                                //Reset loser mon
                                battle.player2MonIndex = -1;
                                //Send winner mon
                                battle.player1.currentMonData = battle.player1Mons[battle.player1MonIndex] = winnerMon;
                                battle.player1.SendMonData();
                            }
                            else

                            {
                                battle.player1.SetFishData(battle.player1MonIndex, loserMon.level, loserMon.exp);
                                battle.player2.SetFishData(battle.player2MonIndex, winnerMon.level, winnerMon.exp);
                                //Reset loser mon
                                battle.player1MonIndex = -1;
                                //Send winner mon
                                battle.player2.currentMonData = battle.player2Mons[battle.player2MonIndex];
                                battle.player2.SendMonData();
                            }
                            battle.state = BattleState.Choice;
                        }
                        break;
                    case BattleState.End:
                        bool player1Won = battle.player2MonIndex == -1;
                        if (battle.player1.Entity.Alive())
                            battle.player1.HandleBattleEnd(player1Won);
                        if (battle.player2.Entity.Alive())
                            battle.player2.HandleBattleEnd(!player1Won);
                        battle.state = BattleState.Wait;
                        //Static world is used by Random Fishes & NPCs, neither needs to spam the chat
                        if (battle.type == BattleType.Arena || battle.type == BattleType.InWorld)
                        {
                            if (player1Won)
                            {
                                MyPlayer.SendMessageToAll($"{battle.player1.Name} has won the battle against {battle.player2.Name}!");
                            }
                            else
                            {
                                MyPlayer.SendMessageToAll($"{battle.player2.Name} has won the battle against {battle.player1.Name}!");
                            }
                        }
                        if (battle.type == BattleType.Arena && battle.player1.Entity.Alive())
                        {
                            //Only reset player 1 as only player 1 is an actual player (Player2 in Arena Battles is NPC)
                            battle.player1.SetPosition(battle.playerLastPosition);
                        }
                        break;
                }
                battles[i] = battle;
            }
        }
    }
}

