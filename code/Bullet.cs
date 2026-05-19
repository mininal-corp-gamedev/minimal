using Sandbox;
using System;
using System.Collections.Generic;

public sealed class Bullet : Component
{
    [Property] public float TimeDie { get; set; } = 2f;
    [Property] public float MoveSpeed { get; set; } = 1.2f;
    [Property] public Vector3 Direction { get; set; } = Vector3.Zero;
    [Property, Category("Collision")] public List<string> PassThroughTags { get; set; } = BulletCollisionRules.CreateDefaultPassThroughTags();
    [Property, Category("Collision")] public int MaxPassThroughHitsPerMove { get; set; } = 8;
    [Property, Category("Collision")] public float HitAdvanceDistance { get; set; } = 1f;

    public GameObject Weapon { get; set; }
    public GameObject Owner { get; set; }

    /// <summary>Должен быть полем: у <see cref="TimeUntil"/> обратный отсчёт через auto-property ломается.</summary>
    private TimeUntil _timeDieDelay;
    private bool _isPaused = false;

    private Vector3 _spawn;
    private readonly List<GameObject> _ignoredPassThroughObjects = new();

    private void Prepare()
    {
        _timeDieDelay = TimeDie;
        _spawn = WorldPosition;
    }

    private void Move()
    {
        if (_isPaused) return;
        if (Direction.LengthSquared < 0.0001f) return;

        var direction = Direction.Normal;
        var start = WorldPosition;
        var end = start + direction * MoveSpeed;

        MoveWithTrace(start, end, direction);
    }

    private void MoveWithTrace(Vector3 start, Vector3 end, Vector3 direction)
    {
        _ignoredPassThroughObjects.Clear();

        var traceStart = start;
        var maxHits = Math.Max(MaxPassThroughHitsPerMove, 0);
        var passThroughHits = 0;

        while (true)
        {
            var tr = TraceSegment(traceStart, end);
            if (!tr.Hit)
            {
                WorldPosition = end;
                return;
            }

            if (!BulletCollisionRules.TryGetPassThroughRoot(BulletCollisionRules.GetHitObject(tr), PassThroughTags, out var passThroughRoot))
            {
                WorldPosition = tr.HitPosition;
                DestroyGameObject();
                return;
            }

            if (passThroughHits >= maxHits)
            {
                WorldPosition = tr.HitPosition;
                DestroyGameObject();
                return;
            }

            AddIgnoredPassThrough(passThroughRoot);
            passThroughHits++;

            traceStart = tr.HitPosition + direction * MathF.Max(HitAdvanceDistance, 0.01f);
            if ((end - traceStart).LengthSquared <= 0.01f)
            {
                WorldPosition = end;
                return;
            }
        }
    }

    private SceneTraceResult TraceSegment(Vector3 start, Vector3 end)
    {
        var trace = Scene.Trace
            .Ray(start, end)
            .WithoutTags("bullet");

        if (Owner.IsValid())
            trace = trace.IgnoreGameObjectHierarchy(Owner);

        if (Weapon.IsValid())
            trace = trace.IgnoreGameObjectHierarchy(Weapon);

        foreach (var ignored in _ignoredPassThroughObjects)
        {
            if (ignored.IsValid())
                trace = trace.IgnoreGameObjectHierarchy(ignored);
        }

        return trace.Run();
    }

    private void AddIgnoredPassThrough(GameObject passThroughRoot)
    {
        if (!passThroughRoot.IsValid()) return;

        foreach (var ignored in _ignoredPassThroughObjects)
        {
            if (ignored == passThroughRoot)
                return;
        }

        _ignoredPassThroughObjects.Add(passThroughRoot);
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
