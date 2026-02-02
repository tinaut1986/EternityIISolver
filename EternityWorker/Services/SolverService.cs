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
    private readonly byte[] _initialBoardPayload;
    private readonly HashSet<int> _initiallyUsedPieceIds;
    
    // Track which pieces we've tried at each position during exploration
    // Key: position (r * 16 + c), Value: set of piece IDs tried at that position
    private readonly Dictionary<int, HashSet<int>> _triedPiecesAtPosition;
    
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
        _triedPiecesAtPosition = new Dictionary<int, HashSet<int>>();

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
        _maxDepth = _initiallyUsedPieceIds.Count;
        _bestBoard = null;
        _triedPiecesAtPosition.Clear();

        // Find first empty cell
        int startR = -1, startC = -1;
        for (int r = 0; r < GameConfig.BoardSize; r++)
        {
            for (int c = 0; c < GameConfig.BoardSize; c++)
            {
                if (_board.GetPieceId(r, c) == null) { startR = r; startC = c; goto Found; }
            }
        }

    Found:
        if (startR == -1) 
            return ("SOLUTION_FOUND", Convert.ToBase64String(BoardBinarySerializer.Serialize(_board)), null, _nodesVisited, _leafChecksum, 256, null);

        Console.WriteLine($"[Solver] Starting search at [{startR},{startC}]...");
        
        try 
        {
            bool solved = await Task.Run(() => BacktrackSync(startR, startC, ct), ct);
            
            string res = solved ? "SOLUTION_FOUND" : "NO_SOLUTION_FOUND";
            Console.WriteLine($"[Solver] Search finished: {res}. Nodes: {_nodesVisited:N0}");
            
            if (solved) 
                return ("SOLUTION_FOUND", Convert.ToBase64String(BoardBinarySerializer.Serialize(_board)), null, _nodesVisited, _leafChecksum, 256, null);
            return ("NO_SOLUTION_FOUND", null, null, _nodesVisited, _leafChecksum, _maxDepth, _bestBoard != null ? Convert.ToBase64String(_bestBoard) : null);
        }
        catch (OperationCanceledException)
        {
            // Generate splits, backtracking if necessary
            var splits = GenerateSplitsWithBacktrack();
            Console.WriteLine($"[Solver] Search split after {_nodesVisited:N0} nodes. Best depth: {_maxDepth}");
            return ("SPLIT", null, splits, _nodesVisited, _leafChecksum, _maxDepth, _bestBoard != null ? Convert.ToBase64String(_bestBoard) : null);
        }
    }

    private bool BacktrackSync(int r, int c, CancellationToken ct)
    {
        _nodesVisited++;
        int pos = r * GameConfig.BoardSize + c;
        
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

        // Find next empty cell
        int nextR = r, nextC = c + 1;
        if (nextC >= GameConfig.BoardSize) { nextR++; nextC = 0; }
        while (nextR < GameConfig.BoardSize && _board.GetPieceId(nextR, nextC) != null)
        {
            nextC++;
            if (nextC >= GameConfig.BoardSize) { nextR++; nextC = 0; }
        }

        // Track which pieces we try at this position
        if (!_triedPiecesAtPosition.ContainsKey(pos))
            _triedPiecesAtPosition[pos] = new HashSet<int>();

        foreach (var piece in _allPieces)
        {
            if (_usedPieces[piece.Id]) continue;

            // Record that we're trying this piece at this position
            _triedPiecesAtPosition[pos].Add(piece.Id);

            var rotations = _pieceRotations[piece.Id];
            for (int rot = 0; rot < 4; rot++)
            {
                var rotatedPiece = rotations[rot];
                if (_board.TryPlace(r, c, rotatedPiece, (byte)rot))
                {
                    _usedPieces[piece.Id] = true;
                    
                    if (nextR >= GameConfig.BoardSize || BacktrackSync(nextR, nextC, ct)) 
                        return true;
                    
                    _board.Remove(r, c);
                    _usedPieces[piece.Id] = false;
                }
            }
        }

        // Clear tracking for this position since we're backtracking past it
        _triedPiecesAtPosition.Remove(pos);
        
        return false;
    }

    private List<string> GenerateSplitsWithBacktrack()
    {
        // Strategy: 
        // 1. Try to generate splits at the current first empty position
        // 2. If no valid pieces, backtrack (remove last placed piece) and try again
        // 3. When backtracking, only generate splits for pieces we HAVEN'T tried yet at that position
        // 4. Continue until we find splits or reach the initial state
        
        int attempts = 0;
        int maxBacktrackAttempts = 256; // Safety limit
        
        while (attempts < maxBacktrackAttempts)
        {
            attempts++;
            
            // Find first empty cell
            int splitR = -1, splitC = -1;
            for (int row = 0; row < GameConfig.BoardSize; row++)
            {
                for (int col = 0; col < GameConfig.BoardSize; col++)
                {
                    if (_board.GetPieceId(row, col) == null) { splitR = row; splitC = col; goto FoundEmpty; }
                }
            }
        FoundEmpty:
            if (splitR == -1) 
            {
                Console.WriteLine($"[Solver] Board is full during split generation!");
                return new List<string>();
            }

            int pos = splitR * GameConfig.BoardSize + splitC;
            var triedAtThisPos = _triedPiecesAtPosition.GetValueOrDefault(pos, new HashSet<int>());
            
            // Count pieces on board and available
            int piecesOnBoard = 0;
            for (int row = 0; row < GameConfig.BoardSize; row++)
                for (int col = 0; col < GameConfig.BoardSize; col++)
                    if (_board.GetPieceId(row, col) != null) piecesOnBoard++;

            Console.WriteLine($"[Solver] Attempt {attempts}: Trying splits at [{splitR},{splitC}] with {piecesOnBoard} pieces. Already tried: {triedAtThisPos.Count}");
            
            var splits = new List<string>();
            foreach (var piece in _allPieces)
            {
                if (_usedPieces[piece.Id]) continue;
                
                // Skip pieces we've already fully explored at this position
                if (triedAtThisPos.Contains(piece.Id)) continue;
                
                var rotations = _pieceRotations[piece.Id];
                for (int rot = 0; rot < 4; rot++)
                {
                    var rotatedPiece = rotations[rot];
                    if (_board.TryPlace(splitR, splitC, rotatedPiece, (byte)rot))
                    {
                        splits.Add(Convert.ToBase64String(BoardBinarySerializer.Serialize(_board)));
                        _board.Remove(splitR, splitC);
                    }
                }
            }
            
            if (splits.Count > 0)
            {
                Console.WriteLine($"[Solver] Generated {splits.Count} subjobs at depth {piecesOnBoard} (excluded {triedAtThisPos.Count} already tried).");
                return splits;
            }
            
            // No splits at this level. Need to backtrack.
            // Find the last piece that was placed (not an initial hint) and remove it.
            Console.WriteLine($"[Solver] No valid splits at [{splitR},{splitC}]. Backtracking...");
            
            bool foundPieceToRemove = false;
            for (int row = GameConfig.BoardSize - 1; row >= 0 && !foundPieceToRemove; row--)
            {
                for (int col = GameConfig.BoardSize - 1; col >= 0 && !foundPieceToRemove; col--)
                {
                    var pieceId = _board.GetPieceId(row, col);
                    if (pieceId != null && !_initiallyUsedPieceIds.Contains(pieceId.Value))
                    {
                        // Found a non-initial piece to remove
                        Console.WriteLine($"[Solver] Removing piece {pieceId} at [{row},{col}] to backtrack.");
                        _board.Remove(row, col);
                        _usedPieces[pieceId.Value] = false;
                        foundPieceToRemove = true;
                    }
                }
            }
            
            if (!foundPieceToRemove)
            {
                // We've backtracked all the way to the initial state!
                Console.WriteLine($"[Solver] Backtracked to initial state. No more splits possible.");
                return new List<string>();
            }
        }
        
        Console.WriteLine($"[Solver] Exceeded max backtrack attempts!");
        return new List<string>();
    }
}
