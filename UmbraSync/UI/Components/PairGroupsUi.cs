using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using UmbraSync.API.Data.Extensions;
using UmbraSync.MareConfiguration;
using UmbraSync.UI.Handlers;
using UmbraSync.WebAPI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UmbraSync.UI.Components;

public class PairGroupsUi
{
    private readonly ApiController _apiController;
    private readonly MareConfigService _mareConfig;
    private readonly SelectPairForGroupUi _selectGroupForPairUi;
    private readonly TagHandler _tagHandler;
    private readonly UiSharedService _uiSharedService;

    public PairGroupsUi(MareConfigService mareConfig, TagHandler tagHandler, ApiController apiController,
        SelectPairForGroupUi selectGroupForPairUi, UiSharedService uiSharedService)
    {
        _mareConfig = mareConfig;
        _tagHandler = tagHandler;
        _apiController = apiController;
        _selectGroupForPairUi = selectGroupForPairUi;
        _uiSharedService = uiSharedService;
    }

    public void Draw<T>(List<T> visibleUsers, List<T> onlineUsers, List<T> offlineUsers, Action? drawVisibleExtras = null) where T : DrawPairBase
    {
        // Only render those tags that actually have pairs in them, otherwise
        // we can end up with a bunch of useless pair groups
        var tagsWithPairsInThem = _tagHandler.GetAllTagsSorted();
        var allUsers = onlineUsers.Concat(offlineUsers).ToList();
        if (typeof(T) == typeof(DrawUserPair))
        {
            DrawUserPairs(tagsWithPairsInThem, allUsers.Cast<DrawUserPair>().ToList(), visibleUsers.Cast<DrawUserPair>(), onlineUsers.Cast<DrawUserPair>(), offlineUsers.Cast<DrawUserPair>(), drawVisibleExtras);
        }
    }

    private void DrawButtons(string tag, List<DrawUserPair> availablePairsInThisTag)
    {
        var allArePaused = availablePairsInThisTag.All(pair => pair.UserPair.OwnPermissions.IsPaused());
        var pauseButton = allArePaused ? FontAwesomeIcon.Play : FontAwesomeIcon.Pause;
        var flyoutMenuSize = _uiSharedService.GetIconButtonSize(FontAwesomeIcon.Bars);
        var pauseButtonSize = _uiSharedService.GetIconButtonSize(pauseButton);
        var spacingX = ImGui.GetStyle().ItemSpacing.X;
        var currentX = ImGui.GetCursorPosX();
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var buttonsWidth = pauseButtonSize.X + flyoutMenuSize.X + spacingX;
        var pauseStart = Math.Max(currentX, currentX + availableWidth - buttonsWidth);

        ImGui.SameLine(pauseStart);
        if (_uiSharedService.IconButton(pauseButton))
        {
            if (allArePaused)
            {
                ResumeAllPairs(availablePairsInThisTag);
            }
            else
            {
                PauseRemainingPairs(availablePairsInThisTag);
            }
        }
        if (allArePaused)
        {
            UiSharedService.AttachToolTip($"Resume pairing with all pairs in {tag}");
        }
        else
        {
            UiSharedService.AttachToolTip($"Pause pairing with all pairs in {tag}");
        }

        var menuStart = Math.Max(pauseStart + pauseButtonSize.X + spacingX, currentX);
        ImGui.SameLine(menuStart);
        if (_uiSharedService.IconButton(FontAwesomeIcon.Bars))
        {
            ImGui.OpenPopup("Group Flyout Menu");
        }

        if (ImGui.BeginPopup("Group Flyout Menu"))
        {
            using (ImRaii.PushId($"buttons-{tag}")) DrawGroupMenu(tag);
            ImGui.EndPopup();
        }
    }

