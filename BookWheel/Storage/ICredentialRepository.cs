using BookWheel.Models;

namespace BookWheel.Storage;

public interface ICredentialRepository
{
    Task<bool> HasAccountAsync();
    Task<CredentialRecord> CreateInitialAccountAsync(string username, string password, string email);
    Task<CredentialRecord?> ValidateCredentialsAsync(string username, string password);
    Task<IReadOnlyList<UserAccountSummary>> GetUsersAsync();
    Task<UserAccountSummary> CreateUserAsync(string username, bool isAdmin, string email);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin);
    Task<UserAccountSummary> UpdateUserAsync(Guid userId, string username, bool isAdmin, bool isDisabled, bool forcePasswordReset, bool isLocked, string? email);
    Task<UserAccountSummary> DeleteUserAsync(Guid userId);
    Task<CredentialRecord> MarkForPasswordResetAsync(Guid userId);
    Task<string> SetPasswordAsync(Guid userId, string newPassword);
    Task<string?> GetUsernameAsync(Guid userId);
    Task<CredentialRecord?> FindByUsernameAsync(string username);
    Task<string?> FindUsernameByEmailAsync(string email);
}
