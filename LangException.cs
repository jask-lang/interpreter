namespace JaskLang;

public class LangException : Exception
{
    public int Line { get; }
    public string? FilePath { get; }

    public LangException(string message)
        : base(message)
    {
        Line = 0;
    }

    public LangException(string message, int line, string? filePath = null)
        : base(FormatMessage(message, line, filePath))
    {
        Line = line;
        FilePath = filePath;
    }

    private static string FormatMessage(string message, int line, string? filePath)
    {
        string location = line > 0 ? $"{line}" : "";

        if (string.IsNullOrWhiteSpace(filePath) == false)
        {
            string normalizedPath = filePath.Replace("\\", "/");
            return string.IsNullOrWhiteSpace(location)
                ? $"[{normalizedPath}] {message}"
                : $"[{normalizedPath}:{location}] {message}";
        }

        return string.IsNullOrWhiteSpace(location) ? message : $"{location} {message}";
    }
}