    private void DrawCategory(string tag, IEnumerable<DrawPairBase> onlineUsers, IEnumerable<DrawPairBase> allUsers, IEnumerable<DrawPairBase>? visibleUsers = null, Action? drawExtraContent = null)
    {
        var onlineUsersList = onlineUsers as List<DrawPairBase> ?? onlineUsers.ToList();
        var allUsersList = allUsers as List<DrawPairBase> ?? allUsers.ToList();
        var visibleUsersList = visibleUsers switch
        {
            null => null,
            List<DrawPairBase> list => list,
            _ => visibleUsers.ToList()
        };

        List<DrawPairBase> usersInThisTag;
        HashSet<string>? otherUidsTaggedWithTag = null;
        bool isSpecialTag = false;
        int visibleInThisTag = 0;
        if (tag is TagHandler.CustomOfflineTag or TagHandler.CustomOnlineTag or TagHandler.CustomVisibleTag or TagHandler.CustomUnpairedTag)
        {
            usersInThisTag = onlineUsersList;
            isSpecialTag = true;
        }
        else
        {
            otherUidsTaggedWithTag = _tagHandler.GetOtherUidsForTag(tag);
            usersInThisTag = onlineUsersList
                .Where(pair => otherUidsTaggedWithTag.Contains(pair.UID))
                .ToList();
            visibleInThisTag = visibleUsersList?.Count(p => otherUidsTaggedWithTag.Contains(p.UID)) ?? 0;
        }

        if (isSpecialTag && !usersInThisTag.Any()) return;

        UiSharedService.DrawCard($"pair-group-{tag}", () =>
        {
            DrawName(tag, isSpecialTag, visibleInThisTag, usersInThisTag.Count, otherUidsTaggedWithTag?.Count);
            if (!isSpecialTag)
            {
                using (ImRaii.PushId($"group-{tag}-buttons")) DrawButtons(tag, allUsersList.Cast<DrawUserPair>().Where(p => otherUidsTaggedWithTag!.Contains(p.UID)).ToList());
            }

            if (!_tagHandler.IsTagOpen(tag)) return;

            ImGuiHelpers.ScaledDummy(4f);
            var indent = 18f * ImGuiHelpers.GlobalScale;
            ImGui.Indent(indent);
            DrawPairs(usersInThisTag);
            drawExtraContent?.Invoke();
            ImGui.Unindent(indent);
        }, stretchWidth: true);

        ImGuiHelpers.ScaledDummy(4f);
    }

