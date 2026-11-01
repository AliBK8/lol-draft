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

Her dakika iki aşamada oynanır:
1. **Kayıt:** koridor ve orman akışı dakika başında puana yazılır. Diğer işler kendi saniyesiyle zaman çizelgesine kaydolur: sürpriz 5-25. sn, nehir çatışması 15-30. sn, anlatıcı 25-55. sn, objektif 30. sn, gank 40. sn, kuşatma 45. sn.
2. **Çözüm:** işler saniye sırasıyla çözülür (eşit saniyede kayıt sırası). Her iş kendi saniyesindeki duruma bakar: kim yaşıyor, toplam puan. Savaş kill'leri de ayrı işlerdir: her kill kendi saniyesinde yaşayanlar arasından seçilir. Nexus düşünce kalan işler iptal edilir.

**Koridor akışı** (her koridor, her dakika):
```
Δ = 0.1 × (G_koridor_Mavi − G_koridor_Kırmızı) + matchup[koridor] + N(0, 0.8)
```
- `matchup` prototipte sabit: top +0.1, mid +0.05, bot 0 (ileride veriden).
- Δ hem `kategori[koridor]` hem koridor puanı hem anlatıcı bütçesine eklenir.
- **Sürpriz:** her koridorda her dakika %2 ihtimalle rastgele bir takım lehine 3-5 puan (`surpriz` kategorisi).

**Orman:**
- Her dakika: `0.05 × (G_jg_Mavi − G_jg_Kırmızı) + N(0, 0.3)` → `orman`.
- Gank dakikaları: 3, 6, 9, 12, 18, 24. Gank'ı atan taraf: `p_Mavi = clamp(1 / (1 + e^(−(G_jg farkı)/4)))`. Koridor rastgele.
- Lane fazında (<14 dk) ve koridor bot değilse support %45 ihtimalle gank'a katılır.
- Başarı: tek başına %60, support'la %75. Başarı → `orman` ±4 (support'la ±5), koridor puanı ±3, kill. Başarısızlık → `orman` ∓2.

**Nehir çatışması** (aralıklar: dk 7-9, 12-14, 17-19, 25-27):
- Aralığın başında %75 ihtimalle çatışma olacağına karar verilir; dakika aralık içinden eşit ihtimalle seçilir (her aralıkta en fazla bir). Saniye 15-30 arası.
- Kişi sayısı eşit olmak zorunda değil: her taraf ayrı ayrı 1-3 kişi (1v2, 3v1…). Katılımcı önceliği: jg, sup, mid, adc, top (yaşayanlar).
- `p_Mavi = clamp(1 / (1 + e^(−(ΣG_Mavi − ΣG_Kırmızı + 0.1 × toplam)/6)))`. Az gelen taraf ΣG'de dezavantajlı (herkes G=6 iken 1v3: fark −12 → ~%12); güçlü erken şampiyon erken dakikalarda yine de kazanabilir (prototip takımlarında Mavi 1v3'ün %30'unu alıyor).
- Kazanan `catisma` ±3, kaybedenlerden 1-2 kill. Prototipte sabit dk 8, 13, 18, 26 ve 2v2/3v3 idi.

