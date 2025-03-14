using AO;
using System;

public partial class FishingAbility : Ability
{
    public new MyPlayer Player => (MyPlayer)base.Player;
    public override TargettingMode TargettingMode => TargettingMode.PointAndClick;
    public override Texture Icon => Assets.GetAsset<Texture>("ui/fish.png");
    public override Type Effect => typeof(CastFishingRodEffect);
    public override Type TargettingEffect => typeof(AimingFishingRodEffect);
    public override float MaxDistance => 4f;
    public override float Cooldown => 3f;

    public override bool CanTarget(Player p)
    {
        return false;
    }

    public override bool CanUse()
    {
        return !UIManager.IsUIActive();
    }

    public override bool CanBeginTargeting()
    {
        return true;
    }

    public override bool OnTryActivate(List<Player> targetPlayers, Vector2 direction, float magnitude)
    {
        if (!Water.IsWater(Player.Position, direction * magnitude))
        {
            if (Player.IsLocal)
                Notifications.Show("You can't fish here!");
            return false;
        }
        return base.OnTryActivate(targetPlayers, direction, magnitude);
    }
}

public class AimingFishingRodEffect : AEffect
{
    public new MyPlayer Player => (MyPlayer)base.Player;
    public override bool IsActiveEffect => false;
    public override bool BlockAbilityActivation => true;
    public override List<Type> AbilityWhitelist { get; } = new List<Type>() { typeof(FishingAbility) };
    public Entity RodEntity;
    public Entity targetVis;

    public override void OnEffectStart(bool isDropIn)
    {
        var rodPrefab = Assets.GetAsset<Prefab>(Player.currentRodID + ".prefab");
        if (rodPrefab != null)
        {
            RodEntity = Entity.Instantiate(rodPrefab);
        }
        else
        {
            Log.Error("Rod prefab not found for currentRodID \"" + Player.currentRodID + "\"");
        }
        Player.SetMouseIKEnabled(true);
        if (Player.IsLocal)
        {
            if (Game.IsMobile)
            {
                targetVis = Entity.Create();
                targetVis.Scale = new Vector2(2, 2);
                var spriteRenderer = targetVis.AddComponent<Sprite_Renderer>();
                spriteRenderer.Sprite = Assets.GetAsset<Texture>("$AO/new/Aiming Indicator/line_finite/reticle.png");
            }
            Player.SetZoom(1.35f);
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
        Player.Entity.LocalScaleX = MathF.Sign(AbilityDirection.X);
        RodEntity?.Destroy();
        Player.SetMouseIKEnabled(false);
        if (Player.IsLocal)
        {
            if (Game.IsMobile)
                targetVis.Destroy();
            Player.ResetZoom();
        }
    }

    public override void OnEffectUpdate()
    {
        Player.Entity.LocalScaleX = MathF.Sign(AbilityDirection.X);
        
        if (Player.IsLocal && Game.IsMobile)
            targetVis.Position = Player.Position + AbilityDirection * AbilityMagnitude;

        if (RodEntity != null)
        {
            RodEntity.Position = Player.SpineAnimator.GetBonePosition("Hand_R");
            RodEntity.Rotation = Player.SpineAnimator.GetBoneRotation("Hand_R") * (Player.Entity.LocalScaleX < 0 ? -1 : 1) + (Player.Entity.LocalScaleX < 0 ? 180 : 0);
            RodEntity.LocalScaleY = Player.Entity.LocalScaleX < 0 ? -1 : 1;
        }
    }
}

public partial class CastFishingRodEffect : AEffect
{
    public new MyPlayer Player => (MyPlayer)base.Player;
    public Entity rodEntity;
    public FishingRod fishingRod;
    public override bool IsActiveEffect => false;
    public override bool BlockAbilityActivation => true;
    public override bool FreezePlayer => true;
    public override bool DisableMovementInput => true;

    public override float DefaultDuration => 4.0f;

    private Water water;
    private int targetIndex = -1;

    static Entity splash;

    private bool pullOutPlayed = false;

