namespace BookWheel.Services;

public static class IsbnValidator
{
    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var stripped = new string(raw.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();

        if (stripped.Length == 10 && IsValidIsbn10(stripped))
        {
            normalized = stripped;
            return true;
        }

        if (stripped.Length == 13 && IsValidIsbn13(stripped))
        {
            normalized = stripped;
            return true;
        }

        return false;
    }

    private static bool IsValidIsbn10(string isbn)
    {
        var sum = 0;
        for (var i = 0; i < 10; i++)
        {
            int digit;
            if (i == 9 && isbn[i] == 'X')
            {
                digit = 10;
            }
            else if (char.IsDigit(isbn[i]))
            {
                digit = isbn[i] - '0';
            }
            else
            {
                return false;
            }

            sum += digit * (10 - i);
        }

        return sum % 11 == 0;
    }

    private static bool IsValidIsbn13(string isbn)
    {
        var sum = 0;
        for (var i = 0; i < 13; i++)
        {
            if (!char.IsDigit(isbn[i]))
            {
                return false;
            }

            var digit = isbn[i] - '0';
            sum += digit * (i % 2 == 0 ? 1 : 3);
        }

        return sum % 10 == 0;
    }
}
