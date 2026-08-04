using System.Globalization;
using BookWheel.Services;
using Microsoft.Extensions.Localization;

namespace BookWheel.Tests.Services;

public sealed class ApiMessageLocalizerTests
{
	private static ApiMessageLocalizer CreateLocalizer()
	{
		var factory = new ResourceManagerStringLocalizerFactory(
			Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()),
			Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
		var localizer = new StringLocalizer<SharedErrors>(factory);
		return new ApiMessageLocalizer(localizer);
	}

	[Theory]
	[InlineData("es", "El título del libro es obligatorio.")]
	[InlineData("pl", "Tytuł książki jest wymagany.")]
	public void Localize_TranslatesKnownMessage_ForSupportedCulture(string culture, string expected)
	{
		var originalCulture = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = new CultureInfo(culture);
			var localizer = CreateLocalizer();

			// Positive: a known English message is translated for a supported culture.
			var result = localizer.Localize("Book title is required.");

			Assert.Equal(expected, result);
		}
		finally
		{
			CultureInfo.CurrentUICulture = originalCulture;
		}
	}

	[Fact]
	public void Localize_ReturnsOriginalEnglish_ForEnglishCulture()
	{
		var originalCulture = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = new CultureInfo("en");
			var localizer = CreateLocalizer();

			var result = localizer.Localize("Book title is required.");

			Assert.Equal("Book title is required.", result);
		}
		finally
		{
			CultureInfo.CurrentUICulture = originalCulture;
		}
	}

	[Fact]
	public void Localize_ReturnsMessageUnchanged_WhenMessageIsNotCatalogued()
	{
		var originalCulture = CultureInfo.CurrentUICulture;
		try
		{
			CultureInfo.CurrentUICulture = new CultureInfo("es");
			var localizer = CreateLocalizer();

			// Negative: an unmapped message (e.g. a corrupted-data exception text
			// not yet catalogued) passes through unchanged rather than throwing
			// or returning an empty/garbled string.
			var result = localizer.Localize("Some future exception message nobody catalogued yet.");

			Assert.Equal("Some future exception message nobody catalogued yet.", result);
		}
		finally
		{
			CultureInfo.CurrentUICulture = originalCulture;
		}
	}

	public static IEnumerable<object[]> KnownMessages() =>
		ApiMessageLocalizer.KnownMessageKeys.Keys.Select(message => new object[] { message });

	[Theory]
	[MemberData(nameof(KnownMessages))]
	public void Localize_HasNonEmptyTranslation_ForEverySupportedCulture(string englishMessage)
	{
		var originalCulture = CultureInfo.CurrentUICulture;
		try
		{
			foreach (var culture in new[] { "es", "pl" })
			{
				CultureInfo.CurrentUICulture = new CultureInfo(culture);
				var localizer = CreateLocalizer();

				// Positive (coverage check): every dictionary entry has a real,
				// non-English translation in both supported non-default cultures,
				// catching drift when a new message is catalogued without resx entries.
				var result = localizer.Localize(englishMessage);

				Assert.False(string.IsNullOrWhiteSpace(result));
				Assert.NotEqual(englishMessage, result);
			}
		}
		finally
		{
			CultureInfo.CurrentUICulture = originalCulture;
		}
	}
}
