using Sandbox;
using System;
using System.Collections.Generic;
using System.Text.Json;

public sealed class Textscreen : Component
{
	public const int LineCount = 6;
	public const int MinSize = 16;
	public const int MaxSize = 64;
	public const int DefaultSize = 32;
	public const string DefaultLineText = "Text here";
	public const string DefaultColor = "white";
	public const string TextscreenTag = "textscreen";

	[Sync( SyncFlags.FromHost )] public Player PlayerOwner { get; private set; }
	[Sync( SyncFlags.FromHost )] public string LinesJson { get; private set; } = "[]";
	[Sync( SyncFlags.FromHost )] public bool HasBackground { get; private set; }

	protected override void OnStart()
	{
#if SERVER
		ConfigurePhysicsShell( GameObject );
#endif
	}

	public void SetOwner( Player owner )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		PlayerOwner = owner;
#endif
	}

	public void SetLines( IReadOnlyList<TextscreenLine> lines )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		LinesJson = JsonSerializer.Serialize( NormalizeLines( lines ) );
#endif
	}

	public void SetBackground( bool hasBackground )
	{
#if SERVER
		if ( !Networking.IsHost )
			return;

		HasBackground = hasBackground;
#endif
	}

	public IReadOnlyList<TextscreenLine> GetLines()
	{
		try
		{
			return NormalizeLines( JsonSerializer.Deserialize<List<TextscreenLine>>( LinesJson ?? "[]" ) );
		}
		catch ( Exception e )
		{
			Log.Warning( $"[Textscreen] Failed to parse lines: {e.Message}" );
			return NormalizeLines( null );
		}
	}

	public static List<TextscreenLine> NormalizeLines( IReadOnlyList<TextscreenLine> lines )
	{
		var result = new List<TextscreenLine>( LineCount );

		for ( var i = 0; i < LineCount; i++ )
		{
			var source = lines is not null && i < lines.Count ? lines[i] : null;
			var text = source?.Text ?? string.Empty;
			if ( i == 0 && string.IsNullOrWhiteSpace( text ) )
				text = DefaultLineText;

			result.Add( new TextscreenLine
			{
				Text = text,
				Size = Math.Clamp( source?.Size ?? DefaultSize, MinSize, MaxSize ),
				Color = NormalizeColorId( source?.Color )
			} );
		}

		return result;
	}

	public static string NormalizeColorId( string colorId )
	{
		var normalized = (colorId ?? string.Empty).Trim().ToLowerInvariant();
		return normalized switch
		{
			"black" => "black",
			"red" => "red",
			"green" => "green",
			"blue" => "blue",
			"yellow" => "yellow",
			"cyan" => "cyan",
			"magenta" => "magenta",
			_ => DefaultColor
		};
	}

	public static PropCustom ConfigurePhysicsShell( GameObject gameObject, Player owner = null, bool freeze = false )
	{
#if SERVER
		if ( !Networking.IsHost || !gameObject.IsValid() )
			return null;

		gameObject.Tags.Add( PropCollisionTags.PropTag );
		gameObject.Tags.Add( TextscreenTag );

		if ( !gameObject.Components.TryGet<Rigidbody>( out _ ) )
			gameObject.Components.Create<Rigidbody>();

		if ( !gameObject.Components.TryGet<PropCustom>( out var propCustom ) )
			propCustom = gameObject.Components.Create<PropCustom>();

		if ( owner.IsValid() )
			propCustom.SetOwner( owner );

		if ( !gameObject.Components.TryGet<WorldTextscreen>( out _ ) )
			gameObject.Components.Create<WorldTextscreen>();

		PropCollisionTags.RefreshPhysicsShapeTags( gameObject );

		if ( freeze )
			propCustom.TryFreezePhysics();

		return propCustom;
#else
		return null;
#endif
	}
}

public sealed class TextscreenLine
{
	public string Text { get; set; } = string.Empty;
	public int Size { get; set; } = Textscreen.DefaultSize;
	public string Color { get; set; } = Textscreen.DefaultColor;
}
