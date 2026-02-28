using UnityEngine;
using Game.Scripts.Tactics.Data;

namespace Game.Scripts.Tactics
{
    /// <summary>
    /// Global manager to track the player's current managerial license level.
    /// Used by tactical components to ensure they are unlocked before use.
    /// </summary>
    public class PlayerLicenseManager : MonoBehaviour
    {
        public static PlayerLicenseManager Instance { get; private set; }

        [Tooltip("The persistent manager profile asset containing the actual data.")]
        [SerializeField] private Game.Scripts.Data.ManagerProfileSO _managerProfile;

        /// <summary>
        /// The current license level unlocked by the player.
        /// Reads from the ManagerProfileSO if assigned; otherwise, uses internal state.
        /// </summary>
        public LicenseLevel CurrentLicense 
        { 
            get => _managerProfile != null ? _managerProfile.CurrentLicense : _fallbackLicense;
            private set 
            {
                if (_managerProfile != null) _managerProfile.CurrentLicense = value;
                else _fallbackLicense = value;
            }
        }

        private LicenseLevel _fallbackLicense = LicenseLevel.None_1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                
                if (_managerProfile == null)
                {
                    // Fallback to Resources loading
                    _managerProfile = Resources.Load<Game.Scripts.Data.ManagerProfileSO>("Defaults/DefaultManagerProfile");
                    if (_managerProfile == null) 
                    {
                        Debug.LogWarning("[PlayerLicenseManager] No ManagerProfileSO assigned or found in Resources. Using transient fallback state.");
                    }
                }
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Updates the player's license level if the new level is higher.
        /// </summary>
        /// <param name="newLevel">The new license level to apply.</param>
        public void UpgradeLicense(LicenseLevel newLevel)
        {
            if (newLevel > CurrentLicense)
            {
                CurrentLicense = newLevel;
                Debug.Log($"[LICENSE] Manager license upgraded to {CurrentLicense}");
                // Fire an event if UI needs to update
            }
        }
    }
}
