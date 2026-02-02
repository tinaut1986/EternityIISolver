namespace EternityShared.Game;

public record struct Hint(int Row, int Col, int PieceId, int Rotation);

public static class HintLoader
{
    public static List<Hint> LoadFromCsv(string csvPath)
    {
        var hints = new List<Hint>();
        if (!File.Exists(csvPath)) return hints;

        var lines = File.ReadAllLines(csvPath);
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var parts = line.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) continue;

            if (!int.TryParse(parts[0], out int row)) continue;
            if (!int.TryParse(parts[1], out int col)) continue;
            if (!int.TryParse(parts[2], out int pieceId)) continue;
            if (!int.TryParse(parts[3], out int rotation)) continue;

            hints.Add(new Hint(row, col, pieceId, rotation));
        }

        return hints;
    }
}
