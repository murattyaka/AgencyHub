# ACCEPTANCE_CRITERIA

## Genel "Tamamlandı" tanımı (her modül için)
1. Kod tamamlandı ve review edildi.
2. `dotnet build` uyarısız ve hatasız.
3. Unit, integration ve API testleri geçiyor.
4. Yetkilendirme testleri: anonim → 401, yetkisiz rol → 403.
5. Tenant izolasyon testleri: başka ajansın kaynağına liste, detay, güncelleme ve silme → 404 / etkisiz.
6. Hata senaryoları test edildi (doğrulama, bulunamadı, çakışma, iptal).
7. Uzun süren işlemler `CancellationToken` destekliyor.
8. PROJECT_STATUS, CHANGELOG, API_DOCUMENTATION ve DATABASE_SCHEMA güncel.

---

## Sprint 1

| # | Kriter | Durum | Kanıt |
|---|---|---|---|
| AC-1 | Build uyarısız, testler %100 başarılı | ✅ | 184/184, 0 uyarı (2026-11-11) |
| AC-2 | Tenant filtresi: ajans yalnızca kendi verisini görür; tenant yoksa veri dönmez | ✅ | `TenantQueryFilterTests` (SQLite) + Identity üyelik sorguları (SQL Server) |
| AC-3 | Yazma koruması | ✅ | `TenantSaveChangesInterceptorTests`, `IdentityProvisioningTests` |
| AC-4 | İstekte gönderilen tenant bilgisi (header, query, token payload) yok sayılır veya reddedilir | ✅ | `TenantResolutionMiddlewareTests`, `Me_returns_user_of_the_token_and_ignores_tenant_headers`, `Changing_the_agency_claim_invalidates_the_signature`, `Agency_id_in_the_request_body_is_ignored` |
| AC-5 | A ajansı kullanıcısı B ajansının kaynağına ID ile eriştiğinde 404 alır (tüm CRUD endpoint'leri) | ✅ | `CrossTenantIsolationTests` (11) + mutasyon kontrolü |
| AC-6 | Hiçbir endpoint entity döndürmez | ✅ | `EndpointSecurityTests.No_endpoint_exposes_a_persistence_entity` |
| AC-7 | Refresh token tek kullanımlık; yeniden kullanımda aile iptal edilir | ✅ | `Reusing_a_rotated_refresh_token_revokes_the_whole_family` + mutasyon kontrolü |
| AC-8 | Login 5 deneme/dk sonrasında 429 + `Retry-After` döner | ✅ | `Login_is_rate_limited_per_client_with_retry_after` |
| AC-9 | Geçersiz girdi → 400; 500'de bilgi sızmaz | ✅ | `GlobalExceptionHandlerTests`, `Login_with_missing_fields_returns_validation_problem` |
| AC-10 | Loglarda parola veya token yok | ✅ | `SensitiveDataLoggingTests` |
| AC-11 | Health endpoint'leri çalışır (ready → DB) | ✅ | `PipelineTests` |
| AC-12 | Migration'lar boş bir veritabanına sorunsuz uygulanır | ✅ | Her test koşusu boş `AgencyHub_Test_*` veritabanına `InitialIdentity` uyguluyor |
| AC-13 | Güvenlik header'ları; OpenAPI production'da yok; HTTP → HTTPS | ✅ | `PipelineTests` |
| AC-14 | Mimari kurallar | ✅ | `LayeringTests` (7) |
| AC-15 | Angular: login + Client/Website listeleri | ⛔ | Node.js kurulumu bekleniyor |
| AC-16 | PROJECT_STATUS ve CHANGELOG güncel | ✅ | 2026-11-11 |
| AC-17 | Yalnızca allow-list'teki endpoint'ler anonim | ✅ | `EndpointSecurityTests.Only_allow_listed_endpoints_are_anonymous` |
| AC-18 | Hesap 5 hatalı denemede kilitlenir, süre dolunca açılır | ✅ | `BruteForceProtectionTests` |
| AC-19 | Sahte, değiştirilmiş, süresi dolmuş ve `alg=none` token'lar reddedilir | ✅ | `AccessTokenValidationTests` (6) |
| AC-20 | Ajans kaydı: Owner oluşturulur ve giriş yapabilir; hata durumunda ajans geri alınır; kayıt kapalıyken 404 | ✅ | `AgencyRegistrationTests` |
| AC-21 | Client → Project → Website CRUD, sayfalama (≤100), arama ve filtre; alt kaydı olan üst kayıt silinemez (409) | ✅ | `PortfolioLifecycleTests` |
| AC-22 | Rol matrisi (Viewer/Member/Admin/Owner) | ✅ | `RoleAuthorizationTests` (16 kombinasyon + anonim) |
| AC-23 | Güvensiz website URL'leri (localhost, iç IP, metadata, özel port, kimlik bilgisi, iç alan adları) reddedilir | ✅ | `WebsiteUrlPolicyTests` (30), `InputValidationTests` |
| AC-24 | Bozuk JSON ve hatalı query parametreleri 400 döner | ✅ | `InputValidationTests` |
| AC-25 | Modül sınırları: modüller birbirine yalnızca Contracts üzerinden erişir; iç tipler internal | ✅ | `LayeringTests` (modül başına) |
