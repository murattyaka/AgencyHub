# SECURITY_CHECKLIST

Durum: ✅ uygulandı ve testli · 🟡 uygulandı, test eksik · ⏳ bekliyor · ⚠️ dikkat gerektiren kural

## 1. Multi-tenant güvenliği
- ✅ Tenant yalnızca doğrulanmış token claim'inden alınır; header/query manipülasyonu yok sayılır (`TenantResolutionMiddleware` testleri).
- ✅ Geçersiz veya boş tenant claim'i reddedilir.
- ✅ Scope içinde tenant değiştirilemez.
- ✅ Okuma izolasyonu: tenant filtresi, ID ile erişim (IDOR) ve `Find` senaryoları testli.
- ✅ Yazma izolasyonu: yabancı AgencyId ile ekleme, başka tenant'ın satırını güncelleme/silme ve AgencyId değiştirme engellenir.
- ✅ Tenant bağlamı olmadan tenant'a ait veriye yazılamaz.
- ✅ Her DbContext `TenantAwareDbContext`'ten türemek zorunda (mimari test).
- ✅ Endpoint seviyesinde cross-tenant testleri (Client/Project/Website): başka ajansın kaydına ID ile okuma, güncelleme veya silme → 404 ve veri değişmez; listelerde görünmez; başka ajansın üst kaydına alt kayıt bağlanamaz (422, rastgele ID ile aynı yanıt); body'deki `agencyId` yok sayılır. Filtre kapatılınca 11/11 test kırılıyor (mutasyon kontrolü).
- ✅ Agency kaynağında route'ta ID yok; erişim yalnızca token'daki ajansa (`/api/v1/agency`).
- ✅ Rol matrisi (Viewer/Member/Admin/Owner × GET/POST/PUT/DELETE) testli.
- ⏳ Arka plan job'larında tenant bağlamının taşınması (Sprint 2).
- ✅ İmzalı token içindeki `agency_id` değiştirilirse imza geçersiz olur ve 401 döner (testli).
- ✅ Doğru imzalı ama `agency_id` claim'i olmayan token fallback policy'de 403 alır (testli).
- ⚠️ `IgnoreQueryFilters(["Tenant"])` yalnızca platform yöneticisi servislerinde kullanılabilir. Her kullanım code review'da gerekçelendirilmeli ve `SECURITY REVIEWED` yorumuyla işaretlenmelidir. Mevcut kullanımlar:
  - `AuthService.MembershipsOfUser`: login ve refresh'te henüz tenant yok; sorgu, kimliğini kanıtlamış kullanıcının kendi üyelikleriyle sınırlı.
- ⚠️ `ExecuteUpdate` / `ExecuteDelete` / ham SQL interceptor'ları atlar. Bunlarla **AgencyId asla set edilmez**; ham SQL'de tenant koşulu elle eklenir ve review edilir.

