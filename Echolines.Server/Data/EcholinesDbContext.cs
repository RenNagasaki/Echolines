using Echolines.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Echolines.Server.Data;

public class EcholinesDbContext(DbContextOptions<EcholinesDbContext> options) : DbContext(options)
{
    public DbSet<Line> Lines => Set<Line>();
    public DbSet<PendingLine> PendingLines => Set<PendingLine>();
    public DbSet<PendingVote> PendingVotes => Set<PendingVote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Line>(e =>
        {
            e.ToTable("lines");
            e.Property(x => x.Lang).HasMaxLength(8);
            e.Property(x => x.NpcName).HasMaxLength(128);
            e.Property(x => x.Gender).HasMaxLength(32);
            e.Property(x => x.Race).HasMaxLength(32);
            e.Property(x => x.TextHash).HasMaxLength(LineLimits.MaxHashLength);
            e.Property(x => x.Text).HasMaxLength(LineLimits.MaxTextLength);

            e.HasIndex(x => new { x.Lang, x.NpcName, x.TextHash }).IsUnique();
            e.HasIndex(x => new { x.Lang, x.NpcName });
            e.HasIndex(x => x.TextHash);
            e.HasIndex(x => x.LastSeenUtc);
            // Phase 6 ("what does the harvest not know?") reads exactly this pair.
            e.HasIndex(x => new { x.SeenLive, x.SeenHarvest });
        });

        modelBuilder.Entity<PendingLine>(e =>
        {
            e.ToTable("pending_lines");
            e.Property(x => x.Lang).HasMaxLength(8);
            e.Property(x => x.NpcName).HasMaxLength(128);
            e.Property(x => x.Gender).HasMaxLength(32);
            e.Property(x => x.Race).HasMaxLength(32);
            e.Property(x => x.TextHash).HasMaxLength(LineLimits.MaxHashLength);
            e.Property(x => x.Text).HasMaxLength(LineLimits.MaxTextLength);

            e.HasIndex(x => new { x.Lang, x.NpcName, x.TextHash }).IsUnique();
            // The admin work list is "state = pending, newest first" — and this table never shrinks,
            // so an unindexed list view becomes unusable within a year.
            e.HasIndex(x => new { x.State, x.LastSeenUtc });
            e.HasIndex(x => new { x.Lang, x.NpcName });
            e.HasIndex(x => x.TextHash);
        });

        modelBuilder.Entity<PendingVote>(e =>
        {
            e.ToTable("pending_votes");
            e.Property(x => x.VoterKey).HasMaxLength(64);

            e.HasOne(x => x.Pending)
                .WithMany(x => x.Votes)
                .HasForeignKey(x => x.PendingId)
                .OnDelete(DeleteBehavior.Cascade);

            // One vote per installation — the whole consensus rule, expressed as a constraint.
            e.HasIndex(x => new { x.PendingId, x.VoterKey }).IsUnique();
        });
    }
}
