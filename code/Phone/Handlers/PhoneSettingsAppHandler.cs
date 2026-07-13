namespace Minimal.PhoneSystem;

public sealed class PhoneSettingsAppHandler : IPhoneAppHandler
{
	public string AppId => "settings";

	public void OnOpened( PhoneAppContext context )
	{
	}

	public void OnAction( PhoneAppContext context, string actionId )
	{
		if ( actionId != "reload_apps" )
			return;

		PhoneAppDatabase.Reload();
		Notification.Info( GameLocalization.Phrase( "notify.phone.apps_reloaded", "Application catalog reloaded." ), 2f );
	}
}
