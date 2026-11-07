/// Physics layers. User layers 8+ exist in every project even when unnamed.
public static class Layers
{
    public const int World = 8;
    public const int Player = 9;
    public const int Enemy = 10;
    public const int Debris = 11; // ragdolls, broken pieces, pickups: collide with the world only

    public const int WorldMask = 1 << World;
    public const int PlayerMask = 1 << Player;
    public const int EnemyMask = 1 << Enemy;
    public const int DebrisMask = 1 << Debris;
}
