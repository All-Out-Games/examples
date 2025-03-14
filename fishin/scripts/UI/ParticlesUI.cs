using AO;

public struct ParticleData
{
    public Vector2 Position;
    public Vector2 Velocity;
    public float Lifetime;
}

public struct Particle
{
    public Texture texture;
    public Func<ParticleData, ParticleData> OnUpdateVelocity;
    public ParticleData Data;
    public Vector4 Color;
    public float Size;

    public Particle(Texture texture, Func<ParticleData, ParticleData> onUpdateVelocity, Vector2 position, Vector2 velocity, Vector4 color, float size, float lifetime)
    {
        this.texture = texture;
        OnUpdateVelocity = onUpdateVelocity;
        Data = new ParticleData() { Position = position, Velocity = velocity, Lifetime = lifetime };
        Color = color;
        Size = size;
    }
}

public class ParticlesUI : Component
{
    private static ParticlesUI instance;
    private List<Particle> particles = new List<Particle>();
    private List<Particle> screenParticles = new List<Particle>();

    public IM.QuadData[] ParticleQuads;

    public override void Awake()
    {
        instance = this;
    }

    public static void SpawnParticle(Particle particle, int count, bool useRandomStartVelocity = false, float magnitude = 0.0f)
    {
        for (int i = 0; i < count; i++)
        {
            if (useRandomStartVelocity)
            {
                particle.Data.Velocity = new Vector2(Random.Shared.NextFloat(-magnitude, magnitude), Random.Shared.NextFloat(-magnitude, magnitude));
            }
            instance.particles.Add(particle);
        }
    }

    public static void SpawnScreenParticle(Particle particle, int count, bool useRandomStartVelocity = false, float magnitude = 0.0f)
    {
        for (int i = 0; i < count; i++)
        {
            if (useRandomStartVelocity)
            {
                particle.Data.Velocity = new Vector2(Random.Shared.NextFloat(-magnitude, magnitude), Random.Shared.NextFloat(-magnitude, magnitude));
            }
            instance.screenParticles.Add(particle);
        }
    }

    public override void Update()
    {
        int particlesCount = Math.Max(particles.Count, screenParticles.Count);
        if (ParticleQuads == null || ParticleQuads.Length < particlesCount)
        {
            ParticleQuads = new IM.QuadData[particlesCount];
        }
        UpdateWorldParticles();
        UpdateScreenParticles();
    }

    void UpdateWorldParticles()
    {
        if (particles.Count == 0) return;
        using var _1 = UI.PUSH_CONTEXT(UI.Context.WORLD);
        using var _2 = UI.PUSH_LAYER(5);
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var particle = particles[i];
            particle.Data.Position += particle.Data.Velocity * Time.DeltaTime;
            particle.Data.Lifetime -= Time.DeltaTime;

            if (particle.Data.Lifetime <= 0)
            {
                particles.RemoveAt(i);
            }
            else
            {
                ParticleQuads[i] = new IM.QuadData(particle.Data.Position - new Vector2(particle.Size, particle.Size), particle.Data.Position + new Vector2(particle.Size, particle.Size), particle.Color, particle.texture);
                particle.Data = particle.OnUpdateVelocity?.Invoke(particle.Data) ?? particle.Data;
                particles[i] = particle;
            }
        }
        if (particles.Count > 0)
        {
            IM.Quads(ParticleQuads, particles.Count);
        }
    }

    void UpdateScreenParticles()
    {
        if (screenParticles.Count == 0) return;
        using var _1 = UI.PUSH_CONTEXT(UI.Context.SCREEN);
        using var _2 = UI.PUSH_LAYER(5);
        using var _3 = UI.PUSH_SCALE_FACTOR(10.0f);
        for (int i = screenParticles.Count - 1; i >= 0; i--)
        {
            var particle = screenParticles[i];
            particle.Data.Position += particle.Data.Velocity * Time.DeltaTime;
            particle.Data.Lifetime -= Time.DeltaTime;

            if (particle.Data.Lifetime <= 0)
            {
                screenParticles.RemoveAt(i);
            }
            else
            {
                ParticleQuads[i] = new IM.QuadData(particle.Data.Position - new Vector2(particle.Size, particle.Size), particle.Data.Position + new Vector2(particle.Size, particle.Size), particle.Color, particle.texture);
                particle.Data = particle.OnUpdateVelocity?.Invoke(particle.Data) ?? particle.Data;
                screenParticles[i] = particle;
            }
        }
        if (screenParticles.Count > 0)
        {
            IM.Quads(ParticleQuads, screenParticles.Count);
        }
    }
}