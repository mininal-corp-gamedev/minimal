using Sandbox;

/// <summary>
/// Локальный синглтон — мост между ColorSlot (куб) и ColorSlotPanel (Razor UI).
/// Не синкается, живёт только на клиенте.
/// </summary>
public sealed class ColorSlotState : Component
{
    public static ColorSlotState Instance { get; private set; }

    // ── Данные для панели ────────────────────────

    public ColorSlot ActiveSlot  { get; private set; }
    public bool      IsOpen { get; private set; }
    public bool      IsWaiting   { get; private set; }

    // Результат
    public bool HasResult    { get; private set; }
    public bool ResultWon    { get; private set; }
    public bool ResultGreen  { get; private set; }
    public bool NoMoney      { get; private set; }

    // ── Жизненный цикл ──────────────────────────

    protected override void OnStart()
    {
        Instance = this;
    }

    protected override void OnUpdate()
    {
        if ( !IsOpen ) return;
        if ( ActiveSlot is null ) return;

        var ply = Player.Local;
        if ( ply is null ) return;

        var dist = Vector3.DistanceBetween( ply.WorldPosition, ActiveSlot.WorldPosition );
        if ( dist > 150f )
            Close();
    }

    protected override void OnDestroy()
    {
        if ( Instance == this )
            Instance = null;
    }

    // ── API — вызывается из ColorSlot и панели ──

    public void Open( ColorSlot slot )
    {
        ActiveSlot  = slot;
        IsOpen      = true;
        IsWaiting   = false;
        HasResult   = false;
        NoMoney     = false;

        Mouse.Visible = true;
    }

    public void Close()
    {
        IsOpen        = false;
        IsWaiting     = false;
        Mouse.Visible = false;
    }

    public void PlaceBet( bool onGreen )
    {
        if ( ActiveSlot is null || IsWaiting ) return;

        var ply = Player.Local;
        if ( ply is null ) return;

        IsWaiting = true;
        HasResult = false;
        NoMoney   = false;

        ActiveSlot.RpcPlaceBet( ply.GameObject, onGreen );
    }

    public void OnResult( bool won, bool green, bool noMoney )
    {
        IsWaiting   = false;
        HasResult   = true;
        ResultWon   = won;
        ResultGreen = green;
        NoMoney     = noMoney;
    }
}