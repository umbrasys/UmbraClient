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
            new(new Version(2, 1, 3, 0), "2.1.3.0", new List<ChangelogLine>
            {
                new("Nouveau système de déduplication des téléchargements pour éviter les téléchargements en double."),
                new("Système de retry automatique pour les applications de mods échouées."),
                new("Détection des fichiers manquants avec réapplication forcée automatique."),
                new("Traitement parallèle des paires activé par défaut."),
            }),
            new(new Version(2, 1, 2, 1), "2.1.2.1", new List<ChangelogLine>
            {
                new("Résolution d'un problème de décryptage lors d'un partage MCDF."),
                new("Résolution d'un problème lors de l'application d'un MCDF sur la cible en /gpose."),
                new("Ajout de la notification lors de la réception d'un partage MCDF."),
                new("Ajout de la notification lorsque une Syncshell (Avec droits admin) devient visible dans le SyncFinder"),

            }),
            new(new Version(2, 1, 2, 0), "2.1.2.0", new List<ChangelogLine>
            {
                new("Correction d'un problème de chargement utilisateur"),
                new("Correction de l'affichage de la bulle d'écriture et de sa distance de visibilité"),
                new("Correction de la zone de notification inférieur-droit qui pouvait bloquer l'interaction de la zone"),
                new("Mise à jour API Penumbra"),

            }),

            new(new Version(2, 1, 1, 0), "2.1.1.0", new List<ChangelogLine>
            {
                new("Nouveau système de cache pour les données de personnages"),
                new("Cache métrique pour éviter des recalculs inutiles lors de la synchronisation"),
                new("Limite de traitement simultané des paires augmentée de 16 à 50"),
                new("Correction d'un problème de pause user"),
                new("Radius de detection SlotSync réduit à 20m maximum"),

            }),
            new(new Version(2, 1, 0, 0), "2.1.0.0", new List<ChangelogLine>
            {
                new("Nouvelle fonctionnalité : Ajout du support complet des profils RP (Roleplay) avec gestion d'images et descriptions séparées par personnage."),
                new("Nouvelle fonctionnalité : gestion des SyncShells et Slots pour une synchronisation avancée."),
                new("Nouvelle fonctionnalité : Ajout de notifications interactives pour les invitations de pair avec options de configuration."),
                new("Réécriture complète de l'intégration Penumbra avec architecture modulaire pour une meilleure stabilité."),
                new("Traitement parallèle des pairs avec limitation configurable et amélioration des performances."),
                new("Migration vers MessagePack v2.5.187 suite à un problème de vulnérabilité."),
                new("Nombreuses améliorations de stabilité, modernisation du code et optimisations."),

            }),
        };
    }

    private readonly record struct ChangelogEntry(Version Version, string VersionLabel, IReadOnlyList<ChangelogLine> Lines);

    private readonly record struct ChangelogLine(string Text, int IndentLevel = 0, System.Numerics.Vector4? Color = null);
}