using Sandbox;

public sealed class Bullet : Component
{
    [Property] public float TimeDie { get; set; } = 2f;
    [Property] public float MoveSpeed { get; set; } = 1.2f;
    [Property] public Vector3 Direction { get; set; } = Vector3.Zero;

    public GameObject Weapon { get; set; }
    public GameObject Owner { get; set; }

    /// <summary>Должен быть полем: у <see cref="TimeUntil"/> обратный отсчёт через auto-property ломается.</summary>
    private TimeUntil _timeDieDelay;
    private bool _isPaused = false;

    private Vector3 _spawn;

    private void Prepare()
    {
        _timeDieDelay = TimeDie;
        _spawn = WorldPosition;
    }

    private void Move()
    {
        if (_isPaused) return;
        
        WorldPosition += Direction * MoveSpeed;
    }

    private void DieDelay()
    {
        if (_isPaused) return;
        if (!_timeDieDelay) return;
        DestroyGameObject();
    }

    private void Show()
    {
        Gizmo.Draw.Color = Color.Blue;
        Gizmo.Draw.LineThickness = 1f;
        Gizmo.Draw.Arrow(_spawn, WorldPosition, 6, 5);
    }

    protected override void OnStart()
    {
        Prepare();
    }

    protected override void OnFixedUpdate()
    {
        DieDelay();
        Move();
        //Show();
    }
}
