namespace AcoesRapidas.Input;

/// <summary>Resultado da análise de um evento de teclado.</summary>
/// <param name="Suppress">O evento não deve chegar aos outros programas.</param>
/// <param name="Triggered">O usuário pressionou a tecla Copilot.</param>
/// <param name="MaskWinKey">É preciso evitar que o Iniciar abra quando o Win for solto.</param>
public readonly record struct DetectorResult(bool Suppress, bool Triggered, bool MaskWinKey = false);

/// <summary>
/// Reconhece a sequência enviada pela tecla Copilot: Win esquerdo + Shift esquerdo + F23.
/// Quando o Windows associa a tecla ao menu de contexto, a sequência vem seguida da tecla
/// Aplicativos, que também é bloqueada para o menu de contexto não abrir junto.
/// </summary>
public sealed class CopilotKeyDetector
{
    public const int VkLeftWin = 0x5B;
    public const int VkApps = 0x5D;
    public const int VkLeftShift = 0xA0;
    public const int VkF23 = 0x86;

    /// <summary>Uma pressão pode gerar a sequência duas vezes em poucos milissegundos.</summary>
    private const uint DuplicateWindowMs = 100;

    private const uint CompanionKeyWindowMs = 300;

    private bool _winDown;
    private bool _shiftDown;
    private bool _f23Down;
    private bool _appsKeySuppressed;
    private uint? _lastTrigger;
    private uint? _lastSequence;

    /// <summary>Indica uma tecla que deve ser analisada mesmo quando gerada por software.</summary>
    public static bool IsCompanionKey(int vk) => vk == VkApps;

    /// <param name="vk">Código virtual da tecla.</param>
    /// <param name="isDown">Verdadeiro quando a tecla desce.</param>
    /// <param name="timestamp">Horário do evento em milissegundos, como informado pelo Windows.</param>
    public DetectorResult Process(int vk, bool isDown, uint timestamp = 0)
    {
        switch (vk)
        {
            case VkApps:
                return ProcessAppsKey(isDown, timestamp);

            case VkLeftWin:
                _winDown = isDown;
                if (isDown)
                {
                    // Uma nova sequência começou. Recupera o estado caso a soltura do F23
                    // tenha se perdido, por exemplo ao bloquear a tela.
                    _f23Down = false;
                }

                return default;

            case VkLeftShift:
                _shiftDown = isDown;
                return default;

            case VkF23:
                return ProcessF23(isDown, timestamp);

            default:
                return default;
        }
    }

    private static bool IsWithin(uint? start, uint timestamp, uint windowMs) =>
        start is { } value && unchecked(timestamp - value) < windowMs;

    private DetectorResult ProcessAppsKey(bool isDown, uint timestamp)
    {
        if (isDown)
        {
            _appsKeySuppressed = IsWithin(_lastSequence, timestamp, CompanionKeyWindowMs);
            return new DetectorResult(Suppress: _appsKeySuppressed, Triggered: false);
        }

        var suppress = _appsKeySuppressed;
        _appsKeySuppressed = false;
        return new DetectorResult(Suppress: suppress, Triggered: false);
    }

    private DetectorResult ProcessF23(bool isDown, uint timestamp)
    {
        if (!isDown)
        {
            var wasTracked = _f23Down;
            _f23Down = false;
            return new DetectorResult(Suppress: wasTracked, Triggered: false);
        }

        if (_f23Down)
        {
            // Repetição automática enquanto a tecla continua pressionada.
            return new DetectorResult(Suppress: true, Triggered: false);
        }

        if (!_winDown || !_shiftDown)
        {
            return default;
        }

        _f23Down = true;
        _lastSequence = timestamp;

        var isDuplicate = IsWithin(_lastTrigger, timestamp, DuplicateWindowMs);
        if (!isDuplicate)
        {
            _lastTrigger = timestamp;
        }

        return new DetectorResult(Suppress: true, Triggered: !isDuplicate, MaskWinKey: true);
    }
}
