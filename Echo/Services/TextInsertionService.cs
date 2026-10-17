using GregsStack.InputSimulatorStandard;
using GregsStack.InputSimulatorStandard.Native;
using TextCopy;

namespace Echo.Services;

/// <inheritdoc cref="ITextInsertionService"/>
/// <param name="textInsertionSettingsOptions"><see cref="IOptions{TextInsertionSettings}"/></param>
/// <param name="clipboard">The system clipboard.</param>
/// <param name="inputSimulator">Presses keys as if the user did.</param>
internal sealed class TextInsertionService(
    IOptions<TextInsertionSettings> textInsertionSettingsOptions,
    IClipboard clipboard,
    IInputSimulator inputSimulator) : ITextInsertionService
{
    private readonly IClipboard _clipboard = clipboard;
    private readonly IInputSimulator _inputSimulator = inputSimulator;
    private readonly TextInsertionSettings _textInsertionSettings = textInsertionSettingsOptions.Value;

    /// <inheritdoc/>
    public async Task InsertTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // The text is always left in the clipboard; without automatic insertion it is pasted by hand
        await _clipboard.SetTextAsync(text);

        if (!_textInsertionSettings.UseAutoInsert)
            return;

        await Task.Delay(50);

        _inputSimulator.Keyboard.ModifiedKeyStroke(
            VirtualKeyCode.CONTROL,
            VirtualKeyCode.VK_V);
    }
}
