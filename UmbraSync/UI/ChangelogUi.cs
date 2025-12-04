using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using UmbraSync.Localization;
using UmbraSync.MareConfiguration;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace UmbraSync.UI;

public sealed class ChangelogUi : WindowMediatorSubscriberBase
{
    private const int AlwaysExpandedEntryCount = 2;

    private readonly MareConfigService _configService;
    private readonly UiSharedService _uiShared;
    private readonly Version _currentVersion;
    private readonly string _currentVersionLabel;
    private readonly IReadOnlyList<ChangelogEntry> _entries;

    private bool _showAllEntries;
    private bool _hasAcknowledgedVersion;

    public ChangelogUi(ILogger<ChangelogUi> logger, UiSharedService uiShared, MareConfigService configService,
        MareMediator mediator, PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, Loc.Get("ChangelogUi.WindowTitle"), performanceCollectorService)
    {
        _uiShared = uiShared;
        _configService = configService;
        _currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0);
        _currentVersionLabel = _currentVersion.ToString();
        _entries = BuildEntries();
        _hasAcknowledgedVersion = string.Equals(_configService.Current.LastChangelogVersionSeen, _currentVersionLabel, StringComparison.Ordinal);

        RespectCloseHotkey = true;
        SizeConstraints = new()
        {
            MinimumSize = new(520, 360),
            MaximumSize = new(900, 1200)
        };
        Flags |= ImGuiWindowFlags.NoResize;
        ShowCloseButton = true;

