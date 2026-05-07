using Sandbox;

public sealed class FadingDoor : Component
{
    public Player PlayerOwner { get; set; }

    public void Open()
    {
        //todo GameObject disable collision
    }

    public void Close()
    {
        //todo GameObject enable collision
    }
}
