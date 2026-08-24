using BookWheel.Storage.Postgres;
using Microsoft.EntityFrameworkCore;

namespace BookWheel.Tests.Storage.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresBookTypeTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private BookWheelDbContext _context = null!;

    public PostgresBookTypeTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        _context = _fixture.CreateContext();
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
    }

    // Positive: seed data exists after migration

    [Fact]
    public async Task BookTypes_Table_Has_Exactly_Three_Seeded_Types()
    {
        var types = await _context.BookTypes.ToListAsync();

        Assert.Equal(3, types.Count);
    }

    [Fact]
    public async Task Physical_Type_Has_Id_1_And_Correct_Name()
    {
        var type = await _context.BookTypes.FindAsync(1);

        Assert.NotNull(type);
        Assert.Equal(1, type!.Id);
        Assert.Equal("Physical", type.Name);
    }

    [Fact]
    public async Task Digital_Type_Has_Id_2_And_Correct_Name()
    {
        var type = await _context.BookTypes.FindAsync(2);

        Assert.NotNull(type);
        Assert.Equal(2, type!.Id);
        Assert.Equal("Digital", type.Name);
    }

    [Fact]
    public async Task NookOnly_Type_Has_Id_3_And_Correct_Name()
    {
        var type = await _context.BookTypes.FindAsync(3);

        Assert.NotNull(type);
        Assert.Equal(3, type!.Id);
        Assert.Equal("Nook Only", type.Name);
    }

    [Fact]
    public async Task BookType_Names_Are_Unique()
    {
        var types = await _context.BookTypes.ToListAsync();
        var names = types.Select(t => t.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    // Negative: book_types seed data is not affected by the books table reset

    [Fact]
    public async Task BookTypes_Persist_After_Books_Table_Is_Truncated()
    {
        await _fixture.ResetAsync();

        var types = await _context.BookTypes.ToListAsync();

        Assert.Equal(3, types.Count);
    }
}
