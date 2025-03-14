using AO;

public class FrontmanMaskSkinEffect : MyEffect
{
    public override bool IsActiveEffect => false;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.SpineAnimator.SpineInstance.EnableSkin("009SG/squidgame_front_man");
        Player.SpineAnimator.SpineInstance.RefreshSkins();
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.SpineAnimator.SpineInstance.DisableSkin("009SG/squidgame_front_man");
        Player.SpineAnimator.SpineInstance.RefreshSkins();
    }

    public override void OnEffectUpdate()
    {
    }
}

public partial class FrontmanGunAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Line;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/revolver_icon.png");
    public override Type TargettingEffect => typeof(FrontmanAimingGunEffect);
    public override float MaxDistance => 10f;

    public override bool CanBeginTargeting()
    {
        if (Player.IsFrontman == false) return false;
        return true;
    }

    public override bool CanUse()
    {
        if (!base.CanUse()) return false;
        if (Player.IsFrontman == false) return false;
        return true;
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("shoot");
        var projectileEntity = Game.SpawnProjectile(Player, "FrontmanBullet.prefab", "frontman_bullet", Player.Position, positionOrDirection);
        var projectile = projectileEntity.GetComponent<FrontmanBullet>();
        projectile.OwningPlayer = Player;
        projectile.Sprite.Entity.LocalRotation = MathF.Atan2(positionOrDirection.Y, positionOrDirection.X) * (180.0f / MathF.PI);
        SFX.Play(Assets.GetAsset<AudioAsset>("sfx/revolver_shoot.wav"), new SFX.PlaySoundDesc(){Positional=true, Position=Player.Entity.Position});
        return true;
    }
}

public class FrontmanAimingGunEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override List<Type> AbilityWhitelist { get; } = new List<Type>(){typeof(FrontmanGunAbility)};
    // public Entity GunEntity;

    public override void OnEffectStart(bool isDropIn)
    {
        // GunEntity = Entity.Instantiate(Assets.GetAsset<Prefab>("PlayerEquip.prefab"));
        Player.SetMouseIKEnabled(true);
    }

    public override void OnEffectEnd(bool interrupt)
    {
        // GunEntity.Destroy();
        Player.SetMouseIKEnabled(false);
    }

    public override void OnEffectUpdate()
    {
        // GunEntity.Position = Player.SpineAnimator.GetBonePosition("Hand_R");
        // GunEntity.Rotation = Player.SpineAnimator.GetBoneRotation("Hand_R") * (Player.Entity.LocalScaleX < 0 ? -1 : 1) + (Player.Entity.LocalScaleX < 0 ? 180 : 0);
        // GunEntity.LocalScaleY = Player.Entity.LocalScaleX < 0 ? -1 : 1;
    }
}

public partial class FrontmanBullet : Component
{
    [Serialized] public Sprite_Renderer Sprite;
    [Serialized] public Circle_Collider Collider;
    [Serialized] public MyPlayer OwningPlayer;

    public bool AlreadyHitSomething;

    public float Lifetime;

    public override void Awake()
    {
        Collider.OnCollisionEnter += (other) =>
        {
            if (other.Alive() == false) return;

            if (AlreadyHitSomething)
            {
                return;
            }

            if (other.TryGetComponent<MyPlayer>(out var player))
            {
                if (player == OwningPlayer) return;
                if (player.IsDead) return;

                AlreadyHitSomething = true;
                if (Network.IsServer)
                {
                    CallClient_HitPlayer(player);
                }
                Entity.Destroy();
            }
        };
    }

    public override void Update()
    {
        Lifetime += Time.DeltaTime;
        if (Lifetime >= 1f)
        {
            Entity.Destroy();
        }
    }

    [ClientRpc]
    public static void HitPlayer(MyPlayer player)
    {
        player.AddEffect<DeathByFrontmanEffect>();
    }
}

public class DeathByFrontmanEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override bool FreezePlayer => true;

    public override void OnEffectStart(bool isDropIn)
    {
        Player.TryLocalSetCurrentTargettingAbility(null);
        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("death_sniped");
        SFX.Play(Assets.GetAsset<AudioAsset>("sfx/schleemer_death.wav"), new SFX.PlaySoundDesc() { Volume = 0.7f, Positional = true, Position = Entity.Position });
        if (Network.IsServer)
        {
            Player.ServerKillPlayer();
        }
        DurationRemaining = 1f;
    }

    public override void OnEffectUpdate()
    {
    }

    public override void OnEffectEnd(bool interrupt)
    {
        if (Network.IsServer)
        {
            if (Player.UseLivesForMinigame == false || Player.MinigameLivesLeft <= 0)
            {
                Player.ServerPutInSpectatorMode();
                Player.IsEliminated.Set(true);
            }
            else
            {
                if (GameManager.Instance.CurrentMinigame.Alive() && GameManager.Instance.CurrentMinigame.ControlsRespawning() == false)
                {
                    Player.ServerRespawn();
                }
                else
                {
                    Player.ServerPutInSpectatorMode();
                }
            }
        }
    }
}

public partial class FrontmanInvisAbility : MyAbility
{
    public override TargettingMode TargettingMode => TargettingMode.Self;
    public override Texture Icon => Assets.GetAsset<Texture>("AbilityIcons/invisibility_icon.png");

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
    {
        if (Network.IsServer)
        {
            CallClient_Activate(Player, Player.HasEffect<FrontmanInvisEffect>() == false);
        }
        return true;
    }

    [ClientRpc]
    public static void Activate(MyPlayer player, bool enable)
    {
        player.RemoveEffect<FrontmanInvisEffect>(true);
        if (enable)
        {
            player.AddEffect<FrontmanInvisEffect>();
        }
    }
}

public class FrontmanInvisEffect : MyEffect
{
    public override bool IsActiveEffect => true;
    public override List<Type> AbilityWhitelist { get; } = new() { typeof(FrontmanInvisAbility) };
    public override bool IsValidTarget => false;

    public override void OnEffectStart(bool isDropIn)
    {
        if (!isDropIn)
        {
            if (Player.IsLocal)
            {
                Player.SpineAnimator.SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, 0.5f);
            }
            else
            {
                Player.AddInvisibilityReason(nameof(FrontmanInvisEffect));
                Player.AddNameInvisibilityReason(nameof(FrontmanInvisEffect));
                Player.NameInvisCounter += 1;
            }
        }
    }

    public override void OnEffectUpdate() { }

    public override void OnEffectEnd(bool interrupt)
    {
        if (Player.IsLocal)
        {
            Player.SpineAnimator.SpineInstance.ColorMultiplier = new Vector4(1, 1, 1, 1);
        }
        else
        {
            Player.RemoveInvisibilityReason(nameof(FrontmanInvisEffect));
            Player.RemoveNameInvisibilityReason(nameof(FrontmanInvisEffect));
            Player.NameInvisCounter -= 1;
        }
    }
}