namespace CBuilding.Enemies
{
    /// <summary>
    /// A component that may cancel an enemy's death (Phoenix-Ghoul's Fiery Egg).
    /// RosterEnemy.Die() asks each interceptor in turn BEFORE the death path runs;
    /// the first to return true owns the outcome and MUST restore health via
    /// RosterEnemy.ServerRestoreHealth, otherwise the enemy is a zombie-zombie at 0 HP.
    /// Server-side only — Die() never executes anywhere else.
    /// </summary>
    public interface IDeathInterceptor
    {
        bool TryInterceptDeath(RosterEnemy enemy);
    }
}
