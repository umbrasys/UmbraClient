namespace UmbraSync.Services;

public record UmbraProfileData(bool IsFlagged, bool IsNSFW, string Base64ProfilePicture, string Description,
    string? Base64RpProfilePicture = null, string? RpDescription = null, bool IsRpNSFW = false,
    string? RpFirstName = null, string? RpLastName = null, string? RpTitle = null, string? RpAge = null,
    string? RpHeight = null, string? RpBuild = null, string? RpOccupation = null, string? RpAffiliation = null,
    string? RpAlignment = null, string? RpAdditionalInfo = null)
{
    public Lazy<byte[]> ImageData { get; } = new Lazy<byte[]>(() => string.IsNullOrEmpty(Base64ProfilePicture) ? Array.Empty<byte>() : Convert.FromBase64String(Base64ProfilePicture));
    public Lazy<byte[]> RpImageData { get; } = new Lazy<byte[]>(() => string.IsNullOrEmpty(Base64RpProfilePicture) ? Array.Empty<byte>() : Convert.FromBase64String(Base64RpProfilePicture));
}