# API_DOCUMENTATION

Canlı sözleşme: Development ortamında `GET /openapi/v1.json`. Production'da endpoint map edilmez.

## Endpoint'ler

| Metot | Yol | Yetki | Rate limit | Açıklama |
|---|---|---|---|---|
| GET | `/health/live` | Anonim | global | Süreç ayakta mı? `200 Healthy` |
| GET | `/health/ready` | Anonim | global | Identity veritabanı bağlantısı. `200 Healthy` / `503 Unhealthy` |
| GET | `/openapi/v1.json` | Anonim, yalnızca Development | global | OpenAPI dokümanı |
| POST | `/api/v1/auth/login` | Anonim | `authentication` | Giriş |
| POST | `/api/v1/auth/refresh` | Anonim (cookie) | `authentication` | Token yenileme |
| POST | `/api/v1/auth/logout` | Anonim (cookie) | `authentication` | Çıkış |
| GET | `/api/v1/auth/me` | `AgencyMember` | global | Oturumdaki kullanıcı |
| POST | `/api/v1/agencies/register` | Anonim (yalnızca `AgencyRegistration:Enabled=true` iken) | `authentication` | Ajans + Owner kullanıcı |
| GET | `/api/v1/agency` | `AgencyMember` | global | Oturumdaki ajans |
| PUT | `/api/v1/agency` | `AgencyAdmin` | global | Ajans adını güncelle |
| GET | `/api/v1/clients` · `/projects` · `/websites` | `AgencyMember` | global | Sayfalı liste |
| GET | `/api/v1/{clients\|projects\|websites}/{id}` | `AgencyMember` | global | Detay |
| POST | `/api/v1/{clients\|projects\|websites}` | `AgencyEditor` | global | Oluştur → `201` + `Location` |
| PUT | `/api/v1/{clients\|projects\|websites}/{id}` | `AgencyEditor` | global | Güncelle (tam gövde) |
| DELETE | `/api/v1/{clients\|projects\|websites}/{id}` | `AgencyAdmin` | global | Sil → `204` |

### Rol matrisi (ADR-015)
| Rol | Okuma | Oluşturma/Güncelleme | Silme / ajans ayarları |
|---|---|---|---|
| Viewer | ✅ | 403 | 403 |
| Member | ✅ | ✅ | 403 |
| Admin, Owner | ✅ | ✅ | ✅ |

### POST /api/v1/agencies/register
```json
{ "agencyName": "Örnek Dijital", "ownerEmail": "ayse@ornek.com", "ownerDisplayName": "Ayşe", "ownerPassword": "••••••••••••" }
```
`201` → `{ "agencyId": "…", "ownerUserId": "…" }`. Ardından `/auth/login` ile giriş yapılır.
- `400`: geçersiz alan veya zayıf parola (en az 12 karakter)
- `403`: istek giriş yapmış bir ajans kullanıcısından geliyor
- `404`: kayıt kapalı
- `409`: email zaten kayıtlı
- `429`: rate limit

Hata durumunda ajans kaydı geri alınır (ADR-013).

### Clients
Liste: `GET /api/v1/clients?page=1&pageSize=20&search=acme&status=Active`
```json
// POST / PUT gövdesi (PUT'ta "status" zorunlu: Active | Archived)
{ "name": "Acme", "contactName": "Ali", "contactEmail": "it@acme.com", "notes": "…", "status": "Active" }
// Yanıt
{ "id": "…", "name": "Acme", "contactName": "Ali", "contactEmail": "it@acme.com", "notes": "…", "status": "Active", "createdAtUtc": "…", "updatedAtUtc": null }
```
`DELETE` → müşterinin projesi varsa `409`.

### Projects
Liste: `GET /api/v1/projects?clientId=…&search=&status=Active|Completed|Archived`
```json
// POST
{ "clientId": "…", "name": "Yeni site", "description": "…" }
// PUT (clientId değiştirilemez)
{ "name": "Yeni site", "description": "…", "status": "Completed" }
```
- `POST`: `clientId` yoksa veya başka ajansa aitse `422` (iki durum ayırt edilemez).
- `DELETE`: projenin sitesi varsa `409`.

