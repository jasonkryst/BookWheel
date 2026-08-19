using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BookWheel.Storage.Postgres;

public sealed class BookWheelDbContextFactory : IDesignTimeDbContextFactory<BookWheelDbContext>
{
    public BookWheelDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BookWheelDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=bookwheel;Username=bookwheel;Password=design-time-only");
        return new BookWheelDbContext(optionsBuilder.Options);
    }
}
