# Düşman Spawn Sistemi — Kurulum Rehberi

Yeni dosyalar: `Assets/_Project/Scripts/Enemies/Spawning/`

| Dosya | Görev |
|---|---|
| `SpawnSystemTypes.cs` | `SpawnNodeType` (Ground/Wall/Ceiling/Vent/Sand/Void) ve `EnvironmentalEventType` (NightPhase/Sandstorm/DebrisShower/Vacuum) flag enum'ları |
| `SectionEncounterSO.cs` | Bölüm başına veri paketi: Threat Capacity, Regular/Special havuzlar, event çarpanları, Mark of Guilt ayarları |
| `SpawnNode.cs` | Fiziksel üretim noktası. `IHackable`, hack durumu NetworkVariable ile replike, virüs enjeksiyonu spawn anında |
| `IHackable.cs` | Bahadır ultisi için hedefleme arayüzü |
| `SpawnDirector.cs` | AI Yönetmeni: kapasite bütçesi, frustum-dışı node seçimi, Attention/Special release, Guilt spawn |
| `EnvironmentalEventManager.cs` | Aktif çevresel olayların networked otoritesi |
| `NetworkEnemyPool.cs` | NGO `INetworkPrefabInstanceHandler` havuzu — `Despawn(true)` otomatik havuza döner |
| `FissionOnDeath.cs` | Ölümde bölünme (1→2→4), havuzdan çeker, threat bütçesine kaydolur |

Değişen dosyalar:
- `BaseEnemy.cs` — havuz-güvenli: her spawn'da `ResetServerState()` (agent, collider, threat tablosu, timer'lar, state, flash tint sıfırlanır). `Die()` yolunda değişiklik yok.
- `BahadirUltimateRuntime.cs` — kanal tamamlanınca `infectionRadius` içindeki tüm SpawnNode'ları hack'ler; eski yarıçap-pencere sistemi node-dışı spawnlar (fission vb.) için fallback olarak duruyor.

## Sahne Kurulumu (adım adım)

1. **SectionEncounterSO oluştur**: Project → Create → C-Building → Spawning → Section Encounter. Her bölüm için bir tane (`Enc_Forest`, `Enc_Desert`...). `sectionIndex` SectionManager bölüm numarasıyla eşleşmeli.
   - Regular Pool'a Grunt/Leaper vb. ekle: prefab, weight, threatCost, allowedNodeTypes, prewarmCount.
   - Event Modifiers örneği: `NightPhase` → [Stalker-Stitch, Nightstalker] × 3. Void için: `Vacuum/DebrisShower` → Juggernaut × 0, Wraithframe Drone × 3 (özel kod yok, tamamen data).
   - House için `guiltMarkEffect` (Mark of Guilt EffectDataSO) + `guiltSpawnEntry` (Family Echo) doldur.
2. **SpawnNode'lar**: Sahneye boş obje + `SpawnNode` + `NetworkObject` + Collider (isTrigger). `nodeType` seç, istersen `spawnPoint` child'ı ve `hackedVisual` VFX'i ata. Tavan node'ları için `navMeshSampleRadius`'u yükselt.
3. **Yönetici objeleri** (NetworkGameManager'ın yanına):
   - `SpawnDirector` + `NetworkObject`: tüm encounter SO'larını ata. Kamera rig değerlerini (offset/euler/FOV) Cinemachine'den kopyala — server her oyuncunun frustum'unu bu değerlerle yeniden kurar, ekranda spawn asla görünmez.
   - `EnvironmentalEventManager` + `NetworkObject`.
   - `NetworkEnemyPool` (NetworkObject gerekmez): aynı encounter SO'larını ata; Micro-Spawn gibi havuz-dışı prefabları `extraPrefabs`'a ekle. **Host ve client build'lerinde aynı liste atanmalı** (client tarafı handler'lar için).
4. **Düşman prefabları**: hepsi Network Prefabs listesinde olmalı (mevcut kural). Fission davranışı için prefaba `FissionOnDeath` ekle, `childPrefab` ata.

## Çalışma Mantığı Özeti