## 2. Kimlik ve oturum
- ✅ JWT: 15 dk access token; `iss`, `aud`, `exp`, imza ve algoritma (yalnızca HS256) doğrulanıyor. Sahte anahtar, `alg=none`, süresi dolmuş ve yanlış audience token'lar 401 (testli).
- ✅ JWT ayarları açılışta doğrulanıyor: anahtar ≥32 byte, `DEV-ONLY-` anahtarlar Development dışında reddediliyor, ömür sınırları (testli).
- ✅ Refresh token: 256-bit rastgele, yalnızca SHA-256 hash'i saklanıyor, tek kullanımlık ve rotasyonlu. Yeniden kullanımda ailenin tamamı iptal ediliyor (testli + mutasyon kontrolü). Paralel refresh'ler concurrency token ile engelleniyor.
- ✅ Refresh token `HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth` cookie'de; yanıt gövdesinde yok (testli).
- ✅ Logout ailenin tamamını iptal ediyor ve cookie'yi siliyor (testli).
- ✅ Refresh sırasında kullanıcı ve üyelik yeniden doğrulanıyor; pasif üyelik → 401 (testli).
- ✅ Parola: PBKDF2 (ASP.NET Core PasswordHasher v3), en az 12 en fazla 128 karakter, email ile aynı olamaz. Hash gerektiğinde login sırasında yenileniyor.
- ✅ Kullanıcı tespitine karşı: bilinmeyen kullanıcı, yanlış parola ve kilitli hesap için aynı 401 yanıtı; bilinmeyen kullanıcıda da hash maliyeti ödeniyor (testli: aynı yanıt; zamanlama ölçülmedi).
- ✅ Brute-force: 5 hatalı deneme → 15 dk kilit (testli). `authentication` rate limit 5/dk/IP → 429 + `Retry-After` (testli).
- ✅ Fallback policy: anonim olmayan her endpoint, ajansa bağlı ve kimliği doğrulanmış kullanıcı gerektirir. Anonim endpoint'ler allow-list testiyle sabit.
- ⚠️ Kilitleme, saldırganın kullanıcıyı kilitlemesine (DoS) izin verir. IP rate limit ile sınırlı; CAPTCHA veya artan gecikme değerlendirilecek.
- ⏳ Refresh token ailesi için mutlak oturum ömrü (ör. 30 gün).
- ⏳ JWT anahtar rotasyonu / asimetrik imza (ADR-012).
- ⏳ MFA (TOTP), Sprint 2.
- ⏳ Login denemeleri ve oturum olayları için Audit Log modülü (şu an yalnızca uygulama logu).

## 3. Secret yönetimi
- ✅ Repoda secret yok. Geliştirmede `dotnet user-secrets` (UserSecretsId tanımlı).
- ⏳ Production: ortam değişkeni veya korumalı konfigürasyon; connection string'de Windows Authentication tercih edilir.
- ⏳ OAuth access/refresh token'ları ASP.NET Data Protection ile şifreli saklanır; anahtar halkası DPAPI-NG ile korunur ve yedeklenir.
- ✅ Loglama: request body ve query string loglanmaz. Login, refresh ve logout akışında parola, refresh token ve access token hiçbir log olayında görünmüyor (testli: `SensitiveDataLoggingTests`).
- ✅ JWT imza anahtarı Production'da `Jwt__SigningKey` ortam değişkeninden okunuyor; repodaki tek anahtar `DEV-ONLY-` önekli ve Production'da reddediliyor.
- ⏳ Serilog'a hassas alan maskeleme policy'si (password, token, secret, apikey, authorization); derinlemesine savunma için.

## 4. SSRF (Website, SEO, Uptime, Security, Performance modülleri)
- ✅ **Katman 1 – kayıt anında URL politikası** (`WebsiteUrlPolicy`, ADR-016). Testli (30 unit + 10 integration vaka):
  - Yalnızca http/https; standart port; kimlik bilgisi ve query yok.
  - IP literal yok: ondalık (`2130706433`), hex (`0x7f000001`), IPv6 ve IPv4-mapped IPv6 dahil.
  - `localhost` ve iç ağ alan adları yok; tek etiketli adlar yok.
