using AO;

public class Fish : Component
{
    private Vector2 vel;

    private Vector2 _smoothVel;

    private float _lastAngle;
    private float fader = 0f;
    private Sprite_Renderer spriteRenderer = null;

    public float GetAngle()
    {
        if (_smoothVel.X == 0 && _smoothVel.Y == 0)
            return _lastAngle;
        float angle = (float)Math.Atan(_smoothVel.Y / _smoothVel.X) * 180 / (float)Math.PI - 90;
        if (_smoothVel.X < 0)
            angle += 180;
        _lastAngle = angle;
        return angle;
    }

    public Vector4 GetSyncData()
    {
        return new Vector4(Entity.Position.X, Entity.Position.Y, vel.X, vel.Y);
    }

    public void SetGoal(Vector2 goal, Vector2 vel, bool caught = false, bool justSpawned = false)
    {
        // NOTE(Phillip): later we should make these framerate-independent damps
        if (caught)
            Entity.Position = Vector2.Lerp(Entity.Position, goal, 0.04f);
        else 
            Entity.Position = Vector2.Lerp(Entity.Position, goal, 0.10f);
        if (justSpawned)
        {
            _smoothVel = vel;
            Entity.Position = goal;
        }
        this.vel = vel;
    }

    public override void Awake()
    {
        foreach (Entity child in Entity.Children)
        {
            Sprite_Renderer s = child.GetComponent<Sprite_Renderer>();
            if (s != null)
            {
                spriteRenderer = s;
                break;
            }
        }
    }
    public override void Update()
    {
        // Fade in fish over time
        fader += Time.DeltaTime * 2f;
        if (fader > 1)
        {
            fader = 1;
        }
        spriteRenderer.Tint = new Vector4(1, 1, 1, fader);

        _smoothVel = Vector2.Lerp(_smoothVel, vel, Time.DeltaTime * 4.0f);
        Entity.Rotation = GetAngle();
    }
}