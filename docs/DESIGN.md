# LoL Draft Oyunu — Tasarım Dokümanı

Sürüm: 0.3 · Tarih: 1 Kasım 2026 · Durum: tasarım + çalışan prototip (`prototype/mac-motoru.html`)

---

## 0. Özet

İki (ileride dört) oyuncu, arkadaşına link atıp LoL turnuva draft'ı yapar: ban, pick, bo1/bo3/bo5, fearless. Draft bittikten sonra maçı oyuncu oynamaz; **gizli bir puan motoru** maçı simüle eder ve sonuç, haritada hareket eden şampiyon ikonları ve yanda akan bir olay log'u ile "izlenen bir maç" olarak gösterilir. Matematik oyuncudan gizlidir, maç sonunda "neden kazandın?" dökümü olarak açılır.

Temel ilkeler:

- **Beceri draft'ta, hikâye maçta.** Kazanma ihtimalini draft belirler, maç bu ihtimali izlenebilir bir hikâyeye çevirir.
- **Matematik önce, görüntü sonra.** Motor sadece sayılarla çalışır. Ekranda görünen her kill, kule ve objektif arkadaki puan değişiminin karşılığıdır.
- **Maçı her zaman bir savaşı kazanan bitirir.** Puan sadece savaş ihtimalini etkiler.
- **Erken bitiş mümkün, scale garanti değil.** Erken comp 20-25. dakikada kuşatmayla bitirebilir; bitiremezse scale comp gelir.
- **Şans var ama ölçülü.** Her olay ihtimali %10-%90 arasında; hiçbir şey kesin değil.
- **Kâr amacı yok.**

---

## 1. Karar günlüğü

| Konu | Karar | Neden / reddedilen alternatif |
|---|---|---|
| Maç içi oyuncu etkisi | Maç tamamen motor tarafından oynanır. Oyuncunun girdisi draft (+ ileride maç öncesi koç direktifi). | Reddedildi: oyuncunun her fazda puan dağıttığı "Koç Masası" sistemi ("bütün güçleri ben seçiyorum, olmaz"). Reddedildi: maç içi kart sistemi (kafaya yatmadı). Reddedildi: tamamen zar. |
| Kazanma mantığı | Gizli takım puanı + puandan etkilenen savaş olasılıkları. Maçı son savaşı / kuşatmayı kazanan bitirir. | v1 prototipte "puanı yüksek olan kazanır" vardı; savaşı kaybedenin oyunu bitirmesi mantıksızdı, değiştirildi. |
| Görselleştirme | Hesap motoru + Anlatıcı (olay bütçesi) + zamanlı olay listesi; front sadece oynatır. | Football Manager maç motoru mantığı. |
| Hareket | Şimdilik senaryo kuralları (v2 prototip). Hedef: gerçek maç timeline'larından bölge geçiş olasılıkları (Markov tablosu). ML yok. | ML gereksiz; dakikalık konum verisiyle Markov yeterli ve basit. |
| Stack | ASP.NET Core + SignalR (tek API uygulaması) önünde YARP gateway, React + TypeScript, SQLite ile başla. Veri hattı Python. | Gateway; tek giriş noktası, rate limiting, TLS ve auth için sonradan eklendi. Mikroservis, Redis, Hangfire MVP için gereksiz. Node/TS alternatifi değerlendirildi; motor paylaşımına gerek olmadığı ve Monte Carlo ağır olduğu için .NET. |
| Güvenlik | Sunucu otoriter, kayıtsız misafir JWT, iki katmanlı rate limiting (gateway + hub), katmanlı mimari ve sıkı kapsülleme. | §8 |

---

## 2. Oyun akışı

1. **Lobi.** Mod (1v1, ileride 2v2), seri (bo1/bo3/bo5), draft kuralı (normal, soft fearless, full fearless), şampiyon havuzu. Arkadaş link ile davet edilir, kayıt yok.
2. **Draft.** Turnuva sırası, her adım 30 sn:
   - Ban fazı 1: M1 K1 M2 K2 M3 K3
   - Pick fazı 1: M1 · K1 K2 · M2 M3 · K3
   - Ban fazı 2: K4 M4 K5 M5
   - Pick fazı 2: K4 · M4 M5 · K5
   - Swap fazı: şampiyonlar rollere atanır (top, jg, mid, adc, sup).
   (M = Mavi, K = Kırmızı)
3. **(İleride) Koç direktifi.** Draft bitince 20 sn içinde maç planı seçilir (§4.10). Güç vermez, olayların dağılımını değiştirir.
4. **Simülasyon.** Sunucu maçı seed ile hesaplar, olay listesini gönderir; istemci 1 dk oyun = 3 sn hızında oynatır (2x, 4x, sona atla).
5. **Maç sonu.** Kazanan, bitiş türü, son savaşın ihtimali, puan dökümü. (İleride: draft kalitesi Monte Carlo, paylaşım kartı.)
6. **Seri.** Seri devam ediyorsa fearless kilitleri uygulanır; kaybeden taraf seçer.

**Fearless:**
- **Full:** Seride seçilen şampiyon iki takım için de kilitlenir.
- **Soft:** Sadece seçen takım için kilitlenir (bir şampiyon seride en fazla iki kez, her takımda bir).
- Banlar sonraki maça taşınmaz.

**2v2 (ileride):** Takımda iki kişi: biri top/jg/mid, diğeri adc/sup draft eder; sıra takım arkadaşları arasında döner.

---

## 3. Şampiyon veri modeli

```json
{ "id": "renekton", "name": "Renekton", "roles": ["top"],
  "e": 9, "m": 6, "l": 4,          // güç eğrisi: erken (<14 dk), orta (14-25), geç (25+), 1-10
  "fl": 2, "en": 2, "peel": 0, "poke": 0, "pick": 1,   // etiketler 0-3
  "ap": 0                          // AP payı: 0 = AD, 0.5 = karışık, 1 = AP
}
```

