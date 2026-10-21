  # StorageConnectionStabilityTest (SCSTest)

>  **Ana Odak:** Bu uygulama diskin veya SSD'nin donanımsal ömrünü değil; diskin bağlı olduğu **USB kutularının (Enclosure), SATA/NVMe dönüştürücülerin, Dock Station cihazlarının ve bağlantı kablolarının** yük altındaki bağlantı kararlılığını (stability) ve kesinti durumlarını test eder.

---

### Hızlı İndirme

[ Tek Dosya (.exe) İndir — v0.3.0-alpha](https://github.com/yavuz-caner/StorageConnectionStabilityTest/releases/latest/download/StorageConnectionStabilityTest.exe)

---

### Kullanım Amacı ve Çözdüğü Problem

Teknik servis süreçlerinde depolama birimlerinde yaşanan kopmaların veya yavaşlamaların büyük bir kısmı diskin kendisinden değil; kullanılan **USB bridge çipinden, dönüştürücü karttan veya kalitesiz/hasarlı kablolardan** kaynaklanır. 

**SCSTest (Storage Connection Stability Test)**, bu donanım bileşenlerini yük altına sokarak anlık voltaj düşüşlerini, bağlantı kopmalarını ve köprü çip (bridge controller) hatalarını milisaniye hassasiyetinde tespit eder.

---

### Öne Çıkan Özellikler

* **Köprü (Bridge) ve Controller Tespiti:** WMI altyapısı ile bağlı olan dönüştürücünün VID, PID, PNP Device ID ve desteklenen durumlarda köprü çip (JMicron, ASMedia, Realtek vb.) bilgilerini rapora işler.
* **Milisaniyeli Canlı Cihaz İzleme:** Test sırasında Windows cihaz takılma/çıkarılma (Device Arrival/Removal) olaylarını anlık olarak dinler ve kopma anını tam zaman damgasıyla kaydeder.
* **Kesinti & I/O Hata Tespiti:** Yük altındaki veri transferi sırasında oluşan anlık bağlantı kopmalarını ve I/O hatalarını anında yakalar.
* **Taşınabilir (Portable) Yapı:** Herhangi bir `.NET Runtime` veya kurulum gerektirmeyen, doğrudan çalıştırılabilir tek parça `.exe` mimarisi.

---

### Nasıl Kullanılır?

1. İndirilen `StorageConnectionStabilityTest.exe` dosyasını çalıştırın (Yönetici hakları önerilir).
2. Test etmek istediğiniz USB kutusu veya dönüştürücüye bağlı sürücüyü seçin.
3. Test parametrelerini belirleyip **Testi Başlat** butonuna basın.
4. Test bitiminde veya kopma durumunda oluşan detaylı raporu inceleyin.

---

### Geliştirme Yol Haritası (Roadmap)

- [x] **Aşama 1:** Temel I/O ve Yazma Hızı Testi (`v0.1.0-alpha`)
- [x] **Aşama 2:** Canlı Cihaz İzleme ve Milisaniyeli Olay Kaydı (`v0.2.0-alpha`)
- [x] **Aşama 3:** WMI ile USB Controller, VID/PID ve Bridge Bilgisi Raporlama (`v0.3.0-alpha`)
- [ ] **Aşama 4:** Kesintisiz Sürekli Okuma Testi Modülü
- [ ] **Aşama 5:** Döngüsel Sürekli Yazma/Silme Testi Modülü (Diski doldurmadan yük testi)
- [ ] **Aşama 6:** Birleşik Servis Dashboard Arayüzü & Laravel ERP Entegrasyonu

---
*Geliştirici:* **Yavuz Caner** — *Teknik Servis & Depolama Bağlantı Teşhis Aracı*
