using AO;

public class FishMon : Component
{
    private Spine_Animator vfxAnimator;
    public enum EffectType { Mouth, Self, Target }
    private static readonly Dictionary<int, EffectType> EffectsType = new()
    {
        { 0, EffectType.Mouth },   // bubble
        { 1, EffectType.Target },  // geyser
        { 2, EffectType.Target },  // floor fire
        { 3, EffectType.Target },  // tornado thingy
        { 4, EffectType.Target },  // vines
        { 5, EffectType.Target },  // tree fall
        { 6, EffectType.Self },    // level up
    };

    private static readonly Dictionary<int, Vector2> EffectsOffset = new()
    {
        { 0, new Vector2(0, 0) },  // bubble
        { 1, new Vector2(0, 0) },  // geyser
        { 2, new Vector2(0, 0) },  // floor fire
        { 3, new Vector2(0, 0) },  // tornado thingy
        { 4, new Vector2(0, 0) },  // vines
        { 5, new Vector2(0.5f, 0) },  // tree fall
        { 6, new Vector2(0, 0) },  // level up
    };

    private Vector2 defaultVfxPosition => Entity.Position;

    public FishMon _cachedTargetMon;

    private bool ValidateCache()
    {
        if (!_cachedTargetMon.Alive())
        {
            _cachedTargetMon = null;
            return false;
        }
        return true;
    }

    private FishMon GetTargetMon()
    {
        //Auto find. Only works for the clients currently involved in a battle.
        if (!ValidateCache() && player.battleId >= 0 && player.battleId < FightSystem.instance.battles.Count)
        {
            var battle = FightSystem.instance.battles[player.battleId];
            var targetFighter = player == battle.player1 ? battle.player2 : battle.player1;
            _cachedTargetMon = targetFighter.GetLocalMon();
        }
        //Default behaviour will obtain cached fish from FightSystem.
        return _cachedTargetMon;
    }

    private Vector2 GetTargetPosition()
    {
        var targetMon = GetTargetMon();
        return targetMon?.Entity.Position ?? Entity.Position;
    }

    public Vector2 EffectPosition(int effectIndex)
    {
        return EffectsType[effectIndex] switch
        {
            EffectType.Mouth => animator.GetBonePosition("MOUTH_SPAWN"),
            EffectType.Self => Entity.Position,
            EffectType.Target => GetTargetPosition() + new Vector2(Entity.LocalScaleX * EffectsOffset[effectIndex].X, EffectsOffset[effectIndex].Y),
            _ => Entity.Position
        };
    }

    // Add Y position offsets for floor-based effects
    private static readonly Dictionary<int, float> EffectYOffsets = new()
    {
        { 0, 0f },     // bubble at mouth height
        { 1, -0.5f },  // geyser on floor
        { 2, -0.4f },  // floor fire on floor
        { 3, -0.5f },  // tornado on floor
        { 4, 0f },     // vines at mouth height
        { 5, 0f },     // tree fall on floor
        { 6, 0f },     // level up at center
    };

    private static readonly Dictionary<int, float> EffectXScale = new()
    {
        { 0, 1.0f },  // bubble at mouth height
        { 1, 1.0f },  // geyser on floor
        { 2, 1.0f },  // floor fire on floor
        { 3, 1.0f },  // tornado on floor
        { 4, 1.0f },  // vines at mouth height
        { 5, -1.0f }, // tree fall on floor
        { 6, 1.0f },  // level up at center
    };

    public struct MonDisplayData
    {
        public int health;
        public int exp;

        public static MonDisplayData Lerp(MonDisplayData a, MonDisplayData b, float t)
        {
            a.exp = (int)MathF.Round(a.exp + (b.exp - a.exp) * t);
            return a;
        }
    }

    private MonDisplayData currentData;
    private MonDisplayData targetData;
    private float dataLerpTime;
    private const float DATA_LERP_DURATION = 0.5f;

    // Damage effect variables
    private float damageShakeTime;
    private const float DAMAGE_SHAKE_DURATION = 0.3f;
    private const float DAMAGE_SHAKE_INTENSITY = 0.2f;
    private float damageTintTime;
    private const float DAMAGE_TINT_DURATION = 0.22f;
    private Vector2 preShakePosition;
    private Vector2 currentShakeOffset;

