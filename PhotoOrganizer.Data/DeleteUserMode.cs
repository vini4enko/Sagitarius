namespace PhotoOrganizer.Data;

/// <summary>
/// Стратегия обработки фотографий при удалении пользователя.
/// </summary>
public enum DeleteUserMode
{
    /// <summary>Удалить фотографии вместе с пользователем (конфиденциальность).</summary>
    DeletePhotos,

    /// <summary>Передать права просмотра другому пользователю (требует TargetUserId).</summary>
    Transfer,

    /// <summary>Сделать фотографии публичными (видимыми всем app_user).</summary>
    Unrestrict
}