using System.Runtime.InteropServices;

namespace EternityShared.Game;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PiecePlacement
{
    public ushort PieceId;  // 1-256
    public byte Position;   // 0-255
    public byte Rotation;   // 0-3
}

public static class BoardBinarySerializer
{
    public static byte[] Serialize(Board board)
    {
        var placements = new List<PiecePlacement>();
        for (int r = 0; r < board.Size; r++)
        {
            for (int c = 0; c < board.Size; c++)
            {
                var pieceId = board.GetPieceId(r, c);
                if (pieceId != null)
                {
                    placements.Add(new PiecePlacement
                    {
                        Position = (byte)(r * board.Size + c),
                        PieceId = (ushort)pieceId.Value,
                        Rotation = board.GetRotation(r, c)
                    });
                }
            }
        }

        return MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(placements)).ToArray();
    }

    public static List<PiecePlacement> Deserialize(byte[] data)
    {
        if (data == null || data.Length == 0) return new List<PiecePlacement>();
        return MemoryMarshal.Cast<byte, PiecePlacement>(data).ToArray().ToList();
    }
}
