using Sandbox;

/// <summary>
/// Уничтожает GameObject через заданное количество секунд после старта.
/// </summary>
public sealed class DestroyAfterSeconds : Component
{
    [Property] public float Seconds { get; set; } = 0.5f;

    private TimeUntil _timeUntilDestroy;

    protected override void OnStart()
    {
        _timeUntilDestroy = Seconds;
    }

    protected override void OnFixedUpdate()
    {
        if (!_timeUntilDestroy) return;
        DestroyGameObject();
    }
}
