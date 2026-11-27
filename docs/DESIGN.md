# LoL Draft Oyunu — Tasarım Dokümanı

Sürüm: 0.4 · Tarih: 12 Kasım 2026 · Durum: motor + sunucu çalışıyor, ön yüz sırada (iş sırası: `CLAUDE.md`)

## Doküman haritası

Bölüm numaraları (§) bütün dosyalarda ortaktır; koddaki "DESIGN.md §X" atıfları bu tabloyla bulunur.

| Dosya | Bölümler | İçerik |
|---|---|---|
| `DESIGN.md` (bu dosya) | §0-7, §10-15 | Özet, karar günlüğü, oyun akışı, motor v1 kuralları, hareket, veri hattı, ön yüz, yol haritası, fikirler |
| [`SISTEM.md`](SISTEM.md) | §8.1-8.9 | Mimari, katmanlar, kapsülleme, rate limiting, güvenlik, API yüzeyi, dağıtım |
| [`UYGULAMA.md`](UYGULAMA.md) | §8.10 ve sonrası | Adım adım uygulama notları (4c, 4e-1, 4e-2a/b/c, 4f-1, 4f-2, 5a, 5b, 5c, 6, H4, …); yeni adımlar buraya eklenir |
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
| Maç içi oyuncu etkisi | Maç motor tarafından oynanır; oyuncunun girdisi draft. 2026-11-19 yön: ileride maç öncesi oyun planı + maç içi 3-5 karar anı (eşzamanlı gizli seçim, plan varsayılan cevap; §4.10, henüz uygulanmadı). Savaş için yapay zeka modeli yok: gerçek veri katsayıları ayarlar, sonuç kurallarla çalışır. | Reddedildi: oyuncunun her fazda puan dağıttığı "Koç Masası" sistemi ("bütün güçleri ben seçiyorum, olmaz"). Reddedildi: maç içi kart sistemi (kafaya yatmadı). Reddedildi: tamamen zar. |
| Kazanma mantığı | Gizli takım puanı + puandan etkilenen savaş olasılıkları. Maçı son savaşı / kuşatmayı kazanan bitirir. | v1 prototipte "puanı yüksek olan kazanır" vardı; savaşı kaybedenin oyunu bitirmesi mantıksızdı, değiştirildi. |
| Görselleştirme | Hesap motoru + Anlatıcı (olay bütçesi) + zamanlı olay listesi; front sadece oynatır. | Football Manager maç motoru mantığı. |
| Hareket | Senaryo kuralları (v2 prototip) hedefi verir; şampiyon hedefine haritada yürür (2026-11-19, §8.26: ızgara + A*, oyun hızı, olaya erken çıkış). Hedef: boşta hareket gerçek timeline'lardan bölge geçiş olasılıkları (Markov tablosu). ML yok. | ML gereksiz; dakikalık konum verisiyle Markov yeterli ve basit. |
| Stack | ASP.NET Core + SignalR (tek API uygulaması) önünde YARP gateway, React + TypeScript, SQLite ile başla. Veri hattı Python. | Gateway; tek giriş noktası, rate limiting, TLS ve auth için sonradan eklendi. Mikroservis, Redis, Hangfire MVP için gereksiz. Node/TS alternatifi değerlendirildi; motor paylaşımına gerek olmadığı ve Monte Carlo ağır olduğu için .NET. |
| Güvenlik | Sunucu otoriter, kayıtsız misafir JWT, iki katmanlı rate limiting (gateway + hub), katmanlı mimari ve sıkı kapsülleme. | §8 |
| Seri: taraf ve fearless | Takım kimliği sabit koltuk (`TeamSlot` A/B), renk maçlık: ilk mavi rastgele, sonra renkler her maç değişir. Fearless tek tür: seçilen şampiyon iki takıma da kilitlenir. Fearless + bo5 havuz yetene kadar kapalıydı (40'lık havuza sığmıyordu); havuz 80'e çıkınca açıldı (2026-11-17). (2026-11-10) | Reddedildi: "kaybeden taraf seçer" (ek bir seçim ekranı, otomatik değişim daha basit). Reddedildi: Host/Guest koltuk adı (2v2 ve turnuva modunda anlamsız). Reddedildi: soft fearless. |
| Lobi ömrü ve koltuğa geri dönme | Lobi açılıştan en fazla 2 saat yaşar (hareket olsa da; 2 saatte ~20 maç oynanır). Sekmeyi kapatan oyuncu linki yeniden açınca tarayıcıdaki tek kullanımlık koltuk anahtarıyla aynı koltuğa döner (§8.4, §8.24). (2026-11-17) | Reddedildi: başka cihaza taşıma linki (gerek yok; linki gören koltuğu alırdı). Reddedildi: rejoin'de eski JWT'yi iptal etmek (koltuk "nesli" kontrolü; anahtar zaten tek kullanımlık). Reddedildi: süresi dolan JWT'yi sessizce yenilemek (2 saat sınırıyla gereksiz). |
| Rol dışı oynama | Serbest ama cezalı ve kumarlı: rol mesafesine göre güç −%22…−%60, lane fazında oynak koridor, karnede "kumar tuttu / tutmadı" (§16.8). (2026-11-10, güncellendi 2026-11-12; faz D tur 4'te sertleştirildi, 2026-11-17) | Reddedildi: swap'ta yasak (fearless 3. maçta uygun şampiyon kalmayabilir). Reddedildi: cezasız bırakmak (motor rolü bilmiyor, istismar açılır). |
| TLS | Gateway'in önünde Caddy: sertifikayı (localhost'ta iç CA, alan adında Let's Encrypt) alır, yeniler, HTTP→HTTPS yönlendirir. Gateway düz HTTP konuşur, `X-Forwarded-*`'a sadece Caddy'nin sabit IP'sinden güvenir (§8.20). (2026-11-13) | Reddedildi: sertifikayı gateway'de (.NET içinde ACME) yönetmek; ek kod, yenileme ve bakım. Bedeli ek bir konteyner. |
| Şampiyon görselleri ve harita | Riot Data Dragon portreleri, betikle bir kez indirilir ve gateway'den sunulur; sayfa altında Riot'un fan projesi notu. (2026-11-12) Maç haritası da aynı yoldan: Summoner's Rift görseli (`map11.png`), kuleler ve çukurlar gerçek oyun koordinatlarından; kule ve nexus ikonları CommunityDragon'dan (2026-11-17, §7). | Reddedildi: tarayıcının doğrudan Riot'tan çekmesi (CSP'ye dış adres, oyuncunun IP'si Riot'a gider, yama sürümü kayar). Kısaltmalı daireler yerine gerçek portre: draft ekranında tanımak çok daha kolay. |
| Süre dolunca seçili şampiyon | Ban/pick adımında süre dolarsa takımın o adımdaki son ön izlemesi kilitlenir (hâlâ uygunsa); yoksa eski kural (boş ban / rastgele pick). "Ban yok" seçiliyken ön izleme boş: süre dolunca boş ban (§8.46). (2026-11-25) | Sebep (kullanıcı): şampiyon seçiliyken "Kilitle"ye basmadan süre dolunca rastgele bir şey geliyordu; LoL'de seçili duran kilitlenir. Ön izleme zaten sunucuya gidiyordu (§8.32), sadece hatırlanmıyordu. |
| "Ban yok" ve boş ban görünümü | Ban adımında havuzun ilk kutusu "Ban yok" (seç, onayla); sonucu süre dolmasıyla aynı (şampiyonsuz ban), kayıtta zaman aşımı değil. Boş geçen ban şeritte ⊘ ile, oynanmamış yuvadan ayrı görünür (§8.33). (2026-11-23) | Sebep: AFK'da süresi dolan ban'lar oynanmamış yuva gibi görünüyordu; ban turu bitmemiş gibi durup pick'ler başlıyordu (kural doğruydu, görüntü yanlıştı). Reddedildi: tek tıkla "Ban yok" (yanlışlıkla geçilmesin, LoL'deki gibi seç + onayla). |
| Draft'ta rakibin ön izlemesi | Sırası gelenin havuzda tıkladığı şampiyon (kilitlemeden) rakibin ekranında, o takımın sıradaki yuvasında yarı saydam görünür; ban ve pick adımında. Hamle değil, durumu değiştirmez; hamleyle aynı kurallarla doğrulanır, sadece rakibe gider. Mesaj limiti ayrı kovadan (§8.32, SISTEM §8.5). (2026-11-23) | Gerçek LoL'de rakip hover'ı görmez; burada turnuva yayınının görünümü seçildi (blöfe de kapı açar). Reddedildi: ön izlemenin hamle kovasını paylaşması (hızlı tıklayan kendi Pick'ini engelletirdi). |
| Görsel tema ve yazı tipi | Koyu lacivert-mor tema, lavanta vurgu, yumuşak köşeli kutular; takım renkleri (mavi/kırmızı) temadan bağımsız. Yazı tipi Outfit, npm paketiyle kendi sunucumuzdan (§8.31). Kullanıcı beğenmezse yazı tipi değişir (tek satır). (2026-11-22) Outfit kullanıcı onaylı (2026-11-24). Renk dağılımı 45/45/10: lacivert + mor zemin, %10 şeftali vurgu sadece ana düğme ve seçili durumlarda (§8.31). (2026-11-23) | Reddedildi: Google Fonts'tan yüklemek (oyuncunun IP'si üçüncü tarafa gider; portrelerdeki kararın aynısı). Eski altın vurgulu gri-koyu tema ("çirkin ama çalışan", 5. adım) yerine. |
| Oyun planı (F1) | Swap'tan sonra 20 sn'lik plan fazı: 6 plan + Dengeli, gizli ve eşzamanlı, seçim tek seferlik, süre dolarsa Dengeli. Rakibin planı maç sonunda karnede. Düğme değerleri motorda tek tabloda (`Tactics.cs`). Uyum hem kazancı hem bedeli ölçekler: uyan kazanç ×1,25 / bedel ×0,75, uymayan tersi. Dengeli × Dengeli maç plansız motorla bit bit aynı (parmak izi testi). Ölçüm: bütün planlar %49-51, uyum Ejder & late ve Erken baskı'da belirgin (~%55 / ~%46) (§4.10, §8.35, DENGE.md). (2026-11-24) | Kullanıcı: rakibin planı maç sonunda görünsün (canlı görünmesi blöfü bozar). Reddedildi: düğmeler `data/tactics.json`'da (neredeyse her plan yeni düğme kodu istiyor; dosya motorun dış yüzünü büyütürdü). Reddedildi: bedel hep tam (tur 1'de uyum ~1 puan, görünmüyordu). Split'in takım savaşı cezası kaldırıldı: split olunca splitter zaten savaşta yok, bedel iki kez kesiliyordu (%45,6). |
| Savaş dizilişi ve orman hareketi | Savaşta (objektif, son savaş, kuşatma, nehir çatışması, istila) iki takım olay noktasının iki yanında, iki sıra: ön sıra top, ormancı, support; arka sıra mid, adc. İkonlar kenar kenara; eksen her yer için yürünebilir alana göre seçilir. Ormancı kamptan kampa gerçek bir program izler (6 kamp, doğma süreleri; başlangıç tarafı ve temizleme hızı şampiyondan). İlk recall 6:00'dan sonra, yaklaşan savaştan önce recall yok (§8.34). (2026-11-24) | Sebep (kullanıcı): ejderde 5 kişi üst üste biniyordu (7 birim arayla tek çizgi), savaşta dağılmıyorlardı; iki ormancı aynı saniyelerde aynı sıradaki kampları dolaşıyordu. Reddedildi: sabit "kendi üssüne doğru" eksen (ejder çukurunun o yanı duvar, takımlar karışıyordu). |
| Gank zamanı ve ormancı istilası | Gank sabit dakikaların 40. saniyesinde değil, pencerelerde (dk 4, 6-7, 9-10, 12-13, 17-18, 23-24) 10-55. saniyede. Her maçın %20'sinde 4:05-4:40'ta ormancı istilası: rakibin yeniden doğan küçük kampı, cevap yoksa çalınır, cevap varsa çatışma; kazanan 3 dk orman temposu alır (§4.5, §8.34). (2026-11-24) | Sebep (kullanıcı): gank'lar saat gibi işliyordu (2:40, 5:40, 8:40…), baskınlar hep aynı görünüyordu. İstila Dengeli'de de %20 (kullanıcı); "Ormancı istilası" planı kesinleştirir (§4.10). Kendi altını yok: çalınan kamp farm, "ormancı geride" orman akışına tempo (farm kuralı altına çevirir). |
| Oyun planının draft'a uyumu | Planın bedeli hep tam; kazancı draft'a uyana ×1,25, nötre ×1, uymayana ×0,75 (§4.10). (2026-11-24) | Kullanıcı: uymayana ×0,5 fazla ezici, simetrik ×0,75 daha adil. Uymayan plan yine net küçük ceza (bedel tam, kazanç eksik); ayrı ceza kuralı gerekmez. |
| Şampiyon havuzu | Aşamalı büyür: 40 → 80 (rol başına 16, faz H) → 125 (rol başına 25, parti 1) → 170 (parti 2: gerçek oyundaki hepsi, Locke, Zaahen ve Yunara hariç; kitleri bilinmeden etiketlenmez). Etiket taslağı Data Dragon / CommunityDragon'dan Claude çıkarır, güç eğrisi veri hattından, onay kullanıcıda; her aşamadan sonra `--check` ve denge ölçümü. K13 kombo tablosu her aşamada yeni ikililerle büyür. (2026-11-17) | Reddedildi: ~170'i bir kerede eklemek (yüzlerce etiket birden ölçülemez, sorunlu şampiyon gürültüde kaybolur). |
| Harita düzeni | Motor her takım için düzen seçer (Koridorlar, Grup, 1-4, 1-3-1), görünmez `layout` olayıyla ekrana bildirir; ekran kendi kuralıyla tahmin etmez. 14. dk'dan sonra 2-3 dk'da bir ve her objektif / kuşatmadan sonra değişir. Arkadan kapatma şimdilik basit (geç katılım, ilk hedef carry, küçük ihtimal katkısı), asıl hâli F3'te. Her alt sistemin kendi zar akışı (§4.1, §4.11). (2026-11-27) | Sebep (kullanıcı): bir süre sonra her olay mid'de dönüyordu, alt koridor boş kalıyordu; kule sonrası ilerleme, arkadan kapatma, orman yakalaması yoktu. Düzen F2 karar anlarının da zemini (ejder takası, split / grupla). Reddedildi: ekranda daha akıllı tahmin kuralı (motor ile ekran yine ayrı dünya kalırdı). |
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
   Süre dolarsa: o adımda seçili duran şampiyon (son ön izleme, LoL'deki gibi; §8.46) ban'lanır / seçilir; seçili şampiyon yoksa ban adımı boş geçer, pick adımında uygun şampiyonlardan rastgele biri seçilir. Swap'ta o anki dizilim kalır. Ban adımı bilerek de boş geçilebilir ("Ban yok", LoL'deki None; §8.33). Sıra sabittir: AFK kalınsa da pick turu ancak ban turu (boş geçenler dahil) bitince gelir.
3. **Oyun planı.** Swap'tan sonra 20 sn: iki oyuncu gizlice ve aynı anda bir plan seçer (§4.10, §8.35). Güç vermez, motorun düğmelerini o takım için çevirir; her kazancın bedeli var, draft'a uyum planı ne kadar iyi uyguladığını belirler. Süre dolarsa Dengeli. Rakibin planı maç sonunda karnede görünür.
4. **Simülasyon.** Sunucu maçı seed ile hesaplar, olay listesini gönderir; istemci 1 dk oyun = 3 sn hızında oynatır (2x, 4x, sona atla).
5. **Maç sonu.** Kazanan, bitiş türü, son savaşın ihtimali, puan dökümü. (İleride: draft kalitesi Monte Carlo, paylaşım kartı.)
6. **Seri.** İlk maçta mavi taraf rastgele belirlenir, sonraki her maçta renkler değişir. Skor renge değil takıma yazılır. Seri devam ediyorsa fearless kilitleri uygulanır.

**Fearless:**
- Seride seçilen şampiyon iki takım için de kilitlenir (soft fearless yok).
- Banlar sonraki maça taşınmaz.
- Havuz serinin en uzun hâline yetmeli (§8.3): havuz 80'e çıktığından beri bo1, bo3 ve bo5 fearless açık (40'lık havuzda bo5 kapalıydı).

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

