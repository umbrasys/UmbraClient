namespace Glamourer.Api.Enums;

/// <summary> What type of Glamourer process was performed on the actors state to update it. </summary>
public enum StateFinalizationType
{
    /// <summary> A characters saved state had the model id altered. </summary>
    ModelChange = 0,

    /// <summary> A singular Design was applied to an actors state. </summary>
    DesignApplied = 1,

    /// <summary> A characters saved state had been reset to game values. </summary>
    Revert = 2,

    /// <summary> A characters saved state had only its customization data reset to game state. </summary>
    RevertCustomize = 3,

    /// <summary> A characters saved state had only its equipment data reset to game state. </summary>
    RevertEquipment = 4,

    /// <summary> A characters saved state had its advanced values reverted to game state. </summary>
    RevertAdvanced = 5,

    /// <summary> A characters saved state was reverted to automation state on top of their game state </summary>
    RevertAutomation = 6,

    /// <summary> A characters saved state had a generic reapply as a single operation. </summary>
    Reapply = 7,

    /// <summary> A characters saved state had their automation state reapplied over their existing state. </summary>
    ReapplyAutomation = 8,

    /// <summary> A characters save state finished applying all updated slots for game state on gearset change or initial load. </summary>
    Gearset = 9,
}

