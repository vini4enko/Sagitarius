using CommunityToolkit.Mvvm.ComponentModel;
using PhotoOrganizer.Admin.Services;

namespace PhotoOrganizer.Admin.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public UsersViewModel Users { get; }
    public AuditViewModel Audit { get; }
    public IntegrityViewModel Integrity { get; }
    public ExtraChecksViewModel ExtraChecks { get; }

    public MainWindowViewModel(
        UsersViewModel users, AuditViewModel audit,
        IntegrityViewModel integrity, ExtraChecksViewModel extra)
    {
        Users = users; Audit = audit;
        Integrity = integrity; ExtraChecks = extra;
    }
}