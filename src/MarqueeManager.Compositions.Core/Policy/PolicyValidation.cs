namespace MarqueeManager.Compositions.Core.Policy;

public static class PolicyLimits
{
    // §20.1: maxCrop is a decimal in [0.0, 0.60]. Out of range invalidates the
    // field — it is NEVER silently clamped.
    public const double MaxCropMin = 0.0;
    public const double MaxCropMax = 0.60;
}

/// <summary>A stable, non-localized policy validation problem (the Setup translates it).</summary>
public sealed record PolicyValidationError(string Path, string Code, string? Detail = null)
{
    public const string MaxCropOutOfRange = "policy.maxcrop_out_of_range";
}

public static class PolicyValidation
{
    private const double Epsilon = 1e-9;

    public static bool IsValidMaxCrop(double value)
        => value >= PolicyLimits.MaxCropMin - Epsilon && value <= PolicyLimits.MaxCropMax + Epsilon;

    /// <summary>Reports every out-of-range terminal in an effective policy. Empty = valid.</summary>
    public static IReadOnlyList<PolicyValidationError> Validate(ScopePolicy policy)
    {
        var errors = new List<PolicyValidationError>();
        foreach (var (kind, settings) in policy.Sources)
        {
            if (settings.Fit is { } fit && !IsValidMaxCrop(fit.MaxCrop))
                errors.Add(new PolicyValidationError(
                    $"sources.{kind.ToString().ToLowerInvariant()}.fit.maxCrop",
                    PolicyValidationError.MaxCropOutOfRange,
                    fit.MaxCrop.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)));
        }
        return errors;
    }
}
