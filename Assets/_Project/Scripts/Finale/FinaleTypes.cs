namespace CBuilding.Finale
{
    /// <summary>
    /// Section 4 (Final Phase) faz makinesi durumları — güncel Bodrum+4 kat / Ruh sistemi.
    /// Eski Overwatch/Hack-Trap-Mark/Upload-Bar tasarımının YERİNİ alır.
    /// </summary>
    public enum FinalePhase : byte
    {
        Inactive, // Section 1-3 devam ediyor, Finale henüz başlamadı.
        Voting,   // Defender oylaması açık (VotingManager).
        JackIn,   // Defender bedeni Core'da bırakılır, Spirit'e dönüşür; Runner'lar Bodrum'a ışınlanır.
        Escape,   // Kat kat tırmanış + Escape Timer.
        Resolved  // Win (çatı) ya da Lose (timer doldu → patlama).
    }

    /// <summary>Kat indeksleri: 0 = Bodrum (Core/start) ... 4 = Çatı (extraction).</summary>
    public static class FinaleFloors
    {
        public const int Basement = 0;
        public const int Roof = 4;
        public const int Count = 5;
    }
}
