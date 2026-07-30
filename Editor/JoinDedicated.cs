using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Editor;
using Editor.Preferences;
using Sandbox;
public static class NrpEditorMenu
{
    [Menu( "Editor", "Nrp/Join Dedicated" )]
    public static void OpenMyMenu() => _ = JoinDedicated( string.Empty );

	[Menu( "Editor", "Nrp/Fix Scene Play" )]
	public static void FixScenePlay()
	{
		var sess = FindPlayableSession();
		if ( sess is null )
		{
			Log.Warning( "No playable session found." );
			return;
		}

		var scene = IGameInstance.Current.Scene;
		if ( scene is null || !scene.IsValid() )
		{
			Log.Warning( "Aucune scene active disponible." );
			return;
		}

		Log.Info( "game play state = " + Game.IsPlaying.ToString() );
		sess.SetPlaying( scene );
		EditorEvent.Run( "scene.play" );
	}

    public static async Task JoinDedicated( string steamId )
    {
        var sess = FindPlayableSession();
        if ( sess is null )
        {
            Log.Warning( "No playable session found." );
            return;
        }

        var scene = sess.Scene;
        if ( scene is null || !scene.IsValid() )
        {
            Log.Warning( "Aucune scene active disponible." );
            return;
        }

        if ( string.IsNullOrEmpty( steamId ) )
        {
            EditorScene.Play( sess );
            return;
        }

        if ( !ulong.TryParse( steamId, out var id ) )
        {
            Log.Warning( $"Invalid Steam ID: {steamId}" );
            return;
        }

        // Connect to the dedicated server FIRST so Networking.IsActive = true.
        // When sess.SetPlaying fires GameManager, it will call CreateLobby which
        // returns immediately because IsActive is already true — no local host created.
        Log.Info( $"Connecting to dedicated server {steamId}..." );
        var connected = await Networking.TryConnectSteamId( id );

        if ( !connected )
        {
            Log.Warning( $"Failed to connect to {steamId}" );
            return;
        }

        Log.Info( "Connected! Waiting for snapshot..." );
        LoadingScreen.IsVisible = false;

        // Wait for the server snapshot to be fully received before entering game view
        var timeout = 0;
        while ( Networking.IsConnecting && timeout < 300 )
        {
            await Task.Delay( 100 );
            timeout++;
        }

        Log.Info( "Snapshot received. Entering play mode..." );
        FixScenePlay();
    }

    public static void SpawnNewInstance()
    {
        using var p = new Process();

        p.StartInfo.FileName = "sbox.exe";
        p.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError = true;
        p.StartInfo.UseShellExecute = false;

        p.StartInfo.ArgumentList.Add( "-joinlocal" );

        // Count existing instances and assign the next possible instance id
        // +1 accounts for the editor instance, which runs under a different executable
        int instanceCount = Process.GetProcessesByName( "sbox" ).Length + 1;
        p.StartInfo.ArgumentList.Add( "+instanceid" );
        p.StartInfo.ArgumentList.Add( (instanceCount + 2).ToString() );
		Log.Info( "SpawnNewInstance: " + (instanceCount + 2).ToString() );

        if ( EditorPreferences.WindowedLocalInstances )
        {
            p.StartInfo.ArgumentList.Add( "-sw" );
            p.StartInfo.ArgumentList.Add( "-720" );
        }

        AddUserCommandLineArgs( p.StartInfo, EditorPreferences.NewInstanceCommandLineArgs );

        p.Start();
    }

    static void AddUserCommandLineArgs( ProcessStartInfo startInfo, string argumentString )
    {
        if ( string.IsNullOrWhiteSpace( argumentString ) )
            return;

        var args = argumentString.Split( ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

        foreach ( var arg in args )
        {
            startInfo.ArgumentList.Add( arg );
        }
    }

    static SceneEditorSession FindPlayableSession()
    {
        if ( SceneEditorSession.Active?.Scene is not PrefabScene )
            return SceneEditorSession.Active;

        var sessions = SceneEditorSession.All.Where( x => x.Scene is not PrefabScene ).ToList();
        var idx = sessions.IndexOf( SceneEditorSession.Active );

        if ( idx < 0 )
            return null;

        return sessions.Take( idx ).LastOrDefault() ?? sessions.Skip( idx + 1 ).FirstOrDefault();
    }
}

/// <summary>
/// Injects a "Join Dedicated" button into the scene viewport toolbar.
/// </summary>
public static class JoinDedicatedToolbar
{
	private static Widget _button;
	private static Widget _spawnButton;
	public static string LastSteamId = string.Empty;

	[EditorEvent.Frame]
	public static void Frame()
	{
		if ( !IsButtonInToolbar() )
			TryInjectIntoViewportToolbar();
	}

