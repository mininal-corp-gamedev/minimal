using Sandbox;
using Sandbox.UI;

namespace Sandbox;

internal static class CrosshairRuntime
{
	public static void EnsureExists()
	{
		if ( Networking.IsHost && Connection.Local is null )
			return;

		var scene = Game.ActiveScene;
		if ( scene is null )
			return;

		foreach ( var crosshair in scene.GetAllComponents<Crosshair>() )
		{
			if ( crosshair.IsValid() )
				return;
		}

		var panelObject = scene.CreateObject();
		panelObject.Name = "CrosshairRuntime";
		panelObject.Components.Create<ScreenPanel>();
		panelObject.Components.Create<Crosshair>();
	}
}
