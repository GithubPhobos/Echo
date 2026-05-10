using GregsStack.InputSimulatorStandard;
using GregsStack.InputSimulatorStandard.Native;
using TextCopy;

namespace Echo.Services;

/// <inheritdoc cref="ITextInsertionService"/>
/// <param name="textInsertionSettingsOptions"><see cref="IOptions{TextInsertionSettings}"/></param>
internal sealed class TextInsertionService(
    IOptions<TextInsertionSettings> textInsertionSettingsOptions) : ITextInsertionService
{
    private readonly InputSimulator _inputSimulator = new();
    private readonly TextInsertionSettings _textInsertionSettings = textInsertionSettingsOptions.Value;

    /// <inheritdoc/>
    public async Task InsertTextAsync(string? text)
    {
        if (!_textInsertionSettings.UseAutoInsert)
            return;

        if (string.IsNullOrWhiteSpace(text))
            return;

        ClipboardService.SetText(text);

        await Task.Delay(50);

        _inputSimulator.Keyboard.ModifiedKeyStroke(
            VirtualKeyCode.CONTROL,
            VirtualKeyCode.VK_V);
    }
}
