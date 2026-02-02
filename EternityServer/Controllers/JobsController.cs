using EternityServer.Data;
using EternityServer.Models;
using EternityShared.Dtos;
using EternityShared.Game;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EternityServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly EternityDbContext _context;
    private readonly ILogger<JobsController> _logger;
    private static readonly System.Threading.SemaphoreSlim _lock = new(1, 1);

    public JobsController(EternityDbContext context, ILogger<JobsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost("request")]
    public async Task<ActionResult<JobResponseDto>> RequestJob([FromBody] JobRequestDto request)
    {
        await _lock.WaitAsync();
        try
        {
            // V2: Priority to PENDING. 
            var job = await _context.Jobs
                .Where(j => j.Status == JobStatus.Pending && !j.IsVerified)
                .OrderBy(j => j.Id)
                .FirstOrDefaultAsync();

            if (job == null) return NotFound("No pending jobs available.");

            job.Status = JobStatus.Assigned;
            job.AssignedWorkerId = request.WorkerId;
            job.LastHeartbeat = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // Dynamic time limit based on workload
            int pendingJobCount = await _context.Jobs.CountAsync(j => j.Status == JobStatus.Pending);
            int timeLimitSeconds;
            if (pendingJobCount < 4)
            {
                timeLimitSeconds = 60; // Few jobs: split quickly to create more parallelism
            }
            else if (pendingJobCount < 20)
            {
                timeLimitSeconds = 300; // Moderate: 5 minutes
            }
            else
            {
                timeLimitSeconds = 600; // Plenty of work: 10 minutes for deep exploration
            }

            return Ok(new JobResponseDto
            {
                JobId = job.Id,
                BoardPayloadBase64 = Convert.ToBase64String(job.BoardPayload),
                TimeLimitSeconds = timeLimitSeconds
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatDto heartbeat)
    {
        var job = await _context.Jobs.FindAsync(heartbeat.JobId);
        if (job == null || job.AssignedWorkerId != heartbeat.WorkerId) return BadRequest();

        job.LastHeartbeat = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("report-success")]
    public async Task<IActionResult> ReportSuccess([FromBody] ReportSuccessDto report)
    {
        _logger.LogInformation("[ReportSuccess] Job {JobId}, Worker {WorkerId}, Result: {Result}, Nodes: {Nodes}, BestDepth: {Depth}", 
            report.JobId, report.WorkerId, report.Result, report.NodesVisited, report.BestDepth);

        var job = await _context.Jobs.FindAsync(report.JobId);
        if (job == null) return NotFound();

        // Admin Token Bypass
        bool isAdmin = report.AdminToken == "YOUR_SECURE_ADMIN_TOKEN"; // Should be in config

        if (report.Result == "SOLUTION_FOUND")
        {
            job.Status = JobStatus.Solved;
            job.IsVerified = isAdmin;
            _context.Solutions.Add(new Solution
            {
                JobId = job.Id,
                FullBoardState = Convert.FromBase64String(report.SolutionData ?? ""),
                FoundByWorker = report.WorkerId,
                Verified = isAdmin
            });
        }
        else
        {
            // Update job stats with the best depth found by this worker
            if (report.BestDepth > job.MaxDepthFound) 
            {
                job.MaxDepthFound = report.BestDepth;
                job.BestBoardState = Convert.FromBase64String(report.BestBoardBase64 ?? "");
            }

            // V2: Validation Logic (Quorum)
            if (isAdmin)
            {
                job.Status = JobStatus.Completed;
                job.IsVerified = true;
                job.ValidationCount = 2;
            }
            else
            {
                // Cross-verify with existing data if any
                if (job.ValidationCount > 0)
                {
                    if (job.NodesVisited == report.NodesVisited && job.LeafChecksum == report.LeafChecksum)
                    {
                        job.ValidationCount++;
                        job.SecondWorkerId = report.WorkerId; // Record the second validator
                        if (job.ValidationCount >= 2)
                        {
                            job.Status = JobStatus.Completed;
                            job.IsVerified = true;
                        }
                    }
                    else
                    {
                        // Conflict! Reset and wait for 3rd opinion or admin
                        job.ValidationCount = 0;
                        job.Status = JobStatus.Pending;
                        job.AssignedWorkerId = null;
                        job.FirstWorkerId = null; // Clear history on conflict to start over
                        job.SecondWorkerId = null;
                        _logger.LogWarning("Conflict detected on Job {JobId} between {W1} and {W2}", job.Id, job.FirstWorkerId, report.WorkerId);
                    }
                }
                else
                {
                    // First report
                    job.NodesVisited = report.NodesVisited;
                    job.LeafChecksum = report.LeafChecksum;
                    job.ValidationCount = 1;
                    job.FirstWorkerId = report.WorkerId; // Record the first validator
                    job.Status = JobStatus.Pending;
                    job.AssignedWorkerId = null;
                }
            }
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("report-split")]
    public async Task<IActionResult> ReportSplit([FromBody] ReportSplitDto report)
    {
        _logger.LogInformation("[ReportSplit] Job {JobId}, Worker {WorkerId}, Subjobs: {Count}", 
            report.JobId, report.WorkerId, report.NewSubJobsBase64?.Count ?? 0);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var parentJob = await _context.Jobs.FindAsync(report.JobId);
            if (parentJob == null) return NotFound();

            // First-write-wins protection
            if (parentJob.Status != JobStatus.Assigned || parentJob.AssignedWorkerId != report.WorkerId)
            {
                return Conflict("Job already processed or assigned elsewhere.");
            }

            parentJob.Status = JobStatus.SplitParent;
            parentJob.NodesVisited = report.NodesVisited;
            parentJob.LeafChecksum = report.LeafChecksum;
            
            // Save best progress found during exploration
            if (report.BestDepth > parentJob.MaxDepthFound)
            {
                parentJob.MaxDepthFound = report.BestDepth;
                if (!string.IsNullOrEmpty(report.BestBoardBase64))
                {
                    parentJob.BestBoardState = Convert.FromBase64String(report.BestBoardBase64);
                }
            }

            foreach (var base64 in report.NewSubJobsBase64)
            {
                _context.Jobs.Add(new Job
                {
                    BoardPayload = Convert.FromBase64String(base64),
                    Status = JobStatus.Pending,
                    ParentJobId = parentJob.Id
                });
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return Ok();
        }
        catch
        {
            await transaction.RollbackAsync();
            return StatusCode(500);
        }
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var totalJobs = await _context.Jobs.CountAsync();
        var pendingJobs = await _context.Jobs.CountAsync(j => j.Status == JobStatus.Pending);
        var assignedJobs = await _context.Jobs.CountAsync(j => j.Status == JobStatus.Assigned);
        var completedJobs = await _context.Jobs.CountAsync(j => j.Status == JobStatus.Completed);
        var splitParentJobs = await _context.Jobs.CountAsync(j => j.Status == JobStatus.SplitParent);
        var solvedJobs = await _context.Jobs.CountAsync(j => j.Status == JobStatus.Solved);
        
        // Sum all nodes visited across all jobs
        var totalNodesVisited = await _context.Jobs.SumAsync(j => j.NodesVisited);
        
        var bestJob = await _context.Jobs
            .Where(j => j.MaxDepthFound > 0)
            .OrderByDescending(j => j.MaxDepthFound)
            .FirstOrDefaultAsync() 
            ?? await _context.Jobs.OrderBy(j => j.Id).FirstOrDefaultAsync();

        // Get active workers (unique worker IDs with assigned jobs)
        var activeWorkers = await _context.Jobs
            .Where(j => j.Status == JobStatus.Assigned)
            .Select(j => j.AssignedWorkerId)
            .Distinct()
            .CountAsync();

        return Ok(new
        {
            // Job counts by status
            TotalJobs = totalJobs,
            PendingJobs = pendingJobs,
            AssignedJobs = assignedJobs,
            CompletedJobs = completedJobs,
            SplitParentJobs = splitParentJobs,
            SolvedJobs = solvedJobs,
            
            // Progress metrics
            TotalNodesVisited = totalNodesVisited,
            BestDepth = bestJob?.MaxDepthFound ?? 0,
            
            // Workers
            ActiveWorkers = activeWorkers,
            
            // Best board
            BestBoardBase64 = bestJob?.BestBoardState != null 
                ? Convert.ToBase64String(bestJob.BestBoardState) 
                : (bestJob?.BoardPayload != null ? Convert.ToBase64String(bestJob.BoardPayload) : null)
        });
    }

    [HttpGet("pieces")]
    public IActionResult GetPieces()
    {
        var pieces = PieceLoader.LoadFromCsv("../Data/eternity2_256.csv");
        return Ok(pieces);
    }
}
