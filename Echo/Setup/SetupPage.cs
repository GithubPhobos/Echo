using Echo.UI;

namespace Echo.Setup;

/// <summary>
///     One screen of the setup wizard. The wizard remembers the pages it has shown, so going back always
///     leads to the page that was on the screen before, whichever step it belongs to.
/// </summary>
/// <param name="ShowAsync">Shows the page and stores the choice in the context.</param>
/// <param name="IsNeeded">Whether the page applies to the choices made so far; always, when null.</param>
/// <param name="SubTitle">
///     The name of the page in the navigation, for pages that are not the first one of their step.
/// </param>
internal sealed record SetupPage(
    Func<SetupContext, SetupScreen, CancellationToken, Task<SetupStepResult>> ShowAsync,
    Func<SetupContext, bool>? IsNeeded = null,
    Func<string>? SubTitle = null);