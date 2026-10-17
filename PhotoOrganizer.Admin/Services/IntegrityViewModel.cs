using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;

namespace PhotoOrganizer.Admin.Services
{
    public partial class IntegrityViewModel : ObservableObject
    {
        private readonly IntegrityService _service;
        public ObservableCollection<string> OrphanedS3 { get; } = new();
        public ObservableCollection<PhotoRecordInfo> MissingInS3 { get; } = new();
        [ObservableProperty] private string _statusText = "";

        public IntegrityViewModel(IntegrityService service) { _service = service; }

        [RelayCommand]
        private async Task CheckAsync()
        {
            var report = await _service.CheckAsync();
            OrphanedS3.Clear();
            foreach (var k in report.OrphanedS3Keys) OrphanedS3.Add(k);
            MissingInS3.Clear();
            foreach (var p in report.PhotosWithoutObject) MissingInS3.Add(p);
            StatusText = $"S3-сирот: {OrphanedS3.Count}, БД без файла: {MissingInS3.Count}";
        }

        [RelayCommand]
        private async Task DeleteOrphanedS3Async()
        {
            var s3 = new S3StorageService(...); // передать из DI
            await s3.DeleteManyAsync(OrphanedS3);
            OrphanedS3.Clear();
            StatusText = "S3-сироты удалены";
        }

        [RelayCommand]
        private async Task ExportOrphanedS3Async()
        {
            // Использовать AvaloniaFolderPicker для выбора папки,
            // затем для каждого ключа скачать и сохранить.
        }

        [RelayCommand]
        private async Task DeleteMissingDbRecordsAsync()
        {
            var admin = new AdminRepository();
            foreach (var p in MissingInS3)
                await admin.DeletePhotoRecordAsync(p.Id);
            MissingInS3.Clear();
            StatusText = "Записи без объектов удалены из БД";
        }
    }
}
