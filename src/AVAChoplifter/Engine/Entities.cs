namespace AVAChoplifter.Engine;

/// <summary>The controls: flying (each -1, 0 or 1; up the screen is -1), fire, and turning to face the front.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, bool Front = false);

/// <summary>
/// The strip: a long desert, the enemy's land to the left and the home base at the far right, a border fence
/// between. The screen shows a stretch of it under the score bar.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>The score bar and radar take the top of the screen.</summary>
    public const double Hud = 76;
    public const double Ceiling = Hud + 40, Ground = 704;
    public const double WorldWidth = 5600;
    /// <summary>The landing pad, and the door the hostages walk in at.</summary>
    public const double BaseX = 5250, DoorX = 5420;
    /// <summary>The border: the enemy doesn't come past it.</summary>
    public const double Border = 4700;

    public static double Distance(double ax, double ay, double bx, double by) => Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
}

/// <summary>The rescue helicopter.</summary>
public sealed class Chopper
{
    public double X = Field.BaseX, Y = Field.Ground - HalfHeight, Vx, Vy, FireIn, Turning, Safe, Rotor, BoardIn;
    /// <summary>-1 facing left, 1 right, 0 the front (to bomb).</summary>
    public int Facing = -1;
    /// <summary>The way it faced last time it faced sideways.</summary>
    public int Side = -1;
    public int Aboard;
    public bool Landed = true, Alive = true;
    public const int Capacity = 16;
    public const double HalfWidth = 34, HalfHeight = 16;
    /// <summary>Where it sits when it's down.</summary>
    public const double LandedY = Field.Ground - HalfHeight;
}

/// <summary>A barracks where hostages are held: shoot it open and they come out.</summary>
public sealed class Barracks
{
    public double X, Hit;
    public int Hits, Inside, Held;
    public bool Open;
    /// <summary>Until the next one comes out of the door.</summary>
    public double OutIn;
    public const double HalfWidth = 64, Height = 52;
    public const int Toughness = 3;
}

public enum HostageState { Waiting, Running, Walking }

/// <summary>A hostage: out of the barracks waiting, running for the landed chopper, or walking in at the base.</summary>
public sealed class Hostage
{
    public double X, Home, Vx, Walk, T;
    public HostageState State;
    public bool Gone;
}

public sealed class Tank
{
    public double X, Vx, FireIn, Aim, T;
    public bool Dead;
    public const double HalfWidth = 30, Height = 28;
}

/// <summary>An enemy jet: in from the left, down to the chopper's height, a missile, away, and back again.</summary>
public sealed class Jet
{
    public double X, Y, Vx, Vy, FireIn, Cruise;
    public int Passes;
    public bool Dead, Gone;
    public const double HalfWidth = 36, HalfHeight = 10;
}

/// <summary>A floating air mine, from the third sortie: it drifts after the chopper.</summary>
public sealed class Mine
{
    public double X, Y, Vx, Vy, T;
    public bool Dead;
    public const double Radius = 13;
}

public enum ShotKind { Bullet, Bomb, Shell, Missile }

public sealed class Shot
{
    public ShotKind Kind;
    public double X, Y, Vx, Vy, Life;
    public bool Hostile => Kind is ShotKind.Shell or ShotKind.Missile;
}

public enum EffectKind { Score, Blast, Word, Smoke }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
