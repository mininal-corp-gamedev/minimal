using Sandbox;
using System;
using System.Linq;

public sealed class PlayerObserver : Component
{
    public Player Player { get; set; }

    private Angles _eyeAngles;
    private TimeSince _timeSinceStarted;
    private DeathCameraTarget _cachedCorpse;
    private float _currentDistance;

    protected override void OnEnabled()
    {
        _eyeAngles = Scene.Camera is not null ? Scene.Camera.WorldRotation.Angles() : default;
        _timeSinceStarted = 0f;
        _currentDistance = 32f;
        _cachedCorpse = FindCorpse();
    }

    protected override void OnUpdate()
    {
        if (!Player.IsValid() || !Player.IsDead)
            GameObject.Destroy();
    }

    protected override void OnPreRender()
    {
        if (!Player.IsValid() || !Player.IsDead)
            return;

        _cachedCorpse = _cachedCorpse.IsValid() ? _cachedCorpse : FindCorpse();
        if (!_cachedCorpse.IsValid())
            return;

        RotateAround(_cachedCorpse);
    }

    private DeathCameraTarget FindCorpse()
    {
        var owner = Player.IsValid() ? Player.GameObject.Network.Owner : null;

        return Scene.GetAllComponents<DeathCameraTarget>()
            .Where(x => x.Player == Player || (owner is not null && x.Connection == owner))
            .OrderByDescending(x => x.Created)
            .FirstOrDefault();
    }

    private void RotateAround(DeathCameraTarget target)
    {
        if (Scene.Camera is null)
            return;

        var center = target.WorldPosition + Vector3.Up * 48f;
        var renderer = target.Components.Get<SkinnedModelRenderer>();
        if (renderer.IsValid() && renderer.TryGetBoneTransform("pelvis", out var pelvis))
            center = pelvis.Position + Vector3.Up * 25f;

        var eye = _eyeAngles;
        eye += Input.AnalogLook;
        eye.pitch = Math.Clamp(eye.pitch, -90f, 90f);
        eye.roll = 0f;
        _eyeAngles = eye;

        _currentDistance = MathX.Lerp(_currentDistance, 150f, Time.Delta * 5f, true);

        var targetPos = center - _eyeAngles.Forward * _currentDistance;
        var tr = Scene.Trace.FromTo(center, targetPos)
            .Radius(1f)
            .WithoutTags("ragdoll", "effect")
            .Run();

        Scene.Camera.WorldPosition = tr.EndPosition;
        Scene.Camera.WorldRotation = _eyeAngles;
    }
}
