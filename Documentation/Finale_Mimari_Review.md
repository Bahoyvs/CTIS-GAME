# Final Phase Mimari Dokümanı — Review & Kod Tabanı Uyum Raporu

Tarih: 2026-07-15 · Kapsam: "C-Building: Uçtan Uca Game Loop Mimarisi" dokümanının mevcut
kod tabanıyla (Assets/_Project/Scripts) karşılaştırması + implementasyon notları.

---

## 1. Doküman ↔ Kod Tabanı Çelişkileri (kritik)

**1.1 "Mevcut" denen kod mevcut değil.** Doküman §2, biyom seçimi / ölüm-dirilme /
vending→boss mantığının `SectionManager.cs`'te "önceki teslimatta mevcut" olduğunu söylüyor.
Repodaki gerçek `SectionManager` (Core/GameFlow) yalnızca 1-3 arası section indeksi tutan
minimal bir NetworkVariable sarmalayıcısı: biyom seçimi yok, Fisher-Yates yok, boss havuzu
yok, ölüm/dirilme yok, `EnterSection4()` yok. Aynı şekilde §3.2'de "VotingManager (mevcut,
değişmedi)" deniyor — repoda VotingManager hiç yoktu. Ya doküman başka bir branch'i referans
alıyor ya da o teslimat hiç merge edilmedi. **Aksiyon:** doğru branch teyit edilmeli; bu
oturumda VotingManager sıfırdan yazıldı (aşağıda §3).

**1.2 UGS Lobby/Relay varsayımı gerçeği yansıtmıyor.** Doküman §1, UGS Lobby + Relay + join
code akışı tarif ediyor. Mevcut kod tamamen farklı ve çalışan bir yapı: `NetworkSessionManager`
(direkt IP/port 7777, UnityTransport) + `LobbyNetworkManager` (NetworkList tabanlı, sahne içi
lobi; isim/hero seçimi/ready; `HeroSelections` statik snapshot'ı ile `PlayerSpawner`'a handoff).
UGS'ye geçiş paket ekleme + Unity Dashboard proje kurulumu + transport bootstrap değişikliği
gerektirir — bu bir refactor değil, ayrı bir iş kalemi (GS-XX olarak planlanmalı). Mevcut lobi
zaten dokümanın istediği işlevin çoğunu (hero seçimi lobide, ready senkronu, host start)
karşılıyor. **UGS LobbyManager bu teslimatta bilinçli olarak yazılmadı.**

**1.3 GameSessionManager, NetworkGameManager ile çakışıyor.** Dokümanın "yeni" dediği
top-level state machine'in (%80'i) repoda zaten var: `NetworkGameManager` — SessionState
{Lobby, InRun, RunComplete}, connection approval (4 kişi cap), oyuncu registry'si, run
reset hook'ları. Paralel ikinci bir session FSM eklemek iki otorite yaratır (klasik split-brain).
**Öneri:** GameSessionManager'ı ayrı sınıf olarak eklemek yerine NetworkGameManager'ı genişletin
(gerekirse `Loading` durumu + `SceneManager.OnLoadEventCompleted` beklemesi eklenir).
Bu teslimatta ResultsManager, RunComplete/Resolved üzerinden mevcut yapıya bağlandı.

