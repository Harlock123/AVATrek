using System;

namespace AVATrek.Engine;

/// <summary>What a round sends against the Enterprise.</summary>
public record RoundPlan(string Name, int Klingons, int Saucers, bool Nomad, int MaxKlingons, int MaxSaucers);

/// <summary>
/// Each sector is six rounds, as on the cabinet: Klingon patrols and battle groups, a swarm of
/// anti-matter saucers, mixed attacks, and Nomad to finish. Everything grows with the sector number
/// up to the tenth, after which the sectors keep coming at full strength.
/// </summary>
public static class Rounds
{
    public const int PerSector = 6;

    public static RoundPlan Plan(int sector, int round)
    {
        int k = Math.Min(sector - 1, 9);
        int maxK = 2 + Math.Min(3, k / 2);
        int maxS = 4 + Math.Min(4, k / 2);

        return round switch
        {
            1 => new RoundPlan("KLINGON PATROL", 3 + k / 2, 0, false, maxK, maxS),
            2 => new RoundPlan("KLINGON RAIDERS", 4 + k / 2, 2 + k / 2, false, maxK, maxS),
            3 => new RoundPlan("ANTI-MATTER SAUCERS", 1 + k / 3, 8 + k, false, maxK, maxS),
            4 => new RoundPlan("KLINGON BATTLE GROUP", 5 + k / 2, 0, false, maxK + 1, maxS),
            5 => new RoundPlan("COMBINED ATTACK", 4 + k / 2, 5 + k / 2, false, maxK, maxS),
            _ => new RoundPlan("NOMAD", k / 2, 0, true, maxK, maxS)
        };
    }

    /// <summary>Starbases at the start of a sector: three early on, fewer later.</summary>
    public static int StarbasesFor(int sector) => Math.Max(1, 3 - (sector - 1) / 3);

    public static int RoundBonus(int sector) => 500 * sector;

    public static int StarbaseBonus(int sector) => 1000 * sector;
}