    public override void OnEffectStart(bool isDropIn)
    {
        if (Network.IsServer)
        {
            Player.hasFish.Set(false);
        }
        Player.Entity.LocalScaleX = MathF.Sign(AbilityDirection.X);

        water = Water.GetWater(Player.Position, AbilityDirection * AbilityMagnitude);
        if (water == null)
        {
            Player.RemoveEffect(this, true);
            return;
        }

        Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("cast_rod");

        rodEntity = Entity.Instantiate(Assets.GetAsset<Prefab>(Player.currentRodID + ".prefab"));
        fishingRod = rodEntity.GetComponent<FishingRod>();
        Player.SetMouseIKEnabled(true);
        Player.SetZoom(1.05f);

        if (Network.IsServer)
        {
            targetIndex = water.AddFishTarget(Player.Position + AbilityDirection * AbilityMagnitude);
        }

        if (!isDropIn)
        {
            if (Player.IsLocal)
            {
                //TODO: Find better sound? Maybe different sound for different rod? :)
                SFX.Play(Assets.GetAsset<AudioAsset>("audio/throw_in_line.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Player.Entity.Position, Volume = 0.5f });
            }
        }
    }

    public override void OnEffectEnd(bool interrupt)
    {
        if (!pullOutPlayed)
        {
            Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("pull_out");
        }

        Player.Entity.LocalScaleX = MathF.Sign(AbilityDirection.X);

        if (rodEntity != null)
        {
            rodEntity.Destroy();
            Player.SetMouseIKEnabled(false);
        }

        Vector2 endPos = Player.Position + AbilityDirection * AbilityMagnitude;

        if (Network.IsServer)
        {
            if (water != null)
            {
                if (targetIndex != -1)
                    water.RemoveFishTarget(targetIndex);

                Item_Definition fishResult = FishItemManager.Instance.GetRandomFish(Player, water, RodsManager.GetRod(Player.currentRodID), Player.playerBuffLuckActive ? 1 : 0);

                // Check for random fight encounters first
                bool startedBattle = false;
                foreach (var fighter in Scene.Components<RandomFight>())
                {
                    if (fighter.CheckForBattle(Player, endPos, fishResult, true))
                    {
                        startedBattle = true;
                        break;
                    }
                }

                // Only try to catch fish if we didn't start a battle
                if (!startedBattle)
                {
                    bool foundFish = water.KillFish(endPos, 0.25f);
                    if (foundFish && !interrupt)
                    {
                        Player.ProcessNewFish(fishResult);
                    }
                }
                water.FreeFish(endPos, 0.25f);
            }
        }

        if (Player.IsLocal)
        {
            Player.ResetZoom();
            if (interrupt)
                Notifications.Show("You can't fish here!");
        }

        if (Network.IsClient && !interrupt)
        {
            if (Player.hasFish)
            {
                if (splash == null)
                {
                    splash = Entity.Create();
                    var splashSpineAnimator = splash.AddComponent<Spine_Animator>();
                    splashSpineAnimator.SpineInstance.SetSkeleton(Assets.GetAsset<SpineSkeletonAsset>("Animations/splashspine/splash.spine"));
                    splashSpineAnimator.SpineInstance.Scale = new Vector2(0.5f, 0.5f);
                    splashSpineAnimator.SpineInstance.SetAnimation("splash", false);
                    splashSpineAnimator.SpineInstance.Speed = 0.6f;
                }
                else
                {
                    var splashSpineAnimator = splash.GetComponent<Spine_Animator>();
                    splashSpineAnimator.SpineInstance.SetAnimation("splash", false);
                    splashSpineAnimator.SpineInstance.Speed = 0.6f;
                }
                splash.Position = endPos;
                SFX.Play(Assets.GetAsset<AudioAsset>("audio/fish_complete.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = endPos, Volume = 0.45f });
            }
            else
            {
                SFX.Play(Assets.GetAsset<AudioAsset>("audio/missing_hook.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = endPos, Volume = 0.45f });
            }
        }
    }

    public static float Angle(Vector2 from, Vector2 to)
    {
        float theta = (float)Math.Atan2(from.Y - to.Y, from.X - to.X);
        return AOMath.ToDegrees(theta);
    }

    public override void OnEffectUpdate()
    {
        Player.Entity.LocalScaleX = MathF.Sign(AbilityDirection.X);
        if (Network.IsServer)
        {
            if (!Player.hasFish)
            {
                bool startedBattle = false;
                foreach (var fighter in Scene.Components<RandomFight>())
                {
                    if (fighter.CheckForBattle(Player, Player.Position + AbilityDirection * AbilityMagnitude, null, false))
                    {
                        startedBattle = true;
                        break;
                    }
                }
                if (startedBattle || water.HasFish(Player.Position + AbilityDirection * AbilityMagnitude, 0.25f))
                {
                    if (targetIndex != -1)
                        water.RemoveFishTarget(targetIndex);
                    Player.hasFish.Set(true);
                    targetIndex = -1;
                }
            }
        }

        if (Network.IsClient)
        {
            if (ElapsedTime < 0.5f)
            {
                rodEntity.Position = Player.SpineAnimator.GetBonePosition("Hand_R");
                rodEntity.Rotation = Player.SpineAnimator.GetBoneRotation("Hand_R") * (Player.Entity.LocalScaleX < 0 ? -1 : 1) + (Player.Entity.LocalScaleX < 0 ? 180 : 0);
                rodEntity.LocalScaleY = Player.Entity.LocalScaleX < 0 ? -1 : 1;
                return;
            }

            //Place Rod
            rodEntity.Rotation = 0;
            rodEntity.LocalScaleY = 1;
            fishingRod.rod.Position = Player.SpineAnimator.GetBonePosition("Hand_R");
            fishingRod.rod.Rotation = Player.SpineAnimator.GetBoneRotation("Hand_R") * (Player.Entity.LocalScaleX < 0 ? -1 : 1) + (Player.Entity.LocalScaleX < 0 ? 180 : 0);
            fishingRod.rod.LocalScaleY = Player.Entity.LocalScaleX < 0 ? -1 : 1;

            if (DurationRemaining < 0.5f && !pullOutPlayed)
            {
                Player.SpineAnimator.SpineInstance.StateMachine.SetTrigger("pull_out");
                pullOutPlayed = true;
            }

            Vector2 floaterPos = Player.Position + AbilityDirection * AbilityMagnitude;
            Vector2 rodTip = fishingRod.lineAnchor.Position;

            //Move Floater with water
            fishingRod.floater.Position = floaterPos;
            fishingRod.floater.Rotation = 180;

            floaterPos += new Vector2(0, 0.1f);

            //Place Line
            float angle = Angle(rodTip, floaterPos);
            fishingRod.line.Rotation = angle;
            fishingRod.line.Position = floaterPos + ((rodTip - floaterPos) / 2.0f);
            fishingRod.line.LocalScaleX = (floaterPos - rodTip).Length * 7.5f;
            fishingRod.line.LocalScaleY = 0.1f;
        }
    }
}

