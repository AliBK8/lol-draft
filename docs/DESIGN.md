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
| Seri: taraf ve fearless | Takım kimliği sabit koltuk (`TeamSlot` A/B), renk maçlık: ilk mavi rastgele, sonra renkler her maç değişir. Fearless tek tür: seçilen şampiyon iki takıma da kilitlenir. Fearless + bo5 havuz yetene kadar kapalı (40'lık havuza sığmıyor). (2026-11-10) | Reddedildi: "kaybeden taraf seçer" (ek bir seçim ekranı, otomatik değişim daha basit). Reddedildi: Host/Guest koltuk adı (2v2 ve turnuva modunda anlamsız). Reddedildi: soft fearless. |
| Rol dışı oynama | Serbest ama cezalı: rolü dışındaki yuvada şampiyonun gücü −%25 (ölçülüp ayarlanacak), karnede görünür (§16.8). (2026-11-10) | Reddedildi: swap'ta yasak (fearless 3. maçta uygun şampiyon kalmayabilir). Reddedildi: cezasız bırakmak (motor rolü bilmiyor, istismar açılır). |
| Gerçekçilik mi eğlence mi | **Eğlence önce.** Gerçek veri yön verir, hedefi belirlemez: motor "turnuva hissi"nde kalır, geride olan takımın her zaman gerçek bir şansı olur. Önde olanın kazanma oranı %80-85'i geçmez. | Reddedildi: solo kuyruk eğrisine oturtmak (15. dk 5.000+ önde %98,5, 3.000+ geride %8,5 kurtarıyor; §16.13 adım 13). Bu bir web oyunu, ciddi LoL maçı değil: kaybeden taraf için eğlence sıfıra düşmemeli. |

---

## 2. Oyun akışı

1. **Lobi.** Mod (1v1, ileride 2v2), seri (bo1/bo3/bo5), draft kuralı (normal, fearless), şampiyon havuzu. Arkadaş link ile davet edilir, kayıt yok.
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
6. **Seri.** İlk maçta mavi taraf rastgele belirlenir, sonraki her maçta renkler değişir. Skor renge değil takıma yazılır. Seri devam ediyorsa fearless kilitleri uygulanır.

**Fearless:**
- Seride seçilen şampiyon iki takım için de kilitlenir (soft fearless yok).
- Banlar sonraki maça taşınmaz.
- Havuz serinin en uzun hâline yetmeli: 40'lık havuzda bo1 ve bo3 açık, bo5 kapalı (§8.3).

**2v2 (ileride):** Takımda iki kişi: biri top/jg/mid, diğeri adc/sup draft eder; sıra takım arkadaşları arasında döner.

---

## 3. Şampiyon veri modeli

> **Geçerli model v2: §16.2** (range, sınıf, econ, 9 etiket; uygulandı, `ChampionProfile` + `ChampionTags`). Aşağıdaki v1 biçimi prototipin referansı olarak duruyor.

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
  LolDraft.Gateway.Tests/   gateway → API hattı uçtan uca (bellekte), rate limit, JWT, başlıklar
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

    public Result Ban(Side side, ChampionId champion, Instant now) { /* faz, süre, sıra, havuz, tekrar, fearless */ }
    public Result Pick(Side side, ChampionId champion, Instant now) { /* aynı kontroller */ }
    public Result SwapRoles(Side side, Role a, Role b, Instant now) { /* swap fazında iki yuvayı değiştir */ }
    public Result LockLineup(Side side, Instant now) { /* iki takım da kilitleyince draft biter */ }
    public Result Timeout(Instant now, IDraftRandom random) { /* sunucu sayacı: boş ban / rastgele uygun pick / swap biter */ }
}
```
Dışarıdan `Phase` ya da `Turn` set edilemez; geçersiz hamle `Result.Fail` döner ve durum değişmez.

**Uygulama (4d, `LolDraft.Domain`):**
- Durum = sabit adım tablosu (`DraftOrder`, 20 adım) + adım sayacı; faz ve sıra tablodan okunur, ayrı değişken yok.
- Domain Engine'i bilmez (§8.2): draft bitince `DraftResult` (taraf → 5 yuvalık `TeamLineup` + hamleler) üretir; Application bunu `IChampionCatalog` ile motorun `TeamDraft`'ına çevirir. Domain'in kendi `Side`'ı var.
- Sadece açık draft (gizli/eşzamanlı seçim yok; karar 2026-11-04).
- Swap: 20. adımdan sonra her takımın pick'leri pick sırasıyla yuvalara dizilir (1. pick top … 5. pick support). Takım istediği kadar iki yuvayı yer değiştirir (arayüzde top pick'ine, sonra adc yuvasına tıklamak), sonra kilitler. İkisi de kilitleyince ya da süre dolunca o anki dizilimle biter.
- Zaman aşımı: ban → boş ban; pick → uygun (havuz − ban − seçilen − fearless kilidi) şampiyonlar id sırasıyla dizilir, zar birini seçer.
- Fearless kilidi (`FearlessLocks`, tek ortak liste) hem pick'i hem ban'ı engeller (kimsenin seçemeyeceği şampiyonu ban'lamak anlamsız).
- Havuz kuralı: kilitler düşüldükten sonra ≥ 20 şampiyon (10 ban + 10 pick) kalmalı, yoksa `pool_too_small`.

**Seri (`Series`, 4d):**
- Kimlik `TeamSlot` (A/B), seri boyunca sabit; renk (`Side`) maçlık. İlk mavi dışarıdan gelir (sunucu CSPRNG), sonra her maç değişir: `BlueTeam`, `SideOf(team)`, `TeamOn(side)`.
- Akış: `StartDraft(timings, now)` → draft oynanır → simülasyon → `RecordGame(kazanan renk)`. Seri kazanan rengi o maçta o renkteki takıma çevirir, skoru takıma yazar. Bitiş: bo1 → 1, bo3 → 2, bo5 → 3 galibiyet.
- Seri o anki draft'ın sahibidir (`CurrentDraft`): maç sürerken ikinci draft başlamaz (`game_in_progress`), draft bitmeden sonuç yazılmaz (`draft_not_completed`), biten seride ikisi de `series_finished`. Aynı maç iki kez yazılamaz.
- Fearless kilitleri: biten maçların bütün pick'leri (maç başına 10); ban'lar taşınmaz.
- Havuz kontrolü seri başlarken, en uzun hâle göre: `havuz − (maç sayısı − 1) × 10 ≥ 20`. 40'lık havuz: fearless bo3 tam sığar (3. maçta 20 kilit, 20 kalır), fearless bo5 sığmaz (4. maçta 30 kilit, 10 kalır) → `pool_too_small`. Kural havuz boyutundan hesaplandığı için havuz 60'a çıkınca bo5 kendiliğinden açılır.

**Application portları:**
```csharp
// Yazıldı (4e-1, Application/Ports.cs):
public interface ILobbyRepository      // lobi başına kilit: işlem kilidin altında koşar, canlı Lobby dışarı çıkmaz
{
    Task Add(Lobby lobby, CancellationToken ct);
    Task<Result<T>> Execute<T>(LobbyCode code, Func<Lobby, Result<T>> action, CancellationToken ct);
}
public interface IGuestTokenService { string Issue(GuestIdentity identity); Task<GuestIdentity?> Read(string token); }
public interface ISecureRandom { byte[] Bytes(int count); }   // CSPRNG: lobi kodu, oyuncu kimliği, yazı tura
public interface IClock { Instant Now { get; } }
public interface IChampionCatalog { IReadOnlyCollection<ChampionId> All { get; } }   // profil verisi simülasyon adımında

