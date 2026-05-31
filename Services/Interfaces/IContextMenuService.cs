namespace ScreenShotManager.Services.Interfaces;

/// <summary>
/// Registers/removes the "Downscale Video" entry in the Windows Explorer right-click menu
/// for the supported video extensions (per-user, HKCU — no admin required).
/// </summary>
public interface IContextMenuService
{
    /// <summary>True when the verb is already registered for all supported extensions.</summary>
    bool IsRegistered();

    /// <summary>Registers the verb for every supported extension. Idempotent.</summary>
    void Register();

    /// <summary>Removes the verb for every supported extension. Idempotent.</summary>
    void Unregister();
}
