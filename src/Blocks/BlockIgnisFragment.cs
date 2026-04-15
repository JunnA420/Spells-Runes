using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Client.Tesselation;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Blocks;

public class BlockIgnisFragment : Block
{
    private const string CrystalTextureCode = "ignis-fragment.png";
    private const string EmissiveTextureCode = "ignis-fragment-emissive.png";
    private const string PedestalTextureCode = "ignis-fragment-pedestal.png";
    private static readonly AssetLocation CrystalTexturePath = new("spellsandrunes", "block/ignis-fragment");
    private static readonly AssetLocation EmissiveTexturePath = new("spellsandrunes", "block/ignis-fragment-emissive");
    private ICoreClientAPI? capi;
    private Shape? worldShape;

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);

        if (api is not ICoreClientAPI clientApi || Shape?.Base == null) return;

        capi = clientApi;
        worldShape = clientApi.TesselatorManager.GetCachedShape(Shape.Base);
    }

    public override void OnJsonTesselation(ref MeshData sourceMesh, ref int[] lightRgbsByCorner, BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
    {
        base.OnJsonTesselation(ref sourceMesh, ref lightRgbsByCorner, pos, chunkExtBlocks, extIndex3d);

        if (capi == null || worldShape == null) return;

        var pedestalTexturePath = ResolvePedestalTexture(pos, chunkExtBlocks, extIndex3d) ?? CrystalTexturePath;

        var textures = new Dictionary<string, CompositeTexture>
        {
            [CrystalTextureCode] = new CompositeTexture(CrystalTexturePath),
            [EmissiveTextureCode] = new CompositeTexture(EmissiveTexturePath),
            [PedestalTextureCode] = new CompositeTexture(pedestalTexturePath)
        };

        var texSource = new ShapeTextureSource(capi, worldShape, $"{Code}-world", textures, path => path);
        capi.Tesselator.TesselateShape("block", worldShape, out sourceMesh, texSource);
        ApplyRandomRotation(ref sourceMesh, pos);
    }

    private static void ApplyRandomRotation(ref MeshData mesh, BlockPos pos)
    {
        unchecked
        {
            int hash = pos.X * 73856093 ^ pos.Y * 19349663 ^ pos.Z * 83492791;
            float angleRad = (hash & 2047) / 2047f * GameMath.TWOPI;
            mesh.Rotate(new Vec3f(0.5f, 0f, 0.5f), 0f, angleRad, 0f);
        }
    }

    private AssetLocation? ResolvePedestalTexture(BlockPos pos, Block[] chunkExtBlocks, int extIndex3d)
    {
        Block? belowBlock = null;

        if (chunkExtBlocks != null && extIndex3d > 0)
        {
            belowBlock = chunkExtBlocks[extIndex3d + TileSideEnum.MoveIndex[TileSideEnum.Down]];
        }

        belowBlock ??= api.World.BlockAccessor.GetBlock(pos.DownCopy());
        if (belowBlock == null || belowBlock.Id == 0) return null;

        return ResolveTopTexturePath(belowBlock);
    }

    private static AssetLocation? ResolveTopTexturePath(Block block)
    {
        var textures = block.Textures;
        if (textures == null || textures.Count == 0) return null;

        if (TryGetTextureBase(textures, "up", out var texturePath)) return texturePath;
        if (TryGetTextureBase(textures, "top", out texturePath)) return texturePath;
        if (TryGetTextureBase(textures, "north", out texturePath)) return texturePath;
        if (TryGetTextureBase(textures, "all", out texturePath)) return texturePath;

        foreach (var texture in textures.Values)
        {
            if (texture?.Base != null) return texture.Base;
        }

        return null;
    }

    private static bool TryGetTextureBase(IDictionary<string, CompositeTexture> textures, string key, out AssetLocation? texturePath)
    {
        texturePath = null;
        if (!textures.TryGetValue(key, out var texture) || texture?.Base == null) return false;

        texturePath = texture.Base;
        return true;
    }
}
