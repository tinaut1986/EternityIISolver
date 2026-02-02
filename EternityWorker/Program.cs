using EternityWorker;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

string baseUrl = configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5210";

Console.WriteLine("Eternity II Solver Worker V2");
Console.WriteLine($"Server URL: {baseUrl}");
Console.WriteLine("Select Mode: 1. Background (15%), 2. Balanced (50%), 3. Turbo (100%)");
var key = Console.ReadKey().KeyChar;
Console.WriteLine(); // New line after key press

int threads = key switch {
    '1' => Math.Max(1, Environment.ProcessorCount / 8),
    '2' => Math.Max(1, Environment.ProcessorCount / 2),
    _ => Environment.ProcessorCount
};

var worker = new Worker(baseUrl) { DegreeOfParallelism = threads }; 
await worker.RunAsync();
