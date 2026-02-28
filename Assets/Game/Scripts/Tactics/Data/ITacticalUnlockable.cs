namespace Game.Scripts.Tactics.Data
{
    /// <summary>
    /// Interface for tactical elements that must be unlocked via a specific LicenseLevel.
    /// </summary>
    public interface ITacticalUnlockable
    {
        /// <summary>
        /// The minimum license level required to use this tactical element.
        /// </summary>
        LicenseLevel RequiredLicense { get; }

        /// <summary>
        /// Checks if the provided manager license is sufficient to unlock this tactical element.
        /// </summary>
        /// <param name="currentManagerLicense">The manager's current license level.</param>
        /// <returns>True if the manager's license is equal to or higher than the required license.</returns>
        bool IsUnlocked(LicenseLevel currentManagerLicense);
    }
}
