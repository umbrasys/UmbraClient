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
            new(new Version(2, 0, 3, 5), "2.0.3.5", new List<ChangelogLine>
            {
                new("Correction du crash lors de rassemblements importants (50+ personnes) et amélioration de la stabilité de la synchronisation."),
                new("Amélioration de la file de téléchargement : toujours active par défaut, limite augmentée à 50 téléchargements simultanés."),

            }),
            new(new Version(2, 0, 3, 4), "2.0.3.4", new List<ChangelogLine>
            {
                new("Optimisation de la gestion des mods Penumbra pour éviter les redraw inutiles."),
            }),
            new(new Version(2, 0, 3, 3), "2.0.3.3", new List<ChangelogLine>
            {
                new("Mise à jour de l'IPC Moodle."),
                new("Désactivation des bulles d'écriture quand l'on entre en gpose."),
            }),
            new(new Version(2, 0, 3, 2), "2.0.3.2", new List<ChangelogLine>
            {
                new("Mise à jour pour le support V3 de Brio."),
                new("Mise à jour des IPC Glamourer & Penumbra."),
                new("Correction d'une 'access violation' lors de la déconnexion provoquant un crash du joueur et de tout les paires autour."),
                new("Divers changements et ajustement de l'interface graphique."),
            }),
            new(new Version(2, 0, 3, 1), "2.0.3.1", new List<ChangelogLine>
            {
                new("Correction de la synchronisation de l'état de pause entre les paires individuelles et les Syncshells."),
                new("Correction de la ré-application des mods lors de la reconnexion au serveur ou de la sortie de pause."),
                new("Correction d'un problème où un utilisateur pouvait apparaître hors ligne après avoir été sorti de pause."),
                new("Correction du classement des utilisateurs dans la liste 'visible' des Syncshells après une sortie de pause."),
                new("Application d'une distance de 20 mètres maximum autour du personnage pour l'affichage de la bulle d'écriture."),
            }),
            new(new Version(2, 0, 3, 0), "2.0.3.0", new List<ChangelogLine>
            {
                new("Passage au .NET SDK 10.0.101."),
                new("Mise à jour vers Dalamud.NET.Sdk 14.0.1."),
                new("Compatible avec la version 7.4 de FFXIV."),  
                new("Adaptations internes pour les changements de structure de l'API Dalamud."),   
                new("Correction de l'affichage de la bulle en fonction du canal utilisé."),  
                new("Correction d'un problème en mettant un utilisateur en pause pouvant mettre en pause temporairement d'autres utilisateurs."),
                new("Correction d'un problème pouvant provoquer des erreurs de doublon de collection temporaire Penumbra empêchant l'utilisateur d'être vue avec ses mods.")               
            }),
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