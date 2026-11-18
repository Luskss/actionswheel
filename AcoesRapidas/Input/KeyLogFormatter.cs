namespace AcoesRapidas.Input;

/// <summary>
/// Descreve teclas para o diagnóstico sem registrar o que foi digitado.
/// </summary>
public static class KeyLogFormatter
{
    public const string RedactedKey = "(tecla comum)";

    public static string Describe(int vk) => vk switch
    {
        0x5B => "Win esquerdo",
        0x5C => "Win direito",
        0xA0 => "Shift esquerdo",
        0xA1 => "Shift direito",
        0xA2 => "Ctrl esquerdo",
        0xA3 => "Ctrl direito",
        0xA4 => "Alt esquerdo",
        0xA5 => "Alt direito",
        >= 0x70 and <= 0x87 => $"F{vk - 0x6F}",
        _ when IsTypedKey(vk) => RedactedKey,
        _ => $"VK 0x{vk:X2}",
    };

    private static bool IsTypedKey(int vk) => vk is
        0x08 or 0x09 or 0x0D or 0x20
        or (>= 0x30 and <= 0x39)
        or (>= 0x41 and <= 0x5A)
        or (>= 0x60 and <= 0x6F)
        or (>= 0xBA and <= 0xC2)
        or (>= 0xDB and <= 0xDF)
        or 0xE2;
}
