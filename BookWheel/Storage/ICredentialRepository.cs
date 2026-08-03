using BookWheel.Models;

namespace BookWheel.Storage;

public interface ICredentialRepository
{
    Task<bool> HasAccountAsync();
    Task<CredentialRecord> CreateInitialAccountAsync(string username, string password);
    Task<CredentialRecord?> ValidateCredentialsAsync(string username, string password);
    Task<IReadOnlyList<UserAccountSummary>> GetUsersAsync();
    Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked);
    Task<UserAccountSummary> DeleteUserAsync(Guid userId);
    Task<CredentialRecord> MarkForPasswordResetAsync(Guid userId);
    Task<string> SetPasswordAsync(Guid userId, string newPassword);
    Task<string?> GetUsernameAsync(Guid userId);
}
