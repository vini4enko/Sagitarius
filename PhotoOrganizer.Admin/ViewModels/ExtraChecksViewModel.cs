using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;

namespace PhotoOrganizer.Admin.ViewModels
{
    public partial class ExtraChecksViewModel : ObservableObject
    {
        private readonly AdminRepository _adminRepo = new();
        public ObservableCollection<ProblemItem> Problems { get; } = new();
        [ObservableProperty] private string _statusText = "";

        [RelayCommand]
        private async Task CheckOrphanedAttributesAsync()
        {
            // SQL: SELECT a.id FROM attributes a LEFT JOIN photos p ON p.id = a.photo_id WHERE p.id IS NULL
        }
        // ... аналогично для остальных проверок
    }
}
