namespace AcoesRapidas.UI;

/// <summary>
/// Converte um ícone colorido em uma máscara monocromática, para ele ser pintado com a mesma
/// cor dos demais ícones. Logos de cor chapada viram uma forma sólida com os detalhes vazados;
/// imagens com muitos tons, como capas de jogos, mantêm a luz e a sombra.
/// </summary>
public static class IconStylizer
{
    private const int BytesPerPixel = 4;
    private const int ToneBins = 16;
    private const int DominantWindow = 3;
    private const double FlatShare = 0.5;
    private const double CutStart = 0.12;
    private const double CutEnd = 0.30;
    private const byte SolidAlpha = 200;
    private const double PhotoFloor = 0.18;
    private const double PercentileMargin = 0.05;

    /// <summary>Recebe pixels BGRA e devolve pixels BGRA brancos, com o desenho no canal alfa.</summary>
    public static byte[] CreateMask(byte[] bgra)
    {
        var tones = ReadSolidTones(bgra);
        var toAlpha = CreateToneMapping(tones);

        var mask = new byte[bgra.Length];
        for (var i = 0; i < bgra.Length; i += BytesPerPixel)
        {
            var alpha = bgra[i + 3];

            // Bordas semitransparentes têm cor pouco confiável e só acompanham o contorno.
            var coverage = alpha >= SolidAlpha ? toAlpha(ToneAt(bgra, i)) : 1;

            mask[i] = mask[i + 1] = mask[i + 2] = byte.MaxValue;
            mask[i + 3] = (byte)Math.Round(alpha * coverage);
        }

        return mask;
    }

    private static List<double> ReadSolidTones(byte[] bgra)
    {
        var tones = new List<double>(bgra.Length / BytesPerPixel);
        for (var i = 0; i < bgra.Length; i += BytesPerPixel)
        {
            if (bgra[i + 3] >= SolidAlpha)
            {
                tones.Add(ToneAt(bgra, i));
            }
        }

        return tones;
    }

    private static double ToneAt(byte[] bgra, int index) =>
        ((0.0722 * bgra[index]) + (0.7152 * bgra[index + 1]) + (0.2126 * bgra[index + 2])) / byte.MaxValue;

    private static Func<double, double> CreateToneMapping(List<double> tones)
    {
        if (tones.Count == 0)
        {
            return _ => 1;
        }

        var (dominantTone, share) = FindDominantTone(tones);
        return share >= FlatShare ? CreateLogoMapping(dominantTone) : CreatePhotoMapping(tones);
    }

    private static (double Tone, double Share) FindDominantTone(List<double> tones)
    {
        var bins = new int[ToneBins];
        foreach (var tone in tones)
        {
            bins[BinOf(tone)]++;
        }

        var bestStart = 0;
        var bestCount = -1;
        for (var start = 0; start <= ToneBins - DominantWindow; start++)
        {
            var count = bins.Skip(start).Take(DominantWindow).Sum();
            if (count > bestCount)
            {
                (bestStart, bestCount) = (start, count);
            }
        }

        var dominant = tones.Where(tone => BinOf(tone) >= bestStart && BinOf(tone) < bestStart + DominantWindow);
        return (dominant.Average(), (double)bestCount / tones.Count);
    }

    private static int BinOf(double tone) => Math.Min((int)(tone * ToneBins), ToneBins - 1);

    /// <summary>A cor principal do logo fica sólida e o que contrasta com ela é vazado.</summary>
    private static Func<double, double> CreateLogoMapping(double dominantTone) =>
        tone => 1 - SmoothStep(CutStart, CutEnd, Math.Abs(tone - dominantTone));

    /// <summary>Estica os tons da imagem, mantendo um mínimo para o formato continuar visível.</summary>
    private static Func<double, double> CreatePhotoMapping(List<double> tones)
    {
        var sorted = tones.Order().ToList();
        var low = sorted[(int)((sorted.Count - 1) * PercentileMargin)];
        var high = sorted[(int)((sorted.Count - 1) * (1 - PercentileMargin))];
        var range = Math.Max(high - low, 0.01);

        return tone => PhotoFloor + ((1 - PhotoFloor) * Math.Clamp((tone - low) / range, 0, 1));
    }

    private static double SmoothStep(double start, double end, double value)
    {
        var t = Math.Clamp((value - start) / (end - start), 0, 1);
        return t * t * (3 - (2 * t));
    }
}
