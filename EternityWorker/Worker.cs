using EternityWorker.Services;
using EternityShared.Game;
using EternityShared.Dtos;
using System.IO;

namespace EternityWorker;

public class Worker
{
    private readonly string _workerId = Guid.NewGuid().ToString();
    private readonly ApiClient _apiClient;
    private readonly string _piecesPath = "eternity2_256.csv";
    private readonly string _hintsPath = "eternity2_256_all_hints.csv";
    
    // V2: Power Profiles
    public int DegreeOfParallelism { get; set; } = Environment.ProcessorCount; 

    public Worker(string baseUrl)
    {
        _apiClient = new ApiClient(baseUrl);
    }

    public async Task RunAsync()
    {
        string fullPiecesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _piecesPath);
        Console.WriteLine($"Starting worker {_workerId} with {DegreeOfParallelism} threads.");
        var allPieces = PieceLoader.LoadFromCsv(fullPiecesPath);
        
        var tasks = Enumerable.Range(0, DegreeOfParallelism).Select(i => Task.Run(() => RunSingleLoopAsync(allPieces, i)));
        await Task.WhenAll(tasks);
    }

    private async Task RunSingleLoopAsync(List<Piece> allPieces, int threadId)
    {
        string threadWorkerId = $"{_workerId}-{threadId}";
        while (true)
        {
            EternityShared.Dtos.JobResponseDto? job = null;
            try
            {
                Console.WriteLine($"[{DateTime.Now:H:mm:ss}] Thread {threadId} requesting job...");
                job = await _apiClient.RequestJobAsync(threadWorkerId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:H:mm:ss}] Thread {threadId} error connecting to server: {ex.Message}. Retrying in 10s...");
            }

            if (job == null)
            {
                Console.WriteLine($"[{DateTime.Now:H:mm:ss}] Thread {threadId}: No jobs available or server unreachable.");
                await Task.Delay(10000); // 10s wait if error or no jobs
                continue;
            }

            Console.WriteLine($"[{DateTime.Now:H:mm:ss}] Thread {threadId}: Job {job.JobId} started. Solving...");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(job.TimeLimitSeconds));
            
            _ = Task.Run(async () =>
            {
                try {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(30000, cts.Token); 
                        await _apiClient.SendHeartbeatAsync(job.JobId, threadWorkerId);
                        Console.WriteLine($"[{DateTime.Now:H:mm:ss}] Heartbeat sent for Job {job.JobId} (Thread {threadId}).");
                    }
                } catch {}
            });

            var solver = new SolverService(allPieces, job.BoardPayloadBase64);
            var (result, data, splits, nodes, checksum, maxDepth, bestBoard) = await solver.SolveAsync(cts.Token);

            if (result == "SPLIT" && (splits == null || splits.Count == 0))
            {
                // If it was supposed to split but found no children, it's actually a dead end
                result = "NO_SOLUTION_FOUND";
            }

            if (result == "SPLIT")
            {
                Console.WriteLine($"[{DateTime.Now:H:mm:ss}] Thread {threadId}: Job {job.JobId} split into {splits?.Count ?? 0} subjobs. Best depth: {maxDepth}/256. Nodes: {nodes:N0}.");
                await _apiClient.ReportSplitAsync(new ReportSplitDto
                {
                    JobId = job.JobId,
                    WorkerId = threadWorkerId,
                    NewSubJobsBase64 = splits ?? new(),
                    NodesVisited = nodes,
                    LeafChecksum = checksum,
                    BestDepth = maxDepth,
                    BestBoardBase64 = bestBoard
                });
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:H:mm:ss}] Thread {threadId}: Job {job.JobId} completed ({result}). Best depth: {maxDepth}/256. Nodes: {nodes:N0}.");
                await _apiClient.ReportSuccessAsync(new ReportSuccessDto
                {
                    JobId = job.JobId,
                    WorkerId = threadWorkerId,
                    Result = result,
                    SolutionData = data,
                    NodesVisited = nodes,
                    LeafChecksum = checksum,
                    BestDepth = maxDepth,
                    BestBoardBase64 = bestBoard
                });
            }

            cts.Cancel();
        }
    }
}
