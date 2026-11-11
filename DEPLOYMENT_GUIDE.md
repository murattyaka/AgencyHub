# DEPLOYMENT_GUIDE

> Durum: **Taslak. Henüz bir sunucuda uygulanıp doğrulanmadı.** İlk deploy'da adımlar test edilip güncellenecek.

## 1. Sunucu ön koşulları (Windows Server)
- IIS + **ASP.NET Core 10 Hosting Bundle** (runtime 10.0.x; kurulumdan sonra `iisreset`).
- .NET 10 Runtime (Worker için).
- SQL Server (Standard önerilir, P-4), Redis eşleniği (P-1).
- Sprint 5'ten itibaren Node.js LTS + Chrome (Lighthouse/Playwright).
- Geçerli TLS sertifikası.

## 2. Build
```powershell
dotnet test --solution AgencyHub.slnx
dotnet publish src/Hosts/AgencyHub.Api    -c Release -o out/api
dotnet publish src/Hosts/AgencyHub.Worker -c Release -o out/worker
```

## 3. Ortam değişkenleri / secret'lar
| Değişken | Hedef | Not |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` / `DOTNET_ENVIRONMENT` | `Production` | |
| `AGENCYHUB_LOG_DIR` | ör. `D:\Logs\AgencyHub` | App pool kimliğine **Modify** izni verilmeli |
| `ConnectionStrings__Default` | | Windows Authentication tercih edilir; parola gerekiyorsa repoya yazılmaz |
| `Jwt__SigningKey` | ≥32 byte rastgele değer | **Zorunlu.** Eksik, kısa veya `DEV-ONLY-` önekliyse uygulama açılmaz. Üretmek için: `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))` |
| `Jwt__Issuer` / `Jwt__Audience` | ör. `agencyhub-api` / `agencyhub-web` | Varsayılanlar appsettings'te |
| `RateLimiting__AuthenticationPermitLimitPerMinute` | 5 | Login, refresh, logout ve ajans kaydı için IP başına |
| `AgencyRegistration__Enabled` | `false` (varsayılan) | Self-service ajans kaydı; P-7 kararı verilene kadar kapalı tutulmalı |
| `AllowedHosts` | gerçek hostname | `appsettings.Production.json` içindeki placeholder değiştirilmeli |
| `Cors__AllowedOrigins__0` | Angular sitesi origin'i | |

IIS'te ortam değişkenleri `web.config` → `<environmentVariables>` veya `appcmd` ile verilir. Secret'lar repoda tutulmaz.

## 4. IIS (API)
- Application Pool: **No Managed Code**, Integrated pipeline, kimlik `ApplicationPoolIdentity` (veya ayrı servis hesabı).
- Hosting modeli: in-process (varsayılan).
- `web.config`: `<security><requestFiltering removeServerHeader="true" /></security>` ve `X-Powered-By` kaldırılır.
- Yalnızca HTTPS binding'i; HTTP binding'i yönlendirme için kalabilir (uygulama HTTPS'e yönlendirir ve HSTS gönderir).
- Klasör izinleri: uygulama klasörü yalnızca okuma, log klasörü Modify.

## 5. Worker (Windows Service)
```powershell
sc.exe create "AgencyHub Worker" binPath= "D:\Apps\AgencyHub\worker\AgencyHub.Worker.exe" start= delayed-auto
sc.exe failure "AgencyHub Worker" reset= 86400 actions= restart/60000/restart/60000/restart/300000
```
Servis hesabı: en az yetkili servis hesabı. Ortam değişkenleri servis için tanımlanır.

## 6. Veritabanı
- Migration'lar `dotnet ef migrations bundle` ile üretilir ve deploy öncesi **yedek alındıktan sonra** çalıştırılır. Uygulama açılışında otomatik migration yapılmaz. Modül başına bir bundle üretilir:
  ```powershell
  dotnet tool restore
  dotnet ef migrations bundle --project src/Modules/Identity/AgencyHub.Modules.Identity --startup-project src/Modules/Identity/AgencyHub.Modules.Identity -o out/migrate-identity.exe
  out/migrate-identity.exe --connection "<production connection string>"
  ```
- Modüller: `Identity`, `Agency`, `Client`, `Project`, `Website`. Her biri için ayrı bir bundle üretilir (yukarıdaki komutta modül adını değiştirin).
- Geliştirme ortamında tüm modüller için: `./scripts/update-database.ps1` (`.\SQLEXPRESS` üzerinde `AgencyHub` veritabanı).
- Uygulama hesabına yalnızca `db_datareader`, `db_datawriter` ve gerekli şemalarda EXECUTE yetkisi verilir. DDL yetkisi yalnızca migration hesabında olur.
- Yedekleme: günlük full, saatlik log yedeği (Express'te SQL Agent olmadığından Task Scheduler gerekir).
- **Restore testi** ayda bir yapılır ve sonucu kaydedilir.

## 7. Production öncesi kontrol listesi
- [ ] `ASPNETCORE_ENVIRONMENT=Production`, debug kapalı
- [ ] `AllowedHosts` gerçek hostname
- [ ] Connection string güvenli (repoda değil)
- [ ] `Jwt__SigningKey` ortam değişkeni tanımlı, production'a özgü ve yedeklenmiş (kaybolursa tüm oturumlar düşer)
- [ ] `Cors__AllowedOrigins` yalnızca Angular sitesinin origin'i; Angular ve API aynı site altında (ADR-011)
- [ ] OpenAPI kapalı (otomatik testle doğrulanıyor)
- [ ] HTTPS zorunlu, HSTS aktif (tarayıcıdan doğrulanmalı)
- [ ] `Server` / `X-Powered-By` header'ları yok
- [ ] Log klasörü izinleri ve 30 günlük rotasyon
- [ ] `/health/live` ve `/health/ready` izleniyor, alarm tanımlı
- [ ] Hangfire dashboard yalnızca platform admin rolüne açık (Sprint 2)
- [ ] Redis bağlantısı ve kimlik doğrulaması (Sprint 2)
- [ ] Yedek alındı, restore testi yapıldı
- [ ] Data Protection anahtar halkası kalıcı klasörde ve yedekli

## 8. Geri alma
Önceki publish klasörü saklanır. IIS fiziksel yolu önceki sürüme çevrilir ve servis önceki binary ile yeniden başlatılır. Migration geri alımı için yalnızca önceden test edilmiş down script'leri kullanılır, aksi halde yedekten dönülür.
