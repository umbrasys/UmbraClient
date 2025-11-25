using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Handlers;
using UmbraSync.Services;
using UmbraSync.Services.Mediator;
using UmbraSync.WebAPI.Files;
using UmbraSync.WebAPI.Files.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;
using UmbraSync.Localization;
using System;
using System.Globalization;

namespace UmbraSync.UI;

public class DownloadUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly ConcurrentDictionary<GameObjectHandler, Dictionary<string, FileDownloadStatus>> _currentDownloads = new();
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileUploadManager _fileTransferManager;
    private readonly UiSharedService _uiShared;
    private readonly ConcurrentDictionary<GameObjectHandler, bool> _uploadingPlayers = new();

    public DownloadUi(ILogger<DownloadUi> logger, DalamudUtilService dalamudUtilService, MareConfigService configService,
        FileUploadManager fileTransferManager, MareMediator mediator, UiSharedService uiShared, PerformanceCollectorService performanceCollectorService)
        : base(logger, mediator, Loc.Get("DownloadUi.WindowTitle"), performanceCollectorService)
    {
        _dalamudUtilService = dalamudUtilService;
        _configService = configService;
        _fileTransferManager = fileTransferManager;
        _uiShared = uiShared;

        SizeConstraints = new WindowSizeConstraints()
        {
            MaximumSize = new Vector2(500, 90),
            MinimumSize = new Vector2(500, 90),
        };

        Flags |= ImGuiWindowFlags.NoMove;
        Flags |= ImGuiWindowFlags.NoBackground;
        Flags |= ImGuiWindowFlags.NoInputs;
        Flags |= ImGuiWindowFlags.NoNavFocus;
        Flags |= ImGuiWindowFlags.NoResize;
        Flags |= ImGuiWindowFlags.NoScrollbar;
        Flags |= ImGuiWindowFlags.NoTitleBar;
        Flags |= ImGuiWindowFlags.NoDecoration;
        Flags |= ImGuiWindowFlags.NoFocusOnAppearing;

        DisableWindowSounds = true;

        ForceMainWindow = true;

        IsOpen = true;

        Mediator.Subscribe<DownloadStartedMessage>(this, (msg) => _currentDownloads[msg.DownloadId] = msg.DownloadStatus);
        Mediator.Subscribe<DownloadFinishedMessage>(this, (msg) => _currentDownloads.TryRemove(msg.DownloadId, out _));
        Mediator.Subscribe<GposeStartMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<GposeEndMessage>(this, (_) => IsOpen = true);
        Mediator.Subscribe<PlayerUploadingMessage>(this, (msg) =>
        {
            if (msg.IsUploading)
            {
                _uploadingPlayers[msg.Handler] = true;
            }
            else
            {
                _uploadingPlayers.TryRemove(msg.Handler, out _);
            }
        });
    }

    protected override void DrawInternal()
    {
        if (_configService.Current.ShowTransferWindow)
        {
            try
            {
                if (_fileTransferManager.CurrentUploads.Any())
                {
                    var currentUploads = _fileTransferManager.CurrentUploads.ToList();
                    var totalUploads = currentUploads.Count;

                    var doneUploads = currentUploads.Count(c => c.IsTransferred);
                    var totalUploaded = currentUploads.Sum(c => c.Transferred);
                    var totalToUpload = currentUploads.Sum(c => c.Total);

                    UiSharedService.DrawOutlinedFont($"▲", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                    ImGui.SameLine();
                    var xDistance = ImGui.GetCursorPosX();
                    UiSharedService.DrawOutlinedFont(string.Format(CultureInfo.CurrentCulture, Loc.Get("DownloadUi.Uploads.Status"), doneUploads, totalUploads),
                        ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                    ImGui.NewLine();
                    ImGui.SameLine(xDistance);
                    UiSharedService.DrawOutlinedFont(
                        $"{UiSharedService.ByteToString(totalUploaded, addSuffix: false)}/{UiSharedService.ByteToString(totalToUpload)}",
                        ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);

                    if (_currentDownloads.Any()) ImGui.Separator();
                }
            }
            catch
            {
                // ignore errors thrown from UI
            }

            try
            {
                foreach (var item in _currentDownloads.ToList())
                {
                    var dlSlot = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.WaitingForSlot);
                    var dlQueue = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.WaitingForQueue);
                    var dlProg = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.Downloading);
                    var dlDecomp = item.Value.Count(c => c.Value.DownloadStatus == DownloadStatus.Decompressing);
                    var totalFiles = item.Value.Sum(c => c.Value.TotalFiles);
                    var transferredFiles = item.Value.Sum(c => c.Value.TransferredFiles);
                    var totalBytes = item.Value.Sum(c => c.Value.TotalBytes);
                    var transferredBytes = item.Value.Sum(c => c.Value.TransferredBytes);

                    UiSharedService.DrawOutlinedFont($"▼", ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                    ImGui.SameLine();
                    var xDistance = ImGui.GetCursorPosX();
                    UiSharedService.DrawOutlinedFont(
                        string.Format(CultureInfo.CurrentCulture, Loc.Get("DownloadUi.Downloads.Status"), item.Key.Name, dlSlot, dlQueue, dlProg, dlDecomp),
                        ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                    ImGui.NewLine();
                    ImGui.SameLine(xDistance);
                    UiSharedService.DrawOutlinedFont(
                        $"{transferredFiles}/{totalFiles} ({UiSharedService.ByteToString(transferredBytes, addSuffix: false)}/{UiSharedService.ByteToString(totalBytes)})",
                        ImGuiColors.DalamudWhite, new Vector4(0, 0, 0, 255), 1);
                }
            }
            catch
            {
                // ignore errors thrown from UI
            }
        }

        if (_configService.Current.ShowTransferBars)
        {
            const int transparency = 100;
            const int dlBarBorder = 3;

            foreach (var transfer in _currentDownloads.ToList())
            {
                var screenPos = _dalamudUtilService.WorldToScreen(transfer.Key.GetGameObject());
                if (screenPos == Vector2.Zero) continue;

                var totalBytes = transfer.Value.Sum(c => c.Value.TotalBytes);
                var transferredBytes = transfer.Value.Sum(c => c.Value.TransferredBytes);
                var displayTotalBytes = Math.Max(totalBytes, transferredBytes);

                var maxDlText = $"{UiSharedService.ByteToString(displayTotalBytes, addSuffix: false)}/{UiSharedService.ByteToString(displayTotalBytes)}";
                var textSize = _configService.Current.TransferBarsShowText ? ImGui.CalcTextSize(maxDlText) : new Vector2(10, 10);

                int dlBarHeight = _configService.Current.TransferBarsHeight > ((int)textSize.Y + 5) ? _configService.Current.TransferBarsHeight : (int)textSize.Y + 5;
                int dlBarWidth = _configService.Current.TransferBarsWidth > ((int)textSize.X + 10) ? _configService.Current.TransferBarsWidth : (int)textSize.X + 10;

                var dlBarStart = new Vector2(screenPos.X - dlBarWidth / 2f, screenPos.Y - dlBarHeight / 2f);
                var dlBarEnd = new Vector2(screenPos.X + dlBarWidth / 2f, screenPos.Y + dlBarHeight / 2f);
                var drawList = ImGui.GetBackgroundDrawList();

                // Make the bar pill-shaped by using large rounding (half the height)
                var barRounding = dlBarHeight / 2f;

                // Outer shadow
                drawList.AddRectFilled(
                    dlBarStart with { X = dlBarStart.X - dlBarBorder - 1, Y = dlBarStart.Y - dlBarBorder - 1 },
                    dlBarEnd with { X = dlBarEnd.X + dlBarBorder + 1, Y = dlBarEnd.Y + dlBarBorder + 1 },
                    UiSharedService.Color(0, 0, 0, transparency), barRounding + dlBarBorder + 1);

                // Border
                drawList.AddRectFilled(
                    dlBarStart with { X = dlBarStart.X - dlBarBorder, Y = dlBarStart.Y - dlBarBorder },
                    dlBarEnd   with { X = dlBarEnd.X   + dlBarBorder, Y = dlBarEnd.Y   + dlBarBorder },
                    UiSharedService.Color(60, 50, 70, transparency), barRounding + dlBarBorder);

                // Track background
                drawList.AddRectFilled(
                    dlBarStart, dlBarEnd,
                    UiSharedService.Color(25, 22, 28, transparency), barRounding);
                var dlProgressPercent = displayTotalBytes == 0
                    ? 0
                    : Math.Min(transferredBytes / (double)displayTotalBytes, 1);
                // Filled progress with a left-to-right purple gradient
                var progressEndXRaw = dlBarStart.X + (float)(dlProgressPercent * dlBarWidth);
                static float SnapPx(float x) => MathF.Floor(x) + 0.5f; // align on pixel grid to avoid seams
                var progressEndX = SnapPx(MathF.Min(progressEndXRaw, dlBarEnd.X));
                var progressEnd = dlBarEnd with { X = progressEndX };

                // Clamp rounding for very small widths to keep a proper capsule look
                var progressWidth = Math.Max(0.001f, progressEndX - dlBarStart.X);
                var progressRounding = Math.Min(barRounding, progressWidth / 2f);

                // Gradient colors inspired by the mockup
                // Dynamic tint based on a gentle breathing, to add life without distraction
                // Réduction de l'animation « breathing » pour stabiliser la perception du dégradé
                var t = (float)(Math.Sin(ImGui.GetTime() * 0.8f) * 0.1 + 0.5); // amplitude ±0.1 autour de 0.5
                byte Lerp(byte a, byte b, float tt) => (byte)(a + (b - a) * tt);
                var leftColor  = UiSharedService.Color(Lerp(88, 96, t),  Lerp(66, 74, t),  Lerp(124, 130, t), transparency);   // dark purple (breathing)
                var rightColor = UiSharedService.Color(Lerp(168, 186, t), Lerp(120, 130, t), Lerp(210, 220, t), transparency);  // light purple (breathing)
                if (progressWidth <= 1.5f)
                {
                    drawList.AddRectFilled(dlBarStart, progressEnd, leftColor, progressRounding);
                }
                else
                {

                    drawList.AddRectFilled(dlBarStart, progressEnd, leftColor, progressRounding);

                    var inset = 0.6f; // léger retrait pour éviter tout liseré; ajusté pour réduire les artefacts
                    var insetStart = new Vector2(SnapPx(dlBarStart.X + inset), dlBarStart.Y + inset);
                    var insetEnd   = new Vector2(SnapPx(progressEndX - inset), dlBarEnd.Y - inset);

                    if (insetEnd.X > insetStart.X + 0.5f && insetEnd.Y > insetStart.Y + 0.5f)
                    {
                        drawList.PushClipRect(dlBarStart, progressEnd, true);
                        try
                        {
                            drawList.AddRectFilledMultiColor(
                                insetStart,
                                insetEnd,
                                leftColor, 
                                rightColor,
                                rightColor, 
                                leftColor  
                            );
                        }
                        catch
                        {
                            // Fallback ultra-simple: bandeau vertical en 1 px aligné sur la grille
                            // (au cas où l'API n'existerait pas sur un runtime spécifique)
                            static float Snap(float x) => MathF.Floor(x) + 0.5f;
                            var width = insetEnd.X - insetStart.X;
                            var x = insetStart.X;
                            while (x < insetEnd.X)
                            {
                                var next = MathF.Min(x + 1f, insetEnd.X);
                                var mx = (Snap(x) + Snap(next)) * 0.5f;
                                var k = Math.Clamp((mx - insetStart.X) / Math.Max(1e-3f, width), 0f, 1f);
                                byte L(byte a, byte b, float kk) => (byte)(a + (b - a) * kk);
                                var col = UiSharedService.Color(
                                    L((byte)((leftColor >> 0) & 0xFF),  (byte)((rightColor >> 0) & 0xFF),  k),
                                    L((byte)((leftColor >> 8) & 0xFF),  (byte)((rightColor >> 8) & 0xFF),  k),
                                    L((byte)((leftColor >> 16) & 0xFF), (byte)((rightColor >> 16) & 0xFF), k),
                                    (byte)transparency);
                                drawList.AddRectFilled(new Vector2(x, insetStart.Y), new Vector2(next, insetEnd.Y), col);
                                x = next;
                            }
                        }
                        finally
                        {
                            drawList.PopClipRect();
                        }
                    }
                }

                // Subtle top gloss over the entire bar (very low alpha)
                // We draw it after the fill so it overlays both unfilled track and filled part.
                var glossTop    = dlBarStart;
                var glossBottom = dlBarStart with { Y = dlBarStart.Y + dlBarHeight * 0.55f };
                var glossAlphaTop = 22;   // faint white
                var glossAlphaMid = 8;
                var glossAlphaBot = 0;
                try
                {
                    drawList.AddRectFilledMultiColor(
                        glossTop,
                        glossBottom,
                        UiSharedService.Color(255, 255, 255, (byte)glossAlphaTop),
                        UiSharedService.Color(255, 255, 255, (byte)glossAlphaTop),
                        UiSharedService.Color(255, 255, 255, (byte)glossAlphaBot),
                        UiSharedService.Color(255, 255, 255, (byte)glossAlphaBot)
                    );
                }
                catch
                {
                    // Fallback: simple translucent strip
                    drawList.AddRectFilled(glossTop, glossBottom, UiSharedService.Color(255, 255, 255, (byte)glossAlphaMid), barRounding);
                }

                // Light shimmer sweep across the filled portion
                if (progressWidth > 6f)
                {
                    var time = (float)ImGui.GetTime();
                    var sweepWidth = Math.Max(12f, dlBarWidth * 0.15f);
                    var sweepSpeed = Math.Max(30f, dlBarWidth * 0.8f); // px per second
                    var sweepOffset = (time * sweepSpeed) % (progressWidth + sweepWidth);
                    var sweepStartX = dlBarStart.X + sweepOffset - sweepWidth;
                    var sweepEndX = sweepStartX + sweepWidth;

                    // Clamp sweep within the filled region
                    sweepStartX = MathF.Max(sweepStartX, dlBarStart.X);
                    sweepEndX   = MathF.Min(sweepEndX, progressEndX);
                    if (sweepEndX > sweepStartX + 2f)
                    {
                        var sweepStart = new Vector2(sweepStartX, dlBarStart.Y + 1);
                        var sweepEnd   = new Vector2(sweepEndX,   dlBarEnd.Y   - 1);
                        var edge = UiSharedService.Color(255, 255, 255, 18);
                        var mid  = UiSharedService.Color(255, 255, 255, 38);
                        // draw: edge -> mid -> edge by using two quads
                        var midX = (sweepStartX + sweepEndX) / 2f;
                        // Clip le shimmer à la zone de progression
                        drawList.PushClipRect(dlBarStart, progressEnd, true);
                        try
                        {
                            // Nouveau shimmer sans couture: superposition de 3 bandes centrées, aux alphas progressifs
                            // 1) Base douce sur toute la largeur
                            drawList.AddRectFilled(sweepStart, sweepEnd, UiSharedService.Color(255, 255, 255, 16), 0f);

                            // 2) Bande médiane (50% de la largeur), un peu plus opaque
                            var midWidth = (sweepEndX - sweepStartX) * 0.5f;
                            var midStartX = SnapPx(((sweepStartX + sweepEndX) * 0.5f) - midWidth * 0.5f);
                            var midEndX   = SnapPx(midStartX + midWidth);
                            if (midEndX > midStartX + 1f)
                            {
                                drawList.AddRectFilled(new Vector2(midStartX, sweepStart.Y), new Vector2(midEndX, sweepEnd.Y), UiSharedService.Color(255, 255, 255, 28), 0f);
                            }

                            // 3) Cœur fin (20% de la largeur), le plus lumineux
                            var coreWidth = (sweepEndX - sweepStartX) * 0.2f;
                            var coreStartX = SnapPx(((sweepStartX + sweepEndX) * 0.5f) - coreWidth * 0.5f);
                            var coreEndX   = SnapPx(coreStartX + coreWidth);
                            if (coreEndX > coreStartX + 1f)
                            {
                                drawList.AddRectFilled(new Vector2(coreStartX, sweepStart.Y), new Vector2(coreEndX, sweepEnd.Y), UiSharedService.Color(255, 255, 255, 44), 0f);
                            }
                        }
                        catch
                        {
                            // Fallback simple: bande unique si MultiColor indisponible (peu probable)
                            drawList.AddRectFilled(sweepStart, sweepEnd, UiSharedService.Color(255, 255, 255, 22), 0f);
                        }
                        finally
                        {
                            drawList.PopClipRect();
                        }
                    }
                }

                // Affichage du texte seulement avant 100 %. À 100 %, on affiche un "spark" centré et on masque le texte.
                var showProgressText = _configService.Current.TransferBarsShowText && dlProgressPercent < 1.0 - double.Epsilon;
                if (showProgressText)
                {
                    var downloadText = $"{UiSharedService.ByteToString(transferredBytes, addSuffix: false)}/{UiSharedService.ByteToString(displayTotalBytes)}";
                    UiSharedService.DrawOutlinedFont(drawList, downloadText,
                        screenPos with { X = screenPos.X - textSize.X / 2f - 1, Y = screenPos.Y - textSize.Y / 2f - 1 },
                        UiSharedService.Color(255, 255, 255, transparency),
                        UiSharedService.Color(0, 0, 0, transparency), 1);
                }

                // Spark centré lorsque la barre atteint 100 % (texte masqué à ce moment-là)
                if (dlProgressPercent >= 1.0 - double.Epsilon)
                {
                    var time = (float)ImGui.GetTime();
                    // Spark doux et centré (pulse) pendant un court instant
                    var center = new Vector2((dlBarStart.X + dlBarEnd.X) / 2f, (dlBarStart.Y + dlBarEnd.Y) / 2f);
                    var pulse = (MathF.Sin(time * 6.0f) * 0.5f + 0.5f); // 0..1
                    var baseR = Math.Min(4f, dlBarHeight * 0.22f);
                    var outerR = baseR + 2f + pulse * 2f;
                    var colInner = UiSharedService.Color(255, 245, 220, 170);
                    var colOuter = UiSharedService.Color(200, 160, 255, 75);
                    drawList.AddCircleFilled(center, baseR + pulse, colInner);
                    drawList.AddCircle(center, outerR, colOuter, 48, 2f);
                }
            }

            if (_configService.Current.ShowUploading)
            {
                foreach (var player in _uploadingPlayers.Select(p => p.Key).ToList())
                {
                    var screenPos = _dalamudUtilService.WorldToScreen(player.GetGameObject());
                    if (screenPos == Vector2.Zero) continue;

                    try
                    {
                        using var _ = _uiShared.UidFont.Push();
                        var uploadText = Loc.Get("DownloadUi.UploadingLabel");

                        var textSize = ImGui.CalcTextSize(uploadText);

                        var drawList = ImGui.GetBackgroundDrawList();
                        UiSharedService.DrawOutlinedFont(drawList, uploadText,
                            screenPos with { X = screenPos.X - textSize.X / 2f - 1, Y = screenPos.Y - textSize.Y / 2f - 1 },
                            UiSharedService.Color(255, 255, 0, transparency),
                            UiSharedService.Color(0, 0, 0, transparency), 2);
                    }
                    catch
                    {
                        // ignore errors thrown on UI
                    }
                }
            }
        }
    }

    public override bool DrawConditions()
    {
        if (_uiShared.EditTrackerPosition) return true;
        if (!_configService.Current.ShowTransferWindow && !_configService.Current.ShowTransferBars) return false;
        if (!_currentDownloads.Any() && !_fileTransferManager.CurrentUploads.Any() && !_uploadingPlayers.Any()) return false;
        if (!IsOpen) return false;
        return true;
    }

    public override void PreDraw()
    {
        base.PreDraw();

        if (_uiShared.EditTrackerPosition)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
            Flags &= ~ImGuiWindowFlags.NoBackground;
            Flags &= ~ImGuiWindowFlags.NoInputs;
            Flags &= ~ImGuiWindowFlags.NoResize;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
            Flags |= ImGuiWindowFlags.NoBackground;
            Flags |= ImGuiWindowFlags.NoInputs;
            Flags |= ImGuiWindowFlags.NoResize;
        }

        var maxHeight = ImGui.GetTextLineHeight() * (_configService.Current.ParallelDownloads + 3);
        SizeConstraints = new()
        {
            MinimumSize = new Vector2(300, maxHeight),
            MaximumSize = new Vector2(300, maxHeight),
        };
    }
}
