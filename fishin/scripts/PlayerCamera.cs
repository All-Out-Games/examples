using AO;

public class PlayerCamera
{
    public const float DEFAULT_ZOOM = 1.2f;

    private CameraControl _camera;
    private Entity _target;
    private Vector2 _offset;
    private float _zoom;

    public float scale => _zoom / DEFAULT_ZOOM;

    public Vector2 position => _camera.Position;

    public void Init(Entity target, float zoom)
    {
        _camera = CameraControl.Create(10);
        SetZoom(zoom);
        SetTarget(target);
    }

    public void SetZoom(float newZoom)
    {
        _zoom = newZoom;
    }

    public void SetTarget(Entity newTarget)
    {
        _target = newTarget;
        _offset = Vector2.Zero;
    }

    public void SetTarget(Entity newTarget, Vector2 offset)
    {
        _target = newTarget;
        _offset = offset;
    }

    private bool _reset = false;
    public void Reset()
    {
        _reset = true;
    }

    public void SetOffset(Vector2 offset)
    {
        _offset = offset;
    }

    private ulong _rumbleId;
    public void Shake(float duration)
    {
        _camera.Shake(0.2f, duration);
        _rumbleId = SFX.Play(Assets.GetAsset<AudioAsset>("audio/rumble.wav"), new SFX.PlaySoundDesc() { Positional = false, Volume = 0.2f, Loop = false, Speed = 2.0f });
    }

    public void Update()
    {
        if (_reset)
        {
            _camera.Position = _target.Position + _offset;
            _camera.Zoom = _zoom;
            _reset = false;
        }
        _camera.Zoom = AOMath.Lerp(_camera.Zoom, _zoom, 2f * Time.DeltaTime);
        _camera.Position = Vector2.Lerp(_camera.Position, _target.Position + _offset, 4f * Time.DeltaTime);

        if (_rumbleId != 0)
        {
            SFX.UpdateSoundDesc(_rumbleId, new SFX.PlaySoundDesc() { Positional = false, Volume = 0.4f * (_camera.ShakeDurationLeft / _camera.ShakeDuration), Speed = 2.0f });
        }
    }

}