# DECISION_LOG

Her kayıt: bağlam, karar, alternatifler, sonuçlar. Durum: **Kabul** / **Önerildi** (onay bekliyor) / **Bekliyor** (karar verilmedi).

---

## ADR-001 – Modular Monolith + Clean Architecture · Kabul · 2026-11-11
**Bağlam:** 17 modül var, ekip küçük ve hedef ortam tek Windows Server + IIS.
**Karar:** Tek deploy edilen modüler monolit. Her modül kendi `Domain / Application / Infrastructure / Endpoints` katmanlarına ve kendi DbContext + SQL şemasına sahip olacak. Modüller arası iletişim yalnızca public contract'lar ve in-process event'ler üzerinden yapılacak.
**Alternatif:** Mikroservisler. Operasyon maliyeti ve dağıtık transaction karmaşıklığı bu aşamada gerekçesiz.
**Sonuç:** Modül sınırları mimari testlerle zorlanacak. İleride yoğun bir modül (ör. crawler) ayrı servise çıkarılabilir.

## ADR-002 – Tenant = Agency; paylaşımlı veritabanı, satır bazlı izolasyon · Kabul · 2026-11-11
**Karar:**
- Tenant anahtarı `AgencyId`. Tenant'a ait her entity `ITenantOwned` uygular.
- Tenant **yalnızca** doğrulanmış token claim'inden (`agency_id`) çözülür. Header, route, query ve body asla tenant kaynağı değildir.
- Okuma: `TenantAwareDbContext` isimli global query filter uygular. Tenant yoksa hiçbir satır dönmez (fail-closed).
- Yazma: `TenantSaveChangesInterceptor` AgencyId'yi atar ve doğrular.
- Bir scope'ta tenant bir kez set edilir, değiştirilemez.

**Alternatifler:** Tenant başına veritabanı (izolasyonu güçlü ama 10.000 site / çok ajans ölçeğinde migration ve operasyon maliyeti yüksek) veya tenant başına şema.
**Açık öneri:** Derinlemesine savunma için SQL Server Row-Level Security (`SESSION_CONTEXT`) eklenmesi. İlk modül tamamlandıktan sonra ayrıca değerlendirilecek.

