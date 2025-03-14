using AO;

public class YesYesZone : Component
{
    private static List<Entity> zones = new();


    public override void Awake()
    {
        zones.Add(Entity);
    }

    public static bool IsInRadius(int numRays, Vector2 origin, float radius)
    {
        // Cast rays in a circular pattern
        for (int i = 0; i < numRays; i++)
        {
            float angle = (i * 2 * MathF.PI) / numRays;
            Vector2 direction = new Vector2(
                MathF.Cos(angle) * radius,
                MathF.Sin(angle) * radius
            );
            
            if (Physics.RaycastWithWhitelist(origin, direction.Normalized, radius, zones.ToArray(), null, out _))
            {
                return true;
            }
        }  
        return false;
    }
    
}