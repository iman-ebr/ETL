using Microsoft.EntityFrameworkCore;

namespace Mapna.LogData;

public class LogDbContext : DbContext
{
    public LogDbContext(DbContextOptions<LogDbContext> options) : base(options)
    {
        
    }


    public DbSet<Personnel> Personnel { get; set; }
    public DbSet<SendLogEntry> SendLogs { get; set; }
    public DbSet<ReceiveLogEntry> ReceiveLogs { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Personnel>(p =>
        {
            p.ToTable("Personnel");
            p.HasKey(x => x.Id);
            p.HasIndex(x => x.PerId).IsUnique();
        });
        modelBuilder.Entity<SendLogEntry>(e =>
        {
            e.ToTable("SendLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.ChangedFields).HasMaxLength(500);
            e.Property(x => x.PayloadSnapshot).HasColumnType("nvarchar(max)");
            e.HasIndex(x => x.PerId);
        });

        modelBuilder.Entity<ReceiveLogEntry>(e =>
        {
            e.ToTable("ReceiveLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.ChangedFields).HasMaxLength(500);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasIndex(x => x.PerId);
        });



    }
}