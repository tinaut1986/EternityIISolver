namespace EternityServer.Models;

public class Solution
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public byte[] FullBoardState { get; set; } = Array.Empty<byte>();
    public string FoundByWorker { get; set; } = string.Empty;
    public bool Verified { get; set; }
}
