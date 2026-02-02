namespace EternityShared.Dtos;

public enum JobStatus
{
    Pending,
    Assigned,
    Completed,
    SplitParent,
    Solved
}

public class JobRequestDto
{
    public string WorkerId { get; set; } = string.Empty;
    public string Capabilities { get; set; } = string.Empty;
}

public class JobResponseDto
{
    public long JobId { get; set; }
    public string BoardPayloadBase64 { get; set; } = string.Empty; // Binary state as Base64
    public int TimeLimitSeconds { get; set; }
}

public class HeartbeatDto
{
    public long JobId { get; set; }
    public string WorkerId { get; set; } = string.Empty;
}

public class ReportSuccessDto
{
    public long JobId { get; set; }
    public string WorkerId { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty; // "NO_SOLUTION_FOUND" | "SOLUTION_FOUND"
    public string? SolutionData { get; set; } // Base64 of full board
    
    // Validation data
    public long NodesVisited { get; set; }
    public long LeafChecksum { get; set; }
    
    // Pseudo-solution tracking
    public int BestDepth { get; set; }
    public string? BestBoardBase64 { get; set; }

    public string? AdminToken { get; set; }
}

public class ReportSplitDto
{
    public long JobId { get; set; }
    public string WorkerId { get; set; } = string.Empty;
    public List<string> NewSubJobsBase64 { get; set; } = new();
    
    // Validation of the work done before split
    public long NodesVisited { get; set; }
    public long LeafChecksum { get; set; }
    
    // Best progress found during exploration
    public int BestDepth { get; set; }
    public string? BestBoardBase64 { get; set; }
}
