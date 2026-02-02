using System.Collections.Generic;

namespace EternityShared.Game;

public class Board
{
    private readonly int?[] _pieceIds;
    private readonly byte[] _rotations;
    // Pre-calculated edges for fast lookup [row, col, direction(0=T, 1=R, 2=B, 3=L)]
    private readonly byte[,,] _edges;
    private readonly int _size;
    private readonly Dictionary<int, Piece> _masterPieces;

    public Board(Dictionary<int, Piece> masterPieces, int size = GameConfig.BoardSize)
    {
        _size = size;
        _pieceIds = new int?[size * size];
        _rotations = new byte[size * size];
        _edges = new byte[size, size, 4];
        _masterPieces = masterPieces;

        // Initialize with "no piece" marker for internal edges
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                for (int d = 0; d < 4; d++)
                    _edges[r, c, d] = byte.MaxValue;
    }

    public int Size => _size;

    public bool TryPlace(int row, int col, Piece piece, byte rotation)
    {
        if (!IsValidPlacement(row, col, piece)) return false;
        
        int idx = row * _size + col;
        _pieceIds[idx] = piece.Id;
        _rotations[idx] = rotation;
        
        _edges[row, col, 0] = piece.Top;
        _edges[row, col, 1] = piece.Right;
        _edges[row, col, 2] = piece.Bottom;
        _edges[row, col, 3] = piece.Left;
        
        return true;
    }

    public void Remove(int row, int col)
    {
        int idx = row * _size + col;
        _pieceIds[idx] = null;
        for (int d = 0; d < 4; d++) _edges[row, col, d] = byte.MaxValue;
    }

    public int? GetPieceId(int row, int col) => _pieceIds[row * _size + col];
    public byte GetRotation(int row, int col) => _rotations[row * _size + col];

    public bool IsValidPlacement(int row, int col, Piece piece)
    {
        // --- Border Color Constraints ---
        // Top edge: Must be 0 if on border, must NOT be 0 if interior
        if (row == 0) { if (piece.Top != GameConfig.BorderColor) return false; }
        else { if (piece.Top == GameConfig.BorderColor) return false; }

        // Bottom edge: Must be 0 if on border, must NOT be 0 if interior
        if (row == _size - 1) { if (piece.Bottom != GameConfig.BorderColor) return false; }
        else { if (piece.Bottom == GameConfig.BorderColor) return false; }

        // Left edge: Must be 0 if on border, must NOT be 0 if interior
        if (col == 0) { if (piece.Left != GameConfig.BorderColor) return false; }
        else { if (piece.Left == GameConfig.BorderColor) return false; }

        // Right edge: Must be 0 if on border, must NOT be 0 if interior
        if (col == _size - 1) { if (piece.Right != GameConfig.BorderColor) return false; }
        else { if (piece.Right == GameConfig.BorderColor) return false; }

        // --- Neighbor Matching Constraints ---
        // Check Top neighbor
        if (row > 0) {
            byte neighborBottom = _edges[row - 1, col, 2];
            if (neighborBottom != byte.MaxValue && neighborBottom != piece.Top) return false;
        }

        // Check Left neighbor
        if (col > 0) {
            byte neighborRight = _edges[row, col - 1, 1];
            if (neighborRight != byte.MaxValue && neighborRight != piece.Left) return false;
        }

        return true;
    }
}
