using Sandbox;

/// <summary>
/// Живёт на том же GameObject что и ScreenPanel (Player Controller → Screen).
/// Управляет открытием/закрытием и передаёт данные в Razor-панель.
/// </summary>
public sealed class ColorSlotScreen : Component
{
    // Razor-панель читает эти свойства через ссылку на этот компонент
    public ColorSlot  Slot        { get; private set; }
    public bool       IsOpen      { get; private set; }
    public bool       IsWaiting   { get; private set; }

    // Результат
    public bool       HasResult   { get; private set; }
    public bool       ResultWon   { get; private set; }
    public bool       ResultGreen { get; private set; }
    public bool       NoMoney     { get; private set; }

    [Property] public GameObject ScreenPanelObject { get; set; }

    // ── Открыть панель ───────────────────────────

    public void Open( ColorSlot slot )
    {
        Slot        = slot;
        IsOpen      = true;
        IsWaiting   = false;
        HasResult   = false;
        NoMoney     = false;

        if ( ScreenPanelObject.IsValid() )
            ScreenPanelObject.Enabled = true;

        Mouse.Visible = true;
    }

    // ── Закрыть панель ───────────────────────────

    public void Close()
    {
        IsOpen      = false;
        IsWaiting   = false;

        if ( ScreenPanelObject.IsValid() )
            ScreenPanelObject.Enabled = false;

        Mouse.Visible = false;
    }

    // ── Ставка — вызывается из Razor ────────────

    public void PlaceBet( bool onGreen )
    {
        if ( Slot is null || IsWaiting ) return;

        var ply = Player.Local;
        if ( ply is null ) return;

        IsWaiting = true;
        HasResult = false;
        NoMoney   = false;

        Slot.RpcPlaceBet( ply.GameObject, onGreen );
    }

    // ── Результат — вызывается из ColorSlot RPC ──

    public void OnResult( bool won, bool resultGreen, bool noMoney )
    {
        IsWaiting   = false;
        HasResult   = true;
        ResultWon   = won;
        ResultGreen = resultGreen;
        NoMoney     = noMoney;
    }
}