**Objektifler** (dakika = motor dakikası; savaş o dakikanın 30. sn'sinde, ör. dk 6 → 5:30):

| Dakika | Objektif | Puan |
|---|---|---|
| 6, sonra alındıktan 5 dk sonra (11, 16, 21, 26, 31) — bir takım 4'e ulaşana kadar | Ejder | 4 |
| 4. ejderi alan | Ejder ruhu | +6 ve takım savaşlarında `d`'ye +2 |
| ruhtan 6 dk sonra, alınınca 6 dk sonra tekrar (ruh yoksa hiç) | Elder | 12 |
| 14 | Herald | 4 |
| 23 | Baron | 8 |
| 35 | Son takım savaşı | 15 (maçı bitirir) |

Takım savaşı ihtimali:
```
d      = (Takım gücü Mavi − Takım gücü Kırmızı) + sinerji farkı + 0.2 × toplam puan + ruh bonusu (ruh kimdeyse ±2)
p_Mavi = clamp(1 / (1 + e^(−d/15)), 0.1, 0.9)
```
- Geride olan takım objektifi kazanırsa puan ×1.25 (geri dönüş).
- Savaşta kazanandan kaybedene kill'ler: Ejder 1-2, Baron/Herald/Elder 1-3, son savaş 1-4.
- İki objektif aynı dakikaya düşerse yeni planlanan bir dakika kayar; son savaş dakikasına (ve sonrasına) düşen objektif çıkmaz. Kuşatmanın "o dakika objektif yok" şartı bu dinamik takvime bakar.
- En hızlı ruh 4-0 ile dk 21'de, en erken Elder dk 27'de. Ruh hiç alınmazsa (3-3 → 7. ejder dk 36'ya düşer) Elder çıkmaz.
- **Ejder ruhu ölçülü tutuldu:** eşit maçta ruh tek başına takım savaşını ~%60'a çıkarır. Ayar düğmeleri: ruh puanı (6), ruh savaş bonusu (2). Hedef: ruhu alan takım maçların %65-75'ini kazansın.
- Ölçüm (prototip takımları, 1000 maç): maçların %48,7'sinde ruh alınıyor, ruhu alan %65,9 kazanıyor (Kırmızı/scale alırsa %84, Mavi/erken alırsa %55: ruh geç geldiği için geç güçlü takımda daha etkili). Maç başı 4,7 ejder, 0,27 Elder. Prototipte takvim sabitti (ejder 6, 11, 16, 21, 27; Elder 30).

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
| dk ≥ 14, koridor fırsatı (aşağıda): önce önde olan, sonra diğer taraf | Kule (dış → iç → inhibitör) | — (kendi puanı: +4, inhibitör +6) |
| \|bütçe\| ≥ 8, önde olan ve rakipten biri yaşıyor | Solo kill | 8 |
| 5 ≤ dk < 14, \|bütçe\| ≥ 5 | Kule plakası | 5 |
| \|bütçe\| ≥ 3, son CS olayından ≥ 4 dk geçti | CS farkı | 3 |

Olay zamanı: dakikanın 25-55. saniyeleri arası rastgele. Bütçe sadece anlatım içindir; puan zaten Δ ile eklenmiştir.

Anlatıcı koridorda olanı anlatır: koridorun yüzü (bot'ta adc) o an ölü ya da meşgulse (aşağıda) o dakika solo kill / plaka / CS anlatımı olmaz; bütçe harcanmaz, birikir. Solo kill kurbanı da koridorda olmalı.

**Kule = fırsat.** Kuleye ancak koridorda rakip yokken vurulur; koridorda önde olmak gerekmez.
- **Koridor fırsatı:** kendi koridorcun yaşıyor ve koridorda; karşısındaki herkes ölü ya da meşgul; kule kalmış. Meşgul: nehir çatışması, objektif savaşı ya da kuşatmaya katılan 25 sn, gank'a giden support 20 sn koridorunda sayılmaz (meşguliyet iş çözüldüğü anda bilinir). Puanı gerçektir: koridor kategorisine +4, inhibitör +6.
- **Savaş sonrası:** objektif savaşından 12 sn sonra kaybedenlerden en az 2 kişi ölüyse kazanan yakındaki kuleyi alır: ejder → bot, Herald → mid, Baron/Elder → en çok kulesi kalmış koridor. Ek puan yok: kazanılan savaşın görünür sonucu, puanı objektif değerinde (ek puan verince erken comp kartopu %52 → %61'e çıkıyordu).
- Ölçüm (prototip takımları, 1000 maç): maç başı 4,3 kule (3,95 savaş sonrası, 0,33 koridor fırsatı); denge değişmedi (Mavi %51).
- İleride: inhibitörü düşmemiş koridordan kuşatmaya gidilemesin (kuşatma ↔ kule bağı).

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
public static class MatchEngine { public static IMatchSimulator CreateSimulator(); }   // simülatör internal kalsın diye
public sealed record MatchInput(TeamDraft Blue, TeamDraft Red, Seed Seed);   // Directive'ler v0.2'de eklenecek
public sealed record MatchResult(IReadOnlyList<MatchEvent> Events, ScoreBreakdown Breakdown, MatchEnd End,
    IReadOnlyList<GoldFrame> GoldFrames);                // dakika kareleri (§16.11)
```
Yanında değer nesneleri ve olay tipleri: `Seed`, `TeamSide`, `ChampionProfile`, `TeamDraft`, `ScoreBreakdown`, `ScoreCategory`, `MatchEnd`, `GoldFrame`, `ChampionGold`, `MatchEvent` ve alt tipleri. Liste `ArchitectureTests` ile kilitli; yeni public tip bilinçli eklenir. Olaylar JSON bilmez, JSON'a çevirmek Api'nin işi (DTO).

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

**C# motoru, güncel kurallar** (zaman çizelgesi, kule = fırsat, aralıklı/eşitsiz nehir çatışması, ejder ruhu/Elder), seed 0-999: Mavi %53,8; kuşatmayla biten %40,5 (%100 Mavi); uzayan maçlarda Mavi %22,7; ortalama 29,6 dk; maç başı 20,6 kill, 0,23 throw, 2,7 nehir çatışması, 4,5 kule; ruhu alan %65,9 kazanıyor.

**C# motoru, prototip portu anı (`6ee7999`), seed 0-999:** prototiple olay olay birebir aynı (V8 ile .NET'in `Math.Log/Cos` son bit farkı yüzünden toplam puan 15. basamakta değişebilir, hiçbir olayı etkilemiyor). Mavi %53,3; kuşatmayla biten %40,5 (%99 Mavi); uzayan maçlarda Mavi %22; ortalama 29,6 dk; maç başı 21,8 kill, 0,24 throw, 2,7 nehir çatışması.

**Hedefler:**
- Belirgin şekilde iyi bir draft tek maçta %60-65, bo3'te ~%70 kazansın.
- Ortalama maç 25-32 dk, maç başı 15-30 kill.
- Erken comp'un maçların %30-45'ini 35. dk'dan önce bitirmesi.

**Ayar düğmeleri:** koridor katsayısı (0.1), gürültü σ (0.8), savaş paydası (15), puan etkisi (0.2), kuşatma eşiği (45), objektif değerleri, anlatıcı eşikleri.

**Denge aracı** (`tools/LolDraft.Balance`):
```
dotnet run --project tools/LolDraft.Balance -c Release -- --drafts 10000 --seed 1 [--champions havuz.json]
```
- Havuz §3 biçiminde JSON (`roles`: top, jg, mid, adc, sup); varsayılan `sample-champions.json`: prototipteki 10 şampiyon + her role 2 arketip (gerçek 40 şampiyon gelince değişecek).
- Rastgele draft (her rolde iki takıma farklı şampiyon). Her draft **ayna** oynanır: A Mavi/B Kırmızı, sonra yer değiştirip aynı seed. Draft etkisi iki tarafa eşit dağılır; Mavi'nin %50'den sapması saf taraf etkisidir.
- Rapor: genel ölçümler (§9 ve §16.12 hedefleriyle yan yana), **ekonomi** (14. dk CS, 15. dk takım altın farkı, shutdown ve yakalanma sayısı/ödülü), **altın farkı → kazanma tablosu** (10/15/20. dk × fark dilimi 0-1.499 / 1.500-2.999 / 3.000-4.999 / 5.000+; o dakikaya ulaşan maçlar), güç ve sinerji farkına göre kazanma, belirgin erken comp (ve 14. dk 3.000+ geride kalan scale comp'un kurtarma oranı), bitiş dakikası dağılımı, şampiyon kazanma oranları (%95 hata payıyla). Eski "maç sonu puanda önde olan kaybetti" kalktı (puan artık karar vermiyor). Motoru sadece public sözleşmeden (`MatchEngine`) kullanır; aynı `--seed` aynı raporu verir. 10.000 draft (20.000 maç) ~8 sn.

**İlk ölçüm (örnek havuz, 10.000 draft)** ve geçici deneyler (değiştir → ölç → geri al):

| Ölçüm | Şu an | A | B (sinerji d'den çık) | B2 (sinerji d'de ×0,5) | Kuşatma 35 | A+B2+kuşatma 30 | Hedef |
|---|---|---|---|---|---|---|---|
| Mavi (saf taraf) | %54,2 | %50,5 | %54,4 | %54,3 | %55,0 | %50,6 | ~%50 |
| Ortalama süre | 33,7 | 33,7 | 33,8 | 33,7 | 32,4 | 31,7 | 25-32 |
| Kuşatmayla biten | %16,9 | %15,9 | %15,2 | %15,8 | %28,3 | %34,2 | — |
| Sinerji farkı 5'te iyi olan | %69,3 | %69,0 | %56,2 | %63,4 | %70,0 | %63,2 | %60-65 |
| Erken comp kazanıyor | %41,5 | %40,9 | %42,5 | %42,0 | %45,6 | %49,5 | — |
| Erken comp 35'ten önce bitiriyor | %15,4 | %14,4 | %15,1 | %15,2 | %24,0 | %29,7 | %30-45 |

- **A:** koridor matchup sabitleri (top +0,1, mid +0,05) her zaman Mavi'ye ekleniyordu → taraf kayırması %54. Sıfırlayınca %50,5; başka hiçbir şeyi değiştirmiyor. Matchup ileride şampiyon ikilisinden (veriden, iki yönlü) gelmeli.
- **B/B2:** sinerji iki kez sayılıyor (toplam puanda ve takım savaşı `d`'sinde ayrıca). Tamamen tek sayım etkisini fazla zayıflatıyor; `d`'de ×0,5 hedefe oturuyor.
- **Kuşatma eşiği:** rastgele draft'larda farklar prototip takımlarındaki kadar uç değil, 45 puana nadiren ulaşılıyor; maçların %83'ü 35. dakikaya gidiyor.

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

### Bilinen sorunlar (prototipten gelen, C# portu bitince düzeltilecek)
C# motoru port süresince prototiple birebir aynı tutuluyor (aynı seed → aynı olaylar); bu sorunlar port bitince ayrı adımda düzeltilecek.
1. ~~**Kule olayı hiç oluşmuyor.**~~ **Çözüldü** (§4.8 kule = fırsat). Prototipte solo kill (bütçe ≥ 8) kuleden (≥ 10) önce kontrol edildiği için 1000 maçta 0 kule.
2. ~~**Dakika içinde zaman sırası tutarsız.**~~ **Çözüldü** (§4.5 kayıt/çözüm). Prototipte kurallar sabit sırayla işlendiği için ölü şampiyon kill alabiliyor, "İlk kan!" ikinci kill'e yazılabiliyordu (1000 maçın 365'inde).
3. ~~**Gank tarafı ihtimali sınırlanmıyor.**~~ **Çözüldü:** `p_Mavi = clamp(1/(1+e^(−fark/4)))`; prototipte ormancı farkı 9 olunca ~%90.5 oluyordu.

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

---

## 16. Draft ve ekonomi v2 (onaylandı — uygulanıyor; sayılar faz D'de ayarlanacak)

> Durum: kullanıcı onayı bekleniyor. Onaylanınca §3 (şampiyon modeli) ve §4.2-4.6 (puan, güç, sinerji, savaş, kuşatma) bu bölüme göre yeniden yazılır. Tüm sayılar ayar düğmesidir; denge aracıyla (§9) ölçülüp oturtulur.

### 16.1 Amaç ve ana fikir
Draft oyunun asıl kararı. Motor şu zinciri kurar; her halka gerçek etkidir ve maç sonu karnesinde açıklanır:

**draft (şampiyon özellikleri) → koridor etkileşimleri → ekonomi (altın) → gerçek güç → savaşlar/objektifler → sonuç**

- **Draft yönü belirler, oyun o yönün gerçekleşip gerçekleşmediğini.** Scale şampiyonun geç gücü bir *potansiyeldir*; altını yoksa ortaya çıkmaz. Erken ezilen scale comp geç oyunda da yetişemeyebilir.
- **Geri dönüş kapıları açık:** zayıf draft bir şekilde rakibi yakalarsa (pick, iyi savaş) altında öne geçip scale olabilir. Çok önde olan takım bir yerde yakalanıp **shutdown** verirse fark kapanır; son savaş başa baş olabilir.
- Bugünkü gizli "puan" kalkar, yerini **altın** alır. Temel kural "ekrandaki her olay motorun puan değişiminin karşılığıdır" şuna dönüşür: *her olay bir altın/güç değişiminin karşılığıdır*.

### 16.2 Şampiyon veri modeli v2
```json
{ "id": "jayce", "name": "Jayce", "roles": ["top", "mid"],
  "range": "ranged",              // melee | ranged
  "classes": ["fighter", "mage"], // 1-2 sınıf (ilk = ana sınıf)
  "e": 8, "m": 7, "l": 5,         // güç POTANSİYELİ (1-10), altınla gerçekleşir
  "econ": 2,                      // altın/item bağımlılığı (0-3)
  "ap": 0.3,                      // AP payı (0-1)
  "tags": { "fl": 0, "en": 0, "dis": 1, "peel": 0, "poke": 3,
            "pick": 1, "dive": 1, "split": 2, "mob": 2 } }   // 0-3
```

**Sınıflar:** `tank`, `fighter` (dövüşçü), `assassin` (suikastçı), `mage` (büyücü), `marksman` (nişancı), `enchanter` (koruyucu destek). Engage destek (Leona) = `tank` + yüksek `en`; yakalayıcı (Thresh) = `tank`/`enchanter` + yüksek `pick`.

**Türetilen tip — hiper taşıyıcı:** `marksman` ya da `mage`, `econ` 3 ve `l` ≥ 9 (ör. Jinx, Kassadin, Kayle). Kurallar bu tipe bakar.

**Etiketler (0-3):**

| Etiket | Anlamı | Nerede kullanılır |
|---|---|---|
| `fl` | frontline: önde durur, hasar emer | takım savaşı |
| `en` | engage: savaşı başlatır | objektif savaşı, son savaş, bot koridoru |
| `dis` | disengage: engage'i boşa çıkarır | rakip engage'ini siler |
| `peel` | koruma: taşıyıcıyı korur | dive/pick'e karşı, hiper taşıyıcı |
| `poke` | uzaktan aşındırma | kuşatma (saldırı ve savunma) |
| `pick` | tek kişiyi yakalama | nehir çatışması, gank, yakalanma |
| `dive` | arka hatta atlama | korunmasız taşıyıcıya karşı |
| `split` | yan koridorda tek başına baskı | kule fırsatı, objektifte yokluk |
| `mob` | hareket kabiliyeti | range'e yaklaşma, yakalanmadan kaçma |

**`econ` (altın bağımlılığı):** 0 = altınsız da işler (bazı destekler/tanklar), 1 = az, 2 = normal, 3 = item'sız işe yaramaz (hiper taşıyıcı). Etiketleme yükü: şampiyon başı ~13 değer × 40 şampiyon.

### 16.3 Güç eğrisi (yumuşak)
Bugün güç 14. ve 25. dakikada basamakla değişiyor; scale yavaş yavaş olmalı:
```
G_pot(dk) = e          dk ≤ 10
          = e → m      10 < dk < 18   (doğrusal geçiş)
          = m → l      18 ≤ dk < 28   (doğrusal geçiş)
          = l          dk ≥ 28
```

### 16.4 Ekonomi (altın)
Her şampiyonun altını tutulur. Sayılar gerçek oyuna yakın seçildi ki veriyle kalibre edilebilsin.

| Kaynak | Altın | Not |
|---|---|---|
| Başlangıç | 500 | |
| Pasif gelir | 2/sn (120/dk) | **sadece yaşarken**: ölmek gelir kaybıdır |
| Farm (CS) | CS × 20 | aşağıdaki CS kuralı; top, mid, adc koridor minyonu, ormancı kamp keser |
| Support item'ı | 90/dk, yaşarken | support CS kesmez |
| Kill | katile 300 + **ödül** | |
| Asist | savaş/çatışma/gank kill'inde katil dışındaki yaşayan katılımcılara toplam 150, eşit bölünür | solo koridor kill'inde asist yok |
| Plaka | 160, plakayı alan koridorcuya | |
| Kule | 250 koridorculara eşit bölünür (bot'ta 125'er), takımın kalanına kişi başı 50 | dış/iç/inhibitör aynı (ayar); savaş sonrası kule de verir |
| Ejder | takıma kişi başı 40 | asıl değeri ruh |
| Herald | takıma kişi başı 100 | |
| Baron | takıma kişi başı 300 + **Baron buff'ı** | |
| Elder | altın yok, **Elder buff'ı** | |

**CS kuralı (uygulandı, `MatchRun.Farm.cs`):** dakikada 12 minyon (2 dalga × 6), 10 sn'lik 6 dilimde. Normal tempoda %70'i kesilir: dilim başı 1,4 CS (dk'da 8,4). Her dilimde:
```
kaçan  = taban × min(0,25 × |Δ|, 0,5)      // Δ: o dakikanın koridor Δ'sı (ormancıda orman akışı)
ezilen = taban − kaçan                       // kule altında dalga kaçırır
ezen   = taban + 0,5 × kaçan                 // kaçanın yarısını ezen keser, yarısı kuleye gider
```
- Dilim anında koridorda olmayan (ölü, roam, savaş) o dilimin CS'sini kesemez.
- Örnek: Δ = 0,7 → ezilen dilim başı 1,155, ezen 1,5225. Erken comp vs scale comp (test takımları), 2.000 maç ortalaması 14. dk: Renekton 117 CS, Kayle 86 CS; takım farkı 10. dk ~2.600, 14. dk ~4.600 altın.
- CS de kalibre edilecek: timeline'larda dakika dakika oyuncu başı minyon/kamp sayısı var.
- "CS farkı açıyor" anlatımı faz A adım 3'te gerçek CS farkına bağlanır (şimdilik anlatıcı bütçesi).

**Altın rolüyle kıyaslanır:** support'un geliri düşük olduğu için `r` (16.5) her şampiyonun altınını **aynı roldeki rakibiyle** kıyaslar (uygulandı). Ödül de aynı kıyası kullanır.

**Ödül (shutdown, uygulandı):**
```
taban = 0,3 × (altın_kurban − altın_kurbanın_rol_rakibi)                   // kurbanın ölmeden önceki altını
ödül  = taban > 0 ise min(taban × (1 + 0,13 × son ölümden beri yaşanan dk), 700), değilse 0
```
Önde olanı öldürmek çok altın getirir; geri dönüş kapısının ana parçası. Uzun süre ölmeden önde kalan giderek değerli hedef olur (seri ödülü). Örnek: rakibinden 1.000 önde → 300; 5 dk ölmezse ~495. Zaman **çarpandır**: toplama (+40/dk) denendi, 50 altın önde olan bile tam bonus alıyordu (maç başı ~9 shutdown, %40'ı tavanda). Ödülü sadece katil alır (asistler kendi payını alır). `KillEvent.Bounty` miktarı taşır; 150+ ödül "Shutdown! … (+N ödül altını)" diye anlatılır.

### 16.5 Altından gerçek güce
```
r     = altın / ((altın + rakip_rol_altını) / 2)    // 1 = başa baş, 1,1 = rakibinden %22 fazla (uygulandı)
s     = { econ 0: 0,5 · econ 1: 1 · econ 2: 1,5 · econ 3: 2 }   // altına duyarlılık (deneyle ölçeklendi, aşağıda)
G_eff = G_pot(dk) × clamp(1 + s × (r − 1), 0,5, 1,4)
```
- **Scale şampiyon altına daha duyarlıdır** (`econ` 3): az altınla potansiyelinin yarısına iner, çok altınla en fazla %40 artar.
- Taban ve tavan kartopu sınırlar (azalan getiri): 10 kill alan, 3 kill alandan çok daha güçlü olmaz.
- Uygulama (`MatchRun.Power.cs`): `econ` henüz yok (adım 8), herkes `s = 1,5`. Örnek: Renekton 5.150 vs Kayle 4.050 altın → r 1,12 / 0,88 → güç ×1,18 / ×0,82.
- **Duyarlılık deneyi** (herkes aynı s; eşit draft 5.000 maç + denge aracı 4.000 draft):

| Ölçüm | s 0,9 | **s 1,5 (seçildi)** | s 2 | s 3 | Hedef |
|---|---|---|---|---|---|
| Eşit draft: 15. dk 3.000+ önde kazanıyor | %64,5 | %72,2 | %79,9 | %89,4 | %70-80 |
| Eşit draft: 15. dk 5.000+ önde kazanıyor | %70,7 | %82,1 | %88,6 | %94,7 | — |
| Erken comp kazanıyor / 35'ten önce bitiriyor | %39,9 / %16,2 | %48,1 / %27,0 | %54,7 / %36,2 | %64,4 / %52,5 | — / %30-45 |
| Ruhu alan kazanıyor | %64,6 | %70,4 | %74,4 | %81,4 | %65-75 |
| Ortalama süre | 33,3 | 32,1 | 30,7 | 28,4 | 25-32 dk |
| Sınıra takılan savaş | %2,6 | %3,2 | %4,1 | %6,4 | düşük |

  0,9 çok zayıf (altın önemsiz), 3 kartopu. 1,5 seçildi: erken ve scale comp başa baş, gerçekçi. 2 hedeflerin hepsine giriyor ama erken comp'u hafif öne çıkarıyor; faz D'de (econ'la birlikte) yeniden bakılacak aday.
- `G_eff`, bugün `G`'nin kullanıldığı her yere geçer: koridor akışı, nehir çatışması, takım gücü.

**Örnek: Kayle (2-6-10, econ 3) vs Renekton (9-6-4, econ 1), top, 28. dk:**

| Senaryo | Kayle r | Renekton r | Kayle G_eff | Renekton G_eff | Sonuç |
|---|---|---|---|---|---|
| Normal lane | 0,9 | 1,1 | 10 × 0,88 = 8,8 | 4 × 1,06 = 4,2 | Kayle scale etti |
| Kayle ezildi | 0,6 | 1,35 | 10 × 0,52 = 5,2 | 4 × 1,21 = 4,8 | neredeyse eşit; takım geriyse yetişemez |
| Renekton ezildi | 1,1 | 0,7 | 10 × 1,12 = 11,2 | 4 × 0,82 = 3,3 | maç Kayle'a erken açılır |

### 16.6 Koridor akışı
```
Δ = 0,1 × (G_eff_koridor_Mavi − G_eff_koridor_Kırmızı) + koridor kuralları (16.8) + N(0, 0,8)
```
Sabit matchup sayıları (top +0,1, mid +0,05) kalkar: her zaman Mavi'ye ekleniyorlardı (taraf kayırması %54, §9). Koridor avantajı artık kurallardan, ileride veriden (iki yönlü) gelir. Δ altına (16.4 farm), altın `G_eff` ile Δ'ya döner: koridor içi kartopu; taban/tavan ve ödül dengeler.

### 16.7 Savaşlar
Tüm takım savaşları (objektif, kuşatma, son savaş) ve nehir çatışması aynı iskeleti kullanır:
```
d      = ΣG_eff(katılımcılar) farkı + kural bonusları (olay türüne göre, 16.8) + buff'lar
p_Mavi = clamp(1 / (1 + e^(−d / bölen)), 0,1, 0,9)        bölen: takım savaşı 15, çatışma 6
```
- Bugünkü `0,2 × toplam puan` ve ayrıca eklenen `sinerji farkı` kalkar (sinerji iki kez sayılıyordu, §9). Üstünlük altın üzerinden `ΣG_eff`'e girer.
- **Buff'lar (uygulandı):** ejder ruhu kalıcı d +2 · Baron buff'ı 3 dk kuşatma d +4 ve kuşatma eşiği yarıya · Elder buff'ı 2 dk tüm takım savaşlarında (objektif, kuşatma, son savaş) d +6. Buff takıma aittir (ölen üye kaybetmez; sadeleştirme).
- **Geri dönüş:** ayrı ×1,25 çarpanı kalktı (uygulandı); yerini ödül (16.4) aldı: geride olan kazandığında önde olanları öldürür, ödül toplar.

### 16.8 Kurallar (ilk set: 13 kural)
Her kuralın adı, koşulu, etkili olduğu yer, ağırlığı ve karne metni var. Ağırlıklar JSON'da (kod değişmeden ayar), kurallar C#'ta test edilebilir küçük parçalar.

**Koridor kuralları** (lane fazı, aksi belirtilmedikçe dk < 14; Δ'ya eklenir):

| # | Kural | Koşul | Etki | Karne metni |
|---|---|---|---|---|
| K1 | Range baskısı | ranged vs melee | +0,3/dk; melee'nin `max(mob, en)` ≥ 2 ise +0,1 | "Range üstünlüğü (Jayce)" |
| K2 | Tank tamponu | koridorda tank var | iki tarafın Δ'sı × 0,7 | "Tank koridoru sakinleştirdi" |
| K3 | Suikastçı all-in | mid: suikastçı (`mob` ≥ 2) vs mage/marksman | dk < 10: suikastçıya +0,3 · dk ≥ 18: büyücüye +0,2 | "Erken all-in (Zed)" / "Dalga temizliği (Orianna)" |
| K4 | Bot engage | destek `en` ≥ 2 ve rakip bot `peel + dis` < 2 | bot'a +0,25/dk | "Bot'ta engage baskısı (Leona)" |

**Takım kuralları** (d'ye eklenir, olay türüne göre):

| # | Kural | Koşul | Nerede | Etki |
|---|---|---|---|---|
| K5 | Engage | `en` toplam farkı | objektif savaşı, son savaş | d += 1,0 × fark (±4); rakip `dis + peel` toplamının yarısı kadar silinir |
| K6 | Poke | `poke` toplam farkı | kuşatma (saldırı ve savunma) | d += 1,0 × fark (±4); objektif savaşında rakip `en` ≥ 5 ise poke takımına −2 |
| K7 | Pick | `pick` toplam farkı | nehir çatışması, gank, yakalanma | çatışma d += 1,0 × fark (±3); gank başarısı +%5 × fark (±%15) |
| K8 | Dive tehdidi | rakipte hiper taşıyıcı ve rakip `peel + fl` < 3 | takım savaşları | d += 0,8 × `dive` toplamı (±3) |
| K9 | Korunan taşıyıcı | takımda hiper taşıyıcı | dk ≥ 25 takım savaşları | `peel` ≥ 3 → d +2 · `peel + fl` < 2 → taşıyıcının `G_eff` × 0,85 |
| K10 | Frontline eksik | `fl` toplamı < 2 | takım savaşları | d −3 (rakip `en` ≥ 4 ise −4) |
| K11 | Tek tip hasar | AP payı < %20 ya da > %80 | takım savaşları | d −2 |
| K12 | Split | top/mid'de `split` ≥ 2 | dk ≥ 14 objektif savaşları | o şampiyon savaşa gelmez (takım 4 kişi savaşır), aynı anda kendi koridorunda kule fırsatı arar |
| K13 | Kombo | tablo (Orianna+Malphite, Yasuo+knock-up…) | tablodaki olay türü | d +2 |

Bugünkü sinerji kuralları (frontline, engage, AP dengesi, kombo) K5, K10, K11, K13'e dağılır; maç başı tek "sinerji puanı" kalkar.

### 16.9 Yakalanma (geri dönüş kapısı)
Çok önde olan takım bazen bir yerde yakalanır:
- dk ≥ 15, objektif olmayan dakikalarda (objektifte herkes savaşta) bir kez, dakikanın 5-40. saniyesinde (45. sn kuşatmadan önce); takım altın farkı o an ≥ 1.000 ise.
- Kurban: **önde olan takımın, koridorunda olan (ölü/savaşta değil) en zengini**: rol rakibine göre en yüksek altın oranı.
- İhtimal: `p = min(0,03 + 0,01 × takım altın farkı / 1000, 0,12) × (1 + 0,15 × rakip pick toplamı)`. 3.000 önde → %6/dk, 9.000+ → %12.
- Yakalanırsa kaçma şansı `0,2 + 0,1 × mob`; kaçamazsa rakibin en yüksek `pick`'li müsait şampiyonu öldürür ve katil **ödülü** alır. Log: "Shutdown! Draven alt koridorda yakalandı, kill Lee Sin aldı (+650 ödül altını)". Kill olayı `Lane` boş (koridor anlatıcısının kill'i değil).
- Önde olan için risk, geride olan için umut; son savaştan önce farkın kapanmasını sağlayan mekaniklerden.
- Uygulama (`MatchRun.Pick.cs`): `pick` ve `mob` henüz yok (adım 8): ihtimal sadece altın farkına bağlı, kaçma %20, katil nehre yakınlık sırasıyla ilk müsait (ormancı, support, mid, adc, top).
- Ölçüm (5.000 maç, yakalanmalı / yakalanmasız): maç başı 0,6 yakalanma, ortalama ödül ~680. Eşit draft'ta 15. dk 5.000+ önde olan %79,4 / %81,3, erken vs scale'de %73,4 / %74,9 kazanıyor; büyük farkta ~2 puan geri dönüş.

### 16.10 Kuşatma ve maç sonu
- Şart: dk ≥ 20, o dakika objektif yok, **takım altın farkı ≥ 4.000** (Baron buff'ı varken ≥ 2.000), en az bir koridorda dış ve iç kule yıkılmış, son kuşatmadan ≥ 3 dk.
- Başarı ihtimali: takım savaşı (16.7) + K6 poke. Başarı → Nexus. Başarısız → **throw**: savunan savaşı kazanmış sayılır, kill'ler ödülle → fark kendiliğinden kapanır (sabit +10 kalktı).
- Uygulandı (`MatchRun.Siege.cs`): saldıran = kuşatma anında altında önde olan; K6 poke adım 9'da. Puan (`ScoreSheet`) artık hiçbir karara girmiyor; sadece `Breakdown` çıktısında duruyor, karne gelince (adım 10) altın kaynakları dökümüyle değişecek.
- Ölçüm (10.000 draft): süre 29,3 dk, maçların %52,7'si kuşatmayla bitiyor, erken comp %32,6'sını 35'ten önce bitiriyor (ilk kez hedefte), eşit draft'ta 15. dk 3.000+ önde olan %78,7 kazanıyor. **Bilinen:** bitişlerin %15'i tam 20. dakikada (ilk kuşatma dakikası birikiyor); faz D'de bakılacak (ör. ilk kuşatma için inhibitör şartı ya da eşiğin dakikayla artması).
- Son savaş (dk 35) aynı kalır: o anki `ΣG_eff`, kurallar ve buff'larla.

### 16.11 Çıktı (motor sözleşmesine eklenecekler)
- `KillEvent`'e ödül altını; altın getiren olaylara miktar.
- **Dakika kareleri** (`GoldFrame`, uygulandı): k. kare = k. dakikanın başındaki (t = k × 60) her şampiyonun altını ve CS'si (aşağı yuvarlanmış), `TeamGold(side)` takım toplamı. 0. kare herkes 500; son kare maçın bittiği dakika, Nexus anı → front'ta altın grafiği.
- **Draft karnesi:** maçta etkili olan kurallar (kural, taraf, nerede, ne kadar etki, metin) ve altın farkının kaynakları (koridorlar, kill'ler, ödüller, objektifler). Örnek: "Top: range üstünlüğü (Jayce) → +900 altın · Kayle scale edemedi: 28. dk'da potansiyelinin %55'i · Draven yakalandı → 650 ödül, fark kapandı".
- `ScoreBreakdown` yerine altın kaynakları dökümü.

### 16.12 Denge hedefleri (araçla ölçülecek)
| Ölçüm | Hedef |
|---|---|
| Saf taraf etkisi (ayna) | ~%50 |
| Belirgin iyi draft (kural toplamı yüksek) | %60-65; en iyi draft bile ≤ %70 |
| Üstün kimlik (K5-K8 lehine) | %55-60 |
| Lane fazında belirgin geride kalan scale comp maçı kurtarıyor | %25-35 |
| 15. dk 3.000+ altın önde olan kazanıyor | %70-80 (gerçek veriyle kalibre) |
| Ortalama süre / maç başı kill | 25-32 dk / 15-30 |
| Erken comp 35'ten önce bitiriyor | %30-45 |
| Ruhu alan kazanıyor | %65-75 |

Kalibrasyon: veri hattındaki timeline'larda dakika dakika takım altını var → "X. dakikada Y altın önde olanın kazanma oranı" gerçek eğrisi; altın duyarlılığı (16.5) buna göre oturtulur.

### 16.13 Uygulama sırası
Her adım: kod → testler → denge aracıyla kısa ölçüm → bir sonraki adım. Sayılar (ağırlıklar, eşikler) adımlar boyunca taslak kalır; asıl ayar faz D'de yapılır.

**Faz A — Ekonomi (puan → altın)**
1. ~~Altın defteri: başlangıç, pasif gelir (sadece yaşarken), kill/asist/plaka/kule/objektif altını, dakika kareleri. Henüz güce bağlı değil, yan kayıt; mevcut davranış ve testler değişmez.~~ (bitti: `GoldLedger`, `MatchRun.Gold.cs`, `GoldFrame`; maç akışı birebir aynı)
2. ~~Koridor farmı: CS kuralı (16.4), ezilen CS kaçırır, koridorda olmayan kesemez; support item'ı.~~ (bitti: `MatchRun.Farm.cs`, karelerde CS; maç akışı birebir aynı)
3. ~~Yumuşak güç eğrisi + `G_eff`: savaş ve koridor ihtimalleri altından hesaplanır. Büyük geçiş: matchup sabitleri ve ikinci sinerji sayımı kalkar, testler güncellenir.~~ (bitti) Koridor Δ, orman/gank, nehir çatışması, takım savaşı `G_eff` kullanıyor; `0,2 × puan` ve `0,1 × puan` kalktı, sinerji d'de tek kez (kurallar gelince kalkacak); CS anlatımı gerçek CS farkına (≥ 15, olay metninde sayılar). Kuşatma eşiği ve ×1,25 hâlâ puanla (adım 4, 6). Ölçüm (10.000 draft): Mavi %54,2 → %50,6; sinerji 5 farkta %67, 7'de %72 (değişmedi, adım 9); erken comp %42,8 kazanıyor, %17,5'ini 35'ten önce bitiriyor; süre 33,3 dk. Eşit draft'ta 1.500+ altın önde olan objektif savaşının %55,7'sini kazanıyor (altın etkisi kapalıyken %50,6): altının etkisi şimdilik küçük, faz D'de veriyle büyütülecek. Motor artık prototiple birebir değil.
4. ~~Shutdown ödülü.~~ (bitti; objektiflerdeki ×1,25 kalktı) Ölçüm (5.000 maç, ödüllü / ödülsüz): eşit draft'ta 15. dk 1.500+ önde olan %62,0 / %62,8, 5.000+ önde olan %71,9 / %72,6 kazanıyor; erken vs scale'de 5.000+ önde %60,2 / %60,6. Maç başı ~3 shutdown, ~1.200 ödül altını. Sonra ödül büyütüldü (0,3 + zaman çarpanı, yukarıdaki formül): maç başı ~6 shutdown, ortalama ~500, ~3.250 ödül altını; ama eşit draft'ta 1.500+ önde olan hâlâ %60,4, 5.000+ önde olan %70,7 kazanıyor. **Ödülü 3 katına çıkarmak geri dönüşü ancak ~2 puan artırdı: darboğaz ödül değil, altının savaşa etkisi (s, r).** Faz D'de önce o ayarlanacak.
5. ~~Yakalanma (16.9).~~ (bitti; ölçüm 16.9'da)
6. ~~Kuşatma v2 (altın farkı + kuleler), throw'un ödülle kapanması, Baron/Elder buff'ları.~~ (bitti; ölçüm 16.10'da)
7. ~~Denge aracı altına geçer → ara ölçüm (ayna ~%50, süre, geri dönüş oranı).~~ (bitti) **Faz A sonu ölçümü** (10.000 draft): Mavi %49,5; süre 29,3 dk; %52,7 kuşatmayla bitiyor; kill 21,9; ruhu alan %69,3; 14. dk CS 102; 15. dk ort. takım farkı 3.228; maç başı 6,3 shutdown (ort. 509), 0,49 yakalanma (ort. 679). 15. dk'da önde olan: 0-1.499 → %53, 1.500-2.999 → %60, 3.000-4.999 → %69, 5.000+ → %80 (hedef 3.000+ %70-80 ✓). Erken comp %47,3 kazanıyor, %32,6'sını 35'ten önce bitiriyor ✓; 14. dk 3.000+ geride scale comp %33,6 kurtarıyor ✓ (%25-35). Açık: sinerji 5 farkta %66, 7'de %72 (faz B kurallarıyla), 20. dk bitiş birikmesi %15 (faz D).

**Faz B — Şampiyon modeli v2 ve kurallar**
8. `ChampionProfile` v2 (range, sınıf, econ, etiketler) + `sample-champions.json`.
9. Koridor kuralları (K1-K4), sonra takım kuralları (K5-K13); her kural kendi testiyle.
10. Draft karnesi çıktısı (16.11).
11. Denge aracına kural etkisi ve kimlik-kimlik raporu → ölçüm.

**Faz C — Veri**
12. 40 şampiyon etiketleme (kullanıcı) + tutarlılık kontrol aracı ("hiper taşıyıcı ama econ 1", "tank ama fl 0" uyarıları).

**Faz D — Dengeleme**
13. Veri hattından gerçek altın eğrisi ("X. dk Y altın önde → kazanma oranı"), altın duyarlılığı ve kural ağırlıkları 16.12 hedeflerine oturtulur.

### 16.14 Kullanıcıya açık sorular
1. Sınıf listesi yeterli mi? (`tank`, `fighter`, `assassin`, `mage`, `marksman`, `enchanter`)
2. `cc` ve `waveclear` ayrı etiket olsun mu, yoksa `pick`/`en` ve sınıf yeterli mi?
3. K1-K13'te oyun bilgisine göre yanlış ya da eksik olan var mı? Yönler ve büyüklükler (ör. range baskısı +0,3) mantıklı mı?
4. Yakalanma (16.9) ve throw'un ödülle kapanması: geri dönüş sıklığı hedefi ne olsun? Öneri: lane'de belirgin geride kalan %25-35 kurtarır.
5. Oyuncu ne görsün? Öneri: draft sırasında kimlik çubuğu (hangi kimliğin neyi yendiği gizli), maç sonunda tam karne.
