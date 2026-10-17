using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading.Tasks;

namespace PhotoOrganizer.Admin.ViewModels
{
    public partial class UsersViewModel : ObservableObject
    {
        private readonly AdminRepository _adminRepo = new();
        public ObservableCollection<AppUser> Users { get; } = new();
        public ObservableCollection<DbSession> Sessions { get; } = new();

        [ObservableProperty] private AppUser? _selectedUser;
        [ObservableProperty] private DbSession? _selectedSession;
        [ObservableProperty] private string _statusText = "";

        [RelayCommand]
        private async Task LoadAsync()
        {
            Users.Clear();
            foreach (var u in await _adminRepo.GetAllUsersAsync()) Users.Add(u);
            Sessions.Clear();
            foreach (var s in await _adminRepo.GetActiveSessionsAsync()) Sessions.Add(s);
        }

        [RelayCommand]
        private async Task RegisterUserAsync()
        {
            var login = $"user_{DateTime.Now:HHmmss}";
            try
            {
                var id = await _adminRepo.RegisterUserAsync(login, "DefaultPass123");
                StatusText = $"Зарегистрирован {login} (id={id})";
            }
            catch (Exception ex) { StatusText = $"Ошибка: {ex.Message}"; }
            await LoadAsync();
        }

        [RelayCommand]
        private async Task DeleteUserAsync()
        {
            if (SelectedUser is null) return;
            try
            {
                await _adminRepo.DeleteUserAsync(SelectedUser.Id, DeleteUserMode.Transfer, 1);
                StatusText = $"Пользователь {SelectedUser.Login} удалён";
            }
            catch (Exception ex) { StatusText = $"Ошибка: {ex.Message}"; }
            await LoadAsync();
        }

        [RelayCommand]
        private async Task ToggleUserActiveAsync()
        {
            if (SelectedUser is null) return;
            // Пример: смена статуса (требует процедуры в БД)
            StatusText = "Функция в разработке";
        }

        [RelayCommand]
        private async Task TerminateSessionAsync()
        {
            if (SelectedSession is null) return;
            await _adminRepo.TerminateSessionAsync(SelectedSession.Pid);
            StatusText = $"Сессия PID {SelectedSession.Pid} завершена";
            await LoadAsync();
        }
    }
}