    public IFighter player;
    public string monId;
    public string type;
    public int level;
    public string name;
    public int maxHealth;
    public int maxExp;

    public Spine_Animator animator;

    public override void Awake()
    {
        vfxAnimator = Entity.Instantiate(Assets.GetAsset<Prefab>("VFX.prefab")).GetComponent<Spine_Animator>();

        // Initialize main animator
        animator = Entity.GetComponent<Spine_Animator>();
        animator.Awaken();
        var sm = StateMachine.Make();
        animator.SpineInstance.SetStateMachine(sm, animator.Entity);
        var mainLayer = sm.CreateLayer("main");
        var idleState = mainLayer.CreateState("idle", 0, true);
        var attackState = mainLayer.CreateState("attack", 0, false);
        var attackTrigger = sm.CreateVariable("attack", StateMachineVariableKind.TRIGGER);
        mainLayer.CreateGlobalTransition(attackState, true).CreateTriggerCondition(attackTrigger);
        mainLayer.CreateTransition(attackState, idleState, true);
        mainLayer.InitialState = idleState;

        // Initialize VFX animator
        vfxAnimator.Awaken();
        vfxAnimator.SpineInstance.Speed = 1.1f;  // Set VFX animation speed
        var vfxSm = StateMachine.Make();
        vfxAnimator.SpineInstance.SetStateMachine(vfxSm, vfxAnimator.Entity);
        var vfxLayer = vfxSm.CreateLayer("main");

        // Create states for all VFX animations
        var emptyState = vfxLayer.CreateState("__CLEAR_TRACK__", 0, true);
        var bubbleState = vfxLayer.CreateState("bubble_attack", 0, false);
        var geyserState = vfxLayer.CreateState("geyser", 0, false);
        var floorFireState = vfxLayer.CreateState("Floor Fire", 0, false);
        var tornadoSpawnState = vfxLayer.CreateState("tornado", 0, false);
        var vineState = vfxLayer.CreateState("vines", 0, false);
        var treeFallState = vfxLayer.CreateState("tree_fall", 0, false);
        var levelUpState = vfxLayer.CreateState("level_up", 0, false);

        // Create variables for state selection
        var vfxTrigger = vfxSm.CreateVariable("play_vfx", StateMachineVariableKind.TRIGGER);
        var currentVfx = vfxSm.CreateVariable("current_vfx", StateMachineVariableKind.INTEGER);

        // Create transitions from empty state based on currentVfx value
        var bubbleTransition = vfxLayer.CreateGlobalTransition(bubbleState, false);
        bubbleTransition.CreateTriggerCondition(vfxTrigger);
        bubbleTransition.CreateIntCondition(currentVfx, 0, StateMachineTransitionNumericConditionKind.EQUAL);

        var geyserTransition = vfxLayer.CreateGlobalTransition(geyserState, false);
        geyserTransition.CreateTriggerCondition(vfxTrigger);
        geyserTransition.CreateIntCondition(currentVfx, 1, StateMachineTransitionNumericConditionKind.EQUAL);

        var floorFireTransition = vfxLayer.CreateGlobalTransition(floorFireState, false);
        floorFireTransition.CreateTriggerCondition(vfxTrigger);
        floorFireTransition.CreateIntCondition(currentVfx, 2, StateMachineTransitionNumericConditionKind.EQUAL);

        var tornadoTransition = vfxLayer.CreateGlobalTransition(tornadoSpawnState, false);
        tornadoTransition.CreateTriggerCondition(vfxTrigger);
        tornadoTransition.CreateIntCondition(currentVfx, 3, StateMachineTransitionNumericConditionKind.EQUAL);

        var vineTransition = vfxLayer.CreateGlobalTransition(vineState, false);
        vineTransition.CreateTriggerCondition(vfxTrigger);
        vineTransition.CreateIntCondition(currentVfx, 4, StateMachineTransitionNumericConditionKind.EQUAL);

        var treeFallTransition = vfxLayer.CreateGlobalTransition(treeFallState, false);
        treeFallTransition.CreateTriggerCondition(vfxTrigger);
        treeFallTransition.CreateIntCondition(currentVfx, 5, StateMachineTransitionNumericConditionKind.EQUAL);

        // Add level up transition
        var levelUpTransition = vfxLayer.CreateGlobalTransition(levelUpState, false);
        levelUpTransition.CreateTriggerCondition(vfxTrigger);
        levelUpTransition.CreateIntCondition(currentVfx, 6, StateMachineTransitionNumericConditionKind.EQUAL);

        // Return to empty state after other animations
        vfxLayer.CreateTransition(geyserState, emptyState, true);
        vfxLayer.CreateTransition(tornadoSpawnState, emptyState, true);
        vfxLayer.CreateTransition(bubbleState, emptyState, true);
        vfxLayer.CreateTransition(floorFireState, emptyState, true);
        vfxLayer.CreateTransition(vineState, emptyState, true);
        vfxLayer.CreateTransition(treeFallState, emptyState, true);

        // Return to empty after level up
        vfxLayer.CreateTransition(levelUpState, emptyState, true);

        vfxLayer.InitialState = emptyState;

        animator.OnEvent += OnAnimationEvent;
    }


