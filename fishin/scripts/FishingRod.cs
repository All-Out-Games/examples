using AO;

public class FishingRod : Component
{
    [Serialized] public Entity rod;
    [Serialized] public Entity lineAnchor;
    [Serialized] public Entity line;
    [Serialized] public Entity floater;
    [Serialized] public Spine_Animator rodSpine;

    public override void Awake()
    {
        rod = Entity.TryGetChildByName("Rod");
        lineAnchor = rod.TryGetChildByName("LineAnchor");
        floater = Entity.TryGetChildByName("Floater");
        line = Entity.TryGetChildByName("Line");
        if (rodSpine != null)
        {
            rodSpine.Awaken();
            var sm = StateMachine.Make();
            rodSpine.SpineInstance.SetStateMachine(sm, rodSpine.Entity);
            var mainLayer = sm.CreateLayer("main");
            var idleState = mainLayer.CreateState("011FISH/rod_FX_AL", 0, true);
            mainLayer.InitialState = idleState;
        }
    }
}