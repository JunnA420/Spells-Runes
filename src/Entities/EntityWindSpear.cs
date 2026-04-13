using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using SpellsAndRunes.Spells.Air;

namespace SpellsAndRunes.Entities;

public class EntityWindSpear : EntityProjectile
{
    // spawn anim is 24 frames at 30fps = 0.8s
    private const int SpawnDurationMs = 800;
    private long idleCallbackId;

    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        PlaySpawn();
        TrySpawnFx();
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
        PlaySpawn();
        TrySpawnFx();
    }

    public void RestartSpawnAnimation()
    {
        PlaySpawn();
    }

    private void PlaySpawn()
    {
        if (idleCallbackId != 0) Api.Event.UnregisterCallback(idleCallbackId);

        AnimManager?.StopAnimation("wind_spear_idle");
        AnimManager?.StartAnimation(new AnimationMetaData
        {
            Code           = "wind_spear_spawn",
            Animation      = "wind_spear_spawn",
            AnimationSpeed = 1f,
            EaseInSpeed    = 999f,
            EaseOutSpeed   = 999f,
            Weight         = 1f,
        });

        idleCallbackId = Api.Event.RegisterCallback(_ => PlayIdle(), SpawnDurationMs);
    }


    private void PlayIdle()
    {
        if (!Alive) return;
        idleCallbackId = 0;
        AnimManager?.StopAnimation("wind_spear_spawn");
        AnimManager?.StartAnimation(new AnimationMetaData
        {
            Code           = "wind_spear_idle",
            Animation      = "wind_spear_idle",
            AnimationSpeed = 1.5f,
            EaseInSpeed    = 10f,
            EaseOutSpeed   = 10f,
            Weight         = 1f,
        });
    }

    private void TrySpawnFx()
    {
        if (Api == null || Api.Side != EnumAppSide.Client) return;
        Vec3d forward = DirectionFromYawPitch(Pos.Yaw, Pos.Pitch);
        WindSpear.SpawnFx(Api.World, Pos.XYZ, forward, 1, 2f);
    }

    private static Vec3d DirectionFromYawPitch(float yaw, float pitch)
    {
        double cosPitch = System.Math.Cos(pitch);
        return new Vec3d(
            System.Math.Sin(yaw) * cosPitch,
            -System.Math.Sin(pitch),
            System.Math.Cos(yaw) * cosPitch);
    }
}
