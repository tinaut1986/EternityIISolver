using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EternityShared.Game;

namespace EternityWorker.Services;

public class SolverService
{
    private readonly List<Piece> _allPieces;
    private readonly Dictionary<int, Piece[]> _pieceRotations;
    private readonly Board _board;
    private readonly bool[] _usedPieces;
    private readonly byte[] _initialBoardPayload; // Store initial state for splitting
    private readonly HashSet<int> _initiallyUsedPieceIds; // Pieces used in initial state (hints)
    private long _nodesVisited;
    private long _leafChecksum;
    private int _maxDepth;
    private byte[]? _bestBoard;

    public SolverService(List<Piece> allPieces, string base64Payload)
    {
        _allPieces = allPieces;
        _pieceRotations = allPieces.GroupBy(p => p.Id).ToDictionary(
            g => g.Key, 
            g => {
                var p = g.First();
                return new Piece[] { p, p.Rotate(1), p.Rotate(2), p.Rotate(3) };
            }
        );

        _board = new Board(_pieceRotations.ToDictionary(k => k.Key, v => v.Value[0]));
        _usedPieces = new bool[allPieces.Count > 0 ? allPieces.Max(p => p.Id) + 1 : 257];
        _initiallyUsedPieceIds = new HashSet<int>();

        // Store initial payload for later use in splitting
        _initialBoardPayload = string.IsNullOrEmpty(base64Payload) ? Array.Empty<byte>() : Convert.FromBase64String(base64Payload);

        if (!string.IsNullOrEmpty(base64Payload))
        {
            var data = Convert.FromBase64String(base64Payload);
            var placements = BoardBinarySerializer.Deserialize(data);
            int count = 0;
            foreach (var p in placements)
            {
                int r = p.Position / GameConfig.BoardSize;
                int c = p.Position % GameConfig.BoardSize;
                if (_pieceRotations.TryGetValue(p.PieceId, out var rotations))
                {
                    if (_board.TryPlace(r, c, rotations[p.Rotation], p.Rotation))
                    {
                        _usedPieces[p.PieceId] = true;
                        _initiallyUsedPieceIds.Add(p.PieceId);
                        count++;
                    }
                }
            }
            Console.WriteLine($"[Solver] Loaded board state with {count} pieces (hints).");
        }
    }

    public async Task<(string Result, string? Data, List<string>? Splits, long Nodes, long Checksum, int MaxDepth, string? BestBoard)> SolveAsync(CancellationToken ct)
    {
        _nodesVisited = 0;
        _leafChecksum = 0;
        _maxDepth = _initiallyUsedPieceIds.Count; // Start depth from hints count
        _bestBoard = null;

        int startR = -1, startC = -1;
        for (int r = 0; r < GameConfig.BoardSize; r++)
        {
            for (int c = 0; c < GameConfig.BoardSize; c++)
            {
                if (_board.GetPieceId(r, c) == null) { startR = r; startC = c; goto Found; }
            }
        }

    Found:
        if (startR == -1) return ("SOLUTION_FOUND", Convert.ToBase64String(BoardBinarySerializer.Serialize(_board)), null, _nodesVisited, _leafChecksum, 256, null);

        Console.WriteLine($"[Solver] Starting search at [{startR},{startC}]...");
        
        try 
        {
            bool solved = await Task.Run(() => BacktrackSync(startR, startC, ct), ct);
            
            string res = solved ? "SOLUTION_FOUND" : "NO_SOLUTION_FOUND";
            Console.WriteLine($"[Solver] Search finished: {res}. Nodes: {_nodesVisited:N0}");
            
            if (solved) return ("SOLUTION_FOUND", Convert.ToBase64String(BoardBinarySerializer.Serialize(_board)), null, _nodesVisited, _leafChecksum, 256, null);
            return ("NO_SOLUTION_FOUND", null, null, _nodesVisited, _leafChecksum, _maxDepth, _bestBoard != null ? Convert.ToBase64String(_bestBoard) : null);
        }
        catch (OperationCanceledException)
        {
            // Generate splits from the INITIAL state, not the current exploration state
            var splits = GenerateSplitsFromInitialState();
            Console.WriteLine($"[Solver] Search split after {_nodesVisited:N0} nodes. Best depth: {_maxDepth}");
            return ("SPLIT", null, splits, _nodesVisited, _leafChecksum, _maxDepth, _bestBoard != null ? Convert.ToBase64String(_bestBoard) : null);
        }
    }

