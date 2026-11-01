namespace Echo.Listening;

/// <summary>
///     What the state line says about the settings Echo runs with.
/// </summary>
/// <param name="Model">The short name of the model.</param>
/// <param name="Key">The push-to-talk key, the way a person names it, with short names of the modifiers.</param>
/// <param name="SilenceLevel">The silence level: what is quieter is not recorded, and moves no arc.</param>
internal sealed record ListenInfo(string Model, string Key, float SilenceLevel = 0.01f);