### Websites
Liste: `GET /api/v1/websites?projectId=…&search=&status=Active|Paused` (arama ad ve host'ta yapılır)
```json
// POST
{ "projectId": "…", "url": "https://www.ornek.com/tr/", "name": "Kurumsal site" }
// PUT
{ "url": "https://www.ornek.com/tr", "name": "Kurumsal site", "status": "Paused" }
// Yanıt
{ "id": "…", "projectId": "…", "name": "Kurumsal site", "url": "https://www.ornek.com/tr", "host": "www.ornek.com", "status": "Active", "createdAtUtc": "…", "updatedAtUtc": null }
```
URL kuralları (ADR-016):
- Yalnızca `http`/`https`, standart port (80/443).
- Alan adı zorunlu; IP adresi kabul edilmez.
- Kullanıcı adı/parola ve query string olamaz.
- `localhost`, `*.local`, `*.internal`, `*.lan`, `*.home.arpa` vb. ve tek etiketli adlar reddedilir.

URL normalleştirilir: scheme ve host küçük harfe çevrilir, IDN adı punycode'a dönüşür, sondaki `/` ve `#fragment` atılır. Aynı ajans içinde aynı URL ikinci kez eklenemez (`409`); farklı ajanslar aynı siteyi izleyebilir. Proje yoksa veya başka ajansa aitse `422`.

Diğer tüm endpoint'ler (gelecekte eklenecekler dahil) varsayılan olarak `AgencyMember` gerektirir. Anonim endpoint listesi `EndpointSecurityTests` ile sabitlenmiştir.

### POST /api/v1/auth/login
İstek:
```json
{ "email": "ayse@ajans.com", "password": "••••••••••••", "agencyId": null }
```
`agencyId` yalnızca kullanıcı birden fazla ajansa üyeyse gereklidir ve kullanıcının kendi üyelikleri arasından olmalıdır.

Başarılı yanıt `200`:
```json
{ "accessToken": "eyJ…", "tokenType": "Bearer", "expiresAtUtc": "2026-11-11T09:15:00+00:00", "agencyId": "…", "role": "Owner" }
```
Refresh token yanıt gövdesinde dönmez; `Set-Cookie` ile gelir:
`agencyhub_refresh=…; path=/api/v1/auth; secure; samesite=strict; httponly; expires=…`

| Durum | Ne zaman |
|---|---|
| 400 | Email veya parola boş, ya da çok uzun |
| 401 | Bilinmeyen email, yanlış parola, kilitli veya pasif hesap. **Tümü aynı yanıtı döner.** |
| 403 | Aktif üyelik yok veya istenen `agencyId` kullanıcıya ait değil |
| 422 | Kullanıcı birden fazla ajansa üye ve `agencyId` verilmedi |
| 429 | Rate limit aşıldı (`Retry-After` header'ı ile) |

### POST /api/v1/auth/refresh
Gövde yok; refresh cookie'si tarayıcı tarafından otomatik gönderilir (Angular: `withCredentials: true`).
- `200`: Login ile aynı gövde ve **yeni** refresh cookie'si. Eski token artık geçersizdir.
- `401`: Cookie yok, token geçersiz, süresi dolmuş veya iptal edilmiş, kullanıcı ya da üyelik pasif. **Daha önce kullanılmış bir token gönderilirse o oturumun tüm token'ları iptal edilir.**

### POST /api/v1/auth/logout
Gövde yok. Oturumun tüm refresh token'larını iptal eder ve cookie'yi siler. Her zaman `204` döner. Access token süresi dolana kadar (≤15 dk) geçerli kalır; istemci onu bellekten silmelidir.

### GET /api/v1/auth/me
```json
{ "userId": "…", "email": "ayse@ajans.com", "displayName": "Ayşe", "agencyId": "…", "role": "Owner" }
```
`403`: Kullanıcının bu ajanstaki üyeliği pasifleştirilmiş.

## Access token içeriği
| Claim | Anlamı |
|---|---|
| `sub` | Kullanıcı ID |
| `agency_id` | Tenant. **Sunucu tenant'ı yalnızca buradan alır.** |
| `role` | `Owner` / `Admin` / `Member` / `Viewer` |
| `jti`, `iat`, `nbf`, `exp`, `iss`, `aud` | Standart claim'ler |

## Sözleşme kuralları
- **Taban yol:** `/api/v1/...`. Kırıcı değişiklik yeni sürümle (`v2`) yapılır.
- **Kimlik doğrulama:** `Authorization: Bearer <access_token>`. İstekte `agencyId` header'ı veya query parametresi gönderilse de yok sayılır (testli).
- **İsimlendirme:** JSON `camelCase`, tarihler ISO 8601, ID'ler GUID, enum'lar string (`"Active"`).
- **Fazladan JSON alanları yok sayılır.** Body'de `agencyId` gönderilse bile kayıt çağıranın ajansına yazılır (testli).
- **Eşzamanlılık:** güncellemelerde şimdilik son yazan kazanır (teknik borç 9).
- **Entity'ler asla dönmez** (testli); her endpoint'in kendi DTO'su vardır.
- **Sayfalama:** `?page=1&pageSize=20` (en fazla 100). Yanıt: `{ items, page, pageSize, totalCount, totalPages }`.
- **Uzun işler:** `202 Accepted` + `Location: /api/v1/jobs/{id}`.

## Hata formatı (RFC 9457)
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Validation failed",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "errors": { "Email": ["'Email' must not be empty."] },
  "traceId": "00-…"
}
```

| Durum | Anlamı |
|---|---|
| 400 | Doğrulama hatası (`errors` alanı ile), bozuk JSON, hatalı query parametresi |
| 401 | Kimlik doğrulanmadı. Anonim istemciler bilinmeyen route'larda da 401 alır (route keşfine karşı). |
| 403 | Yetki yok veya tenant ihlali (detay verilmez) |
| 404 | Kaynak yok **veya başka bir ajansa ait** |
| 409 | Çakışma |
| 422 | İş kuralı ihlali |
| 429 | Rate limit aşıldı |
| 500 | Beklenmeyen hata; destek için `traceId` iletilir |
