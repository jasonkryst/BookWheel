using Microsoft.Extensions.Localization;

namespace BookWheel.Services;

public sealed class ApiMessageLocalizer(IStringLocalizer<SharedErrors> localizer)
{
	private static readonly Dictionary<string, string> KeysByEnglishMessage = new(StringComparer.Ordinal)
	{
		["Invalid username or password."] = "InvalidCredentials",
		["The password reset link is invalid or has expired."] = "LinkInvalidOrExpired",
		["Book title is required."] = "BookTitleRequired",
		["A valid token and password are required."] = "TokenAndPasswordRequired",
		["Password reset token data is corrupted and has been quarantined. Restore App_Data from backup."] = "ResetTokenDataCorrupted",
		["An account already exists."] = "AccountAlreadyExists",
		["Username and password are required."] = "UsernameAndPasswordRequired",
		["Create the initial account first."] = "CreateInitialAccountFirst",
		["Username is required."] = "UsernameRequired",
		["Username already exists."] = "UsernameAlreadyExists",
		["User not found."] = "UserNotFound",
		["At least one administrator account is required."] = "AtLeastOneAdminRequired",
		["The first account cannot be removed."] = "FirstAccountCannotBeRemoved",
		["User not found for this reset link."] = "UserNotFoundForResetLink",
		["Credential data is corrupted and has been quarantined. Restore App_Data from backup."] = "CredentialDataCorrupted",
		["Book not found."] = "BookNotFound",
		["No books are available in the wheel."] = "NoBooksAvailable",
		["Book data is corrupted and has been quarantined. Restore App_Data from backup."] = "BookDataCorrupted",
		["Administrators can only update other user accounts."] = "AdminsCanOnlyUpdateOthers",
		["Administrators can only generate reset links for other user accounts."] = "AdminsCanOnlyGenerateResetLinksForOthers",
		["Administrators can only remove other user accounts."] = "AdminsCanOnlyRemoveOthers",
		["The provided ISBN is not valid."] = "InvalidIsbn",
		["Provide an ISBN or a title to look up."] = "IsbnOrTitleRequired",
		["No book metadata found for that ISBN."] = "BookMetadataNotFoundByIsbn",
		["No book metadata found for that title."] = "BookMetadataNotFoundByTitle",
	};

	public static IReadOnlyDictionary<string, string> KnownMessageKeys => KeysByEnglishMessage;

	public string Localize(string englishMessage)
	{
		if (!KeysByEnglishMessage.TryGetValue(englishMessage, out var key))
		{
			return englishMessage;
		}

		var result = localizer[key];
		return result.ResourceNotFound ? englishMessage : result.Value;
	}
}
