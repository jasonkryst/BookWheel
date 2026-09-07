using BookWheel.Models;

namespace BookWheel.Storage;

public interface IUserPreferencesRepository
{
    Task<UserPreferences> GetAsync(Guid userId);
    Task<UserPreferences> UpdateAsync(Guid userId, string? theme, bool analyticsConsentOptedOut, int? preferredBookInfoProviderId);
}
