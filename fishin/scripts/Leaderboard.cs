using AO;

public class Leaderboard : Component
{
    private LeaderboardEntry[] topEntries;

    public const string LeaderboardId = "fishermon_level";

    public override void Awake()
    {
        Leaderboards.TrackLeaderboard(LeaderboardId);
        GameManager.OnIncrementScore += IncrementScore;
        GameManager.OnRequestLeaderboard += ChatTopEntries;
    }

    public static double GetLocalScore()
    {
        return Leaderboards.GetLocalPlayerEntry(LeaderboardId).Score;
    }

    public void IncrementScore(Player player, double amount)
    {
        Leaderboards.IncrementPlayerScore(LeaderboardId, player, amount);
    }

    public void ChatTopEntries(Player player)
    {
        topEntries = Leaderboards.GetTop(LeaderboardId, 10);
        Chat.SendMessage(player, "Top 10 entries for " + LeaderboardId + ":");
        for (int i = 0; i < topEntries.Length; i++)
        {
            Chat.SendMessage(player, topEntries[i].Username + ": " + (topEntries[i].Score+1));
        }
    }
}