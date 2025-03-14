using AO;
public class Boid
{
    public bool caught = false;
    public Fish target;
    public Vector2 pos;
    public Vector2 vel;

    public Boid(float x, float y, float xVel, float yVel)
    {
        pos.X = x;
        pos.Y = y;
        vel.X = xVel;
        vel.Y = yVel;
    }
    public void MoveForward(float delta, float minSpeed = 0, float maxSpeed = 5)
    {
        var speed = GetSpeed();
        if (speed > maxSpeed)
        {
            vel.X = (vel.X / speed) * maxSpeed;
            vel.Y = (vel.Y / speed) * maxSpeed;
        }
        else if (speed < minSpeed)
        {
            vel.X = (vel.X / speed) * minSpeed;
            vel.Y = (vel.Y / speed) * minSpeed;
        }

        pos.X += vel.X * delta;
        pos.Y += vel.Y * delta;
    }

    public Vector2 GetPosition(float time)
    {
        return new Vector2(pos.X + vel.X * time, pos.Y + vel.Y * time);
    }

    public void Accelerate(float scale = 1.0f)
    {
        vel.X *= scale;
        vel.Y *= scale;
    }

    private float _lastAngle = 0.0f;
    public float GetAngle()
    {
        if (vel.X == 0 && vel.Y == 0)
            return _lastAngle;
        float angle = (float)Math.Atan(vel.Y / vel.X) * 180 / (float)Math.PI - 90;
        if (vel.X < 0)
            angle += 180;
        _lastAngle = angle;
        return angle;
    }

    public float GetSpeed()
    {
        return (float)Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
    }

    public float GetDistance(Vector2 pos)
    {
        float dX = pos.X - pos.X;
        float dY = pos.Y - pos.Y;
        float dist = (float)Math.Sqrt(dX * dX + dY * dY);
        return dist;
    }

    public float GetDistance(Boid otherBoid)
    {
        float dX = otherBoid.pos.X - pos.X;
        float dY = otherBoid.pos.Y - pos.Y;
        float dist = (float)Math.Sqrt(dX * dX + dY * dY);
        return dist;
    }
}