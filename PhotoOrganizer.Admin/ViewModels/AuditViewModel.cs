using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;

namespace PhotoOrganizer.Admin.ViewModels
{
    public partial class AuditViewModel : ObservableObject
    {
        private readonly AdminRepository _adminRepo = new();
        public ObservableCollection<AuditEntry> Entries { get; } = new();
        [ObservableProperty] private bool _isAuditEnabled;
        [ObservableProperty] private string? _filterTable;
        [ObservableProperty] private string _statusText = "";

        [RelayCommand]
        private async Task LoadAsync()
        {
            IsAuditEnabled = await _adminRepo.IsAuditEnabledAsync();
            Entries.Clear();
            foreach (var e in await _adminRepo.GetAuditLogAsync(FilterTable, null, null))
                Entries.Add(e);
        }

        [RelayCommand]
        private async Task ToggleAuditAsync()
        {
            await _adminRepo.SetAuditEnabledAsync(!IsAuditEnabled);
            IsAuditEnabled = !IsAuditEnabled;
            StatusText = $"Аудит {(IsAuditEnabled ? "включён" : "отключён")}";
        }
    }
}
