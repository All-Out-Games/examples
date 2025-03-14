using AO;
using System;

// TODO(Phillip): This is only separated out into a manager singleton because NetSync on statics duplicates all the data. Fix this!
public class FishManager : Component
{
    [NetSync] public Vector4[] fishPositions = new Vector4[0];
    [NetSync] public byte[] fishCaught = new byte[0];

    public const long fishSyncsPerSecond = 6;

    internal static bool hasNetSyncFlags { get; private set; } = true;
    // We indirect through a function call here so any member access type-resolve failure can get caught.
    // This won't be necessary after the next protocol update.
    internal static void NetSync_SetEnabled(Entity e, bool enabled)
    {
        if (enabled) { e.NetSyncFlags |=  Entity.NetSyncOptions.Enabled; }
        else         { e.NetSyncFlags &= ~Entity.NetSyncOptions.Enabled; }
    }

    public static void NetSyncUpdate(Entity e)
    {
        const long frameSyncInterval = 60 / fishSyncsPerSecond;
        const long moduloCheck1 = 0 / (60 % fishSyncsPerSecond == 0 ? 1 : 0); // if this errors, then fishSyncsPerSecond does not divide the framerate!
        const long moduloCheck2 = 0 % frameSyncInterval; // if this errors, then fishSyncsPerSecond is too high, turn it down!
        bool enableThisFrame = (Game.FrameNumber % frameSyncInterval) == 0;
        if (hasNetSyncFlags)
        {
            try
            {
                NetSync_SetEnabled(e, enableThisFrame);
            }
            catch (System.Exception)
            {
                hasNetSyncFlags = false;
            }
        }
    }

    public override void Awake()
    {
        NetSyncUpdate(Entity);
    }

    public override void Update()
    {
        NetSyncUpdate(Entity);
    }

}

public enum IslandType
{
    MainIsland,
    SkullIsland,
    MonkeyIsland,
    SusIsland,
    MiniIsland1,
    MiniIsland2,
    AmogusIsland,
    FootIsland,
    VolcanIsland,
    CoralIsland,
    CaveIsland,
    FinalDoor
}

public class Water : Component
{
    [Serialized] public IslandType islandType;
    //Water collision checks
    public static List<Entity> waterList = new List<Entity>();
    public Entity[] whitelist = new Entity[1];


    #region Fish Syncing
    private FishManager fishManager = null;
    //Position + vel (can't sync angle since need smoothing vel)
    public Vector4[] fishPositions => fishManager.fishPositions;
    //0 = not caught, 1 = caught, 2 = hidden
    public byte[] fishCaught => fishManager.fishCaught;
    [NetSync] public int indexOffset = 0;
    #endregion

    //Other
    [Serialized] private int maxFish = 3;
    //Server side
    private readonly List<Boid> boids = new List<Boid>();
    private readonly List<float> respawnTimers = new List<float>();
    //Client side
    private readonly List<Fish> fishes = new List<Fish>();
    public readonly List<Vector2> fishTargets = new List<Vector2>();

    private readonly List<Vector2> spawnPoints = new List<Vector2>();

    public override void Awake()
    {
        foreach (Entity e in Scene.Entities())
        {
            FishManager m = null;
            if (e.Alive() && e.TryGetComponent<FishManager>(out m))
            {
                fishManager = m;
            }
        }

        if (Network.IsServer)
        {
            // Calculate offset based on current array size
            indexOffset = fishPositions.Length;
            boids.AddRange(Enumerable.Repeat<Boid>(null, maxFish));
            respawnTimers.AddRange(Enumerable.Repeat<float>(0, maxFish));
            spawnPoints.Add(Entity.Position);
            foreach (Entity child in Entity.Children)
            {
                spawnPoints.Add(child.Position);
            }
        }
        Array.Resize(ref fishManager.fishCaught, fishPositions.Length + maxFish);
        Array.Resize(ref fishManager.fishPositions, fishPositions.Length + maxFish);
        waterList.Add(Entity);
        whitelist[0] = Entity;
    }

