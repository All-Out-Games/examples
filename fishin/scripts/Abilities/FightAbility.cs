using AO;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class FightAbility : Ability
{
    public new MyPlayer Player => (MyPlayer)base.Player;
    public override TargettingMode TargettingMode => TargettingMode.Nearest;
    public override Texture Icon => Assets.GetAsset<Texture>("ui/fight.png");
    public override Type Effect => typeof(StartBattleEffect);
    public override float MaxDistance => 4.0f;
    public override int MaxTargets => 1;
    public override float Cooldown => 7.5f;
    public override bool CanTarget(Player p)
    {
        MyPlayer mp = (MyPlayer)p;
        return mp.Alive() && !mp.inBattle && mp != Player;
    }

    public override bool CanUse()
    {
        return !UIManager.IsUIActive() && MyPlayer.players.Any(p => p.Alive() && p != Player && Vector2.Distance(Player.Position, p.Position) <= FightSystem.BATTLE_RADIUS);
    }
}

public partial class StartBattleEffect : AEffect
{
    public new MyPlayer Caster => (MyPlayer)base.Caster;
    public new MyPlayer Player => (MyPlayer)base.Player;
    public override bool IsActiveEffect => true;


    public override void OnEffectStart(bool isDropIn)
    {
        if(Caster.IsLocal)
        {
            Notifications.Show("Requesting battle from " + Player.Name + "...");
        }
        if (Network.IsServer)
        {
            Caster.AttemptBattle(Player.Name);
        }
        Player.RemoveEffect(this, true);
    }


    public override void OnEffectEnd(bool isDropIn)
    {
    }

    public override void OnEffectUpdate()
    {
    }
}