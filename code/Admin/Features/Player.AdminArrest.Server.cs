public sealed partial class Player
{
#if SERVER
	/// <summary>
	/// Host-only admin toggle for arrest state. Keeps arrest authority outside the client build.
	/// </summary>
	public bool HostAdminToggleArrest()
	{
		if ( !Networking.IsHost )
			return IsArrested;

		if ( IsArrested )
		{
			HostRelease();
			return false;
		}

		HostArrest();
		return true;
	}
#endif
}
