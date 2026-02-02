using System.IO;

namespace EternityShared.Game;

public static class PieceLoader
{
    public static List<Piece> LoadFromCsv(string csvPath)
    {
        var pieces = new List<Piece>();
        if (!File.Exists(csvPath)) return pieces;

        var lines = File.ReadAllLines(csvPath);

        Console.WriteLine($"[PieceLoader] Loading pieces from {csvPath}...");
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(',');
            if (parts.Length < 5) continue;

            if (!int.TryParse(parts[0], out int id)) continue; 

            // Skip potential metadata line (if it looks like 16,16,5,17)
            if (id == 16 && parts[1] == "16") {
                Console.WriteLine("[PieceLoader] Skipped metadata line starting with 16,16...");
                continue; 
            }

            byte east = ParseEdge(parts[1]);
            byte south = ParseEdge(parts[2]);
            byte west = ParseEdge(parts[3]);
            byte north = ParseEdge(parts[4]);

            pieces.Add(new Piece(id, north, east, south, west));
        }

        Console.WriteLine($"[PieceLoader] Successfully loaded {pieces.Count} pieces.");

        return pieces;
    }

    private static byte ParseEdge(string val)
    {
        if (string.IsNullOrWhiteSpace(val)) return 0; // Empty means border (0)
        return byte.TryParse(val.Trim(), out byte result) ? result : (byte)0;
    }
}