- Director yalnızca sunucuda çalışır. `threatCapacity` bütçesi doluysa spawn durur; ölüm `OnDied` ile bütçeyi geri açar (+5 sn'de bir güvenlik süpürmesi).
- Node seçimi: tip eşleşmesi → cooldown → mesafe bandı (min 6 m / max 40 m) → tüm oyuncuların frustum'u dışında.
- Attention: pasif + `SpawnDirector.ReportAttention(x)` (yetenek kullanımına bağlamak için AbilityController'dan çağır). NightPhase aktifken çarpanlı. Eşik dolunca Special Pool'dan bir düşman salınır.
- Olaylar: `EnvironmentalEventManager.Instance.ServerSetEvent(EnvironmentalEventType.Sandstorm, true)` — ağırlıklar anında değişir.
- Bahadır ultisi: hack'li node'dan doğan her düşmana Spyware, doğduğu frame'de `StatusEffectController.ApplyEffect` ile basılır. Efektin `stackingPolicy`'si çakışmaları çözer.

## Spawning State ve Giriş Animasyonu

Düşmanlar artık ekran içinde de doğabilir; "pat diye belirme" yerine senkronize bir **Spawning State** var.

**Akış:** Spawn → `spawnEntryDuration` boyunca Spawning (hedeflenemez: collider'lar kapalı; hasar almaz: `TakeDamage` erken çıkar; hareketsiz: agent + beyin kapalı) → süre bitince otomatik aktif.

**Ağ tasarımı — neden ClientRpc değil:** `IsSpawning` bir NetworkVariable ve başlangıç değeri spawn paketinin İÇİNDE gider. Her peer (host, client, geç katılan) bayrağı görüp giriş animasyonunu **lokal** oynatır — ekstra mesaj yok, spawn'la yarışan RPC yok, geç katılan kaçırmaz. DOTween/animasyon dosyası bağımlılığı da yok: coroutine + `AnimationCurve`.

**Inspector kurulumu (prefab başına):**

1. `BaseEnemy` → *Spawn Entry* → `spawnEntryDuration` (örn. 1.2 sn; 0 = anında aktif, eski davranış).
2. Prefaba `EnemySpawnEntryPresenter` ekle:
   - `visualRoot`: sprite/mesh CHILD objesi (networked root DEĞİL — NetworkTransform root'un sahibi, presenter yalnızca child'ı local space'te oynatır).
   - `style`: `RiseFromGround` (topraktan çıkma — Shambler, Desert Worm; `riseDepth` 2 m), `DropIn` (tavandan düşme — Ceiling Spider; `dropHeight` 3 m), `Materialize` (0→1 scale — Void düşmanları, Family Echo), `AnimatorOnly` (klip varsa trigger atar).
   - `ease`: EaseOut = kazma hissi, EaseIn = ağır düşüş. Süreyi presenter `BaseEnemy.SpawnEntryDuration`'dan okur — görsel ve dokunulmazlık aynı frame'de biter.
   - `entryParticles` (ops.): toprak fışkırması vb. — düşmanın child'ı olsun ki havuzla birlikte geri dönsün.
3. `SpawnDirector` → `useFrustumCheck` KAPALI ise node seçimi yalnız mesafe bandıyla yapılır, ekran içi spawn serbest. Koddan da değişir: `SpawnDirector.Instance.UseFrustumCheck = false;` (örn. Sandstorm sırasında kumdan gözle görülür worm çıkışları için).

Havuz güvenliği: `ResetServerState()` her yaşamda Spawning state'i yeniden başlatır — geri dönüştürülen düşman, girişini bitirmeden asla aggro çekmez, hasar almaz, hedeflenemez. Spawn anında basılan Spyware DoT'u da dokunulmazlık penceresinde tick atmaz (kayıp ~1 sn, tasarım gereği).

## Test Sırası (yol haritandaki 5 adım)

1. SO'ları oluştur, Inspector'da doldur.
2. 3-4 node koy, Director olmadan test: `node.ServerSpawnEnemy(prefab)` debug tuşuyla çağır.
3. Ultiyi node üstünde test et: hack → spawn → Spyware ikonu HUD'da görünmeli.
4. Director'ü aç, node'ların yalnızca ekran dışından ürettiğini doğrula (Scene view'da izle).
5. `FissionOnDeath` zinciriyle havuzu stres-test et: Profiler'da `Instantiate` çağrısı görünmemeli (yalnızca prewarm anında).
