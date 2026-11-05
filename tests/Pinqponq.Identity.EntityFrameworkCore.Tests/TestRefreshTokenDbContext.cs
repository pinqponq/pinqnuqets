using Microsoft.EntityFrameworkCore;

namespace Pinqponq.Identity.EntityFrameworkCore.Tests;

public sealed class OwnedRefreshTokenRecord : IRefreshTokenRecord
{
    public int Id { get; set; }

    public int OwnerId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? ReplacedByTokenHash { get; set; }
}

public sealed class RenamedColumnRefreshTokenRecord : IRefreshTokenRecord
{
    public long Id { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? ReplacedByTokenHash { get; set; }
}

public sealed class TestRefreshTokenDbContext(DbContextOptions<TestRefreshTokenDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OwnedRefreshTokenRecord>(entity =>
        {
            entity.ToTable("owned_refresh_tokens");
            entity.HasIndex(record => record.TokenHash).IsUnique();
        });

        modelBuilder.Entity<RenamedColumnRefreshTokenRecord>(entity =>
        {
            entity.ToTable("renamed_column_refresh_tokens");
            entity.Property(record => record.Id).HasColumnName("token_id");
            entity.Property(record => record.Subject).HasColumnName("subject_key");
            entity.Property(record => record.TokenHash).HasColumnName("hash_value");
            entity.Property(record => record.CreatedAt).HasColumnName("created_at_utc");
            entity.Property(record => record.ExpiresAt).HasColumnName("expires_at_utc");
            entity.Property(record => record.RevokedAt).HasColumnName("revoked_at_utc");
            entity.Property(record => record.ReplacedByTokenHash).HasColumnName("replaced_by_hash_value");
            entity.HasIndex(record => record.TokenHash).IsUnique();
        });
    }
}
