namespace Echo.Setup;

/// <summary>
///     How an additional setting is shown and edited.
/// </summary>
internal enum SettingKind
{
    /// <summary>On or off.</summary>
    Switch,

    /// <summary>A whole number within a range.</summary>
    Whole,

    /// <summary>A number with a fractional part within a range.</summary>
    Fraction,

    /// <summary>A share between 0 and 1, shown and typed as whole percent.</summary>
    Percent,

    /// <summary>Free text.</summary>
    Text,

    /// <summary>One of a fixed set of values.</summary>
    Choice
}