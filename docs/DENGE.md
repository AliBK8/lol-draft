# Denge ve ölçümler (§9, §16.13)

Tasarım dokümanının parçası; harita ve ilkeler: [DESIGN.md](DESIGN.md). Bölüm numaraları (§) bütün dosyalarda ortaktır. Ekonomi v2 kuralları: [EKONOMI-V2.md](EKONOMI-V2.md).

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
- Havuz §16.2 biçiminde JSON (`roles`: top, jg, mid, adc, sup); varsayılan `data/champions.json`: oyunun 40 şampiyonu (faz C), oyunla aynı okuyucu (`ChampionFile`). Eski `sample-champions.json` (prototipin 10'u + 10 arketip) faz C'de kaldırıldı; faz B ölçümleri onunla.
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
12. ~~40 şampiyon etiketleme~~ (bitti: `data/champions.json`, eskiden `tools/LolDraft.Balance/` altında; taslağı Claude çıkardı, kullanıcı onayladı. Her koridordan 8, her koridorda farklı tipler; 6 hiper taşıyıcı: Kayle, Karthus, Kassadin, Viktor, Jinx, Vayne. Bilinçli seçimler: Ashe'nin oku `en` değil `pick` 3 (engage tank/destek işi, adc'ye `en` K5'i bozar); Gnar ve Jayce koridorda ranged. K13 kombo tablosu yeni listeye göre: Kayle/Jinx/Vayne + Lulu, Leona + Draven, Yasuo + Malphite/Alistar, Orianna + Amumu.) + ~~tutarlılık kontrol aracı~~ (bitti: `dotnet run --project tools/LolDraft.Balance -- --check [--champions havuz.json]`, `PoolCheck.cs`). Yazdıkları: rol başına sayı (öneri ≥ 6), sınıf dağılımı, etiket ortalamaları, hiper taşıyıcılar ve uyarılar: hiper taşıyıcıya benzeyip econ < 3 · econ 3 ama geç < 8 · tank ama fl < 2 ya da econ ≥ 2 · enchanter ama peel + dis < 2 · suikastçı ama mob < 2 (K3 çalışmaz) · melee nişancı (§16.2 koridor menzili) · split ≥ 2 ama top/mid değil (K12) · adc ama en ≥ 2 · güç toplamı 14-22 dışı · hiç etiket yok. Uyarı hata değildir (Kayle bilinçli istisna). Örnek havuzda: tek uyarı Kayle; dis ortalaması 0,35, peel 0,55 (koruma kıt: Poke/Engage sonuçlarını etkiliyor olabilir, 40'lık listede dengeli olsun).

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
   - **Ayar turu 4** (40'lık havuz, 10.000 draft, seed 1; başlangıç tur 2 C):

     | # | Değişiklik | Neden | Sonuç |
     |---|---|---|---|
     | 1 | K8 iki yönlü: etkin dive = `dive` − rakip `peel`, fark × 0,5 (±3); hiper taşıyıcı ve `fl` şartı kalktı | Dive %44: K8 maçların ~%6'sında çalışıyordu; tank dive'ı durdurmaz, peel durdurur | Dive %44,0 → %49,5 ✓ · Engage %57,0 → %58,2 · Poke %46,2 → %44,6 · K8 objektif savaşı olan maçların %84'ünde (ort. 1,8 puan, lehine olan %55) · scale comp kurtarıyor %36,9 · Mavi %49,8, süre 29,7 |

     Karar (kullanıcı): K8 kalıyor; "her maçta küçük etki" (küçük farklara eşik) sonraki değişikliklere göre yeniden bakılacak.

     **Poke teşhisi** (hepsi denendi, geri alındı; başlangıç: Poke %44,6, Brand %40,7, Engage vs Poke %66,8):

     | Deney | Poke | Brand | Engage vs Poke |
     |---|---|---|---|
     | E1: K6 objektifte "poke kurdu" 0,5 → 1,0, tavan 2 → 3 | %46,0 | %41,3 | %65,8 |
     | E2: Brand `pick` 1 → 2 (veri) | %44,9 | %40,7 | %65,1 |
     | E3: E1 + E2 | %46,3 | %41,5 | %64,5 |
     | E4: K6 objektif kuralı son savaşta da | %45,1 | %41,0 | %67,7 |
     | E5: Brand `m` 7 → 8 (güç) | %44,9 | %41,6 | %66,2 |

     Okuma: ne K6'nın ağırlığı ne Brand'in gücü düğme. Brand'in sorunu getirdiği değil **getirmediği**: etiket toplamı 4 (poke 3, pick 1); destek koltuğundaki diğerleri 7-13 (Leona/Alistar `fl 3 en 3`, Lulu/Janna `dis/peel 3`). Brand'li takım K10'a (frontline), K5'e (engage) ve K8'den beri rakibin dive'ına açık. Kuşatmada poke'u fazla olan +4,3 puan alıp yine %46,9 kazanıyor: poke draft'ı objektifleri kaybedip savunmaya düşüyor. Kural ayarının tavanı ~%46.

     | # | Değişiklik | Neden | Sonuç |
     |---|---|---|---|
     | 2 | **K6 poke itişi** (kullanıcı kararı): dk ≥ 14, koridor anında normal fırsat yoksa poke'u fazla olan takım kuleyi savunan koridordayken de alır, %1 × poke farkı (≤ %5). Kule = fırsat kuralının tek istisnası (`MatchRun.Towers.cs` `TryPokePush`) | Poke'un gerçek gücü kule kırmak; frontline'sız kazanma yolu kule → kuşatma (orada K6 +4) | Poke %44,6 → **%48,4** · Engage vs Poke %66,8 → %61,4 (karşı hâlâ çalışıyor) · Poke vs diğerleri %47,5-52 · maç başı kule 5,3 → 6,6 · erken comp %46,3 → %47,1, 35'ten önce %29,5 → %32,2 ✓ · scale comp kurtarıyor %35,5 · Engage %57,3 · Dive %47,1 · Mavi %50,3 · süre 29,2. Şampiyonlar: Caitlyn %51,9, Karma %51,1, Ezreal %50,9, Jayce %46,9, Karthus %47,3, **Brand %41,8** |
     | 2b | ~~%1,5 × fark, ≤ %7~~ (geri alındı) | daha güçlüsü | Poke %49,0, kule 6,9: doyuyor, değmez |

     Açık: Brand %41,8 kural değil koltuk sorunu (destekte boş etiket); veride çözülecek (ayrı karar). Dive %49,5 → %47,1 (gürültü sınırında, izlenecek).

     **Kural toplamına tavan** (adım 3): takım savaşında iki tarafın kural etkilerinin net toplamı ±T (`TeamRules.CappedTotal`; çatışmada sadece K7 ±3, tavan değmez). Karnedeki kural etkisi "bu kural olmasa" ihtimaline tavanla bakar (tavana takılmış savaşta fazlalık kural ~0). Rapordaki "kural farkı" ekseni tavansız ham draft farkı (son savaş, dk 35).

     | Kural farkı | Tavansız | T = 10 | **T = 8 (seçildi)** | T = 6 |
     |---|---|---|---|---|
     | 4 | %57,5 | %57,5 | %57,5 | %57,2 |
     | 5-6 | %60,3 / %61,6 | %60,3 / %61,5 | %60,4 / %61,4 | %59,6 / %60,8 |
     | 7-8 | %64,4 / %65,1 | %64,0 / %64,7 | %63,3 / %63,6 | %61,5 / %61,0 |
     | 9-10 | %67,4 / %70,0 | %67,4 / %68,6 | %65,6 / %66,4 | %59,8 / %61,2 |
     | 12 / 14 | %79,1 / %78,8 | %75,0 / %76,9 | %71,5 / %73,1 | %66,3 / %67,3 |
     | Engage / Poke | %57,3 / %48,4 | %57,0 / %48,5 | %56,5 / %48,8 | %55,5 / %49,1 |
     | Brand | %41,8 | %42,1 | %42,6 | %43,6 |

     T = 8: eğri tekdüze kalıyor, fark 12+ ~%71-73 (önce %79-82; 12+ maçların ~%3'ü, hata payı ±6). T = 6 eğriyi 6'dan sonra %61'de düzleştiriyor: "çok daha iyi draft" ödüllenmiyor. Tavansız (T = 99) önceki ölçümü birebir verdi: tavan başka hiçbir şeyi değiştirmiyor. Diğerleri değişmedi: Mavi %50,4, süre 29,2, erken comp %47,1 / %32,2, scale comp kurtarıyor %35,6, Engage vs Poke %60,4.

     **Rol dışı kumarın kaldıracı** (adım 4; `--off-role 0.1`, hepsi denendi, geri alındı). Sütunlar: rol dışı oyuncunun takımı kazanıyor · maç sonu rol rakibinin 1.000+ önünde ("tuttu") · 1.000+ gerisinde. Kendi rolünde: %50,9 · %29,9 · %27,6.

     | Deney | Kazanma | Tuttu | Tutmadı |
     |---|---|---|---|
     | Bugün (ceza %20-35, lane fazı gürültü × 1,5) | %42,2 | %19,3 | %39,4 |
     | Rol dışı koridorda sürpriz ihtimali × 2 / × 3 / × 4 (yazı-tura kill) | %42,2-42,3 | %19,4-19,6 | %39 |
     | "Rolü kaptı": 14. dk rol rakibinden öndeyse ceza kalkar | %44,5 | %22,6 | %37,0 |
     | Aynısı, ceza yarıya iner / eşik 500 altın | %43,1 / %43,3 | %21,0 / %20,9 | %38 |
     | Ceza tablosu × 0,75 (%15-26) | %43,9 | %21,1 | %36,7 |
     | Ceza tablosu × 0,5 (%10-18) | %46,0 | %23,0 | %34,0 |

     Okuma: sürpriz lane fazında rol dışı koridorda ~0,2 olay (× 4 ile ~1); tek kill maç boyu süren cezayı oynatmıyor. "Rolü kapma" da az tetikleniyor (cezayla 14. dk'da önde olmak nadir). Cezayı yarıya indirmek bile "tuttu"yu %23'e taşıyor: **kumar yapısal olarak ~beşte bir tutuyor; oynaklık düğmesi yok, tek gerçek düğme bedel (kazanma oranı)**.

     Karar (kullanıcı): bedel sertleşsin ("support top oynayan takım %40 kazanmamalı"). Ceza dilimine göre takım kazanıyor:

     | Tablo (yakın / orman / geçiş / uzak) | Yakın | Orman | Geçiş | Uzak | Ortalama · tuttu |
     |---|---|---|---|---|---|
     | Eski %20 / %25 / %30 / %35 | %45,7 | %42,8 | %42,0 | %40,5 | %42,2 · %19,3 |
     | %25 / %30 / %40 / %45 | %44,1 | %41,0 | %39,7 | %39,4 | %40,7 · %17,3 |
     | %22 / %30 / %40 / %50 | %45,0 | %41,3 | %39,9 | %37,4 | %40,1 · %17,4 |
     | **%22 / %30 / %45 / %60 (seçildi)** | %45,4 | %42,3 | %39,0 | **%35,6** | %39,5 · %16,6 |
     | %25 / %35 / %50 / %65 | %44,3 | %42,1 | %38,6 | %34,9 | %38,8 · %15,9 |

     Ceza büyüdükçe etkisi azalıyor (bir oyuncu takım gücünün beşte biri, altın çarpanı tabanı 0,5): uzak rol %35 → %60 sadece 5 puan. Seçilen tablo yakın rolü neredeyse aynı bırakıyor (makul esneklik: Syndra top), uzak rolü belirgin cezalandırıyor (Leona top). Rol dışısız ölçümler değişmez (ceza sadece rol dışında).

     **Brand** (adım 5, veri; geçici havuz dosyasıyla ölçüldü): poke 3, pick 1 → %42,6 · + pick 2, peel 1 → %46,2 · + pick 2, peel 2 → %50,0 · + pick 2, dis 1, peel 1 → %47,8. Peel işe yarıyor (K5 engage'ini, K8 dive'ını siler) ama Brand peel şampiyonu değil. Brand'in asıl değeri takım savaşında AoE hasar (yanma, seken ulti); etiket setinde karşılığı yok, sadece e/m/l'de (`m` 7 → 8 etkisiz, E5). Karar (kullanıcı): sadece gerçekçi olan **pick 2** (yanan hedefe stun) → Brand %43,2 (gürültü sınırında); asıl çözüm ileride `aoe` etiketi (takım savaşı hasarı / toplu CC: Brand, Orianna, Karthus, Malphite, Amumu, Yasuo...; şimdi K13 kombo tablosu kısmen karşılıyor). Diğerleri: Poke %49,2, Engage %56,7, Dive %46,6, Mavi %50,4.

     **Tur 4 sonu.** Dive %46,6 kabul (kullanıcı): kimlikler %46-52 bandında (Split %47, Pick %49, Poke %49), taş-kağıt-makas çalışıyor (Koruma > Dive %55, Dive > Split %53). Gerçek "başarılı dive oranı" ölçülemiyor (Riot verisi comp ya da dive olayı işaretlemiyor; solo kuyruk referans değil) ve gerçekte çok değişkene bağlı (kaç kişi dalıyor, tank hasarı emiyor mu). K8 bilerek basit: dalış toplamı − rakip peel; tankın hasarı emmesi K8'de yok, frontline K10'da. Açık kalan: Brand (→ `aoe` etiketi).
   - **Ayar turu 5: `aoe` etiketi ve K14 toplu hasar** (EKONOMI-V2 §16.2, §16.8). Tanım kullanıcıyla: hasar, CC değil (toplu CC `en`'de; yoksa engage tank'lar iki kez ödüllenir). Tank'lar hasar yapmaz (kullanıcı): Malphite 0, Amumu 1. 18 şampiyon: aoe 3 Brand, Karthus · 2 Orianna, Viktor, Jinx, Kayle · 1 Darius, Gnar, Amumu, Sejuani, Graves, Syndra, Yasuo, Jayce, Varus, Ezreal, Ashe, Karma. Havuz ortalaması 0,65.

     | Ayar | Brand | Dive | Erken comp | Orianna | Poke |
     |---|---|---|---|---|---|
     | K14 yok (`0bec59f`) | %43,2 | %46,6 | %47,1 | ~%55,5 | %49,2 |
     | 0,5 × fark (±3), Orianna 3 | %46,3 | %42,4 | %44,6 | %59,9 | %51,7 |
     | aynısı, wombo × 1,0 (yok) | %46,5 | %43,1 | %44,3 | %59,3 | %51,7 |
     | 0,75 × fark (±3), Orianna 3 | %47,3 | %41,1 | %43,9 | %60,9 | %52,2 |
     | 0,5 × fark (±3), Orianna 2 | %46,4 | %42,6 | %44,6 | %58,5 | %51,7 |
     | 0,35 × fark (±2), Orianna 2 | %45,2 | %43,6 | %44,9 | %58,0 | %51,0 |
     | **0,25 × fark (±2), Orianna 2 (seçildi)** | **%44,8** | %44,4 | %45,2 | %57,7 | %50,5 |

     Okuma: K14 takım savaşı değerini toplu hasarcılara verir; onlar çoğunlukla geç oyun büyücü/nişancıları (Orianna, Viktor, Jinx, Karthus, Kayle). Kaybeden: aoe'si doğru olarak 0 olan Dive (suikastçılar tek hedef) ve erken comp. Brand'e kazandırılan her puan Dive/erken comp'tan ~bir puan götürüyor; ağırlık takası yumuşatıyor, kaldırmıyor. Wombo çarpanı ölçümde neredeyse etkisiz ama kavram olarak kaldı (Brand'i engage'li takımda değerli kılar, karnede "Wombo" metni). Karar (kullanıcı): yumuşak ayar. Diğerleri: Mavi %50,4, süre 29,3, Engage %56,9, Koruma %52,1, scale comp kurtarıyor %36,7, erken comp 35'ten önce %31,0 ✓, kural farkı 10 %67, 12 %65.
     Açık: Orianna %57,7 (aoe değil: her etiketten biraz alıyor, en 2 · peel 2 · poke 2 + K13 kombo; veri meselesi). Dive %44,4 ve erken comp %45,2 bandın alt sınırında.

**Faz H — Havuz genişletme (40 → 80)**
- H1: `data-pipeline/06_champions.py` (Data Dragon 173 şampiyon + 500 maçtan rol dağılımı → `out/champion_candidates.json`). H2: 40 yeni şampiyon, rol başına 8, taslak Claude, onay kullanıcı (Aatrox ve Rengar geç gücü 5 → 6). Havuz kontrolü temiz; dis 0,45 → 0,36, peel 0,60 → 0,49 (koruma biraz daha kıt).
- **H3 denge, ilk ölçüm (80'lik havuz, 10.000 draft):** Mavi %50,0, süre 29,3, kill 24,6. Kimlikler: Engage %57,9 · Poke %48,7 · **Pick %44,8** (40'lıkta %48) · Koruma %54,8 · Dive %49,0 · Split %46,6. Erken comp %44,3, 35'ten önce %27,8 (hedef 30'un altı). Kural farkı 10 %70,6, 12 %74,4. Şampiyon başına ~2.500 maç, hata payı ±2. Dışarıda: Orianna %58,6 · Pyke %37,9 · Blitzcrank %41,4; sınırda Garen %43,1, Draven %43,4, TF %43,9, Elise %44,1 (hepsi pick ya da erken baskı).
- **Teşhis:** pick'in değeri (K7 çatışma, gank, yakalanma) objektife taşınmıyordu; K7 çatışmada lehine olan %46,8 kazanıyordu. İki engel: (1) takım savaşında ölüler de sayılıyordu (güç ve etiket), (2) yakalanma objektiften ≥ 50 sn önce, respawn 20-45 sn: yakalanan savaşa yetişiyordu.

  | Adım | Pick | Engage vs Pick / Pick vs Koruma | Erken comp / 35'ten önce | Kural farkı 10 / 12 | Pyke / Blitzcrank |
  |---|---|---|---|---|---|
  | İlk ölçüm | %44,8 | %64,8 / %39,5 | %44,3 / %27,8 | %70,6 / %74,4 | %37,9 / %41,4 |
  | (1) savaşa sadece yaşayanlar girer (kullanıcı: "kesinlikle düzeltilmeli") | %45,5 | — / %40,6 | %45,0 / %29,3 | — | %39,3 / — |
  | **(1) + (2) objektif öncesi yakalama** (8-25 sn önce rastgele, %4 × pick farkı ≤ %25; §16.9) | **%50,0** | %54,0 / %46,2 | **%48,0 / %32,9** ✓ | %64,8 / %65,1 | %41,5 / %43,0 |

  Sonrası diğerleri: Mavi %49,9, süre 29,2, kill 25,1, Engage %56,0, Koruma %52,7, Poke %49,1, Dive %49,0, scale comp kurtarıyor %33,3 ✓, K7 çatışmada lehine olan %50,6 (artık nötr). Şampiyonlar: üstte Thresh %58,8 (pick 3 + fl/en), Orianna %56,8, Galio %55,4; altta Pyke %41,5, Blitzcrank %43,0, Garen %43,5, Draven %43,6.
- **H3 veri** (20.000 draft, şampiyon başına ±1,4): Thresh fl 2 → 1 ve sınıf enchanter/tank (ranged fener desteği, ön saf tank değil) %59,3 → %57,5 · Blitzcrank en 1 → 2 (kanca savaşı açar) %43,4 → %45,6 · Garen geç 5 → 6 %44,3 → %45,0. Denendi, etkisiz: Pyke en 2 + mob 3 (%41,3 → %41,9), Draven orta 6 → 7 (%43,3 → %43,7). Karar (kullanıcı): Pyke (~%41) ve Draven (~%43) olduğu gibi: ikisi de kartopu yapamazsa düşen şampiyon (Pyke destek koltuğunda fl/peel 0 ve geç 4; Draven 9-6-5, takım etiketi yok), gerçek karakterleri bu. Son ölçüm: Mavi %49,7, süre 29,2, kill 25,1, Engage %55,2, Poke %49,0, Pick %50,5, Koruma %53,3, Dive %49,0, Split %45,7, erken comp %48,5 / %32,5 ✓, scale comp kurtarıyor %32,1 ✓, kural farkı 10 %65,4, 12 %68,7. H3 bitti.
- **H4 K13 kombo tablosu 7 → 15** (20.000 draft, aynı seed, önce/sonra): Xayah + Rakan, Yasuo + Yone, Yasuo + Wukong, Miss Fortune + Amumu, Kai'Sa + Nautilus, Lucian + Braum, Pyke + Draven, Kalista + Blitzcrank (taslak Claude, onay kullanıcı). Bilinçli dışarıda: Thresh (%57,5) ve Orianna (~%56) yeni kombo almadı; Yasuo'ya daha fazla knock-up ortağı (Jarvan, Rell, Rakan, Sejuani) eklenmedi, 4'te kaldı. "Bir şampiyon tek komboya girer" kuralı yok, ±8 tavan yeterli sayıldı. Sonuç: K13 objektiflerde %5,3 → %11,2 maçta, kombolu taraf %57,4 → %54,7 kazanıyor (kombo sıklaşınca tek başına ağırlığı düştü). Üyeler gürültü içinde kıpırdadı (+0,1…+0,5): Yasuo %51,1 → %51,6, Nautilus %53,6 → %54,0, Pyke %40,6 → %41,1, Draven %43,2 → %43,6. Genel değişmedi: Mavi %49,7, süre 29,2, erken comp %48,7 / %32,6 ✓, scale comp kurtarıyor %32,0 ✓. Rastgele ayna draft'ta ikilinin aynı takıma düşmesi seyrek; asıl etki oyuncu bilerek kombo seçince, o da +2 ile sınırlı.

**Faz H parti 1 — Havuz 80 → 125 (rol başına 9 yeni)**
- Seçim: aday listesinde havuz dışı 93 şampiyon; en çok oynananlar, koridorda tip çeşitliliği ve koruma açığı (H2'den beri `peel`/`dis` kıt → Nami, Soraka, Milio, Taric, Zilean). 500 maçlık örneklemin rol bilgisi gürültülü (Quinn ormanda, Lux nişancıda), roller oyun bilgisiyle. Ertelendi (parti 2): Locke, Zaahen, Yunara (kitleri yeterince bilinmiyor, etiket tahmin olur), Ambessa, Pantheon, Zoe, Hwei, Aurora, Ryze, Rek'Sai…
  - top: Shen, Gangplank, Riven, Sett, Mordekaiser, Dr. Mundo, Vladimir, Tryndamere, Teemo · jg: Ekko, Diana, Nocturne, Kindred, Kayn, Nunu & Willump, Master Yi, Zac, Gragas · mid: Katarina, Fizz, Lissandra, Azir, Veigar, Anivia, Annie, Talon, Aurelion Sol · adc: Zeri, Kog'Maw, Twitch, Samira, Smolder, Ziggs, Corki, Nilah, Swain · sup: Nami, Soraka, Milio, Taric, Morgana, Zilean, Lux, Seraphine, Tahm Kench.
  - Bilinçli: Nilah melee nişancı (`--check` uyarısı, Kayle gibi); Gangplank ve Tryndamere `econ` 3 ama dövüşçü (hiper taşıyıcı sayılmaz). Hiper taşıyıcı 9 → 16 (Vladimir, Azir, Veigar, Aurelion Sol, Zeri, Kog'Maw, Twitch, Smolder).
- Taslak ölçümle düzeltildi (geçici havuz dosyası, 30.000 draft, şampiyon başına ±1,4-1,6): Lissandra `en` 3 → 2 (%56,1 → %53,5; engage'i tek hedefli ulti + pençe, toplu değil) · Vladimir `aoe` 2 → 3 (%44,8 → %45,6; hourglass + flash + ulti savaşı tek başına çevirir) · Twitch `mob` 1 → 2 (`--check`: suikastçı ama mob 1 → K3 hiç çalışmaz; görünmezlik + hız). Kullanıcı: Smolder 3-7-10 → 4-7-9, Aurelion Sol ve Kog'Maw geç 10 → 9. K13 +2 ikili: Lucian + Nami, Kog'Maw + Lulu (önerilen Samira + Rell ve Master Yi + Taric kullanıcı kararıyla çıkarıldı).
- **Son ölçüm (125'lik havuz, 30.000 draft):** Mavi %50,0, süre 29,2, kill 25,0. Kimlikler: Engage %54,3 · Poke %49,5 · Pick %49,8 · Koruma %51,7 · Dive %49,7 · Split %47,0 (80'liğe göre daha sıkı: Engage %55,2, Koruma %53,3 idi). Erken comp %49,5 / 35'ten önce %33,3 ✓, scale comp kurtarıyor %33,4 ✓, kural farkı 10 %63,2, 12 %67,6. Yeni 45'in hepsi taslak ölçümünde bantta; son ölçümde kenarda Lissandra %55,1 ve Teemo %44,8 (taslakta %53,5 / %46,9: havuz değişince gürültü içinde oynadılar). Eskiler: Orianna %56,8, Alistar %54,7, Thresh %54,6, Ornn %53,8, Karthus %44,9, Blitzcrank %44,5, Draven %42,7, Pyke %40,9. Taslakta Alistar %57 / Ornn %55,5 görünmüştü (yeni gelenlerin çoğu `fl` 0 → `fl` 3 + `en` 3 tank değerlendi); son ölçümde kenara döndüler. İzleme: Orianna, Lissandra, Teemo; kalan ~45 şampiyondan sonra yeniden.

**Faz H parti 2 — Havuz 125 → 170 (kalan hepsi)**
- Seçim: havuz dışındaki 48 adaydan Locke, Zaahen ve Yunara çıkarıldı (kullanıcı kararı: kitleri bilinmeden etiket tahmin olur), kalan 45'in hepsi. Ana roller: top 14, jg 12, mid 10, sup 9, adc 0 (gerçek oyundaki saf nişancılar Yunara hariç zaten havuzda; Quinn ve Mel'in ikinci rolü adc). Havuzda Bot'ta oynayabilen 27, diğer koridorlarda 38-51; fearless bo5'in son maçında 19 adc açık kalır.
  - top: Ambessa, Olaf, Gwen, Yorick, Nasus, Singed, Volibear, Kled, Urgot, Illaoi, Quinn, Cho'Gath, Trundle, Heimerdinger · jg: Shaco, Rek'Sai, Ivern, Skarner, Shyvana, Briar, Udyr, Evelynn, Rammus, Bel'Veth, Fiddlesticks, Warwick · mid: Zoe, Aurora, Ryze, Hwei, Naafiri, Akshan, Vex, Malzahar, Qiyana, Mel · sup: Sona, Yuumi, Maokai, Zyra, Vel'Koz, Xerath, Neeko, Pantheon, Poppy.
- Taslak ölçümle düzeltildi (geçici havuz; 30.000 draft ±2,0, sonra 60.000 draft ±1,2): Ambessa orta 8 → 7 (%56,1 → %52,0; güç toplamı 22, etiketleri geniş) · Hwei `aoe` 2 → 1 (%55,9 → %53,9; ulti toplu hasar ama Brand seviyesinde değil) · Yorick `pick` 1 → 2 (%43,7 → %45,5; W kafesi) · Bel'Veth `split` 2 → 1 (`--check`: sadece orman, K12 top/mid'de). Kullanıcı: Olaf geç 5 → 6, Nasus erken 3 → 4, Warwick geç 5 → 6, Pantheon erken 9 → 8. K13 +1: Yuumi + Zeri → 18.
- 170'lik havuzda şampiyon başına maç azaldı: 30.000 draft'ta hata payı ±2,0; artık ölçüm **60.000 draft** (±1,0-1,4).
- **Son ölçüm (170'lik havuz, 60.000 draft):** Mavi %50,1, süre 29,3, kill 25,0. Kimlikler: Engage %54,0 · Poke %50,4 · Pick %49,4 · Koruma %50,9 · Dive %49,6 · Split %47,2 (şimdiye kadarki en sıkı). Erken comp %49,7 / 35'ten önce %32,4 ✓, scale comp kurtarıyor %32,7 ✓, kural farkı 10 %64,3, 12 %67,7. K13 objektiflerde %2,9 maçta (havuz büyüdükçe ikililer seyrekleşiyor). Kullanıcı değişiklikleri: Warwick %50,8, Olaf %50,1, Nasus %48,8, Pantheon %47,3. Bant dışı: Thresh %56,4, Orianna %56,3, Ornn %55,8 · Fiddlesticks %44,9, Vladimir %44,7, **Bel'Veth %43,5** (taslakta %44,9), Draven %42,9, Pyke %41,3 (son ikisi bilinçli). Sonraki denge turu adayları: Thresh/Orianna/Ornn ve Bel'Veth/Vladimir/Fiddlesticks.
