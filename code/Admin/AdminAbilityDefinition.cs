namespace Sandbox;

public sealed class AdminAbilityDefinition
{
	public string Id { get; }
	public string Title { get; }
	public string Description { get; }
	public int RequiredRank { get; }
	public bool NeedsOnlinePlayer { get; }
	public bool NeedsSteamIdInput { get; }
	public bool NeedsValue { get; }
	public bool NeedsReason { get; }
	public string ValueLabel { get; }
	public string ValuePlaceholder { get; }

	public AdminAbilityDefinition( string id, string title, string description, int requiredRank, bool needsOnlinePlayer, bool needsSteamIdInput, bool needsValue, bool needsReason, string valueLabel, string valuePlaceholder )
	{
		Id = id;
		Title = title;
		Description = description;
		RequiredRank = requiredRank;
		NeedsOnlinePlayer = needsOnlinePlayer;
		NeedsSteamIdInput = needsSteamIdInput;
		NeedsValue = needsValue;
		NeedsReason = needsReason;
		ValueLabel = valueLabel;
		ValuePlaceholder = valuePlaceholder;
	}
}