        if (!string.Equals(_configService.Current.LastChangelogVersionSeen, _currentVersionLabel, StringComparison.Ordinal))
        {
            IsOpen = true;
        }
    }

    public override void OnClose()
    {
        MarkCurrentVersionAsReadIfNeeded();
        base.OnClose();
    }

    protected override void DrawInternal()
    {
        _ = _uiShared.DrawOtherPluginState();

        DrawHeader();
        DrawEntries();
        DrawFooter();
    }

    private void DrawHeader()
    {
        using (_uiShared.UidFont.Push())
        {
            ImGui.TextUnformatted(Loc.Get("ChangelogUi.HeaderTitle"));
        }

        ImGui.TextColored(ImGuiColors.DalamudGrey, string.Format(CultureInfo.CurrentCulture, Loc.Get("ChangelogUi.LoadedVersion"), _currentVersionLabel));
        ImGui.Separator();
    }

    private void DrawEntries()
    {
        bool expandedOldVersions = false;
        for (int index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            if (!_showAllEntries && index >= AlwaysExpandedEntryCount)
            {
                if (!expandedOldVersions)
                {
                    expandedOldVersions = ImGui.CollapsingHeader(Loc.Get("ChangelogUi.FullHistory"));
                }

                if (!expandedOldVersions)
                {
                    continue;
                }
            }

            DrawEntry(entry);
        }
    }

    private void DrawEntry(ChangelogEntry entry)
    {
        using (ImRaii.PushId(entry.VersionLabel))
        {
            ImGui.Spacing();
            UiSharedService.ColorText(entry.VersionLabel, entry.Version == _currentVersion
                ? ImGuiColors.HealerGreen
                : ImGuiColors.DalamudWhite);

            ImGui.Spacing();

            foreach (var line in entry.Lines)
            {
                DrawLine(line);
            }

            ImGui.Spacing();
            ImGui.Separator();
        }
    }

    private static void DrawLine(ChangelogLine line)
    {
        using var indent = line.IndentLevel > 0 ? ImRaii.PushIndent(line.IndentLevel) : null;
        if (line.Color != null)
        {
            ImGui.TextColored(line.Color.Value, $"- {line.Text}");
        }
        else
        {
            ImGui.TextUnformatted($"- {line.Text}");
        }
    }

    private void DrawFooter()
    {
        ImGui.Spacing();
        if (!_showAllEntries && _entries.Count > AlwaysExpandedEntryCount)
        {
            if (ImGui.Button(Loc.Get("ChangelogUi.ShowAll")))
            {
                _showAllEntries = true;
            }

            ImGui.SameLine();
        }

        if (ImGui.Button(Loc.Get("ChangelogUi.MarkAsRead")))
        {
            MarkCurrentVersionAsReadIfNeeded();
            IsOpen = false;
        }
    }

    private void MarkCurrentVersionAsReadIfNeeded()
    {
        if (_hasAcknowledgedVersion)
            return;

        _configService.Current.LastChangelogVersionSeen = _currentVersionLabel;
        _configService.Save();
        _hasAcknowledgedVersion = true;
    }

    private static IReadOnlyList<ChangelogEntry> BuildEntries()
    {
        return new List<ChangelogEntry>
        {
            new(new Version(2, 0, 2, 0), "2.0.2.0", new List<ChangelogLine>
            {
                new("Pré-Cache : Permet d'envoyer tout vos mods sur le serveur UmbraSync pour faciliter les téléchargements / changements."),
                new("Correction du problème de freeze quand l'on redimensionne trop vite la fenêtre UmbraSync"),
                new("Correction de l'affichage pré-maturé des états Glamourer empêchant Glamourer de fonctionner correctement."),
                new("Correction de l'affichage des animations/compagnons/montures moddé avec un squelette spécifique."),
                
            }),
            new(new Version(2, 0, 1, 0), "2.0.1.0", new List<ChangelogLine>
            {
                new("Réécriture de l'AutoDetect pour de meilleurs performances"),
                new("Possibilité de définir un ID personnalisé dans votre profil"),
                new("Définir la limite des membres de la syncshell"),
                
            }),
            new(new Version(2, 0, 0, 2), "2.0.0.2", new List<ChangelogLine>
            {
                new("Mise à niveau de Dalamud."),
                new("Mise à niveau API Penumbra & Glamourer"),
                new("Correction traduction dans l'introduction."),
            }),
            new(new Version(2, 0, 0, 1), "2.0.0.1", new List<ChangelogLine>
            {
                new("Rétablissement de la visibilité de la bulle d'écriture sous certaines conditions."),
            }),
            new(new Version(2, 0, 0, 0), "2.0.0.0", new List<ChangelogLine>
            {
                new("Nouvelle interface graphique, plus moderne et plus lisible."),
                new("Partage MCDF : Il vous est désormais possible de partager le MCDF de votre personnage avec d'autres utilisateurs. (Hub de données... > Data Hub > MCDF Share)"),
                new("Il vous est maintenant possible de rendre publique votre Syncshell depuis l'interface administrateur de celle-ci."),
                new("Optimisation du téléchargement et de la compression des données téléchargée."),
                new ("Continuité de la traduction en français"),
                new("Compatibilité de la bulle d'écriture avec le plugin ChatTwo"),
                new("D'autres ajustement visuel, modérnisation du code source"),
            }),
            
            new(new Version(0, 1, 9, 6), "0.1.9.6", new List<ChangelogLine>
            {
                new("Possibilité de désactiver l'alerte self-analysis (Settings => Performance)."),
            }),
            new(new Version(0, 1, 9, 5), "0.1.9.5", new List<ChangelogLine>
            {
                new(Loc.Get("ChangelogUi.0_1_9_5.Line1")),
                new(Loc.Get("ChangelogUi.0_1_9_5.Line2")),
                new(Loc.Get("ChangelogUi.0_1_9_5.Line3")),
                new(Loc.Get("ChangelogUi.0_1_9_5.Line4")),
                new(Loc.Get("ChangelogUi.0_1_9_5.Line5")),
            }),
            new(new Version(0, 1, 9, 4), "0.1.9.4", new List<ChangelogLine>
            {
                new(Loc.Get("ChangelogUi.0_1_9_4.Line1")),
                new(Loc.Get("ChangelogUi.0_1_9_4.Line2")),
                new(Loc.Get("ChangelogUi.0_1_9_4.Line3")),
                new(Loc.Get("ChangelogUi.0_1_9_4.Line4")),
                new(Loc.Get("ChangelogUi.0_1_9_4.Line5")),
                new(Loc.Get("ChangelogUi.0_1_9_4.Line6")),
                new(Loc.Get("ChangelogUi.0_1_9_4.Line7")),
            }),
            new(new Version(0, 1, 9, 3), "0.1.9.3", new List<ChangelogLine>
            {
                new(Loc.Get("ChangelogUi.0_1_9_3.Line1")),
            }),
            new(new Version(0, 1, 9, 2), "0.1.9.2", new List<ChangelogLine>
            {
                new(Loc.Get("ChangelogUi.0_1_9_2.Line1")),
            }),
            new(new Version(0, 1, 9, 1), "0.1.9.1", new List<ChangelogLine>
            {
                new(Loc.Get("ChangelogUi.0_1_9_1.Line1")),
                new(Loc.Get("ChangelogUi.0_1_9_1.Line2")),

            }),
            new(new Version(0, 1, 9, 0), "0.1.9.0", new List<ChangelogLine>
            {
                new(Loc.Get("ChangelogUi.0_1_9_0.Line1")),
                new(Loc.Get("ChangelogUi.0_1_9_0.Line2")),
                new(Loc.Get("ChangelogUi.0_1_9_0.Line3")),
                new(Loc.Get("ChangelogUi.0_1_9_0.Line4")),
                new(Loc.Get("ChangelogUi.0_1_9_0.Line5")),
                new(Loc.Get("ChangelogUi.0_1_9_0.Line6")),
            }),
            new(new Version(0, 1, 8, 2), "0.1.8.2", new List<ChangelogLine>
            {
                new(Loc.Get("ChangelogUi.0_1_8_2.Line1")),
                new(Loc.Get("ChangelogUi.0_1_8_2.Line2")),
                new(Loc.Get("ChangelogUi.0_1_8_2.Line3")),
                new(Loc.Get("ChangelogUi.0_1_8_2.Line4")),
                new(Loc.Get("ChangelogUi.0_1_8_2.Line5")),
                new(Loc.Get("ChangelogUi.0_1_8_2.Line6")),
                new(Loc.Get("ChangelogUi.0_1_8_2.Line7")),
            }),
            new(new Version(0, 1, 8, 1), "0.1.8.1", new List<ChangelogLine>
            {
                new(Loc.Get("ChangelogUi.0_1_8_1.Line1")),
                new(Loc.Get("ChangelogUi.0_1_8_1.Line2")),
                new(Loc.Get("ChangelogUi.0_1_8_1.Line3")),
                new(Loc.Get("ChangelogUi.0_1_8_1.Line4")),
            }),
            new(new Version(0, 1, 8, 0), "0.1.8.0", new List<ChangelogLine>
            {
                new(Loc.Get("ChangelogUi.0_1_8_0.Line1")),
                new(Loc.Get("ChangelogUi.0_1_8_0.Line2"), 1, ImGuiColors.DalamudGrey),
                new(Loc.Get("ChangelogUi.0_1_8_0.Line3"), 1, ImGuiColors.DalamudGrey),
                new(Loc.Get("ChangelogUi.0_1_8_0.Line4")),
                new(Loc.Get("ChangelogUi.0_1_8_0.Line5")),
                new(Loc.Get("ChangelogUi.0_1_8_0.Line6")),
            }),
        };
    }

    private readonly record struct ChangelogEntry(Version Version, string VersionLabel, IReadOnlyList<ChangelogLine> Lines);

    private readonly record struct ChangelogLine(string Text, int IndentLevel = 0, System.Numerics.Vector4? Color = null);
}