    public override void OnDestroy()
    {
        vfxAnimator.Entity.Destroy();
    }

    private void UpdateDisplayData()
    {
        dataLerpTime += Time.DeltaTime;
        float t = MathF.Min(1.0f, dataLerpTime / DATA_LERP_DURATION);
        t = Ease.OutQuart(t);
        currentData = MonDisplayData.Lerp(currentData, targetData, t);
    }

    public void SetMon(IFighter myPlayer, RuntimeMon mon)
    {
        bool probablySameMon = this.monId == mon.itemDef && this.type == mon.type;
        if (this.currentData.health <= 0 && mon.currentHealth > 0)
        {
            //Our current displayed fish is dead, this can't be the same fish!
            probablySameMon = false;
        }
        bool isLevelUp = probablySameMon && this.level != 0 && mon.displayLevel > this.level;

        this.player = myPlayer;
        this.type = mon.type;
        this.level = mon.displayLevel;
        this.maxHealth = mon.maxHealth;
        this.maxExp = mon.baseExp;

        // Update target data and reset lerp time
        targetData = new MonDisplayData
        {
            health = mon.currentHealth,
            exp = mon.exp,
        };

        // If this is a new mon, instantly set current data
        if (!probablySameMon)
        {
            currentData = targetData;
            dataLerpTime = DATA_LERP_DURATION;
        }

        this.monId = mon.itemDef;
        animator.SpineInstance.SetSkin(FishItemManager.GetFishSkin(monId));
        animator.SpineInstance.RefreshSkins();
        this.name = FishItemManager.GetFish(monId).Name;

        if (isLevelUp)
        {
            PlayLevelUpEffect();
        }
    }

