using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation;
using FFXIVClientStructs.Havok.Common.Base.Types;
using FFXIVClientStructs.Havok.Common.Serialize.Resource;
using FFXIVClientStructs.Havok.Common.Serialize.Util;
using Lumina.Data;
using UmbraSync.FileCache;
using UmbraSync.Interop.GameModel;
using UmbraSync.MareConfiguration;
using UmbraSync.PlayerData.Handlers;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;
#pragma warning disable CS8500 // direct pointer access into Havok structures

namespace UmbraSync.Services;

public sealed class XivDataAnalyzer
{
    private readonly ILogger<XivDataAnalyzer> _logger;
    private readonly FileCacheManager _fileCacheManager;
    private readonly XivDataStorageService _configService;
    private readonly List<string> _failedCalculatedTris = [];
    private readonly List<string> _failedCalculatedTex = [];

    public XivDataAnalyzer(ILogger<XivDataAnalyzer> logger, FileCacheManager fileCacheManager,
        XivDataStorageService configService)
    {
        _logger = logger;
        _fileCacheManager = fileCacheManager;
        _configService = configService;
    }

    public unsafe Dictionary<string, List<ushort>>? GetSkeletonBoneIndices(GameObjectHandler handler)
    {
        if (handler.Address == nint.Zero) return null;
        var chara = (CharacterBase*)(((Character*)handler.Address)->GameObject.DrawObject);
        if (chara->GetModelType() != CharacterBase.ModelType.Human) return null;
        var resHandles = chara->Skeleton->SkeletonResourceHandles;
        Dictionary<string, List<ushort>> outputIndices = [];
        try
        {
            for (int i = 0; i < chara->Skeleton->PartialSkeletonCount; i++)
            {
                var handle = *(resHandles + i);
                _logger.LogTrace("Iterating over SkeletonResourceHandle #{i}:{x}", i, ((nint)handle).ToString("X", CultureInfo.InvariantCulture));
                if ((nint)handle == nint.Zero) continue;
                var curBones = handle->BoneCount;
                // this is unrealistic, the filename shouldn't ever be that long
                if (handle->FileName.Length > 1024) continue;
                var skeletonName = handle->FileName.ToString();
                if (string.IsNullOrEmpty(skeletonName)) continue;
                outputIndices[skeletonName] = new();
                for (ushort boneIdx = 0; boneIdx < curBones; boneIdx++)
                {
                    var boneName = handle->HavokSkeleton->Bones[boneIdx].Name.String;
                    if (boneName == null) continue;
                    outputIndices[skeletonName].Add((ushort)(boneIdx + 1));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not process skeleton data");
        }

        return (outputIndices.Count != 0 && outputIndices.Values.All(u => u.Count > 0)) ? outputIndices : null;
    }

    public unsafe Dictionary<string, List<ushort>>? GetBoneIndicesFromPap(string hash)
    {
        if (_configService.Current.BonesDictionary.TryGetValue(hash, out var bones)) return bones;

        var cacheEntity = _fileCacheManager.GetFileCacheByHash(hash, preferSubst: true);
        if (cacheEntity == null) return null;

        using BinaryReader reader = new BinaryReader(File.Open(cacheEntity.ResolvedFilepath, FileMode.Open, FileAccess.Read, FileShare.Read));

        // most of this shit is from vfxeditor, surely nothing will change in the pap format :copium:
        reader.ReadInt32(); // ignore
        reader.ReadInt32(); // ignore
        reader.ReadInt16(); // read 2 (num animations)
        reader.ReadInt16(); // read 2 (modelid)
        var type = reader.ReadByte();// read 1 (type)
        if (type != 0) return null; // it's not human, just ignore it, whatever

        reader.ReadByte(); // read 1 (variant)
        reader.ReadInt32(); // ignore
        var havokPosition = reader.ReadInt32();
        var footerPosition = reader.ReadInt32();
        var havokDataSize = footerPosition - havokPosition;
        reader.BaseStream.Position = havokPosition;
        var havokData = reader.ReadBytes(havokDataSize);
        if (havokData.Length <= 8) return null; // no havok data

        var output = new Dictionary<string, List<ushort>>(StringComparer.OrdinalIgnoreCase);
        var tempHavokDataPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()) + ".hkx";
        try
        {
            var typeInfoRegistry = hkBuiltinTypeRegistry.Instance()->GetTypeInfoRegistry();
            var classNameRegistry = hkBuiltinTypeRegistry.Instance()->GetClassNameRegistry();
            File.WriteAllBytes(tempHavokDataPath, havokData);

            var loadoptions = stackalloc hkSerializeUtil.LoadOptions[1];
            loadoptions->TypeInfoRegistry = typeInfoRegistry;
            loadoptions->ClassNameRegistry = classNameRegistry;
            loadoptions->Flags = new hkFlags<hkSerializeUtil.LoadOptionBits, int>
            {
                Storage = (int)(hkSerializeUtil.LoadOptionBits.Default)
            };

            var pathBytes = Encoding.ASCII.GetBytes(tempHavokDataPath + "\0");
            hkResource* resource;
            fixed (byte* pathPtr = pathBytes)
            {
                resource = hkSerializeUtil.LoadFromFile(pathPtr, null, loadoptions);
            }
            if (resource == null)
            {
                throw new InvalidOperationException("Resource was null after loading");
            }

            var container = (hkRootLevelContainer*)resource->GetContentsPointer("hkRootLevelContainer", typeInfoRegistry);
            if (container == null)
                throw new InvalidOperationException("Failed to get hkRootLevelContainer");
            var animContainer = (hkaAnimationContainer*)container->findObjectByName("hkaAnimationContainer", null);
            if (animContainer == null)
                throw new InvalidOperationException("Failed to find hkaAnimationContainer");
            for (int i = 0; i < animContainer->Bindings.Length; i++)
            {
                var binding = animContainer->Bindings[i].ptr;
                var boneTransform = binding->TransformTrackToBoneIndices;
                string name = binding->OriginalSkeletonName.String! + "_" + i;
                output[name] = [];
                for (int boneIdx = 0; boneIdx < boneTransform.Length; boneIdx++)
                {
                    output[name].Add((ushort)boneTransform[boneIdx]);
                }
                output[name].Sort();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load havok file in {path}", tempHavokDataPath);
        }
        finally
        {
            File.Delete(tempHavokDataPath);
        }

        _configService.Current.BonesDictionary[hash] = output;
        _configService.Save();
        return output;
    }

    public long GetTrianglesByHash(string hash)
    {
        if (_configService.Current.TriangleDictionary.TryGetValue(hash, out var cachedTris) && cachedTris > 0)
            return cachedTris;

        if (_failedCalculatedTris.Contains(hash, StringComparer.Ordinal))
            return 0;

        var path = _fileCacheManager.GetFileCacheByHash(hash, preferSubst: true);
        if (path == null || !path.ResolvedFilepath.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
            return 0;

        var filePath = path.ResolvedFilepath;

        try
        {
            _logger.LogDebug("Detected Model File {path}, calculating Tris", filePath);
            var file = new MdlFile(filePath);
            if (file.LodCount <= 0)
            {
                _failedCalculatedTris.Add(hash);
                _configService.Current.TriangleDictionary[hash] = 0;
                _configService.Save();
                return 0;
            }

            long tris = 0;
            for (int i = 0; i < file.LodCount; i++)
            {
                try
                {
                    var meshIdx = file.Lods[i].MeshIndex;
                    var meshCnt = file.Lods[i].MeshCount;
                    tris = file.Meshes.Skip(meshIdx).Take(meshCnt).Sum(p => p.IndexCount) / 3;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not load lod mesh {mesh} from path {path}", i, filePath);
                    continue;
                }

                if (tris > 0)
                {
                    _logger.LogDebug("TriAnalysis: {filePath} => {tris} triangles", filePath, tris);
                    _configService.Current.TriangleDictionary[hash] = tris;
                    _configService.Save();
                    break;
                }
            }

            return tris;
        }
        catch (Exception e)
        {
            _failedCalculatedTris.Add(hash);
            _configService.Current.TriangleDictionary[hash] = 0;
            _configService.Save();
            _logger.LogWarning(e, "Could not parse file {file}", filePath);
            return 0;
        }
    }

    public (uint Format, int MipCount, ushort Width, ushort Height) GetTexFormatByHash(string hash)
    {
        if (_configService.Current.TexDictionary.TryGetValue(hash, out var cachedTex) && cachedTex.Mip0Size > 0)
            return cachedTex;

        if (_failedCalculatedTex.Contains(hash, StringComparer.Ordinal))
            return default;

        var path = _fileCacheManager.GetFileCacheByHash(hash);
        if (path == null || !path.ResolvedFilepath.EndsWith(".tex", StringComparison.OrdinalIgnoreCase))
            return default;

        var filePath = path.ResolvedFilepath;

        try
        {
            _logger.LogDebug("Detected Texture File {path}, reading header", filePath);
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var r = new LuminaBinaryReader(stream);
            var texHeader = r.ReadStructure<Lumina.Data.Files.TexFile.TexHeader>();

            if (texHeader.Format == default || texHeader.MipCount == 0 || texHeader.ArraySize != 0 || texHeader.MipCount > 13)
            {
                _failedCalculatedTex.Add(hash);
                _configService.Current.TexDictionary[hash] = default;
                _configService.Save();
                return default;
            }

            return ((uint)texHeader.Format, texHeader.MipCount, texHeader.Width, texHeader.Height);
        }
        catch (Exception e)
        {
            _failedCalculatedTex.Add(hash);
            _configService.Current.TriangleDictionary[hash] = 0;
            _configService.Save();
            _logger.LogWarning(e, "Could not parse file {file}", filePath);
            return default;
        }
    }
}
#pragma warning restore CS8500
