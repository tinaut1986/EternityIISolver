using System.Net.Http.Json;
using EternityShared.Dtos;

namespace EternityWorker.Services;

public class ApiClient
{
    private readonly HttpClient _httpClient;

    public ApiClient(string baseUrl)
    {
        _httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public async Task<JobResponseDto?> RequestJobAsync(string workerId)
    {
        var response = await _httpClient.PostAsJsonAsync("api/jobs/request", new JobRequestDto { WorkerId = workerId });
        if (response.IsSuccessStatusCode)
        {
            return await response.Content.ReadFromJsonAsync<JobResponseDto>();
        }
        return null;
    }

    public async Task SendHeartbeatAsync(long jobId, string workerId)
    {
        await _httpClient.PostAsJsonAsync("api/jobs/heartbeat", new HeartbeatDto { JobId = jobId, WorkerId = workerId });
    }

    public async Task ReportSuccessAsync(ReportSuccessDto report)
    {
        await _httpClient.PostAsJsonAsync("api/jobs/report-success", report);
    }

    public async Task ReportSplitAsync(ReportSplitDto report)
    {
        await _httpClient.PostAsJsonAsync("api/jobs/report-split", report);
    }
}
