using Sandbox;

public interface IDoorHackable
{
	string DoorHackName { get; }
	Vector3 DoorHackWorldPosition { get; }
	float DoorHackInteractRange { get; }

	void RequestDoorHack();
	bool CanBeDoorHacked( Player hacker );
	void HostOnDoorHackSucceeded( Player hacker );
	void HostOnDoorHackFailed( Player hacker );
}
