# PROJECT_ROADMAP

Sprintler 2 haftalıktır. Tarihler planlama varsayımıdır; ekip kapasitesi belirlenince güncellenecek.

| Sprint | Hedef | Modüller / işler | Çıkış kriteri |
|---|---|---|---|
| **1** | Temel altyapı + çekirdek hiyerarşi | Solution iskeleti, tenant altyapısı, hata yönetimi ve loglama, Identity (JWT + refresh), Agency, Client, Project, Website CRUD, Angular iskeleti, CI | ACCEPTANCE_CRITERIA → Sprint 1 |
| **2** | Arka plan altyapısı + güvenli dış erişim | Hangfire (SQL storage) + Worker, job'larda tenant bağlamı, `SafeHttpClient` (SSRF), Redis, MFA, Audit Logging modülü | SSRF test seti yeşil, tenant'lı job testleri |
| **3** | İzleme | Uptime scheduler (P-3), SSL sertifika ve DNS takibi, Notification (e-posta, webhook), veri saklama ve özet tabloları | 1.000 sitelik yük testi |
| **4** | SEO + İçerik | SEO crawler (robots, sitemap, meta, kırık link, canonical), Content Audit | Crawler limitleri ve iptal testleri |
| **5** | Performans + Güvenlik taraması | Lighthouse/Playwright worker'ı, güvenlik başlıkları ve TLS analizi, pasif güvenlik kontrolleri | Eşzamanlılık limitleri, egress proxy |
| **6** | Entegrasyonlar | Google OAuth (GA4, Search Console), Microsoft Clarity, token şifreleme ve yenileme | Token yenileme hata senaryoları |
| **7** | İş yönetimi + Raporlama | Task Management, zamanlanmış PDF/HTML raporlar | Rapor üretimi worker'da |
| **8** | AI Öneri Motoru | Öneri modeli (problem, veri, çözüm, öncelik, etki, güven, URL, tarih), geçmiş takibi, onay akışı | AI hiçbir aksiyonu onaysız uygulamaz (test) |
| **9** | Sertleştirme | 10.000 site yük testi, pen-test, yedekleme/restore tatbikatı, production deploy | DEPLOYMENT_GUIDE checklist'i tamam |

## Sprint 1 ayrıntı
Bkz. PROJECT_STATUS → Sprint 1 iş durumu (S1-01 … S1-09).

## Bağımlılıklar
- Sprint 3–6, Sprint 2'deki `SafeHttpClient` ve Worker altyapısına bağlıdır.
- Sprint 8 (AI), Sprint 3–6'da biriken veriye ve P-2 kararına bağlıdır.