    public void ApplyReceivedDamage()
    {
        SFX.Play(Assets.GetAsset<AudioAsset>("audio/fish_get_hit.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Entity.Position, Volume = 0.5f, Loop = false, SpeedPerturb = 0.5f });
        int nextHealth = targetData.health;
        int currentHealth = currentData.health;
        int damage = currentHealth - nextHealth;
        if (damage > 0)
        {
            TextEffectUI.SpawnWorldText(UI.Fonts.Barlow, Entity.Position, new Vector4(1f, 0f, 0f, 1f), damage.ToString());
            // Trigger damage effects
            damageShakeTime = DAMAGE_SHAKE_DURATION;
            damageTintTime = DAMAGE_TINT_DURATION;
            preShakePosition = Entity.Position;
            currentShakeOffset = Vector2.Zero;
        }
        currentData.health = nextHealth;
    }


    private void PlayLevelUpEffect()
    {
        SFX.Play(Assets.GetAsset<AudioAsset>("audio/level_up.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Entity.Position, Volume = 1.0f, Loop = false });
        // Reset position to center of fish
        vfxAnimator.Entity.Position = defaultVfxPosition;
        vfxAnimator.Entity.LocalScaleX = Entity.LocalScaleX * EffectXScale[6];

        vfxAnimator.SpineInstance.StateMachine.SetInt("current_vfx", 6);
        vfxAnimator.SpineInstance.StateMachine.SetTrigger("play_vfx");
    }

    private bool vfxQuickAttack = false;
    private float attackFxCooldown = -1;
    public void Attack(bool quickAttack)
    {
        animator.SpineInstance.StateMachine.SetTrigger("attack");
        vfxQuickAttack = quickAttack;
        attackFxCooldown = 0.2f;
    }

    public void OnAnimationEvent(string eventName)
    {
        if (eventName == "ATTACK")
        {
            //SFX.Play(Assets.GetAsset<AudioAsset>("audio/fish_attack.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Entity.Position, Volume = 1.0f, Loop = false });
            GetTargetMon()?.ApplyReceivedDamage();
        }
    }

    public void DisplayAttackFx()
    {
        if (!vfxQuickAttack)
        {
            int selectedVfx = 0;
            // Play a random VFX based on type
            switch (type.ToLower())
            {
                case "water":
                    selectedVfx = Random.Shared.NextFloat() > 0.5f ? 0 : 1;
                    if (selectedVfx == 0)
                    {
                        SFX.Play(Assets.GetAsset<AudioAsset>("audio/bubble_attack.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Entity.Position, Volume = 0.5f, Loop = false, SpeedPerturb = 0.5f });
                    }
                    else
                    {
                        SFX.Play(Assets.GetAsset<AudioAsset>("audio/geyser_attack.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Entity.Position, Volume = 0.5f, Loop = false, SpeedPerturb = 0.5f });
                    }
                    break;
                case "fire":
                    selectedVfx = Random.Shared.NextFloat() > 0.5f ? 2 : 3;
                    if (selectedVfx == 2)
                    {
                        SFX.Play(Assets.GetAsset<AudioAsset>("audio/fire_attack.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Entity.Position, Volume = 0.5f, Loop = false, SpeedPerturb = 0.5f });
                    }
                    else
                    {
                        SFX.Play(Assets.GetAsset<AudioAsset>("audio/fire_tornado_attack.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Entity.Position, Volume = 0.5f, Loop = false, SpeedPerturb = 0.5f });
                    }
                    break;
                case "grass":
                    selectedVfx = Random.Shared.NextFloat() > 0.5f ? 4 : 5;
                    if (selectedVfx == 4)
                    {
                        SFX.Play(Assets.GetAsset<AudioAsset>("audio/vine_attack.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Entity.Position, Volume = 0.5f, Loop = false, SpeedPerturb = 0.5f });
                    }
                    else
                    {
                        SFX.Play(Assets.GetAsset<AudioAsset>("audio/log_fall_attack.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Entity.Position, Volume = 0.5f, Loop = false, SpeedPerturb = 0.5f });
                    }
                    break;
            }
            vfxAnimator.Entity.LocalScaleX = Entity.LocalScaleX * EffectXScale[selectedVfx];
            Vector2 basePos = EffectPosition(selectedVfx);
            float yOffset = EffectYOffsets[selectedVfx];
            vfxAnimator.Entity.Position = basePos + new Vector2(0, yOffset);
            vfxAnimator.SpineInstance.StateMachine.SetInt("current_vfx", selectedVfx);
            vfxAnimator.SpineInstance.StateMachine.SetTrigger("play_vfx");
        }
    }

    private float StraightenTypeAngle(string type)
    {
        return type switch
        {
            "water" => 0f,
            "fire" => 15f,
            "grass" => 35f,
            _ => 0f
        };
    }

    private float TypeOffset(string type)
    {
        return type switch
        {
            "water" => 0f,
            "fire" => 0f,
            "grass" => -0.07f,
            _ => 0f
        };
    }

    private Vector4 GetTypeColor(string type)
    {
        return type switch
        {
            "water" => new Vector4(0.0f, 0.35f, 0.8f, 1.0f),  // Deeper blue
            "fire" => new Vector4(0.8f, 0.1f, 0.1f, 1.0f),    // Darker red
            "grass" => new Vector4(0.2f, 0.7f, 0.1f, 1.0f),   // Forest green
            _ => new Vector4(0.8f, 0.8f, 0.8f, 1.0f)          // Slightly darker white
        };
    }

    public override void Update()
    {
        if (attackFxCooldown > 0)
        {
            attackFxCooldown -= Time.DeltaTime;
            if (attackFxCooldown <= 0)
            {
                DisplayAttackFx();
                attackFxCooldown = -1;
            }
        }
        UpdateDisplayData();

        // Update damage effects
        if (damageShakeTime > 0)
        {
            damageShakeTime -= Time.DeltaTime;
            float shakeProgress = damageShakeTime / DAMAGE_SHAKE_DURATION;
            float shakeAmount = DAMAGE_SHAKE_INTENSITY * shakeProgress;

            // Generate new shake offset
            Vector2 targetShakeOffset = new Vector2(
                (float)(Random.Shared.NextDouble() * 2 - 1) * shakeAmount,
                (float)(Random.Shared.NextDouble() * 2 - 1) * shakeAmount
            );

            // Smoothly interpolate between current and target shake
            currentShakeOffset = Vector2.Lerp(currentShakeOffset, targetShakeOffset, Time.DeltaTime * 30);

            // If shake is ending, smoothly return to original position
            if (shakeProgress < 0.2f)
            {
                float returnProgress = 1 - (shakeProgress / 0.2f);
                currentShakeOffset *= 1 - returnProgress;
            }

            Entity.Position = preShakePosition + currentShakeOffset;
        }

        // Apply red tint
        if (damageTintTime > 0)
        {
            damageTintTime -= Time.DeltaTime;
            float tintProgress = damageTintTime / DAMAGE_TINT_DURATION;
            animator.SpineInstance.ColorMultiplier = Vector4.Lerp(
                new Vector4(1f, 1f, 1f, 1f),
                new Vector4(1f, 0.2f, 0.2f, 1f),
                Ease.OutQuad(tintProgress)
            );
        }
        else
        {
            animator.SpineInstance.ColorMultiplier = new Vector4(1f, 1f, 1f, 1f);
        }

        if (currentData.health <= 0)
        {
            animator.LocalEnabled = false;
            return;
        }
        else
        {
            animator.LocalEnabled = true;
        }

        //Setup world UI
        using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
        using var _4 = UI.PUSH_LAYER(10);

        // Health bar
        SliderUI.WorldSliderUI((float)currentData.exp / maxExp, Entity.Position + Vector2.Up * (0.9f - 0.2f) + Vector2.Left * 0.1f, SliderUI.SliderType.Slanted, new Vector2(1.3f, 0.2f));

        SliderUI.WorldSliderUI((float)currentData.health / maxHealth, Entity.Position + Vector2.Up * 0.9f, player == MyPlayer.localPlayer ? SliderUI.SliderType.Green : SliderUI.SliderType.Red, new Vector2(1.5f, 0.3f), true, $"{currentData.health}/{maxHealth}");

        using var _3 = UI.PUSH_SCALE_FACTOR(1f);

        float elementWidth = 1.7f;  // Width for text elements
        float iconSize = 0.7f;      // Size for the type icon


        Vector2 iconStartPos = Entity.Position + Vector2.Up * (0.9f + TypeOffset(type)) + Vector2.Left * 0.8f;

        //Icon Rect
        Rect iconRect = new Rect(
         new Vector2(iconStartPos.X - iconSize / 2, iconStartPos.Y - iconSize / 2),
         new Vector2(iconStartPos.X + iconSize / 2, iconStartPos.Y + iconSize / 2)
        );

        //Type Rect
        Rect typeRect = iconRect.CutLeft(iconSize).FitAspect(1);
        UI.Image(typeRect, Assets.GetAsset<Texture>($"ui/type_{type}.png"), new Vector4(1, 1, 1, 1), default, StraightenTypeAngle(type));
        //Display level in type
        typeRect = typeRect.Offset(0, -TypeOffset(type));
        typeRect = typeRect.Scale(0.3f);
        var typeSettings = UIUtils.CenteredText(true);
        typeSettings.OutlineColor = GetTypeColor(type);
        typeSettings.DropShadowColor = GetTypeColor(type);
        UI.Text(typeRect, $"{level}", typeSettings);

        //Name Rect
        Vector2 nameStartPos = Entity.Position + Vector2.Up * 1f;
        Rect nameRect = new Rect(
         new Vector2(nameStartPos.X - elementWidth / 2.5f, nameStartPos.Y + 0.05f),
         new Vector2(nameStartPos.X + elementWidth / 2, nameStartPos.Y + iconSize / 2)
        );
        var textSettings = UIUtils.WorldCenteredText(false);
        textSettings.OutlineThickness *= 1.3f;
        UI.Text(nameRect, $"{name}", textSettings);
    }
}