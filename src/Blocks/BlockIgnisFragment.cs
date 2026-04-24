using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Client.Tesselation;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SpellsAndRunes.Blocks;

public class BlockIgnisFragment : Block
{
    internal const string CrystalTextureCode = "ignis-fragment.png";
    internal const string EmissiveTextureCode = "ignis-fragment-emissive.png";
    internal const string PedestalTextureCode = "ignis-fragment-pedestal.png";
    internal static readonly AssetLocation CrystalTexturePath = new("spellsandrunes", "block/ignis-fragment");
    internal static readonly AssetLocation EmissiveTexturePath = new("spellsandrunes", "block/ignis-fragment-emissive");
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

        var positionVariant = GetPositionVariant();
        var attachmentFace = GetAttachmentFace(positionVariant);
        var pedestalTexturePath = ResolvePedestalTexture(pos, chunkExtBlocks, extIndex3d, attachmentFace) ?? CrystalTexturePath;

        var textures = new Dictionary<string, CompositeTexture>
        {
            [CrystalTextureCode] = new CompositeTexture(CrystalTexturePath),
            [EmissiveTextureCode] = new CompositeTexture(EmissiveTexturePath),
            [PedestalTextureCode] = new CompositeTexture(pedestalTexturePath)
        };

        var texSource = new ShapeTextureSource(capi, worldShape, $"{Code}-world", textures, path => path);
        capi.Tesselator.TesselateShape("block", worldShape, out sourceMesh, texSource);
        ApplyAttachmentRotation(ref sourceMesh, positionVariant);
        ApplyRandomRotation(ref sourceMesh, pos, attachmentFace);
    }

    internal string GetPositionVariant()
    {
        return Variant != null && Variant.TryGetValue("position", out var positionCode)
            ? positionCode
            : "up";
    }

    internal static BlockFacing GetAttachmentFace(string positionVariant)
    {
        return positionVariant switch
        {
            "down" => BlockFacing.DOWN,
            "north" => BlockFacing.NORTH,
            "east" => BlockFacing.EAST,
            "south" => BlockFacing.SOUTH,
            "west" => BlockFacing.WEST,
            _ => BlockFacing.UP
        };
    }

    internal static Vec3f GetMeshRotationDegrees(BlockPos pos, string positionVariant)
    {
        float randomAngleDeg;

        unchecked
        {
            int hash = pos.X * 73856093 ^ pos.Y * 19349663 ^ pos.Z * 83492791;
            randomAngleDeg = (hash & 2047) / 2047f * 360f;
        }

        return positionVariant switch
        {
            "down" => new Vec3f(180f, randomAngleDeg, 0f),
            "north" => new Vec3f(-90f, 0f, randomAngleDeg),
            "east" => new Vec3f(randomAngleDeg, 0f, -90f),
            "south" => new Vec3f(90f, 0f, randomAngleDeg),
            "west" => new Vec3f(randomAngleDeg, 0f, 90f),
            _ => new Vec3f(0f, randomAngleDeg, 0f)
        };
    }

    internal static float[] CreateAnimationTransform(BlockPos pos, string positionVariant)
    {
        float angleRad;

        unchecked
        {
            int hash = pos.X * 73856093 ^ pos.Y * 19349663 ^ pos.Z * 83492791;
            angleRad = (hash & 2047) / 2047f * GameMath.TWOPI;
        }

        var attachmentFace = GetAttachmentFace(positionVariant);
        var transform = Mat4f.Create();

        Mat4f.Identity(transform);
        Mat4f.Translate(transform, transform, 0.5f, 0.5f, 0.5f);

        switch (positionVariant)
        {
            case "down":
                Mat4f.RotateX(transform, transform, GameMath.PI);
                break;
            case "north":
                Mat4f.RotateX(transform, transform, -GameMath.PIHALF);
                break;
            case "east":
                Mat4f.RotateZ(transform, transform, -GameMath.PIHALF);
                break;
            case "south":
                Mat4f.RotateX(transform, transform, GameMath.PIHALF);
                break;
            case "west":
                Mat4f.RotateZ(transform, transform, GameMath.PIHALF);
                break;
        }

        if (attachmentFace == BlockFacing.UP || attachmentFace == BlockFacing.DOWN)
        {
            Mat4f.RotateY(transform, transform, angleRad);
        }

        Mat4f.Translate(transform, transform, -0.5f, -0.5f, -0.5f);

        return transform;
    }

    private static void ApplyAttachmentRotation(ref MeshData mesh, string positionVariant)
    {
        var center = new Vec3f(0.5f, 0.5f, 0.5f);

        switch (positionVariant)
        {
            case "down":
                mesh.Rotate(center, GameMath.PI, 0f, 0f);
                break;
            case "north":
                mesh.Rotate(center, -GameMath.PIHALF, 0f, 0f);
                break;
            case "east":
                mesh.Rotate(center, 0f, 0f, -GameMath.PIHALF);
                break;
            case "south":
                mesh.Rotate(center, GameMath.PIHALF, 0f, 0f);
                break;
            case "west":
                mesh.Rotate(center, 0f, 0f, GameMath.PIHALF);
                break;
        }
    }

    private static void ApplyRandomRotation(ref MeshData mesh, BlockPos pos, BlockFacing attachmentFace)
    {
        unchecked
        {
            int hash = pos.X * 73856093 ^ pos.Y * 19349663 ^ pos.Z * 83492791;
            float angleRad = (hash & 2047) / 2047f * GameMath.TWOPI;
            var center = new Vec3f(0.5f, 0.5f, 0.5f);

            if (attachmentFace == BlockFacing.EAST || attachmentFace == BlockFacing.WEST)
            {
                return;
            }

            if (attachmentFace == BlockFacing.NORTH || attachmentFace == BlockFacing.SOUTH)
            {
                return;
            }

            mesh.Rotate(center, 0f, angleRad, 0f);
        }
    }

    internal AssetLocation? ResolvePedestalTexture(BlockPos pos, Block[] chunkExtBlocks, int extIndex3d, BlockFacing attachmentFace)
    {
        var supportOffset = attachmentFace.Opposite;
        Block? supportBlock = null;

        if (chunkExtBlocks != null && extIndex3d > 0)
        {
            supportBlock = chunkExtBlocks[extIndex3d + TileSideEnum.MoveIndex[supportOffset.Index]];
        }

        supportBlock ??= api.World.BlockAccessor.GetBlock(pos.AddCopy(supportOffset));
        if (supportBlock == null || supportBlock.Id == 0) return null;

        return ResolveSupportTexturePath(supportBlock, attachmentFace);
    }

    private static AssetLocation? ResolveSupportTexturePath(Block block, BlockFacing supportSurface)
    {
        var textures = block.Textures;
        if (textures == null || textures.Count == 0) return null;

        if (TryGetTextureBase(textures, supportSurface.Code, out var texturePath)) return texturePath;

        if (supportSurface == BlockFacing.UP)
        {
            if (TryGetTextureBase(textures, "top", out texturePath)) return texturePath;
        }
        else if (supportSurface == BlockFacing.DOWN)
        {
            if (TryGetTextureBase(textures, "bottom", out texturePath)) return texturePath;
        }
        else
        {
            if (TryGetTextureBase(textures, "side", out texturePath)) return texturePath;
        }

        if (TryGetTextureBase(textures, "up", out texturePath)) return texturePath;
        if (TryGetTextureBase(textures, "down", out texturePath)) return texturePath;
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
