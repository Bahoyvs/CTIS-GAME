using Unity.Netcode.Components;

namespace CBuilding.Network
{
    /// <summary>
    /// Owner-authoritative NetworkTransform (official NGO-sanctioned pattern).
    ///
    /// WHY: stock NetworkTransform is SERVER-authoritative — every client input would need a
    /// round-trip before the hero moves, which feels awful in an action game. For co-op
    /// (trusted peers, no ranked PvP) the standard trade-off is: MOVEMENT is client/owner
    /// authoritative for responsiveness, while COMBAT (health, damage, AI) stays
    /// server-authoritative. Cheating scope is limited to "I moved fast", which co-op tolerates.
    ///
    /// USE: put THIS on the Hero prefab (owner drives it). Enemies use the regular
    /// NetworkTransform component (server drives them).
    /// </summary>
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false; // Owner writes transform state.
    }
}
