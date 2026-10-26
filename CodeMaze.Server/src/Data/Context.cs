using CodeMaze.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CodeMaze.Server.Data;

public class CodeMazeDbContext(DbContextOptions<CodeMazeDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Score> Scores => Set<Score>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Player>(e =>
        {
            e.HasIndex(p => p.NameNormalized).IsUnique();
            e.Property(p => p.NameNormalized).HasMaxLength(32);
            e.Property(p => p.DisplayName).HasMaxLength(32);
        });

        b.Entity<Score>(e =>
        {
            e.HasOne(s => s.Player).WithMany(p => p.Scores).HasForeignKey(s => s.PlayerId);
            e.HasIndex(s => new { s.PlayerId });
        });
    }
}