using Ambi.Storage;
using Sandbox;
using System;

/// <summary>Server-side identifiers and progress entry points for Mark's tutorial.</summary>
public static class MarkIntroQuest
{
	public const string QuestId = "mark_intro";
	public const string HitTaskId = "mark_hit";
	public const string BuyDoorsTaskId = "mark_buy_doors";
	public const string ReturnTaskId = "mark_return";

	public static bool TryAdvance( Player player, string expectedTaskId, int delta = 1 )
	{
		if ( !Networking.IsHost || !player.IsValid() || delta <= 0 ) return false;

		var playerQuest = player.Components.Get<PlayerQuest>();
		var definition = QuestDatabase.FindQuestById( QuestId );
		if ( !playerQuest.IsValid() || definition is null ) return false;

		var activeQuest = playerQuest.GetActiveQuest( definition );
		if ( activeQuest is null ) return false;
		if ( !string.Equals( activeQuest.CurrentQuestTask?.Id, expectedTaskId, StringComparison.OrdinalIgnoreCase ) ) return false;

		return QuestManager.TryAdvanceCount( playerQuest, definition, delta );
	}

	internal static void Reward( PlayerQuest questPlayer, int money, string itemId, string message )
	{
		if ( !Networking.IsHost || !questPlayer.IsValid() ) return;

		var player = questPlayer.GameObject.Components.Get<Player>( FindMode.EverythingInSelfAndParent );
		if ( !player.IsValid() ) return;

		if ( money > 0 )
			player.Money = (int)Math.Clamp( (long)player.Money + money, 0L, int.MaxValue );

		if ( !string.IsNullOrWhiteSpace( itemId ) && player.Inventory is not null && player.Inventory.GetTotalCount( itemId ) <= 0 )
			player.HostAddItem( Item.Create( itemId, 1, canDrop: false, isJobItem: false, canSave: true ) );

		QuestNpcMark.HostNotify( player.GameObject.Network.Owner, message, true );
	}
}

public sealed class MarkIntroQuestHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.QuestId;

	public void OnQuestCompleted( PlayerQuest player, Quest quest )
	{
		Log.Info( $"[Mark] Tutorial completed by {player?.GameObject?.Network.Owner?.DisplayName ?? "unknown"}." );
	}
}

public sealed class MarkIntroHitTaskHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.HitTaskId;

	public void OnTaskCompleted( PlayerQuest player, Quest quest, QuestTaskDefinition task )
	{
		MarkIntroQuest.Reward( player, 50, "hands", "Задание выполнено: +$50. Кулаки закреплены в инвентаре." );
	}
}

public sealed class MarkIntroBuyDoorsTaskHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.BuyDoorsTaskId;

	public void OnTaskCompleted( PlayerQuest player, Quest quest, QuestTaskDefinition task )
	{
		MarkIntroQuest.Reward( player, 50, "keys", "Задание выполнено: +$50 и ключи. Вернись к Марку." );
	}
}

public sealed class MarkIntroReturnTaskHandler : IQuestHandler
{
	public string Id => MarkIntroQuest.ReturnTaskId;

	public void OnTaskCompleted( PlayerQuest player, Quest quest, QuestTaskDefinition task )
	{
		MarkIntroQuest.Reward( player, 100, "burger", "Марк: Отлично. У тебя есть жильё и базовые инструменты. Награда: +$100 и бургер." );
	}
}