// Sırada (simülasyon adımı):
public interface IMatchRepository { Task Save(MatchRecord record, CancellationToken ct); Task<MatchRecord?> Get(MatchId id, CancellationToken ct); }
public interface ISeedGenerator { Seed Next(); }   // CSPRNG
```

### 8.4 Kimlik ve yetki (kayıtsız)

Kayıt yok ama her istek bir kimliğe bağlı.

1. **Lobi oluşturma:** `POST /api/lobbies` → sunucu `LobbyCode` (128 bit rastgele, URL-güvenli) ve oluşturana bir **misafir JWT'si** üretir.
2. **Davet linki:** `/l/{LobbyCode}`. Kod tahmin edilemez; boş koltuk bir kez alınır.
3. **Katılma:** `POST /api/lobbies/{code}/join` → ikinci oyuncuya JWT.
4. **JWT içeriği:** `sub` (PlayerId), `lobby` (LobbyCode), `seat` (takım koltuğu A/B; renk değil, renk her maç seriden gelir), `exp` (2 saat). HMAC-SHA256 ile imzalanır, anahtar ortam değişkeninde.
5. **Yeniden bağlanma (ertelendi, karar 2026-11-10):** JWT 2 saat geçerli; istemci saklarsa sayfa yenileme çalışır. Token kaybolursa koltuğu geri almak için ileride: JWT ile birlikte uzun ömürlü olmayan bir `rejoinToken` (tek kullanımlık, lobi ömrü kadar) verilir. Sayfa yenilenirse koltuk geri alınır.
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
| POST | `/api/lobbies/{code}/rejoin` | rejoinToken | Koltuğu geri al (ertelendi; gateway rotası hazır, API'de uç nokta yok) |
| GET | `/api/lobbies/{code}` | JWT (lobide) | `{yourSeat, lobby}`: koltuk + hub'daki `LobbyUpdated` ile aynı biçimde durum |
| GET | `/api/matches/{id}` | JWT ya da paylaşım linki | Olay listesi (tekrar izleme) |
| WS | `/hubs/draft` | JWT | `Ready`, `Ban`, `Pick`, `SwapRoles`, `LockLineup` (her biri `actionId` ile) → `LobbyUpdated` (tam durum + `deadline` + `serverNow`; saniyelik `TimerTick` yok, karar 2026-11-10), ileride `MatchReady`, `RateLimited` |

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

### 8.10 Uygulama: 4c (gateway + API hattı)

**Bir isteğin yolu:** İstemci → Gateway: iletilmiş başlıklar (sadece güvenilen proxy) → correlation id → güvenlik başlıkları (OnStarting) → gövde sınırı → HTTPS/HSTS (production) → yönlendirme (beyaz liste) → rate limit → JWT → rota politikası → YARP (istemcinin sır başlığı silinir, gerçek sır eklenir) → API: `GatewayOnlyMiddleware` → correlation id = TraceIdentifier → uç nokta.

| Önlem | Nerede | Karar ve neden |
|---|---|---|
| Sadece gateway'den | `Api/Middleware/GatewayOnlyMiddleware.cs` | `X-Gateway-Secret` tam bir değer ve eşleşmeli; ikisi de SHA-256'dan geçip `FixedTimeEquals` (zamanlama saldırısı, uzunluk sızıntısı yok). Red **404** (403 "burada bir şey var" der). |
| Sır taklidi | `Gateway/Proxy/GatewayTransforms.cs` | İstemcinin gönderdiği sır başlığı silinir, yerine gerçeği yazılır. |
| Beyaz liste | `Gateway/appsettings.json` `ReverseProxy:Routes` | Sadece listedeki yol + metot iletilir; API'ye eklenen uç nokta otomatik dışarı açılmaz. Bilinmeyen yol 404, yanlış metot 405, ikisi de API'ye ulaşmaz. |
| Rate limit | `Gateway/RateLimiting/GatewayRateLimiting.cs` | §8.5 tablosu (global token bucket + rota politikaları), IP'ye göre. **Kimlik doğrulamadan önce**: sel saldırısı imza doğrulamasına CPU harcatmaz. 429 + `Retry-After`. `simulate-heavy` ağır uçlar gelince. |
| JWT | `Gateway/Auth/GuestAuthentication.cs` | HS256 dışı algoritma reddedilir (`alg: none`), yayıncı `lol-draft`, hedef `lol-draft-game`, saat toleransı 30 sn (varsayılan 5 dk), `MapInboundClaims = false`. Hub yolunda token `access_token` sorgusundan, diğer yollarda sadece `Authorization` başlığından. `guest` politikası: imza + `lobby` + `seat` claim'i; yoksa 403. |
| Token loglanmaz | `appsettings.json` | `Microsoft.AspNetCore.Hosting.Diagnostics` ve `Yarp` Warning: Information seviyesi istek URL'sini (sorgudaki `access_token` dahil) yazardı. |
| İstemci IP'si | `Gateway/Program.cs` ForwardedHeaders | Varsayılan "localhost'a güven" de silinir; sadece `Gateway:TrustedProxies` listesi. Yoksa herkes `X-Forwarded-For`'a sahte IP yazıp rate limit'i atlatırdı. |
| Başlıklar | `Gateway/Middleware/SecurityHeadersMiddleware.cs` | CSP (kendi origin + Google Fonts, `frame-ancestors 'none'`, `object-src 'none'`), nosniff, Referrer-Policy, X-Frame-Options, Permissions-Policy; `Server` silinir. OnStarting'de: proxy'lenen cevaba da girer, API ezemez. |
| Gövde sınırı | `Gateway/Middleware/RequestSizeLimitMiddleware.cs` + Kestrel | 16 KB; Content-Length büyükse gövde okunmadan 413, chunked'da Kestrel sınırı. |
| Correlation id | `Gateway/Middleware/CorrelationIdMiddleware.cs` | Gelen değer sadece `[A-Za-z0-9-]{1,64}` ise korunur (log enjeksiyonu), yoksa yeni GUID; API'ye iletilir, cevaba yazılır. |
| Sırlar | `GatewaySettings.cs`, `ApiSettings.cs` | `GATEWAY_SECRET` (≥ 32 karakter), `JWT_SIGNING_KEY` (≥ 32 bayt); eksik/zayıfsa uygulama açılmaz. |

**Uç noktalar (şu an):** sadece `GET /api/health` (API'de gerçek). Lobi, maç ve hub rotaları gateway'de tanımlı (politikalarıyla) ama API'de henüz uç noktaları yok (404); sıradaki adımda gelecekler.

**Testler:** `LolDraft.Api.Tests/GatewayOnlyTests` (8) ve yeni `LolDraft.Gateway.Tests` (21). Gateway testleri iki uygulamayı bellekte bağlar: YARP'ın HTTP istemcisi (`IForwarderHttpClientFactory`) API test sunucusunun handler'ına yönlendirilir; API'nin önündeki gözcü (`IStartupFilter`) API'ye hangi isteğin hangi sır başlığıyla ulaştığını kaydeder ("API'ye hiç ulaşmadı" kanıtlanabilir). Ayrıca gerçek süreçlerle (Kestrel + gerçek HTTP) duman testi yapıldı: sağlık 200 + başlıklar, API'ye doğrudan 404, tokensiz 401, beyaz liste dışı 404, 6. lobi 429, 20 KB 413.

**Geliştirmede çalıştırma:**
```
dotnet user-secrets set GATEWAY_SECRET "<en az 32 rastgele karakter>" --project src/LolDraft.Api
dotnet user-secrets set GATEWAY_SECRET "<aynı değer>"                  --project src/LolDraft.Gateway
dotnet user-secrets set JWT_SIGNING_KEY "<en az 32 rastgele karakter>" --project src/LolDraft.Gateway
dotnet user-secrets set JWT_SIGNING_KEY "<aynı değer>"                 --project src/LolDraft.Api
dotnet run --project src/LolDraft.Api       # http://localhost:5100 (iç)
dotnet run --project src/LolDraft.Gateway   # https://localhost:5001, http://localhost:5000 (dış)
```
Rastgele değer: `openssl rand -base64 48`. JWT anahtarı iki uygulamada aynı: API imzalar, gateway doğrular.

### 8.11 Uygulama: 4e-1 (lobi + misafir token)

**Lobi açmanın yolu:** `POST /api/lobbies {format, fearless}` → gateway (`lobby-create` limiti) → `LobbyEndpoints.Create` (format metni → `SeriesFormat`) → `CreateLobby`: CSPRNG'den kod (16 bayt), oyuncu kimliği (16 bayt) ve yazı tura (ilk mavi) → `Lobby.Create` (seri ayarı havuza sığıyor mu) → depoya ekle → token üret → `{code, seat: "A", token}`.

**Katılmanın yolu:** `POST /api/lobbies/{code}/join` → `JoinLobby`: yeni oyuncu kimliği → `ILobbyRepository.Execute(code, lobby => lobby.Join(player))` (lobinin kilidi altında) → koltuk alındıysa token → `{code, seat: "B", token}`.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Lobi kuralları | `Domain/Lobby.cs` | Açan A'ya, katılan B'ye oturur; boş koltuk bir kez alınır (`lobby_full`). Lobi serinin sahibi; seri ayarı lobi açılırken doğrulanır (rakip davet edildikten sonra değil). |
| Kod ve kimlik | `Domain/LobbyCode.cs`, `PlayerId.cs` | 128 bit, base64url, 22 karakter. Rastgele baytlar dışarıdan gelir (Domain saf); istemciden gelen değer katı biçimden geçer. |
| Aktif lobiler bellekte | `Infrastructure/Lobbies/InMemoryLobbyRepository.cs` | Canlı draft her hamlede değişir, diske yazmak gereksiz. Bedeli: yeniden başlatmada açık lobiler düşer, tek API kopyası. Port async ve kilit portun içinde: kalıcı depoya geçiş sadece bu sınıfı değiştirir. |
| Eşzamanlılık | aynı | `Execute` işlemi lobi başına `lock` altında koşturur; `Lobby` eşzamanlılık bilmez. Aynı anda 10 katılmadan biri oturur (test). Canlı `Lobby` dışarı verilmez, okuma da kilit altında kopya (`LobbyView`) üretir. |
| Token | `Infrastructure/Auth/GuestTokenService.cs` | Üretir ve doğrular. Doğrulama gateway'le aynı kurallar (sadece HS256, yayıncı, hedef, süre, 30 sn tolerans). Claim'ler değer nesnelerinden geçer; `seat` sadece tam "A"/"B". |
| API de doğrular | `Api/Lobbies/LobbyEndpoints.cs` | Gateway imzayı doğrulayıp geçirir; API claim'leri imzasız okumaz, kendisi de doğrular. Sonra yetki: token'daki lobi = yoldaki lobi ve oyuncu o koltukta oturuyor mu (`not_in_lobby`, 403). |
| Hata → HTTP | aynı | `unauthorized` 401, `not_in_lobby` 403, `lobby_not_found` 404 (biçimi bozuk kod da), `lobby_full` 409, diğerleri (`pool_too_small`, `invalid_format`) 400. Gövde: `{error, message}`. |
| Şampiyon listesi | `Infrastructure/Champions/EmbeddedChampionCatalog.cs` | `tools/LolDraft.Balance/champions.json` derlemeye gömülür (tek kaynak); şimdilik sadece kimlikler. |
| Kayıt | `Infrastructure/InfrastructureServices.cs` | `AddInfrastructure(jwtSigningKey)` tek public kapı; gerçekler `internal sealed`. API açılışta `JWT_SIGNING_KEY` ister (≥ 32 bayt), yoksa açılmaz. |

### 8.12 Uygulama: 4e-2a (canlı draft hub'ı)

**Bir hamlenin yolu:** istemci `Pick("zed", actionId)` → (gateway: JWT, `hub-connect`) → API `GuestAuthenticationHandler` (token Bearer başlığından, hub yolunda başlık yoksa `access_token` sorgusundan) → `DraftHub.Pick` (metin → `ChampionId`, `Guid` → `ActionId`) → `LobbyCommands.Pick` → `ILobbyRepository.Execute` (lobi kilidi) → token'daki koltuk = lobideki koltuk mu → `Lobby.Pick(player, actionId, …)`: tekrar kontrolü, koltuk → bu maçtaki renk (`Series.SideOf`) → `DraftSession.Pick` → başarılıysa yeni durum (`LobbyView`) aynı kilidin altında üretilir → hamleyi yapana `{ok, error, message}`, gruptaki herkese `LobbyUpdated`.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Draft başlangıcı | `Domain/Lobby.Ready` | İki koltuk da hazır deyince `Series.StartDraft`; hazırlar sıfırlanır, serinin sonraki maçında yeniden "hazır" denir (karar 2026-11-10). Lobi dolmadan `lobby_not_full`, maç sürerken `game_in_progress`. |
| Yetki ve renk | `Domain/Lobby` | Hamle oyuncu kimliğiyle gelir; lobide değilse `not_in_lobby`. Token renk taşımaz: koltuk her maç seriden renge çevrilir. |
| Tekrar koruması | `Domain/Lobby.Once`, `ActionId` | Aynı oyuncu + aynı `actionId` → ilk sonuç (başarılı ya da hatalı) aynen döner, hamle tekrarlanmaz. Lobi başına son 64 kimlik. Lobide olmayanın kimliği saklanmaz. Boş Guid `invalid_action_id`. |
| Kimlik doğrulama | `Api/Auth/GuestAuthenticationHandler.cs` | Doğrulamanın tek gerçeği `IGuestTokenService`; handler sadece token'ı bulur. REST GET de aynı handler'ı kullanır (`RequireAuthorization`). Hub `[Authorize]`: token yoksa negotiate 401. Token geçerli ama oyuncu lobide değilse `OnConnectedAsync` bağlantıyı kapatır. |
| Yayın | `Api/Hubs/DraftHub.cs` | Her lobi bir SignalR grubu (`lobby:{kod}`). Bağlanan önce güncel durumu alır. Başarısız hamle yayınlanmaz, sadece yapana hata döner. |
| Geri sayım | `LobbyStateDto.serverNow` + `draft.deadline` | Saniyelik mesaj yok: kalan = deadline − serverNow, istemci kendi saatiyle çizer (saat farkını serverNow'dan düzeltir). Süre dolunca karar sunucunun (4e-2b sayaç servisi). |
| JSON sözleşmesi | `Api/Lobbies/LobbyDtos.cs` | Domain tipleri doğrudan serileştirilmez; adlar elle çevrilir: faz `ban1…completed`, taraf `blue/red`, adım `ban/pick`, koltuk `A/B`, yuvalar `top, jungle, mid, bot, support`. |
| SignalR sınırları | `Api/Program.cs` | Mesaj ≤ 4 KB, canlılık 15 sn, istemci zaman aşımı 30 sn (§8.6). |

Testler: Domain (hazır, renk çevirisi, tekrar), Api (gerçek SignalR istemcisi, long polling: bağlanma, yayın, sıra dışı hamle, tekrar, geçersiz şampiyon, token'sız/bozuk token), Gateway (hub YARP üzerinden uçtan uca; token'sız hub isteği API'ye ulaşmaz).

### 8.13 Uygulama: 4e-2b (lobi saati + hub mesaj limiti)

**Lobi saati:** tek arka plan döngüsü, zamana bağlı her şey buradan (sayaç; 4e-2c'de hükmen galibiyet ve temizlik de). `LobbyClockService` (`PeriodicTimer`, 250 ms) → `AdvanceLobbies` → her lobi kendi kilidi altında `Lobby.Advance(now, random)` → durumu değişenler gruba `LobbyUpdated`.

| Konu | Nerede | Karar ve neden |
|---|---|---|
| Karar Domain'de | `Domain/Lobby.Advance` | Adımın süresi dolduysa `DraftSession.Timeout` (boş ban / rastgele uygun pick / swap biter). Servis sadece zamanlar ve yayınlar. |
| Hamleyle yarış | aynı | İkisi de lobi kilidinden geçer. Süre dolduktan sonra gelen hamle `turn_expired` (ya da sıra çoktan geçmiş), süre dolmadan zaman aşımı olmaz: aynı adım iki kez oynanamaz. |
| Gecikme | `Api/Background/LobbyClockService.cs` | Süre dolduktan en geç ~250 ms sonra uygulanır. Tikteki hata loglanır, döngü durmaz (durursa hiçbir draft'ın süresi dolmaz). |
| Rastgele pick | `Infrastructure/InfrastructureServices.cs` | `IDraftRandom` = `RandomNumberGenerator.GetInt32`: tahmin edilemez (yoksa "süreyi bitirirsem X gelir" diye oynanırdı). |
| Ölçek | `Application/Lobbies/AdvanceLobbies.cs` | Her tikte bütün lobiler gezilir (`ILobbyRepository.Codes()`); yüzlerce lobi milisaniyeler. Binlerce olursa bitiş anına göre sıralı kuyruk. |
| Test | `LobbyClockTests` | `IClock` elle ilerletilir ("31 sn geçti"), döngü gerçek. Test saati gerçek "şimdi"den başlar: token o saatle imzalanır, doğrulama sistem saatine bakar. |

**Hub mesaj limiti** (`Api/Hubs/RateLimitHubFilter.cs`, `IHubFilter`): bağlantı başına saniyede 5, dakikada 60. Aşan mesaj hub metoduna hiç ulaşmaz, yapana `rate_limited` döner. Her ret bir ihtar; dakikada 10 ihtar → bağlantı kapatılır (karar 2026-11-10).
- Sayaç `SlidingWindow` (kayan pencere, olay zamanları kuyrukta): .NET'in hazır sınırlayıcıları otomatik yenileme için bağlantı başına zamanlayıcı açar, bu açmaz. Zaman `TimeProvider`'dan: test elle ilerletir.
- Sayaçlar bağlantının `Items`'ında yaşar, bağlantıyla birlikte gider. Kilit yok: SignalR bir bağlantının çağrılarını sırayla işler.
- Önce saniye, sonra dakika: saniyeye takılan mesaj dakika bütçesini harcamaz; reddedilen deneme pencereye yazılmaz (ısrar pencereyi uzatmaz).

Kalan (4e-2c): bağlantı takibi + hükmen galibiyet (2 dk; iki oyuncu birden yoksa kimse kazanmaz, lobi 5 dk sonra kapanır, karar 2026-11-11), lobi temizliği (30 dk hareketsiz) ve IP başına 3 lobi sınırı (o zamana kadar bellekteki lobiler silinmez), rejoin (ertelendi). Draft bitince simülasyon + `RecordGame` 4f'de.

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
- Havuz §16.2 biçiminde JSON (`roles`: top, jg, mid, adc, sup); varsayılan `champions.json`: oyunun 40 şampiyonu (faz C). Eski `sample-champions.json` (prototipin 10'u + 10 arketip) faz C'de kaldırıldı; faz B ölçümleri onunla.
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

- **v0.1 (MVP, ~5-7 hafta):** 40 elle etiketli şampiyon, 1v1 link daveti, bo1/bo3, fearless, turnuva draft sırası + sunucu sayacı, C# motoru (bu dokümandaki kurallar), harita oynatıcı + log, maç sonu dökümü. Gateway (YARP) + rate limiting + misafir JWT + güvenlik başlıkları + docker-compose dağıtımı.
- **v0.2:** Veri hattı + Markov hareket tablosu + kill ısı haritası, bo5 (fearless bo5 havuz büyüyünce), koç direktifleri, draft kalitesi (Monte Carlo).
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

**`range` = koridor menzili (karar):** `range`'e sadece K1 bakar, o da lane fazında (dk < 14). Geç oyundaki menzil sınıf ve etiketlerde (poke, peel, dive). Form değiştiren şampiyon, **lane'de hangi formda baskı kuruyorsa** öyle etiketlenir. `hybrid` ya da dakikaya bağlı menzil denendi, elendi: kural bir dal fazla, veri modeline zaman girer.

| Şampiyon | Etiket | Neden |
|---|---|---|
| Jayce | `ranged` | lane'de range formda poke |
| Kayle | `melee` | 6'ya kadar melee, lane'de zayıf; geç gücü `l` 10 ve `econ` 3'te |
| Gnar | `ranged` | lane'i mini formda oynar |
| Elise, Nidalee (orman) | önemsiz | K1 koridorda; mid/sup Nidalee `ranged` (mızrak poke'u) |

**`econ` (altın bağımlılığı):** 0 = altınsız da işler (bazı destekler/tanklar), 1 = az, 2 = normal, 3 = item'sız işe yaramaz (hiper taşıyıcı). Etiketleme yükü: şampiyon başı ~13 değer × 40 şampiyon.

**Uygulama (adım 8):** `ChampionProfile(id, name, range, ana sınıf, ikincil sınıf?, e, m, l, econ, ap, ChampionTags)`. Sınıf liste değil ana + ikincil (record eşitliği değer üzerinden kalsın); ikincil sınıf ana sınıfla aynı olamaz. `ChampionTags` değer nesnesi, verilmeyen etiket 0. `IsHyperCarry` ikincil sınıfa da bakar (Kassadin: suikastçı/büyücü). Karar (kullanıcı): sınıf listesi bu 6'sı, `cc` ve `waveclear` etiketi yok (`pick`/`en` ve sınıf yeterli); gerekirse sonra eklenir. JSON'da `range`, `classes` (1-2), `econ`, `tags` (eksik etiket 0).

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
s     = { econ 0: 0,45 · econ 1: 0,9 · econ 2: 1,35 · econ 3: 1,8 }   // altına duyarlılık (deneyle ölçeklendi, aşağıda; faz D'de %10 indirildi)
G_eff = G_pot(dk) × clamp(1 + s × (r − 1), 0,5, 1,4)
```
- **Scale şampiyon altına daha duyarlıdır** (`econ` 3): az altınla potansiyelinin yarısına iner, çok altınla en fazla %40 artar.
- Taban ve tavan kartopu sınırlar (azalan getiri): 10 kill alan, 3 kill alandan çok daha güçlü olmaz.
- Uygulama (`MatchRun.Power.cs`): `s = 0,45 + 0,45 × econ` (adım 8'den beri şampiyona göre, öncesinde herkes 1,5; faz D ayar turu 2'de 0,5 + 0,5 × econ'dan indirildi). Örnek r = 1,1: Jinx (econ 3) ×1,18, Draven (econ 2) ×1,135, Leona (econ 0) ×1,045.
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

**Uygulama (adım 9a, `LaneRules.cs`):** saf statik sınıf; her kural `RuleEffect(kural, taraf, değer, kaynak şampiyon)` döndürür (karne, adım 10, bunları toplar). Koridorda `Δ = 0,1 × güç farkı + Σ kural + gürültü`, K2 tutarsa tüm Δ (gürültü dahil) × 0,7. Yorumlar:
- K1 ve K2 sadece üst ve ortada: bot 2v2, oranın melee/tank'ı genelde engage destek (tampon değil); bot'u K4 karşılar. K1'de melee `max(mob, en)` ≥ 2 ise bonus +0,3 yerine +0,1.
- K2: koridorda ana sınıfı tank olan; iki tarafta da olsa bir kez × 0,7.
- K3: sınıf ana ya da ikincil (LeBlanc vs Kassadin: iki yöne de tutar, sıfırlanır). dk 10-17 arası etkisiz.
- Kurallar toplanır: Zed vs Orianna 5. dk → K3 +0,3, K1 Orianna +0,1 → net +0,2.
- Ağırlıklar şimdilik `LaneRules` sabitlerinde; JSON'a taşıma faz D'de (ayar orada denge aracıyla yapılacak).

**Takım kuralları** (d'ye eklenir, olay türüne göre):

| # | Kural | Koşul | Nerede | Etki |
|---|---|---|---|---|
| K5 | Engage | `en` toplam farkı | objektif savaşı, son savaş | d += 1,0 × fark (±4); rakip `dis + peel` toplamı kadar silinir (faz D: eskiden yarısı) |
| K6 | Poke | `poke` toplam farkı | kuşatma (saldırı ve savunma), objektif savaşı | kuşatmada d += 1,0 × fark (±4); objektif savaşında rakip `en` ≥ 8 ise poke takımına −2, değilse poke takımına 0,5 × fark (±2) (faz D: eskiden sadece ceza, eşik 5) |
| K7 | Pick | `pick` toplam farkı | nehir çatışması, gank, yakalanma | çatışma d += 1,0 × fark (±3); gank başarısı +%5 × fark (±%15) |
| K8 | Dive tehdidi | rakipte hiper taşıyıcı ve rakip `peel + fl` < 3 | takım savaşları | d += 0,8 × `dive` toplamı (±3) |
| K9 | Korunan taşıyıcı | takımda hiper taşıyıcı | dk ≥ 25 takım savaşları | `peel` ≥ 3 → d +2 · `peel + fl` < 2 → taşıyıcının `G_eff` × 0,85 |
| K10 | Frontline eksik | `fl` toplamı < 2 | takım savaşları | d −3 (rakip `en` ≥ 4 ise −4) |
| K11 | Tek tip hasar | AP payı < %20 ya da > %80 | takım savaşları | d −2 |
| K12 | Split | top/mid'de `split` ≥ 2, takım savaşı kaybetmeye yakın | dk ≥ 14 ejder/Herald/Baron | bir karar zinciri (aşağıda): splitter savaşa gelmez; rakip ya cevap verir (4v4 + düello) ya da kuleyi kaybeder (4v5) |
| K13 | Kombo | tablo (Orianna+Malphite, Yasuo+knock-up…) | tablodaki olay türü | d +2 |

Bugünkü sinerji kuralları (frontline, engage, AP dengesi, kombo) K5, K10, K11, K13'e dağılır; maç başı tek "sinerji puanı" kalkar (uygulandı, `SynergyCalculator` silindi).

**Uygulama (adım 9b, `TeamRules.cs`):** `FightKind` (objektif, kuşatma, son savaş, nehir çatışması); takım savaşlarında iki takımın tamamı, çatışmada sadece gelenler. Etkiler `RuleEffect` (koridor kurallarıyla aynı). Yorumlar:
- K5: etkin engage = `en` toplamı − rakibin `dis + peel` toplamı (≥ 0; faz D'den önce yarısı); fark ±4. Örnek: erken comp 7 − 7 = 0, scale comp 3 − 3 = 0 → K5 yok (Lulu'lu koruma engage'i siler).
- K6: objektifte poke toplamı fazla olan takıma: rakip sert engage'liyse (`en` ≥ 8 ≈ havuz ortalaması × 1,6) "Poke, engage'e yakalandı" −2, değilse "Poke objektifi kurdu" +0,5 × fark (±2). Son savaşta poke yok.
- K7: çatışmada gelenlerin `pick` farkı. Gank: başarı += %5 × (gelenlerin `pick` toplamı − hedefin `mob`'u), ±%15 (pick yakalar, mob kaçar; yakalanmayla aynı mantık). Yakalanmadaki pick adım 8'de.
- K9 korumasız taşıyıcı: d'den 0,15 × taşıyıcının `G_eff`'i düşer (her hiper taşıyıcı için).
- K12 split: bonus değil davranış, `MatchRun.Split.cs` (aşağıda "K12 karar zinciri").
- Döküm: `ScoreCategory.Synergy` artık "son savaştaki takım kuralları farkı" (potansiyel güçle, dk 35); karara girmez, denge aracının "kural farkı" tablosu buna bakar.
- Örnek, test takımları son savaş dk 30: K5 Mavi +2, K9 Kırmızı +2, K13 ikisi de +2 → 0: erken comp'un engage'i scale comp'un geç korumasıyla dengelenir. 20. dk objektif: K5 +2, K6 (scale poke'u engage'e yakalanır) +2 → Mavi +4.

**K12 karar zinciri (uygulandı, kullanıcıyla kararlaştırıldı):** split bir karardır, etiket varsa hep gidilmez. Takım savaşını kaybedeceğini düşünen takım haritanın öbür ucunda karşılık arar; rakip ya cevap verir ya da bedelini öder. Her objektif savaşından hemen önce:
1. **Aday var mı:** dk ≥ 14; objektif ejder, Herald ya da Baron (Elder ve son savaşta herkes toplanır); takımın top ya da mid'i, `split` ≥ 2, koridorunda ve rakibini 1v1 yeniyor (`G_eff` yüksek). İkisi de uygunsa en yüksek split'li (eşitlikte top).
2. **Gidiyor mu:** sadece 5v5 ihtimali < %50 olan takım; ihtimal split 2 → %40, split 3 → %60 (aynı şampiyon bazen gider bazen gitmez).
3. **Rakip cevap veriyor mu:** aynı roldeki rakip koridordaysa %50.
   - Cevap verirse: objektif **4v4**; objektiften 6 sn sonra yan koridorda **düello** (`p = clamp(1/(1+e^(−G_eff farkı/6)))`), kazanan kill alır; splitter kazandıysa 10. sn'de kule.
   - Vermezse: objektif **4v5**; splitter 10. sn'de kuleyi karşılıksız alır (normal kule şartları aranmaz; koridorda kule kaldıysa).
- Savaşta olmayanlar ne güçleriyle ne etiketleriyle d'ye girer, savaş kill'lerinde ölmez. `SplitEvent` (splitter, cevap veren) log'a: "Kayle üst koridorda split yapıyor, Renekton cevap verdi". Split kulesi 10. sn'de (savaş sonrası itiş 12'de, karışmasın).
- Örnek: Fiora (split 3), 18. dk ejder, takımı geride (5v5 %38). %60 ihtimalle split'e gider. Renekton cevap verirse ejder 4v4 (~%45), Fiora düelloyu kazanırsa kill + kule; cevap vermezse ejder 4v5 (~%25) ama kule kesin.

**Rol dışı cezası (karar 2026-11-10, henüz uygulanmadı; simülasyon Application'a bağlanırken):**
- Sorun: motor yuvayı rol sayar, şampiyonun `roles`'unu bilmez (`ChampionProfile`'da yok). `e/m/l` şampiyonun kendi rolündeki gücü; rol dışında da aynen kullanılınca istismar açılır (ör. erken gücü yüksek Leona top'ta tam farm alır, cezası yok).
- Karar: yasak değil ceza. Swap'ta yerleştirme serbest, arayüz rol dışı yuvayı uyarır. Yasak olsa fearless bo3'ün 3. maçında (kalan 20 şampiyon) bir rol için uygun şampiyon kalmayabilir, draft kilitlenirdi.
- Uygulama: `ChampionProfile`'a `Roles` eklenir (Application kataloktan taşır). Rolü dışındaki yuvada oynayan şampiyonun gücü **−%25** (ayar düğmesi; kullanıcı %20-30 arası düşünüyor, denge aracıyla ölçülecek). Karnede görünür: "Rol dışı: Leona top'ta (−%25)".
- Denge aracı: `RandomDrafter` hep rolünde draft yapıyor; "rol dışı oranı" parametresi eklenip cezanın etkisi ölçülecek.

### 16.9 Yakalanma (geri dönüş kapısı)
Çok önde olan takım bazen bir yerde yakalanır:
- dk ≥ 15, objektif olmayan dakikalarda (objektifte herkes savaşta) bir kez, dakikanın 5-40. saniyesinde (45. sn kuşatmadan önce); takım altın farkı o an ≥ 1.000 ise.
- Kurban: **önde olan takımın, koridorunda olan (ölü/savaşta değil) en zengini**: rol rakibine göre en yüksek altın oranı.
- İhtimal: `p = min(0,03 + 0,02 × takım altın farkı / 1000, 0,2) × (1 + 0,15 × rakip pick toplamı)`. 3.000 önde → %9/dk, 8.500+ → %20. (Faz D ayar turu 2: eskiden 1.000 başına %1, tavan %12; büyük farkı köreltir, draft tarzına bakmaz.)
- Yakalanırsa kaçma şansı `0,2 + 0,1 × mob`; kaçamazsa rakibin en yüksek `pick`'li müsait şampiyonu öldürür ve katil **ödülü** alır. Log: "Shutdown! Draven alt koridorda yakalandı, kill Lee Sin aldı (+650 ödül altını)". Kill olayı `Lane` boş (koridor anlatıcısının kill'i değil).
- Önde olan için risk, geride olan için umut; son savaştan önce farkın kapanmasını sağlayan mekaniklerden.
- Uygulama (`MatchRun.Pick.cs`): formül yukarıdaki gibi (adım 8'den beri `pick` ve `mob` gerçek). Katil: rakibin müsait oyuncuları içinde en yüksek `pick`'li; eşitlikte nehre yakınlık sırası (ormancı, support, mid, adc, top).
- Ölçüm (5.000 maç, yakalanmalı / yakalanmasız): maç başı 0,6 yakalanma, ortalama ödül ~680. Eşit draft'ta 15. dk 5.000+ önde olan %79,4 / %81,3, erken vs scale'de %73,4 / %74,9 kazanıyor; büyük farkta ~2 puan geri dönüş.

### 16.10 Kuşatma ve maç sonu
- Şart: dk ≥ 16, o dakika objektif yok, **takım altın farkı ≥ eşik** (16. dk 7.000, dakikada −750, 20. dk'dan sonra 4.000; Baron buff'ı varken yarısı), en az bir koridorda dış ve iç kule yıkılmış, son kuşatmadan ≥ 3 dk. (Faz D: eskiden dk ≥ 20 ve sabit 4.000; önde olanlar birikip maçların %12'si tam 20. dk'da bitiyordu.)
- Başarı ihtimali: takım savaşı (16.7) + K6 poke − **savunma avantajı 3** (savunan kule ve inhibitör altında; faz D ayar turu 2). Başarı → Nexus. Başarısız → **throw**: savunan savaşı kazanmış sayılır, kill'ler ödülle → fark kendiliğinden kapanır (sabit +10 kalktı).
- Uygulandı (`MatchRun.Siege.cs`): saldıran = kuşatma anında altında önde olan; K6 poke adım 9'da. Puan (`ScoreSheet`) artık hiçbir karara girmiyor; sadece `Breakdown` çıktısında duruyor, karne gelince (adım 10) altın kaynakları dökümüyle değişecek.
- Ölçüm (10.000 draft): süre 29,3 dk, maçların %52,7'si kuşatmayla bitiyor, erken comp %32,6'sını 35'ten önce bitiriyor (ilk kez hedefte), eşit draft'ta 15. dk 3.000+ önde olan %78,7 kazanıyor. **Bilinen:** bitişlerin %15'i tam 20. dakikada (ilk kuşatma dakikası birikiyor); faz D'de bakılacak (ör. ilk kuşatma için inhibitör şartı ya da eşiğin dakikayla artması).
- Son savaş (dk 35) aynı kalır: o anki `ΣG_eff`, kurallar ve buff'larla.

### 16.11 Çıktı (motor sözleşmesine eklenecekler)
- `KillEvent`'e ödül altını; altın getiren olaylara miktar.
- **Dakika kareleri** (`GoldFrame`, uygulandı): k. kare = k. dakikanın başındaki (t = k × 60) her şampiyonun altını ve CS'si (aşağı yuvarlanmış), `TeamGold(side)` takım toplamı. 0. kare herkes 500; son kare maçın bittiği dakika, Nexus anı → front'ta altın grafiği.
- **Draft karnesi:** maçta etkili olan kurallar (kural, taraf, nerede, ne kadar etki, metin) ve altın farkının kaynakları (koridorlar, kill'ler, ödüller, objektifler). Örnek: "Top: range üstünlüğü (Jayce) → +900 altın · Kayle scale edemedi: 28. dk'da potansiyelinin %55'i · Draven yakalandı → 650 ödül, fark kapandı".
- `ScoreBreakdown` yerine altın kaynakları dökümü.

**Uygulama (adım 10):** `MatchResult.Report` (`DraftReport`), `MatchRun.Report.cs`. Zar atmaz, maç akışını değiştirmez (denge aracı birebir aynı sonucu veriyor).
- **Kurallar** (`RuleImpact`: kural, taraf, yer, kaynak şampiyon, sayı, etki, metin). Etki **doğrudan etki**dir: kuralın katkısı çıkarılınca o an ne değişirdi; kazanılan altının sonraki kartopu sayılmaz (onun için maçı kuralsız yeniden oynatmak gerekirdi).
  - Koridor (K1-K4): her farm diliminde CS gerçek Δ'yla ve kuralın katkısı çıkarılmış Δ'yla (K2'de yumuşatılmamış Δ'yla) hesaplanır; farm altını farkına toplam katkı. Sayı = etkili olduğu dakika.
  - Savaş (objektif, kuşatma, son savaş, çatışma): kuralın tarafının kazanma ihtimali kuralla ve kuralsız d'yle; olay başına ortalama, yüzde puan. Split kararındaki "savaş olsa" hesabı yazılmaz. Gank: K7 payı doğrudan.
  - Her `RuleEffect` kendi karne metnini taşır ("Range üstünlüğü (Jayce)", "Poke, engage'e yakalandı (Jinx)", "Kombo: Leona + Draven").
  - İki yöne de tutan kural (LeBlanc vs Kassadin, K3) iki satır olur: ikisi de gerçekten işledi, birbirini götürür.
- **Taşıyıcılar** (`CarryReport`): her hiper taşıyıcının maç sonunda `G_eff / geç gücü`. Altın çarpanı < 0,9 → "scale edemedi (altını rakibinin gerisinde)"; maç 28. dk'dan önce bittiyse "geç oyuna ulaşamadı"; değilse "scale etti".
- **Altın kaynakları** (`TeamGoldReport`, takım başına): pasif, farm, support item'ı, kill, ödül, asist, plaka, kule, objektif.
- `ScoreBreakdown` şimdilik duruyor (testler ona bakıyor, karara girmiyor); kaldırılması ayrı temizlik.
- K12 split karnede kural satırı değil: log'da zaten (split, düello, kule).
- Örnek (test takımları, seed 7, Mavi 20. dk kuşatmayla): "Erken all-in (LeBlanc) +151 altın, 9 dk", "Gank'ta yakalama (Lee Sin) +10 puan, 4 gank", "Engage üstünlüğü (Leona) +1,7 puan, 4 savaş", "Poke, engage'e yakalandı (Jinx) −1,7 puan", "Kayle scale edemedi (altını rakibinin gerisinde): 20. dk'da gücü geç potansiyelinin %40 kadarı"; altın Mavi 35.749 (kill 4.500) vs Kırmızı 27.029 (kill 900, ödül 1.850).

### 16.12 Denge hedefleri (araçla ölçülecek)
| Ölçüm | Hedef |
|---|---|
| Saf taraf etkisi (ayna) | ~%50 |
| Belirgin iyi draft (kural toplamı yüksek) | %60-65; en iyi draft bile ≤ %70 |
| Üstün kimlik (K5-K8 lehine) | %55-60 |
| Lane fazında belirgin geride kalan scale comp maçı kurtarıyor | %25-40 (faz D'de %25-35'ten genişletildi: eğlence önce, §1) |
| 15. dk 3.000+ altın önde olan kazanıyor | %70-80; 5.000+ önde bile ≤ %85 (eğlence önce, §1; gerçek solo kuyruk %85-98) |
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
8. ~~`ChampionProfile` v2 (range, sınıf, econ, etiketler) + `sample-champions.json`.~~ (bitti; 16.2 "Uygulama") Hemen devreye girenler: `s` econ'dan (16.5), yakalanmada `pick`/`mob` (16.9). Kurallar henüz yok, eski sinerji `Tags` üzerinden. Ölçüm (10.000 draft, faz A sonu → adım 8): Mavi %49,5 → %50,2; süre 29,3 → 29,8 dk; kuşatmayla biten %52,7 → %49,2; yakalanma 0,49 → 0,69; 15. dk 3.000-4.999 önde %69 → %67, 5.000+ %80 → %78; erken comp %47,3 → %46,2 kazanıyor, %32,6 → %29,7'sini 35'ten önce bitiriyor (hedefin hemen altında); geride scale comp %33,6 → %36,3 kurtarıyor (hedefin hemen üstünde); 20. dk birikmesi %15 → %12,4. Beklenen yön: econ 3 taşıyıcılar öndeyken daha güçlü, econ 0 destek/tank altından az etkileniyor, pick'li takımlar daha çok yakalıyor → geri dönüş biraz arttı. Sinerji 5 farkta %67, 7'de %73 (adım 9'da kalkacak).
9. Koridor kuralları (K1-K4), sonra takım kuralları (K5-K13); her kural kendi testiyle.
   - ~~9a: K1-K4~~ (bitti; 16.8 "Uygulama"). Ölçüm (10.000 draft, adım 8 → 9a): Mavi %50,2 → %50,3; süre 29,8 → 29,7; erken comp %46,2 → %46,7 kazanıyor, 35'ten önce %29,7 → %30,9; scale comp kurtarıyor %36,3 → %36,7. Genel tablo neredeyse aynı: koridor kuralları küçük (+0,1-0,3/dk, gürültü σ 0,8) ve örnek havuzda K1 sadece ortada çalışıyor (üstte ranged şampiyon yok). Şampiyon bazında: Büyücü Destek %47,1 → %45,9 (K4'e açık), Roam Mid %43,5 → %44,4 (K3). Motor testi: eşit draft'ta Mavi üstü range'e çevirmek (karşısı mesafe kapatamayan melee) 14. dk'da ortalama 5+ CS fark açıyor. Kural etkisinin ayrıntılı ölçümü adım 11'de (denge aracında kural raporu).
   - ~~9b: K5-K11, K13 (eski tek sinerji puanı kalkar).~~ (bitti; 16.8 "Uygulama") Ölçüm (10.000 draft, 9a → 9b): Mavi %50,3; süre 29,7 → 29,6; erken comp %46,7 → %48,4 kazanıyor, %30,9 → %32,9'unu 35'ten önce bitiriyor ✓; scale comp kurtarıyor %36,7 → %34,3 ✓ (ilk kez hedefte); 15. dk 3.000-4.999 önde %68, 5.000+ %79 ✓; sınıra takılan savaş %2,7 → %5,0. **Kural farkı → kazanma** eğrisi düzgün ve tekdüze: 2 → %56, 4 → %62,5, 6 → %67,5, 8 → %73, 10 → %77, 12+ → %80-90. Hedef (belirgin iyi draft %60-65, en iyisi ≤ %70) fark ~4-6'da tutuyor ama uç draft'lar (fark 8+, maçların ~%7'si) fazla kazanıyor: cezalar üst üste biniyor (frontline yok −4, tek tip hasar −2, korumasız taşıyıcıya dive +3...). Şampiyonlar: engage tank'lar önde (Leona %57, Sejuani %56, Tank Top %55: hem `en` hem `fl` ödülleniyor, K5 + K10), Büyücü Destek %37,6 (fl/en/peel 0 → K10, K4, K5'e açık). Faz D adayları: kural toplamına tavan (ör. ±8) ya da ağırlıkları küçültme; K5 + K10 tank'ı iki kez ödüllüyor mu bakılacak; örnek havuz arketipleri de gerçek veriyle değişecek.
   - ~~9c: K12 split~~ (bitti; 16.8 "K12 karar zinciri"). Ölçüm (10.000 draft): maç başı 0,59 split, %50'si cevaplanıyor, %76'sı kule getiriyor; split yapan takım o objektifi %33 alıyor, maçı %39 kazanıyor (zaten geride olan takım). Genel tablo aynı: Mavi %49,8, süre 29,5, maç başı kule 4,8 → 5,3, scale comp kurtarıyor %34,2 ✓. Dövüşçü Top (split 3) %47,8 → %48,7. Denge aracına split satırları eklendi.
10. ~~Draft karnesi çıktısı (16.11).~~ (bitti; 16.11 "Uygulama")
11. ~~Denge aracına kural etkisi ve kimlik-kimlik raporu → ölçüm.~~ (bitti) Karnedeki kural satırları maç başına (kural, yer) birleştirilir (iki tarafa işlediyse net); rapor: çalıştığı maç, ortalama doğrudan etki, net lehine olan taraf kazanıyor. **Dikkat:** son sütun "kural güçlü" değil "bu kurala sahip draft kazanıyor" demek; kural, draft'ın başka zayıflıklarıyla birlikte gelebilir (K6 kuşatmada lehine olan %41 kazanıyor: poke takımı genel olarak zayıf). Kimlik (`Identity.cs`): her etiket toplamı havuzdaki beklenen takım toplamına bölünür, en yüksek oran ≥ 1,25 ise kimlik o (Engage, Poke, Pick, Koruma = peel + dis, Dive, Split), değilse Karma.
   Ölçüm (10.000 draft, örnek havuz):
   - Kurallar: K5 objektif %96 maç, olay başına 3,7 puan, lehine olan %60 · K6 objektif cezası **%76 maçta** (rakip `en` ≥ 5; havuzda ortalama takım `en` toplamı ~5,75: eşik ortalamanın altında) · K7 gank 5,7 puan ama lehine olan %50 (gürültüde kayboluyor) · K8 nadir (%8) ama lehine olan %73-80 · K10 5,6-5,9 puan, lehine olan %66-73 · koridor kuralları maç başı 50-130 altın (K4 en büyük).
   - Kimlikler: Engage %61,1 (hedef %55-60'ın hemen üstü), **Poke %40,5**, Pick %53,9, Koruma %52,9, Dive %48,4, Split %46,3, Karma %52. Engage vs Poke %66,5, Engage vs Dive %64, Engage vs Split %62; Koruma vs Split %56.
   - Faz D'ye: (1) K6 objektif cezasının eşiği havuz ortalamasına göre (ör. beklenen `en` × 1,4 ≈ 8) ya da kaldırılmalı; Poke kimliğinin tek kazancı kuşatma, objektifte hep ceza yiyor. (2) Engage biraz güçlü: K5 + K10 tank'ı iki kez ödüllüyor (9b'deki not). (3) K7'nin etkisi büyük ama sonuca yansımıyor: pick'in değeri belki yakalanma/gank sıklığında olmalı. (4) Örnek havuz arketip; ayar gerçek 40 şampiyonla yapılacak, şimdi değil.

**Faz C — Veri**
12. ~~40 şampiyon etiketleme~~ (bitti: `tools/LolDraft.Balance/champions.json`; taslağı Claude çıkardı, kullanıcı onayladı. Her koridordan 8, her koridorda farklı tipler; 6 hiper taşıyıcı: Kayle, Karthus, Kassadin, Viktor, Jinx, Vayne. Bilinçli seçimler: Ashe'nin oku `en` değil `pick` 3 (engage tank/destek işi, adc'ye `en` K5'i bozar); Gnar ve Jayce koridorda ranged. K13 kombo tablosu yeni listeye göre: Kayle/Jinx/Vayne + Lulu, Leona + Draven, Yasuo + Malphite/Alistar, Orianna + Amumu.) + ~~tutarlılık kontrol aracı~~ (bitti: `dotnet run --project tools/LolDraft.Balance -- --check [--champions havuz.json]`, `PoolCheck.cs`). Yazdıkları: rol başına sayı (öneri ≥ 6), sınıf dağılımı, etiket ortalamaları, hiper taşıyıcılar ve uyarılar: hiper taşıyıcıya benzeyip econ < 3 · econ 3 ama geç < 8 · tank ama fl < 2 ya da econ ≥ 2 · enchanter ama peel + dis < 2 · suikastçı ama mob < 2 (K3 çalışmaz) · melee nişancı (§16.2 koridor menzili) · split ≥ 2 ama top/mid değil (K12) · adc ama en ≥ 2 · güç toplamı 14-22 dışı · hiç etiket yok. Uyarı hata değildir (Kayle bilinçli istisna). Örnek havuzda: tek uyarı Kayle; dis ortalaması 0,35, peel 0,55 (koruma kıt: Poke/Engage sonuçlarını etkiliyor olabilir, 40'lık listede dengeli olsun).

**Faz D — Dengeleme**
13. Veri hattından gerçek altın eğrisi ("X. dk Y altın önde → kazanma oranı"), altın duyarlılığı ve kural ağırlıkları 16.12 hedeflerine oturtulur.
   - **Ayar turu 1** (40'lık havuz, 10.000 draft; her düğme tek tek ölçüldü):

     | # | Değişiklik | Neden | Sonuç |
     |---|---|---|---|
     | 1 | K6 sert engage eşiği 5 → 8 | havuzda ortalama takım `en` toplamı 5: ortalama rakip de "sert engage" sayılıyordu (ceza maçların %61'inde) | ceza %19'a indi ama Poke %42,4 → %43,6: asıl sebep bu değil |
     | 2 | K6 objektifte simetrik: sert engage yoksa poke farkı × 0,5 (±2) | poke'un tek kazancı kuşatmaydı; gerçekte poke objektifi kurar, ona sert engage karşı koyar | Poke %45,6 |
     | 3 | K5'te `dis + peel` engage'i tam ağırlıkla siler (eskiden yarısı) | Engage herkesi yeniyordu, Koruma'yı bile (%57,5); taş-kağıt-makas çalışmıyordu | Engage %59,3 → %56,6, Engage vs Koruma %50, Poke %47, kural farkı 8-9 %65-68 |
     | 4 | ~~K8 koruma şartı sadece `peel` < 2~~ (geri alındı) | Dive %44: K8 maçların %6'sında | K8 %18'e çıktı ama Dive yerinde (%44,5), kural farkı 9 %72'ye çıktı: Dive'ın zayıflığı K10'dan (suikastçıların `fl`'si 0) |
     | 5 | Kuşatma 16. dk'da açılır, eşik dakikayla iner (16. dk 7.000 → 20. dk 4.000) | maçların %12'si tam 20. dk'da bitiyordu (birikme) | 20. dk %3,3; 17-20 arası yayıldı; 24. dk (Baron sonrası) %15 kaldı, gerçekçi |

     Sonuç (önce → sonra): Engage %58,3 → %56,4 (hedef %55-60 ✓) · Poke %42,4 → %47,2 · Pick %52,3 · Koruma %51,2 · Split %47,6 · Dive %44,1 · Karma %50,6 · kural farkı 9-10 %70 ✓, 11+ %76-83 (maçların ~%2,5'i) · 15. dk 5.000+ önde %82 (≤ %85 ✓) · scale comp kurtarıyor %31 ✓ · erken comp %50,5 kazanıyor, %34,6'sını 35'ten önce bitiriyor ✓ · Mavi %49,4 · süre 29,1 dk. Şampiyonlar %41,5 (Brand) - %55,4 (Thresh, Alistar).
     Açık: Dive %44 (suikastçılara `fl` mı, K10'a dive istisnası mı?), poke destekler (Brand %41,5, Karma), kural farkı 11+ (toplam kural d'sine tavan adayı).
   - **Ayar turu 2: 15. dk 5.000+ önde olanın kazanma oranı ~%75'e** (kullanıcı isteği; §1 eğlence önce). Kavram: önde olan bu farkı ya altının güce dönüşmesiyle (`s`) ya da kuşatmayla maça çevirir; geri dönüş kapıları (ödül, yakalanma, throw) onu yavaşlatır. Hesap: 5.000 takım farkı rol başına ~1.000, r ≈ 1,1, güç ×1,15 / ×0,85 → her takım savaşında d ≈ +10 (~%66); 4-6 savaşta öndeki üst üste kazanır.

     | Deneme | 15. dk 5.000+ | 15. dk 3-5k | Ezilen scale comp kurtarıyor | Erken comp kazanıyor / 35'ten önce |
     |---|---|---|---|---|
     | Başlangıç (tur 1 sonu) | %82,1 | %69,1 | %31,1 | %50,5 / %34,6 |
     | Clamp 0,6-1,3 (eskiden 0,5-1,4) | %82,1 | %69,0 | %31,2 | — (geri alındı: 5.000 farkta r ≈ 1,1, clamp'e hiç değmiyor) |
     | **A.** Kuşatmada savunma avantajı (saldıran d −3) | %80,9 | %68,1 | %32,5 | %49,7 / %33,1 |
     | `s` %20 düşük (A'sız yakalanmasız denendi, A üstüne) | %78,0 | %65,7 | %37,9 | %46,0 / %29,6 (geri alındı: C'den kötü) |
     | **B.** A + yakalanma 1.000 başına %2, tavan %20 | %79,1 | %66,0 | %35,6 | %48,1 / %30,9 |
     | **C. (seçildi)** B + `s` %10 düşük | **%76,7** | %65,2 | %37,8 | %46,6 / %29,8 |

     **Yapısal gerilim:** 15. dk'da büyük farkla önde olan çoğunlukla erken comp; önde olanı yavaşlatan her mekanik geride kalan scale comp'a yarar. Bu yüzden "ezilen scale comp kurtarıyor" hedefi %25-40'a genişletildi (geride kalanın şansı = eğlence). C'de diğerleri: Mavi %49,8, süre 29,8 dk, ruhu alan %65,9, yakalanma maç başı 1,06, Engage %57,0, Poke %46,2, Dive %44,0.
     Açık (tur 3): erken comp %46,6'ya düştü (35'ten önce %29,8, hedefin hemen altı) → küçük telafi adayı: 16-19. dk kuşatma eşiği biraz düşük ya da erken güç eğrisi.
   - **Ayar turu 3 (denendi, geri alındı):** erken kuşatma eşiği dakikada −750 yerine −500 (16. dk 6.000). Etkisiz: erken comp %46,5, 35'ten önce %29,9 (gürültü içinde). Eşik bağlayıcı değil; erken comp 16-19. dk'da kuşatma şartını (bir koridorda iki kule) nadiren sağlıyor. Erken comp'un %46,5'i tur 2'nin (C) bilinen bedeli olarak kalıyor; telafi motor yerine veride aranabilir (erken şampiyonların `e`/`m` değerleri) ya da kule şartında. Şimdilik açık, öncelik düşük.
   - ~~Gerçek eğri~~ (bitti: `data-pipeline/05_economy.py` → `out/economy.json`; denge aracıyla aynı ölçümler). Not: `DRAGON_SOUL_GIVEN` ruhun alındığı an değil (2. ejderden sonra ruh türü, `teamId` 0); ruh = 4. ejderi ilk kesen takım.

     | Ölçüm | Gerçek (EUW Challenger/GM solo, 495 maç) | Motor (10.000 draft, adım 11) | Hedef |
     |---|---|---|---|
     | Ortalama süre | 26,4 dk | 29,5 dk | 25-32 |
     | Maç başı kill | 54,6 | 22,8 | 15-30 |
     | 14. dk CS (koridorcu + ormancı) | 111 | 102 | — |
     | 15. dk ortalama takım altın farkı | 3.567 | 3.128 | — |
     | 15. dk önde: 0-1.499 / 1.500-2.999 | %57 / %64 | %53 / %60 | — |
     | 15. dk önde: 3.000-4.999 / 5.000+ | **%84,5 / %98,5** | %68 / %79 | %70-80 |
     | 10. dk önde 1.500-2.999 | %79 | %61 | — |
     | Ruhu alan kazanıyor | %96 (100 maç) | %69 | %65-75 |
     | 14. dk 3.000+ geride kurtarıyor | %8,5 (223 maç) | %34 (sadece scale comp) | %25-35 |

     Okuma: solo kuyrukta kartopu motordan çok daha sert (3.000+ önde %85-98, geride olan %8,5 kurtarıyor). Ama solo kuyruk **teslimle** biter (geride olan 15-20. dk'da bırakır; ortalama 26,4 dk) ve koordinasyon zayıftır; turnuva oyunu daha az kill ve daha çok geri dönüş üretir.
     **Karar (kullanıcı): eğlence önce (§1).** Motor solo kuyruğa oturtulmaz; "turnuva hissi" hedefleri kalır, geride olanın her zaman gerçek şansı olur. Gerçek veri sadece yön verir: önde olanın kazanma oranı en fazla %80-85 (5.000+ önde için üst sınır), erken fark biraz daha belirleyici olabilir (gerçekte 10. dk 1.500+ önde %79). Geri dönüş hedefi %25-35 kalır. Kill sayısı turnuvaya yakın (15-30).

### 16.14 Kullanıcıya açık sorular
1. ~~Sınıf listesi yeterli mi?~~ Evet, şimdilik bu 6'sı; gerekirse sonra eklenir.
2. ~~`cc` ve `waveclear` ayrı etiket olsun mu?~~ Hayır, `pick`/`en` ve sınıf yeterli.
3. K1-K13'te oyun bilgisine göre yanlış ya da eksik olan var mı? Yönler ve büyüklükler (ör. range baskısı +0,3) mantıklı mı?
4. Yakalanma (16.9) ve throw'un ödülle kapanması: geri dönüş sıklığı hedefi ne olsun? Öneri: lane'de belirgin geride kalan %25-35 kurtarır.
5. Oyuncu ne görsün? Öneri: draft sırasında kimlik çubuğu (hangi kimliğin neyi yendiği gizli), maç sonunda tam karne.
