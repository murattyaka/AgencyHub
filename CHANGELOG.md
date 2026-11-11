# CHANGELOG

Biçim: [Keep a Changelog](https://keepachangelog.com/tr/1.1.0/). Sürümleme: SemVer (ilk yayın 0.1.0).

## [Unreleased]

### Eklendi – 2026-11-11 (S1-06 / S1-07 Agency, Client, Project, Website)
- Yeni modüller: `Agency`, `Client` (+Contracts), `Project` (+Contracts), `Website` (+Contracts); şemalar `agency`, `client`, `project`, `website`; her biri için `Initial*` migration'ı.
- Agency:
  - `POST /api/v1/agencies/register`: ajans ve ilk Owner kullanıcı; anonim, rate limit'li, `AgencyRegistration:Enabled` ile açılıp kapanır, varsayılan kapalı.
  - `GET` / `PUT /api/v1/agency`.
- Client, Project ve Website için CRUD: `GET` liste (sayfalama, arama, filtre), `GET /{id}`, `POST`, `PUT /{id}`, `DELETE /{id}`. Alt kayıtları olan üst kayıt silinemez (409).
- Rol matrisi: Viewer okur; Member oluşturur ve günceller; Owner/Admin ayrıca siler. Yeni `AgencyEditor` policy'si.
- `WebsiteUrlPolicy`, SSRF'e karşı ilk katman:
  - Yalnızca http/https ve standart portlar; URL'de kimlik bilgisi olamaz.
  - IP literal, `localhost`, iç ağ alan adları, tek etiketli host ve query içeren URL'ler reddedilir.
  - URL normalleştirme (küçük harf, IDN → punycode, sondaki `/` kaldırılır); ajans içinde URL tekil.
- Modüller arası sözleşmeler: `IClientDirectory`, `IProjectDirectory`, `IWebsiteDirectory` (tenant kapsamlı).
- Ortak altyapı: `AddModuleDbContext<T>`, `ModuleDesignTimeDbContextFactory<T>`, `ToPagedResultAsync`.
- `scripts/update-database.ps1`: geliştirme veritabanına tüm modül migration'larını uygular.
- Testler: 89 yeni test (cross-tenant izolasyonu, rol matrisi, URL politikası, kayıt telafisi, girdi doğrulama); mimari testler tüm modüllere genellendi.

### Değişti – 2026-11-11
- Bozuk JSON ve hatalı query parametreleri 500 yerine 400 ProblemDetails döner (`BadHttpRequestException` eşlemesi).
- JSON'da enum'lar string olarak taşınır (`"Active"`).
- Identity modülü ortak `AddModuleDbContext` kaydını kullanıyor.

### Eklendi – 2026-11-11 (S1-05 Identity)
- `AgencyHub.Modules.Identity` ve `AgencyHub.Modules.Identity.Contracts` projeleri.
- `identity` şeması ve tabloları: `Users`, `AgencyMemberships`, `RefreshTokens` (migration `InitialIdentity`).
- Endpoint'ler: `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh`, `POST /api/v1/auth/logout`, `GET /api/v1/auth/me`.
- Kimlik doğrulama:
  - JWT access token (HS256, 15 dk).
  - Refresh token: HttpOnly + Secure + SameSite=Strict cookie, tek kullanımlık, rotasyonlu, yalnızca SHA-256 hash'i saklanıyor. Yeniden kullanımda token ailesinin tamamı iptal ediliyor.
- Brute-force koruması:
  - 5 hatalı denemede 15 dk hesap kilidi.
  - Tüm başarısız girişlerde aynı 401 yanıtı.
  - Bilinmeyen kullanıcılar için de parola hash maliyeti ödeniyor (zamanlama ile kullanıcı tespitine karşı).
- `IIdentityProvisioning` sözleşmesi (diğer modüller için kullanıcı ve üyelik oluşturma); parola politikası en az 12 karakter.
- Yetkilendirme:
  - Fallback policy: tüm endpoint'ler ajansa bağlı, kimliği doğrulanmış kullanıcı gerektirir.
  - `AgencyMember` ve `AgencyAdmin` policy'leri.
- JWT ayarları açılışta doğrulanıyor: anahtar ≥32 byte, `DEV-ONLY-` anahtarlar yalnızca Development'ta, ömür sınırları.
- `AuthenticationFailedException` → 401 eşlemesi.
- Rate limit değerleri konfigürasyona taşındı (`RateLimiting:*`).
- `/health/ready` artık Identity veritabanını kontrol ediyor.
- Test altyapısı: test koşusu başına izole SQL Server veritabanı (`AgencyHub_Test_<guid>`); 41 yeni test.
- `dotnet-ef` yerel araç olarak eklendi (`dotnet-tools.json`).

### Değişti – 2026-11-11
- Anonim istemciler bilinmeyen route'larda 404 yerine 401 alıyor (fallback policy; route'lar keşfedilemez).
- Serilog request logging en dış katmana alındı: 401/404 gibi işlenmiş hatalar artık "500 + exception" olarak loglanmıyor.
- Serilog `preserveStaticLogger: true`: her host kendi logger'ını kullanıyor.
- CORS `AllowCredentials` (refresh cookie için); origin'ler açıkça listelenmeli.
- `AgencyId` için otomatik index, `AgencyId` ile başlayan bir index zaten varsa eklenmiyor.
- Test projeleri xUnit v3 + Microsoft.Testing.Platform üzerinde.

### Eklendi – 2026-11-11
- Solution iskeleti (`AgencyHub.slnx`): SharedKernel, Infrastructure, Api, Worker ve üç test projesi.
- Merkezi build ayarları (`Directory.Build.props`: nullable, `TreatWarningsAsErrors`, `latest-recommended` analyzer'lar) ve merkezi paket yönetimi (`Directory.Packages.props`).
- Tenant altyapısı:
  - `ITenantOwned`, `ITenantContext` / `ITenantContextSetter`, scope başına tek tenant kuralı.
  - `TenantAwareDbContext`: tüm `ITenantOwned` entity'lere otomatik, isimli (`"Tenant"`) global query filter ve `AgencyId` index'i.
  - `TenantSaveChangesInterceptor`: AgencyId atama, başka tenant'a yazma, tenant değiştirme ve tenant bağlamı olmadan yazma girişimlerinin engellenmesi.
  - `AuditableEntityInterceptor`: Created/Updated alanları ve Created alanlarının üzerine yazılmasının engellenmesi.
- API host:
  - RFC 9457 ProblemDetails ile merkezi hata yönetimi (`GlobalExceptionHandler`). 500 yanıtlarında mesaj ve stack trace sızmıyor.
  - Tenant'ın yalnızca doğrulanmış token claim'inden çözülmesi (`TenantResolutionMiddleware`).
  - Güvenlik header'ları; `Server` header'ı kaldırıldı.
  - Rate limiting (IP başına global 300/dk; `authentication` policy 5/dk).
  - `/health/live` ve `/health/ready`, Serilog, OpenTelemetry tracing (OTLP opsiyonel), CORS, HSTS.
  - OpenAPI yalnızca Development ortamında açık.
- Worker host: Windows Service desteği ve Serilog.
- Testler: 54 test (unit, integration, architecture). xUnit v3 + Microsoft.Testing.Platform.
- Dokümantasyon: 10 proje dokümanı.
