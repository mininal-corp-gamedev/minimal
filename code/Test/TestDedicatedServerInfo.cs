using Sandbox;

public sealed class TestDedicatedServerInfo : Component
{
	private TimeUntil _f = 1f;

	protected override void OnFixedUpdate()
	{
		if (!_f) return;

		_f = 1.25f;

        StartDedic();
		StartClient();
		StartShared();
    }

	private void StartDedic()
	{
#if SERVER
		Log.Info("SERVERUS A");
#endif
    }

    private void StartClient()
	{
#if SERVER
#else
		Log.Info("CLIENT F");
	#endif
	}

	private void StartShared()
	{
        Log.Info("SHARED B");
    }
}
