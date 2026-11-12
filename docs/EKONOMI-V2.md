# Draft ve ekonomi v2 (§16)

Tasarım dokümanının parçası; harita ve ilkeler: [DESIGN.md](DESIGN.md). Bölüm numaraları (§) bütün dosyalarda ortaktır. Uygulama sırası ve ölçümler (§16.13): [DENGE.md](DENGE.md).

---

## 16. Draft ve ekonomi v2 (onaylandı — uygulanıyor; sayılar faz D'de ayarlanacak)

> Durum: onaylandı, uygulanıyor. §3 (şampiyon modeli) ve §4.2-4.6 (puan, güç, sinerji, savaş, kuşatma) v1'i anlatır; çeliştikleri yerde bu bölüm geçerlidir. Tüm sayılar ayar düğmesidir; denge aracıyla (§9) ölçülüp oturtulur.

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

**Rol dışı: ceza + kumar (karar 2026-11-10, yeniden düşünüldü ve uygulandı 2026-11-12; `Engine/Simulation/OffRole.cs`):**
- Düz −%25 yerine **rol mesafesi tablosu** (simetrik): top↔mid, mid↔bot %20 · orman %25 · mid↔sup, bot↔sup %30 · support ↔ top/orman, bot ↔ top/orman %35. Çok rollü şampiyon en yakın rolünü sayar. Bilinen flex'ler (Jayce top) `roles`'ta: cezasız. Ceza G_pot'a çarpan: `G_eff = G_pot × (1 − ceza) × altın çarpanı`.
- Eşleşme avantajı ayrı (kurallardan): ranged büyücü top'ta melee'ye karşı K1 (+0,3/dk) alır, ceza onun bedelidir. Örnek: Syndra (7-8-7) top'ta %20 cezayla Darius'a (8-7-5) karşı koridorda hâlâ hafif önde (5. dk Δ +0,06/dk), geç oyunda güçlü. Ortalama bonus bilerek yok (olsaydı rol dışı counterpick optimal olurdu).
- **Kumar (oynaklık):** lane fazında (dk < 14) rol dışı oyuncunun koridorunda (ve ormanda) gürültü × 1,5. Amaç "ya ezer ya ezilir" hikâyesi. Karnede satır (`OffRoleReport`): "Rol dışı kumar tuttu / tutmadı / başa baş" (maç sonu rol rakibine göre ±1.000 altın).
- **Ölçüm (2026-11-12, 10.000 draft, her yuva %10 rol dışı, `--off-role 0.1`):** rol dışı oynayan takım %43,2 kazanıyor (kendi rolünde %50,8). Maç sonu rol rakibinin 1.000+ önünde: rol dışı %18,9, kendi rolünde %29,7; 1.000+ gerisinde: %38,9 / %27,4. **Açık sorun:** oynaklık neredeyse etkisiz; ×2 yapınca "önde" %19,0 (lane gürültüsü sadece CS'yi oynatıyor, maç sonu altınını kill/objektif/savaş belirliyor). Cezayı 5 puan indirmek kazanmayı %44,6'ya çıkarıyor, "önde"yi %20,1'e. Kumarın gerçekten tutabilmesi için daha güçlü bir kaldıraç gerekiyor (faz D; öneri: rol dışı koridorda sürpriz sıklığı, ortalama-nötr kill'ler).
- Eski not (2026-11-10):
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

### 16.14 Kullanıcıya açık sorular
1. ~~Sınıf listesi yeterli mi?~~ Evet, şimdilik bu 6'sı; gerekirse sonra eklenir.
2. ~~`cc` ve `waveclear` ayrı etiket olsun mu?~~ Hayır, `pick`/`en` ve sınıf yeterli.
3. K1-K13'te oyun bilgisine göre yanlış ya da eksik olan var mı? Yönler ve büyüklükler (ör. range baskısı +0,3) mantıklı mı?
4. Yakalanma (16.9) ve throw'un ödülle kapanması: geri dönüş sıklığı hedefi ne olsun? Öneri: lane'de belirgin geride kalan %25-35 kurtarır.
5. Oyuncu ne görsün? Öneri: draft sırasında kimlik çubuğu (hangi kimliğin neyi yendiği gizli), maç sonunda tam karne.
