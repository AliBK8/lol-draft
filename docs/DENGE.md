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
