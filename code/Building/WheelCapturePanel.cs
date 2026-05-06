using System;
using Sandbox.UI;

namespace Sandbox;

public sealed class WheelCapturePanel : Panel
{
	[Parameter] public Action<Vector2> OnWheel { get; set; }

	public override void OnMouseWheel( Vector2 value )
	{
		OnWheel?.Invoke( value );
	}
}