    public override void Update()
    {
        if (Network.IsServer)
        {
            UpdateFishes();
            for (int i = 0; i < boids.Count; i++)
            {
                int arrayIndex = i + indexOffset;
                if (boids[i] == null)
                    continue;
                fishPositions[arrayIndex] = boids[i].target.GetSyncData();
                fishCaught[arrayIndex] = boids[i].caught ? (byte)1 : (byte)0;
            }
            FishManager.NetSyncUpdate(Entity);
        }
        else
        {
            //Notifications.Show(fishPositions.Length.ToString());
            //Handle Client side fishes
            for (int i = 0; i < maxFish; i++)
            {
                int arrayIndex = i + indexOffset;
                if (arrayIndex >= fishPositions.Length || arrayIndex >= fishCaught.Length)
                {
                    //Something is wrong or hasn't been initialized yet
                    return;
                }
                bool shouldShow = fishCaught[arrayIndex] != 2;
                if (!shouldShow)
                {
                    if (fishes.Count > i && fishes[i] != null)
                    {
                        fishes[i].Entity.Destroy();
                        fishes[i] = null;
                    }
                }
                else
                {
                    Fish fish = null;
                    bool justSpawned = false;
                    //Add fish if necessary
                    if (i >= fishes.Count)
                    {
                        fish = Entity.Instantiate(Assets.GetAsset<Prefab>("fish_object.prefab")).GetComponent<Fish>();
                        fishes.Add(fish);
                        justSpawned = true;
                    }
                    else if (fishes[i] == null)
                    {
                        fish = Entity.Instantiate(Assets.GetAsset<Prefab>("fish_object.prefab")).GetComponent<Fish>();
                        fishes[i] = fish;
                        justSpawned = true;
                    }
                    else
                    {
                        fish = fishes[i];
                    }
                    fish.SetGoal(
                        new Vector2(fishPositions[arrayIndex].X, fishPositions[arrayIndex].Y),
                        new Vector2(fishPositions[arrayIndex].Z, fishPositions[arrayIndex].W),
                        fishCaught[arrayIndex] == 1,
                        justSpawned
                    );

                }
            }
        }
    }

    private static Vector2 GetRandomVector2Dir()
    {
        Random random = new Random();
        return new Vector2(random.NextFloat(-1.0f, 1.0f), random.NextFloat(-1.0f, 1.0f));
    }

    public int AddFishTarget(Vector2 position)
    {
        for (int i = 0; i < fishTargets.Count; i++)
        {
            if (fishTargets[i] == Vector2.Zero)
            {
                fishTargets[i] = position;
                return i;
            }
        }
        fishTargets.Add(position);
        return fishTargets.Count - 1;
    }

    public void RemoveFishTarget(int index)
    {
        fishTargets[index] = Vector2.Zero;
    }

    public void AddFish(int index)
    {
        Vector2 spawnPoint = spawnPoints[Random.Shared.Next(spawnPoints.Count)];
        Fish newFish = Entity.Instantiate(Assets.GetAsset<Prefab>("fish_object.prefab")).GetComponent<Fish>();
        Vector2 startVel = GetRandomVector2Dir();
        var newBoid = new Boid(spawnPoint.X, spawnPoint.Y, startVel.X, startVel.Y) { target = newFish };
        boids[index] = newBoid;
        newFish.SetGoal(boids[index].pos, boids[index].vel, false, true);
    }

    public void UpdateFishes()
    {
        bool addedFish = false;

        //Update fishes
        Advance(Time.DeltaTime);

        // move all boids forward in time
        for (int i = 0; i < boids.Count; i++)
        {
            //Add boids if necessary (max one per frame)
            if (boids[i] == null)
            {
                if (!addedFish)
                {
                    respawnTimers[i] -= Time.DeltaTime;
                    if (respawnTimers[i] <= 0)
                    {
                        AddFish(i);
                        respawnTimers[i] = Random.Shared.NextFloat(0.5f, 1.5f);
                        addedFish = true;
                    }
                    else continue;
                }
                else continue;
            }
            if (boids[i].caught)
            {
                boids[i].vel = Vector2.Lerp(boids[i].vel, GetRandomVector2Dir(), 0.04f);
                boids[i].target.SetGoal(boids[i].pos, boids[i].vel, true);
                continue;
            }
            boids[i].MoveForward(Time.DeltaTime, 0.8f, 1.0f);
            boids[i].target.SetGoal(boids[i].pos, boids[i].vel);
        }
    }

