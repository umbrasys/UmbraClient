using Dalamud.Plugin.Services;

namespace UmbraSync.Services.Mediator;

public interface IMediatorSubscriber : IDalamudService
{
    MareMediator Mediator { get; }
}