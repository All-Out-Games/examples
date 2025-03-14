using AO;

public class NpcTrigger : Component
{
    public enum ShopType { Fish, Rods, Boat }
    [Serialized] public Interactable Interactable;
    [Serialized] public Spine_Animator SpineAnimator;
    [Serialized] public ShopType shopType;
    [Serialized] public bool overrideSkin = false;
    [Serialized] public int colorIndex = 0;
    public override void Awake()
    {
        Interactable.Awaken();

        Interactable.CanUseCallback = (Player p) =>
        {
            return ((MyPlayer)p).battleId == -1 && !UIManager.IsUIActive();
        };

        Interactable.OnInteract = (Player p) =>
        {
            OpenUI((MyPlayer)p);
        };
        StartRig();
    }

    public void StartRig()
    {
        SpineAnimator.Awaken();
        if(overrideSkin)
        {
            //SpineAnimator.SpineInstance.SetSkeleton(Assets.GetAsset<SpineSkeletonAsset>("Animations/player.merged_spine_rig#output"));
            SpineAnimator.SetCrewchsia(colorIndex);
        }
        var sm = StateMachine.Make();
        SpineAnimator.SpineInstance.SetStateMachine(sm, SpineAnimator.Entity);
        var mainLayer = sm.CreateLayer("main");
        var idleState = mainLayer.CreateState("Idle", 0, true);
        mainLayer.InitialState = idleState;
    }

    public void OpenUI(MyPlayer p)
    {
        if (p.IsLocal)
        {
            //p.StoreEntity = Entity;
            switch (shopType)
            {
                case ShopType.Rods:
                    UIManager.OpenPositionalUI(() => Store.DrawShop(Store.Instance.rodShop), Position);
                    break;
                case ShopType.Fish:
                    Store.Instance.RefreshSell();
                    UIManager.OpenPositionalUI(() => Store.DrawShop(Store.Instance.fishShop), Position);
                    break;
                case ShopType.Boat:
                    UIManager.OpenPositionalUI(() => Store.DrawShop(Store.Instance.boatShop), Position);
                    break;
            }
        }
    }
}