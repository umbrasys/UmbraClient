using UmbraSync.API.Data;
namespace UmbraSync.PlayerData.Pairs;
public interface IPairHandlerAdapter : IDisposable
{
    string Ident { get; }
    bool Initialized { get; }
    bool IsVisible { get; }
    bool ScheduledForDeletion { get; set; }
    CharacterData? LastReceivedCharacterData { get; }
    string? PlayerName { get; }
    uint PlayerCharacterId { get; }
    long LastAppliedDataBytes { get; }
    DateTime? LastDataReceivedAt { get; }
    DateTime? LastApplyAttemptAt { get; }
    DateTime? LastSuccessfulApplyAt { get; }
    void Initialize(string name);
    void ApplyCharacterData(Guid applicationBase, CharacterData characterData, bool forceApplyCustomization = false);
    void OnDataReceived();
    void SetUploading(bool isUploading);
    void Invalidate();
}