    private bool BacktrackSync(int r, int c, CancellationToken ct)
    {
        _nodesVisited++;
        
        if ((_nodesVisited & 0xFFFFF) == 0) 
        {
            Console.WriteLine($"[{DateTime.Now:H:mm:ss}] Progress: {_nodesVisited:N0} nodes...");
            ct.ThrowIfCancellationRequested();
        }

        int depth = r * GameConfig.BoardSize + c;
        if (depth > _maxDepth)
        {
            _maxDepth = depth;
            _bestBoard = BoardBinarySerializer.Serialize(_board);
        }

        int nextR = r, nextC = c + 1;
        if (nextC >= GameConfig.BoardSize) { nextR++; nextC = 0; }

        // Skip to next empty cell (in case there are hints ahead)
        while (nextR < GameConfig.BoardSize && _board.GetPieceId(nextR, nextC) != null)
        {
            nextC++;
            if (nextC >= GameConfig.BoardSize) { nextR++; nextC = 0; }
        }

        foreach (var piece in _allPieces)
        {
            if (_usedPieces[piece.Id]) continue;

            var rotations = _pieceRotations[piece.Id];
            for (int rot = 0; rot < 4; rot++)
            {
                var rotatedPiece = rotations[rot];
                if (_board.TryPlace(r, c, rotatedPiece, (byte)rot))
                {
                    _usedPieces[piece.Id] = true;
                    if (nextR >= GameConfig.BoardSize || BacktrackSync(nextR, nextC, ct)) return true;
                    
                    _board.Remove(r, c);
                    _usedPieces[piece.Id] = false;
                }
            }
        }

        return false;
    }

    private List<string> GenerateSplitsFromInitialState()
    {
        // Create a fresh board with only the initial hints
        var freshBoard = new Board(_pieceRotations.ToDictionary(k => k.Key, v => v.Value[0]));
        var freshUsed = new HashSet<int>();

        // Reload initial state
        if (_initialBoardPayload.Length > 0)
        {
            var placements = BoardBinarySerializer.Deserialize(_initialBoardPayload);
            foreach (var p in placements)
            {
                int r = p.Position / GameConfig.BoardSize;
                int c = p.Position % GameConfig.BoardSize;
                if (_pieceRotations.TryGetValue(p.PieceId, out var rotations))
                {
                    freshBoard.TryPlace(r, c, rotations[p.Rotation], p.Rotation);
                    freshUsed.Add(p.PieceId);
                }
            }
        }

        // Find first empty cell in the fresh board
        int splitR = -1, splitC = -1;
        for (int row = 0; row < GameConfig.BoardSize; row++)
        {
            for (int col = 0; col < GameConfig.BoardSize; col++)
            {
                if (freshBoard.GetPieceId(row, col) == null) { splitR = row; splitC = col; goto FoundEmpty; }
            }
        }
    FoundEmpty:
        if (splitR == -1) return new List<string>();

        Console.WriteLine($"[Solver] Generating splits from initial state at [{splitR},{splitC}]...");
        int availablePieces = _allPieces.Count(p => !freshUsed.Contains(p.Id));
        Console.WriteLine($"[Solver] Available pieces for splitting: {availablePieces}");
        
        var splits = new List<string>();
        foreach (var piece in _allPieces)
        {
            if (freshUsed.Contains(piece.Id)) continue;
            var rotations = _pieceRotations[piece.Id];
            for (int rot = 0; rot < 4; rot++)
            {
                var rotatedPiece = rotations[rot];
                if (freshBoard.TryPlace(splitR, splitC, rotatedPiece, (byte)rot))
                {
                    splits.Add(Convert.ToBase64String(BoardBinarySerializer.Serialize(freshBoard)));
                    freshBoard.Remove(splitR, splitC);
                }
            }
        }
        Console.WriteLine($"[Solver] Generated {splits.Count} subjobs.");
        return splits;
    }
}