    public void Advance(float deltaTime)
    {
        for (int i = 0; i < boids.Count; i++)
        {
            if (boids[i] == null || boids[i].caught)
                continue;

            var boid = boids[i];

            Vector2 wallVel = AvoidWall(boid, 50f);
            Vector2 avoidVel = Avoid(boid, 1f, 15f);
            Vector2 followVel = Follow(boid, 1f, 15f);

            Vector2 totalVel = avoidVel + followVel + wallVel;

            boid.vel.X += totalVel.X * deltaTime;
            boid.vel.Y += totalVel.Y * deltaTime;

            if (Time.TimeSinceStartup % 1f < deltaTime)
            {
                Vector2 randomDir = GetRandomVector2Dir() * 0.5f;
                boid.vel.X += randomDir.X;
                boid.vel.Y += randomDir.Y;
            }

            float speed = MathF.Sqrt(boid.vel.X * boid.vel.X + boid.vel.Y * boid.vel.Y);
            if (speed > 5f)
            {
                float scale = 5f / speed;
                boid.vel.X *= scale;
                boid.vel.Y *= scale;
            }
        }
    }

    #region Different boid behaviors that we are currently not using
    // private Vector2 Flock(Boid boid, float distance, float power)
    // {
    //     var neighbors = boids.Where(x => x.GetDistance(boid) < distance);
    //     if (!neighbors.Any()) return Vector2.Zero;

    //     float meanX = neighbors.Sum(x => x.pos.X) / neighbors.Count();
    //     float meanY = neighbors.Sum(x => x.pos.Y) / neighbors.Count();
    //     float deltaCenterX = meanX - boid.pos.X;
    //     float deltaCenterY = meanY - boid.pos.Y;

    //     // Reduce flocking when too many neighbors are present
    //     int neighborCount = neighbors.Count();
    //     float crowdingFactor = MathF.Max(0.1f, 1f - (neighborCount / 3f));

    //     return new Vector2(deltaCenterX, deltaCenterY) * power * crowdingFactor;
    // }

    // private Vector2 Align(Boid boid, float distance, float power)
    // {
    //     var neighbors = boids.Where(x => x.GetDistance(boid) < distance);
    //     float meanXvel = neighbors.Sum(x => x.vel.X) / neighbors.Count();
    //     float meanYvel = neighbors.Sum(x => x.vel.Y) / neighbors.Count();
    //     float dXvel = meanXvel - boid.vel.X;
    //     float dYvel = meanYvel - boid.vel.Y;
    //     return new Vector2(dXvel * power, dYvel * power);
    // }
    #endregion

    private Vector2 Avoid(Boid boid, float distance, float power)
    {
        float sumClosenessX = 0.0f;
        float sumClosenessY = 0.0f;

        for (int i = 0; i < boids.Count; i++)
        {
            var neighbor = boids[i];
            if (neighbor == null || neighbor == boid)
                continue;

            float dist = boid.GetDistance(neighbor);
            if (dist >= distance)
                continue;

            float closeness = (distance - dist) * (distance - dist) / distance;
            Vector2 awayDir = new Vector2(
                boid.pos.X - neighbor.pos.X,
                boid.pos.Y - neighbor.pos.Y
            ).Normalized;

            sumClosenessX += awayDir.X * closeness;
            sumClosenessY += awayDir.Y * closeness;
        }

        return new Vector2(sumClosenessX, sumClosenessY) * power;
    }

    private Vector2 Follow(Boid boid, float distance, float power)
    {
        float dx = 0.0f;
        float dy = 0.0f;
        bool foundTarget = false;

        for (int i = 0; i < fishTargets.Count; i++)
        {
            var target = fishTargets[i];
            if (target == Vector2.Zero)
                continue;

            if (boid.GetDistance(target) < distance)
            {
                dx = target.X - boid.pos.X;
                dy = target.Y - boid.pos.Y;
                foundTarget = true;
                break;  // Only use the first valid target found
            }
        }

        if (!foundTarget)
            return Vector2.Zero;

        return new Vector2(dx, dy).Normalized * power;
    }

    public static Vector2 Reflect(Vector2 inDirection, Vector2 inNormal)
    {
        float num = -2f * Vector2.Dot(inNormal, inDirection);
        return new Vector2(num * inNormal.X + inDirection.X, num * inNormal.Y + inDirection.Y);
    }