- MVP: **40 şampiyon**, elle etiketlenir (1-2 günlük iş).
- Kullanılan etiketler (şu an motorda): `fl` (frontline), `en` (engage), `ap`. Diğerleri ileride kompozisyon kimliği için (Engage, Pick, Poke, Koruma, Split).
- **Matchup tablosu (ileride, veriden):** Aynı koridordaki iki şampiyonun kazanma oranı, %50'ye doğru çekilerek: `(wr × n + 0.5 × 200) / (n + 200)`. Örnek: 520 maçta %53 → %52,2.
- **Güç eğrisi (ileride, veriden):** Maç süresine göre kazanma oranı (<25 dk vs >35 dk) → erken/geç değerleri otomatik çıkar ve her yamada güncellenir.

### Prototipteki takımlar

| Takım | Top | Jg | Mid | Adc | Sup |
|---|---|---|---|---|---|
| Mavi (erken) | Renekton 9-6-4, fl2 en2 | Lee Sin 9-6-3, en2 | LeBlanc 8-7-4, AP | Draven 9-6-5 | Leona 8-6-5, fl3 en3, AP |
| Kırmızı (scale) | Kayle 2-6-10, ap .5 | Sejuani 5-7-7, fl3 en3, AP | Kassadin 3-6-10, AP | Jinx 4-7-9 | Lulu 6-6-7, AP |

---

## 4. Simülasyon motoru

Saf, yan etkisiz bir fonksiyon: `simulate(draft, seed) → { events, frames, categories, end }`. Aynı seed aynı maçı üretir.

### 4.1 Rastgelelik
- RNG: mulberry32(seed). Normal dağılım: Box-Muller.
- Sunucu otoriter: zar sunucuda atılır, istemci hesap yapmaz.

### 4.2 Puan ve kategoriler
Tek bir **toplam puan farkı** vardır (pozitif = Mavi önde). Toplam, kategorilerin toplamıdır; maç sonu dökümü bu kategorilerle gösterilir:

`sinerji, top, mid, bot, orman, catisma (nehir çatışmaları), kusatma, ejder, herald, baron, elder, final (son savaş), surpriz`

### 4.3 Faz gücü
`G(şampiyon, dk) = e (dk < 14) | m (14 ≤ dk < 25) | l (dk ≥ 25)`

- Koridor gücü: koridordaki şampiyonların G ortalaması (bot = adc + sup ortalaması).
- Takım gücü: 5 şampiyonun G toplamı.

### 4.4 Başlangıç puanı: draft sinerjisi

| Kural | Puan |
|---|---|
| En az bir şampiyonda fl ≥ 2 | +3, yoksa −4 |
| En az bir şampiyonda en ≥ 2 | +2 |
| Ortalama AP payı %30-%70 | +2, değilse −3 |
| Kombo tablosundaki her ikili | +2 (ör. Kayle+Lulu, Leona+Draven) |

`sinerji = sinerji(Mavi) − sinerji(Kırmızı)`

### 4.5 Dakika döngüsü (1..35)

Her dakika sırasıyla: koridorlar → orman → nehir çatışması → objektif → kuşatma kontrolü → frame kaydı.

**Koridor akışı** (her koridor, her dakika):
```
Δ = 0.1 × (G_koridor_Mavi − G_koridor_Kırmızı) + matchup[koridor] + N(0, 0.8)
```
- `matchup` prototipte sabit: top +0.1, mid +0.05, bot 0 (ileride veriden).
- Δ hem `kategori[koridor]` hem koridor puanı hem anlatıcı bütçesine eklenir.
- **Sürpriz:** her koridorda her dakika %2 ihtimalle rastgele bir takım lehine 3-5 puan (`surpriz` kategorisi).

**Orman:**
- Her dakika: `0.05 × (G_jg_Mavi − G_jg_Kırmızı) + N(0, 0.3)` → `orman`.
- Gank dakikaları: 3, 6, 9, 12, 18, 24. Gank'ı atan taraf: `p_Mavi = 1 / (1 + e^(−(G_jg farkı)/4))`. Koridor rastgele.
- Lane fazında (<14 dk) ve koridor bot değilse support %45 ihtimalle gank'a katılır.
- Başarı: tek başına %60, support'la %75. Başarı → `orman` ±4 (support'la ±5), koridor puanı ±3, kill. Başarısızlık → `orman` ∓2.

**Nehir çatışması** (dk 8, 13, 18, 26; her biri %75 ihtimalle):
- 2v2 ya da 3v3 (%50/%50). Katılımcı önceliği: jg, sup, mid, adc, top (yaşayanlar).
- `p_Mavi = clamp(1 / (1 + e^(−(ΣG_Mavi − ΣG_Kırmızı + 0.1 × toplam)/6)))`
- Kazanan `catisma` ±3, kaybedenlerden 1-2 kill.

**Objektifler:**

| Dakika | Objektif | Puan |
|---|---|---|
| 6, 11, 16, 21, 27 | Ejder | 4 |
| 14 | Herald | 4 |
| 23 | Baron | 8 |
| 30 | Elder | 12 |
| 35 | Son takım savaşı | 15 (maçı bitirir) |

Takım savaşı ihtimali:
```
d      = (Takım gücü Mavi − Takım gücü Kırmızı) + sinerji farkı + 0.2 × toplam puan
p_Mavi = clamp(1 / (1 + e^(−d/15)), 0.1, 0.9)
```
- Geride olan takım objektifi kazanırsa puan ×1.25 (geri dönüş).
- Savaşta kazanandan kaybedene kill'ler: Ejder 1-2, Baron/Herald/Elder 1-3, son savaş 1-4.

