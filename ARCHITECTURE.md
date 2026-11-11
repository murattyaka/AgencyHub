# ARCHITECTURE

## 1. Bağlam
Ajansların, müşterilerine ait web sitelerini tek merkezden yönetip izlediği Multi-Tenant SaaS platformu.
Hiyerarşi: `Platform → Agency → AgencyUsers → Clients → Projects → Websites → Scans / Monitoring / Tasks / Reports`

## 2. Çalışma zamanı bileşenleri

```
 Tarayıcı (Angular SPA)
        │ HTTPS + JWT
        ▼
 ┌────────────────────┐        ┌─────────────────────────────┐
 │ AgencyHub.Api      │ enqueue│ AgencyHub.Worker            │
 │ IIS (in-process)   │───────►│ Windows Service             │
 │ REST, kısa istekler│        │ Hangfire server, uptime      │
 └─────────┬──────────┘        │ scheduler, crawler,          │
           │                   │ Playwright/Lighthouse        │
           │                   └──────────┬──────────────────┘
           ▼                              ▼
     SQL Server (modül şemaları + Hangfire şeması)      Redis (cache, dağıtık kilit)
                                          │
                              Güvenli egress (SSRF filtresi) ──► Müşteri web siteleri, Google API'leri
```

- **API asla uzun iş yapmaz.** Tarama ve analiz istekleri kuyruğa yazılır, sonuçlar ilerleme durumu ile okunur.
- **Worker IIS dışındadır** (ADR-003).

## 3. Solution yapısı (mevcut)

```
AgencyHub/
├─ src/
│  ├─ BuildingBlocks/
│  │  ├─ AgencyHub.SharedKernel      Bağımlılıksız: Entity, ITenantOwned, ITenantContext, ICurrentUser,
│  │  │                              AppException türleri, PageRequest/PagedResult
│  │  └─ AgencyHub.Infrastructure    EF Core: TenantAwareDbContext, interceptor'lar, TenantContext, DI
│  ├─ Hosts/
│  │  ├─ AgencyHub.Api               Pipeline, hata yönetimi, güvenlik, health, OpenAPI
│  │  └─ AgencyHub.Worker            Windows Service host
│  └─ Modules/
│     ├─ Agency/    AgencyHub.Modules.Agency               (henüz Contracts'a ihtiyaç yok)
│     ├─ Client/    AgencyHub.Modules.Client(.Contracts)   IClientDirectory
│     ├─ Project/   AgencyHub.Modules.Project(.Contracts)  IProjectDirectory
│     ├─ Website/   AgencyHub.Modules.Website(.Contracts)  IWebsiteDirectory, WebsiteUrlPolicy
│     └─ Identity/
│        ├─ AgencyHub.Modules.Identity.Contracts   public: IIdentityProvisioning, AgencyRole,
│        │                                         IdentityClaimTypes, AuthorizationPolicies
│        └─ AgencyHub.Modules.Identity             internal: Domain / Application / Infrastructure
│                                                  (DbContext, migrations) / Endpoints; public: IdentityModule
└─ tests/
   ├─ AgencyHub.UnitTests
   ├─ AgencyHub.Api.IntegrationTests
   └─ AgencyHub.ArchitectureTests
```

### Bağımlılık kuralları (ArchitectureTests ile zorlanır)
| Proje | Bağımlı olabileceği | Bağımlı OLAMAYACAĞI |
|---|---|---|
| SharedKernel | – | EF Core, ASP.NET Core, Infrastructure, Api |
| Infrastructure | SharedKernel, EF Core | ASP.NET Core, Api, Worker |
| Modül Domain | SharedKernel | EF Core, ASP.NET Core, diğer modüller |
| Modül Application | Domain, SharedKernel, FluentValidation | diğer modüllerin iç katmanları |
| Hosts | Her şey | – |

Ek kurallar (hepsi testli):
- Her somut `DbContext`, `TenantAwareDbContext`'ten türemelidir.
- Modüller birbirine yalnızca `*.Contracts` üzerinden bağımlıdır (ADR-010).
- Modül projesindeki tipler `internal`'dır; yalnızca kompozisyon sınıfı (`XModule`) ve EF migration'ları public olabilir.
- Contracts projeleri EF Core'a ve ASP.NET Core'a bağımlı olamaz.

