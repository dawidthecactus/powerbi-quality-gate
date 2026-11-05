using System.Text;

namespace PowerBIQualityGate.Cli.Rendering;

/// <summary>
/// Defense in depth for --no-color and redirected output. Spectre's terminal
/// capability flags are still disabled in ConsoleFactory, but this writer also
/// prevents accidental control sequences from reaching the output stream.
/// Works when a sequence spans multiple TextWriter.Write calls.
/// </summary>
internal sealed class PlainTextOutputWriter : TextWriter
{
    private enum EscapeState
    {
        Text,
        Escape,
        Csi,
        Osc,
        OscEscape,
        ControlString,
        ControlStringEscape
    }

    private readonly TextWriter _inner;
    private EscapeState _state;

    public PlainTextOutputWriter(TextWriter inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        NewLine = inner.NewLine;
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        switch (_state)
        {
            case EscapeState.Text:
                if (value == '\u001b')
                {
                    _state = EscapeState.Escape;
                }
                else if (value is '\r' or '\n' or '\t' || !char.IsControl(value))
                {
                    _inner.Write(value);
                }
                break;

            case EscapeState.Escape:
                _state = value switch
                {
                    '[' => EscapeState.Csi,
                    ']' => EscapeState.Osc,
                    'P' or 'X' or '^' or '_' => EscapeState.ControlString,
                    '\u001b' => EscapeState.Escape,
                    _ => EscapeState.Text
                };
                break;

            case EscapeState.Csi:
                // ANSI CSI parameter/intermediate bytes end at a final byte
                // in the ASCII range 0x40..0x7e.
                if (value >= '@' && value <= '~')
                {
                    _state = EscapeState.Text;
                }
                else if (value == '\u001b')
                {
                    _state = EscapeState.Escape;
                }
                break;

            case EscapeState.Osc:
                if (value == '\a')
                {
                    _state = EscapeState.Text;
                }
                else if (value == '\u001b')
                {
                    _state = EscapeState.OscEscape;
                }
                break;

            case EscapeState.OscEscape:
                _state = value switch
                {
                    '\\' or '\a' => EscapeState.Text,
                    '\u001b' => EscapeState.OscEscape,
                    _ => EscapeState.Osc
                };
                break;

            case EscapeState.ControlString:
                if (value == '\u001b')
                {
                    _state = EscapeState.ControlStringEscape;
                }
                break;

            case EscapeState.ControlStringEscape:
                _state = value switch
                {
                    '\\' => EscapeState.Text,
                    '\u001b' => EscapeState.ControlStringEscape,
                    _ => EscapeState.ControlString
                };
                break;
        }
    }

    public override void Write(string? value)
    {
        if (value is not null)
        {
            Write(value.AsSpan());
        }
    }

    public override void Write(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (index > buffer.Length - count)
        {
            throw new ArgumentException("The requested range exceeds the buffer.", nameof(count));
        }

        Write(buffer.AsSpan(index, count));
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        foreach (var character in buffer)
        {
            Write(character);
        }
    }

    public override void Flush() => _inner.Flush();
}
