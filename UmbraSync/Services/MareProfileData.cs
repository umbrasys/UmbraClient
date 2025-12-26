namespace UmbraSync.Services;

public record MareProfileData(bool IsFlagged, bool IsNSFW, string Base64ProfilePicture, string Description,
    string? Base64RpProfilePicture = null, string? RpDescription = null, bool IsRpNSFW = false)
{
    public Lazy<byte[]> ImageData { get; } = new Lazy<byte[]>(() => string.IsNullOrEmpty(Base64ProfilePicture) ? Array.Empty<byte>() : Convert.FromBase64String(Base64ProfilePicture));
    public Lazy<byte[]> RpImageData { get; } = new Lazy<byte[]>(() => string.IsNullOrEmpty(Base64RpProfilePicture) ? Array.Empty<byte>() : Convert.FromBase64String(Base64RpProfilePicture));
}