### Modül bağımlılıkları (yalnızca Contracts üzerinden, döngüsüz)
```
Agency  ──► Identity.Contracts (IIdentityProvisioning)
Client  ──► Project.Contracts  (silmeden önce alt kayıt kontrolü)
Project ──► Client.Contracts   (üst kayıt var mı?), Website.Contracts (alt kayıt kontrolü)
Website ──► Project.Contracts  (üst kayıt var mı?)
Tümü    ──► Identity.Contracts (policy ve claim adları), Infrastructure, SharedKernel
```
Modüller arası referanslarda veritabanı FK'si yoktur (ADR-014). Tek veritabanı, modül başına bir şema.

### Kimlik doğrulama akışı (Identity)
```
login ──► AuthService ── parola doğrulama, lockout ──► üyelik seçimi
                                          │
              access token (15 dk, body) ◄┴► refresh token (cookie, 14 gün, hash'i DB'de)
refresh ─► token hash ile bul ─► daha önce kullanılmış mı? ── evet ─► aileyi iptal et, 401
                                 │ hayır
                                 └─► kullanıcı/üyelik aktif mi? ─► rotasyon (eski → Rotated) ─► yeni çift
istek ─► JwtBearer ─► TenantResolutionMiddleware (agency_id) ─► Authorization (fallback: AgencyMember)
```

## 4. Modül listesi
1. Identity & Access · 2. Agency · 3. Client · 4. Project · 5. Website · 6. SEO · 7. Security · 8. Uptime & Infrastructure (SSL, DNS) · 9. Performance · 10. Analytics Integration (GA4, GSC, Clarity) · 11. Content Audit · 12. AI Recommendation · 13. Task · 14. Reporting · 15. Notification · 16. Integration Management · 17. Audit Logging

## 5. Multi-tenant tasarımı (ADR-002)

| Katman | Mekanizma | Durum |
|---|---|---|
| Kimlik | Tenant yalnızca JWT `agency_id` claim'inden alınır (`TenantResolutionMiddleware`) | ✅ Testli; JwtBearer → TenantResolution → Authorization sırası bağlı |
| Scope | `TenantContext` scope başına tek tenant; değiştirme girişimi → `TenantIsolationException` | ✅ Testli |
| Okuma | İsimli global query filter `"Tenant"`; tenant yoksa boş sonuç | ✅ Testli (SQLite) |
| Yazma | `TenantSaveChangesInterceptor` | ✅ Testli (SQLite) |
| Arka plan | Job argümanında `AgencyId`, job başında `SetTenant` | ⏳ Sprint 2 |
| Veritabanı | SQL Server RLS (derinlemesine savunma) | 💡 Önerildi |
| Yanıt | Başka tenant'ın kaynağı → **404** (varlığı açığa çıkarılmaz) | ✅ Filtre davranışı ile |

Bilinçli kapsam dışı: `ExecuteUpdate`/`ExecuteDelete` ve ham SQL interceptor'lardan geçmez (bkz. SECURITY_CHECKLIST).

## 6. Hata yönetimi
Tüm istisnalar `GlobalExceptionHandler` içinde RFC 9457 ProblemDetails'e çevrilir.

| İstisna | HTTP |
|---|---|
| `FluentValidation.ValidationException` | 400 + `errors` |
| `NotFoundException` | 404 |
| `ForbiddenAccessException`, `TenantIsolationException` | 403 (tenant ihlalinde detay verilmez, güvenlik logu yazılır) |
| `ConflictException` | 409 |
| `BusinessRuleException` | 422 |
| İstemci isteği iptal etti | 499 (hata logu yazılmaz) |
| Diğer | 500, genel mesaj, `traceId` |

## 7. Gözlemlenebilirlik
- Serilog: Console; Production'da `%AGENCYHUB_LOG_DIR%` altına günlük dönen, 30 gün saklanan, 100 MB'ta bölünen JSON dosyaları.
- Request logging yalnızca path'i loglar (query string ve body loglanmaz).
- OpenTelemetry tracing (ASP.NET Core + HttpClient). OTLP endpoint konfigürasyonla açılır.
- Health: `/health/live` (bağımlılık kontrolü yok), `/health/ready` (`ready` tag'li kontroller: SQL, Redis, Hangfire eklenecek).

## 8. Ölçeklenebilirlik notları (hedef: 10.000 site; doğrulanmış kapasite değildir)
- Uptime: 10.000 × 1 dk ≈ 167 istek/sn ve günde ~14,4 milyon kayıt. Ham veri 30 gün saklanacak; saatlik/günlük özet tabloları ve tarih bazlı partition kullanılacak.
- Playwright/Lighthouse: her analiz bir Chrome süreci açar. Eşzamanlılık limiti (worker başına N) ve ayrı bir kuyruk gerekecek.
- Tüm liste endpoint'leri `PageRequest` kullanır (en fazla 100 kayıt/sayfa).
