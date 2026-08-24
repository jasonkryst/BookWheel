using BookWheel.Storage.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Storage.Postgres;

public sealed class BookWheelDbContext : DbContext
{
    public BookWheelDbContext(DbContextOptions<BookWheelDbContext> options) : base(options)
    {
    }

    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<BookEntity> Books => Set<BookEntity>();
    public DbSet<BookTypeEntity> BookTypes => Set<BookTypeEntity>();
    public DbSet<PasswordResetTokenEntity> PasswordResetTokens => Set<PasswordResetTokenEntity>();
    public DbSet<SpinSelectionEntity> SpinSelections => Set<SpinSelectionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Username).HasColumnType("citext").IsRequired();
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<BookEntity>(entity =>
        {
            entity.ToTable("books");
            entity.HasKey(b => b.Id);
            entity.Property(b => b.Title).IsRequired();
            entity.Property(b => b.Isbn).HasMaxLength(20);
            entity.Property(b => b.Author).HasMaxLength(300);
            entity.Property(b => b.CoverUrl).HasMaxLength(2048);
            entity.HasIndex(b => b.UserId);
            entity.HasIndex(b => b.BookTypeId);
            entity.HasQueryFilter(b => b.DeletedAtUtc == null);
        });

        modelBuilder.Entity<BookTypeEntity>(entity =>
        {
            entity.ToTable("book_types");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Name).HasMaxLength(50).IsRequired();
            entity.HasIndex(t => t.Name).IsUnique();
            entity.HasData(
                new BookTypeEntity { Id = 1, Name = "Physical" },
                new BookTypeEntity { Id = 2, Name = "Digital" },
                new BookTypeEntity { Id = 3, Name = "Nook Only" });
        });

        modelBuilder.Entity<SpinSelectionEntity>(entity =>
        {
            entity.ToTable("spin_selections");
            entity.HasKey(s => s.Id);
            entity.HasIndex(s => s.UserId);
            entity.HasIndex(s => s.BookId);
        });

        modelBuilder.Entity<PasswordResetTokenEntity>(entity =>
        {
            entity.ToTable("password_reset_tokens");
            entity.HasKey(t => t.Id);
            entity.Property(t => t.TokenHash).IsRequired();
            entity.HasIndex(t => t.TokenHash).IsUnique();
            entity.HasIndex(t => t.UserId);
        });
    }
}
