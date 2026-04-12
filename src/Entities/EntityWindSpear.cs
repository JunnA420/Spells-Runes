using Vintagestory.API.Common;
using Vintagestory.GameContent;

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
    }

    public override void OnEntityLoaded()
    {
        base.OnEntityLoaded();
        PlaySpawn();
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
}
