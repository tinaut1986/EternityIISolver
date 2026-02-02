using EternityWorker;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;

// Windows API to prevent sleep
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED);
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

string baseUrl = configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5210";

Console.WriteLine("Eternity II Solver Worker V2");
Console.WriteLine($"Server URL: {baseUrl}");
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    Console.WriteLine("🔋 Sleep prevention: ACTIVE");

Console.WriteLine("Select Mode: 1. Background (15%), 2. Balanced (50%), 3. Turbo (100%)");
var key = Console.ReadKey().KeyChar;
Console.WriteLine();

int threads = key switch {
    '1' => Math.Max(1, Environment.ProcessorCount / 8),
    '2' => Math.Max(1, Environment.ProcessorCount / 2),
    _ => Environment.ProcessorCount
};

var worker = new Worker(baseUrl) { DegreeOfParallelism = threads }; 

try
{
    await worker.RunAsync();
}
finally
{
    // Restore normal sleep behavior on exit
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        SetThreadExecutionState(EXECUTION_STATE.ES_CONTINUOUS);
    }
}

// Windows API declarations
[Flags]
enum EXECUTION_STATE : uint
{
    ES_CONTINUOUS = 0x80000000,
    ES_SYSTEM_REQUIRED = 0x00000001,
    ES_DISPLAY_REQUIRED = 0x00000002  // Also keeps display on, optional
}

[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
static extern EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);
