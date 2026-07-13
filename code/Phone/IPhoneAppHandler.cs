namespace Minimal.PhoneSystem;

/// <summary>
/// Client-side behavior for a phone app. AppId must match the .phoneapp resource ID.
/// Authoritative gameplay changes must be requested through a server-validated RPC.
/// </summary>
public interface IPhoneAppHandler
{
	string AppId { get; }
	void OnOpened( PhoneAppContext context );
	void OnAction( PhoneAppContext context, string actionId );
}

public sealed class PhoneAppContext
{
	public PhoneAppDefinition Definition { get; init; }
	public Player Player { get; init; }
	public Connection Connection { get; init; }
}
