namespace Echo.Listening;

/// <summary>
///     The line the user types commands into, and the commands offered while one is being typed.
/// </summary>
/// <param name="commands">The commands that can be offered.</param>
internal sealed class CommandLine(IReadOnlyList<ListenCommandInfo> commands)
{
    public const char Prefix = '/';

    private readonly IReadOnlyList<ListenCommandInfo> _commands = commands;
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    /// <summary>
    ///     The commands that start with what is typed, while the first word is being typed.
    /// </summary>
    public IReadOnlyList<PanelRow> Suggestions
    {
        get
        {
            string text = Text;

            if (text.Length == 0 || text[0] != Prefix || text.Contains(' '))
                return [];

            return
            [
                .. _commands
                    .Where(command => command.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    .Select(command => new PanelRow(command.Name, command.Name, command.Description())),
            ];
        }
    }

    /// <summary>
    ///     The command typed so far, when its name is complete.
    /// </summary>
    public ListenCommandInfo? Command(string text) =>
        _commands.FirstOrDefault(command => command.Name.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));

    public void Type(char character) => _text.Append(character);

    public void Backspace()
    {
        if (_text.Length > 0)
            _text.Length--;
    }

    public void Clear() => _text.Clear();

    public void Replace(string text)
    {
        _text.Clear();
        _text.Append(text);
    }

    /// <summary>
    ///     Takes the line away to be run.
    /// </summary>
    public string Submit()
    {
        string text = Text.Trim();

        _text.Clear();

        return text;
    }
}