using Sandbox;
using System;

public sealed class DeathCameraTarget : Component
{
    public Player Player { get; set; }
    public Connection Connection { get; set; }
    public DateTime Created { get; set; }

    protected override void OnEnabled()
    {
        Invoke(60f, GameObject.Destroy);
    }
}
