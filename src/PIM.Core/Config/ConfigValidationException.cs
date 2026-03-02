namespace PIM.Core.Config;

public sealed class ConfigValidationException : Exception
{
    public List<string> Errors { get; }

    public ConfigValidationException(List<string> errors)
        : base($"Configuration validation failed:\n{string.Join("\n", errors.Select(e => $"  - {e}"))}")
    {
        Errors = errors;
    }
}