**1.4 Sahne stratejisi çelişkisi.** Doküman additive persistent scene istiyor (kamera rig +
UI + manager'lar kalıcı sahnede); mevcut tüm akış `LoadSceneMode.Single` kullanıyor
(LobbyNetworkManager.StartGame, NetworkSessionManager). Single→Additive geçişi
CameraModeController (FindFirstObjectByType ile tek vcam varsayımı) ve `destroyWithScene:true`
spawn'larını da etkiler. Ayrı, dikkatli bir migration işi — bu teslimatta dokunulmadı.

**1.5 İsimlendirme kalıntıları.** Kurgu "network/hack" → "Ruh" olarak değişti ama kodda
`IHackable`, `SpawnNode.ServerHack`, `StealthUntilClose`'daki "Defender's Marking" yorumları
duruyor. Bahadır'ın Ultimate'i (node hack) Section 1-3 mekaniği olarak meşru — yalnızca
Section 4 bağlamındaki hack/marking referansları öldü. GS-15'teki "God's Eye" adı da
kullanılmamalı (yeni ad: SpiritVision).

## 2. Tasarım Değerlendirmesi (dokümanın kendisi)

**Güçlü yanlar.** Ground-truth önceliğinin açık yazılması; min oyuncu sayısının config'e
alınıp N-1 formülünün dinamik tutulması; Spirit HP=0 belirsizliğinin otomatik varsayılmayıp
boş hook olarak bırakılması; kat bazlı spawn/despawn kararı (5 katı aynı anda ayakta tutmamak);
`ISpawnDirectorRouting`'in sadeleştirilmesi. Bunların hepsi doğru kararlar ve implementasyonda
aynen uygulandı.

**Eksikler / açık riskler:**

- **Convergence Zone "griefing" riski:** tek bir Runner zone'a girmeyi reddederse takım
  kilitlenir. Timer zaten global baskı uyguluyor ama AFK/troll oyuncu için bir çözüm yok
  (ör. zone dışındaki son oyuncuya görünür işaret + süreli otomatik muafiyet?). Kerem'e
  taşınmalı.
- **JackIn sırasında Runner ölümü:** teleport + faz geçişi sırasında ölüm edge-case'i
  tanımsız. Kodda JackIn fazı da ölüm handler'ına dahil edildi (spectate kuralı uygulanır).
- ~~Escape Timer süresi ve Ruh Yeteneği cooldown'u TBD~~ → **ÇÖZÜLDÜ** (bkz. §6):
  timer 300-600 sn bandı, cooldown karaktere göre.
- ~~Tie-break "en yüksek HP" meta riski~~ → **ÇÖZÜLDÜ** (bkz. §6): kural tersine çevrildi,
  en DÜŞÜK HP'li seçilir.
- ~~"Tüm Runner'lar ölürse" tanımsız~~ → **ÇÖZÜLDÜ** (bkz. §6): anında Game Over onaylandı.
- **BaseHero.Die() spectate akışı yok:** `DiedClientRpc` "MVP: leave the body" diyor.
  Section 4'ün "anlık ölüm → spectate" kuralı için spectate kamera/input kapatma katmanı
  hâlâ eksik — ayrı iş kalemi.

## 3. Bu Teslimatta Yazılan Kod

Yeni modül: `Assets/_Project/Scripts/Finale/` (namespace `CBuilding.Finale`)

| Dosya | Sorumluluk |
|---|---|
| FinaleTypes.cs | FinalePhase enum + kat sabitleri (0=Bodrum, 4=Çatı) |
| FinaleManager.cs | Faz FSM (Voting→JackIn→Escape→Resolved), roster (N-1), teleport, win/lose; Defender ölümü/disconnect'i akışı kilitlemez (§6 madde 1-2) |
| VotingManager.cs | Defender oylaması; tie-break: en DÜŞÜK HP → rastgele (§6 madde 4) |
| EscapeTimerController.cs | ServerTime tabanlı geri sayım (300-600 sn bandı); OnExpired → patlama/kayıp |
| ConvergenceZone.cs | Kat başına trigger volume; server-side doluluk (HashSet) |
| FloorConvergenceTracker.cs | "Hayattaki tüm Runner'lar aynı anda zone'da" kontrolü; ölüler otomatik muaf |
| FinaleFloorBounds.cs | Spirit free-cam'in kat başına sınır hacmi |
| SpiritVisionController.cs | Kat kısıtlı free-cam (Cinemachine 3 + Input System), monokrom görüş toggle'ı, fog-of-war sorgusu (`IsPointRevealed`) |
| SpiritAbilityController.cs | Tek Ruh Yeteneği; Ruh Enerjisi barı (kullanımda tükenir, §6 madde 1) + karaktere göre cooldown (`ISpiritAbilityEffect.SpiritAbilityCooldown`, §6 madde 7); hero bazlı içerik Bölüm 8'de |
| ResultsManager.cs | Win/Lose paneli; HostRematch → ResetToLobby, LeaveToMainMenu |

Mevcut dosya değişiklikleri:

- **SectionManager.cs** — clamp 1..3 → 1..4; `FinaleSection = 4` sabiti. Section 1-3
  tüketicileri (basic attack SO swap vb.) 4 değerini hiç görmeden çalışmaya devam eder
  (kendi tablolarında 4 yok).
- **SpawnDirector.cs** — `ISpawnDirectorRouting` implementasyonu: `SetMode` (Normal /
  EscapeCorridorOnly — DefenderCoreOnly kaldırıldı), `RegisterTargetPool` (mesafe bandı,
  frustum ve guilt-spawn döngüleri artık sadece havuzdaki client'ları sayar),
  `SetActiveFloor` (kat değişiminde kayıtlı tüm düşmanlar despawn), `ServerSetEncounterOverride`
  (FinaleManager kat başına encounter atar — SectionEncounterSO yapısı aynen yeniden
  kullanılıyor, yeni veri tipi gerekmedi).
- **Yeni:** `ISpawnDirectorRouting.cs`, `FloorSpawnNodeTag.cs` (Finale node'larını katlarına
  etiketler; Section 1-3 node'larına eklenmesi gerekmez).

**Dokümandan bilinçli sapmalar:** (1) UGS LobbyManager yazılmadı (§1.2), (2) ayrı
GameSessionManager yazılmadı (§1.3), (3) `ManualTriggerVariant`'ı Final Passive SO'suna alan
olarak eklemek yerine hero prefab'ına takılan `ISpiritAbilityEffect` arayüzü tercih edildi —
SO'lara Bölüm 8 içeriği gelmeden alan eklemek şemayı erken kilitler; arayüz, mevcut
`BahadirFinalPassiveRuntime` benzeri runtime'ların yanına doğal oturur.

## 4. Sahne Kurulum Checklist'i (Finale sahnesi)

1. NetworkObject'li bir "FinaleSystems" objesi: FinaleManager + VotingManager +
   EscapeTimerController + SpiritAbilityController (+ FloorConvergenceTracker, NetworkObject
   gerektirmez). FinaleManager'ın Inspector referanslarını bağla.
2. Her kata: ConvergenceZone (isTrigger collider) + FinaleFloorBounds + kat düşman node'larına
   SpawnNode yanına FloorSpawnNodeTag.
3. Runner spawn noktaları (Bodrum) ve Core beden noktası → FinaleManager listelerine.
4. Kat başına SectionEncounterSO (sectionIndex önemsiz, override ile atanıyor) →
   FinaleManager.floorEncounters (index = kat).
5. Spirit vcam (MainIsoCam'den ayrı CinemachineCamera) + monokrom Volume kökü →
   SpiritVisionController.
6. UI: ResultsManager panelleri; HUD'a Escape Timer (`EscapeTimerController.Remaining`),
   **Ruh Enerjisi barı** (`SpiritAbilityController.Energy/MaxEnergy` — HP barı DEĞİL, §6
   madde 1) ve Ruh Yeteneği cooldown göstergesi (`SpiritAbilityController.CooldownRemaining`).
7. GameFlow: Section 3 boss ölümünde `FinaleManager.Instance.ServerBeginFinale()` çağır.
8. Bölüm 8 içeriği geldiğinde: her hero prefab'ına `ISpiritAbilityEffect` implementasyonu
   (karaktere özgü cooldown + efekt).

## 5. Açık Sorular — ~~Kerem'e Taşınacak~~ CEVAPLANDI (2026-07-15)

Tüm sorular cevaplandı; kararlar ve uygulanışları §6'da. Tek kalan açık uç: Convergence Zone
AFK/troll kuralı "şimdilik hayır" dendi — playtest'te sorun çıkarsa yeniden açılacak.

## 6. Systems Design Kararları (Kerem, 2026-07-15) ve Uygulanışı

1. **Spirit'in HP'si yok; run boyunca hep aktif. Bar = skill kullanımında tükenen kaynak.**
   → `SpiritAbilityController`'a Ruh Enerjisi eklendi (`maxEnergy`, `energyCostPerUse`,
   opsiyonel regen — varsayılan 0, sadece tükenir; Escape başında full). FinaleManager'daki
   `OnSpiritHpDepleted` hook'u ve Defender ölüm dalı kaldırıldı — beden ölse bile Spirit
   sistemleri (vision + ability) DefenderClientId üzerinden çalışmaya devam eder.
2. **Defender disconnect oyunu kilitlemez; ekip Spirit'siz devam eder.**
   → `FinaleManager.ServerHandleClientDisconnected`: defender için sadece log; koşu sürer.
   Görüş/destek kaybı doğal ceza — ekstra mekanik yok.
3. **Min 2 / max 4 oyuncu.**
   → `NetworkGameManager.MinPlayers = 2` sabiti + `LobbyNetworkManager.StartGame` guard'ı.
   Runner sayısı zaten N-1 ile dinamik (2 kişide: 1 Runner + 1 Spirit).
4. **Tie-break tersine döndü: eşitlikte en DÜŞÜK HP'li Defender olur, kalan eşitlik rastgele.**
   → `VotingManager.BreakTie` güncellendi (en sağlıklı oyuncu Runner tarafında kalır).
5. **Tüm Runner'lar ölürse anında Game Over.** → Zaten böyle implement edilmişti; onaylandı.
6. **AFK/troll kuralı şimdilik yok.** → Değişiklik yok; playtest sonrası yeniden değerlendirilecek.
7. **Escape Timer 5-10 dk; Ruh Yeteneği cooldown'u karaktere göre.**
   → Timer alanı `Range(300, 600)`, varsayılan 420 sn. Cooldown, hero'daki
   `ISpiritAbilityEffect.SpiritAbilityCooldown`'dan okunur; component'teki değer sadece
   fallback (effect implementasyonu henüz olmayan hero'lar için).
