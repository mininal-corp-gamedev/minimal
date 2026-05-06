using System;

namespace Sandbox;

public static class UiModalManager
{
	public static event Action<string> ActiveModalChanged;

	public static string ActiveModal { get; private set; }
	public static int Version { get; private set; }
	public static bool HasActiveModal => !string.IsNullOrEmpty( ActiveModal );

	public static void Open( string modalId )
	{
		if ( string.IsNullOrWhiteSpace( modalId ) || ActiveModal == modalId )
		{
			UpdateMouseVisibility();
			return;
		}

		ActiveModal = modalId;
		Version++;
		ActiveModalChanged?.Invoke( ActiveModal );
		UpdateMouseVisibility();
	}

	public static void Close( string modalId )
	{
		if ( string.IsNullOrWhiteSpace( modalId ) || ActiveModal != modalId )
		{
			UpdateMouseVisibility();
			return;
		}

		ActiveModal = null;
		Version++;
		ActiveModalChanged?.Invoke( ActiveModal );
		UpdateMouseVisibility();
	}

	public static bool IsActive( string modalId )
	{
		return !string.IsNullOrWhiteSpace( modalId ) && ActiveModal == modalId;
	}

	public static void UpdateMouseVisibility()
	{
#pragma warning disable CS0612
		Mouse.Visible = HasActiveModal;
#pragma warning restore CS0612
	}
}
