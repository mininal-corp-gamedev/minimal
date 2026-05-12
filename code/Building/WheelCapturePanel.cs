using System;
using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Unused component - kept for reference only.
/// Scrolling is handled via polling Input.MouseWheel in PropsMenu.razor
/// </summary>
public sealed class WheelCapturePanel : Panel
{
	[Parameter] public Action<Vector2> OnWheel { get; set; }
	[Parameter] public RenderFragment ChildContent { get; set; }
}
