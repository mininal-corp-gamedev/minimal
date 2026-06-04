using Ambi.Utils;
using Sandbox;
using System;
using System.Threading.Tasks;

public sealed partial class Boombox : Component, Component.IPressable, ICustomDamagable
{
	private const string DefaultRadioUrl = "https://radio-srv1.11one.ru/record192k.mp3";
	private const string CloudPackageIdent = "gtavteam.gta5propmp3dock#97365";
	private const string CloudModelPath = "pr/gta5_prop_mp3_dock.vmdl";

	private static Task<Model> _sharedCloudModelTask;

	[Property, Group( "Stats" )]
	[Sync( SyncFlags.FromHost )]
	public float MaxUseDistance { get; set; } = 100f;

	[Property, Group( "Health" )]
	[Sync( SyncFlags.FromHost )]
	public float MaxHealth { get; set; } = 100f;

	[Property, Group( "Health" )]
	[Sync( SyncFlags.FromHost )]
	public float Health { get; set; } = 100f;

	[Property, Group( "Audio" )]
	[Sync( SyncFlags.FromHost )]
	public string RadioUrl { get; set; } = DefaultRadioUrl;

	[Property, Group( "Audio" )]
	[Sync( SyncFlags.FromHost )]
	public float RadioVolume { get; set; } = 1f;

	[Property, Group( "Audio" )]
	[Sync( SyncFlags.FromHost )]
	public float RadioDistance { get; set; } = 600f;

	[Sync( SyncFlags.FromHost ), Change( nameof( OnRadioEnabledChanged ) )]
	public bool IsRadioEnabled { get; private set; }

	private MusicPlayer _radioPlayer;

	protected override void OnStart()
	{
#if SERVER
		if ( Networking.IsHost )
			Health = MaxHealth;
#endif

		_ = EnsureCloudModelLoadedAsync();
		UpdatePlaybackState();
	}

	protected override void OnUpdate()
	{
		UpdateRadioTransform();
	}

	protected override void OnDisabled()
	{
		StopRadioPlayback();
	}

	protected override void OnDestroy()
	{
		StopRadioPlayback();
	}

	public void OnDamage( in DamageInfo damage )
	{
		if ( Networking.IsHost )
		{
#if SERVER
			ApplyDamageServer( damage.Damage );
#endif
			return;
		}

		RpcApplyDamage( damage.Damage );
	}

	public bool Press( IPressable.Event e )
	{
		var source = e.Source?.GameObject;
		if ( source is null )
			return false;

		if ( !source.Components.TryGet<Player>( out var player, FindMode.EverythingInSelfAndParent ) )
			return false;

		if ( player.IsProxy )
			return false;

		RpcToggleRadio();

		return true;
	}

	[Rpc.Host]
	private void RpcToggleRadio()
	{
#if SERVER
		if ( !TryGetCallerPlayer( out _, 0f ) )
			return;

		ToggleRadioServer();
#endif
	}

	[Rpc.Host]
	private void RpcApplyDamage( float damage )
	{
#if SERVER
		if ( !TryGetCallerPlayer( out _, 160f ) )
			return;

		damage = Math.Clamp( damage, 0f, MaxHealth );
		ApplyDamageServer( damage );
#endif
	}

	private void OnRadioEnabledChanged( bool _, bool _2 )
	{
		UpdatePlaybackState();
	}

	private void UpdatePlaybackState()
	{
		if ( !Enabled )
		{
			StopRadioPlayback();
			return;
		}

		if ( IsRadioEnabled )
			StartRadioPlayback();
		else
			StopRadioPlayback();
	}

	private void StartRadioPlayback()
	{
		if ( _radioPlayer is not null )
		{
			UpdateRadioTransform();
			return;
		}

		if ( string.IsNullOrWhiteSpace( RadioUrl ) )
			return;

		_radioPlayer = MusicPlayer.PlayUrl( RadioUrl );
		if ( _radioPlayer is null )
			return;

		_radioPlayer.ListenLocal = false;
		_radioPlayer.Volume = Math.Max( 0f, RadioVolume );
		_radioPlayer.Distance = Math.Max( 1f, RadioDistance );
		UpdateRadioTransform();
	}

	private void StopRadioPlayback()
	{
		if ( _radioPlayer is null )
			return;

		_radioPlayer.Stop();
		_radioPlayer.Dispose();
		_radioPlayer = null;
	}

	private void UpdateRadioTransform()
	{
		if ( _radioPlayer is null )
			return;

		_radioPlayer.ListenLocal = false;
		_radioPlayer.Position = WorldPosition;
		_radioPlayer.Volume = Math.Max( 0f, RadioVolume );
		_radioPlayer.Distance = Math.Max( 1f, RadioDistance );
	}

	private async Task EnsureCloudModelLoadedAsync()
	{
		var renderer = Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		if ( !renderer.IsValid() )
			return;

		if ( renderer.Model is not null && !renderer.Model.IsError )
			return;

		var model = await GetOrLoadCloudModelAsync();
		if ( model is null || model.IsError || !renderer.IsValid() )
			return;

		renderer.Model = model;

		var collider = Components.Get<ModelCollider>( FindMode.EverythingInSelfAndDescendants );
		if ( collider.IsValid() )
			collider.Model = model;
	}

	private static Task<Model> GetOrLoadCloudModelAsync()
	{
		_sharedCloudModelTask ??= LoadCloudModelAsync();
		return _sharedCloudModelTask;
	}

	private static async Task<Model> LoadCloudModelAsync()
	{
		try
		{
			var model = Model.Load( CloudModelPath );
			if ( model is not null && !model.IsError )
				return model;

			// Temporary workaround: this boombox currently depends on a cloud model that
			// may not be mounted on clients yet. Long-term we should avoid runtime cloud
			// fetches here and ship a reliable local/project-owned model dependency instead.
			var package = await Package.FetchAsync( CloudPackageIdent, partial: false );
			if ( package is null )
				return null;

			if ( await package.MountAsync() is null )
				return null;

			model = Model.Load( CloudModelPath );
			return model is not null && !model.IsError ? model : null;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Boombox: failed to load cloud model '{CloudPackageIdent}': {e.Message}" );
			return null;
		}
	}
}