### 4.6 Maç sonu kuralları
- **Kuşatma:** dk ≥ 20, o dakika objektif yoksa, |toplam| ≥ 45 ve son kuşatmadan bu yana ≥ 3 dk geçtiyse önde olan takım kuşatmaya gider. İhtimal = takım savaşı ihtimali (önde olanın tarafından).
  - Kazanırsa: 1-4 kill, **Nexus düşer, maç biter**.
  - Kaybederse: savunan `kusatma` +10, savunandan 1-3 kill, log'a "Throw!".
- **Son savaş (dk 35):** Kazanan Nexus'a yürür, maç biter. Puanda önde olan genelde favori ama kaybedebilir.
- Maç bittikten sonraki olaylar listeden atılır.

### 4.7 Ölüm
- Yeniden doğma süresi: `10 + t/60` saniye (t = oyun saniyesi).
- Ölü şampiyon olaylara (kill, gank, çatışma) katılamaz.
- İlk kill "İlk kan!" ön ekiyle yazılır.

### 4.8 Anlatıcı (olay bütçesi)
Koridorda biriken Δ bir bütçede tutulur. Her dakika, bütçenin işaretine göre önde olan taraf için **en fazla bir** görünür olay üretilir (kontrol sırası):

| Koşul | Olay | Bütçeden düşülen |
|---|---|---|
| \|bütçe\| ≥ 8, önde olan ve rakipten biri yaşıyor | Solo kill | 8 |
| dk ≥ 14, \|bütçe\| ≥ 10, kule < 3 | Kule (dış → iç → inhibitör) | 10 |
| 5 ≤ dk < 14, \|bütçe\| ≥ 5 | Kule plakası | 5 |
| \|bütçe\| ≥ 3, son CS olayından ≥ 4 dk geçti | CS farkı | 3 |

Olay zamanı: dakikanın 25-55. saniyeleri arası rastgele. Bütçe sadece anlatım içindir; puan zaten Δ ile eklenmiştir.

### 4.9 Olay şeması

```json
{ "t": 427, "type": "kill", "side": "blue",
  "killer": "b0", "victim": "r0", "until": 444, "lane": "top",
  "text": "Renekton üst koridorda öne çıktı, Kayle düştü" }
```

| type | Alanlar | Not |
|---|---|---|
| `kill` | killer, victim, until, lane? | until = yeniden doğma anı |
| `cs`, `plate` | side, lane | |
| `tower` | side, lane, k (0 dış, 1 iç, 2 inhib) | |
| `objective` | obj, side, pct | pct = kazananın ihtimali |
| `skirmish` | side, pct, loc | |
| `surprise` | side, lane, val | |
| `gankfail`, `throw` | side, lane? | |
| `move` | parts[], lane? / loc?, lead, tail | Görünmez; hareket kısıtı |
| `siege` | side, lead, tail | Görünmez; kuşatma toplanması |
| `nexus` | side, pct? | Maç sonu |

`text` boş olan olaylar log'a yazılmaz, sadece hareketi yönlendirir.

### 4.10 Koç direktifi (ileride, maç öncesi)

| Direktif | Etki | Bedel |
|---|---|---|
| Bot lane baskısı | Gank'lar bot'a daha sık | Top yalnız kalır |
| Ormancı istilası | 3-4. dk'da ekstra istila olayı | Kaybedilirse ormancı geride |
| Objektif odaklı | Objektif savaşlarında küçük bonus | Koridor akışı biraz düşer |
| Split | Yan koridor kule olayları artar | Takım savaşlarında 1 kişi eksik sayılır |
| Scale oyna | Lane'de büyük kayıp ihtimali düşer | Erken objektifler daha kolay verilir |
| Hızlı bitir | Kuşatma eşiği düşer (45 → 35) | Throw cezası artar |

Draft'a uymayan direktif küçük bir ceza alır.

---

## 5. Hareket sistemi

### 5.1 Şu anki kurallar (prototip v2)
Öncelik sırası (ilk eşleşen kazanır):

1. **Ölü** → kendi base'i.
2. **Objektif** → olaydan 22 sn önce ile 5 sn sonrası arasında herkes çukura (takımlar karşılıklı dizilir).
3. **Kuşatma / son savaş** → savunan base'in önündeki savaş noktası.
4. **Move olayı** (gank, çatışma) → ilgili koridor noktası ya da nehir konumu (14 sn önce, 4 sn sonra).
5. **Recall** → her şampiyon ~270 sn'de bir, 14 sn base'te (şampiyona göre kaydırılmış).
6. **Faz yeri:**
   - <14 dk: koridorunda; cephe = `0.5 + clamp(koridor puanı / 30, ±0.28)` yolun oranı. Ormancı kendi kampları arasında 18 sn'de bir dolaşır.
   - 14-25 dk: bot duo mid'e geçer; top kendi koridorunda; ormancı nehir ile kamplar arasında gidip gelir.
   - ≥25 dk: top yan koridorda split, diğer dördü mid'de grup.

İkonlar hedefe yumuşak geçişle (lerp) ilerler.

### 5.2 Hedef sistem: veri tabanlı bölge geçişleri (Markov)
1. **Bölgeler:** Harita (~15.000 × 15.000 birim) 15×15 ızgaraya bölünür. Her hücre elle 23 bölgeden birine atanır: top/mid/bot × dış/iç kule × mavi/kırmızı, nehir üst/alt, ormanlar (mavi/kırmızı × üst/alt), ejder/baron çukuru, base'ler ve `mid_center` (`data-pipeline/out/zones.json`, araç: `data-pipeline/tools/zone-painter.html`). Boyama 180° simetriktir (mavi↔kırmızı, üst↔alt, baron↔ejder). Merkez hücre kendi kendisinin simetriği olduğu için iki tarafa da ait olmayan `mid_center` bölgesidir; böylece kendi/rakip çevirisinde iki taraf eşit kalır.
2. **Tablo anahtarı:** `rol | faz | durum | şimdiki bölge` → sonraki bölge olasılıkları.
   - Faz: 0-8, 8-14, 14-25, 25+ dk.
   - Durum: takım altın farkı ≥ +1500 önde, ≤ −1500 geride, arası eşit.
   - Bölgeler oyuncunun takımına göre **kendi/rakip** adlandırılır (`top_outer_blue` mavi için `top_outer_own`, kırmızı için `top_outer_enemy`); iki takımın verisi aynı satırlarda birleşir.
   - Oyuncunun ölü olduğu dakikalar tabloya girmez (ölüm → base kuralı motorda).