	[Event( "scene.play" )]
	public static void OnScenePlay() { _button = null; _spawnButton = null; }

	[Event( "scene.stop" )]
	public static void OnSceneStop() { _button = null; _spawnButton = null; }

	[Event( "scene.open" )]
	public static void OnSceneOpen() { _button = null; _spawnButton = null; }

	private static bool IsButtonInToolbar()
	{
		if ( _button is null || !_button.IsValid() ) return false;
		if ( _spawnButton is null || !_spawnButton.IsValid() ) return false;
		var sceneView = SceneViewWidget.Current;
		if ( !sceneView.IsValid() ) return false;
		return FindDescendant( sceneView, w => w.Name == "JoinDedicatedButton" ) != null;
	}

	private static void TryInjectIntoViewportToolbar()
	{
		var sceneView = SceneViewWidget.Current;
		if ( !sceneView.IsValid() ) return;

		var toolbarWidget = FindDescendant( sceneView, w => w.Name == "ViewportToolbar" );
		if ( toolbarWidget == null ) return;

		Widget playGroup = null;
		foreach ( var child in toolbarWidget.Children )
		{
			if ( child.Children.Any( gc => gc.ToolTip == "Pause" ) )
			{
				playGroup = child;
				break;
			}
		}
		if ( playGroup == null ) return;

		var button = new JoinDedicatedButton();
		playGroup.Layout.Add( button );
		_button = button;

		var spawnButton = new SpawnInstanceButton();
		playGroup.Layout.Add( spawnButton );
		_spawnButton = spawnButton;
	}

	private static Widget FindDescendant( Widget parent, Func<Widget, bool> predicate )
	{
		foreach ( var child in parent.Children )
		{
			if ( predicate( child ) ) return child;
			var found = FindDescendant( child, predicate );
			if ( found != null ) return found;
		}
		return null;
	}
}

file class JoinDedicatedButton : Widget
{
	public JoinDedicatedButton()
	{
		Cursor = CursorShape.Finger;
		MinimumWidth = Theme.RowHeight;
		ToolTip = "Join Dedicated Server";
		Name = "JoinDedicatedButton";
	}

	protected override Vector2 SizeHint() => new( Theme.ControlHeight );

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton ) return;
		OpenJoinPopup();
		e.Accepted = true;
	}

	private void OpenJoinPopup()
	{
		var popup = new PopupWidget( null )
		{
			Layout = Layout.Column(),
			MinimumWidth = 280,
		};
		popup.Layout.Margin = 8;
		popup.Layout.Spacing = 6;

		var label = new Label( "Join Dedicated Server" );
		label.SetStyles( "font-weight: bold;" );
		popup.Layout.Add( label );

		var steamLabel = new Label( "Steam ID / Session" );
		popup.Layout.Add( steamLabel );

		var input = new LineEdit();
		input.PlaceholderText = "e.g. 76561198xxxxxxxxx";
		input.Text = JoinDedicatedToolbar.LastSteamId;
		popup.Layout.Add( input );

		var connectBtn = new Button( "Connect" );
		connectBtn.Clicked = () =>
		{
			JoinDedicatedToolbar.LastSteamId = input.Text.Trim();
			popup.Visible = false;
			_ = NrpEditorMenu.JoinDedicated( JoinDedicatedToolbar.LastSteamId );
		};
		popup.Layout.Add( connectBtn );

		popup.Position = ScreenRect.BottomLeft;
		popup.Visible = true;
		popup.AdjustSize();
		popup.ConstrainToScreen();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;
		Paint.ClearPen();

		var color = Theme.Green;
		if ( Paint.HasMouseOver )
			color = color.Lighten( 0.8f );

		Paint.SetPen( color );
		Paint.DrawIcon( LocalRect, "dns", HeaderBarStyle.IconSize, TextFlag.Center );
	}
}

file class SpawnInstanceButton : Widget
{
	public SpawnInstanceButton()
	{
		Cursor = CursorShape.Finger;
		MinimumWidth = Theme.RowHeight;
		ToolTip = "Spawn New Instance";
		Name = "SpawnInstanceButton";
	}

	protected override Vector2 SizeHint() => new( Theme.ControlHeight );

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton ) return;
		NrpEditorMenu.SpawnNewInstance();
		e.Accepted = true;
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;
		Paint.ClearPen();

		var color = Theme.Blue;
		if ( Paint.HasMouseOver )
			color = color.Lighten( 0.8f );

		Paint.SetPen( color );
		Paint.DrawIcon( LocalRect, "connected_tv", HeaderBarStyle.IconSize, TextFlag.Center );
	}
}