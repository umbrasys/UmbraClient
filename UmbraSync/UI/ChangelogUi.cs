using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.MareConfiguration;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.UI;

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
        : base(logger, mediator, "Umbra Sync - Notes de version", performanceCollectorService)
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
            ImGui.TextUnformatted("Notes de version");
        }

        ImGui.TextColored(ImGuiColors.DalamudGrey, $"Version chargée : {_currentVersionLabel}");
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
                    expandedOldVersions = ImGui.CollapsingHeader("Historique complet");
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
            if (ImGui.Button("Tout afficher"))
            {
                _showAllEntries = true;
            }

            ImGui.SameLine();
        }

        if (ImGui.Button("Marquer comme lu"))
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
            new(new Version(0, 1, 9, 5), "0.1.9.5", new List<ChangelogLine>
            {
                new("Fix l'affichage de la bulle dans la liste du groupe."),
                new("Amélioration de l'ajout des utilisateurs via le bouton +."),
                new("Possibilité de mettre en pause individuellement des utilisateurs d'une syncshell."),
                new("Amélioration de la stabilité du plugin en cas de petite connexion / petite configuration."),
                new("Divers fix de l'interface."),
            }),
            new(new Version(0, 1, 9, 4), "0.1.9.4", new List<ChangelogLine>
            {
                new("Réécriture complète de la bulle de frappe avec la possibilité de choisir la taille de la bulle."),
                new("Désactivation de l'AutoDetect en zone instanciée."),
                new("Réécriture interface AutoDetect pour acceuillir les invitations en attente et préparer les synchsells publiques."),
                new("Amélioration de la compréhension des activations / désactivations des préférences de synchronisation par défaut."),
                new("Mise en avant du Self Analyse avec une alerte lorsqu'un seuil de donnée a été atteint."),
                new("Ajout de l'alerte de la non-compatibilité du plugin Chat2."),
                new("Divers fix de l'interface."),
            }),
            new(new Version(0, 1, 9, 3), "0.1.9.3", new List<ChangelogLine>
            {
                new("Correctif de l'affichage de la bulle de frappe quand l'interface est à + de 100%."),
            }),
            new(new Version(0, 1, 9, 2), "0.1.9.2", new List<ChangelogLine>
            {
                new("Correctif de l'affichage de la bulle de frappe."),
            }),
            new(new Version(0, 1, 9, 1), "0.1.9.1", new List<ChangelogLine>
            {
                new("Début correctif pour la bulle de frappe."),
                new("Les bascules de synchronisation n'affichent plus qu'une seule notification résumée."),

            }),
            new(new Version(0, 1, 9, 0), "0.1.9.0", new List<ChangelogLine>
            {
                new("Il est désormais possible de configurer par défaut nos choix de synchronisation (VFX, Music, Animation)."),
                new("La catégorie 'En attente' ne s'affiche uniquement que si une invitation est en attente"),
                new("(EN PRÉ VERSION) Il est désormais possible de voir quand une personne appairée est en train d'écrire avec une bulle qui s'affiche."),
                new("(EN PRÉ VERSION) La bulle de frappe s'affiche également sur votre propre plaque de nom lorsque vous écrivez."),
                new("Les bascules de synchronisation n'affichent plus qu'une seule notification résumée."),
                new("Correctif : Désormais, les invitation entrantes ne s'affichent qu'une seule fois au lieu de deux."),
            }),
            new(new Version(0, 1, 8, 2), "0.1.8.2", new List<ChangelogLine>
            {
                new("Détection Nearby : la liste rapide ne montre plus que les joueurs réellement invitables."),
                new("Sont filtrés automatiquement les personnes refusées ou déjà appairées."),
                new("Invitations Nearby : anti-spam de 5 minutes par personne, blocage 15 minutes après trois refus."),
                new("Affichage : Correction de l'affichage des notes par défaut plutôt que de l'ID si disponible."),
                new("Les notifications de blocage sont envoyées directement dans le tchat."),
                new("Overlay DTR : affiche le nombre d'invitations Nearby disponibles dans le titre et l'infobulle."),
                new("Poses Nearby : le filtre re-fonctionne avec vos notes locales pour retrouver les entrées correspondantes."),
            }),
            new(new Version(0, 1, 8, 1), "0.1.8.1", new List<ChangelogLine>
            {
                new("Correctif 'Vu sous' : l'infobulle affiche désormais le dernier personnage observé."),
                new("Invitations AutoDetect : triées en tête de liste pour mieux les repérer."),
                new("Invitations AutoDetect : conservées entre les redémarrages du plugin ou du jeu."),
                new("Barre de statut serveur : couleur violette adoptée par défaut."),
            }),
            new(new Version(0, 1, 8, 0), "0.1.8.0", new List<ChangelogLine>
            {
                new("AutoDetect : détection automatique des joueurs Umbra autour de vous et propositions d'appairage."),
                new("AutoDetect : désactivé par défaut pour préserver la confidentialité.", 1, ImGuiColors.DalamudGrey),
                new("AutoDetect : activez-le dans 'Transfers' avec les options Nearby detection et Allow pair requests.", 1, ImGuiColors.DalamudGrey),
                new("Syncshell temporaire : durée configurable de 1 h à 7 jours, expiration automatique."),
                new("Syncshell permanente : possibilité de nommer et d'organiser vos groupes sur la durée."),
                new("Interface : palette UmbraSync harmonisée et menus allégés pour l'usage RP."),
            }),
        };
    }

    private readonly record struct ChangelogEntry(Version Version, string VersionLabel, IReadOnlyList<ChangelogLine> Lines);

    private readonly record struct ChangelogLine(string Text, int IndentLevel = 0, System.Numerics.Vector4? Color = null);
}
