namespace AcoesRapidas.Input;

/// <summary>
/// Comando de teclado do menu: escolher pelo número (Index), andar pelas fatias (Step),
/// confirmar a fatia destacada (Confirm) ou, sem nenhum deles, voltar ou fechar.
/// </summary>
public readonly record struct MenuCommand(int? Index, int Step = 0, bool Confirm = false)
{
    public static MenuCommand Close => new(null);

    public static MenuCommand Next => new(null, 1);

    public static MenuCommand Previous => new(null, -1);

    public static MenuCommand Accept => new(null, Confirm: true);
}

public readonly record struct MenuKeyResult(bool Suppress, MenuCommand? Command);

/// <summary>
/// Traduz Esc, Backspace, Enter, as setas e as teclas 1 a 9 e 0 enquanto o menu está aberto.
/// A soltura de uma tecla bloqueada também é bloqueada, mesmo que o menu já tenha fechado.
/// </summary>
public sealed class MenuKeyFilter
{
    private const int VkBackspace = 0x08;
    private const int VkEnter = 0x0D;
    private const int VkEscape = 0x1B;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkDigit0 = 0x30;
    private const int VkDigit1 = 0x31;
    private const int VkDigit9 = 0x39;
    private const int VkNumpad0 = 0x60;
    private const int VkNumpad1 = 0x61;
    private const int VkNumpad9 = 0x69;

    /// <summary>A tecla 0 escolhe a décima ação, como no teclado.</summary>
    private const int TenthIndex = 9;

    private readonly HashSet<int> _swallowedKeys = [];

    public MenuKeyResult Process(int vk, bool isDown, bool menuOpen)
    {
        if (!isDown)
        {
            return new MenuKeyResult(_swallowedKeys.Remove(vk), null);
        }

        if (_swallowedKeys.Contains(vk))
        {
            // Segurar uma seta continua andando; nas outras teclas a repetição é ignorada.
            return new MenuKeyResult(true, menuOpen && Map(vk) is { Step: not 0 } step ? step : null);
        }

        if (!menuOpen || Map(vk) is not { } command)
        {
            return default;
        }

        _swallowedKeys.Add(vk);
        return new MenuKeyResult(true, command);
    }

    private static MenuCommand? Map(int vk) => vk switch
    {
        VkEscape or VkBackspace => MenuCommand.Close,
        VkEnter => MenuCommand.Accept,
        VkRight or VkDown => MenuCommand.Next,
        VkLeft or VkUp => MenuCommand.Previous,
        >= VkDigit1 and <= VkDigit9 => new MenuCommand(vk - VkDigit1),
        VkDigit0 or VkNumpad0 => new MenuCommand(TenthIndex),
        >= VkNumpad1 and <= VkNumpad9 => new MenuCommand(vk - VkNumpad1),
        _ => null,
    };
}
