namespace Ambi.Storage;

public interface IItemUseHandler
{
    bool Use(Item item, Player caller);

    void OnSwitched(Item item, Player caller) { }
}