## ADR-003 – Arka plan işleri IIS dışında, Windows Service'te · Kabul · 2026-11-11
**Bağlam:** IIS app pool recycle ve idle timeout, uzun süren işleri öldürür.
**Karar:** `AgencyHub.Worker` ayrı bir Windows Service olacak (Hangfire server, uptime scheduler, crawler'lar). API yalnızca kuyruğa iş ekler.
**Sonuç:** İki deploy birimi oluşur (IIS site + Windows Service). Tenant bağlamı job argümanlarıyla taşınacak ve her job kendi scope'unda `SetTenant` çağıracak.

## ADR-004 – API stili: Minimal API endpoint grupları · Önerildi
**Gerekçe:** Modül başına `MapGroup` ile kapsülleme, daha az boilerplate, .NET 10'da yerleşik OpenAPI desteği.
**Alternatif:** Controller'lar. Ekibin alışkanlığı farklıysa tercih edilebilir; performans farkı belirleyici değil.

## ADR-005 – Test altyapısı · Kabul · 2026-11-11
- xUnit v3 + Microsoft.Testing.Platform (`global.json` → `test.runner`). .NET 10 SDK, xUnit v3 için VSTest modunu desteklemiyor.
- BuildingBlocks testleri: SQLite in-memory (hızlı, ilişkisel).
- Modül integration testleri: gerçek SQL Server (yerelde SQLEXPRESS, test başına izole veritabanı). S1-07'de kurulacak.
- Assertion kütüphanesi: yerleşik `Assert` (FluentAssertions v8 ticari lisanslı).

## ADR-006 – Birincil anahtarlar: SQL Server-sıralı GUID · Kabul · 2026-11-11
**Karar:** `Entity.Id` (Guid) uygulama kodunda üretilmez. EF Core SQL Server sağlayıcısı `Add()` sırasında sıralı GUID atar.
**Gerekçe:** `Guid.CreateVersion7()` .NET'te zaman sıralıdır ama SQL Server'ın `uniqueidentifier` sıralamasında değildir. Clustered index fragmentasyonuna yol açar.

## ADR-007 – Kod kalitesi kuralları · Kabul · 2026-11-11
Merkezi paket yönetimi, `TreatWarningsAsErrors`, `AnalysisLevel=latest-recommended`, nullable açık. Paketler .NET 10 hattında tutulur (10.0.x). EF Core 11 / .NET 11 paketleri kullanılmaz.

## ADR-008 – Proje adı `AgencyHub` · Kabul (geçici) · 2026-11-11
Ürün adı belirlenmediği için çalışma adı olarak seçildi. Namespace değişikliği maliyetli olduğundan, kalıcı ad ilk modüller yazılmadan önce netleşmeli.

## ADR-009 – Hafif, özel Identity (ASP.NET Core Identity yerine) · Kabul · 2026-11-11
**Karar:** Kullanıcı, üyelik ve refresh token tabloları modülün kendi DbContext'inde tutulur. Parola hash'i için ASP.NET Core'un `PasswordHasher<T>` (PBKDF2) bileşeni kullanılır; `UserManager` ve `IdentityDbContext` kullanılmaz.
**Gerekçe:** `IdentityDbContext`, `TenantAwareDbContext`'ten türeyemez (mimari kural). Ayrıca cookie tabanlı varsayılanlar ve fazla tablo getirir. İhtiyaç duyulan parçalar (lockout, rotasyon, üyelik) az ve testli.
**Bedel:** MFA (TOTP) Sprint 2'de elle eklenecek (`Microsoft.AspNetCore.Identity` içindeki TOTP yardımcıları kullanılabilir).

## ADR-010 – Modül proje yapısı: Modül + Contracts · Kabul · 2026-11-11
Her modül iki projeden oluşur: `AgencyHub.Modules.X` (tüm tipler `internal`; klasörler Domain/Application/Infrastructure/Endpoints) ve `AgencyHub.Modules.X.Contracts` (diğer modüllerin görebildiği arayüzler ve DTO'lar). Diğer modüller yalnızca Contracts'a referans verebilir; bu kural mimari testlerle zorlanır. 17 modül × 4 katman projesi yerine 34 proje.

## ADR-011 – Refresh token HttpOnly cookie'de · Kabul · 2026-11-11
**Karar:** Access token yanıt gövdesinde döner (SPA bellekte tutar). Refresh token yalnızca `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth` cookie'sinde taşınır.
**Gerekçe:** XSS ile refresh token çalınamaz. SameSite=Strict ve dar path, CSRF yüzeyini auth endpoint'leriyle sınırlar.
**Koşul:** Angular ve API aynı site (registrable domain) altında olmalı (ör. `app.ornek.com` + `api.ornek.com`). CORS'ta credentials açık ve origin'ler açıkça listelenmeli (P-5 ile ilişkili).

## ADR-012 – JWT imzası: HS256 (şimdilik) · Kabul · 2026-11-11
Token'ı tek bir uygulama hem üretip hem doğruladığı için simetrik anahtar yeterli. Anahtar ≥32 byte olmalı ve Production'da ortam değişkeninden okunur. Worker veya üçüncü taraflar token doğrulayacaksa RS256/ES256'ya ve anahtar rotasyonuna (`kid`) geçilecek.

## ADR-013 – Modüller arası yazma: telafi adımı (dağıtık transaction yok) · Kabul · 2026-11-11
**Bağlam:** Ajans kaydı iki modüle yazıyor (Agency + Identity), her biri ayrı DbContext'te. `TransactionScope`, EF'in retry stratejisiyle çakışıyor; MSDTC ise istenmiyor.
**Karar:** Önce ajans satırı yazılır, ardından `IIdentityProvisioning` çağrılır. Başarısız olursa ajans satırı silinir (telafi adımı, `CancellationToken.None` ile).
**Bedel:** İki adım arasında süreç çökerse sahipsiz ajans kalabilir. Temizlik job'ı (Sprint 2) ve sonrasında outbox değerlendirilecek.

## ADR-014 – Modüller arası referanslar: FK yok, sözleşme ile doğrulama · Kabul · 2026-11-11
`Project.ClientId` ve `Website.ProjectId` farklı şemalara işaret eder ve veritabanı FK'si yoktur. Varlık kontrolü `IClientDirectory` / `IProjectDirectory` üzerinden yapılır. Bu sorgular tenant filtresinden geçtiği için başka ajansın kaydı "yok" sayılır ve 422 döner. Alt kaydı olan üst kayıt silinemez (409). Bilinen yarış durumu: teknik borç 10.

## ADR-015 – Rol matrisi · Kabul · 2026-11-11
| Rol | Okuma | Oluşturma/Güncelleme | Silme | Ajans ayarları |
|---|---|---|---|---|
| Viewer | ✅ | ❌ | ❌ | ❌ |
| Member | ✅ | ✅ | ❌ | ❌ |
| Admin | ✅ | ✅ | ✅ | ✅ |
| Owner | ✅ | ✅ | ✅ | ✅ |
Policy'ler: `AgencyMember`, `AgencyEditor`, `AgencyAdmin`. Owner ile Admin arasındaki fark (sahiplik devri, ajans silme) kullanıcı yönetimiyle gelecek.

## ADR-016 – Website URL politikası · Kabul · 2026-11-11
Yalnızca alan adı (IP literal yok), http/https, standart port, kimlik bilgisi yok, query yok, iç ağ alan adları yok. URL normalleştirilmiş haliyle saklanır ve ajans içinde tekil olur; farklı ajanslar aynı siteyi izleyebilir. Bu katman, herkese açık bir DNS adının iç IP'ye çözülmesini engelleyemez; o kontrol bağlantı anında `SafeHttpClient` ile yapılacak (Sprint 2).

## ADR-017 – Ajans kaydı geçici modeli (P-7 için ara çözüm) · Önerildi · 2026-11-11
Self-service kayıt endpoint'i var ancak `AgencyRegistration:Enabled` ile kontrol ediliyor: varsayılan **kapalı**, Development'ta açık. Kapalıyken endpoint 404 döner. Nihai model (herkese açık / platform admin / davet) karara bağlanınca bu kayıt güncellenecek.

---

## Bekleyen kararlar
| # | Konu | Seçenekler | Etki |
|---|---|---|---|
| P-1 | Redis | Garnet (Microsoft, Windows'ta native çalışır) · Memurai (ticari) · Redis on WSL/Linux VM | Cache, rate limit dağıtımı, SignalR backplane |
| P-2 | AI sağlayıcısı | – | AI Recommendation modülü, KVKK değerlendirmesi |
| P-3 | Uptime scheduler | Hangfire recurring · özel `BackgroundService` + `Channel` (önerilen) | 10.000 site × 1 dk ≈ 167 istek/sn |
| P-4 | Production SQL Server sürümü | Standard (önerilen) · Express (10 GB limiti, SQL Agent yok) | Uptime verisi büyümesi, yedekleme |
| P-5 | Angular uygulamasının barındırılması | Ayrı IIS sitesi (önerilen) · API ile aynı site | CORS, CSP, deploy |
| P-6 | Git hosting / CI | Azure DevOps · GitHub · GitLab | S1-09 |
| P-7 | Kayıt modeli | Herkese açık ajans kaydı · yalnızca platform admin'in ajans açması · davet | Ara çözüm ADR-017 (konfigürasyonla, varsayılan kapalı) |
| P-8 | Bir kullanıcı birden çok ajansa üye olabilir mi? | Evet (veri modeli destekliyor, login'de `agencyId` seçimi var) · Hayır | Davet akışı |
