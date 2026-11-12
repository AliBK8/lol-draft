# LoL Draft Oyunu — Tasarım Dokümanı

Sürüm: 0.4 · Tarih: 12 Kasım 2026 · Durum: motor + sunucu çalışıyor, ön yüz sırada (iş sırası: `CLAUDE.md`)

## Doküman haritası

Bölüm numaraları (§) bütün dosyalarda ortaktır; koddaki "DESIGN.md §X" atıfları bu tabloyla bulunur.

| Dosya | Bölümler | İçerik |
|---|---|---|
| `DESIGN.md` (bu dosya) | §0-7, §10-15 | Özet, karar günlüğü, oyun akışı, motor v1 kuralları, hareket, veri hattı, ön yüz, yol haritası, fikirler |
| [`SISTEM.md`](SISTEM.md) | §8.1-8.9 | Mimari, katmanlar, kapsülleme, rate limiting, güvenlik, API yüzeyi, dağıtım |
| [`UYGULAMA.md`](UYGULAMA.md) | §8.10 ve sonrası | Adım adım uygulama notları (4c, 4e-1, 4e-2a/b/c, 4f-1, 4f-2, 5a, 5b, 5c, …); yeni adımlar buraya eklenir |
| [`EKONOMI-V2.md`](EKONOMI-V2.md) | §16 (16.13 hariç) | Şampiyon modeli v2, altın ekonomisi, kurallar K1-K13, yakalanma, kuşatma, karne |
| [`DENGE.md`](DENGE.md) | §9, §16.13 | Denge aracı, ölçümler, ayar turları, uygulama sırası (faz A-D) |

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
| Rol dışı oynama | Serbest ama cezalı ve kumarlı: rol mesafesine göre güç −%20…−%35, lane fazında oynak koridor, karnede "kumar tuttu / tutmadı" (§16.8). (2026-11-10, güncellendi 2026-11-12) | Reddedildi: swap'ta yasak (fearless 3. maçta uygun şampiyon kalmayabilir). Reddedildi: cezasız bırakmak (motor rolü bilmiyor, istismar açılır). |
| Şampiyon görselleri | Riot Data Dragon portreleri, betikle bir kez indirilir ve gateway'den sunulur; sayfa altında Riot'un fan projesi notu. (2026-11-12) | Reddedildi: tarayıcının doğrudan Riot'tan çekmesi (CSP'ye dış adres, oyuncunun IP'si Riot'a gider, yama sürümü kayar). Kısaltmalı daireler yerine gerçek portre: draft ekranında tanımak çok daha kolay. |
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
- **Sürpriz:** her koridorda her dakika %2 ihtimalle (3. dk'dan itibaren) rastgele bir takım koridorda kill alır: outplay (kahraman rakibini indirir) ya da geri püskürtülen dive (rakip atlar, düşer), eşit ihtimal. Kahraman ya da rakipler koridorda değilse olmaz. Kendi ödülü yok; altın, shutdown ödülü, respawn ve kaçan farm kill kurallarından gelir: geride olanın önde olanı indirip ödülü topladığı geri dönüş anı. (2026-11-12; ekonomi v2'den önce sadece puan veriyordu ve v2'de hiçbir şeyi değiştirmiyordu. Ölçüm, 4.000 maç: kill 23,4 → 25,0, shutdown 7,3 → 7,8, kule 5,5 → 6,0; 15. dk 5.000+ önde %80,2 → %78,8, gerisi gürültü içinde.) `surprise` puanı sadece dökümde.

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
| `surprise` | side, lane, val | Metinsiz işaret (haritada rozet); log satırı aynı saniyedeki `kill` ("Sürpriz: …") |
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
- **İkonlar:** Riot Data Dragon portreleri, takım rengi çerçeveyle; bir kez indirilip kendi sunucumuzdan sunulur (`web/public/champions`, §8.17). Ölü ikon %30 opak, base'te.
- **Oynatma:** 1x = 20 oyun saniyesi / gerçek saniye (1 dk = 3 sn), 2x, 4x, sona atla. Olay zamanı gelince log'a yazılır, haritada kısa bir işaret çıkar (✕, "Ejder", "Çatışma", "Throw!").
- **HUD:** Kill skoru, saat, altın farkı (`goldFrames`'ten, dakikalar arası doğrusal, 100'e yuvarlanmış), ejder sayısı, yıkılan kule sayısı, kule ikonları (§8.19).
- **Geliştirici modu ("Matematiği göster"):** Canlı toplam ve koridor puanları, log'da olay ihtimalleri.
- **Maç sonu (karne, §16.11):** Kazanan, dakika, bitiş türü, son savaşın ihtimali, iki takımın altın kaynakları, etkiye göre draft kuralları, hiper taşıyıcılar, rol dışı kumarlar (§8.19). v1'deki kategori grafiği ve sinerji maddeleri v2'de karneye dönüştü.
- **Denge paneli:** "1000 maç oynat" (seed = i × 7919).

---

## 10. Yol haritası

- **v0.1 (MVP, ~5-7 hafta):** 40 elle etiketli şampiyon, 1v1 link daveti, bo1/bo3, fearless, turnuva draft sırası + sunucu sayacı, C# motoru (bu dokümandaki kurallar), harita oynatıcı + log, maç sonu dökümü. Gateway (YARP) + rate limiting + misafir JWT + güvenlik başlıkları + docker-compose dağıtımı.
- **v0.2:** Veri hattı + Markov hareket tablosu + kill ısı haritası, bo5 (fearless bo5 havuz büyüyünce), koç direktifleri, draft kalitesi (Monte Carlo).
- **v0.3:** 2v2, turnuva modu (4/8 kişi), paylaşım kartı + tekrar linki.

Güncel iş sırası `CLAUDE.md` "Sıradaki işler"de tutulur.

---

## 11. İleride eklenebilecek fikirler

| Fikir | Kısaca |
|---|---|
| Draft kalitesi ve "ne olurdu?" | Maç sonu aynı draft 1000 kez; alternatif pick'lerle karşılaştırma ("Kayle yerine Jax %57"). |
| Koç kimlikleri | Seri başında pasif yetenekli koç (rakip direktifini görme, gerideyken fazla shutdown vb.). |
| Maç içi karar anları | Baron/bitirme anında iki oyuncuya eşzamanlı gizli seçim. Şu anki "tam simülasyon" yönünde açık soru. |
| Paylaşım kartı + tekrar linki | Üç kritik an + skor; link kayıtlı olay listesini oynatır (seed'le yeniden simüle etmez: denge ayarı ve platform farkı eski maçı değiştirirdi; karar 2026-11-11). |
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

### Prototipten gelen bilinen sorunlar (hepsi C# motorunda çözüldü)
Kule olayı hiç oluşmuyordu (→ §4.8 kule = fırsat); dakika içinde zaman sırası tutarsızdı, ölü şampiyon kill alabiliyordu (→ §4.5 kayıt/çözüm); gank tarafı ihtimali sınırsızdı (→ `p_Mavi = clamp(1/(1+e^(−fark/4)))`).

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
