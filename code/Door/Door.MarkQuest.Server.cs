public sealed partial class Door
{
	partial void OnBoughtForQuest( Player buyer )
	{
		MarkIntroQuest.TryAdvance( buyer, MarkIntroQuest.BuyDoorsTaskId );
	}
}
