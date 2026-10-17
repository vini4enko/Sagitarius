using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace PhotoOrganizer.Admin.Services
{
    public class IntegrityService
    {
        private readonly S3StorageService _s3;
        private readonly AdminRepository _adminRepo;

        public IntegrityService(S3StorageService s3, AdminRepository adminRepo)
        { _s3 = s3; _adminRepo = adminRepo; }

        public async Task<IntegrityReport> CheckAsync()
        {
            var report = new IntegrityReport();

            // 1. Все ключи из S3
            var s3Keys = await _s3.ListAllKeysAsync();

            // 2. Все ключи из БД
            var dbKeys = await _adminRepo.GetAllPhotoObjectKeysAsync();

            // 3. S3 без записи в БД
            var dbSet = dbKeys.ToHashSet();
            report.OrphanedS3Keys = s3Keys.Where(k => !dbSet.Contains(k)).ToList();

            // 4. Записи БД без объекта в S3
            var s3Set = s3Keys.ToHashSet();
            report.PhotosWithoutObject = (await _adminRepo.GetPhotoRecordsAsync())
                .Where(p => !s3Set.Contains(p.ObjectKey)).ToList();

            // 5. (Опционально) show_photos с отсутствующими photo_id — FK не даст, но проверим.
            //    Если нужно, можно выполнить запрос: SELECT sp.* FROM show_photos sp
            //    LEFT JOIN photos p ON p.id = sp.photo_id WHERE p.id IS NULL;

            return report;
        }
    }
}
