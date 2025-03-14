using AO;

public partial class MyEmoteEffect : AEffect
{
    public new MyPlayer Player => (MyPlayer)base.Player;
    public override bool IsActiveEffect => false;
    public override bool BlockAbilityActivation => Freeze;
    public override bool FreezePlayer => Freeze;

    public string EmoteName;
    public bool Freeze = true;
    public bool AllowEmotes = false;

    public override void NetworkSerialize(AO.StreamWriter writer)
    {
        writer.WriteString(EmoteName);
        writer.Write(Freeze);
        writer.Write(AllowEmotes);
    }

    public override void NetworkDeserialize(AO.StreamReader reader)
    {
        EmoteName = reader.ReadString();
        Freeze = reader.Read<bool>();
        AllowEmotes = reader.Read<bool>();
    }

    public override void OnEffectStart(bool isDropIn) {}
    public override void OnEffectUpdate() {
        if (EmoteName.IsNullOrEmpty()) {
            Player.RemoveEffect<MyEmoteEffect>(true);
            return;
        }

        if (!AllowEmotes || !Player.HasEffect<EmoteEffect>())
        {
            Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger(EmoteName);
        }
    }
    public override void OnEffectEnd(bool interrupt) {
        if (!AllowEmotes || !Player.HasEffect<EmoteEffect>())
        {
            Player.SpineAnimator.SpineInstance.StateMachine.TryGetLayerByIndex(0).ResetToInitialState();
        }
     }
}