3. **Seyreklik:** Üç seviye satır yazılır: tam, durumsuz (`ROL|faz|*|bölge`), faz+durumsuz (`ROL|*|*|bölge`). Satırda 30'dan az gözlem varsa bir üst seviyeye geçilir. Satırda görülmemiş komşu bölgelere (ızgarada kenar paylaşan) 0,5 sahte gözlem eklenir.
4. **Çalışma zamanı:** Motor olayları üretir. Olaylar **kısıttır** (ör. "Leona 10:30'da ejder çukurunda"). Kısıt yoksa her dakika tablodan sonraki bölge örneklenir.
5. **Ara hareket:** Koridor ve nehir boyunca çizilmiş bir yol ağı (düğüm + kenar); bölgeler arası en kısa yol. İkonlar duvardan geçmez.
6. **Kill konumları:** Gerçek kill konumlarından faz ve bölge bazlı ısı haritası; anlatıcı kill'i bu dağılımdan bir noktada gösterir.
7. **Doğrulama:** 1.000 simülasyonun bölge dağılımı (rol başına zamanın % kaçı nerede, bot duo mid'e kaçıncı dk geçiyor, kill'lerin bölge dağılımı) gerçek veriyle karşılaştırılır.

`transitions.json` biçimi (`data-pipeline/04_transitions.py` üretir):
```json
{ "version": 1, "patches": ["16.20", "16.21"], "matches": 495, "min_n": 30,
  "zones": ["base_own", "top_outer_own", "river_top", "mid_center", "…"],
  "adjacency": { "base_own": ["base_own", "bot_inner_own", "…"] },
  "rows": { "UTILITY|8-14|even|bot_outer_own": { "n": 257,
            "next": { "river_bot": 0.20, "bot_outer_own": 0.15, "base_own": 0.13, "…": 0 } },
            "UTILITY|8-14|*|bot_outer_own": { "…": 0 }, "UTILITY|*|*|bot_outer_own": { "…": 0 } } }
```
`kill_heatmap.json`: `"faz|mutlak bölge"` → `[x, y, kill alan takım]` listesi (Riot koordinatı).

İlk ölçüm (495 maç, 16.19-16.21): 94 bin geçiş; geçişlerin %7,4'ü durumsuz satıra, %1,2'si faz+durumsuz satıra düşüyor (eşik %10-15 → 500 maç yeterli). Yüksek eloda maç başı ~55 kill.

---

## 6. Veri hattı (Python, tek seferlik)

Klasör: `data-pipeline/`. Anahtar ortam değişkeninde: `RIOT_API_KEY`. Development anahtarı 24 saatte bir yenilenir; offline analiz için yeterli.

| Adım | Betik | Uç nokta | Çıktı |
|---|---|---|---|
| 1 | `01_players.py` | `/lol/league/v4/challengerleagues/by-queue/RANKED_SOLO_5x5` ve `grandmasterleagues` (platform: `euw1`) | `players.json` (puuid) |
| 2 | `02_match_ids.py` | `/lol/match/v5/matches/by-puuid/{puuid}/ids?queue=420&count=20` (bölge: `europe`) | `match_ids.json` (tekrarsız) |
| 3 | `03_download.py` | `/lol/match/v5/matches/{id}` ve `/lol/match/v5/matches/{id}/timeline` | `raw/{id}.json`, `raw/{id}_tl.json` |
| 4 | `04_transitions.py` (Jupyter da olur) | — | `out/transitions.json`, `out/kill_heatmap.json` |

- Timeline: dakikada bir kare (`frameInterval` 60000 ms); her karede `participantFrames["1".."10"].position {x, y}`, altın, CS. Rol: maç verisinde `participants[].teamPosition` (TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY).
- İndirme betiği var olan dosyayı atlar (kaldığı yerden devam), 429 cevabında `Retry-After` kadar bekler.
- Maç sayısı: prototip **500**, ilk sürüm **2.000**, ince ayar **5.000+**. Maç başı 2 istek; 2 dk'da 100 istek limitiyle 2.000 maç ≈ 80 dk.
- Son 2-3 yama karıştırılabilir.

---

## 7. Ön yüz

- **Harita:** Kendi çizimimiz. 600×600 koordinat; Mavi base sol alt (78, 522), Kırmızı sağ üst (522, 78).
  - Koridor yolları: top (70,530)→(70,70)→(530,70); mid (70,530)→(530,70); bot (70,530)→(530,530)→(530,70).
  - Ejder çukuru (400, 400), Baron/Herald (200, 200).
