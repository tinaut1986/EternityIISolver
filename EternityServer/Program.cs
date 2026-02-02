using EternityServer.Data;
using EternityServer.Models;
using EternityServer.Services;
using EternityShared.Game;
using EternityShared.Dtos;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var serverVersion = new MySqlServerVersion(new Version(8, 0, 0));

builder.Services.AddDbContext<EternityDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHostedService<ZombieJobCleaner>();

var app = builder.Build();

// Seed database with initial job if empty
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<EternityDbContext>();
    context.Database.Migrate();

    if (!context.Jobs.Any())
    {
        Console.WriteLine("--- Database Seeding ---");
        string[] possiblePaths = { "Data", "../Data", "/app/Data" };
        string piecesPath = "", hintsPath = "";

        foreach (var p in possiblePaths) {
            var pp = Path.Combine(p, "eternity2_256.csv");
            var hp = Path.Combine(p, "eternity2_256_all_hints.csv");
            if (File.Exists(pp)) { piecesPath = pp; hintsPath = hp; break; }
        }

        if (string.IsNullOrEmpty(piecesPath)) {
            Console.WriteLine("CRITICAL ERROR: CSV files not found in any expected directory!");
        } else {
            Console.WriteLine($"Loading data from: {piecesPath}");
            var pieces = PieceLoader.LoadFromCsv(piecesPath); 
            var hints = HintLoader.LoadFromCsv(hintsPath);
            var masterPieces = pieces.ToDictionary(p => p.Id);
            
            var board = new Board(masterPieces);
            foreach (var hint in hints)
            {
                if (masterPieces.ContainsKey(hint.PieceId)) {
                    var piece = masterPieces[hint.PieceId].Rotate(hint.Rotation);
                    board.TryPlace(hint.Row, hint.Col, piece, (byte)hint.Rotation);
                }
            }

            context.Jobs.Add(new Job
            {
                BoardPayload = BoardBinarySerializer.Serialize(board),
                Status = JobStatus.Pending,
                ValidationCount = 0,
                MaxDepthFound = hints.Count 
            });
            context.SaveChanges();
            Console.WriteLine($"Database seeded with root job containing {hints.Count} hints.");
        }
    }
}

// Configure the HTTP request pipeline.
app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
