using AO;

public partial class BoatAbility : Ability
{
  public new MyPlayer Player => (MyPlayer)base.Player;
  public override TargettingMode TargettingMode => TargettingMode.CircleAOE;
  public override Texture Icon => Assets.GetAsset<Texture>("Boats/boat_1.png");
  public override float Cooldown => 2;
  public override float MaxDistance => 4;
  public override bool CanUse() => !UIManager.IsUIActive();
  public override bool CanTarget(Player target) => !UIManager.IsUIActive();

  //ALl of the raycasts here can be switched to overlap checks once we support them :D
  public override bool OnTryActivate(List<Player> targetPlayers, Vector2 positionOrDirection, float magnitude)
  {
    if (!Network.IsServer && Player.IsLocal && UIManager.IsUIActive())
    {
      return false;
    }

    Vector2 finalPos = Player.Position + positionOrDirection.Normalized * magnitude;

    if (Player.inBoat)
    {
      //ouchie that's a lot of raycasts
      if (Water.IsWaterInRadius(8, finalPos, 2.1f))
      {
        if (Network.IsClient && Player.IsLocal)
        {
          Notifications.Show("You can't exit into water!");
        }
        return false;
      }
      if (!YesYesZone.IsInRadius(5, Player.Position, 2.6f))
      {
        if (Network.IsClient && Player.IsLocal)
        {
          Notifications.Show("You can't exit the boat. You are too far from shore!");
        }
        return false;
      }
      if(NoNoZone.IsInRadius(8, finalPos, 0.6f))
      {
        if (Network.IsClient && Player.IsLocal)
        {
          Notifications.Show("You can't exit the boat here!");
        }
        return false;
      }
      //Exit boat
      if (Network.IsServer)
      {
        CallClient_End(Player, finalPos);
      }
      return true;
    }

    //ouchie that's a lot of raycasts
    if (magnitude < 0.25f || !Water.IsWaterInRadius(5, finalPos, 2.0f) || NoNoZone.IsInRadius(5, finalPos, 1.3f) || YesYesZone.IsInRadius(5, finalPos, 1.1f))
    {
      if (Network.IsClient && Player.IsLocal)
      {
        Notifications.Show("You are too far from the water!");
      }
      return false;
    }

    if (Network.IsServer)
    {
      CallClient_Begin(Player);
      Player.Teleport(finalPos);
    }
    return true;
  }

  [ClientRpc]
  public static void Begin(MyPlayer player)
  {
    player.AddEffect<BoatEffect>(player);
  }

  [ClientRpc]
  public static void End(MyPlayer player, Vector2 position)
  {
    player.RemoveEffect<BoatEffect>(false);
    player.Teleport(position);
  }
}

public partial class BoatEffect : AEffect
{
  public new MyPlayer Player => (MyPlayer)base.Player;
  public override bool IsActiveEffect => false;

  public Entity Boat;
  public CameraControl Camera;
  public ulong audioID;

  public override void OnEffectStart(bool isDropIn)
  {
    Player.boatSpeed = (Store.BoatsProducts.IndexOf(Store.BoatsProducts.First(x => x.Id == Player.currentBoatID.Value)) + 1) * 5;
    if (Network.IsClient)
    {
      Boat = Entity.Instantiate(Assets.GetAsset<Prefab>("boat 1.prefab"));
      Boat.GetComponent<Sprite_Renderer>().Sprite = Assets.GetAsset<Texture>(Store.BoatsProducts.First(x => x.Id == Player.currentBoatID.Value).Icon);
      Boat.Position = Player.Entity.Position;
      Boat.SetParent(Player.Entity, true);
      Boat.Scale = new Vector2(2.0f * Player.Entity.LocalScaleX, 2.0f);
      if (!isDropIn) // NOTE: Only needed because we don't clear audio on redropin right now. Remove this at next protobump (>31).
      {
        if (Player.currentBoatID.Value == "Boat_Basic")
        {
          audioID = SFX.Play(Assets.GetAsset<AudioAsset>("audio/wooden_boat_move_loop.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Player.Entity.Position, Volume = 0.5f, Loop = true });
        }
        else
        {
          audioID = SFX.Play(Assets.GetAsset<AudioAsset>("audio/speed_boat_move_loop.wav"), new SFX.PlaySoundDesc() { Positional = true, Position = Player.Entity.Position, Volume = 0.5f, Loop = true });
        }
      }
    }
    if (!isDropIn)
    {
      Player.AddEffect<MyEmoteEffect>(Player, null, (e) =>
      {
        e.EmoteName = "force_idle";
        e.Freeze = false;
        e.AllowEmotes = true;
      });
    }
    //Player.AddInvisibilityReason(nameof(BoatEffect));
    Player.GetComponent<Circle_Collider>().Size = 2f;
    if (Player.IsLocal)
    {
      Player.camera.SetZoom(1.8f);
    }
  }

  public override void OnEffectUpdate()
  {
    if (Network.IsClient)
    {
      if (Boat != null && Boat.Alive())
      {
        Boat.LocalEnabled = Player.SpineAnimator.LocalEnabled;
      }

      if (audioID != 0)
      {
        SFX.UpdateSoundDesc(audioID, new SFX.PlaySoundDesc() { Positional = true, Position = Player.Entity.Position, Volume = 0.4f * (Player.Velocity.Length / Player.boatSpeed), Loop = true });
      }
    }
  }

  public override void OnEffectEnd(bool interrupt)
  {
    if (Network.IsClient)
    {
      if (audioID != 0)
      {
        SFX.Stop(audioID);
      }
      if (Boat != null && Boat.Alive())
      {
        Boat.SetParent(null, false);
        Boat.Destroy();
      }
    }

    Player.RemoveEffect<MyEmoteEffect>(false);

    Player.GetComponent<Circle_Collider>().Size = 0.5f;
    if (Player.IsLocal)
    {
      Player.camera.SetZoom(PlayerCamera.DEFAULT_ZOOM);
    }
  }
}