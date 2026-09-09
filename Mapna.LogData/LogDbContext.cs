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
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(LogFieldLimit.StatusMaxLength);
            e.Property(x => x.Reason).HasMaxLength(LogFieldLimit.ReasonMaxLength);
            e.Property(x => x.ChangedFields).HasMaxLength(LogFieldLimit.ChangedFieldsMaxLength);
            e.Property(x => x.PayloadSnapshot).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.PerId, x.Status, x.OccurredAtUtc });
        });

        modelBuilder.Entity<ReceiveLogEntry>(e =>
        {
            e.ToTable("ReceiveLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(LogFieldLimit.StatusMaxLength);
            e.Property(x => x.ChangedFields).HasMaxLength(LogFieldLimit.ChangedFieldsMaxLength);
            e.Property(x => x.Reason).HasMaxLength(LogFieldLimit.ReasonMaxLength);
            e.HasIndex(x => x.PerId);
        });
    }
}           