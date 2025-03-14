using AO;

public partial class MinigameInteractable : Component
{
    [Serialized] public MinigameKind Kind;

    public override void Awake()
    {
        var interactable = Entity.GetComponent<Interactable>();
        interactable.CanUseCallback = (Player p) =>
        {
            if (p.IsAdmin == false) return false;
            switch (GameManager.Instance.State)
            {
                case GameState.WaitingForPlayers:
                {
                    return true;
                }
            }
            return false;
        };
        interactable.OnInteract = (Player p) =>
        {
            if (Network.IsServer)
            {
                GameManager.Instance.PlayerInteractedWithMinigameDoor((MyPlayer)p, Kind);
            }
        };
    }
}