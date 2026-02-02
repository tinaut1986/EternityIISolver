using EternityWorker;
using Microsoft.Extensions.Configuration;
using System.Runtime.InteropServices;

// Windows API to prevent sleep
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS | NativeMethods.EXECUTION_STATE.ES_SYSTEM_REQUIRED);
}

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .Build();

string baseUrl = configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5210";

Console.Clear();
Console.WriteLine("╔══════════════════════════════════════════════╗");
Console.WriteLine("║        Eternity II Distributed Solver        ║");
Console.WriteLine("╚══════════════════════════════════════════════╝");
Console.WriteLine($"Server: {baseUrl}");
if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    Console.WriteLine("Status: 🔋 Sleep prevention ACTIVE");
Console.WriteLine();

// Interactive Menu
string[] options = { 
    "Background Mode (Uses ~15% CPU)", 
    "Balanced Mode   (Uses ~50% CPU)", 
    "Turbo Mode      (Uses 100% CPU)",
    "Exit / Cancel" 
};
int selectedIndex = 1; // Default to Balanced
bool selectionMade = false;

Console.CursorVisible = false;

while (!selectionMade)
{
    Console.SetCursorPosition(0, 7);
    Console.WriteLine("Select processing mode (Use arrows ↑↓ and Enter):");
    
    for (int i = 0; i < options.Length; i++)
    {
        if (i == selectedIndex)
        {
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($" > {options[i]} ");
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine($"   {options[i]} ");
        }
    }

    var key = Console.ReadKey(true).Key;
    switch (key)
    {
        case ConsoleKey.UpArrow:
            selectedIndex = (selectedIndex == 0) ? options.Length - 1 : selectedIndex - 1;
            break;
        case ConsoleKey.DownArrow:
            selectedIndex = (selectedIndex == options.Length - 1) ? 0 : selectedIndex + 1;
            break;
        case ConsoleKey.Enter:
            selectionMade = true;
            break;
        case ConsoleKey.Escape:
            Console.WriteLine("\nOperation cancelled.");
            return;
    }
}

Console.CursorVisible = true;

if (selectedIndex == 3) // Exit option
{
    Console.WriteLine("\nExiting...");
    return;
}

int threads = selectedIndex switch {
    0 => Math.Max(1, Environment.ProcessorCount / 8),
    1 => Math.Max(1, Environment.ProcessorCount / 2),
    _ => Environment.ProcessorCount
};

Console.WriteLine($"\nMode selected: {options[selectedIndex]}");
Console.WriteLine($"Threads initialized: {threads}");
Console.WriteLine("------------------------------------------------");

var worker = new Worker(baseUrl) { DegreeOfParallelism = threads }; 

try
{
    await worker.RunAsync();
}
finally
{
    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
    {
        NativeMethods.SetThreadExecutionState(NativeMethods.EXECUTION_STATE.ES_CONTINUOUS);
    }
}
