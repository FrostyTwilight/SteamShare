namespace SteamShare.Core.Tasks;

/// <summary>
/// Categorizes a <see cref="SteamTask"/> by its primary operation type.
/// </summary>
public enum TaskCategory
{
    /// <summary>Uploading files or file groups to Steam Workshop.</summary>
    Upload,

    /// <summary>Downloading files or file groups from Steam Workshop.</summary>
    Download,

    /// <summary>Sharing file groups with other Steam users.</summary>
    Share,

    /// <summary>Deleting files or file groups from Steam Workshop.</summary>
    Delete,

    /// <summary>General-purpose or unclassified task.</summary>
    General
}
