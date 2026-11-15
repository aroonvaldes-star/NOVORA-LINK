namespace NOVORA.VisionEngine.Adaptation;

/// <summary>Muestra de una ventana de 1 s de la sesión LAN de VisionEngine.</summary>
public readonly record struct VEAdaptiveSample(double RttMs, double LossRatio, double QueueAgeMs, double WriteWaitMs);

/// <summary>Nivel de calidad: tamaño máximo del lado largo, fps y tasa de video.</summary>
public readonly record struct VEAdaptiveLevel(int MaxSize, int Fps, int Bitrate);

public readonly record struct VEAdaptiveDecision(int Level, VEAdaptiveLevel Settings, bool Changed);

/// <summary>
/// Política de calidad del PC: baja un nivel tras 2 s de congestión y sube uno tras 8 s estables.
/// Degrada primero la resolución; los fps no bajan de 30 mientras queden escalones de resolución.
/// Las muestras intermedias (ni buenas ni malas) reinician ambos contadores (histéresis).
/// </summary>
public sealed class VEAdaptivePolicy
{
    public const int DowngradeAfterSeconds = 2;
    public const int UpgradeAfterSeconds = 8;

    private static readonly VEAdaptiveLevel[] Ladder =
    [
        new(1280, 60, 8_000_000),
        new(1280, 45, 8_000_000),
        new(960, 45, 6_000_000),
        new(720, 30, 4_000_000),
        new(720, 30, 2_500_000)
    ];

    private int _level;
    private int _bad;
    private int _good;

    public int MaxLevel => Ladder.Length - 1;

    public VEAdaptiveDecision Current => new(_level, Ladder[_level], false);

    public static VEAdaptiveLevel LevelFor(int level) => Ladder[Math.Clamp(level, 0, Ladder.Length - 1)];

    public VEAdaptiveDecision Tick(VEAdaptiveSample sample)
    {
        bool congested = sample.RttMs > 150 || sample.LossRatio > 0.02 || sample.QueueAgeMs > 200 || sample.WriteWaitMs > 100;
        bool stable = sample.RttMs < 60 && sample.LossRatio < 0.005 && sample.QueueAgeMs < 50 && sample.WriteWaitMs < 20;

        if (congested)
        {
            _good = 0;
            _bad++;
            if (_bad >= DowngradeAfterSeconds && _level < MaxLevel)
            {
                _level++;
                _bad = 0;
                return new(_level, Ladder[_level], true);
            }
        }
        else if (stable)
        {
            _bad = 0;
            _good++;
            if (_good >= UpgradeAfterSeconds && _level > 0)
            {
                _level--;
                _good = 0;
                return new(_level, Ladder[_level], true);
            }
        }
        else
        {
            _bad = 0;
            _good = 0;
        }

        return new(_level, Ladder[_level], false);
    }
}
