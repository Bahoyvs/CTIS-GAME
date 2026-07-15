using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CBuilding.Core
{
    /// <summary>
    /// GS-2 / GameFlow — biome authority for the Section 1-3 loop.
    ///
    /// BIOME RULES (GDD §4 / Section Design Doc v2.0):
    ///   - Section 1 is ALWAYS the "Intact C-Building" (sabit tema, biome roll YOK).
    ///   - Sections 2 & 3: the server rolls 2 DISTINCT biomes out of the 5-biome pool
    ///     (Forest, Frozen, Desert, Void, Family) ONCE at run start, via Fisher-Yates —
    ///     no duplicates possible by construction, no reroll loops.
    ///
    /// REPLICATION: the two rolled indices live in NetworkVariables, so clients (including
    /// late joiners) always know the run's biome plan and can enable the right environment
    /// root locally. Environment swapping is pure client-side presentation driven by
    /// replicated state — the roots themselves need no NetworkObjects.
    ///
    /// SETUP: same GameObject family as SectionManager (needs a NetworkObject). Assign
    /// the intact root + exactly the 5 biome entries; each entry's environmentRoot is a
    /// scene root object containing that biome's visuals/props/nav.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class EnvironmentManager : NetworkBehaviour
    {
        public static EnvironmentManager Instance { get; private set; }

        [Serializable]
        public class BiomeEntry
        {
            [Tooltip("Design name — Forest / Frozen / Desert / Void / Family.")]
            public string biomeName;

            [Tooltip("Scene root enabled while this biome is active (visuals, props, spawn nodes...).")]
            public GameObject environmentRoot;
        }

        [Header("Section 1 — fixed theme")]
        [Tooltip("'Intact C-Building' environment root. Always used for Section 1.")]
        [SerializeField] private GameObject intactBuildingRoot;

        [Header("Biome pool (Sections 2 & 3)")]
        [Tooltip("The 5 biomes. Server rolls 2 distinct ones at run start (Fisher-Yates).")]
        [SerializeField] private List<BiomeEntry> biomePool = new();

        // Rolled ONCE at run start; -1 = not rolled yet. Replicated so every peer
        // (and every late joiner) can resolve section → environment locally.
        private readonly NetworkVariable<int> _netSection2Biome = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<int> _netSection3Biome = new(
            -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Fired on EVERY peer after an environment swap: (section, biomeIndex; -1 = intact/none).</summary>
        public event Action<int, int> OnEnvironmentApplied;

        /// <summary>Any-peer reads for UI ("Next up: Frozen") and debug.</summary>
        public int Section2BiomeIndex => _netSection2Biome.Value;
        public int Section3BiomeIndex => _netSection3Biome.Value;

        public string GetBiomeName(int index) =>
            index >= 0 && index < biomePool.Count ? biomePool[index].biomeName : "Intact C-Building";

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            _netSection2Biome.OnValueChanged += HandleBiomeRollChanged;
            _netSection3Biome.OnValueChanged += HandleBiomeRollChanged;
            SectionManager.OnSectionChanged += HandleSectionChanged;

            if (IsServer) ServerRollBiomes();

            // Late joiners / initial paint: apply whatever the current section is.
            ApplyForSection(SectionManager.CurrentSection);
        }

        public override void OnNetworkDespawn()
        {
            _netSection2Biome.OnValueChanged -= HandleBiomeRollChanged;
            _netSection3Biome.OnValueChanged -= HandleBiomeRollChanged;
            SectionManager.OnSectionChanged -= HandleSectionChanged;
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            base.OnDestroy();
        }

        // ---------------------------------------------------------------- Roll (SERVER)

        /// <summary>
        /// Fisher-Yates over the pool indices; the first two entries of the shuffle are the
        /// run's Section 2 and Section 3 biomes. Distinctness is guaranteed by construction
        /// (a shuffle never repeats an element) — no "reroll until different" loops.
        /// </summary>
        private void ServerRollBiomes()
        {
            if (_netSection2Biome.Value >= 0) return; // already rolled (host re-spawn guard)

            if (biomePool.Count < 2)
            {
                Debug.LogError("[EnvironmentManager] Biome pool needs at least 2 entries (design: 5).", this);
                return;
            }

            int[] indices = new int[biomePool.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;

            for (int i = indices.Length - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1); // inclusive of i — classic F-Y
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            _netSection2Biome.Value = indices[0];
            _netSection3Biome.Value = indices[1];

            Debug.Log($"[EnvironmentManager] Run biomes rolled — S2: {GetBiomeName(indices[0])}, " +
                      $"S3: {GetBiomeName(indices[1])}.");
        }

        // ---------------------------------------------------------------- Apply (EVERY PEER)

        private void HandleSectionChanged(int section) => ApplyForSection(section);

        private void HandleBiomeRollChanged(int previous, int current)
        {
            // Roll can replicate AFTER the initial section paint on clients — re-apply.
            ApplyForSection(SectionManager.CurrentSection);
        }

        /// <summary>Enable exactly the environment root the section calls for; disable the rest.</summary>
        private void ApplyForSection(int section)
        {
            int biomeIndex = section switch
            {
                2 => _netSection2Biome.Value,
                3 => _netSection3Biome.Value,
                _ => -1 // Section 1 (intact) or Finale (owns its own building — not ours)
            };

            if (intactBuildingRoot != null)
                intactBuildingRoot.SetActive(section == 1);

            for (int i = 0; i < biomePool.Count; i++)
            {
                if (biomePool[i].environmentRoot != null)
                    biomePool[i].environmentRoot.SetActive(i == biomeIndex);
            }

            OnEnvironmentApplied?.Invoke(section, biomeIndex);
        }
    }
}