    private Vector2 AvoidWall(Boid boid, float power)
    {
        Vector2 pos = new Vector2(boid.pos.X, boid.pos.Y);
        Vector2 vel = new Vector2(boid.vel.X, boid.vel.Y);
        vel = vel.Normalized * MathF.Max(vel.Length, 0.25f);

        float[] angles = { -45f, 45f, -90f, 90f };
        Vector2 totalAvoidance = Vector2.Zero;
        int wallsDetected = 0;

        foreach (float angle in angles)
        {
            Vector2 checkDir = RotateVector(vel, angle * (MathF.PI / 180f));
            if (Physics.RaycastWithWhitelist(pos, checkDir.Normalized, 0.25f, whitelist, null, out Physics.RaycastHit hit))
            {
                float distance = Vector2.Distance(pos, hit.point);
                float weight = 1f / (distance * distance);

                Vector2 awayFromWall = (pos - hit.point).Normalized;
                totalAvoidance += awayFromWall * weight;
                wallsDetected++;
            }
        }

        if (wallsDetected > 0)
        {
            totalAvoidance /= wallsDetected;
            totalAvoidance = totalAvoidance.Normalized;

            if (Vector2.Dot(vel, totalAvoidance) < 0)
            {
                return totalAvoidance * power * 1.5f;
            }
            return totalAvoidance * power;
        }

        return Vector2.Zero;
    }

    private static Vector2 RotateVector(Vector2 vector, float angle)
    {
        float cos = MathF.Cos(angle);
        float sin = MathF.Sin(angle);
        return new Vector2(
            vector.X * cos - vector.Y * sin,
            vector.X * sin + vector.Y * cos
        );
    }

    public static bool IsWater(Vector2 origin, Vector2 direction)
    {
        return Physics.RaycastWithWhitelist(origin, direction.Normalized, direction.Length, waterList.ToArray(), null, out _);
    }

    public static bool IsWaterInRadius(int numRays, Vector2 origin, float radius)
    {
        // Cast rays in a circular pattern
        for (int i = 0; i < numRays; i++)
        {
            float angle = (i * 2 * MathF.PI) / numRays;
            Vector2 direction = new Vector2(
                MathF.Cos(angle) * radius,
                MathF.Sin(angle) * radius
            );

            if (Physics.RaycastWithWhitelist(origin, direction.Normalized, radius, waterList.ToArray(), null, out _))
            {
                return true;
            }
        }

        return false;
    }

    public static Water GetWater(Vector2 origin, Vector2 direction)
    {
        if (Physics.RaycastWithWhitelist(origin, direction.Normalized, direction.Length, waterList.ToArray(), null, out Physics.RaycastHit hit))
        {
            return hit.Collider.GetComponent<Water>();
        }
        return null;
    }

    public bool HasFish(Vector2 position, float radius)
    {
        for (int i = 0; i < boids.Count; i++)
        {
            var boid = boids[i];
            if (boid != null && !boid.caught && Vector2.Distance(position, boid.target.Position) < radius)
            {
                boid.caught = true;
                //Reuse pos for floater pos
                boid.pos = position;
                return true;
            }
        }
        return false;
    }

    public bool KillFish(Vector2 position, float radius)
    {
        for (int i = 0; i < boids.Count; i++)
        {
            var boid = boids[i];
            if (boid != null && boid.caught && Vector2.Distance(position, boid.target.Position) < radius)
            {
                boid.target.Entity.Destroy();
                boids[i] = null;
                respawnTimers[i] = Random.Shared.NextFloat(1.0f, 3.0f);
                fishCaught[i + indexOffset] = 2;
                return true;
            }
        }
        return false;
    }

    public void FreeFish(Vector2 position, float radius)
    {
        float closestDistance = float.MaxValue;
        Boid closestBoid = null;

        for (int i = 0; i < boids.Count; i++)
        {
            var boid = boids[i];
            if (boid != null && boid.caught)
            {
                float distance = Vector2.Distance(position, boid.target.Position);
                if (distance < radius && distance < closestDistance)
                {
                    closestDistance = distance;
                    closestBoid = boid;
                }
            }
        }

        if (closestBoid != null)
        {
            closestBoid.caught = false;
        }
    }
}