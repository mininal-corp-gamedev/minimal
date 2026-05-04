using Sandbox;
using Sandbox.Utility;
using System;
using System.Collections.Generic;

public sealed class PhygunViewmodel : Component, Component.ExecuteInEditor
{
    [Property] public List<SpriteRenderer> TipSprites { get; set; } = new();
    [Property] public ParticleEffect GlowEffect { get; set; }
    [Property] public ParticleEffect SparksEffect { get; set; }
    [Property] public Material TubeFxMaterial { get; set; }
    [Property] public Material BottleMaterial { get; set; }

    [Property] public bool BeamActive { get; set; }
    [Property] public Color GravTint { get; set; } = new(1f, 0.8f, 0f);
    [Property] public Color PhysTint { get; set; } = new(0f, 0.68333f, 1f);

    private float _tintFrac;
    private Color _effectsTint;
    private float _scroll;
    private float _scrollSpeed;
    private float _scrollSpeedVel;
    private bool _wasActive;

    protected override void OnUpdate()
    {
        //var physgun = GameObject.Root.Components.Get<Minimal.Weapons.WeaponPhysgun>(FindMode.EverythingInSelfAndDescendants);
        //if (physgun.IsValid())
        //{
        //    BeamActive = physgun.BeamActive;
        //    _tintFrac = MathX.Approach(_tintFrac, physgun.PullActive ? 1f : 0f, Time.Delta * 5f);
        //    _effectsTint = Color.Lerp(PhysTint, GravTint, SteepEase(_tintFrac));
        //}
        //else
        //{
        //    _tintFrac = 0f;
        //    _effectsTint = PhysTint;
        //}

        //UpdateGlowEffect();
        //UpdateTipSprites();
        //UpdateTubeFx();
        //UpdateSparks();
        //UpdateBottleGlow();
    }

    private static float SteepEase(float value)
    {
        return value < 0.5f
            ? 8f * value * value * value * value
            : 1f - 8f * (1f - value) * (1f - value) * (1f - value) * (1f - value);
    }

    private void UpdateTubeFx()
    {
        if (TubeFxMaterial is null) return;

        _scrollSpeed = MathX.SmoothDamp(_scrollSpeed, BeamActive ? 2f : 0.2f, ref _scrollSpeedVel,
            BeamActive ? 0.5f : 2.5f, Time.Delta);
        _scroll += _scrollSpeed * Time.Delta;

        TubeFxMaterial.Set("g_vSelfIllumOffset", new Vector2(_scroll % 1f, 0f));
        TubeFxMaterial.Set("g_flSelfIllumBrightness", 3f * (_scrollSpeed + 1.5f));
        TubeFxMaterial.Set("g_vSelfIllumTint", _effectsTint);
    }

    private void UpdateBottleGlow()
    {
        if (BottleMaterial is null) return;

        var bounce = MathF.Sin(Time.Now * (BeamActive ? 45f : 3f)) * 0.5f;
        BottleMaterial.Set("g_vSelfIllumTint", _effectsTint);
        BottleMaterial.Set("g_flSelfIllumBrightness", (BeamActive ? 6f : 1.5f) + bounce);
    }

    private void UpdateTipSprites()
    {
        var mul = BeamActive ? 1f : 0.6f;

        foreach (var sprite in TipSprites)
        {
            if (!sprite.IsValid()) continue;

            sprite.Enabled = true;
            sprite.Color = _effectsTint.WithAlpha(mul * Random.Shared.Float(0.4f, 0.9f));
            sprite.Size = Random.Shared.Float(6f, 7f) * mul;
        }
    }

    private void UpdateGlowEffect()
    {
        if (GlowEffect is null) return;

        GlowEffect.Tint = _effectsTint;
        GlowEffect.Alpha = BeamActive ? 1f : 0.2f;
    }

    private void UpdateSparks()
    {
        if (SparksEffect is null) return;
        if (BeamActive == _wasActive) return;

        _wasActive = BeamActive;
        var count = BeamActive ? 20 : 3;

        for (var i = 0; i < count; i++)
        {
            var particle = SparksEffect.Emit(SparksEffect.WorldPosition, i / (float)count);
            particle.Velocity = Vector3.Random * 100f + SparksEffect.WorldTransform.Forward * 30f;
        }
    }
}

public sealed class PhygunWorldmodel : Component
{
    [Property] public ParticleEffect GlowEffect { get; set; }
    [Property] public PointLight GlowLight { get; set; }
    [Property] public Color GravTint { get; set; } = new(1f, 0.8f, 0f);
    [Property] public Color PhysTint { get; set; } = new(0f, 0.68333f, 1f);

    private float _tintFrac;

    protected override void OnUpdate()
    {
        var physgun = GameObject.Root.Components.Get<Minimal.Weapons.WeaponPhysgun>(FindMode.EverythingInSelfAndDescendants);
        _tintFrac = MathX.Approach(_tintFrac, physgun?.PullActive == true ? 1f : 0f, Time.Delta * 5f);

        var tint = Color.Lerp(PhysTint, GravTint, PhygunViewmodelSteepEase(_tintFrac));

        if (GlowEffect is not null)
            GlowEffect.Tint = tint;

        if (GlowLight is not null)
            GlowLight.LightColor = tint;
    }

    private static float PhygunViewmodelSteepEase(float value)
    {
        return value < 0.5f
            ? 8f * value * value * value * value
            : 1f - 8f * (1f - value) * (1f - value) * (1f - value) * (1f - value);
    }
}
