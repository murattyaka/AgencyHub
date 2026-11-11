# PROJECT_STATUS

> Son güncelleme: 2026-11-11 · Aşama: **Sprint 1 – çekirdek hiyerarşi tamam (backend)** · Proje adı: `AgencyHub` (geçici, ADR-008)

Bu dosyada yalnızca **doğrulanmış** durum yer alır. "Tamamlandı" ifadesi; kodun yazıldığı, build'in başarılı olduğu ve ilgili testlerin geçtiği anlamına gelir.

## Özet

| Gösterge | Durum |
|---|---|
| Build | ✅ 0 uyarı, 0 hata (`TreatWarningsAsErrors`) |
| Testler | ✅ **184 / 184**: Unit 61, Integration 103 (gerçek SQL Server), Architecture 20 |
| Mutasyon kontrolleri | ✅ Tenant filtresi kapatılınca 11/11 cross-tenant endpoint testi kırıldı; refresh-token reuse koruması kapatılınca ilgili test kırıldı. Kod geri alındı. |
| Test veritabanları | ✅ Her koşuda oluşturulup siliniyor (koşu sonrası kalan: 0) |
| Git | ⚠️ Henüz commit yok; `git` PATH'te değil |
| Frontend | ⛔ Node.js / Angular CLI kurulu değil |

## Sprint 1 iş durumu

| ID | İş | Durum | Doğrulama |
|---|---|---|---|
| S1-01 | Repo, solution, paket yönetimi, analyzer'lar | ✅ | Build temiz |
| S1-02 | 10 doküman | ✅ | – |
| S1-03 | Tenant altyapısı | ✅ | Unit (SQLite) + SQL Server üzerinde endpoint seviyesinde |
| S1-04 | Hata yönetimi, loglama, health, header'lar, rate limiting | ✅ | 🟡 HSTS otomatik testi yok |
| S1-05 | Identity | ✅ | Bkz. Test durumu |
| S1-06 | **Agency** (kayıt, mevcut ajans görüntüleme/güncelleme), **Client**, **Project**, **Website** CRUD | ✅ | Yaşam döngüsü, sayfalama, filtreleme, rol matrisi, doğrulama ve URL politikası testleri |
| S1-07 | Cross-tenant endpoint testleri | ✅ | 11 senaryo: okuma, güncelleme, silme, liste, çapraz bağlama, body'de agencyId, aynı URL |
| S1-08 | Angular iskeleti | ⛔ Engelli | Node.js kurulumu gerekiyor |
| S1-09 | CI pipeline | ⏳ | Git hosting bilgisi gerekiyor (P-6) |

## Modül durumu

| Modül | Durum | Not |
|---|---|---|
| BuildingBlocks | ✅ | Ortak modül DbContext kaydı ve sayfalama yardımcıları eklendi |
| API Host | ✅ | 5 modül bağlı; enum'lar JSON'da string |
| Worker Host | 🟡 İskelet | Hangfire Sprint 2'de |
| Identity & Access | ✅ | MFA Sprint 2'de |
| Agency Management | ✅ | Kayıt varsayılan olarak **kapalı** (P-7) |
| Client Management | ✅ | |
| Project Management | ✅ | |
| Website Management | ✅ | URL politikası (SSRF katman 1); DNS/IP kontrolü Sprint 2'de |
| Diğer 12 modül | ⏳ | Bkz. PROJECT_ROADMAP |

## Test durumu

| Proje | Kapsam | Sonuç |
|---|---|---|
| UnitTests | Tenant filtresi ve yazma koruması, audit, paging, **WebsiteUrlPolicy (30 vaka)** | 61 / 61 |
| Api.IntegrationTests | Pipeline, hata yönetimi, Auth (40+), **Ajans kaydı ve telafi adımı, Portfolio yaşam döngüsü, cross-tenant izolasyonu, rol matrisi (4 rol × 4 işlem), girdi doğrulama (güvensiz URL'ler, bozuk JSON ve query)**, anonim endpoint allow-list'i, entity sızıntısı, log hijyeni | 103 / 103 |
| ArchitectureTests | Katmanlar; her modül için: yalnızca Contracts üzerinden erişim, Domain'in framework'ten bağımsızlığı, iç tiplerin internal olması; Contracts'ın framework'ten bağımsızlığı | 20 / 20 |

## Teknik borçlar
1. Coverage ölçülmüyor (`Microsoft.Testing.Extensions.CodeCoverage` eklenmeli).
2. OpenTelemetry'de yalnızca tracing var.
3. Serilog hassas alan maskeleme policy'si yok (kod kuralları + test ile korunuyor).
4. Refresh token ailesinin mutlak ömrü yok.
5. Süresi dolmuş refresh token'ları temizleyen job yok (Sprint 2, Hangfire).
6. JWT anahtar rotasyonu yok (ADR-012).
7. Hesap kilitleme saldırgana kullanıcıyı kilitletme (DoS) imkânı veriyor.
8. Pasifleştirilen üyelik, access token süresi (≤15 dk) dolana kadar `/me` dışındaki endpoint'lere erişebilir.
9. **Optimistic concurrency yok:** Client, Project ve Website güncellemelerinde son yazan kazanıyor. ETag/If-Match ve rowversion eklenmeli.
10. **Modüller arası silme kontrolü "önce kontrol et, sonra sil" şeklinde.** Bir müşteri silinirken aynı anda ona proje eklenirse yetim proje kalabilir. Olasılık düşük; ileride soft delete veya event ile çözülecek.
11. **Ajans kaydında telafi adımı:** Owner oluşturma başarısız olursa ajans satırı siliniyor, ancak süreç iki adım arasında çökerse sahipsiz ajans kalabilir (ADR-013). Temizlik job'ı gerekiyor.
12. `Suspended` durumundaki ajansın kullanıcıları hâlâ giriş yapabiliyor (ajans durumu login'de kontrol edilmiyor).
13. Kayıtta CAPTCHA ve email doğrulaması yok. Kayıt kapalı olduğu sürece risk yok.
14. Arama `LIKE '%x%'` ile yapılıyor; büyük veri hacminde full-text arama gerekecek.

## Bilinen hatalar
- Bilinen bir hata yok.

## Engeller
1. Node.js LTS + Angular CLI kurulu değil.
2. Git for Windows PATH'te değil.
3. P-1 Redis, P-6 Git hosting / CI.
4. **P-7 kayıt modeli:** geçici olarak "konfigürasyonla açılıp kapanan self-service kayıt" seçildi; varsayılan değer kapalı.

## Sonraki öncelikler
1. İlk commit ve CI (P-6)
2. Angular iskeleti: login, ajans kaydı, Client/Project/Website ekranları (Node kurulumundan sonra)
3. Sprint 2: Worker + Hangfire, job'larda tenant bağlamı, `SafeHttpClient` (SSRF katman 2)
4. Teknik borç 9 (concurrency) ve 12 (askıya alınmış ajans)
5. MFA