- MVP **40 şampiyonla** başladı, elle etiketlendi. Şimdi **170**: gerçek oyundaki şampiyonların hepsi, Locke, Zaahen ve Yunara hariç (faz H, parti 1-2, `data/champions.json`; §1 "Şampiyon havuzu"). Koridor sayıları gerçek oyundaki gibi eşit değil: Bot'ta 27 şampiyon oynayabilir, diğerlerinde 38-51.
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
- **Zar akışları** (2026-11-27): her alt sistem (koridor, orman, istila, çatışma, objektif, split, kuşatma, yakalanma, kule, düzen) maç seed'inden türetilmiş kendi mulberry32 akışını kullanır (`Randomness/RandomStreams.cs`). Bir alt sisteme zar eklemek ya da bir karar (§4.10 ②) diğerlerinin zarlarını kaydırmaz: yeni kuralın etkisi aynı seed'le "önce / sonra" ölçülebilir. Eskiden tek akıştı; geçişte her maç değişti (istatistik aynı), parmak izi ve denge temeli yenilendi.
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
1. **Kayıt:** koridor ve orman akışı dakika başında puana yazılır. Diğer işler kendi saniyesiyle zaman çizelgesine kaydolur: sürpriz 5-25. sn, nehir çatışması 15-30. sn, anlatıcı 25-55. sn, objektif 30. sn, gank kendi penceresinde 10-55. sn (aşağıda), istila 4:05-4:40, kuşatma 45. sn.
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
- Gank pencereleri (motor dakikası, dahil): 4, 6-7, 9-10, 12-13, 17-18, 23-24; her pencerede bir gank. Dakika ve saniye (10-55) pencerenin başında zarla seçilir; aynı dakikada objektif varsa saniye 50-55'e sıkışır (ormancı objektif savaşı bitmeden ayrılmasın). İlk pencere 3:10-3:55: ormancı ilk turunu ~3:15'te bitirir. (2026-11-24; eskiden sabit dk 3, 6, 9, 12, 18, 24'ün 40. sn'si: gank saat gibi işliyordu.) Gank'ı atan taraf: `p_Mavi = clamp(1 / (1 + e^(−(G_jg farkı)/4)))`. Koridor rastgele.
- Lane fazında (<14 dk) ve koridor bot değilse support %45 ihtimalle gank'a katılır.
- Başarı: tek başına %60, support'la %75. Başarı → `orman` ±4 (support'la ±5), koridor puanı ±3, kill. Başarısızlık → `orman` ∓2.
- **Ormancı istilası** (2026-11-24, `MatchRun.Invade.cs`): maçların %20'sinde 4:05-4:40'ta (rakibin ilk turda temizlediği küçük kamplar ~2:15 sonra, tam bu aralıkta doğar). İstila eden taraf gank'la aynı eğriyle (güçlü erken ormancı daha olası); hedef rakibin kuşları, kurtları ya da kurbağası. İstilacı ormancı %50 ihtimalle yanına mid ya da support alır. Rakip ormancı müsaitse %60 cevap verir (o da %50 yardımcıyla).
  - Cevap yok: kamp çalınır (istilacı ormancıya 4 CS = 80 altın).
  - Cevap var: nehir çatışmasıyla aynı hesap (bölen 6, K7). Kaybedenden 1-2 kill; istilacılar kazanırsa kampı da alır.
  - Kazanan 3 dk orman akışında ±1 tempo: kaybeden ormancı her farm diliminde %25 kaçırır, kazanan yarısını alır (3 dk'da ~6 CS kayıp, ~3 CS kazanç). Katılan mid/support o sırada koridorunda değil (minyon kaçırır, koridoru kuleye açık).
  - "Ormancı istilası" planı (§4.10) bu ihtimali artırır; Dengeli'de %20.

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
| `invade` | side (istila eden), camp, invaders[], defenders[], stolen, pct?, lead, tail | Ormancı istilası (§4.5); kill'ler ayrı `kill`. Renk kazananın: püskürtülürse savunan |
| `nexus` | side, pct? | Maç sonu |

`text` boş olan olaylar log'a yazılmaz, sadece hareketi yönlendirir.

### 4.10 Oyun planı ve maç içi karar anları (tasarım 2026-11-19; ① oyun planı uygulandı 2026-11-24, ② karar anları sırada)

Oyuncunun maça etkisi draft'la bitmesin: maç öncesi bir **oyun planı**, maç içinde birkaç **karar anı**. Plan, karar anlarının varsayılan cevabıdır (cevap vermeyen, geliştirme botu ve denge aracı planına göre seçer).

```
Draft biter → ① oyun planı (iki oyuncu gizli, eşzamanlı)
   → maç oynar → ② karar anı (maç başı 3-5): oynatma iki tarafta durur, iki taraf gizli seçer (~15 sn),
                    seçmeyene planın varsayılanı → maç kaldığı yerden sürer → … → bitiş
```

**① Oyun planı** (uygulandı, §8.35) motorun ayar düğmelerini o takım için çevirir; her kazancın bir bedeli var. Düğmeler motorda tek tabloda (`Simulation/Tactics.cs`, `PlanKnobs.Of`): neredeyse her yeni plan yeni bir düğme kodu istediği için veri dosyası (`tactics.json`) yerine kod (kullanıcı, 2026-11-24). Değerler nötr uyumdaki hâli (tur 3):

| Plan | Kazanç | Bedel |
|---|---|---|
| Dengeli | — | — (seçmeyen bunu oynar) |
| Ejder & late | Ejder/Elder savaşında d +4,5; 25. dk'dan sonra güç ×1,06 | Herald savaşında d −4,5; 20. dk'ya kadar her koridorda akış −0,225 |
| Erken baskı | 14. dk'ya kadar her koridorda akış +0,15; ek gank pencereleri dk 8 ve 11 (gank'ı bu takım atar); 15. dk'ya kadar nehir çatışmasında d +3 | 20. dk'dan sonra güç ×0,94 |
| Bot baskısı | 14. dk'ya kadar bot akışı +0,25; gank'ların ~%73'ü bot'a, bot'a gank başarısı +%10 | 14. dk'ya kadar top akışı −0,25 |
| Split | Split etiketi 1 olan da gider; rakibin cevap verme ihtimali %50 → %35; splitter düelloda d +2 | Ayrı ceza yok: split olunca splitter objektif savaşında yok (4'e 5) |
| Hızlı bitir | Kuşatma eşiği ×0,7; kuşatmalar arası bekleme 3 → 2 dk | Kuşatmada savunanın avantajı +3; throw'da savunan bir kill fazla |
| Ormancı istilası | 4. dk istilası kesin ve bu takımdan; istila çatışmasında d +3 | Püskürtülürse rakibin orman temposu 1 yerine 2,5 (4 dk) |

- **Draft'a uyum** (kullanıcıyla): planı ne kadar iyi uyguladığın. Uyan draft kazancı ×1,25 alır ve bedeli ×0,75 öder, uymayan kazancı ×0,75 alır ve bedeli ×1,25 öder, nötr ×1. Ayrık kazançlar (zayıf splitçi gider, bekleme 1 dk kısa, istila kesin, throw +1 kill) ölçeklenmez. Ölçütler (`PlanFits`, havuza göre: her plan rastgele draft'ların ~%25-33'üne uyar, ~%20-33'üne uymaz):

  | Plan | Uyar | Uymaz |
  |---|---|---|
  | Ejder & late | Σ(geç − erken) ≥ 7 | ≤ −1 |
  | Erken baskı | Σ(geç − erken) ≤ −1 | ≥ 7 |
  | Bot baskısı | adc + sup erken ortalaması ≥ 7 | ≤ 5,5 |
  | Split | top + mid split toplamı ≥ 3 | 0 |
  | Hızlı bitir | poke ≥ 7 ya da Σ(geç − erken) ≤ −2 | Σ(geç − erken) ≥ 5 ve poke ≤ 4 |
  | Ormancı istilası | ormancı erken ≥ 8 | ≤ 5 |

  Havuz ortalaması Σ(geç − erken) = +2,65 (havuz geç ağırlıklı); eşikler ortaya göre.
