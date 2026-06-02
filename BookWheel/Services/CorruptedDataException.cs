namespace BookWheel.Services;

public sealed class CorruptedDataException : Exception
{
    public CorruptedDataException(string message)
        : base(message)
    {
    }
}
