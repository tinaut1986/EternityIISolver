using EternityServer.Models;
using Microsoft.EntityFrameworkCore;

namespace EternityServer.Data;

public class EternityDbContext : DbContext
{
    public EternityDbContext(DbContextOptions<EternityDbContext> options) : base(options) { }

    public DbSet<Job> Jobs { get; set; } = null!;
    public DbSet<Solution> Solutions { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>().HasIndex(j => j.Status);
        modelBuilder.Entity<Job>().HasIndex(j => j.AssignedWorkerId);
    }
}
