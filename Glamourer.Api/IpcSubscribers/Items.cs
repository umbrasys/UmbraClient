using Dalamud.Plugin;
using Glamourer.Api.Api;
using Glamourer.Api.Enums;
using Glamourer.Api.Helpers;

namespace Glamourer.Api.IpcSubscribers;

/// <inheritdoc cref="IGlamourerApiItems.SetItem"/>
public sealed class SetItem(IDalamudPluginInterface pi)
    : FuncSubscriber<int, byte, ulong, IReadOnlyList<byte>, uint, ulong, int>(pi, Label)
{
    /// <summary> The label. </summary>
    public const string Label = $"Glamourer.{nameof(SetItem)}.V3";

    /// <inheritdoc cref="IGlamourerApiItems.SetItem"/>
    public GlamourerApiEc Invoke(int objectIndex, ApiEquipSlot slot, ulong itemId, IReadOnlyList<byte> stain, uint key = 0,
        ApplyFlag flags = ApplyFlag.Once)
        => (GlamourerApiEc)Invoke(objectIndex, (byte)slot, itemId, stain, key, (ulong)flags);

    /// <summary> Create a provider. </summary>
    public static FuncProvider<int, byte, ulong, IReadOnlyList<byte>, uint, ulong, int> Provider(IDalamudPluginInterface pi,
        IGlamourerApiItems api)
        => new(pi, Label, (a, b, c, d, e, f) => (int)api.SetItem(a, (ApiEquipSlot)b, c, d, e, (ApplyFlag)f));
}

/// <inheritdoc cref="IGlamourerApiItems.SetItemName"/>
public sealed class SetItemName(IDalamudPluginInterface pi)
    : FuncSubscriber<string, byte, ulong, IReadOnlyList<byte>, uint, ulong, int>(pi, Label)
{
    /// <summary> The label. </summary>
    public const string Label = $"Glamourer.{nameof(SetItemName)}.V2";

    /// <inheritdoc cref="IGlamourerApiItems.SetItem"/>
    public GlamourerApiEc Invoke(string objectName, ApiEquipSlot slot, ulong itemId, IReadOnlyList<byte> stain, uint key = 0,
        ApplyFlag flags = ApplyFlag.Once)
        => (GlamourerApiEc)Invoke(objectName, (byte)slot, itemId, stain, key, (ulong)flags);

    /// <summary> Create a provider. </summary>
    public static FuncProvider<string, byte, ulong, IReadOnlyList<byte>, uint, ulong, int> Provider(IDalamudPluginInterface pi,
        IGlamourerApiItems api)
        => new(pi, Label, (a, b, c, d, e, f) => (int)api.SetItemName(a, (ApiEquipSlot)b, c, d, e, (ApplyFlag)f));
}

/// <inheritdoc cref="IGlamourerApiItems.SetBonusItem"/>
public sealed class SetBonusItem(IDalamudPluginInterface pi)
    : FuncSubscriber<int, byte, ulong, uint, ulong, int>(pi, Label)
{
    /// <summary> The label. </summary>
    public const string Label = $"Glamourer.{nameof(SetBonusItem)}";

    /// <inheritdoc cref="IGlamourerApiItems.SetBonusItem"/>
    public GlamourerApiEc Invoke(int objectIndex, ApiBonusSlot slot, ulong itemId, uint key = 0, ApplyFlag flags = ApplyFlag.Once)
        => (GlamourerApiEc)Invoke(objectIndex, (byte)slot, itemId, key, (ulong)flags);

    /// <summary> Create a provider. </summary>
    public static FuncProvider<int, byte, ulong, uint, ulong, int> Provider(IDalamudPluginInterface pi, IGlamourerApiItems api)
        => new(pi, Label, (a, b, c, d, e) => (int)api.SetBonusItem(a, (ApiBonusSlot)b, c, d, (ApplyFlag)e));
}

/// <inheritdoc cref="IGlamourerApiItems.SetBonusItemName"/>
public sealed class SetBonusItemName(IDalamudPluginInterface pi)
    : FuncSubscriber<string, byte, ulong, uint, ulong, int>(pi, Label)
{
    /// <summary> The label. </summary>
    public const string Label = $"Glamourer.{nameof(SetBonusItemName)}.V2";

    /// <inheritdoc cref="IGlamourerApiItems.SetBonusItemName"/>
    public GlamourerApiEc Invoke(string objectName, ApiBonusSlot slot, ulong itemId, uint key = 0, ApplyFlag flags = ApplyFlag.Once)
        => (GlamourerApiEc)Invoke(objectName, (byte)slot, itemId, key, (ulong)flags);

    /// <summary> Create a provider. </summary>
    public static FuncProvider<string, byte, ulong, uint, ulong, int> Provider(IDalamudPluginInterface pi, IGlamourerApiItems api)
        => new(pi, Label, (a, b, c, d, e) => (int)api.SetBonusItemName(a, (ApiBonusSlot)b, c, d, (ApplyFlag)e));
}

/// <inheritdoc cref="IGlamourerApiItems.SetMetaState"/>
public sealed class SetMetaState(IDalamudPluginInterface pi)
    : FuncSubscriber<int, ulong, bool, uint, ulong, int>(pi, Label)
{
    /// <summary> The label. </summary>
    public const string Label = $"Glamourer.{nameof(SetMetaState)}";

    /// <inheritdoc cref="IGlamourerApiItems.SetMetaState"/>
    public GlamourerApiEc Invoke(int objectIndex, MetaFlag types, bool newValue, uint key = 0, 
        ApplyFlag flags = ApplyFlag.Once)
        => (GlamourerApiEc)Invoke(objectIndex, (ulong)types, newValue, key, (ulong)flags);

    /// <summary> Create a provider. </summary>
    public static FuncProvider<int, ulong, bool, uint, ulong, int> Provider(IDalamudPluginInterface pi, 
        IGlamourerApiItems api)
        => new(pi, Label, (a, b, c, d, e) => (int)api.SetMetaState(a, (MetaFlag)b, c, d, (ApplyFlag)e));
}

/// <inheritdoc cref="IGlamourerApiItems.SetMetaStateName"/>
public sealed class SetMetaStateName(IDalamudPluginInterface pi)
    : FuncSubscriber<string, ulong, bool, uint, ulong, int>(pi, Label)
{
    /// <summary> The label. </summary>
    public const string Label = $"Glamourer.{nameof(SetMetaStateName)}";

    /// <inheritdoc cref="IGlamourerApiItems.SetMetaStateName"/>
    public GlamourerApiEc Invoke(string objectName, MetaFlag types, bool newValue, uint key = 0, 
        ApplyFlag flags = ApplyFlag.Once)
        => (GlamourerApiEc)Invoke(objectName, (ulong)types, newValue, key, (ulong)flags);

    /// <summary> Create a provider. </summary>
    public static FuncProvider<string, ulong, bool, uint, ulong, int> Provider(IDalamudPluginInterface pi, 
        IGlamourerApiItems api)
        => new(pi, Label, (a, b, c, d, e) => (int)api.SetMetaStateName(a, (MetaFlag)b, c, d, (ApplyFlag)e));
}