    private void DrawGroupMenu(string tag)
    {
        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Users, "Add people to " + tag))
        {
            _selectGroupForPairUi.Open(tag);
        }
        UiSharedService.AttachToolTip($"Add more users to Group {tag}");

        if (_uiSharedService.IconTextButton(FontAwesomeIcon.Trash, "Delete " + tag) && UiSharedService.CtrlPressed())
        {
            _tagHandler.RemoveTag(tag);
        }
        UiSharedService.AttachToolTip($"Delete Group {tag} (Will not delete the pairs)" + Environment.NewLine + "Hold CTRL to delete");
    }

    private void DrawName(string tag, bool isSpecialTag, int visible, int online, int? total)
    {
        string displayedName = tag switch
        {
            TagHandler.CustomUnpairedTag => "Unpaired",
            TagHandler.CustomOfflineTag => "Offline",
            TagHandler.CustomOnlineTag => _mareConfig.Current.ShowOfflineUsersSeparately ? "Online" : "Contacts",
            TagHandler.CustomVisibleTag => "Visible",
            _ => tag
        };

        string resultFolderName = !isSpecialTag ? $"{displayedName} ({visible}/{online}/{total} Pairs)" : $"{displayedName} ({online} Pairs)";
        bool isOpen = _tagHandler.IsTagOpen(tag);
        bool previousState = isOpen;
        UiSharedService.DrawArrowToggle(ref isOpen, $"##group-toggle-{tag}");
        if (isOpen != previousState)
        {
            _tagHandler.SetTagOpen(tag, isOpen);
        }
        ImGui.SameLine(0f, 6f * ImGuiHelpers.GlobalScale);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(resultFolderName);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            bool newState = !_tagHandler.IsTagOpen(tag);
            _tagHandler.SetTagOpen(tag, newState);
        }

        if (!isSpecialTag && ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted($"Group {tag}");
            ImGui.Separator();
            ImGui.TextUnformatted($"{visible} Pairs visible");
            ImGui.TextUnformatted($"{online} Pairs online/paused");
            ImGui.TextUnformatted($"{total} Pairs total");
            ImGui.EndTooltip();
        }
    }

    private void DrawPairs(IEnumerable<DrawPairBase> availablePairsInThisCategory)
    {
        // These are all the OtherUIDs that are tagged with this tag
        UidDisplayHandler.RenderPairList(availablePairsInThisCategory);
    }

    private void DrawUserPairs(List<string> tagsWithPairsInThem, List<DrawUserPair> allUsers, IEnumerable<DrawUserPair> visibleUsers, IEnumerable<DrawUserPair> onlineUsers, IEnumerable<DrawUserPair> offlineUsers, Action? drawVisibleExtras)
    {
        var onlineUsersList = onlineUsers as List<DrawUserPair> ?? onlineUsers.ToList();
        var offlineUsersList = offlineUsers as List<DrawUserPair> ?? offlineUsers.ToList();
        var visibleUsersList = visibleUsers as List<DrawUserPair> ?? visibleUsers.ToList();

        // Visible section intentionally omitted for Individual Pairs view.
        foreach (var tag in tagsWithPairsInThem)
        {
            if (_mareConfig.Current.ShowOfflineUsersSeparately)
            {
                using (ImRaii.PushId($"group-{tag}")) DrawCategory(tag, onlineUsersList, allUsers, visibleUsersList);
            }
            else
            {
                using (ImRaii.PushId($"group-{tag}")) DrawCategory(tag, allUsers, allUsers, visibleUsersList);
            }
        }
        if (_mareConfig.Current.ShowOfflineUsersSeparately)
        {
            using (ImRaii.PushId($"group-OnlineCustomTag")) DrawCategory(TagHandler.CustomOnlineTag,
                onlineUsersList.Where(u => !_tagHandler.HasAnyTag(u.UID)).ToList(), allUsers);
            using (ImRaii.PushId($"group-OfflineCustomTag")) DrawCategory(TagHandler.CustomOfflineTag,
                offlineUsersList.Where(u => u.UserPair.OtherPermissions.IsPaired()).ToList(), allUsers);
        }
        else
        {
            using (ImRaii.PushId($"group-OnlineCustomTag")) DrawCategory(TagHandler.CustomOnlineTag,
                onlineUsersList.Concat(offlineUsersList.Where(u => u.UserPair.OtherPermissions.IsPaired())).Where(u => !_tagHandler.HasAnyTag(u.UID)).ToList(), allUsers);
        }
        using (ImRaii.PushId($"group-UnpairedCustomTag")) DrawCategory(TagHandler.CustomUnpairedTag,
            offlineUsersList.Where(u => !u.UserPair.OtherPermissions.IsPaired()).ToList(), allUsers);

        drawVisibleExtras?.Invoke();
    }

    private void PauseRemainingPairs(List<DrawUserPair> availablePairs)
    {
        foreach (var pairToPause in availablePairs.Where(pair => !pair.UserPair.OwnPermissions.IsPaused()))
        {
            var perm = pairToPause.UserPair.OwnPermissions;
            perm.SetPaused(paused: true);
            _ = _apiController.UserSetPairPermissions(new(new(pairToPause.UID), perm));
        }
    }

    private void ResumeAllPairs(List<DrawUserPair> availablePairs)
    {
        foreach (var pairToPause in availablePairs)
        {
            var perm = pairToPause.UserPair.OwnPermissions;
            perm.SetPaused(paused: false);
            _ = _apiController.UserSetPairPermissions(new(new(pairToPause.UID), perm));
        }
    }

}