- **Dengeli:** düğmelere dokunmaz; Dengeli × Dengeli maç plansız motorla bit bit aynı (parmak izi testi, `BalancedPlanFingerprintTests`): denge ölçümleri geçerli kalır. Planın katkısı sıfırsa hiç eklenmez, plan yoksa ek zar atılmaz.
- **Ölçüm** (DENGE.md, oyun planı turları; 60.000 draft, rastgele plan çifti): bütün planlar bütün planlara karşı %49,1-51,1 (baskın yok, matriste en uç %47,6 / %52,4). Dengeli'ye karşı uyan / uymayan: Ejder & late %55,2 / %47,0, Erken baskı %54,2 / %46,4. Bot baskısı, Hızlı bitir, İstila ve Split'te uyum gürültü içinde: etkileri birkaç ana sıkışmış "olay planları", maç sonucuna etkileri küçük. Split'in asıl yeri belki ② karar anı ("geç oyun: split / grupla").
- **Gizlilik:** iki oyuncu aynı anda seçer, rakip sadece "seçti"yi görür; hangi plan olduğu maç sonunda karnede (replay'de `plans`).
- Taş-kâğıt-makas: karşıtlıklar elle yazılmaz, düğmelerden doğar; denge aracı (`--plans`) matrisi ölçer, **baskın plan olmamalı**.

**② Karar anı** küçük bir iki kişilik oyun; sonuç iki seçimin kesişimi. Örnek, ejder:

| Mavi ↓ / Kırmızı → | Savaş | Bırak, takas yap |
|---|---|---|
| **Savaş** | Takım savaşı | Mavi ejderi alır, Kırmızı karşıda kule alır |
| **Bırak, takas yap** | Tersi | İkisi de bir şey alır, sakin |

- Doğru seçim duruma bağlı (altın farkı, kim hayatta, kim hangi fazda güçlü); karar ekranında gösterilir: şans değil okuma (pokerdeki karar kalitesi).
- Karar tipleri: ejder/Baron (savaş / bırak-takas / beklet), ace sonrası (bitir / reset), kazanılan savaş sonrası (kovala / objektife dön), geç oyun (split / grupla).
- Kural sınırları aynen: ihtimaller %10-90, geride olanın şansı, her olayın motorda karşılığı.

**Mimari:** maç artık tek seferde simüle edilmez. `simulate(draft, seed, planlar, kararlar[]) → olaylar + bekleyen karar`: sunucu ilk cevapsız karar anına kadar simüle eder; seçimler listeye eklenince maç **baştan, aynı seed ve yeni listeyle** yeniden simüle edilir (milisaniyeler; ara durum saklanmaz, aynı girdi aynı maç). Her alt sistemin kendi zar akışı olur: karar, ilgisiz zarları kaydırmaz; **karardan önceki olaylar birebir aynı kalır** (test). Ön yüz karar işaretinde durur, seçimi hub'a gönderir; hızlı izleyen yavaşı bekler; replay'de kararlar açık görünür.

**Hareketle uyum (§5, §8.26):** hareket katmanı olay türlerini bilmez, sadece "şu saniyede şurada ol" hedeflerine yürür ve yalnız bilinen olaylara bakar. Kararlar yeni olay üretir, güzergâh yeniden kurulur. Tek şart: motor kararın sonucunu karar anından en az yürüme süresi kadar sonraya koyar (ör. karar 4:30, ejder savaşı 5:30); karar anında şampiyonlar yerinde durur, seçimden sonra yürür.

**Sıra:** F1 ayar düğmesi altyapısı + oyun planı (mimari değişmez) → harita düzeni (§4.11; karar anlarının çoğu bir düzen seçimi) → F2 karar anı altyapısı + tek karar tipi (ejder) → F3 diğer karar tipleri + mini savaş simülasyonu (savaşın içinde kim girdi, kim kime odaklandı, kim önce düştü; sonuç ihtimali gerçek timeline verisine oturtulur).

### 4.11 Harita düzeni (tasarım 2026-11-27, kullanıcıyla; uygulama sırada)

**Sorun:** kimin nerede durduğunu motor söylemiyordu, ekran sabit kuralla tahmin ediyordu (§5.1: 14. dk'dan sonra bot ikilisi hep mid'de, 25'ten sonra top yanda). Alt koridor boşalıyor, her olay mid'de dönüyordu. Motor ise adc'yi 30. dakikada da bot koridorunda sayıyordu (kule fırsatı): ekran ile motor iki ayrı dünyaydı.

**Karar:** motor her takım için bir **düzen** seçer (kim hangi koridorda) ve görünmez `layout` olayıyla bildirir; ekran tahmin etmez, uygular. Motordaki "koridorda mı" sorusu da düzene bakar.

| Düzen | Kim nerede | Kazanç | Bedel |
|---|---|---|---|
| Koridorlar | Standart (14. dk'ya kadar herkes) | Farm, dalgalar dengede | — |
| Grup | 5 kişi tek koridorda | O koridorda kule hızlı düşer | Diğer koridorlarda dalga birikir, kendi kulen yalnız |
| 1-4 | Bir splitçi yan koridorda, 4 kişi grup | Yan koridor baskısı | Savaşta 4'e 5 |
| 1-3-1 | İki yan koridor, 3 kişi mid | Haritanın her yerinde baskı | Savaş 3'e 5 başlar; yanlardakiler arkadan kapatır |

- **Sıklık:** 14. dakikadan sonra 2-3 dakikada bir, ayrıca her objektif ve kuşatmadan sonra (takımlar sürekli koşturmasın). Seçimi varsayılan olarak motor yapar, eğilimi plandan gelir (Split → 1-4 / 1-3-1, Hızlı bitir → Grup); F2'de bazı anlarda oyuncuya sorulur (ejder: savaş = grup çukura, takas = grup karşı koridora; geç oyun: split / grupla).
- **Kule sonrası ilerleme:** koridordaki kişi farkı itiş baskısıdır; kule düşünce ön hat ilerler, sıradaki kule hedef olur. Kuşatma bir koridordan gider (`siege` olayına koridor).
- **Arkadan kapatma** (basit hâli; asıl hâli F3 mini savaşta veriyle): yan koridordaki oyuncu savaşa birkaç saniye geç ama rakibin arkasından katılır; ilk hedefi rakibin carry'si, savaş ihtimaline küçük katkı.
- **Orman yakalaması:** yan koridordan dönen ya da rakip ormanında dolaşan konumlu hedef olur; yakalanma (§16.9) "nerede" bilgisini taşır. Takım arkadaşları yakındaysa cevap gelir, ormanda küçük çatışma çıkar.
- **Uygulama sırası:** (A) zar akışları (§4.1) → (B) düzen motorda + `layout` olayı, ekran okur, kuşatmaya koridor → (C) kişi sayısı → kule baskısı ve ilerleme → (D) orman yakalaması, ormanda çatışma, arkadan kapatma → F2.

---

## 5. Hareket sistemi

### 5.1 Şu anki kurallar (prototip v2)
Bu kurallar **hedefi** verir; şampiyon hedefine haritada yürür (2026-11-19, §8.26): duvardan geçmeyen yol (görselden çıkan ızgara, A*; koridorda koridorun orta çizgisi), ~345 birim/sn; olaya zamanında varmak için erken yola çıkar, yetişemezse en fazla 2 kat hızlanır. Ölen öldüğü yerde silik kalır, üste doğar; recall 8 sn kanal + üsse ışınlanma; ikisinden sonra yürüyerek döner.

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

İkonlar hedefe yürür (bkz. bölüm başı, §8.26); eskiden anında hedefe atlayıp 0,3 sn CSS geçişiyle yumuşuyordu (kavgalara ışınlanma).

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

- **Harita (2026-11-17'den beri gerçek harita, §8.25):** Summoner's Rift görseli (Data Dragon `map11.png`, portrelerle aynı yoldan indirilir ve kendi sunucumuzdan sunulur). 600×600 koordinat; mavi üs sol alt (72, 528), kırmızı sağ üst (528, 72). Riot koordinatı → harita: `riot(x, y)` (`web/src/match/geometry.ts`; görsel x -120…14870, y -120…14980 kapsar).
  - Kuleler gerçek yerlerinde: 1000 maçlık timeline'daki yıkılma konumları (her kule her maçta aynı nokta). Koridor yolları kulelerin içinden geçer; top/bot köşeden döner.
  - Kule, inhibitör ve nexus oyunun minimap ikonlarıyla (CommunityDragon, yama klasörüne sabit), takım rengine boyanır, koridor şeridinin ortasında; yıkılan silik. İnhibitör üs kulesinin arkasında; motorda üs kulesiyle tek kademe (tier 2), birlikte düşer.
  - Ejder çukuru (399, 419), Baron/Herald (203, 182): canavar kesilme konumlarının medyanı.
  - Eskiden kendi çizimimizdi (düz zemin, çapraz nehir, köşeli koridorlar).
- **İkonlar:** Riot Data Dragon portreleri, takım rengi çerçeveyle; bir kez indirilip kendi sunucumuzdan sunulur (`web/public/champions`, §8.17). Ölü ikon %30 opak, öldüğü yerde; doğunca üste belirir, yürüyerek döner (§8.26).
- **Draft ekranı (2026-11-19, §8.27):** turnuva yayını görünümü. Üstte takım adları, evre, büyük sayaç ve sıradakinin renginde boşalan süre çubuğu; altında ban şeridi (mavinin ban'ları solda, kırmızınınki sağda; gri + çapraz çizgi). İki yanda 5 pick kartı (Data Dragon yatay splash, karakter bütün; rol ikonu, ad); sıradaki yuva parlar, seçim onaylanmadan kendi yuvada yarı saydam ön izlenir (sadece kendi ekranında). Ortada havuz: rol ikonlu filtreler, fearless kilidi rozeti.
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
| Maç içi karar anları | Tasarlandı (2026-11-19): §4.10 (oyun planı + karar anları, parça parça simülasyon). |
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