- **İkonlar:** Takım rengi çerçeveli daire + kısaltma (ileride kendi ikonlarımız). Ölü ikon %30 opak, base'te.
- **Oynatma:** 1x = 20 oyun saniyesi / gerçek saniye (1 dk = 3 sn), 2x, 4x, sona atla. Olay zamanı gelince log'a yazılır, haritada kısa bir işaret çıkar (✕, "Ejder", "Çatışma", "Throw!").
- **HUD:** Kill skoru, saat, altın farkı (`toplam puan × 110`, 100'e yuvarlanmış), ejder sayısı, yıkılan kule sayısı, kule ikonları.
- **Geliştirici modu ("Matematiği göster"):** Canlı toplam ve koridor puanları, log'da olay ihtimalleri.
- **Maç sonu:** Kazanan, dakika, bitiş türü, son savaşın ihtimali, kategori bazlı ıraksak çubuk grafik, sinerji maddeleri. Puanda önde olan kaybettiyse açıklama satırı.
- **Denge paneli:** "1000 maç oynat" (seed = i × 7919).

---

## 8. Sistem tasarımı

### 8.1 Genel bakış

Dışarıya tek bir giriş noktası açılır: **Gateway**. Oyun API'si ve SignalR hub'ı sadece iç ağda çalışır, internetten doğrudan erişilemez. Simülasyon motoru ASP.NET'ten bağımsız saf bir kütüphanedir. Veri hattı offline çalışır; oyun sunucusu Riot API'sine hiç bağlanmaz ve Riot anahtarını tutmaz.

```
 İnternet
    │  HTTPS / WSS (443)
    ▼
┌───────────────────────────────┐
│ Gateway (YARP, ASP.NET Core)  │  TLS · güvenlik başlıkları · CORS · rate limiting
│                               │  JWT doğrulama · istek boyutu sınırı · correlation id
└──────────────┬────────────────┘
               │ iç ağ (docker network), sadece gateway erişebilir
               ▼
┌───────────────────────────────┐
│ Game.Api                      │  Minimal API'ler + DraftHub (SignalR)
│  └ Application (use case'ler) │  CreateLobby, JoinLobby, SubmitDraftAction, StartMatch, GetReplay
│     ├ Domain (Draft, Lobby)   │  draft durum makinesi, kurallar, fearless kilitleri
│     └ Engine (simülasyon)     │  saf, seed'li, deterministik
│  └ Infrastructure             │  EF Core + SQLite, şampiyon/tablo JSON yükleyici, saat
└──────────────┬────────────────┘
               ▼
         SQLite (volume)

 data-pipeline (Python, offline) ──► out/*.json ──► Infrastructure'a dosya olarak kopyalanır
```

Web istemcisi (React build'i) statik dosya olarak gateway üzerinden sunulur. Böylece tarayıcı tek bir origin görür ve CORS minimum kalır.

**Neden gateway?** Tek uygulama için zorunlu değil. Ama tek giriş noktası; rate limiting, TLS, güvenlik başlıkları ve kimlik doğrulamayı API kodundan ayırır. İleride ikinci bir servis (ör. ayrı bir simülasyon işçisi) eklendiğinde dışarıya hiçbir şey değişmez. Bedeli ek bir süreç ve bir ağ sıçraması.

### 8.2 Projeler ve katmanlar

```
src/
  LolDraft.Engine/          saf simülasyon (bağımlılık yok)
  LolDraft.Domain/          Lobby, DraftSession, kurallar (Engine'e bağımlı değil)
  LolDraft.Application/     use case'ler, port arayüzleri (Domain + Engine'e bağımlı)
  LolDraft.Infrastructure/  EF Core, JSON yükleyiciler, saat, CSPRNG (Application portlarını uygular)
  LolDraft.Api/             HTTP uç noktaları, DraftHub, DTO'lar, doğrulama (Application'a bağımlı)
  LolDraft.Gateway/         YARP reverse proxy, rate limiting, auth, güvenlik başlıkları
tests/
  LolDraft.Engine.Tests/    deterministiklik, denge
  LolDraft.Domain.Tests/    draft durum makinesi, fearless
  LolDraft.Api.Tests/       uç nokta + yetki testleri (WebApplicationFactory)
```

Diğer klasörler: `web/` (React + TypeScript), `data-pipeline/` (Python), `prototype/mac-motoru.html` (referans JS motoru; `LolDraft.Engine` aynı kuralları uygular, aynı seed'le birebir aynı sonucu vermesi gerekmez).

**Bağımlılık kuralı:** Oklar içe doğru. `Api → Application → Domain / Engine`. `Infrastructure`, `Application`'daki arayüzleri uygular. `Domain` ve `Engine` ASP.NET, EF Core ya da dosya sistemi bilmez.

### 8.3 Kapsülleme kuralları

- **Varsayılan görünürlük `internal`.** Bir proje dışarıya sadece kendi giriş arayüzünü açar; testler `InternalsVisibleTo` ile erişir.
- **Sınıflar `sealed`**, kalıtım için açıkça tasarlanmadıysa.
- **Public setter yok.** Durum sadece anlamlı metotlarla değişir ve her metot kendi kuralını kontrol eder.
- **Değer nesneleri** ilkel tipler yerine: `ChampionId`, `LobbyCode`, `PlayerId`, `Seed`, `TeamSide`. Kurucu doğrular; geçersiz değer hiç oluşamaz.
- **Olaylar ve sonuçlar değişmez:** `record` + `IReadOnlyList`.
- **Hata = sonuç nesnesi**, beklenen iş hataları için exception değil: `Result<T>` (`NotYourTurn`, `ChampionUnavailable`, `PhaseClosed`…).

**Engine'in dışa açtığı yüzey (tamamı bu kadar):**
```csharp
public interface IMatchSimulator
{
    MatchResult Simulate(MatchInput input);           // aynı input → aynı sonuç
}
public sealed record MatchInput(TeamDraft Blue, TeamDraft Red, Seed Seed, Directive? BlueDirective, Directive? RedDirective);
public sealed record MatchResult(IReadOnlyList<MatchEvent> Events, ScoreBreakdown Breakdown, MatchEnd End);
```
`SynergyCalculator`, `LaneFlow`, `Narrator`, `FightResolver`, `MovementPlanner`, `IRandomSource` gibi parçalar `internal sealed`.

**Domain örneği: draft durum makinesi:**
```csharp
public sealed class DraftSession
{
    private readonly List<DraftAction> _actions = new();
    public DraftPhase Phase { get; private set; }
    public TeamSide Turn { get; private set; }
    public IReadOnlyList<DraftAction> Actions => _actions;

    public Result Ban(PlayerSeat seat, ChampionId champion, Instant now) { /* sıra, faz, süre, havuz, fearless kontrolü */ }
    public Result Pick(PlayerSeat seat, ChampionId champion, Instant now) { /* aynı kontroller */ }
    public Result Swap(PlayerSeat seat, RoleAssignment roles) { /* sadece swap fazında */ }
    internal Result Timeout(Instant now) { /* sunucu sayacı: boş ban / rastgele uygun pick */ }
}
```
Dışarıdan `Phase` ya da `Turn` set edilemez; geçersiz hamle `Result.Fail` döner ve durum değişmez.

**Application portları:**
```csharp
public interface ILobbyRepository { Task<Lobby?> Get(LobbyCode code, CancellationToken ct); Task Save(Lobby lobby, CancellationToken ct); }
public interface IMatchRepository { Task Save(MatchRecord record, CancellationToken ct); Task<MatchRecord?> Get(MatchId id, CancellationToken ct); }
public interface IChampionCatalog { bool Exists(ChampionId id); ChampionData Get(ChampionId id); }
public interface ISeedGenerator { Seed Next(); }   // CSPRNG
public interface IClock { Instant Now { get; } }
```

### 8.4 Kimlik ve yetki (kayıtsız)

Kayıt yok ama her istek bir kimliğe bağlı.

1. **Lobi oluşturma:** `POST /api/lobbies` → sunucu `LobbyCode` (128 bit rastgele, URL-güvenli) ve oluşturana bir **misafir JWT'si** üretir.
2. **Davet linki:** `/l/{LobbyCode}`. Kod tahmin edilemez; boş koltuk bir kez alınır.
3. **Katılma:** `POST /api/lobbies/{code}/join` → ikinci oyuncuya JWT.
4. **JWT içeriği:** `sub` (PlayerId), `lobby` (LobbyCode), `seat` (Blue/Red), `exp` (2 saat). HMAC-SHA256 ile imzalanır, anahtar ortam değişkeninde.
5. **Yeniden bağlanma:** JWT ile birlikte uzun ömürlü olmayan bir `rejoinToken` (tek kullanımlık, lobi ömrü kadar) verilir. Sayfa yenilenirse koltuk geri alınır.
6. **Doğrulama:** Gateway JWT imzasını ve süresini kontrol eder; geçersizse isteği API'ye hiç iletmez. API ayrıca **yetkiyi** kontrol eder: oyuncu bu lobide mi, bu koltukta mı, sıra onda mı. Yetki kontrolü domain'in işidir, gateway'in değil.
7. SignalR bağlantısında token `access_token` sorgu parametresiyle gelir (WebSocket başlık gönderemez); gateway ve hub bu parametreyi okur. Bu parametre loglara yazılmaz.

### 8.5 Rate limiting

İki katmanda: HTTP istekleri gateway'de, SignalR mesajları API'de (bir WebSocket bağlantısı içindeki mesajlar gateway'in HTTP limitlerinden geçmez).

**Gateway politikaları** (ASP.NET Core `RateLimiter` + YARP route politikası):

| Politika | Uygulandığı yer | Bölümleme | Limit |
|---|---|---|---|
| `global` | tüm rotalar | IP | token bucket, 10 sn'de 100 istek |
| `lobby-create` | `POST /api/lobbies` | IP | sabit pencere, dakikada 5 |
| `lobby-join` | `POST /api/lobbies/{code}/join` | IP | dakikada 10 |
| `replay` | `GET /api/matches/{id}` | IP | dakikada 60 |
| `hub-connect` | `/hubs/draft` (negotiate + WS) | IP | eşzamanlı en fazla 5 bağlantı |
| `simulate-heavy` | Monte Carlo / "ne olurdu?" uçları | oyuncu (JWT `sub`) | eşzamanlılık: sunucu genelinde 4 iş, kuyruk 20; oyuncu başına dakikada 3 |

**Hub içi limit** (`IHubFilter`, API'de): bağlantı başına saniyede 5 mesaj, dakikada 60. Aşılırsa mesaj reddedilir; tekrar ederse bağlantı kapatılır.

- Aşımda `429 Too Many Requests` + `Retry-After` döner. Hub'da istemciye `RateLimited` olayı gider.
- IP, gateway önünde başka bir proxy varsa `ForwardedHeaders` ile ve **sadece güvenilen proxy listesinden** okunur; aksi halde `X-Forwarded-For` taklit edilip limit atlatılabilir.
- Limitler bellekte tutulur (tek instance). Birden fazla gateway instance'ına geçilirse dağıtık limit (Redis) gerekir.

### 8.6 Güvenlik önlemleri

**Oyun bütünlüğü**
- Sunucu otoriter: istemci sadece niyet gönderir (`BanChampion`, `PickChampion`). Sonuç, zar ve puan istemciden asla alınmaz.
- Her draft hamlesi domain'de doğrulanır: sıra, faz, sayaç, şampiyonun havuzda olması, ban/pick tekrarı, fearless kilidi.
- Seed sunucuda `RandomNumberGenerator` (CSPRNG) ile üretilir ve maç bitene kadar istemciye gönderilmez; tekrar izleme maç sonrası açılır.
- Gizli bilgi (ileride eşzamanlı gizli seçimler) çözülene kadar yayınlanmaz; draft durum DTO'su sadece görülmesi gerekeni taşır.
- Sayaçlar sunucuda (`BackgroundService`); istemcinin gönderdiği zaman kullanılmaz.
- Hamleler idempotent: istemci her hamleye bir `actionId` ekler, aynı id ikinci kez işlenmez (yeniden bağlanma ve çift tıklama).

**Girdi ve çıktı**
- DTO doğrulaması (FluentValidation): `ChampionId` beyaz listeden, `LobbyCode` biçimi, takma ad en fazla 20 karakter ve izinli karakter seti.
- React metni otomatik kaçışlar; `dangerouslySetInnerHTML` kullanılmaz. Olay metinleri sunucuda şablondan üretilir, kullanıcı girdisi içermez (takma ad hariç, o da kaçışlanır).
- EF Core parametreli sorgular; ham SQL yok.

**Taşıma ve başlıklar (gateway)**
- Sadece HTTPS/WSS; HTTP → HTTPS yönlendirme, HSTS.
- `Content-Security-Policy` (sadece kendi origin'i ve gerekiyorsa Google Fonts), `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, `frame-ancestors 'none'`.
- CORS sadece web origin'ine açık (web aynı origin'den sunulursa CORS kapalı kalabilir).

**DoS ve kaynak sınırları**
- İstek gövdesi sınırı: 16 KB (gateway ve Kestrel).
- SignalR: `MaximumReceiveMessageSize` 4 KB, `KeepAliveInterval` 15 sn, `ClientTimeoutInterval` 30 sn.
- Lobi yaşam süresi: 30 dk hareketsiz lobi silinir. IP başına en fazla 3 açık lobi.
- Ağır simülasyon işleri sınırlı eşzamanlılıkla kuyrukta çalışır (§8.5 `simulate-heavy`).

**Ağ ve dağıtım**
- API ve veritabanı sadece iç docker ağında; dışarıya sadece gateway'in 443 portu açık.
- API, gateway'den gelmeyen istekleri reddeder (iç ağ + gateway'in eklediği paylaşılan gizli başlık; ileride mTLS).
- Konteynerler root olmayan kullanıcıyla çalışır.

**Sırlar ve kayıt**
- JWT imza anahtarı, iç başlık sırrı vb. ortam değişkeninde (geliştirmede `dotnet user-secrets`); repoya girmez.
- Oyun sunucusu Riot anahtarını tutmaz (veri hattı offline).
- Yapılandırılmış log (Serilog), her istekte correlation id; token, rejoinToken ve IP dışı kişisel veri loglanmaz. Draft hamleleri denetim kaydı olarak tutulur.

**Bağımlılıklar**
- `dotnet list package --vulnerable` ve `npm audit` CI'da; Dependabot açık.

### 8.7 API yüzeyi

| Metot | Yol | Yetki | Açıklama |
|---|---|---|---|
| POST | `/api/lobbies` | yok | Lobi oluştur, JWT + davet kodu döner |
| POST | `/api/lobbies/{code}/join` | yok | Boş koltuğa katıl, JWT döner |
| POST | `/api/lobbies/{code}/rejoin` | rejoinToken | Koltuğu geri al |
| GET | `/api/lobbies/{code}` | JWT (lobide) | Lobi ve draft durumu |
| GET | `/api/matches/{id}` | JWT ya da paylaşım linki | Olay listesi (tekrar izleme) |
| WS | `/hubs/draft` | JWT | `Ban`, `Pick`, `Swap`, `Ready` → `DraftUpdated`, `TimerTick`, `MatchReady`, `RateLimited` |

### 8.8 Dağıtım

`docker-compose`:
- `gateway`: 443 dışarı açık, React build'i ve TLS sertifikası (Let's Encrypt) burada.
- `api`: sadece iç ağ.
- `sqlite`: API konteynerine bağlı volume (yedekleme cron'u ile).

Ölçeklendirme gerekirse sırasıyla: SQLite → PostgreSQL, SignalR için Redis backplane, rate limit için dağıtık depolama, simülasyon için ayrı işçi servisi.

### 8.9 Kodda nereye eklenecek?

| Konu | Proje / dosya |
|---|---|
| YARP rotaları, rate limit politikaları, JWT doğrulama, güvenlik başlıkları, CORS, ForwardedHeaders | `LolDraft.Gateway/Program.cs`, `appsettings.json` (`ReverseProxy` bölümü) |
| Hub mesaj limiti | `LolDraft.Api/Hubs/RateLimitHubFilter.cs` |
| Yetki kontrolü (lobi/koltuk/sıra) | `LolDraft.Domain/Draft/DraftSession.cs` ve `LolDraft.Application/Draft/SubmitDraftActionHandler.cs` |
| DTO doğrulama | `LolDraft.Api/Validation/*Validator.cs` |
| JWT üretimi, rejoin token | `LolDraft.Infrastructure/Auth/GuestTokenService.cs` (arayüz `Application`'da) |
| CSPRNG seed | `LolDraft.Infrastructure/Random/CryptoSeedGenerator.cs` |
| Sunucu sayacı | `LolDraft.Api/Background/DraftTimerService.cs` |
| Lobi temizliği | `LolDraft.Api/Background/LobbyJanitorService.cs` |
| İç başlık kontrolü (sadece gateway'den gelen) | `LolDraft.Api/Middleware/GatewayOnlyMiddleware.cs` |
| Simülasyon motoru | `LolDraft.Engine/` (sadece `IMatchSimulator` public) |

---

## 9. Denge

**Prototip v2 ölçümü** (aynı draft, 1000 maç):

| Ölçüm | Değer |
|---|---|
| Mavi (erken comp) kazanma | %50 |
| Kuşatmayla erken biten maçlar | %40 (%99'u Mavi) |
| Uzayan maçlarda Mavi kazanma | %17 |
| Ortalama süre | 29,6 dk |
| Maç başı kill | ~22 |
| Maç başı throw | ~0,23 |
| Maç başı nehir çatışması | ~2,7 |
| Puanda önde olanın kaybettiği maç | %7 |

**Hedefler:**
- Belirgin şekilde iyi bir draft tek maçta %60-65, bo3'te ~%70 kazansın.
- Ortalama maç 25-32 dk, maç başı 15-30 kill.
- Erken comp'un maçların %30-45'ini 35. dk'dan önce bitirmesi.

**Ayar düğmeleri:** koridor katsayısı (0.1), gürültü σ (0.8), savaş paydası (15), puan etkisi (0.2), kuşatma eşiği (45), objektif değerleri, anlatıcı eşikleri.

Denge aracı: motoru konsol uygulamasıyla binlerce rastgele draft'a karşı çalıştır; şampiyon başına kazanma oranı, ortalama süre ve %10/%90 sınırına takılan olay sayısı raporlansın.

---

## 10. Yol haritası

- **v0.1 (MVP, ~5-7 hafta):** 40 elle etiketli şampiyon, 1v1 link daveti, bo1/bo3, full fearless, turnuva draft sırası + sunucu sayacı, C# motoru (bu dokümandaki kurallar), harita oynatıcı + log, maç sonu dökümü. Gateway (YARP) + rate limiting + misafir JWT + güvenlik başlıkları + docker-compose dağıtımı.
- **v0.2:** Veri hattı + Markov hareket tablosu + kill ısı haritası, soft fearless, bo5, koç direktifleri, draft kalitesi (Monte Carlo).
- **v0.3:** 2v2, turnuva modu (4/8 kişi), paylaşım kartı + tekrar linki.

**Hemen sıradaki işler:**
1. Python veri betikleri (01-03) ve indirme.
2. Bölge boyama aracı → `zones.json`.
3. `04_transitions.py` ve gerçek tabloyla prototipte hareket.
4. Çözüm iskeleti (§8.2 projeleri), gateway + API iletişimi, C# motor iskeleti + deterministiklik testleri.
5. 40 şampiyon listesi ve etiketleri.

---

## 11. İleride eklenebilecek fikirler

| Fikir | Kısaca |
|---|---|
| Draft kalitesi ve "ne olurdu?" | Maç sonu aynı draft 1000 kez; alternatif pick'lerle karşılaştırma ("Kayle yerine Jax %57"). |
| Koç kimlikleri | Seri başında pasif yetenekli koç (rakip direktifini görme, gerideyken fazla shutdown vb.). |
| Maç içi karar anları | Baron/bitirme anında iki oyuncuya eşzamanlı gizli seçim. Şu anki "tam simülasyon" yönünde açık soru. |
| Paylaşım kartı + tekrar linki | Üç kritik an + skor; link aynı seed'le maçı oynatır. |
| Turnuva modu | 4/8 kişilik bracket, fearless bo3. |
| Arkadaşın hayaleti | Arkadaşın geçmiş draft'larından öğrenen bota karşı oynama. |
| Lore sinerjileri | Lore ilişkili şampiyonlara küçük bonus ve özel metin. |
| Haftalık modlar | "Sadece AP", "Ayna draft" gibi kurallar. |
| Tahmin modu | İzleyiciler için ödülsüz, bahissiz tahmin. |

Esinlenilen oyunlar: Teamfight Manager (draft + otomatik maç; ders: draft sonrası pasif izleme sığ kalabiliyor), Football Manager (maç motoru + metin anlatımı), Pokémon Showdown (web, eşzamanlı gizli seçim, tekrar linki), Blood Bowl (sınırlı yeniden zar), Hearthstone Battlegrounds / TFT (seri arası değiştiriciler), poker (karar kalitesi ile sonuç ayrımı).

---

## 12. Rakipler

| Ürün | Ne yapıyor | Bizden farkı |
|---|---|---|
| Draft Battle (lol-tracker.com) | Ücretsiz, 5 turlu draft oyunu, 12'lik ortak havuz, CPU/eşleşme/arkadaş; sabit puanlamayla kazanan. | Ban fazı, bo3/bo5, fearless, izlenen maç ve 2v2 yok. |
| Teamfight Manager | Ücretli menajerlik oyunu, kendi şampiyonları. | Tek oyunculu; arkadaşa karşı web oyunu değil. |
| LoL Esports Manager | Riot/Tencent mobil menajerlik, Çin odaklı. | Kulüp yönetimi ağırlıklı. |
| dr4ft.lol, LeagueOfDraft | Draft simülatörleri (fearless dahil). | Kazanan belirlenmiyor. |

Farkımız (önem sırasıyla): izlenen maç hikâyesi, gerçek turnuva draft'ı + seri + fearless, maç sonu puan dökümü / draft kalitesi, 2v2 ve arkadaş turnuvası.

---

## 13. Riskler

| Risk | Önlem |
|---|---|
| "Tamamen şans" algısı | %10-90 sınırı, maç sonu dökümü, bo3 varsayılanı, ileride draft kalitesi yüzdesi |
| Çözülmüş meta | Yamayla değişen veri, fearless, haftalık modlar |
| Denge hataları | Konsolda binlerce maçlık test, sonra gerçek veri |
| Kapsam büyümesi | v0.1'i 5-7 haftada bitir |

---

## 14. Açık sorular
- Maç içi karar anları olacak mı, yoksa maç tamamen izlenen bir simülasyon mu kalacak?
- 2v2'de sorumluluk ve oy paylaşımı nasıl?
- MVP'de bilgisayar rakibi gerekli mi?
- Harita sadece 2D çizim mi, ileride daha detaylı mı?

---

## 15. Kaynaklar
- Draft Battle: https://lol-tracker.com/draft-battle · https://www.producthunt.com/p/draft-battle/draft-battle
- iTero draft modeli: https://itero.gg/articles/the-draft-model
- Data Dragon: https://hextechdocs.dev/data-dragon/ · Meraki: https://github.com/meraki-analytics/lolstaticdata
- Riot API anahtarları: https://developer.riotgames.com/api-keys.html
- Timeline yapısı: https://www.mintlify.com/mangeloni30/agent-lol/api/timeline
- Fearless kuralları: https://blog.loltheory.gg/what-is-fearless-draft/
- Teamfight Manager incelemesi: https://gideonsgaming.com/2021/07/14/team-fight-manager-review-needs-a-little-coaching/
- Football Manager kenar bağırışları: https://www.passion4fm.com/?p=11591
