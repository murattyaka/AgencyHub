# DATABASE_SCHEMA

## Konvansiyonlar
- Veritabanı: SQL Server (geliştirme: `.\SQLEXPRESS` / SQL Server 2025 Express; production: Standard önerilir, P-4).
- Her modülün kendi şeması, DbContext'i ve migration geçmişi vardır (`<şema>.__EFMigrationsHistory`).
- PK: `Id uniqueidentifier`; EF Core SQL Server-sıralı GUID atar (ADR-006).
- Tenant'a ait tablolarda `AgencyId uniqueidentifier NOT NULL`. `AgencyId` ile başlayan bir index otomatik eklenir (zaten varsa eklenmez).
- Audit kolonları: `CreatedAtUtc datetimeoffset`, `CreatedBy`, `UpdatedAtUtc`, `UpdatedBy`.
- Concurrency token şimdilik yalnızca `identity.RefreshTokens` tablosunda; diğer tablolarda teknik borç 9.
- Enum'lar `nvarchar(20)` olarak string saklanır (yeniden numaralandırma riski yok).
- Migration oluşturma (proje kökünden):
  ```powershell
  dotnet ef migrations add <Ad> --project src/Modules/<Modül>/AgencyHub.Modules.<Modül> --startup-project src/Modules/<Modül>/AgencyHub.Modules.<Modül> --output-dir Infrastructure/Migrations
  ```
  Design-time bağlantı: `AGENCYHUB_DESIGN_CONNECTION` ortam değişkeni, yoksa `.\SQLEXPRESS / AgencyHub`.

## Şema: `identity` (migration: `InitialIdentity`, 2026-11-11) ✅

### identity.Users (tenant'a ait değil; bir kullanıcı birden fazla ajansa üye olabilir)
| Kolon | Tip | Not |
|---|---|---|
| Id | uniqueidentifier PK | |
| Email | nvarchar(256) | Girildiği haliyle |
| NormalizedEmail | nvarchar(256) | `UPPER(TRIM(Email))`, **UX** |
| DisplayName | nvarchar(100) | |
| PasswordHash | nvarchar(256) | ASP.NET Core PasswordHasher v3 (PBKDF2) |
| IsActive | bit | |
| AccessFailedCount | int | Kilitlenince sıfırlanır |
| LockoutEndUtc | datetimeoffset NULL | |
| Audit kolonları | | |

### identity.AgencyMemberships (tenant'a ait)
| Kolon | Tip | Not |
|---|---|---|
| Id | uniqueidentifier PK | |
| AgencyId | uniqueidentifier | Tenant |
| UserId | uniqueidentifier FK → Users (Restrict) | |
| Role | nvarchar(20) | Owner / Admin / Member / Viewer |
| IsActive | bit | |
| Audit kolonları | | |

Index'ler: **UX (AgencyId, UserId)**, IX (UserId).

### identity.RefreshTokens (tenant'a ait değil; `AgencyId` her refresh'te yeniden doğrulanır)
| Kolon | Tip | Not |
|---|---|---|
| Id | uniqueidentifier PK | |
| UserId | uniqueidentifier FK → Users (Cascade) | |
| AgencyId | uniqueidentifier | Token'ın verildiği ajans |
| TokenHash | char(64) | SHA-256 hex, **UX**. Ham token saklanmaz. |
| FamilyId | uniqueidentifier | Aynı login'den doğan token'lar, IX |
| CreatedAtUtc / ExpiresAtUtc | datetimeoffset | |
| RevokedAtUtc | datetimeoffset NULL | |
| RevocationReason | nvarchar(20) NULL | Rotated / SignedOut / ReuseDetected / AccessRevoked |
| ReplacedByTokenId | uniqueidentifier NULL | |
| ConcurrencyStamp | uniqueidentifier | Concurrency token: paralel refresh'lerden yalnızca biri kazanır |

Bekleyen: süresi dolmuş token'ların temizlik job'ı (Sprint 2).

## Şemalar: `agency`, `client`, `project`, `website` (2026-11-11) ✅
Modüller arası referanslarda **veritabanı FK'si yoktur** (ADR-014); varlık kontrolü sözleşmeler üzerinden yapılır. Tüm tablolarda audit kolonları bulunur.

| Tablo | Tenant'a ait | Kolonlar | Index'ler |
|---|---|---|---|
| `agency.Agencies` | Hayır (tenant'ın kendisi; erişim `Id = token.agency_id` ile) | Id, Name nvarchar(100), Status nvarchar(20) (Active/Suspended) | PK |
| `client.Clients` | Evet | Id, AgencyId, Name nvarchar(200), ContactName nvarchar(100) NULL, ContactEmail nvarchar(256) NULL, Notes nvarchar(2000) NULL, Status (Active/Archived) | IX(AgencyId, Name) |
| `project.Projects` | Evet | Id, AgencyId, ClientId → Client modülü, Name nvarchar(200), Description nvarchar(2000) NULL, Status (Active/Completed/Archived) | IX(AgencyId, ClientId), IX(AgencyId, Name) |
| `website.Websites` | Evet | Id, AgencyId, ProjectId → Project modülü, Name nvarchar(200), Url nvarchar(500) (normalleştirilmiş), Host nvarchar(253), Status (Active/Paused) | **UX(AgencyId, Url)**, IX(AgencyId, ProjectId) |

Not: `Url` 500 karakterle sınırlı; böylece `(AgencyId, Url)` index anahtarı SQL Server'ın 1700 byte sınırının altında kalır.

Geliştirme veritabanını güncellemek için: `./scripts/update-database.ps1`

## Büyük hacimli tablolar (Sprint 3+, ön tasarım)
- `monitoring.UptimeChecks`: günde ~14,4 milyon satır (10.000 site × 1 dk). Tarih bazlı partition, 30 gün ham veri saklama, saatlik/günlük özet tabloları.
