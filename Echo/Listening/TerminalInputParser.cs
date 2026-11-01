using System.Globalization;

namespace Echo.Listening;

/// <summary>Decodes UTF-8 and terminal sequences before the key-only API can confuse focus with Page Up or F3.</summary>
/// <param name="reports">The session's focus reports.</param>
/// <param name="time">The clock used to bound ambiguous Escape and control-sequence prefixes.</param>
internal sealed class TerminalInputParser(TerminalFocusReports reports, TimeProvider time)
{
    private readonly StringBuilder _pending = new();
    private readonly Decoder _utf8 = Encoding.UTF8.GetDecoder();
    private readonly Queue<ConsoleKeyInfo> _recovered = new();
    private long _escapeAt;
    private static readonly TimeSpan _escapeDelay = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan _sequenceDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Retains incomplete UTF-8 characters and sequences for the next read.</summary>
    internal void Feed(ReadOnlySpan<byte> bytes)
    {
        // Expire before appending: a later command must not complete an abandoned prefix,
        // even if the caller has not polled ReadKey during the pause.
        RecoverExpiredPrefix();
        Span<char> chars = stackalloc char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
        int count = _utf8.GetChars(bytes, chars, flush: false);
        if (_pending.Length == 0)
            _escapeAt = time.GetTimestamp();
        _pending.Append(chars[..count]);
    }

    /// <summary>Consumes focus reports and returns a real key, without blocking on fragmented sequences.</summary>
    internal ConsoleKeyInfo? ReadKey()
    {
        RecoverExpiredPrefix();
        if (_recovered.TryDequeue(out ConsoleKeyInfo recovered))
            return recovered;
        while (_pending.Length > 0)
        {
            if (_pending[0] != '\u001b')
                return TakeCharacter(1, alt: false);
            if (_pending.Length == 1)
                return null;
            if (_pending[1] is not ('[' or 'O'))
                return TakeCharacter(2, alt: true);
            if (!TrySequenceEnd(out int end))
            {
                if (end == _pending.Length)
                {
                    if (_pending.Length <= 64)
                        return null;
                }
                // Discard only the invalid prefix, preserving the first unrelated character.
                Take(end);
                continue;
            }
            char final = _pending[end];
            string arguments = _pending.ToString(2, end - 2);
            bool csi = _pending[1] == '[';
            Take(end + 1);
            if (csi && arguments.Length == 0 && final is 'I' or 'O')
            {
                reports.Report(final == 'I');
                continue;
            }
            ConsoleKeyInfo? key = Sequence(final, arguments, csi);
            if (key is not null)
                return key;
            // Unknown control replies never become text in the command line.
        }
        return null;
    }

    /// <summary>Returns a final-byte position, or the boundary of an incomplete/invalid prefix.</summary>
    private bool TrySequenceEnd(out int end)
    {
        end = 2;
        if (_pending[1] == '[')
        {
            while (end < _pending.Length && _pending[end] is >= '0' and <= '?')
                end++;
            while (end < _pending.Length && _pending[end] is >= ' ' and <= '/')
                end++;
        }
        return end < _pending.Length && _pending[end] is >= '@' and <= '~';
    }

    /// <summary>Resolves Alt+[ / Alt+O, or drops an abandoned parameter prefix without consuming later input.</summary>
    private void RecoverExpiredPrefix()
    {
        if (_pending.Length == 0 || _pending[0] != '\u001b')
            return;
        TimeSpan wait = _pending.Length == 1 ? _escapeDelay : _sequenceDelay;
        if (time.GetElapsedTime(_escapeAt) < wait)
            return;
        if (_pending.Length == 1)
        {
            _recovered.Enqueue(new ConsoleKeyInfo('\u001b', ConsoleKey.Escape, false, false, false));
            Take(1);
            return;
        }
        if (_pending[1] is not ('[' or 'O') || TrySequenceEnd(out int end) || end != _pending.Length)
            return;
        if (_pending.Length == 2)
            _recovered.Enqueue(Character(_pending[1], alt: true));
        Take(_pending.Length);
    }

    private ConsoleKeyInfo TakeCharacter(int count, bool alt)
    {
        char character = _pending[count - 1];
        Take(count);
        return Character(character, alt);
    }

    private void Take(int count)
    {
        _pending.Remove(0, count);
        _escapeAt = time.GetTimestamp();
    }

    private static ConsoleKeyInfo Character(char c, bool alt = false)
    {
        ConsoleKey key = c switch
        {
            '\r' or '\n' => ConsoleKey.Enter, '\t' => ConsoleKey.Tab,
            '\b' or '\u007f' => ConsoleKey.Backspace, ' ' => ConsoleKey.Spacebar,
            >= '\u0001' and <= '\u001a' => (ConsoleKey)((int)ConsoleKey.A + c - 1),
            >= 'a' and <= 'z' => (ConsoleKey)((int)ConsoleKey.A + c - 'a'),
            >= 'A' and <= 'Z' => (ConsoleKey)((int)ConsoleKey.A + c - 'A'),
            >= '0' and <= '9' => (ConsoleKey)((int)ConsoleKey.D0 + c - '0'), _ => 0,
        };
        bool control = c is >= '\u0001' and <= '\u001a' && c is not ('\t' or '\r' or '\n');
        return new ConsoleKeyInfo(c, key, char.IsUpper(c), alt, control);
    }

    private static ConsoleKeyInfo? Sequence(char final, string arguments, bool csi)
    {
        string[] parts = arguments.Split(';');
        int modifiers = parts.Length > 1 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int parsed) ? parsed - 1 : 0;
        ConsoleKey key = final switch
        {
            'A' => ConsoleKey.UpArrow, 'B' => ConsoleKey.DownArrow, 'C' => ConsoleKey.RightArrow, 'D' => ConsoleKey.LeftArrow,
            'H' => ConsoleKey.Home, 'F' => ConsoleKey.End, 'Z' => ConsoleKey.Tab,
            'P' => ConsoleKey.F1, 'Q' => ConsoleKey.F2, 'R' => ConsoleKey.F3, 'S' => ConsoleKey.F4, _ => 0,
        };
        if (csi && final == '~' && int.TryParse(parts[0], CultureInfo.InvariantCulture, out int number))
        {
            key = number switch
            {
                1 or 7 => ConsoleKey.Home, 2 => ConsoleKey.Insert, 3 => ConsoleKey.Delete, 4 or 8 => ConsoleKey.End,
                5 => ConsoleKey.PageUp, 6 => ConsoleKey.PageDown,
                11 => ConsoleKey.F1, 12 => ConsoleKey.F2, 13 => ConsoleKey.F3, 14 => ConsoleKey.F4, 15 => ConsoleKey.F5,
                17 => ConsoleKey.F6, 18 => ConsoleKey.F7, 19 => ConsoleKey.F8, 20 => ConsoleKey.F9, 21 => ConsoleKey.F10,
                23 => ConsoleKey.F11, 24 => ConsoleKey.F12, _ => 0,
            };
        }
        if (key == 0)
            return null;
        return new ConsoleKeyInfo('\0', key, final == 'Z' || (modifiers & 1) != 0, (modifiers & 2) != 0, (modifiers & 4) != 0);
    }
}