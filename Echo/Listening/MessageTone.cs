namespace Echo.Listening;

/// <summary>
///     What an ordinary message says about how things went, as the picture it was written with told.
/// </summary>
internal enum MessageTone
{
    /// <summary>Nothing in particular.</summary>
    Plain,

    /// <summary>The name of a group of steps, such as "Audio:".</summary>
    Heading,

    /// <summary>A step that is under way.</summary>
    Progress,

    /// <summary>A step that succeeded.</summary>
    Done,

    /// <summary>A step that did not.</summary>
    Failed,

    /// <summary>The push-to-talk key was pressed or released.</summary>
    Key,

    /// <summary>Echo itself starting or being ready.</summary>
    Title,

    /// <summary>What the recognition library says about itself.</summary>
    Library
}