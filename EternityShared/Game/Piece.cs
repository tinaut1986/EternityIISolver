namespace EternityShared.Game;

public record struct Piece(int Id, byte Top, byte Right, byte Bottom, byte Left)
{
    public Piece Rotate(int count)
    {
        count = ((count % 4) + 4) % 4;
        Piece result = this;
        for (int i = 0; i < count; i++)
        {
            result = new Piece(result.Id, result.Left, result.Top, result.Right, result.Bottom);
        }
        return result;
    }

    public override string ToString() => $"#{Id}[T:{Top}, R:{Right}, B:{Bottom}, L:{Left}]";
}
