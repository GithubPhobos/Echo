namespace Echo.Services;

/// <inheritdoc cref="ITextInsertionService"/>
/// <param name="textInsertionSettingsOptions"><see cref="IOptions{TextInsertionSettings}"/></param>
/// <param name="clipboard">The system clipboard.</param>
/// <param name="pasteKeys">Presses the keys that paste, as if the user did.</param>
/// <param name="logger"><see cref="ILogger{TextInsertionService}"/></param>
internal sealed class TextInsertionService(
    IOptions<TextInsertionSettings> textInsertionSettingsOptions,
    ITextClipboard clipboard,
    IPasteKeys pasteKeys,
    ILogger<TextInsertionService> logger) : ITextInsertionService
{
    private readonly ITextClipboard _clipboard = clipboard;
    private readonly IPasteKeys _pasteKeys = pasteKeys;
    private readonly ILogger<TextInsertionService> _logger = logger;
    private readonly TextInsertionSettings _textInsertionSettings = textInsertionSettingsOptions.Value;

    /// <summary>
    ///     Everything Echo puts into the clipboard goes in one at a time: a phrase stays there from its copying until
    ///     its keys are pressed, and nothing comes in between.
    /// </summary>
    private readonly SemaphoreSlim _clipboardTurn = new(1, 1);

    /// <summary>
    ///     Without automatic insertion no key is ever pressed, so nothing is renewed.
    /// </summary>
    /// <inheritdoc/>
    public Task PrepareAsync(CancellationToken cancellationToken) =>
        _textInsertionSettings.UseAutoInsert ? _pasteKeys.PrepareAsync(cancellationToken) : Task.CompletedTask;

    /// <inheritdoc/>
    public async Task InsertTextAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        await _clipboardTurn.WaitAsync();

        try
        {
            // The text is always left in the clipboard; without automatic insertion it is pasted by hand
            await _clipboard.SetTextAsync(text);

            if (!_textInsertionSettings.UseAutoInsert)
                return;

            await Task.Delay(50);

            if (!await _pasteKeys.PasteAsync())
            {
                _logger.LogWarning("{Tab}{TextEmoji} Copied, paste it yourself: the system does not let Echo press keys.",
                    LoggerConstants.Tab, LoggerConstants.TextEmoji);
            }
        }
        finally
        {
            _clipboardTurn.Release();
        }
    }

    /// <inheritdoc/>
    public async Task CopyAsync(string text)
    {
        await _clipboardTurn.WaitAsync();

        try
        {
            await _clipboard.SetTextAsync(text);
        }
        finally
        {
            _clipboardTurn.Release();
        }
    }
}