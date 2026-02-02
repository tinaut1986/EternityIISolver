using System.ComponentModel.DataAnnotations;
using EternityShared.Dtos;

namespace EternityServer.Models;

public class Job
{
    public long Id { get; set; }
    
    [MaxLength(1024)]
    public byte[] BoardPayload { get; set; } = Array.Empty<byte>();
    
    public JobStatus Status { get; set; }
    public string? AssignedWorkerId { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public int ExecutionTimeMs { get; set; }
    public int ValidationCount { get; set; }
    public long? ParentJobId { get; set; }

    // Validation V2 (Tracking)
    public string? FirstWorkerId { get; set; }
    public string? SecondWorkerId { get; set; }
    public long NodesVisited { get; set; }
    public long LeafChecksum { get; set; }
    public bool IsVerified { get; set; }

    // Best progress found in this branch
    public int MaxDepthFound { get; set; }
    public byte[]? BestBoardState { get; set; }
}