- ⚠️ Katman 1, herkese açık bir adın iç IP'ye çözülmesini (`127.0.0.1.nip.io`) ve DNS rebinding'i **engelleyemez**. Katman 2 zorunludur.
- ⏳ Ajans kaydı (anonim): CAPTCHA ve email doğrulaması yok. Kayıt varsayılan olarak kapalı (ADR-017).
- ⏳ Tek bir `SafeHttpClient` dışında dış dünyaya HTTP isteği yapılmaz (mimari testle zorlanacak).
- ⏳ IP doğrulaması `SocketsHttpHandler.ConnectCallback` içinde, **bağlanılan** IP üzerinde yapılır (DNS rebinding'e karşı).
- ⏳ Engellenecek aralıklar: loopback, RFC1918, link-local (169.254.0.0/16, cloud metadata dahil), CGNAT 100.64/10, 0.0.0.0/8, multicast, IPv6 `::1`, `fc00::/7`, `fe80::/10`, IPv4-mapped IPv6.
- ⏳ Otomatik redirect kapalı; her adım yeniden doğrulanır, en fazla 5 redirect.
- ⏳ Yalnızca `http`/`https`, port 80/443 (ek portlar beyaz listeyle), yanıt boyutu ve süre limiti.
- ⏳ Playwright/Lighthouse tarayıcı trafiği filtreli bir egress proxy üzerinden geçer (alt kaynaklar da kontrol edilir).
- ⏳ SSRF test seti: `127.0.0.1`, `localhost`, `[::1]`, `169.254.169.254`, `0x7f000001`, ondalık IP, DNS rebinding, iç adrese 302.

## 5. Web güvenliği (OWASP Top 10)
- ✅ Merkezi hata yönetimi: 500 yanıtında mesaj ve stack trace yok (testli).
- ✅ Güvenlik header'ları: nosniff, `X-Frame-Options: DENY`, CSP `default-src 'none'`, `Referrer-Policy`, `Permissions-Policy`, COOP/CORP (testli).
- ✅ `Server` header'ı gizli (Kestrel testli; IIS için `web.config` ayarı DEPLOYMENT_GUIDE'da).
- ✅ HTTPS yönlendirme (Production testli). 🟡 HSTS yapılandırıldı, otomatik testi yok (localhost hariç tutulur).
- ✅ OpenAPI yalnızca Development'ta açık (testli).
- ✅ CORS: varsayılan boş liste, yalnızca konfigürasyondaki origin'ler.
- ✅ Rate limiting: global 300/dk/IP ve auth 5/dk/IP; değerler konfigürasyondan okunuyor (auth testli).
- ✅ Yetkilendirme: fallback policy aktif; anonim istemciler bilinmeyen route'larda da 401 alır (route keşfine karşı).
- ✅ Hiçbir endpoint persistence entity'si döndürmüyor (çalışan uygulamanın endpoint tablosu üzerinden testli).
- ⏳ SQL Injection: yalnızca EF Core/parametreli sorgular; `FromSqlRaw` string birleştirme yasak.
- ⏳ XSS: API yalnızca JSON döner; Angular varsayılan sanitization; `bypassSecurityTrust*` yasak.
- ✅ CSRF: API çağrıları Bearer header ile yapılır (cookie ile değil). Cookie kullanan tek yüzey `/api/v1/auth/refresh|logout` ve bunlar `SameSite=Strict` + dar path ile korunur. Cross-site bir sayfa yanıtı okuyamaz (CORS, origin'ler açıkça listeli).
- ⏳ Dosya yükleme (rapor logosu vb.): boyut limiti, uzantı + magic number kontrolü, web kökü dışında saklama.
- ✅ Mass assignment: yalnızca request DTO'ları bind edilir; DTO'larda `AgencyId` yok, fazladan JSON alanları yok sayılır (testli). Entity'ler API'den dönmez (testli).
- ✅ Bozuk JSON ve hatalı parametreler 400 döner (500 değil; stack trace sızmaz).
- ✅ Liste endpoint'lerinde sayfa boyutu en fazla 100, arama en fazla 100 karakter.

## 6. AI modülü
- ⏳ AI yalnızca öneri üretir; uygulama kullanıcı onayıyla ve ayrı bir yetkiyle yapılır.
- ⏳ Prompt injection: taranan sayfa içeriği güvenilmeyen veri olarak işaretlenir; AI çıktısı hiçbir zaman otomatik aksiyona dönüşmez.
- ⏳ Müşteri verisinin harici sağlayıcıya gönderimi için KVKK değerlendirmesi (P-2).

## 7. Production
- ⏳ Hangfire dashboard yalnızca platform admin rolüne açık.
- ⏳ `AllowedHosts` gerçek hostname ile doldurulur (şu an placeholder).
- ⏳ Bağımlılık güvenlik taraması (`dotnet list package --vulnerable`) CI'da çalışır.
